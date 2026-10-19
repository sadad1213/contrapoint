using Godot;
using System;
using System.Threading.Tasks;
using Contrapoint.Players;
using Contrapoint.Weapons;

public partial class GrenadeChecks : Node3D
{
	private int _checks, _failures;
	private Player _player = null!;
	private void Check(bool condition, string label)
	{
		_checks++;
		if (condition) return;
		_failures++;
		GD.PushError(label);
	}
	private static void Press(GrenadeView grenade, bool down) => grenade._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down });
	private static void Advance(GrenadeView grenade, double seconds, double dt = 1.0 / 144)
	{
		while (seconds > 0.0000001)
		{
			var step = Math.Min(seconds, dt);
			grenade._Process(step);
			seconds -= step;
		}
	}
	private async Task Frame()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}
	private StaticBody3D Box(Vector3 position, Vector3 size)
	{
		var box = new StaticBody3D { Position = position };
		box.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
		AddChild(box);
		return box;
	}
	private GrenadeProjectile Launch(GrenadeView grenade)
	{
		Advance(grenade, 1);
		Press(grenade, true);
		Advance(grenade, 1);
		Press(grenade, false);
		Advance(grenade, 0.11);
		grenade._PhysicsProcess(1.0 / 66);
		var projectile = grenade.LastProjectile!;
		projectile.SetPhysicsProcess(false);
		return projectile;
	}
	private void ClearEffects()
	{
		foreach (var child in GetChildren())
			if (child is GrenadeProjectile or GrenadeSmoke or GrenadeBurst or AudioStreamPlayer3D) child.QueueFree();
	}

	public override async void _Ready()
	{
		try { await Run(); }
		catch (Exception error) { _failures++; GD.PushError(error.ToString()); }
		ClearEffects();
		foreach (var child in GetChildren())
			if (child is Player player) player.QueueFree();
		await Frame();
		await Task.Delay(150); // Let the audio mixer release stopped playback voices.
		GD.Print($"GRENADE_CHECKS checks={_checks} failures={_failures}");
		GetTree().Quit(_failures == 0 ? 0 : 1);
	}

	private async Task Run()
	{
		Box(new(0, -0.5f, 0), new(150, 1, 150));
		_player = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		_player.LocalInput = false;
		AddChild(_player);
		_player.SetPhysicsProcess(false);
		_player.MovementAudio.SetPhysicsProcess(false);
		foreach (var weapon in _player.Inventory.Weapons)
		{
			weapon.SetProcess(false);
			weapon.SetPhysicsProcess(false);
		}
		await Frame();
		var inventory = _player.Inventory;
		foreach (var script in new[] { "hegrenade.bounce", "flashbang.bounce", "smokegrenade.bounce", "basegrenade.explode", "flashbang.explode", "basesmokeeffect.sound" })
		{
			var voice = SourceSoundPlayer.PlayWorld(this, Vector3.One, script);
			Check(voice.Playing && voice.Stream.GetLength() > 0 && voice.GlobalPosition == Vector3.One, $"Original grenade WAV must play spatially: {script}");
			if (script == "flashbang.explode") Check(voice.PitchScale >= 0.7f && voice.PitchScale <= 1.3f, "Flashbang pitch must use the CSS script's 70–130 range.");
			if (script == "basegrenade.explode") Check(voice.PitchScale >= 0.8f && voice.PitchScale <= 1.2f, "HE pitch must use the CSS script's 80–120 range.");
			voice.QueueFree();
		}
		var he = (GrenadeView)inventory.GetNode("HEGrenade");
		var flash = (GrenadeView)inventory.GetNode("Flashbang");
		var smoke = (GrenadeView)inventory.GetNode("SmokeGrenade");
		Check(he.Ammo == 1 && flash.Ammo == 2 && smoke.Ammo == 1, "CSS loadout must be 1 HE, 2 flashes, 1 smoke.");
		Check(inventory.HandleInput(new InputEventKey { Pressed = true, PhysicalKeycode = Key.Key4 }) && inventory.ActiveWeapon == he, "4 must select the grenade slot.");
		Check(!inventory.HandleInput(new InputEventKey { Pressed = true, Echo = true, PhysicalKeycode = Key.Key4 }), "Key repeat must not cycle grenades.");
		inventory.EquipSlot(WeaponSlot.Grenade);
		Check(inventory.ActiveWeapon == flash, "Repeated grenade selection must cycle HE/flash/smoke.");
		inventory.EquipSlot(WeaponSlot.Grenade);
		Check(inventory.ActiveWeapon == smoke, "Third grenade slot entry must be smoke.");
		inventory.Equip(he);
		Press(he, true); Press(he, false);
		Advance(he, 0.5);
		Check(!he.PinPulled && he.Throws == 0, "Deploy must gate attacks.");
		Advance(he, 1);
		Press(he, true);
		Advance(he, 1.1);
		Check(he.PinPulled && he.Throws == 0, "Holding must freeze the ready pose without launching.");
		Advance(he, 5);
		Check(he.Throws == 0 && he.Ammo == 1, "Holding the pin must not cook the grenade or consume ammunition.");
		inventory.EquipSlot(WeaponSlot.Melee);
		Advance(he, 2);
		he._PhysicsProcess(1.0 / 66);
		Check(he.Throws == 0, "Holster must cancel a held grenade without a delayed throw.");

		foreach (var fps in new[] { 30, 60, 144 })
		{
			he.ResetAmmo();
			inventory.Equip(he);
			Advance(he, 1, 1.0 / fps);
			he.Sounds.Enabled = true;
			var sounds = he.Sounds.SoundsPlayed;
			Press(he, true); Press(he, false);
			Advance(he, 0.6, 1.0 / fps);
			Check(he.Sounds.SoundsPlayed == sounds && he.PinPulled, $"Pin audio must not play early ({fps} FPS).");
			Advance(he, 0.12, 1.0 / fps);
			Check(he.Sounds.SoundsPlayed == sounds + 1 && he.Sounds.LastSound == "default.pullpin_grenade", $"The authored pin event must play once ({fps} FPS).");
			Advance(he, 0.22, 1.0 / fps);
			he._PhysicsProcess(1.0 / 66);
			var throws = he.Throws;
			Advance(he, 0.12, 1.0 / fps);
			Check(!inventory.EquipSlot(WeaponSlot.Melee), "The committed throw must survive switching before launch.");
			he._PhysicsProcess(1.0 / 66);
			Check(he.Throws == throws + 1 && he.Ammo == 1, $"Early release must wait for pin pull and preserve the unlimited HE supply ({fps} FPS).");
			var projectile = he.LastProjectile!;
			projectile.SetPhysicsProcess(false);
			Check(projectile.Age == 0, "The fuse must start on projectile creation.");
			Advance(he, 1, 1.0 / fps);
			Check(inventory.ActiveWeapon?.Slot == WeaponSlot.Primary && he.CanEquip, "Throw completion must restore a firearm and keep the grenade available.");
			projectile.QueueFree();
			he.Sounds.Enabled = false;
		}

		var velocity = GrenadeProjectile.ThrowVelocity(Basis.Identity, Vector3.Zero);
		Check(Mathf.Abs(velocity.Length() / SourceMovement.Unit - 675) < 0.01f && velocity.Y > 0, "Level throw must travel at 675 u/s with ten degrees of upward compensation.");
		Check((GrenadeProjectile.ThrowVelocity(Basis.Identity, Vector3.Right * 2) - velocity).IsEqualApprox(Vector3.Right * 2.5f), "Throw must inherit 125% of player velocity.");
		he.ResetAmmo(); inventory.Equip(he);
		var flight = Launch(he);
		for (var i = 0; i < 20; i++) flight._PhysicsProcess(1.0 / 66);
		Check(flight.GlobalPosition.Z < -4 && flight.GlobalPosition.Y > 1, "Grenade must move ballistically through free space.");
		flight.QueueFree();
		Advance(he, 1);

		var wall = Box(new(0, 1.5f, -2), new(10, 3, 0.04f));
		await Frame();
		he.ResetAmmo(); inventory.Equip(he);
		var bounced = Launch(he);
		for (var i = 0; i < 20; i++) bounced._PhysicsProcess(1.0 / 66);
		Check(bounced.Bounces > 0 && bounced.GlobalPosition.Z > -2, "Swept hull must bounce off a thin wall without tunnelling.");
		bounced.QueueFree(); wall.QueueFree(); Advance(he, 1);
		await Frame();

		// Detonation fixtures keep the target and occluding wall independent of throwing.
		var target = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		target.LocalInput = false; target.Team = PlayerTeam.CounterTerrorist; target.Position = new(0, 0, -3);
		AddChild(target); target.SetPhysicsProcess(false);
		he.ResetAmmo(); inventory.Equip(he);
		var blast = Launch(he);
		blast.GlobalPosition = new(0, 0.6f, -2);
		blast.Detonate();
		Check(target.Health < 100 && _player.Health < 100, "HE must damage nearby players including its thrower.");
		Advance(he, 1); target.Respawn(); _player.Respawn();
		wall = Box(new(0, 1.5f, -2), new(10, 3, 0.2f));
		await Frame();
		inventory.Equip(he);
		blast = Launch(he); blast.GlobalPosition = new(0, 0.6f, -1); blast.Detonate();
		Check(target.Health == 100, "A solid wall must block HE damage.");
		wall.QueueFree(); Advance(he, 1);
		await Frame();

		_player.Respawn(); inventory.Equip(flash);
		var flashProjectile = Launch(flash);
		flashProjectile.GlobalPosition = new(0, 1.6f, -1.5f);
		flashProjectile.Detonate();
		Check(_player.GrenadeVision.FlashRemaining > 0 && flash.Ammo == 2, "Flash must blind visible players without consuming the unlimited flash supply.");
		var frontal = _player.GrenadeVision.FlashRemaining;
		_player.GrenadeVision.Reset();
		_player.GrenadeVision.Flash(3, -1);
		Check(_player.GrenadeVision.FlashRemaining < frontal, "Looking away must shorten flash duration.");
		Advance(flash, 1);

		_player.Respawn(); inventory.Equip(smoke);
		var smokeProjectile = Launch(smoke);
		smokeProjectile.GlobalPosition = new(0, 30, -1);
		smokeProjectile.Velocity = new(10, 0, 0);
		for (var i = 0; i < 100; i++) smokeProjectile._PhysicsProcess(1.0 / 66);
		Check(!smokeProjectile.Detonated, "Smoke must not bloom while moving after the fuse.");
		smokeProjectile.GlobalPosition = new(0, 0.2f, -1);
		smokeProjectile.Velocity = Vector3.Zero;
		for (var i = 0; i < 200 && !smokeProjectile.Detonated; i++) smokeProjectile._PhysicsProcess(1.0 / 66);
		Check(smokeProjectile.Detonated, "Smoke must bloom once it comes to rest.");
		await Frame();
		var cloud = GetTree().GetFirstNodeInGroup("source_smoke") as GrenadeSmoke;
		Check(cloud != null, "Smoke detonation must create the actual cloud.");
		if (cloud != null)
		{
			cloud.SetProcess(false);
			cloud._Process(6);
			Check(cloud.Density > 0.99f && cloud.EyeDensity(cloud.GlobalPosition) > 0.99f, "Dense smoke must fully obscure its core.");
			cloud._Process(12);
			Check(cloud.Density < 1 && cloud.Density > 0, "Smoke must begin its cosine fade after 17 seconds.");
			cloud._Process(5);
			Check(cloud.IsQueuedForDeletion(), "Smoke must expire after 22 seconds.");
		}
		_player.Respawn();
		Check(he.Ammo == 1 && flash.Ammo == 2 && smoke.Ammo == 1 && _player.GrenadeVision.FlashRemaining == 0, "Respawn must restore grenade counts and clear flash.");
		inventory.Equip(he); Advance(he, 1); Press(he, true); Advance(he, 1);
		_player.TakeDamage(999);
		he._PhysicsProcess(1.0 / 66);
		Check(he.LastProjectile is { } dropped && dropped.InitialVelocity.IsZeroApprox(), "Death with a pulled pin must drop a live grenade.");
	}
}
