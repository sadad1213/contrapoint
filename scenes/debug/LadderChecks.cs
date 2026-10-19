using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.Players;

// Real Jolt sweeps plus numeric fixtures from Source's ladder movement.
public partial class LadderChecks : Node3D
{
	private CharacterBody3D _body = null!;
	private SourceMovement _movement = null!;
	private Camera3D _camera = null!;
	private int _checks;
	private const float U = SourceMovement.Unit;
	private float Dt => 1f / Engine.PhysicsTicksPerSecond;
	private static PlayerCommand Forward => new(new(0, -1), false, false, false);

	public override async void _Ready()
	{
		try
		{
			await Run();
			GD.Print($"LADDER_CHECKS_OK checks={_checks}");
			GetTree().Quit();
		}
		catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
	}

	private void Check(bool condition, string message)
	{
		_checks++;
		if (!condition) throw new InvalidOperationException(message);
	}

	private void Near(Vector3 expected, string message, float tolerance = 0.1f)
	{
		var actual = _body.Velocity / U;
		Check(actual.DistanceTo(expected) < tolerance, $"{message}: {actual} vs {expected} u/s");
	}

	private StaticBody3D Box(Vector3 center, Vector3 size)
	{
		var body = new StaticBody3D { Position = center };
		body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size, Margin = .001f } });
		AddChild(body);
		return body;
	}

	private Area3D Ladder(Vector3 center, Vector3 size, float yaw = 0)
	{
		var area = new Area3D { Position = center, Rotation = Vector3.Up * yaw, CollisionLayer = 16, CollisionMask = 0, Monitoring = false };
		area.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size, Margin = .001f } });
		AddChild(area);
		area.AddToGroup("source_ladder");
		return area;
	}

	private void Spawn(Vector3 position, float pitch = 0, float yaw = 0)
	{
		_body.GlobalTransform = new(Basis.FromEuler(new(0, yaw, 0)), position);
		_camera.Rotation = new(Mathf.DegToRad(pitch), 0, 0);
		_movement.Reset();
	}

	private async Task Tick(PlayerCommand command = default, int count = 1)
	{
		for (var i = 0; i < count; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			_movement.Simulate(command, 250, Dt);
		}
	}

	private async Task Run()
	{
		var ladder = Ladder(new(0, 5, 0), new(2, 10, .1f));
		_body = new CharacterBody3D();
		_body.AddChild(new CollisionShape3D { Name = "CollisionShape3D", Shape = new BoxShape3D { Size = new(32 * U, SourceMovement.StandingHeight, 32 * U) } });
		var view = new Node3D { Name = "View" };
		_camera = new Camera3D { Name = "Camera3D" };
		view.AddChild(_camera);
		_body.AddChild(view);
		_movement = new SourceMovement { Name = "Movement" };
		_body.AddChild(_movement);
		AddChild(_body);
		var contact = new Vector3(0, 3, .05f + 16 * U + .02f);

		Spawn(contact);
		await Tick();
		Check(!_movement.IsOnLadder && _body.Velocity.Y < 0, "Idle airborne overlap must not acquire a ladder");
		Spawn(contact);
		await Tick(new(new(0, 1), false, false, false));
		Check(!_movement.IsOnLadder, "Moving away must not acquire a ladder");
		Spawn(contact + Vector3.Back * (2 * U + .01f));
		await Tick(Forward);
		Check(!_movement.IsOnLadder, "Initial acquisition range must be 2 Source units");
		Spawn(contact);
		await Tick(Forward);
		Check(_movement.IsOnLadder, "Forward sweep must acquire the front face");
		Near(new(0, 200, 0), "Level forward climb");
		await Tick();
		Check(_movement.IsOnLadder, "Attached ladder must remain acquired without input");
		Near(Vector3.Zero, "Release input must stop without gravity");
		Spawn(contact, 45);
		await Tick(Forward);
		Near(new(0, 282.8427f, 0), "45 degree upward climb must retain uncapped velocity");
		Spawn(contact, -45);
		await Tick(Forward);
		Near(Vector3.Zero, "45 degree downward view balances forward climb");
		Spawn(contact);
		await Tick(Forward with { Move = new(1, -1) });
		Near(new(40, 200, 0), "CSS incidence damping on W+D");
		Spawn(contact);
		await Tick(Forward);
		await Tick(new(new(1, 0), false, false, false));
		Near(new(200, 0, 0), "Pure lateral movement follows Source's active formula");
		Spawn(contact);
		await Tick(Forward with { Duck = true });
		Near(new(0, 68, 0), "Crouch climb is 34 percent");
		Spawn(contact);
		await Tick(Forward with { Walk = true });
		Near(new(0, 200, 0), "Walk key does not change ladder button speed");
		await Tick(new(Vector2.Zero, true, false, false));
		Check(!_movement.IsOnLadder, "Jump detaches");
		Near(new(0, -800 * Dt, 270), "Ladder jump uses outward impulse and ordinary gravity");
		await Tick(new(Vector2.Zero, true, false, false));
		Check(!_movement.IsOnLadder, "Jump momentum must continue away from the ladder");

		Spawn(contact);
		await Tick(Forward);
		_body.Position += Vector3.Back * (8 * U);
		await Tick();
		Check(_movement.IsOnLadder, "Attached probe reaches 10 units");
		_body.Position += Vector3.Back * (4 * U);
		await Tick();
		Check(!_movement.IsOnLadder, "Ladder releases outside the 10 unit probe");

		Spawn(new(0, 10.01f, 0), -89);
		await Tick(Forward);
		Check(!_movement.IsOnLadder, "The top face is not climbable");
		Spawn(contact);
		var obstruction = Box(new(0, 5, .065f), new(2, 10, .01f));
		await Tick(Forward);
		Check(!_movement.IsOnLadder, "A solid face before the ladder blocks acquisition");
		obstruction.QueueFree();
		await Tick();

		ladder.Rotation = new(0, Mathf.Pi / 4, 0);
		var normal = Basis.FromEuler(ladder.Rotation) * Vector3.Back;
		Spawn(Vector3.Up * 3 + normal * contact.Z, yaw: Mathf.Pi / 4);
		await Tick(Forward);
		Check(_movement.IsOnLadder && _movement.LadderNormal.Dot(normal) > .999f, "Rotated ladders use the real collision normal");
		Near(new(0, 200, 0), "Rotated face climb");
		await Tick(new(Vector2.Zero, true, false, false));
		Near(normal * 270 + Vector3.Down * (800 * Dt), "Rotated face jump");
		ladder.Rotation = Vector3.Zero;
		Spawn(contact, 89, -Mathf.Pi / 2);
		await Tick(Forward with { Move = new(-1, -1) });
		Check(_movement.IsOnLadder && _body.Velocity.Y / U > 399, "Upward view plus side input retains the 400 u/s ladder technique");

		// Walk off a ledge above a ladder facing away from it: CSS checks behind
		// the hull and catches the ladder without requiring forward input into it.
		var ledge = Box(new(0, 9.9f, -2), new(4, .2f, 4));
		Spawn(new(0, 10.003f, -.7f));
		await Tick(count: 5);
		Check(_body.IsOnFloor(), "Start on the ledge above the ladder");
		var caught = false;
		for (var i = 0; i < 60; i++)
		{
			await Tick(new(new(0, 1), false, false, false));
			if (_movement.IsOnLadder) { caught = true; break; }
		}
		Check(caught, "Walking off the upper ledge catches the ladder behind the player");
		await Tick();
		await Tick(new(Vector2.Zero, true, false, false));
		await Tick(count: 10);
		Check(!_movement.IsOnLadder && _body.Position.Z > .8f, "Automatic backwards catch must not pull a dismount back onto the ladder");
		ledge.QueueFree();

		ladder.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		await CheckKzMap();
		_body.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		await CheckAudio();
		GC.Collect();
		GC.WaitForPendingFinalizers();
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
	}

	private async Task CheckKzMap()
	{
		var map = GD.Load<PackedScene>("res://assets/maps/kz_jayverb000/kz_jayverb000.scn").Instantiate<Node3D>();
		AddChild(map);
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		var ladders = GetTree().GetNodesInGroup("source_ladder").OfType<Area3D>().ToArray();
		Check(ladders.Length == 5, "The KZ map has five authored ladders");
		foreach (var area in ladders)
		{
			var normal = area.GetMeta("ladder_normal").AsVector3();
			var center = area.GetMeta("ladder_center").AsVector3();
			var points = ((ConvexPolygonShape3D)area.GetChildren().OfType<CollisionShape3D>().First().Shape).Points;
			var halfDepth = points.Max(p => Mathf.Abs((area.ToGlobal(p) - center).Dot(normal)));
			var offset = halfDepth + 16 * U + .006f;
			var position = center + normal * offset - Vector3.Up * .2f;
			var query = new PhysicsShapeQueryParameters3D
			{
				Shape = _body.GetNode<CollisionShape3D>("CollisionShape3D").Shape,
				Transform = new(Basis.Identity, position + Vector3.Up * (SourceMovement.StandingHeight / 2)),
				CollisionMask = 1, Exclude = new() { _body.GetRid() }
			};
			if (GetWorld3D().DirectSpaceState.IntersectShape(query, 1).Count > 0) normal = -normal;
			position = center + normal * offset - Vector3.Up * .2f;
			Spawn(position);
			_body.LookAt(position - normal);
			await Tick(Forward);
			Check(_movement.IsOnLadder, "Climb the authored KZ ladder " + area.Name);
			Check(_body.Velocity.Y > 190 * U, "KZ ladder climbs at Source speed " + area.Name);
			await Tick(new(Vector2.Zero, true, false, false));
			Check(!_movement.IsOnLadder && _body.Velocity.Dot(normal) > 260 * U, $"Detach from the authored KZ ladder {area.Name}: velocity={_body.Velocity / U} normal={normal} actual={_movement.LadderNormal}");
		}

		// Screenshot location: first ladder at Source (-2112,-1854,326.375),
		// adjacent 80-unit crate at x=-2000..-1920, z=256..336.
		Spawn(new(-53.6448f, 6.51f, 46.52f), 45, Mathf.Pi);
		for (var i = 0; i < 100 && _body.Position.Y < 9.7f; i++) await Tick(Forward);
		Check(_movement.IsOnLadder && _body.Position.Y >= 9.7f, "Climb above the adjacent crate on the screenshot's ladder");
		_body.Rotation = new(0, Mathf.Pi + Mathf.DegToRad(65), 0);
		_camera.Rotation = new(Mathf.DegToRad(30), 0, 0);
		var leftLadder = false;
		var landedOnCrate = false;
		for (var i = 0; i < 100; i++)
		{
			await Tick(Forward);
			leftLadder |= !_movement.IsOnLadder;
			if (_body.IsOnFloor())
			{
				landedOnCrate = _body.Position.Y > 8.5f && _body.Position.X >= -51.21f;
				break;
			}
		}
		Check(leftLadder && landedOnCrate, "The screenshot's ladder-to-crate transition must be traversable with actual map collisions");
		map.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
	}

	private async Task CheckAudio()
	{
		var ladder = Ladder(new(100, 5, 0), new(2, 10, .1f));
		var player = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		player.LocalInput = false;
		player.DebugControls = false;
		AddChild(player);
		player.SetPhysicsProcess(false);
		player.LocalInput = true;
		player.SetMouseCaptured(true);
		player.MovementAudio.Sounds.Enabled = true;
		player.SourceTeleport(new(100, 2, .4764f), 0, 0);
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		player.Simulate(Forward, Dt);
		Check(player.Movement.IsOnLadder && !player.IsOnFloor(), "Audio test climbs vertically without a ground contact");
		Check(player.MovementAudio.Sounds.LastSound.StartsWith("player/footsteps/ladder"), "Vertical climbing plays the stock ladder samples");
		var sounds = player.MovementAudio.Sounds.SoundsPlayed;
		for (var i = 0; i < 27; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			player.Simulate(Forward, Dt);
		}
		Check(player.MovementAudio.Sounds.SoundsPlayed == sounds, "Ladder sound cadence waits 450 ms");
		for (var i = 0; i < 4; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			player.Simulate(Forward, Dt);
		}
		Check(player.MovementAudio.Sounds.SoundsPlayed == sounds + 1, "Moving on a ladder repeats its step sound");
		sounds = player.MovementAudio.Sounds.SoundsPlayed;
		for (var i = 0; i < 35; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			player.Simulate(default, Dt);
		}
		Check(player.MovementAudio.Sounds.SoundsPlayed == sounds, "Standing still on a ladder is silent");
		for (var i = 0; i < 35; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			player.Simulate(Forward with { Duck = true }, Dt);
		}
		Check(player.MovementAudio.Sounds.SoundsPlayed == sounds, "Crouch climbing is silent");
		player.SetMouseCaptured(false);
		player.QueueFree();
		ladder.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
	}
}
