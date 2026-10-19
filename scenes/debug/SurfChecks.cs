using Godot;
using System;
using System.Threading.Tasks;
using Contrapoint.Players;

// Actual swept box collisions against Jolt ramps; no imported maps are required.
public partial class SurfChecks : Node3D
{
	private CharacterBody3D _body = null!;
	private SourceMovement _movement = null!;
	private int _checks;
	private int _landings;
	private int _jumps;
	private float Dt => 1f / Engine.PhysicsTicksPerSecond;
	private const float U = SourceMovement.Unit;
	private const float HalfWidth = 16 * U;

	public override async void _Ready()
	{
		try
		{
			await Run();
			GD.Print($"SURF_CHECKS_OK checks={_checks}");
			GetTree().Quit();
		}
		catch (Exception error)
		{
			GD.PushError(error.ToString());
			GetTree().Quit(1);
		}
	}

	private void Check(bool condition, string message)
	{
		_checks++;
		if (!condition) throw new InvalidOperationException(message);
	}

	private StaticBody3D Ramp(float degrees, float length = 200, bool triangles = false)
	{
		var angle = Mathf.DegToRad(degrees);
		var normal = new Vector3(-Mathf.Sin(angle), Mathf.Cos(angle), 0);
		var ramp = new StaticBody3D { Rotation = new(0, 0, angle) };
		Shape3D shape;
		if (triangles)
		{
			var a = new Vector3(-100, 0, -length / 2);
			var b = new Vector3(100, 0, -length / 2);
			var c = new Vector3(100, 0, length / 2);
			var d = new Vector3(-100, 0, length / 2);
			shape = new ConcavePolygonShape3D { Data = [a, b, c, a, c, d] };
		}
		else
		{
			ramp.Position = -normal * 0.5f;
			shape = new BoxShape3D { Size = new(200, 1, length) };
		}
		ramp.AddChild(new CollisionShape3D { Shape = shape });
		AddChild(ramp);
		return ramp;
	}

	private void Spawn(float degrees, Vector3 velocity, float z = 0)
	{
		_body.GlobalTransform = new(Basis.Identity,
			new(0, HalfWidth * Mathf.Abs(Mathf.Tan(Mathf.DegToRad(degrees))) + 0.003f, z));
		_movement.Reset();
		_body.Velocity = velocity * U;
		_landings = _jumps = 0;
	}

	private async Task Tick(PlayerCommand command = default, int count = 1)
	{
		for (var i = 0; i < count; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			_movement.Simulate(command, 250, Dt);
			Check(_body.Position.IsFinite() && _body.Velocity.IsFinite(), "Surf must remain finite.");
		}
	}

	private async Task Remove(Node node)
	{
		node.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
	}

	private async Task Run()
	{
		_body = new CharacterBody3D();
		_body.AddChild(new CollisionShape3D { Name = "CollisionShape3D",
			Shape = new BoxShape3D { Size = new(32 * U, SourceMovement.StandingHeight, 32 * U) } });
		var view = new Node3D { Name = "View" };
		view.AddChild(new Camera3D { Name = "Camera3D" });
		_body.AddChild(view);
		_movement = new SourceMovement { Name = "Movement" };
		_body.AddChild(_movement);
		AddChild(_body);
		_movement.Landed += _ => _landings++;
		_movement.Jumped += () => _jumps++;

		// Numeric Source result: a vertical impact is projected onto the plane,
		// while longitudinal velocity stays untouched. Both gravity halves matter.
		foreach (var triangles in new[] { false, true })
		foreach (var degrees in new[] { -60f, 46f, 60f, 75f })
		foreach (var yaw in new[] { 0f, 45f })
		{
			var ramp = Ramp(degrees, triangles: triangles);
			Spawn(degrees, new(0, -400, -600));
			_body.Rotation = Vector3.Up * Mathf.DegToRad(yaw);
			var normal = new Vector3(-Mathf.Sin(Mathf.DegToRad(degrees)), Mathf.Cos(Mathf.DegToRad(degrees)), 0);
			var incoming = _body.Velocity + Vector3.Down * (800 * U * Dt / 2);
			var expected = incoming - normal * incoming.Dot(normal) + Vector3.Down * (800 * U * Dt / 2);
			await Tick();
			Check(_body.Velocity.DistanceTo(expected) < 0.03f,
				$"{degrees} degree ramp (triangles={triangles}, yaw={yaw}) must clip like Source: {_body.Velocity} vs {expected}.");
			Check(!_body.IsOnFloor(), "A steep ramp must remain airborne.");
			var initialSpeed = new Vector2(_body.Velocity.X, _body.Velocity.Z).Length();
			await Tick(count: 35);
			Check(!_body.IsOnFloor() && _landings == 0, "Surf must not trigger landing or ground friction.");
			Check(Mathf.Abs(_body.Velocity.Z / U + 600) < 0.05f, "Surf must preserve speed along the ramp.");
			Check(new Vector2(_body.Velocity.X, _body.Velocity.Z).Length() > initialSpeed + U * 20,
				"Gravity must accelerate the player downhill.");
			var support = normal.Dot(_body.Position) - Mathf.Abs(normal.X) * HalfWidth;
			Check(Mathf.Abs(support) < 0.015f, $"Surf must follow the ramp without sticking or sinking, gap={support}.");
			await Remove(ramp);
		}

		// Exact Source walkable cutoff, including slopes within Godot's default
		// extra 0.01-radian floor tolerance.
		foreach (var normalY in new[] { 0.699f, 0.701f })
		{
			var degrees = Mathf.RadToDeg(Mathf.Acos(normalY));
			var ramp = Ramp(degrees);
			Spawn(degrees, new(0, -100, 0));
			await Tick(count: 4);
			Check(_body.IsOnFloor() == (normalY >= 0.7f), $"Walkable cutoff must be normal.Y >= 0.7, got {normalY}.");
			await Remove(ramp);
		}

		var longRamp = Ramp(60);
		Spawn(60, new(-200, -350, -900));
		await Tick(count: 3);
		await Tick(new(Vector2.Zero, true, false, false), 3);
		Check(_jumps == 0 && !_body.IsOnFloor(), "Space must not jump off an unwalkable surf ramp.");
		// A/D presses into the ramp and changes the uphill/downhill trajectory.
		Spawn(60, new(-200, -350, -900));
		await Tick(count: 10);
		var passive = _body.Velocity;
		await Tick();
		var passiveX = _body.Velocity.X;
		Spawn(60, new(-200, -350, -900));
		await Tick(count: 10);
		Check(_body.Velocity.DistanceTo(passive) < 0.001f, "Repeated surf entry must be deterministic.");
		await Tick(new(new(1, 0), false, false, false));
		Check(_body.Velocity.X > passiveX + 0.01f, $"Strafing into a ramp must provide air control: passive={passiveX}, active={_body.Velocity}, entry={passive}.");
		await Remove(longRamp);

		// Ride upward and off the end: the ramp must not cancel launch velocity.
		var shortRamp = Ramp(60, 3);
		Spawn(60, new(180, 180 * Mathf.Tan(Mathf.DegToRad(60)), -800), 0);
		await Tick(count: 8);
		Check(_body.Position.Z < -2 && _body.Velocity.Y > 150 * U && !_body.IsOnFloor(),
			$"Ramp exit must preserve upward momentum, got {_body.Position}, {_body.Velocity}.");
		var exitVelocity = _body.Velocity;
		await Tick(count: 3);
		Check(Mathf.Abs(_body.Velocity.X - exitVelocity.X) < 0.001f
			&& Mathf.Abs(_body.Velocity.Z - exitVelocity.Z) < 0.001f,
			"Air after surf exit must preserve horizontal momentum above weapon speed.");
		await Remove(shortRamp);

		// Airborne wall/ceiling/corner contacts use the same solver as ramps.
		var wallX = Box(new(2, 10, 0), new(1, 30, 30));
		var wallZ = Box(new(0, 10, -2), new(30, 30, 1));
		_body.Position = new(0, 10, 0);
		_movement.Reset();
		_body.Velocity = new Vector3(900, -100, -900) * U;
		await Tick(count: 10);
		Check(_body.Position.X < 1.1f && _body.Position.Z > -1.1f
			&& Mathf.Abs(_body.Velocity.X) < 0.001f && Mathf.Abs(_body.Velocity.Z) < 0.001f
			&& _body.Velocity.Y < -100 * U,
			$"Two wall planes must block horizontal movement and allow falling: {_body.Position}, {_body.Velocity}.");
		await Remove(wallX);
		await Remove(wallZ);
		var ceiling = Box(new(0, 13, 0), new(30, 1, 30));
		_body.Position = new(0, 10, 0);
		_movement.Reset();
		_body.Velocity = new Vector3(500, 600, 0) * U;
		await Tick(count: 6);
		Check(_body.Velocity.Y < 0 && Mathf.Abs(_body.Velocity.X / U - 500) < 0.01f,
			"A ceiling must stop ascent while preserving tangential momentum.");
		await Remove(ceiling);

		var floor = Ramp(0);
		_body.Position = new(0, 3, 0);
		_movement.Reset();
		_landings = 0;
		await Tick(count: 60);
		Check(_body.IsOnFloor() && _body.Velocity.Y == 0 && _landings == 1,
			"Air solver must hand off to flat ground with exactly one landing.");
		await Remove(floor);

		await CheckKzRamp();
	}

	private async Task CheckKzRamp()
	{
		var map = GD.Load<PackedScene>("res://assets/maps/kz_jayverb000/kz_jayverb000.scn").Instantiate<Node3D>();
		var world = map.GetNode<Node3D>("WorldBrushes");
		map.RemoveChild(world);
		map.Free();
		AddChild(world);
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		// The long surf face on world brush 273, in the map's original coordinates.
		var normal = new Vector3(-1.125f, 1, 0).Normalized();
		var center = new Vector3(-1.6256f, 11.176f, 35.7632f);
		foreach (var gap in new[] { .003f, -.001f, -.01f, -.05f, -.2f })
		{
			_movement.Reset();
			_movement.ToggleNoclip();
			await Tick(new(new(0, -1), false, false, false), 2);
			_body.GlobalTransform = new(Basis.Identity, center + Vector3.Up * (HalfWidth * 1.125f + gap));
			_movement.ToggleNoclip();
			for (var i = 0; i < 12; i++)
			{
				await Tick(new(new(1, 0), false, false, false));
				GD.Print($"KZ_SURF entry={gap} tick={i} pos={_body.Position} vel={_body.Velocity / U} floor={_body.IsOnFloor()} gap={normal.Dot(_body.Position - center) - Mathf.Abs(normal.X) * HalfWidth}");
			}
		}
		await Remove(world);
	}

	private StaticBody3D Box(Vector3 position, Vector3 size)
	{
		var body = new StaticBody3D { Position = position };
		body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
		AddChild(body);
		return body;
	}
}
