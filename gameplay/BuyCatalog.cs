using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Contrapoint.Players;
using Contrapoint.Weapons;

namespace Contrapoint.Gameplay;

public static class BuyCatalog
{
	public sealed record Item(string Id, string Card, WeaponSlot Slot, bool Equipment = false);
	public static IReadOnlyList<Item> Items { get; } = new Item[] {
		new("usp", "usp45", WeaponSlot.Secondary), new("deagle", "deserteagle", WeaponSlot.Secondary),
		new("ak47", "ak47", WeaponSlot.Primary), new("m4a1", "m4a1", WeaponSlot.Primary),
		new("scout", "scout", WeaponSlot.Primary), new("awp", "awp", WeaponSlot.Primary),
		new("glock", "glock18", WeaponSlot.Secondary), new("p228", "p228", WeaponSlot.Secondary),
		new("elite", "elites", WeaponSlot.Secondary), new("fiveseven", "fiveseven", WeaponSlot.Secondary),
		new("aug", "aug", WeaponSlot.Primary), new("famas", "famas", WeaponSlot.Primary),
		new("g3sg1", "g3sg1", WeaponSlot.Primary), new("galil", "galil", WeaponSlot.Primary),
		new("m249", "m249", WeaponSlot.Primary), new("m3", "m3", WeaponSlot.Primary),
		new("mac10", "mac10", WeaponSlot.Primary), new("mp5navy", "mp5", WeaponSlot.Primary),
		new("p90", "p90", WeaponSlot.Primary), new("sg550", "sg550", WeaponSlot.Primary),
		new("sg552", "sg552", WeaponSlot.Primary), new("tmp", "tmp", WeaponSlot.Primary),
		new("ump45", "ump45", WeaponSlot.Primary), new("xm1014", "xm1014", WeaponSlot.Primary),
		new("vest", "kevlar", WeaponSlot.Objective, true), new("vesthelm", "kevlar_helmet", WeaponSlot.Objective, true),
		new("defuser", "defuser", WeaponSlot.Objective, true),
		new("flashbang", "flashbang", WeaponSlot.Grenade), new("hegrenade", "hegrenade", WeaponSlot.Grenade),
		new("smokegrenade", "smokegrenade", WeaponSlot.Grenade)
	};
	private static readonly Dictionary<string, int> Prices = LoadPrices();
	private static Dictionary<string, int> LoadPrices()
	{
		using var data = JsonDocument.Parse(FileAccess.GetFileAsString("res://assets/ui/css/buy/menus.json"));
		return data.RootElement.GetProperty("prices").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32());
	}
	public static Item? Find(string id) => Items.FirstOrDefault(i => i.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
	public static bool CanOpen(Player player) => player.IsAlive && !player.PickingClass && !player.AwaitingInitialClass
		&& player.Team is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist && RoundState.Instance?.RestartPending != true
		&& (!DefuseRules.Active || DefuseState.Instance?.CanBuy(player) == true);
	public static int Price(Player player, Item item)
	{
		if (DeathmatchRules.Active) return 0;
		if (item.Id == "defuser") return 200;
		if (item.Id == "vesthelm" && player.Armor == 100 && !player.HasHelmet) return 350;
		if (item.Id == "vesthelm" && player.HasHelmet) return 650;
		return Prices[item.Id];
	}
	public static string? Unavailable(Player player, Item item)
	{
		if (!CanOpen(player)) return "Buying is only available while alive.";
		if (item.Id == "defuser" && (!DefuseRules.Active || player.Team != PlayerTeam.CounterTerrorist)) return "Only Counter-Terrorists can buy a defusal kit on a bomb map.";
		if (item.Id == "defuser" && player.HasDefuser) return "You already have a defusal kit.";
		if (DefuseRules.Active)
		{
			var ctWeapon = item.Id is "usp" or "fiveseven" or "m4a1" or "aug" or "famas" or "sg550" or "tmp";
			var tWeapon = item.Id is "glock" or "elite" or "ak47" or "sg552" or "galil" or "g3sg1" or "mac10";
			if (ctWeapon && player.Team != PlayerTeam.CounterTerrorist || tWeapon && player.Team != PlayerTeam.Terrorist) return "This weapon is unavailable for your team.";
		}
		if (player.ActiveWeapon?.CanHolster == false || player.ActiveWeapon is GrenadeView { PinPulled: true }) return "Finish the current weapon action first.";
		if (DeathmatchRules.Active && item.Slot == WeaponSlot.Grenade) return "Grenades are disabled in Deathmatch.";
		if (item.Equipment)
		{
			if (item.Id != "defuser" && player.Armor == 100 && (item.Id == "vest" || player.HasHelmet)) return "You already have this equipment.";
		}
		else
		{
			var owned = player.Inventory.Weapons.FirstOrDefault(w => WeaponPickup.ClassOf(w) == "weapon_" + item.Id);
			if (owned is GrenadeView grenade)
			{ if (grenade.Ammo >= (item.Id == "flashbang" ? 2 : 1)) return "You cannot carry any more."; }
			else if (owned != null) return "You already have this weapon.";
		}
		return player.Money < Price(player, item) ? "You have insufficient funds!" : null;
	}
}
