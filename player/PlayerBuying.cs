using System.Linq;
using System.Collections.Generic;
using Contrapoint.Gameplay;
using Contrapoint.Network;
using Contrapoint.Weapons;

namespace Contrapoint.Players;

public partial class Player
{
	private string[] _rebuy = [];
	public string[] CaptureBuyPackage()
	{
		var items = new List<string>();
		foreach (var weapon in Inventory.Weapons)
		{
			var id = WeaponPickup.ClassOf(weapon)[7..];
			if (BuyCatalog.Find(id) == null) continue;
			var count = weapon is GrenadeView grenade ? grenade.Ammo : 1;
			for (var i = 0; i < count; i++) items.Add(id);
		}
		if (Armor > 0) items.Add(HasHelmet ? "vesthelm" : "vest");
		if (HasDefuser) items.Add("defuser");
		return items.ToArray();
	}
	public void BuyPackage(IEnumerable<string> items)
	{
		// A package describes the desired inventory, including grenade counts.
		// Buy only the missing grenades, rather than adding the whole count again.
		foreach (var group in items.ToArray().GroupBy(id => id))
		{
			var owned = Inventory.Weapons.OfType<GrenadeView>().FirstOrDefault(w => WeaponPickup.ClassOf(w) == "weapon_" + group.Key);
			var count = group.Count() - (owned?.Ammo ?? 0);
			for (var i = 0; i < count; i++) Buy(group.Key);
		}
	}
	public void Rebuy() => BuyPackage(_rebuy);
	public void AutoBuy()
	{
		if (!Inventory.Weapons.Any(w => w.Slot == WeaponSlot.Primary))
		{
			// Stock autobuy_default.txt: first affordable primary, then armor.
			var terrorist = Team == PlayerTeam.Terrorist;
			foreach (var id in new[] { terrorist ? "ak47" : "m4a1", terrorist ? "galil" : "famas", "p90", "mp5navy" })
				if (Buy(id).Length == 0) break;
		}
		if (Buy("vesthelm").Length > 0) Buy("vest");
	}
	public string Buy(string id)
	{
		var item = BuyCatalog.Find(id);
		if (item == null) return "This item is not available.";
		if (LanSession.Instance?.RequestBuy(this, item.Id) == true) return "";
		var error = BuyCatalog.Unavailable(this, item);
		if (error != null) return error;
		var price = BuyCatalog.Price(this, item);
		if (item.Equipment)
		{
			if (item.Id == "defuser") HasDefuser = true;
			else { Armor = 100; if (item.Id == "vesthelm") HasHelmet = true; }
		}
		else
		{
			var owned = Inventory.Weapons.FirstOrDefault(w => WeaponPickup.ClassOf(w) == "weapon_" + item.Id);
			if (owned is GrenadeView grenade) grenade.RestoreAmmo(grenade.Ammo + 1);
			else
			{
				// DM replaces the old firearm without leaving a pickup in the world.
				if (item.Slot is WeaponSlot.Primary or WeaponSlot.Secondary)
					foreach (var old in Inventory.Weapons.Where(w => w.Slot == item.Slot).ToArray())
					{
						if (DeathmatchRules.Active) Inventory.RemoveWeapon(old, selectReplacement: false);
						else Inventory.DropWeapon(old);
					}
				var weapon = Inventory.AddWeapon(PlayerSceneCache.Load(WeaponPickup.ViewPath("weapon_" + item.Id)), equip: false);
				if (weapon is GrenadeView addedGrenade) addedGrenade.RestoreAmmo(1);
				if (weapon is FirearmView gun) Inventory.SetReserveAmmo(gun.AmmoType, gun.ReserveCapacity, gun.ReserveCapacity);
				if (item.Slot is WeaponSlot.Primary or WeaponSlot.Secondary) Inventory.Equip(weapon, purchased: true);
			}
			if (DeathmatchRules.Active) Inventory.RememberSpawnLoadout();
		}
		Money -= price;
		_rebuy = CaptureBuyPackage();
		LanSession.Instance?.BroadcastInventory(this, pickupSound: true);
		LanSession.Instance?.BroadcastVitals(this);
		if (LocalInput) ActiveWeapon?.Sounds.PlayWave("items/itempickup.wav", relay: false);
		return "";
	}
}
