using Godot;
using System;
using System.Threading.Tasks;
using Contrapoint.Players;
using Contrapoint.Weapons;

public partial class SpreadChecks : Node3D
{
	private Player _player = null!;
	private int _checks, _failures;
	private const float Dt = 1f / 66;

	public override async void _Ready()
	{
		try { await Run(); }
		catch (Exception error) { _failures++; GD.PushError(error.ToString()); }
		await Task.Delay(150);
		GD.Print($"SPREAD_CHECKS checks={_checks} failures={_failures}");
		GetTree().Quit(_failures == 0 ? 0 : 1);
	}

	private void Check(bool condition, string message)
	{
		_checks++;
		if (condition) return;
		_failures++;
		GD.PushError(message);
	}

	private async Task Stance(bool airborne = false, bool duck = false)
	{
		_player.SetSpawn(new(Basis.Identity, new(0, airborne ? 4 : 0.003f, 0)));
		_player.Respawn();
		for (var i = 0; i < 20; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			_player.Simulate(new(Vector2.Zero, false, duck, false), Dt);
		}
		_player.Velocity = Vector3.Zero;
	}

	private float SampleRms(float radius)
	{
		var random = new RandomNumberGenerator { Seed = 12345 };
		float sum = 0;
		var mean = Vector2.Zero;
		for (var i = 0; i < 512; i++)
		{
			var shot = SourceWeaponSpread.Sample(Transform3D.Identity, radius, random);
			var direction = -shot.Basis.Z;
			var offset = new Vector2(direction.X, direction.Y) / -direction.Z;
			Check(direction.IsFinite() && Mathf.Abs(direction.Length() - 1) < 0.0001f && offset.Length() <= radius + 0.0001f,
				"Spread samples must be normalized and stay inside the cone.");
			sum += offset.LengthSquared();
			mean += offset;
		}
		Check((mean / 512).Length() < radius * 0.08f, "Spread must be centered on aim rather than drifting sideways.");
		return Mathf.Sqrt(sum / 512);
	}

	private async Task Run()
	{
		var floor = new StaticBody3D { Position = new(0, -.5f, 0) };
		floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(200, 1, 200) } });
		AddChild(floor);
		var wall = new StaticBody3D { Position = new(0, 4, -20) };
		wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(60, 60, .2f) } });
		AddChild(wall);
		_player = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		_player.LocalInput = false;
		_player.DebugControls = false;
		AddChild(_player);
		_player.Combat.SetPhysicsProcess(false);
		_player.Recoil.SetPhysicsProcess(false);
		_player.Recoil.SetProcess(false);
		foreach (var weapon in _player.Inventory.Weapons) weapon.SetProcess(false);
		foreach (var weapon in _player.Inventory.Weapons)
		{
			if (weapon is not FirearmView gun) continue;
			await Stance();
			var standing = SourceWeaponSpread.Radius(gun, _player);
			_player.Velocity = Vector3.Right * (gun.MovementSpeed * 0.52f * SourceMovement.Unit);
			var walking = SourceWeaponSpread.Radius(gun, _player);
			_player.Velocity = Vector3.Right * (gun.MovementSpeed * SourceMovement.Unit);
			var running = SourceWeaponSpread.Radius(gun, _player);
			await Stance(airborne: true);
			var airborne = SourceWeaponSpread.Radius(gun, _player);
			await Stance(duck: true);
			var crouched = SourceWeaponSpread.Radius(gun, _player);
			Check(walking > standing && running > walking && airborne > standing && crouched <= standing,
				$"{gun.Kind}: crouch <= stand < walk < run; air > stand");
			Check(SampleRms(airborne) > SampleRms(standing) && SampleRms(running) > SampleRms(standing),
				$"{gun.Kind}: actual bullet distributions must widen with movement/air.");
			GD.Print($"SPREAD {gun.Kind}: stand={standing:F4} walk={walking:F4} run={running:F4} air={airborne:F4}");
		}
		await Stance();
		_player.Inventory.EquipSlot(WeaponSlot.Primary);
		var rifle = (FirearmView)_player.ActiveWeapon!;
		_player.Velocity = Vector3.Right * (rifle.MovementSpeed * SourceMovement.Unit);
		_player.Combat.QueueBullet(rifle, 30);
		var sampledDirection = _player.Combat.LastShotDirection;
		Check(sampledDirection.AngleTo(Vector3.Forward) > 0.00001f, "Queued running shot must include spread.");
		_player.Velocity = Vector3.Zero;
		_player.Combat._PhysicsProcess(Dt);
		var actualDirection = (_player.Combat.LastHitPosition - _player.Camera.GlobalPosition).Normalized();
		Check(actualDirection.Dot(sampledDirection) > 0.99999f, "Physics hit must use the sampled shot, even if stance changes before resolution.");
		foreach (var gun in _player.Inventory.Weapons)
		{
			if (gun is not FirearmView firearm || firearm.Kind is not (FirearmView.FirearmKind.AK47 or FirearmView.FirearmKind.M4A1)) continue;
			_player.Inventory.Equip(firearm);
			firearm._Process(2);
			var baseline = SourceWeaponSpread.Radius(firearm, _player);
			var expected = firearm.Kind == FirearmView.FirearmKind.AK47 ? .00701f : .0055f;
			Check(Mathf.Abs(baseline - expected) < .00001f, "Rested accuracy must match the installed CSSO script");
			var random = new RandomNumberGenerator { Seed = 8721 };
			var sum = Vector2.Zero;
			float squared = 0;
			for (var i = 0; i < 4096; i++)
			{
				var direction = -SourceWeaponSpread.Sample(Transform3D.Identity, firearm, _player, random).Basis.Z;
				var offset = new Vector2(direction.X, direction.Y) / -direction.Z;
				Check(direction.IsFinite() && offset.Length() <= baseline + .00001f, "CSSO samples must stay within their combined cone");
				sum += offset;
				squared += offset.LengthSquared();
			}
			var spread = .0006f;
			var accuracy = expected - spread;
			var expectedRms = Mathf.Sqrt((accuracy * accuracy + spread * spread) / 3);
			Check(sum.Length() / 4096 < baseline * .03f && Mathf.Abs(Mathf.Sqrt(squared / 4096) - expectedRms) < expectedRms * .04f,
				"Actual rifle spread must follow the sum of two centered CSSO polar distributions");
			firearm._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
			firearm._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
			var fired = SourceWeaponSpread.Radius(firearm, _player);
			Check(fired > baseline, "Accepted rifle fire must immediately accumulate an accuracy penalty");
			var shots = firearm.ShotsFired;
			firearm._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
			firearm._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
			Check(firearm.ShotsFired == shots && SourceWeaponSpread.Radius(firearm, _player) == fired, "Rejected fire must not increase spread");
			firearm._Process(.05);
			Check(SourceWeaponSpread.Radius(firearm, _player) < fired, "Accuracy must recover during a pause");
			for (var i = 0; i < 10; i++) firearm.RifleAccuracy.Fired(firearm);
			var sprayed = SourceWeaponSpread.Radius(firearm, _player);
			firearm._Process(.05);
			Check((SourceWeaponSpread.Radius(firearm, _player) - baseline) / (sprayed - baseline) > .7f,
				"Long sprays must retain CSSO's slower final recovery");
			firearm._Process(4);
			Check(Mathf.Abs(SourceWeaponSpread.Radius(firearm, _player) - baseline) < .00001f, "Accuracy must recover to its baseline");
			if (firearm.Kind == FirearmView.FirearmKind.M4A1)
			{
				firearm._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
				firearm._Process(4);
				Check(firearm.Silenced, "M4 fixture must attach the silencer");
				Check(Mathf.Abs(SourceWeaponSpread.Radius(firearm, _player) - .0054f) < .00001f, "Silenced M4 must use CSSO's alternate spread");
				firearm._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
				firearm._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
				Check(Mathf.Abs(SourceWeaponSpread.Radius(firearm, _player) - .0124f) < .00001f, "Silenced M4 firing penalty must be .007");
			}
			firearm.SpreadScale = 0;
			Check(SourceWeaponSpread.Sample(Transform3D.Identity, firearm, _player, new()).Basis.IsEqualApprox(Basis.Identity), "SpreadScale zero must bypass both CSSO samples");
			firearm.SpreadScale = 1;
			_player.Respawn();
			Check(firearm.RifleAccuracy.Penalty == 0 && firearm.RifleAccuracy.Index == 0, "Respawn must clear rifle accuracy and pattern state");
		}
		_player.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}
}
