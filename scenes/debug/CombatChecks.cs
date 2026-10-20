using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.Players;
using Contrapoint.Weapons;

public partial class CombatChecks : Node3D
{
	private int _checks, _failures;
	private Player _player = null!;
	private WeaponCombat Combat => _player.Combat;

	public override async void _Ready()
	{
		try { await Run(); }
		catch (Exception error) { _failures++; GD.PushError(error.ToString()); }
		await Task.Delay(150);
		GD.Print($"COMBAT_CHECKS checks={_checks} failures={_failures}");
		GetTree().Quit(_failures == 0 ? 0 : 1);
	}

	private void Check(bool condition, string message)
	{
		_checks++;
		if (condition) return;
		_failures++;
		GD.PushError(message);
	}

	private StaticBody3D Box(Vector3 at, Vector3 size, string surface = "concrete")
	{
		var body = new StaticBody3D { Position = at };
		body.SetMeta("source_surface", surface);
		body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
		AddChild(body);
		return body;
	}

	private async Task Frame()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
	}

	private RigidBody3D PhysicalProp(Vector3 at, float mass)
	{
		var prop = new RigidBody3D();
		prop.SetScript(GD.Load<Script>("res://assets/maps/source_prop.gd"));
		prop.Position = at;
		prop.Mass = mass;
		prop.GravityScale = 0;
		prop.LinearDamp = 0;
		prop.AngularDamp = 0;
		prop.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = Vector3.One } });
		AddChild(prop);
		return prop;
	}

	private void Advance(WeaponView weapon, double seconds)
	{
		while (seconds > 0.000001)
		{
			var dt = Math.Min(seconds, 1.0 / 144);
			weapon._Process(dt);
			seconds -= dt;
		}
	}

	private static void Press(WeaponView weapon, MouseButton button, bool keepCameraAngle = false)
	{
		// These fixtures isolate collision/effects at a fixed aim; recoil has
		// its own checks against Source coefficients and shot directions.
		if (weapon is FirearmView && weapon.Wielder is { } owner)
		{
			var angle = owner.Camera.Rotation;
			owner.Recoil.Reset();
			if (keepCameraAngle) owner.Camera.Rotation = angle;
		}
		weapon._UnhandledInput(new InputEventMouseButton { ButtonIndex = button, Pressed = true });
		weapon._UnhandledInput(new InputEventMouseButton { ButtonIndex = button, Pressed = false });
	}

	private async Task Run()
	{
		Box(new(0, -0.5f, 0), new(100, 1, 100));
		var wall = Box(new(0, 1.6f, -8.4f), new(4, 4, 1)); // Thick concrete stops CSS penetration.
		_player = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		_player.LocalInput = false;
		AddChild(_player);
		Combat.SetPhysicsProcess(false);
		foreach (var weapon in _player.Inventory.Weapons)
		{
			weapon.SetProcess(false);
			if (weapon is FirearmView gun) gun.SpreadScale = 0; // Isolate collision fixtures.
		}
		await Frame();
		_player.Inventory.EquipSlot(WeaponSlot.Primary);
		var rifle = (FirearmView)_player.ActiveWeapon!;
		Advance(rifle, 2);
		Press(rifle, MouseButton.Left);
		Check(Combat.BulletsResolved == 0, "Shots must wait for a safe physics query.");
		Combat._PhysicsProcess(1.0 / 66);
		Check(Combat.BulletsResolved == 1 && Combat.MarksCreated == 1 && Combat.TracersCreated == 1,
			"One accepted shot must produce one hit/mark/tracer.");
		Check(Mathf.Abs(Combat.LastHitPosition.Z + 7.9f) < 0.005f, "Bullet must stop at the nearest wall.");
		var muzzleTracer = Combat.GetNode<Node3D>("WorldEffects").GetChildren().OfType<ShotTracer>().Last();
		Check(_player.Body.TryGetMuzzlePosition(out var muzzle) && muzzleTracer.StartPosition.DistanceTo(muzzle) < .001f,
			"A remote player's resolved tracer must start at the world weapon muzzle.");
		Combat.PresentNetworkImpact(new Transform3D(Basis.Identity, _player.Camera.GlobalPosition + Vector3.Up * 5), Combat.LastHitPosition, Vector3.Zero, null, false, false);
		muzzleTracer = Combat.GetNode<Node3D>("WorldEffects").GetChildren().OfType<ShotTracer>().Last();
		Check(muzzleTracer.StartPosition.DistanceTo(muzzle) < .001f, "A received shot effect must use the displayed weapon muzzle, independent of the eye-ray origin.");
		Check(Combat.LastHitNormal.Dot(Vector3.Back) > 0.99f, "Hole must align with the surface normal.");
		var mark = wall.GetChild<ImpactMark>(1);
		Check(mark.GlobalPosition.DistanceTo(Combat.LastHitPosition) < 0.003f, "Hole must sit on the surface without z-fighting.");
		var original = mark.GlobalPosition;
		wall.Position += Vector3.Right * 0.2f;
		Check(Mathf.Abs(mark.GlobalPosition.X - original.X - 0.2f) < 0.001f, "Moving surfaces must carry their marks.");
		wall.Position -= Vector3.Right * 0.2f;
		Press(rifle, MouseButton.Left);
		Combat._PhysicsProcess(0.02);
		Check(Combat.BulletsResolved == 1, "Rejected shots must not spawn bullets or effects.");
		_player.Inventory.EquipSlot(WeaponSlot.Melee);
		Check(GodotObject.IsInstanceValid(mark), "Weapon switching must preserve previous marks.");
		var knife = (KnifeView)_player.ActiveWeapon!;
		((BoxShape3D)wall.GetChild<CollisionShape3D>(0).Shape).Size = new(4, 4, .2f);
		knife.Sounds.Enabled = true;
		Advance(knife, 2);
		wall.Position = new(0, 1.6f, -1.05f);
		await Frame();
		var soundCount = knife.Sounds.SoundsPlayed;
		Press(knife, MouseButton.Left);
		Combat._PhysicsProcess(0.05);
		Check(Combat.MeleeResolved == 1 && Combat.MarksCreated == 1, "Knife damage traces immediately, but its decal waits for the smack delay.");
		Combat._PhysicsProcess(0.06);
		Check(Combat.MeleeResolved == 1 && Combat.MarksCreated == 2, "Slash within 48 units must scratch the wall.");
		Check(knife.Sounds.LastSound == "weapon_knife.hitwall" && knife.Sounds.SoundsPlayed == soundCount + 1,
			"Wall contact plays its delayed hit sound.");
		wall.Position = new(0, 1.6f, -1.4f); await Frame();
		Advance(knife, 1.1);
		Press(knife, MouseButton.Right);
		Combat._PhysicsProcess(0.21);
		Check(Combat.MeleeResolved == 2 && Combat.MarksCreated == 2, "Stab must miss beyond its 32-unit sweep plus the Source knife hull.");
		wall.Position = new(0, 1.6f, -0.8f);
		await Frame();
		Advance(knife, 1.1);
		Press(knife, MouseButton.Right);
		Combat._PhysicsProcess(0.21);
		Check(Combat.MarksCreated == 3 && knife.Sounds.LastSound == "weapon_knife.hitwall", "Close stab must leave a mark and hit sound.");
		// A live target receives damage without a wall decal on its body.
		wall.Position = new(0, 1.6f, -5);
		((BoxShape3D)wall.GetChild<CollisionShape3D>(0).Shape).Size = new(4, 4, 1);
		var target = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		target.LocalInput = false;
		target.Team = PlayerTeam.CounterTerrorist;
		target.Position = new(0, 0, -2);
		AddChild(target);
		await Frame();
		_player.Inventory.EquipSlot(WeaponSlot.Primary);
		rifle = (FirearmView)_player.ActiveWeapon!;
		Advance(rifle, 2);
		_player.Camera.LookAt(target.Body.Hitboxes.GroupCenter(SourceHitGroup.Chest));
		Press(rifle, MouseButton.Left, keepCameraAngle: true);
		Combat._PhysicsProcess(0.02);
		Check(target.Health < 100 && target.LastDamageKind == PlayerDamageKind.Bullet
			&& !target.GetChildren().OfType<ImpactMark>().Any(),
			$"Bullet damages an enemy through stock animated hitboxes without a wall hole on the player: HP={target.Health}, marks={Combat.MarksCreated}.");
		target.GlobalPosition = new(0, 0, -7);
		await Frame();
		var blockedHealth = target.Health;
		var blockedMarks = Combat.MarksCreated;
		Advance(rifle, 0.2);
		Press(rifle, MouseButton.Left);
		Combat._PhysicsProcess(0.02);
		Check(target.Health == blockedHealth && Combat.MarksCreated == blockedMarks + 1,
			"A solid wall must stop the bullet before a target behind it.");
		target.QueueFree();
		await Frame();
		// Marks have a strict cap even when many shots are fired in one frame.
		Combat.MaxMarks = 3;
		for (var i = 0; i < 8; i++)
		{
			Advance(rifle, 0.2);
			Press(rifle, MouseButton.Left);
			Combat._PhysicsProcess(0.02);
		}
		await Frame();
		Check(wall.GetChildCount() <= 4, "Impact mark count must remain capped (plus the collision shape).");
		foreach (var child in wall.GetChildren()) if (child is ImpactMark decal) decal._Process(100);
		await Frame();
		Check(wall.GetChildCount() == 1, "Expired marks must be removed.");
		// A miss still has a tracer but never invents a surface hit.
		wall.Position = new(20, 1.6f, -5);
		await Frame();
		var marks = Combat.MarksCreated;
		var tracers = Combat.TracersCreated;
		Advance(rifle, 0.2);
		Press(rifle, MouseButton.Left);
		Combat._PhysicsProcess(0.02);
		Check(Combat.MarksCreated == marks && Combat.TracersCreated == tracers + 1, "Misses must create only a tracer.");
		Combat.QueueKnife(knife, false); // Holstered weapons cannot attack.
		var melee = Combat.MeleeResolved;
		Combat._PhysicsProcess(1);
		Check(Combat.MeleeResolved == melee, "Holstered knife must not create a phantom hit.");
		_player.Camera.Rotation = new(-Mathf.Pi * 0.5f, 0, 0);
		Advance(rifle, 0.2);
		Press(rifle, MouseButton.Left, keepCameraAngle: true);
		Combat._PhysicsProcess(0.02);
		Check(Combat.LastHitNormal.Dot(Vector3.Up) > 0.99f, "Floor impacts must have a finite, upward-facing mark basis.");
		// Shoot a sleeping barrel-mass prop through the real weapon trace.
		_player.Recoil.Reset();
		_player.Camera.Rotation = Vector3.Zero;
		var barrel = PhysicalProp(_player.Camera.GlobalPosition + new Vector3(-.2f, -.2f, -2), 30);
		await Frame();
		barrel.Sleeping = true;
		Advance(rifle, .2);
		Press(rifle, MouseButton.Left);
		Combat._PhysicsProcess(.02);
		await Frame();
		Check(!barrel.Sleeping && barrel.LinearVelocity.Z < -1.5f,
			$"A bullet must visibly move a sleeping 30 kg barrel: {barrel.LinearVelocity}.");
		Check(barrel.AngularVelocity.Length() > .5f, "An off-center bullet must also rotate the barrel.");
		Check(Mathf.Abs(barrel.LinearVelocity.Z + rifle.BulletPhysicsImpulse / barrel.Mass) < .01f,
			"The weapon trace must deliver exactly one ammo impulse to the barrel.");
		foreach (var gun in _player.Inventory.Weapons.OfType<FirearmView>())
		{
			var expected = gun.Kind == FirearmView.FirearmKind.USP ? 2100f
				: gun.Kind == FirearmView.FirearmKind.AWP ? 2800f : 2400f;
			Check(Mathf.Abs(gun.BulletPhysicsImpulse - expected * SourceMovement.Unit) < .001f,
				$"{gun.Kind} must use the CS:S impulse for its ammo type.");
		}
		barrel.QueueFree();
		var light = PhysicalProp(new(10, 10, 0), 1);
		var heavy = PhysicalProp(new(20, 10, 0), 75);
		var protectedProp = PhysicalProp(new(30, 10, 0), 30);
		protectedProp.Set("source_data", new Godot.Collections.Dictionary { ["nodamageforces"] = "1" });
		var fixedProp = PhysicalProp(new(40, 10, 0), 30);
		fixedProp.Freeze = true;
		await Frame();
		foreach (var prop in new[] { light, heavy, protectedProp, fixedProp })
			prop.Call("source_bullet_damage", 30, prop.GlobalPosition, Vector3.Forward, 2400 * SourceMovement.Unit);
		await Frame();
		Check(Mathf.Abs(light.LinearVelocity.Length() - 2400 * SourceMovement.Unit) < .1f,
			"A 1 kg prop must receive the converted CS:S ammo impulse.");
		Check(heavy.LinearVelocity.Z < -.75f && heavy.LinearVelocity.Length() < light.LinearVelocity.Length(),
			"Heavy barrels must react to bullets while retaining their greater weight.");
		Check(protectedProp.LinearVelocity.IsZeroApprox(), "Props with nodamageforces must remain protected.");
		Check(fixedProp.Freeze && fixedProp.LinearVelocity.IsZeroApprox(), "Map-fixed props must remain fixed.");
		foreach (var prop in new[] { light, heavy, protectedProp, fixedProp }) prop.QueueFree();
		Combat.QueueBullet(rifle, 30);
		var resolved = Combat.BulletsResolved;
		_player.Respawn();
		Combat._PhysicsProcess(0.02);
		Check(Combat.BulletsResolved == resolved, "Respawn must discard pending attacks from the previous life.");
		var effects = Combat.GetNode<Node3D>("WorldEffects");
		var effectsPosition = effects.GlobalPosition;
		_player.Position += Vector3.Right;
		Check(effects.GlobalPosition.IsEqualApprox(effectsPosition), "World effects must not follow player movement.");
		foreach (var child in effects.GetChildren()) if (child is ShotTracer tracer) tracer._Process(1);
		await Frame();
		Check(!effects.GetChildren().OfType<ShotTracer>().Any(), "Tracers must expire promptly instead of accumulating.");
		_player.QueueFree();
		await Frame();
	}
}
