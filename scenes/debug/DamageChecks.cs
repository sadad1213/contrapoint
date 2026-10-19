using Godot;
using System;
using System.Threading.Tasks;
using Contrapoint.Players;
using Contrapoint.Weapons;
using Contrapoint.UI;

public partial class DamageChecks : Node3D
{
	private int _checks, _failures;
	private Player _player = null!;
	private void Check(bool condition, string message)
	{
		_checks++;
		if (!condition) { _failures++; GD.PushError(message); }
	}
	private async Task Frame()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}
	private StaticBody3D Box(Vector3 at, Vector3 size)
	{
		var box = new StaticBody3D { Position = at };
		box.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
		AddChild(box); return box;
	}
	private void Reset(Vector3? at = null)
	{
		_player.SetSpawn(new(Basis.Identity, at ?? Vector3.Zero));
		_player.Respawn();
	}
	private void Blast(Vector3 at)
	{
		var grenade = new GrenadeProjectile();
		grenade.ConfigureNetwork(GrenadeKind.HE, null);
		AddChild(grenade); grenade.GlobalPosition = at; grenade.Detonate();
	}
	public override async void _Ready()
	{
		try { await Run(); }
		catch (Exception error) { _failures++; GD.PushError(error.ToString()); }
		foreach (var child in GetChildren()) child.QueueFree();
		await Frame(); await Task.Delay(150);
		GD.Print($"DAMAGE_CHECKS checks={_checks} failures={_failures}");
		GetTree().Quit(_failures == 0 ? 0 : 1);
	}
	private async Task Run()
	{
		SourceDamageRules.FriendlyFire = false; // Stock CSS reference cases; sandbox defaults are covered by DeathChecks.
		_player = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		_player.LocalInput = false; _player.DebugControls = false; AddChild(_player);
		await Frame();
		Check(Player.FallDamage(580) == 0 && Mathf.IsEqualApprox(Player.FallDamage(996), 100), "CSS safe and lethal fall speeds.");
		Check(Mathf.IsEqualApprox(Player.HeDamage(0), 100)
			&& Mathf.IsEqualApprox(Player.HeDamage(175), 32.46525f), "CSS Gaussian blast reference values.");
		Reset(); _player.Armor = 100; _player.HasHelmet = true;
		_player.TakeHeDamage(80, null);
		Check(_player.Health == 60 && _player.Armor == 80 && _player.HasHelmet, "HE splits damage between health and armor.");
		Reset(); _player.Armor = 10; _player.HasHelmet = true; _player.TakeHeDamage(80, null);
		Check(_player.Health == 40 && _player.Armor == 0 && !_player.HasHelmet, "Exhausted armor passes excess HE damage to health.");
		Reset(); _player.Armor = 100; _player.ApplyFallDamage(788);
		Check(_player.Health == 50 && _player.Armor == 100, "Fall damage bypasses armor.");
		Reset(); _player.TakeHeDamage(.6f, null); _player.TakeHeDamage(.6f, null);
		Check(_player.Health == 100, "CCSPlayer truncates each damage event before base-character fractional accumulation.");
		Reset(); _player.TakeHeDamage(.9f, null); Reset(); _player.TakeHeDamage(.2f, null);
		Check(_player.Health == 100, "Respawn clears fractional damage.");
		_player.TakeHeDamage(50, _player);
		Check(_player.Health == 50, "Self HE damage remains enabled.");
		var ally = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		ally.LocalInput = false; ally.Position = new(20, 0, 0); AddChild(ally);
		Reset(); _player.TakeHeDamage(50, ally);
		Check(_player.Health == 100, "Default CSS friendly fire prevents teammate HE damage.");
		ally.Team = PlayerTeam.CounterTerrorist; _player.TakeHeDamage(50, ally);
		Check(_player.Health == 50, "Enemy HE damages players.");
		void Hit(float damage, SourceHitGroup group = SourceHitGroup.Chest, float ratio = 1.55f, bool knife = false, bool large = true) =>
			_player.TakeTraceDamage(damage, ally, group, _player.GlobalPosition + Vector3.Up, Vector3.Back, ratio, knife, large);
		Reset(); Hit(36);
		Check(_player.Health == 64 && _player.LastHitGroup == SourceHitGroup.Chest, "AK chest damage without armor.");
		Check(Mathf.IsEqualApprox(_player.Movement.DamageSpeedModifier, .65f) && _player.Recoil.DamagePunchDegrees.X < -3.5f,
			"Rifle body hit tags and punches the unarmored victim.");
		Reset(); _player.Armor = 100; Hit(36);
		Check(_player.Health == 73 && _player.Armor == 95 && _player.LastDamageSound == "player.damagekevlar",
			"AK armor ratio 1.55, truncated health and armor, kevlar sound.");
		Check(_player.Recoil.DamagePunchDegrees == Vector3.Zero, "Kevlar prevents chest aim punch.");
		Reset(); _player.Armor = 100; _player.HasHelmet = true; Hit(36, SourceHitGroup.Head);
		Check(!_player.IsAlive && _player.LastDamageSound == "player.damagehelmet", "AK headshot remains lethal through helmet.");
		Reset(); _player.Armor = 100; _player.HasHelmet = true; Hit(33, SourceHitGroup.Head, 1.4f);
		Check(_player.Health == 8 && _player.Armor == 80 && _player.Recoil.DamagePunchDegrees == Vector3.Zero,
			"M4 helmet headshot deals 92 damage without aim punch.");
		Reset(); Hit(10, SourceHitGroup.Head);
		Check(_player.Health == 60 && Mathf.IsEqualApprox(_player.Recoil.DamagePunchDegrees.X, -12)
			&& Mathf.Abs(_player.Recoil.DamagePunchDegrees.Z) <= 9 && _player.LastDamageSound == "player.damageheadshot", "Bare head has 4x damage, capped pitch/roll and headshot audio.");
		Reset(); Hit(36, SourceHitGroup.Stomach);
		Check(_player.Health == 55, "Stomach multiplies damage by 1.25.");
		Reset(); _player.Armor = 100; Hit(36, SourceHitGroup.LeftLeg);
		Check(_player.Health == 73 && _player.Armor == 100 && _player.Movement.DamageSpeedModifier == .5f,
			"Leg damage is .75, bypasses kevlar and uses small flinch tagging.");
		Reset(); Hit(65, SourceHitGroup.Generic, 1.7f, true, false);
		Check(_player.Health == 35 && _player.Recoil.DamagePunchDegrees == Vector3.Zero, "Knife hull damage uses generic group, no head or stomach scaling.");
		Reset(); _player.Armor = 100; Hit(65, SourceHitGroup.Generic, 1.7f, true, false);
		Check(_player.Health == 45 && _player.Armor == 95, "Knife stab uses stock 1.7 armor ratio.");
		Reset(); Hit(195, SourceHitGroup.Generic, 1.7f, true, false);
		Check(!_player.IsAlive, "Backstab is 195 damage before armor.");
		Check(SourceDamageRules.KnifeDamage(false, true, false) == 20 && SourceDamageRules.KnifeDamage(false, false, true) == 15,
			"Only secondary knife attacks receive backstab damage; slash combo is 20/15.");
		Check(SourceDamageRules.IsBackstab(Vector3.Back, Vector3.Zero, Vector3.Forward)
			&& !SourceDamageRules.IsBackstab(Vector3.Forward, Vector3.Zero, Vector3.Forward)
			&& !SourceDamageRules.IsBackstab(Vector3.Right, Vector3.Zero, Vector3.Forward), "CSS backstab cone uses victim forward and horizontal line of sight > .8.");
		Reset(); _player.Velocity = new(0, 0, -8); var beforeVelocity = _player.Velocity; Hit(10);
		Check(_player.Velocity == beforeVelocity && _player.Movement.DamageSpeedModifier == 1,
			"Large flinch above 300 u/s leaves velocity modifier unchanged and never pushes the living player.");
		Reset(); _player.Movement.ApplyNetworkDuck(true); Hit(10);
		Check(_player.Movement.DamageSpeedModifier == .5f, "Crouching uses small flinch even against a rifle.");
		Reset(); SourceDamageRules.FriendlyFire = true; ally.Team = _player.Team; Hit(36);
		Check(_player.Health == 88, "Friendly fire scales bullets to .35 before integer damage.");
		SourceDamageRules.FriendlyFire = false; ally.Team = PlayerTeam.CounterTerrorist;
		Reset(); _player.Armor = 100; _player.TakeHeDamage(80, ally);
		Check(_player.Health == 41 && _player.Armor == 89, "Modern CSS HE uses installed grenade armor ratio 1.475.");
		Reset(); _player.Armor = 100; SourceDamageRules.LegacyGrenadeDamage = true; _player.TakeHeDamage(80, ally);
		Check(_player.Health == 32 && _player.Armor == 94, "Legacy HE reads the thrower's active weapon armor ratio (knife 1.7), as in CCSPlayer.");
		SourceDamageRules.LegacyGrenadeDamage = false;
		Reset(); ConsoleCommands.Execute("sv_falldamage 0", _player.Movement, (_,_) => {}); _player.ApplyFallDamage(1100);
		Check(_player.Health == 100, "Fall damage switch disables lethal falls.");
		Hit(10); Check(_player.Health == 90, "Fall switch preserves bullet damage.");
		ConsoleCommands.Execute("sv_falldamage 1; sv_cheats 1; god", _player.Movement, (_,_) => {});
		Reset(); Hit(100); _player.ApplyFallDamage(1100);
		Check(_player.Health == 100 && _player.Armor == 0, "god prevents gameplay damage before armor processing.");
		ConsoleCommands.Execute("god 0; buddha; hurtme 1000", _player.Movement, (_,_) => {});
		Check(_player.Health == 1, "buddha keeps the player alive at one HP.");
		ConsoleCommands.Execute("sv_cheats 0", _player.Movement, (_,_) => {});
		Check(!_player.GodMode && !_player.BuddhaMode, "Disabling cheats clears invulnerability.");
		ally.QueueFree();
		Reset(); _player.Movement.ToggleNoclip(); _player.TakeHeDamage(100, null);
		Check(_player.Health == 100, "Noclip ignores gameplay damage.");
		Reset(); var deaths = 0; _player.Died += () => deaths++;
		_player.ApplyFallDamage(1100); _player.TakeHeDamage(100, null);
		Check(!_player.IsAlive && deaths == 1 && !_player.Inventory.Enabled, "Lethal damage emits death once and disables weapons.");
		Reset(); Check(_player.IsAlive && _player.Inventory.Enabled, "Respawn restores player after lethal damage.");

		var ground = Box(new(0, -.5f, 0), new(100, 1, 100)); await Frame();
		Reset(new(0, 8, 0));
		for (var i = 0; i < 180 && _player.LastDamageKind != PlayerDamageKind.Fall; i++)
		{ _player.Simulate(default, 1f / 66); await Frame(); }
		// LastDamageKind can survive respawn; wait for actual floor contact too.
		for (var i = 0; i < 180 && !_player.IsOnFloor(); i++)
		{ _player.Simulate(default, 1f / 66); await Frame(); }
		Check(_player.Health < 100 && _player.IsAlive && _player.LastDamageKind == PlayerDamageKind.Fall, "A real 8m fall applies landing damage.");
		var landingHealth = _player.Health;
		for (var i = 0; i < 10; i++) _player.Simulate(default, 1f / 66);
		Check(_player.Health == landingHealth, "Standing on the ground does not repeat fall damage.");
		Reset(new(0, 1, 0));
		for (var i = 0; i < 90; i++) { _player.Simulate(default, 1f / 66); await Frame(); }
		Check(_player.Health == 100, "A low fall is safe.");
		ground.ConstantLinearVelocity = Vector3.Down * (200 * SourceMovement.Unit);
		Reset(new(0, 8, 0));
		for (var i = 0; i < 180; i++) { _player.Simulate(default, 1f / 66); await Frame(); }
		Check(_player.Health == 100, "Descending ground velocity reduces fall impact.");
		ground.ConstantLinearVelocity = Vector3.Zero;
		ground.SetMeta("source_floating", true); Reset(new(0, 8, 0));
		for (var i = 0; i < 180; i++) { _player.Simulate(default, 1f / 66); await Frame(); }
		Check(_player.Health == 100, "Floating ground receives Source's 200 units/s landing allowance.");
		ground.RemoveMeta("source_floating");

		Reset(); await Frame();
		var origin = new Vector3(0, .8f, -2);
		Blast(origin); var exposedDamage = 100 - _player.Health;
		Check(exposedDamage > 0, "Uncovered HE blast damages a real player.");
		Reset(); var wall = Box(new(0, 1, -1), new(10, 3, .1f)); await Frame();
		Blast(origin); Check(_player.Health == 100, "World cover blocks every HE sample.");
		wall.QueueFree(); await Frame();
		Reset(); wall = Box(new(0, .625f, -1), new(10, 1.25f, .1f)); await Frame();
		Blast(origin); var coveredDamage = 100 - _player.Health;
		Check(coveredDamage > 0 && coveredDamage < exposedDamage, $"Partial cover reduces rather than cancels HE damage: {coveredDamage}/{exposedDamage}.");
		wall.QueueFree(); await Frame();
		Reset();
		var cover = new RigidBody3D { Freeze = true, Position = new(0, 1, -1) };
		cover.SetMeta("source_density", 500f);
		cover.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(10, 3, .1f) } }); AddChild(cover);
		await Frame(); Blast(origin);
		Check(_player.Health < 100, "Low density physics cover allows attenuated HE damage.");
		Reset(); cover.SetMeta("source_density", 3000f); Blast(origin);
		Check(_player.Health == 100, "Dense physics cover absorbs HE damage.");
		cover.QueueFree(); await Frame();
		Reset(new(0, 0, -15)); Blast(origin);
		Check(_player.Health == 100, "Players outside HE radius are unaffected.");

		Reset(new(0, 8, 0));
		var water = new Area3D { CollisionLayer = 4, CollisionMask = 0, Position = new(0, .15f, 0) };
		water.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(10, .3f, 10) } }); AddChild(water);
		await Frame();
		for (var i = 0; i < 180; i++) { _player.Simulate(default, 1f / 66); await Frame(); }
		Check(_player.Health == 100 && _player.Movement.WaterLevel > 0, "Landing in shallow water cancels fall damage as in Source.");
		water.Position = new(0, 1, 0);
		((BoxShape3D)water.GetChild<CollisionShape3D>(0).Shape).Size = new(10, 4, 10);
		Reset(); await Frame(); Blast(new(0, 1.5f, -6));
		Check(_player.Health == 100, "An air explosion cannot damage a fully submerged player.");
		Reset(new(6, 0, 0)); await Frame(); Blast(new(4.5f, 1, 0));
		Check(_player.Health == 100, "An underwater explosion cannot damage a dry player.");
		Reset(); await Frame(); Blast(new(0, 1, -2));
		Check(_player.Health < 100, "An underwater explosion damages a submerged player.");
	}
}
