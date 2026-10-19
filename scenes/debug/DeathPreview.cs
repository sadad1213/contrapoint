using Godot;
using System.Threading.Tasks;
using System.Linq;
using Contrapoint.Players;
using Contrapoint.UI;

public partial class DeathPreview : Node3D
{
	private async Task Frames(int count)
	{
		for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
	}
	private async Task Capture(string name)
	{
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		using var image = GetViewport().GetTexture().GetImage();
		image.SavePng("res://.godot/" + name + ".png"); GD.Print("DEATH_PREVIEW " + name);
	}
	public override async void _Ready()
	{
		GetWindow().Size = new(1280, 720);
		var environment = new WorldEnvironment { Environment = new Environment { BackgroundMode = Environment.BGMode.Color,
			BackgroundColor = new(.2f, .22f, .25f), AmbientLightSource = Environment.AmbientSource.Color,
			AmbientLightColor = Colors.White, AmbientLightEnergy = .8f } }; AddChild(environment);
		AddChild(new DirectionalLight3D { RotationDegrees = new(-40, -30, 0), LightEnergy = 1.5f });
		var ground = new StaticBody3D { Position = new(0, -.25f, 0) };
		ground.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(30, .5f, 30) } });
		ground.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new(30, .5f, 30) },
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new(.35f, .37f, .4f) } }); AddChild(ground);
		var victim = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>(); AddChild(victim); victim.SetPhysicsProcess(false);
		var attacker = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>(); attacker.Name = "CSS Player";
		attacker.LocalInput = false; attacker.Team = PlayerTeam.CounterTerrorist; attacker.Position = new(0, 0, 4); AddChild(attacker);
		await Frames(2);
		for (var i = 0; i < 12; i++) { victim.Simulate(default, 1f / 66); attacker.Simulate(default, 1f / 66); await Frames(1); }
		SourceDeathRules.AutoRespawn = false;
		victim.Velocity = Vector3.Right * 2;
		victim.TakeTraceDamage(36, attacker, SourceHitGroup.Head, victim.Body.Hitboxes.GroupCenter(SourceHitGroup.Head), Vector3.Forward, 1.55f, impulse: 2400 * SourceMovement.Unit);
		var screen = victim.Death.GetChildren().OfType<DeathScreen>().First(); screen.Visible = false;
		var camera = new Camera3D { Position = new(4, 2.4f, 5), Fov = 65 }; AddChild(camera); camera.LookAt(new(.4f, .9f, 0)); camera.MakeCurrent();
		await Frames(6); await Capture("death_side_0100");
		await Frames(14); await Capture("death_side_0300");
		await Frames(32); await Capture("death_side_0800");
		victim.Death.Camera.MakeCurrent(); screen.Visible = true;
		await Frames(65);
		GD.Print($"DEATH_LAYOUT panel={screen.Panel.Position} global={screen.Panel.GlobalPosition} size={screen.Panel.Size} canvas={screen.Transform} stretch={GetViewport().GetStretchTransform()} window={GetWindow().Size}");
		await Capture("death_freezecam");
		ConsoleCommands.Execute("jpeg", victim.Movement, (line, error) => { GD.Print(line); if (error) GD.PushError(line); });
		await Frames(3);
		victim.Respawn(); await Frames(2); await Capture("death_respawn");
		foreach (var child in GetChildren()) child.QueueFree();
		await Frames(2); await Task.Delay(150);
		GetTree().Quit();
	}
}
