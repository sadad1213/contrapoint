using System;
using System.Linq;
using Godot;
using Contrapoint.Weapons;
using Contrapoint.Network;

namespace Contrapoint.UI;

public static partial class ConsoleCommands
{
	private static void RegisterWeapons()
	{
		RegisterVariable("cl_knife_style", 0, 0, 1, m => Owner(m).Inventory.KnifeStyle,
			(m, value) => Owner(m).Inventory.KnifeStyle = (int)value,
			"First-person knife style: 0 = current karambit, 1 = CSSO butterfly with T Phoenix arms.", integer: true);
		Register("buymenu", (_, m, _) => { var menu = Owner(m).BuyMenu; if (menu?.IsOpen == true) menu.Close(); else menu?.Open(); });
		Register("buyequip", (_, m, _) => Owner(m).BuyMenu?.Open("equipment"));
		Register("buy", (args, m, print) =>
		{
			if (args.Count != 2) { print("Usage: buy <item>", true); return; }
			var error = Owner(m).Buy(args[1]);
			if (error.Length > 0) print(error, true);
		}, () => Contrapoint.Gameplay.BuyCatalog.Items.Select(item => item.Id));
		Register("autobuy", (_, m, _) => Owner(m).AutoBuy());
		Register("rebuy", (_, m, _) => Owner(m).Rebuy());
		Register("drop", (_, m, _) => Owner(m).DropActiveWeapon());
		Register("cleanup", (args, m, print) =>
		{
			if (args.Count != 1) { print("Usage: cleanup", true); return; }
			var count = 0;
			// Only world pickups: map props and equipped weapons are never candidates.
			foreach (var pickup in Owner(m).GetTree().GetNodesInGroup("source_weapon_pickups").OfType<WeaponPickup>())
			{
				if (pickup.Taken || pickup.NetworkReplica || pickup.IsQueuedForDeletion()) continue;
				Contrapoint.Network.LanSession.Instance?.RemoveWeaponPickup(pickup);
				pickup.Visible = false;
				pickup.QueueFree();
				count++;
			}
			print($"Removed {count} world weapon pickups. Map props preserved.", false);
		}, serverOnly: true);
		RegisterVariable("cl_autowepswitch", 1, 0, 1, m => Owner(m).Inventory.AutoSwitch ? 1 : 0,
			(m, value) => Owner(m).Inventory.AutoSwitch = value != 0, "Switch to a better weapon when picking it up.", integer: true);
		Register("ent_create", (args, m, print) =>
		{
			if (args.Count != 2 || !WeaponPickup.IsSupported(args[1]))
			{ print("Usage: ent_create <" + string.Join('|', WeaponPickup.Classes) + ">", true); return; }
			var player = Owner(m);
			if (LanSession.Instance?.RequestPlayerCheat(player, "ent_create", args[1], print) == true) return;
			player.CreateWeaponEntity(args[1]);
		}, () => WeaponPickup.Classes, cheat: true);
		Register("give", (args, m, print) =>
		{
			if (args.Count != 2 || !WeaponPickup.IsSupported(args[1]))
			{ print("Usage: give weapon_<name>", true); return; }
			var player = Owner(m);
			if (!player.IsAlive) return;
			var pickup = player.CreateWeaponEntity(args[1]);
			if (pickup == null) return;
			// CSS Weapon_Equip drops the previous gun in this slot for a give.
			if (pickup.Slot != WeaponSlot.Grenade)
				foreach (var old in player.Inventory.Weapons.Where(w => w.Slot == pickup.Slot).ToArray()) player.Inventory.DropWeapon(old);
			pickup.GlobalPosition = player.GlobalPosition + Vector3.Up;
			pickup.TryPickup(player);
		}, () => WeaponPickup.Classes, cheat: true, serverOnly: true);
	}
}
