using Godot;
using System;
using System.Threading.Tasks;
using Contrapoint.Players;
using Contrapoint.Weapons;
using Kind = Contrapoint.Weapons.FirearmView.FirearmKind;

public partial class RecoilChecks : Node3D
{
	private Player _player = null!;
	private int _checks, _failures;
	private const float Dt = 1f / 66;
	private SourceRecoil Recoil => _player.Recoil;

	public override async void _Ready()
	{
		try { await Run(); }
		catch (Exception error) { _failures++; GD.PushError(error.ToString()); }
		await Task.Delay(150);
		GD.Print($"RECOIL_CHECKS checks={_checks} failures={_failures}");
		GetTree().Quit(_failures == 0 ? 0 : 1);
	}

	private void Check(bool condition, string message)
	{
		_checks++;
		if (condition) return;
		_failures++;
		GD.PushError(message);
	}

	private void Near(float actual, float expected, string message) => Check(Mathf.Abs(actual - expected) < 0.0001f, $"{message}: {actual} vs {expected}");

	private async Task Tick(PlayerCommand command = default, int count = 1)
	{
		for (var i = 0; i < count; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			_player.Simulate(command, Dt);
		}
	}

	private async Task Stance(string stance)
	{
		_player.SetSpawn(new(Basis.Identity, new(0, stance.Contains("air") ? 4 : 0.003f, 0)));
		_player.Respawn();
		await Tick(new(Vector2.Zero, false, stance == "duck", false), 20);
		_player.Velocity = stance.Contains("move") ? new(10 * SourceMovement.Unit, 0, 0) : Vector3.Zero;
		Recoil.Reset();
	}

	private static void Trigger(FirearmView gun, bool held) => gun._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = held });

	private async Task Run()
	{
		// MD5/CRC32 reference fixtures for Source's shared random integer stream.
		foreach (var sample in new[] { (0, 7, 4), (1, 0, 3), (2, 4, 4), (66, 8, 5), (100, 4, 1), (999, 6, 3), (10000, 1, 4) })
		{
			Check(SourceRecoilRandom.KickBack(sample.Item1, 8) == sample.Item2, "Source shared random must match the reference stream (0..8)");
			Check(SourceRecoilRandom.KickBack(sample.Item1, 5) == sample.Item3, "Source shared random must match the reference stream (0..5)");
		}
		var floor = new StaticBody3D { Position = new(0, -0.5f, 0) };
		floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(100, 1, 100) } });
		AddChild(floor);
		var wall = new StaticBody3D { Position = new(0, 2, -10) };
		wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(10, 10, 0.2f) } });
		AddChild(wall);
		_player = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		_player.LocalInput = false;
		_player.DebugControls = false;
		AddChild(_player);
		Recoil.SetPhysicsProcess(false);
		_player.Combat.SetPhysicsProcess(false);
		foreach (var weapon in _player.Inventory.Weapons)
		{
			weapon.SetProcess(false);
			if (weapon is FirearmView firearm) firearm.SpreadScale = 0; // Isolate recoil directions.
		}
		// Independent reference fixtures: CSSO seeds 223/38965, first suppressed impulse.
		foreach (var sample in new[] {
			(Kind.AK47, false, new Vector2(-19.988424f, -10.330195f), new Vector2(-.2928744f, -.1513601f)),
			(Kind.M4A1, false, new Vector2(-20.009906f, 4.050739f), new Vector2(-.2931892f, .05935225f)),
			(Kind.M4A1, true, new Vector2(-15.436870f, 3.124989f), new Vector2(-.2261841f, .04578797f)) })
		{
			await Stance("stand");
			Recoil.AddShot(sample.Item1, sample.Item2);
			Check(Recoil.PunchDegrees.IsZeroApprox(), "CSSO applies an aim velocity impulse after the shot");
			Check(Recoil.ViewPunchDegrees.DistanceTo(sample.Item3 * .055f) < .0001f, "Immediate view kick must use CSSO's .055 scale");
			var camera = _player.Camera.Rotation;
			Recoil._Process(1.0 / 30);
			Check(_player.Camera.Rotation.IsEqualApprox(camera), "Rendering must not add a second camera follow/easing layer");
			Recoil._PhysicsProcess(Dt);
			Check(Recoil.PunchDegrees.DistanceTo(sample.Item4) < .0001f, "Aim velocity must integrate using CSSO decay");
			var firstTick = Recoil.PunchDegrees;
			Recoil._PhysicsProcess(4);
			Check(Recoil.PunchDegrees.IsZeroApprox() && Recoil.ViewPunchDegrees.IsZeroApprox(), "CSSO recoil must settle fully");
			Recoil.Reset();
			Recoil.AddShot(sample.Item1, sample.Item2);
			Recoil._PhysicsProcess(Dt);
			Check(Recoil.PunchDegrees.IsEqualApprox(firstTick), "A reset spray must reproduce the same pattern");
		}
		foreach (var sample in new[] {
			(Kind.AK47, false, 7, new Vector2(-5.007084f, -1.192303f)),
			(Kind.M4A1, false, 6, new Vector2(-5.439243f, -.029574f)),
			(Kind.M4A1, true, 6, new Vector2(-4.066064f, -.038270f)) })
		{
			Recoil.Reset();
			for (var shot = 0; shot < 30; shot++)
			{
				Recoil.AddShot(sample.Item1, sample.Item2, shot);
				for (var tick = 0; tick < sample.Item3; tick++) Recoil._PhysicsProcess(Dt);
			}
			Check(Recoil.PunchDegrees.DistanceTo(sample.Item4) < .0003f, "Full spray must match the CSSO reference integration");
			Recoil._PhysicsProcess(4);
			Check(Recoil.PunchDegrees.IsZeroApprox() && _player.Camera.Rotation.IsZeroApprox(), "A full spray must recover without residual camera motion");
		}
		foreach (var kind in new[] { Kind.Deagle, Kind.USP, Kind.AWP, Kind.Scout })
		{
			Recoil.Reset();
			Recoil.AddShot(kind);
			Near(Recoil.PunchDegrees.X, -2, $"{kind} must use a two-degree impulse");
			Near(Recoil.PunchDegrees.Y, 0, $"{kind} must not invent lateral shake");
		}
		Recoil._PhysicsProcess(Dt);
		Near(Recoil.PunchDegrees.X, -(2 - 11 * Dt), "Decay must follow len -= (10 + len*.5)*dt");
		Recoil._PhysicsProcess(1);
		Check(Recoil.PunchDegrees == Vector2.Zero && _player.Camera.Rotation.IsZeroApprox(), "Recoil must settle exactly without overshoot or drift");
		await Stance("stand");
		_player.Inventory.EquipSlot(WeaponSlot.Primary);
		var gun = (FirearmView)_player.ActiveWeapon!;
		gun._Process(2);
		Recoil.Reset();
		var eye = _player.Camera.GlobalPosition;
		Trigger(gun, true);
		_player.Combat._PhysicsProcess(Dt);
		Near(_player.Combat.LastHitPosition.Y, eye.Y, "First bullet must be fired before adding its new kick");
		Recoil._PhysicsProcess(.1);
		var before = Recoil.PunchDegrees;
		var expectedDirection = -Basis.FromEuler(new Vector3(-Mathf.DegToRad(before.X * 2), Mathf.DegToRad(before.Y * 2), 0)).Z;
		var expectedHit = eye + expectedDirection * (9.9f / -expectedDirection.Z);
		gun._Process(.1);
		_player.Combat._PhysicsProcess(Dt);
		Check(_player.Combat.LastHitPosition.DistanceTo(expectedHit) < .001f, "Next bullet must use 2x prior punch, independent of the new camera kick");
		var shots = gun.ShotsFired;
		var punch = Recoil.PunchDegrees;
		Trigger(gun, false);
		Trigger(gun, true);
		Trigger(gun, false);
		Check(gun.ShotsFired == shots && Recoil.PunchDegrees == punch, "Rejected fire must not add recoil");
		Recoil._PhysicsProcess(Dt);
		Check(Recoil.ShotsInBurst == 2, "Automatic rifle must retain burst count after release");
		Recoil._PhysicsProcess(.39);
		Check(Recoil.ShotsInBurst == 2, "Burst recovery must wait 0.4 seconds");
		Recoil._PhysicsProcess(.02);
		Check(Recoil.ShotsInBurst == 1, "Burst recovery must decrement after its Source delay");
		Recoil._PhysicsProcess(.023);
		Check(Recoil.ShotsInBurst == 0, "Subsequent recovery must use the 0.0225-second interval");
		Recoil.Reset();
		Recoil.AddShot(Kind.AK47);
		punch = Recoil.PunchDegrees;
		_player.Inventory.EquipSlot(WeaponSlot.Melee);
		Check(Recoil.ShotsInBurst == 0 && Recoil.PunchDegrees == punch, "Deploy must reset the burst without snapping away residual recoil");
		var knife = (KnifeView)_player.ActiveWeapon!;
		knife._Process(2);
		knife._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
		Check(Recoil.PunchDegrees == punch, "Knife swings must not add firearm recoil");
		// Compensating with the mouse must affect the base aim even as punch returns.
		_player.LocalInput = true;
		_player.SetMouseCaptured(true);
		Recoil.Reset();
		Recoil.AddShot(Kind.USP);
		var cameraPitch = _player.Camera.Rotation.X;
		_player._Input(new InputEventMouseMotion { ScreenRelative = new(0, 10) });
		Near(_player.Camera.Rotation.X - cameraPitch, -10 * _player.MouseSensitivity, "Mouse compensation must stay responsive during recoil");
		Near(Recoil.PunchDegrees.X, -2, "Mouse motion must not erase the recoil state");
		Recoil._PhysicsProcess(1);
		Near(_player.Camera.Rotation.X, _player.AimPitch, "Recovery must return to the compensated aim");
		foreach (var kind in new[] { Kind.AK47, Kind.M4A1 })
		{
			Recoil.Reset();
			Recoil.AddShot(kind);
			Recoil._PhysicsProcess(Dt);
			cameraPitch = _player.Camera.Rotation.X;
			var bulletBefore = Recoil.GetBulletAim();
			_player._Input(new InputEventMouseMotion { ScreenRelative = new(0, 10) });
			Near(_player.Camera.Rotation.X - cameraPitch, -10 * _player.MouseSensitivity, "Rifle mouse input must apply immediately");
			Check(!Recoil.GetBulletAim().Basis.IsEqualApprox(bulletBefore.Basis), "Mouse compensation must immediately change ballistic aim");
			Recoil._PhysicsProcess(4);
			Near(_player.Camera.Rotation.X, _player.AimPitch, "Rifle recovery must preserve mouse compensation");
		}
		_player.Respawn();
		Check(Recoil.PunchDegrees == Vector2.Zero && Recoil.ShotsInBurst == 0 && _player.Camera.Rotation.IsZeroApprox(), "Respawn must clear camera and burst recoil");
		_player.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}
}
