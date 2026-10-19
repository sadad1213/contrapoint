using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.Players;
using Contrapoint.Weapons;
using Contrapoint.Gameplay;

public partial class WeaponPickupChecks : Node3D
{
	private int _checks;
	private void Check(bool condition, string message)
	{
		_checks++;
		if (!condition) throw new InvalidOperationException(message);
	}
	private async Task Frames(int count = 1)
	{
		for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}
	private Player AddPlayer(Vector3 position)
	{
		var player = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		player.LocalInput = false; player.DebugControls = false; player.Position = position;
		AddChild(player);
		player.Inventory.ConfigureSourceLoadout();
		return player;
	}
	private void ClearSlot(Player player, WeaponSlot slot)
	{
		foreach (var weapon in player.Inventory.Weapons.Where(w => w.Slot == slot).ToArray()) player.Inventory.RemoveWeapon(weapon);
	}
	private async Task ConsolePickupAfterRespawn()
	{
		var player = AddPlayer(new(-20, 0, 20));
		var cheats = SourceDamageRules.Cheats;
		try
		{
			SourceDamageRules.Cheats = true;
			void Command(string text) => Contrapoint.UI.ConsoleCommands.Execute(text, player.Movement,
				(line, error) => { if (error) throw new InvalidOperationException(line); });
			var existing = GetTree().GetNodesInGroup("source_weapon_pickups").OfType<WeaponPickup>().Select(p => p.GetInstanceId()).ToHashSet();
			Command("ent_create weapon_m4a1");
			var spawned = GetTree().GetNodesInGroup("source_weapon_pickups").OfType<WeaponPickup>().Single(p => !existing.Contains(p.GetInstanceId()));
			await Frames(150);
			Check(spawned.GlobalPosition.Y < .5f, "console-created rifle settles on the floor");
			player.TakeDamage(100); player.Respawn();
			Check(player.IsAlive && !spawned.Taken, "console-created rifle survives its creator's death and respawn");
			player.GlobalPosition = new(spawned.GlobalPosition.X, 0, spawned.GlobalPosition.Z);
			await Frames(3);
			Check(!spawned.Taken && player.Inventory.Weapons.OfType<FirearmView>().Any(w => w.Kind == FirearmView.FirearmKind.AK47),
				"respawn loadout occupies the primary slot until the starting rifle is dropped");
			Command("slot1; drop");
			await Frames(3);
			Check(!GodotObject.IsInstanceValid(spawned) || spawned.Taken, "dropping the starting rifle allows automatic pickup of a console rifle after respawn");
			Check(player.Inventory.Weapons.OfType<FirearmView>().Single(w => w.Slot == WeaponSlot.Primary).Kind == FirearmView.FirearmKind.M4A1,
				"respawned player receives the console-created rifle");
		}
		finally { SourceDamageRules.Cheats = cheats; }
	}
	public override async void _Ready()
	{
		try
		{
			var floor = new StaticBody3D { Position = new(0, -.5f, 0) };
			floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(100, 1, 100) } }); AddChild(floor);
			var player = AddPlayer(Vector3.Zero);
			await Frames(2);
			Check(player.Inventory.Weapons.Count(w => w.Slot == WeaponSlot.Primary) == 1
				&& player.Inventory.Weapons.Count(w => w.Slot == WeaponSlot.Secondary) == 1, "Source loadout has one rifle and pistol");
			player.Inventory.EquipSlot(WeaponSlot.Primary);
			var gun = (FirearmView)player.ActiveWeapon!;
			gun.RestoreMagazine(17); player.Inventory.SetReserveAmmo(gun.AmmoType, 42, gun.ReserveCapacity);
			var drop = player.Inventory.DropWeapon(gun)!;
			Check(drop.Magazine == 17 && drop.ExtraAmmo == 42 && player.Inventory.GetReserveAmmo(gun.AmmoType) == 0, "drop transfers clip and reserve without refill");
			Check(player.ActiveWeapon?.Slot == WeaponSlot.Secondary, "drop selects next best gun");
			Check(drop.Mass == 5 && drop.GetChildren().OfType<CollisionShape3D>().Any(c => c.Shape is ConvexPolygonShape3D), "world entity uses original PHY mass and hull");
			Check(drop.AngularVelocity.IsEqualApprox(new Vector3(1, 1, -1) * Mathf.DegToRad(200)), "CSS 200 degree angular velocity");
			Check(drop.LinearVelocity.Length() > 1 && drop.LinearVelocity.Z < 0 && drop.LinearVelocity.Y > 0, "drop tosses forward and upward");
			Check(!drop.TryPickup(player), "previous owner cannot reclaim immediately");
			var other = AddPlayer(new(20, 0, 0)); ClearSlot(other, WeaponSlot.Primary);
			drop.GlobalPosition = other.GlobalPosition + Vector3.Up;
			Check(drop.TryPickup(other), "another player may collect immediately");
			var recovered = (FirearmView)other.ActiveWeapon!;
			Check(recovered.Kind == FirearmView.FirearmKind.AK47 && recovered.MagazineAmmo == 17 && recovered.ReserveAmmo == 42, "pickup restores ammo exactly");
			Check(recovered.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>().All(m => m.Layers == 2), "new view model remains in weapon render layer");
			Check(!drop.TryPickup(player), "an entity cannot be picked up twice");
			player.Inventory.EquipSlot(WeaponSlot.Melee); var before = player.Inventory.Weapons.Count;
			player.DropActiveWeapon(); Check(player.Inventory.Weapons.Count == before, "knife cannot be dropped with G");
			player.Inventory.EquipSlot(WeaponSlot.Grenade); player.DropActiveWeapon();
			Check(player.Inventory.Weapons.Count == before, "grenades cannot be dropped with G in CSS");
			var occupied = WeaponPickup.Spawn(this, "weapon_m4a1", new(Basis.Identity, other.GlobalPosition + Vector3.Up));
			Check(!occupied.TryPickup(other), "occupied rifle slot rejects pickup");
			occupied.QueueFree();
			ClearSlot(player, WeaponSlot.Secondary);
			var usp = WeaponPickup.Spawn(this, "weapon_usp", new(Basis.Identity, player.GlobalPosition + Vector3.Up), magazine: 3, reserve: 0, silenced: true);
			Check(usp.TryPickup(player), "free pistol slot accepts USP");
			var pistol = player.Inventory.Weapons.OfType<FirearmView>().Single();
			Check(pistol.MagazineAmmo == 3 && pistol.ReserveAmmo == 0 && pistol.Silenced, "pickup preserves silencer and zero reserve");
			player.Inventory.Equip(pistol);
			var silenced = player.Inventory.DropWeapon(pistol)!;
			Check(silenced.Silenced && silenced.Magazine == 3, "silencer survives drop");
			silenced.LinearVelocity = Vector3.Zero; silenced.GlobalPosition = new(5, 2, 0);
			await Frames(90);
			Check(silenced.GlobalPosition.Y < .5f && silenced.GlobalPosition.Y > -.3f, "weapon falls and rests on solid floor");
			Check(silenced.Age >= .8, "owner cooldown advances in physics time");
			Check(silenced.TryPickup(player), "previous owner may recover after 0.8 seconds");
			await Frames();
			player.Inventory.AutoSwitch = false; player.Inventory.EquipSlot(WeaponSlot.Melee);
			var manual = WeaponPickup.Spawn(this, "weapon_awp", new(Basis.Identity, player.GlobalPosition + Vector3.Up), magazine: 0);
			Check(manual.TryPickup(player) && player.ActiveWeapon?.Slot == WeaponSlot.Melee, "cl_autowepswitch 0 keeps selected weapon");
			Check(player.Inventory.Weapons.OfType<FirearmView>().Single(w => w.Kind == FirearmView.FirearmKind.AWP).MagazineAmmo == 0, "empty magazines stay empty");
			ClearSlot(player, WeaponSlot.Primary);
			var touch = WeaponPickup.Spawn(this, "weapon_scout", new(Basis.Identity, player.GlobalPosition + Vector3.Up));
			await Frames(3);
			Check(!GodotObject.IsInstanceValid(touch) || touch.Taken, "touch automatically picks up a free slot");
			ClearSlot(player, WeaponSlot.Primary);
			var wall = new StaticBody3D { Position = new(.5f, 1, 0) };
			wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(.1f, 2, 3) } }); AddChild(wall);
			await Frames(2);
			var blocked = WeaponPickup.Spawn(this, "weapon_ak47", new(Basis.Identity, new(1, 1, 0)));
			Check(!blocked.TryPickup(player), "pickup cannot pass through a wall");
			blocked.QueueFree(); wall.QueueFree();
			await Frames();
			var physical = WeaponPickup.Spawn(this, "weapon_m4a1", new(Basis.Identity, new(10, 2, 0)));
			await Frames(2);
			var previous = physical.LinearVelocity;
			physical.source_bullet_damage(30, physical.GlobalPosition + Vector3.Up * .1f, Vector3.Right, 30);
			await Frames(2);
			Check(physical.LinearVelocity.X > previous.X + 1, "bullet impulse moves a dropped gun");
			physical.QueueFree();
			player.Respawn();
			Check(player.Inventory.Weapons.Any(w => WeaponPickup.ClassOf(w) == "weapon_ak47"), "respawn restores starting loadout after dropping");
			player.TakeDamage(100);
			Check(!player.Inventory.Weapons.Any(w => w.Slot == WeaponSlot.Primary) && player.Inventory.Weapons.Any(w => w.Slot == WeaponSlot.Secondary), "death drops rifle and retains pistol");
			var deathDrops = GetTree().GetNodesInGroup("source_weapon_pickups").OfType<WeaponPickup>().Where(p => p.PreviousOwner == player).ToArray();
			var deathRifle = deathDrops.Single(p => p.Slot == WeaponSlot.Primary);
			var deathGrenade = deathDrops.Single(p => p.Slot == WeaponSlot.Grenade);
			foreach (var entity in deathDrops) { entity.Freeze = true; entity.SetPhysicsProcess(false); }
			deathRifle.GlobalTransform = new(Basis.Identity, other.GlobalPosition + new Vector3(0, .17f, .4f));
			Vector3 Center(WeaponPickup entity)
			{
				var points = entity.GetChildren().OfType<CollisionShape3D>().SelectMany(c => ((ConvexPolygonShape3D)c.Shape).Points).ToArray();
				var bounds = new Aabb(points[0], Vector3.Zero);
				foreach (var point in points) bounds = bounds.Expand(point);
				return entity.GlobalTransform * bounds.GetCenter();
			}
			deathGrenade.GlobalTransform = Transform3D.Identity;
			deathGrenade.GlobalPosition = (other.GlobalPosition + Vector3.Up * (SourceMovement.DuckHeight / 2)).Lerp(Center(deathRifle), .85f) - Center(deathGrenade);
			ClearSlot(other, WeaponSlot.Primary);
			var pileWall = new StaticBody3D { Position = new(other.GlobalPosition.X, 1, Mathf.Lerp(other.GlobalPosition.Z, Center(deathRifle).Z, .95f)) };
			pileWall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(2, 2, .01f) } }); AddChild(pileWall);
			await Frames(2);
			Check(!deathRifle.TryPickup(other), "ignoring another death drop still checks the wall behind it");
			pileWall.QueueFree();
			await Frames(2);
			Check(deathRifle.TryPickup(other), "another death drop must not obstruct picking up a dead player's rifle");
			Check(!deathGrenade.Taken, "full grenade inventory leaves the other death drop on the floor");
			player.Respawn();
			ClearSlot(player, WeaponSlot.Primary);
			var respawnPickup = WeaponPickup.Spawn(this, "weapon_scout", new(Basis.Identity, player.GlobalPosition + Vector3.Up));
			await Frames(3);
			Check(!GodotObject.IsInstanceValid(respawnPickup) || respawnPickup.Taken, "respawned players automatically collect a weapon in a free slot");
			foreach (var mode in new[] { "deathmatch", "teamdeathmatch" })
			{
				RoundState.Instance!.BeginMatch(mode);
				player.Respawn(); player.EndSpawnProtection();
				var entitiesBefore = GetTree().GetNodesInGroup("source_weapon_pickups").Count;
				var weaponsBefore = player.Inventory.Weapons.Count;
				player.TakeDamage(100);
				Check(!player.IsAlive && GetTree().GetNodesInGroup("source_weapon_pickups").Count == entitiesBefore
					&& player.Inventory.Weapons.Count == weaponsBefore, mode + " death creates no weapon pickups");
			}
			RoundState.Instance!.BeginMatch("sandbox");
			player.Respawn(); player.TakeDamage(100);
			Check(!player.Inventory.Weapons.Any(w => w.Slot == WeaponSlot.Primary)
				&& player.Inventory.Weapons.Any(w => w.Slot == WeaponSlot.Melee), "custom mode drops guns and keeps the knife");
			await Frames();
			var mapProp = new RigidBody3D { Name = "prop_physics", Position = new(30, 2, 0) };
			mapProp.AddChild(new CollisionShape3D { Shape = new BoxShape3D() }); AddChild(mapProp);
			var trash = WeaponPickup.Spawn(this, "weapon_ak47", new(Basis.Identity, new(-10, 2, 0)));
			var inventory = player.Inventory.Weapons.ToArray();
			var messages = new System.Collections.Generic.List<string>();
			void Cleanup(string command) => Contrapoint.UI.ConsoleCommands.Execute(command, player.Movement, (text, _) => messages.Add(text));
			Cleanup("cleanup invalid");
			Check(!trash.IsQueuedForDeletion(), "invalid cleanup arguments do not delete pickups");
			Cleanup("sv_cheats 0; cleanup; cleanup");
			Check(trash.IsQueuedForDeletion() && messages.Last().StartsWith("Removed 0 "), "cleanup works without cheats and skips pending deletions");
			await Frames();
			Check(!GodotObject.IsInstanceValid(trash) && GetTree().GetNodesInGroup("source_weapon_pickups").Count == 0,
				"cleanup removes world pickups");
			Check(GodotObject.IsInstanceValid(mapProp) && GodotObject.IsInstanceValid(floor)
				&& player.Inventory.Weapons.SequenceEqual(inventory), "cleanup preserves map props, floor and inventory");
			foreach (var name in WeaponPickup.Classes)
			{
				var entity = WeaponPickup.Spawn(this, name, new(Basis.Identity, new(-10, 2, 0)));
				var shapes = entity.GetChildren().OfType<CollisionShape3D>().ToArray();
				Check(shapes.Length > 0, name + " has an original collision solid");
				var points = shapes.SelectMany(c => ((ConvexPolygonShape3D)c.Shape).Points.Select(p => c.Transform * p)).ToArray();
				var hullBounds = new Aabb(points[0], Vector3.Zero);
				foreach (var point in points) hullBounds = hullBounds.Expand(point);
				var meshes = entity.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().ToArray();
				var modelBounds = entity.GlobalTransform.AffineInverse() * meshes[0].GlobalTransform * meshes[0].GetAabb();
				foreach (var mesh in meshes.Skip(1)) modelBounds = modelBounds.Merge(entity.GlobalTransform.AffineInverse() * mesh.GlobalTransform * mesh.GetAabb());
				Check(hullBounds.GetCenter().DistanceTo(modelBounds.GetCenter()) < .08f
					&& (hullBounds.Size - modelBounds.Size).Length() < .18f, name + " PHY and GLB share the same basis and bounds");
				entity.QueueFree();
			}
			await ConsolePickupAfterRespawn();
			foreach (var child in GetChildren()) child.QueueFree();
			await Frames(2);
			GD.Print($"WEAPON_PICKUP_CHECKS checks={_checks} failures=0"); GetTree().Quit();
		}
		catch (Exception error) { GD.PushError(error.ToString()); GD.Print($"WEAPON_PICKUP_CHECKS checks={_checks} failures=1"); GetTree().Quit(1); }
	}
}
