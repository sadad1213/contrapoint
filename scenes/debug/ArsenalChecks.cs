using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.Gameplay;
using Contrapoint.Players;
using Contrapoint.Weapons;

public partial class ArsenalChecks : Node3D
{
	private int _checks, _failures;
	private Player _player = null!;
	private void Check(bool condition, string message)
	{
		_checks++;
		if (condition) return;
		_failures++; GD.PushError(message);
	}
	private static void Step(FirearmView gun, double seconds)
	{
		while (seconds > .000001) { var dt = Math.Min(seconds, 1.0 / 144); gun._Process(dt); seconds -= dt; }
	}
	private static void Tap(FirearmView gun, string command = "attack")
	{ gun.HandleCommand(command, true); gun.HandleCommand(command, false); }
	public override async void _Ready()
	{
		try { await Run(); } catch (Exception error) { _failures++; GD.PushError(error.ToString()); }
		foreach (var child in GetChildren()) child.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await Task.Delay(100);
		GD.Print($"ARSENAL_CHECKS checks={_checks} failures={_failures}");
		GetTree().Quit(_failures == 0 ? 0 : 1);
	}
	private async Task Run()
	{
		AudioServer.SetBusMute(0, true);
		var floor = new StaticBody3D { Position = new(0, -.5f, 0) };
		floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(100, 1, 100) } }); AddChild(floor);
		_player = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		_player.LocalInput = false; _player.DebugControls = false; _player.Team = PlayerTeam.Terrorist; AddChild(_player);
		_player.Combat.SetPhysicsProcess(false); _player.Recoil.SetPhysicsProcess(false);
		foreach (var weapon in _player.Inventory.Weapons) weapon.SetProcess(false);
		for (var i = 0; i < 12; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			_player.Simulate(new(Vector2.Zero, false, false, false), 1f / 66);
		}
		var capacities = new[] { 30, 7, 12, 30, 10, 10, 30, 30, 25, 20, 20, 35, 20, 100, 8, 30, 30, 13, 50, 30, 30, 30, 25, 7 };
		foreach (var kind in Enum.GetValues<FirearmView.FirearmKind>())
		{
			var id = kind.ToString().ToLowerInvariant();
			var gun = _player.Inventory.Weapons.OfType<FirearmView>().FirstOrDefault(g => g.Kind == kind)
				?? (FirearmView)_player.Inventory.AddWeapon(GD.Load<PackedScene>($"res://weapons/{id}_view.tscn"), false);
			gun.SetProcess(false); gun.Sounds.Enabled = true;
			_player.Inventory.SetReserveAmmo(gun.AmmoType, gun.ReserveCapacity, gun.ReserveCapacity);
			Check(gun.MagazineCapacity == capacities[(int)kind], id + " original CSS magazine");
			Check(BuyCatalog.Find(id) != null && BuyCatalog.Price(_player, BuyCatalog.Find(id)!) > 0, id + " buy entry/price");
			Check(WeaponPickup.IsSupported("weapon_" + id), id + " original PHY pickup");
			_player.Inventory.Equip(gun); Step(gun, 3);
			_player.Body.EnsurePose();
			Check(_player.Body.UpperClip.EndsWith(gun.PlayerAnimationExtension), id + " authored third-person pose");
			var animation = gun.NetworkAnimation;
			Check(animation.GetAnimationList().Length >= 4, id + " imported animations");
			Check(gun.GetNode<Node3D>("WeaponSway/Model").Scale.X == (kind is FirearmView.FirearmKind.Famas or FirearmView.FirearmKind.Galil or FirearmView.FirearmKind.M249 ? 1 : -1),
				id + " respects original BuiltRightHanded flag");
			foreach (MeshInstance3D mesh in gun.FindChildren("*", "MeshInstance3D", true, false))
			{
				Check(mesh.Mesh.GetSurfaceCount() > 0 && mesh.Skin != null, id + " skinned geometry");
				for (var i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
					Check(mesh.GetActiveMaterial(i) is ShaderMaterial || mesh.GetActiveMaterial(i) is StandardMaterial3D { AlbedoTexture: not null }, id + " resolved texture");
			}
			var before = gun.ShotsFired; var soundCount = gun.Sounds.SoundsPlayed;
			Tap(gun);
			Check(gun.ShotsFired == before + 1 && gun.MagazineAmmo == gun.MagazineCapacity - 1, id + " one round per shot");
			Check(gun.Sounds.SoundsPlayed > soundCount, id + " original shot sound plays");
			var penalty = gun.RifleAccuracy.Penalty;
			Tap(gun);
			Check(gun.ShotsFired == before + 1 && gun.RifleAccuracy.Penalty == penalty, id + " cooldown rejects extra shots");
			Step(gun, Math.Max(2, gun.FireInterval));
			Check(gun.RifleAccuracy.Penalty < penalty, id + " CSSO accuracy recovers");
			gun.RestoreMagazine(gun.MagazineCapacity - 2);
			var reserve = gun.ReserveAmmo;
			gun.HandleCommand("reload", true); _player.Body.EnsurePose(); Step(gun, 8);
			Check(gun.MagazineAmmo == gun.MagazineCapacity && gun.ReserveAmmo == reserve - 2, id + " reload conserves ammunition");
			Check(!gun.Busy, id + " returns from reload");
			_player.Combat.Reset();
		}
		Check(_player.Inventory.Weapons.OfType<FirearmView>().Count() == 24, "Complete 24-firearm CSS arsenal");
		foreach (var kind in new[] { FirearmView.FirearmKind.MP5Navy, FirearmView.FirearmKind.P90, FirearmView.FirearmKind.UMP45 })
			foreach (var fps in new[] { 30, 66, 144 })
			{
				var gun = Gun(kind); _player.Inventory.Equip(gun); gun.ResetAmmunition(); Step(gun, 3);
				gun.Sounds.Enabled = false; var before = gun.ShotsFired;
				gun.HandleCommand("attack", true);
				for (double time = 0; time < .99;)
				{ var dt = Math.Min(1.0 / fps, .99 - time); gun._Process(dt); time += dt; }
				gun.HandleCommand("attack", false);
				Check(gun.ShotsFired - before == 1 + (int)(.99 / gun.FireInterval), kind + " CSS cadence at " + fps + " FPS");
				Step(gun, 3); _player.Combat.Reset();
			}
		var mp5 = Gun(FirearmView.FirearmKind.MP5Navy);
		Check(mp5.AmmoType == Gun(FirearmView.FirearmKind.Glock).AmmoType && mp5.ReserveCapacity == 120, "CSS shared 9mm reserve");
		Check(Mathf.Abs(CssoRifleBallistics.Data(mp5).Stand - .01f) < .000001f
			&& Mathf.Abs(CssoRifleBallistics.Data(mp5).Spread - .0006f) < .000001f, "Installed CSSO MP5-SD accuracy constants");
		var elite = Gun(FirearmView.FirearmKind.Elite);
		_player.Inventory.Equip(elite); elite.ResetAmmunition(); Step(elite, 3); Tap(elite);
		Check(elite.NetworkAnimation.CurrentAnimation == "shoot_left1", "Dual Berettas use clip parity for the left pistol after reloading");
		Step(elite, .2); Tap(elite);
		Check(elite.NetworkAnimation.CurrentAnimation == "shoot_right1", "Dual Berettas alternate the right pistol");
		Step(elite, .2); elite.RestoreMagazine(1); Tap(elite);
		Check(elite.NetworkAnimation.CurrentAnimation == "shoot_rightlast", "Dual Berettas use the authored last-round clip");
		var empty = Gun(FirearmView.FirearmKind.P228);
		_player.Inventory.Equip(empty); Step(empty, 3); empty.Sounds.Enabled = true; empty.RestoreMagazine(1);
		_player.Inventory.SetReserveAmmo(empty.AmmoType, 0, empty.ReserveCapacity); Tap(empty); Step(empty, 2);
		Check(empty.NetworkAnimation.AssignedAnimation == "shoot_empty", "Empty pistol retains the original empty-slide pose");
		var emptyShots = empty.ShotsFired; Tap(empty);
		Check(empty.ShotsFired == emptyShots && empty.Sounds.LastSound == "default.clipempty_pistol", "Dry fire uses the CSS empty sound without spawning a bullet");
		_player.Combat.Reset();
		foreach (var kind in new[] { FirearmView.FirearmKind.Glock, FirearmView.FirearmKind.Famas })
		{
			var gun = Gun(kind); _player.Inventory.Equip(gun); gun.ResetAmmunition(); Step(gun, 3);
			Tap(gun, "attack2"); var before = gun.ShotsFired; Tap(gun);
			Step(gun, kind == FirearmView.FirearmKind.Glock ? .055 : .07);
			Check(gun.ShotsFired == before + 1, kind + " respects CSS burst spacing");
			Step(gun, .14);
			Check(gun.BurstMode && gun.ShotsFired == before + 3 && gun.MagazineAmmo == gun.MagazineCapacity - 3, kind + " three-round burst");
			Step(gun, 1); gun.RestoreMagazine(1); before = gun.ShotsFired; Tap(gun); Step(gun, .2);
			Check(gun.ShotsFired == before + 1 && gun.MagazineAmmo == 0, kind + " burst cannot overdraw last round");
			gun.Holster(); Step(gun, 1); gun.Equip(); Step(gun, 3);
			Check(gun.BurstMode, kind + " mode survives switching");
			gun.ApplyNetworkBurst(false); _player.Combat.Reset();
		}
		foreach (var kind in new[] { FirearmView.FirearmKind.M3, FirearmView.FirearmKind.XM1014 })
		{
			var gun = Gun(kind); _player.Inventory.Equip(gun); gun.ResetAmmunition(); Step(gun, 3);
			var before = _player.Combat.BulletsResolved; Tap(gun); _player.Combat._PhysicsProcess(1f / 66);
			Check(_player.Combat.BulletsResolved - before == (kind == FirearmView.FirearmKind.M3 ? 9 : 6), kind + " complete CSS pellet count");
			Step(gun, 2); gun.RestoreMagazine(gun.MagazineCapacity - 3); var reserve = gun.ReserveAmmo;
			gun.HandleCommand("reload", true); Step(gun, 1.02);
			Check(gun.MagazineAmmo == gun.MagazineCapacity - 2 && gun.ReserveAmmo == reserve - 1, kind + " one shell committed at 1s");
			gun.Holster(); Step(gun, 3);
			Check(gun.MagazineAmmo == gun.MagazineCapacity - 2, kind + " holster cancels uncommitted shells");
			gun.Equip(); Step(gun, 3); _player.Combat.Reset();
			gun.HandleCommand("reload", true); Step(gun, .55);
			var shots = gun.ShotsFired; var shells = gun.MagazineAmmo; reserve = gun.ReserveAmmo;
			Tap(gun);
			Check(gun.ShotsFired == shots + 1 && gun.MagazineAmmo == shells - 1 && gun.ReserveAmmo == reserve && !gun.Busy,
				kind + " firing interrupts insertion without consuming an uncommitted shell");
			Step(gun, 2); _player.Combat.Reset();
		}
		foreach (var kind in new[] { FirearmView.FirearmKind.AUG, FirearmView.FirearmKind.SG552, FirearmView.FirearmKind.G3SG1, FirearmView.FirearmKind.SG550 })
		{
			var gun = Gun(kind); _player.Inventory.Equip(gun); Step(gun, 3); Tap(gun, "attack2");
			Check(gun.ZoomLevel == 1 && gun.SourceZoomFov < 90, kind + " secondary zoom");
			if (!gun.IsSniper) Check(Math.Abs(gun.FireInterval - .135) < .000001, kind + " original CSS scoped fire interval");
			gun.Holster(); Check(gun.ZoomLevel == 0, kind + " holster restores FOV");
		}
		// Reliable inventory serialization accepts every new class and preserves modes/ammo.
		var contents = _player.Inventory.CaptureContents();
		var replica = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		replica.LocalInput = false; replica.DebugControls = false; replica.Position = new(10, 0, 0); AddChild(replica);
		replica.Inventory.ApplyContents(100, contents, "MP5Navy");
		Check(replica.Inventory.Weapons.OfType<FirearmView>().Count() == 24 && replica.ActiveWeapon is FirearmView { Kind: FirearmView.FirearmKind.MP5Navy }, "Complete arsenal network inventory roundtrip");
		foreach (var weapon in replica.Inventory.Weapons) weapon.SetProcess(false);
		foreach (var kind in Enum.GetValues<FirearmView.FirearmKind>().Skip(6))
		{
			var id = kind.ToString().ToLowerInvariant();
			var pickup = WeaponPickup.Spawn(this, "weapon_" + id, new(Basis.Identity, new(20, 2, 0)), magazine: 3);
			Check(pickup.GetChildren().OfType<CollisionShape3D>().Any() && pickup.Mass > 0, id + " world geometry/physics");
			Check(pickup.Slot == Gun(kind).Slot, id + " pickup slot matches inventory");
			pickup.QueueFree();
		}
	}
	private FirearmView Gun(FirearmView.FirearmKind kind) => _player.Inventory.Weapons.OfType<FirearmView>().First(g => g.Kind == kind);
}
