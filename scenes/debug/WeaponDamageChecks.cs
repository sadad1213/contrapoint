using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.Players;
using Contrapoint.Weapons;

public partial class WeaponDamageChecks : Node3D
{
	private int _checks, _failures;
	private void Check(bool value, string message)
	{
		_checks++; if (value) return; _failures++; GD.PushError(message);
	}
	private async Task Frame()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}
	private Player Spawn(Vector3 at, PlayerTeam team)
	{
		var player = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		player.LocalInput = false; player.DebugControls = false; player.Position = at; player.Team = team; AddChild(player);
		// These are stationary trace fixtures; there is no map/floor to simulate.
		player.SetPhysicsProcess(false);
		player.Combat.SetPhysicsProcess(false);
		foreach (var weapon in player.Inventory.Weapons) weapon.SetProcess(false);
		return player;
	}
	public override async void _Ready()
	{
		try { await Run(); } catch (Exception exception) { _failures++; GD.PushError(exception.ToString()); }
		foreach (var child in GetChildren()) child.QueueFree();
		await Frame(); await Task.Delay(150);
		GD.Print($"WEAPON_DAMAGE_CHECKS checks={_checks} failures={_failures}");
		GetTree().Quit(_failures == 0 ? 0 : 1);
	}
	private async Task Run()
	{
		var attacker = Spawn(Vector3.Zero, PlayerTeam.Terrorist);
		var target = Spawn(new(0, 0, -3), PlayerTeam.CounterTerrorist);
		target.MaxHealth = 500; target.Respawn();
		await Frame();
		var guns = attacker.Inventory.Weapons.OfType<FirearmView>().ToArray();
		var expected = new[] { (FirearmView.FirearmKind.AK47, 36, 27, 35), (FirearmView.FirearmKind.M4A1, 33, 23, 32),
			(FirearmView.FirearmKind.USP, 34, 17, 26), (FirearmView.FirearmKind.Deagle, 54, 40, 43),
			(FirearmView.FirearmKind.AWP, 115, 112, 113), (FirearmView.FirearmKind.Scout, 75, 63, 73) };
		foreach (var (kind, damage, armored, at500) in expected)
		{
			var gun = guns.First(g => g.Kind == kind); gun.ApplyNetworkSilencer(false);
			Check(gun.EffectiveBulletDamage == damage, $"{kind} stock CTX damage.");
			target.Respawn(); target.Armor = 100;
			target.TakeTraceDamage(damage, attacker, SourceHitGroup.Chest, Vector3.Up, Vector3.Forward, SourceDamageRules.ArmorRatio(gun));
			Check(target.Health == 500 - armored, $"{kind} independently known armored chest damage.");
			Check((int)SourceDamageRules.DistanceDamage(damage, 500 * SourceMovement.Unit, SourceDamageRules.RangeModifier(gun)) == at500,
				$"{kind} damage at 500 Source units.");
		}
		var usp = guns.First(g => g.Kind == FirearmView.FirearmKind.USP);
		usp.ApplyNetworkSilencer(true); Check(usp.EffectiveBulletDamage == 30, "Silenced USP uses 30 instead of 34 damage."); usp.ApplyNetworkSilencer(false);
		var m4 = guns.First(g => g.Kind == FirearmView.FirearmKind.M4A1);
		m4.ApplyNetworkSilencer(true); Check(SourceDamageRules.RangeModifier(m4) == .95f, "Silenced M4 uses .95 range modifier."); m4.ApplyNetworkSilencer(false);
		foreach (var agent in PlayerBody.AgentIds)
		{
			target.SetAgent(agent); target.Respawn();
			Check(target.Body.Hitboxes.Count > 15, $"{agent} original MDL hitbox set.");
			var head = target.Body.Hitboxes.GroupCenter(SourceHitGroup.Head);
			Check(head.Y > 1.4f && head.Y < 2 && Mathf.Abs(head.Z + 3) < .5f,
				$"{agent} Source bone-local hitboxes align with Godot model: head={head}.");
			Check(target.Body.Hitboxes.Trace(head + Vector3.Back * 2, head, out _, out var group, out _) && group == SourceHitGroup.Head,
				$"{agent} actual head trace resolves HITGROUP_HEAD.");
		}
		target.SetAgent("urban"); target.Respawn();
		var ak = guns.First(g => g.Kind == FirearmView.FirearmKind.AK47);
		void Shoot()
		{
			var aim = new Transform3D(Basis.Identity, attacker.Camera.GlobalPosition).LookingAt(target.Body.Hitboxes.GroupCenter(SourceHitGroup.Chest));
			attacker.Combat.ResolveNetworkAttack(ak, aim, false, false); attacker.Combat._PhysicsProcess(1f / 66);
		}
		Shoot(); var uncovered = 500 - target.Health;
		Check(uncovered == 35, $"Real AK chest ray at 3m: {uncovered}.");
		var wall = new StaticBody3D { Position = new(0, 1, -1.5f) };
		wall.SetMeta("source_surface", "wood");
		var shape = new BoxShape3D { Size = new(4, 4, .1f) };
		wall.AddChild(new CollisionShape3D { Shape = shape }); AddChild(wall); await Frame();
		target.Respawn(); Shoot(); var throughWood = 500 - target.Health;
		Check(throughWood > 0 && throughWood < uncovered, $"Thin wood penetrates with CSS damage loss: {throughWood}/{uncovered}.");
		shape.Size = new(4, 4, 1); wall.SetMeta("source_surface", "concrete"); await Frame();
		target.Respawn(); Shoot(); Check(target.Health == 500, "Thick concrete exceeds the AK penetration budget.");
		wall.QueueFree(); await Frame();
		var knife = attacker.Inventory.Weapons.First(w => w is KnifeView);
		async Task Stab(bool back)
		{
			target.SetSpawn(new(Basis.FromEuler(new(0, back ? 0 : Mathf.Pi, 0)), new(0, 0, -.75f))); target.Respawn();
			await Frame();
			var aim = new Transform3D(Basis.Identity, attacker.Camera.GlobalPosition).LookingAt(target.GlobalPosition + Vector3.Up);
			attacker.Combat.ResolveNetworkAttack(knife, aim, true, true); attacker.Combat._PhysicsProcess(.21);
		}
		await Stab(false); Check(target.Health == 435 && target.LastHitGroup == SourceHitGroup.Generic, $"Real front stab uses generic damage: {target.Health}.");
		await Stab(true); Check(target.Health == 305, $"Real rear stab triples damage to 195: {target.Health}.");
		Check(GetChildren().OfType<SourceDamageEffects>().Any(), "Authoritative trace damage creates blood impact effects.");
		var health = target.Health; attacker.Combat.PresentNetworkImpact(Transform3D.Identity, Vector3.Zero, Vector3.Up, target, false, false);
		Check(target.Health == health, "Replicated cosmetic impacts cannot deal a second hit.");
		Check(SourceDamageHearing.PresetAt(-.1) == 0 && SourceDamageHearing.PresetAt(0) == 137
			&& SourceDamageHearing.PresetAt(.06) == 138 && SourceDamageHearing.PresetAt(.1) == 139
			&& SourceDamageHearing.PresetAt(3.1) == 138 && SourceDamageHearing.PresetAt(4) == 137
			&& SourceDamageHearing.PresetAt(4.5) == 0, "CSS HE deafness delay, filter stages and recovery.");
		var filters = AudioServer.GetBusEffectCount(0);
		attacker.LocalInput = true; attacker.DamageHearing.Shock(); attacker.DamageHearing._Process(.32);
		Check(attacker.DamageHearing.Preset == 137 && AudioServer.GetBusEffectCount(0) == filters + 1,
			"Local HE shock installs the Godot audio filter after the .3 second delay.");
		attacker.DamageHearing.Reset(); attacker.LocalInput = false;
		Check(!attacker.DamageHearing.Active && !AudioServer.IsBusEffectEnabled(0, filters), "Respawn/reset clears local deafness.");
	}
}
