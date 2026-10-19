using Godot;
using System;
using System.Threading.Tasks;
using Contrapoint.Players;

public partial class PropMovementChecks : Node3D
{
	private Player _player = null!;
	private int _checks;
	private float Dt => 1f / Engine.PhysicsTicksPerSecond;
	private void Check(bool condition, string message)
	{
		_checks++;
		if (!condition) throw new InvalidOperationException(message);
	}
	private async Task Tick(PlayerCommand command = default, int count = 1)
	{
		for (var i = 0; i < count; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			_player.Simulate(command, Dt);
		}
	}
	private RigidBody3D Prop(Vector3 position, Vector3 size, float mass)
	{
		var prop = new RigidBody3D { Position = position, Mass = mass, GravityScale = 0,
			LockRotation = true, LinearDamp = 0, AngularDamp = 0 };
		prop.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
		AddChild(prop);
		return prop;
	}
	public override async void _Ready()
	{
		try
		{
			await Run();
			GD.Print($"PROP_MOVEMENT_CHECKS_OK checks={_checks}");
			GetTree().Quit();
		}
		catch (Exception error)
		{
			GD.PushError(error.ToString());
			GetTree().Quit(1);
		}
	}
	private async Task Run()
	{
		var floor = new StaticBody3D { Position = new(0, -0.5f, 0) };
		floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(200, 1, 200) } });
		AddChild(floor);
		_player = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		_player.LocalInput = false;
		_player.DebugControls = false;
		AddChild(_player);
		var platform = Prop(new(0, 0.5f, 0), new(30, 1, 30), 1000);
		_player.SetSpawn(new(Basis.Identity, new(0, 1.003f, 0)));
		_player.Respawn();
		await Tick(count: 10);
		Check(_player.IsOnFloor(), "Player must stand on a physical prop.");
		var start = _player.Position;
		platform.LinearVelocity = Vector3.Right * 15;
		await Tick(count: 10);
		Check(Mathf.Abs(_player.Position.X - start.X) < 0.05f,
			$"Moving prop carried idle player by {_player.Position.X - start.X:F3} m.");
		var forward = new PlayerCommand(new(0, -1), false, false, false);
		await Tick(forward, 25);
		Check(_player.Movement.HorizontalSpeed <= 250.1f, "Running on props must retain normal speed.");
		await Tick(forward with { Jump = true });
		Check(!_player.IsOnFloor() && _player.Velocity.Y > 0, "Player must be able to jump off a prop.");
		Check(Mathf.Abs(_player.Velocity.X) < 0.01f && _player.Movement.HorizontalSpeed <= 250.1f,
			$"Jump inherited prop speed: {_player.Velocity}.");
		platform.QueueFree();
		await Tick(count: 2);
		// A heavy rigid prop must block the player, while receiving a push impulse.
		var obstacle = Prop(new(0, 1.5f, -2), new(4, 3, 1), 1000);
		_player.SetSpawn(new(Basis.Identity, new(0, 0.003f, 0)));
		_player.Respawn();
		await Tick(count: 5);
		await Tick(forward, 60);
		Check(_player.Position.Z > -1.15f, "Player passed through a physical prop.");
		Check(obstacle.LinearVelocity.Z < -0.001f, "Player must still push physical props.");
		obstacle.QueueFree();
		await Tick(count: 2);
		// Existing airborne momentum must survive; this fix must not impose a speed cap.
		_player.Position = new(0, 10, 0);
		_player.Movement.Reset();
		_player.Velocity = new(12, 0, 0);
		await Tick();
		Check(Mathf.Abs(_player.Velocity.X - 12) < 0.001f, "Existing airborne momentum was clamped.");
	}
}
