using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.Players;
using Contrapoint.UI;
using Contrapoint.Weapons;

public partial class DeathChecks : Node3D
{
	private int _checks, _failures;
	private void Check(bool value, string message)
	{
		_checks++; if (value) return; _failures++; GD.PushError(message);
	}
	private async Task Frames(int count = 1)
	{
		for (var i = 0; i < count; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
	}
	private Player Spawn(Vector3 at, bool local = false)
	{
		var player = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		player.LocalInput = local; player.DebugControls = false; player.Position = at; AddChild(player);
		player.SetPhysicsProcess(false); return player;
	}
	public override async void _Ready()
	{
		try { await Run(); } catch (Exception error) { _failures++; GD.PushError(error.ToString()); }
		foreach (var child in GetChildren()) child.QueueFree();
		await Frames(2); await Task.Delay(150);
		GD.Print($"DEATH_CHECKS checks={_checks} failures={_failures}");
		GetTree().Quit(_failures == 0 ? 0 : 1);
	}
	private async Task Run()
	{
		Check(SourceDamageRules.FriendlyFire, "The one-team sandbox starts with friendly fire enabled.");
		var ground = new StaticBody3D { Position = new(0, -.5f, 0) };
		ground.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(80, 1, 80) } }); AddChild(ground);
		var victim = Spawn(Vector3.Zero, true);
		victim.Inventory.ConfigureSourceLoadout();
		var attacker = Spawn(new(0, 0, 4));
		await Frames(3);
		victim.TakeHeDamage(10, attacker);
		Check(victim.Health == 90, "Default same-team HE damages the other player.");
		victim.Respawn();
		var gun = attacker.Inventory.Weapons.OfType<FirearmView>().First(g => g.Kind == FirearmView.FirearmKind.AK47);
		attacker.Inventory.Equip(gun);
		victim.TakeTraceDamage(36, attacker, SourceHitGroup.Chest, Vector3.Up, Vector3.Forward, 1.55f);
		Check(victim.Health == 88, "Default same-team bullets use CSS's .35 friendly fire multiplier.");
		victim.Respawn(); SourceDeathRules.AutoRespawn = false;
		SourceDamageRules.FriendlyFire = false; attacker.Team = PlayerTeam.CounterTerrorist;
		victim.Velocity = Vector3.Right * 3;
		var head = victim.Body.Hitboxes.GroupCenter(SourceHitGroup.Head);
		victim.TakeTraceDamage(36, attacker, SourceHitGroup.Head, head, Vector3.Forward, 1.55f, impulse: gun.BulletPhysicsImpulse);
		var ragdoll = victim.Death.Ragdoll!;
		Check(!victim.IsAlive && victim.Death.Active && !victim.Inventory.Enabled, "Death starts the camera and stops weapons.");
		Check(ragdoll.Bones.Count == 16 && ragdoll.Bones.All(b => b.GetBoneId() >= 0 && b.IsSimulatingPhysics()), "All 16 stock PHY bodies simulate on real skeleton bones.");
		Check(Mathf.Abs(ragdoll.Bones.Sum(b => b.Mass) - 102.63675f) < .01f, "Original PHY masses retain their compiled total (including minimum body masses).");
		Check(ragdoll.ImpulseBone.EndsWith("Head1") && ragdoll.AppliedImpulse.Z < -120, "Lethal headshot applies 2x ammo impulse to the head.");
		Check(ragdoll.InitialVelocity == Vector3.Right * 3, "Ragdoll inherits player momentum before Die clears live velocity.");
		Check(!victim.Body.Visible && victim.MouseCaptured && victim.RenderCamera == victim.Death.Camera,
			"Death hides the live model, retains game mouse capture and switches the rendered camera.");
		Check(victim.Body.WorldWeapon == null && !ragdoll.FindChildren("WorldWeapon", "Node3D", true, false).Any(),
			"Death deletes the held world weapon before cloning the corpse.");
		var screen = victim.Death.GetChildren().OfType<DeathScreen>().First();
		Check(!screen.Panel.Visible && !screen.Panel.FindChildren("*", "Button", true, false).Any(), "The original panel has no respawn button and stays hidden during the corpse camera.");
		Check(!victim.Death.RequestRespawn(), "Instant death clicks cannot skip the .8 second death-camera phase.");
		var start = ragdoll.Focus;
		await Frames(24);
		Check(ragdoll.Focus.X > start.X + .1f, $"The corpse keeps moving sideways instead of kneeling in place: {ragdoll.Focus - start}.");
		Check(ragdoll.Bones.All(b => b.GlobalPosition.IsFinite() && b.GlobalPosition.DistanceTo(start) < 10), "Ragdoll constraints remain finite and connected after a head impulse.");
		Check(victim.Body.WorldWeapon == null, "Dead presentation ticks do not recreate a weapon model.");
		await Frames(85);
		Check(victim.Death.CanRespawn && victim.Death.Frozen, "CSS death camera transitions to the killer freeze camera.");
		Check(screen.Panel.Visible && screen.Panel.GetNode<Label>("infolabel1").Text == "KILLED YOU"
			&& screen.Panel.GetNode<Label>("killername").Text == attacker.Name, "The English CSS freeze panel shows the killer at the freeze.");
		Check(ragdoll.ContactSounds > 0, "Real world collisions produce stock ragdoll impact sounds.");
		Check(victim.Death.RequestRespawn() && victim.IsAlive && !victim.Death.Active && victim.Body.Visible,
			"Respawn restores health, live body and death state.");
		Check(victim.Inventory.Enabled && victim.MouseCaptured && victim.RenderCamera != victim.Death.Camera,
			"Respawn restores weapon input and first-person camera.");
		var spawnWeapon = victim.ActiveWeapon;
		Check(victim.Inventory.EquipPrevious() && victim.ActiveWeapon != spawnWeapon,
			"Lastinv immediately selects another weapon after death and respawn.");
		Check(victim.Inventory.EquipPrevious() && victim.ActiveWeapon == spawnWeapon,
			"Repeated lastinv switches back to the spawn weapon.");
		foreach (var primary in victim.Inventory.Weapons.Where(w => w.Slot == WeaponSlot.Primary).ToArray())
			victim.Inventory.RemoveWeapon(primary);
		Check(victim.Inventory.EquipPrevious() && victim.ActiveWeapon?.Slot == WeaponSlot.Secondary,
			"Lastinv falls back to the pistol when no rifle or previous weapon remains.");
		Check(victim.Inventory.EquipPrevious() && victim.ActiveWeapon == spawnWeapon,
			"Lastinv toggles between pistol and knife without a rifle.");
		Check(!screen.Visible && !screen.Panel.Visible, "Respawn removes the freeze image and panel.");
		Check(victim.Body.WorldWeapon != null && victim.Body.WorldWeapon.Visible, "Respawn reattaches the living player's weapon.");
		Check(GodotObject.IsInstanceValid(ragdoll), "The corpse stays in the world after the player respawns.");
		await Frames(2);
		Check(!victim.GetNode<CollisionShape3D>("CollisionShape3D").Disabled, "Respawn restores the live movement hull.");
		SourceDeathRules.AutoRespawn = true; SourceDeathRules.RespawnDelay = .85f;
		victim.TakeDamage(100); await Frames(80);
		Check(victim.IsAlive && !victim.Death.Active && victim.Inventory.Enabled, "Sandbox automatically respawns without a round manager.");
		SourceDeathRules.AutoRespawn = false;
		victim.TakeDamage(100); await Frames(65);
		victim._Input(new InputEventKey { PhysicalKeycode = Key.Space, Keycode = Key.Space, Pressed = true });
		Check(victim.IsAlive, "Space respawns a dead player even though normal gameplay binds are disabled.");
		SourceDeathRules.AutoRespawn = true; SourceDeathRules.RespawnDelay = 6.5f;
	}
}
