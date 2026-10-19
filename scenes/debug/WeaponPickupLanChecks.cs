using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.Network;
using Contrapoint.Players;
using Contrapoint.Weapons;

public partial class WeaponPickupLanChecks : Node
{
	private LanSession Net => LanSession.Instance!;
	private Player Player => Net.LocalPlayer!;
	private string RunId => OS.GetCmdlineUserArgs().First(a => a.StartsWith("--weapon-run="))[13..];
	private string WeaponId => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--weapon-kind="))?[14..] ?? "ak47";
	private bool IsTestGun(FirearmView gun) => gun.Kind.ToString().Equals(WeaponId, StringComparison.OrdinalIgnoreCase);
	private string Root => "user://weapon_pickup_checks/" + RunId + "/";
	private void Write(string name) { using var file = FileAccess.Open(Root + name, FileAccess.ModeFlags.Write); file.StoreString("ok"); }
	private async Task Until(Func<bool> condition, string message)
	{
		var end = Time.GetTicksMsec() + 45000;
		while (!condition())
		{
			if (Time.GetTicksMsec() > end) throw new TimeoutException(message);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
	}
	private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
	private WeaponPickup[] Pickups => GetTree().GetNodesInGroup("source_weapon_pickups").OfType<WeaponPickup>()
		.Where(p => !p.IsQueuedForDeletion()).ToArray();
	public override async void _Ready()
	{
		var host = OS.GetCmdlineUserArgs().Contains("--weapon-host");
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		Reparent(GetTree().Root); GetTree().CurrentScene = null;
		try
		{
			DirAccess.MakeDirRecursiveAbsolute(Root);
			if (host)
			{
				GetTree().ChangeSceneToFile("res://scenes/maps/dust2.tscn");
				await Until(() => Net.LocalPlayer?.TeamMenu?.IsOpen == true, "host map load");
				Player.JoinTeam(2); Player.JoinClass(1); Player.SetPhysicsProcess(false);
				Player.SourceTeleport(new(100, 100, 100), 0, 0);
				Net.Open((line, _) => GD.Print(line)); Write("ready");
				await Until(() => Net.Players.Count == 2 && Net.Players.Values.Any(p => !p.LocalInput && p.IsAlive), "client spawns");
				var remote = Net.Players.Values.First(p => !p.LocalInput);
				remote.SourceTeleport(new(110, 100, 100), 0, 0);
				foreach (var old in remote.Inventory.Weapons.Where(w => w.Slot == WeaponSlot.Primary).ToArray()) remote.Inventory.RemoveWeapon(old, false);
				remote.Inventory.AddWeapon(GD.Load<PackedScene>(WeaponPickup.ViewPath("weapon_" + WeaponId)));
				Net.BroadcastInventory(remote);
				await Until(() => FileAccess.FileExists(Root + "client_setup"), "client ready to drop");
				Write("drop_now");
				await Until(() => Pickups.Any(p => p.WeaponClass == "weapon_" + WeaponId && p.PreviousOwner == remote), "server receives client drop");
				var drop = Pickups.First(p => p.WeaponClass == "weapon_" + WeaponId && p.PreviousOwner == remote);
				drop.Freeze = true;
				Check(drop.Magazine == Math.Min(13, FirearmData.For(Enum.Parse<FirearmView.FirearmKind>(WeaponId, true)).Magazine) && drop.ExtraAmmo == 27, "server keeps client ammo on drop");
				Check(drop.BurstMode == (WeaponId == "famas"), "server keeps burst mode on drop");
				Check(!remote.Inventory.Weapons.Any(w => w.Slot == WeaponSlot.Primary), "host removed remote rifle");
				foreach (var old in Player.Inventory.Weapons.Where(w => w.Slot == WeaponSlot.Primary).ToArray()) Player.Inventory.DropWeapon(old);
				drop.GlobalPosition = Player.GlobalPosition + Vector3.Up;
				Check(drop.TryPickup(Player), "host can acquire client's gun");
				Write("host_picked");
				await Until(() => FileAccess.FileExists(Root + "client_saw_pickup"), "client sees host pickup");
				var hostGun = (FirearmView)Player.ActiveWeapon!;
				Check(hostGun.MagazineAmmo == Math.Min(13, hostGun.MagazineCapacity) && hostGun.ReserveAmmo == 27, "host pickup restored client ammo");
				Check(hostGun.BurstMode == (WeaponId == "famas"), "host pickup restores firing mode");
				var redrop = Player.Inventory.DropWeapon(hostGun)!; redrop.Freeze = true;
				redrop.GlobalPosition = remote.GlobalPosition + Vector3.Up;
				Write("redropped");
				await Until(() => FileAccess.FileExists(Root + "client_picked"), "client auto pickup of host drop");
				Check(remote.Inventory.Weapons.OfType<FirearmView>().Any(w => IsTestGun(w) && w.MagazineAmmo == Math.Min(13, w.MagazineCapacity) && w.ReserveAmmo == 27), "host observes client's restored inventory");
				var received = remote.Combat.BulletsResolved;
				Write("fire_now");
				var remoteGun = remote.Inventory.Weapons.OfType<FirearmView>().First(IsTestGun);
				var pellets = remoteGun.PelletCount * (remoteGun.BurstMode ? 3 : 1);
				await Until(() => remote.Combat.BulletsResolved >= received + pellets, "complete client shot reaches server");
				Check(remote.Combat.BulletsResolved == received + pellets, "server resolves every pellet once");
				Write("done");
				await Until(() => FileAccess.FileExists(Root + "client_passed"), "client finish");
			}
			else
			{
				await Until(() => FileAccess.FileExists(Root + "ready"), "host ready");
				Net.Connect("127.0.0.1:" + LanSession.Port, (line, _) => GD.Print(line));
				await Until(() => Net.WorldReady && Net.LocalPlayer?.TeamMenu?.IsOpen == true, "client join");
				Player.JoinTeam(2);
				await Until(() => Player.Team == PlayerTeam.Terrorist && Player.TeamMenu!.IsClassSelection, "client team");
				Player.JoinClass(1);
				await Until(() => Player.IsAlive && Player.GlobalPosition.Y > 90, "server teleport");
				Player.SetPhysicsProcess(false); Player.Inventory.EquipSlot(WeaponSlot.Primary);
				await Until(() => Player.Inventory.Weapons.OfType<FirearmView>().Any(IsTestGun), "new firearm inventory");
				Player.Inventory.Equip(Player.Inventory.Weapons.OfType<FirearmView>().First(IsTestGun));
				var gun = (FirearmView)Player.ActiveWeapon!;
				gun.RestoreMagazine(13); Player.Inventory.SetReserveAmmo(gun.AmmoType, 27, gun.ReserveCapacity);
				gun.ApplyNetworkBurst(WeaponId == "famas");
				Write("client_setup");
				await Until(() => FileAccess.FileExists(Root + "drop_now"), "drop signal");
				Player.DropActiveWeapon();
				await Until(() => !Player.Inventory.Weapons.Any(w => w.Slot == WeaponSlot.Primary), "reliable owner inventory removes dropped gun");
				await Until(() => FileAccess.FileExists(Root + "host_picked"), "host picked signal");
				await Until(() => Net.Players[1].ActiveWeapon is FirearmView gun && IsTestGun(gun) && gun.MagazineAmmo == Math.Min(13, gun.MagazineCapacity) && gun.ReserveAmmo == 27, "client sees host equipped client's gun");
				Check(Pickups.All(p => p.NetworkReplica && p.Freeze), "client pickups are server-controlled replicas");
				Write("client_saw_pickup");
				await Until(() => FileAccess.FileExists(Root + "redropped"), "host redropped signal");
				await Until(() => Player.Inventory.Weapons.OfType<FirearmView>().Any(w => IsTestGun(w) && w.MagazineAmmo == Math.Min(13, w.MagazineCapacity) && w.ReserveAmmo == 27), "client gets gun back with exact ammo");
				Write("client_picked");
				await Until(() => FileAccess.FileExists(Root + "fire_now"), "server ready for shot");
				gun = Player.Inventory.Weapons.OfType<FirearmView>().First(IsTestGun);
				Check(gun.BurstMode == (WeaponId == "famas"), "client recovers the same firing mode");
				Player.Inventory.Equip(gun); gun._Process(3);
				gun.HandleCommand("attack", true); gun.HandleCommand("attack", false);
				await Until(() => FileAccess.FileExists(Root + "done"), "host verified");
				Write("client_passed");
			}
			GD.Print($"WEAPON_PICKUP_LAN_CHECKS {(host ? "host" : "client")} weapon={WeaponId} failures=0"); Net.Stop(); GetTree().Quit();
		}
		catch (Exception error) { GD.PushError($"WEAPON_PICKUP_LAN_CHECKS {(host ? "host" : "client")}: {error}"); Net.Stop(); GetTree().Quit(1); }
	}
}
