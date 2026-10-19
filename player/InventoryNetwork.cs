using Godot;
using System.Linq;
using Contrapoint.Weapons;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

namespace Contrapoint.Players;

public partial class PlayerInventory
{
	public GArray CaptureContents()
	{
		var contents = new GArray();
		foreach (var weapon in _weapons)
			contents.Add(new GDictionary { ["class"] = WeaponPickup.ClassOf(weapon), ["name"] = weapon.Name.ToString(),
				["magazine"] = (weapon as FirearmView)?.MagazineAmmo ?? -1,
				["reserve"] = (weapon as FirearmView)?.ReserveAmmo ?? 0,
				["silenced"] = (weapon as FirearmView)?.Silenced ?? false,
				["burst"] = (weapon as FirearmView)?.BurstMode ?? false, ["grenades"] = (weapon as GrenadeView)?.Ammo ?? 0 });
		return contents;
	}

	// Called only by authoritative reliable inventory messages, never a client's snapshot.
	public void ApplyContents(int revision, GArray contents, string selected)
	{
		if (revision < NetworkRevision) return;
		NetworkRevision = revision;
		var names = contents.Select(v => v.AsGodotDictionary()["name"].AsString()).ToHashSet();
		foreach (var weapon in _weapons.Where(w => !names.Contains(w.Name.ToString())).ToArray()) RemoveWeapon(weapon, selectReplacement: false);
		foreach (var value in contents)
		{
			var item = value.AsGodotDictionary();
			var name = item["class"].AsString();
			if (name is not ("weapon_knife" or "weapon_c4") && !WeaponPickup.IsSupported(name)) continue;
			var weapon = _weapons.FirstOrDefault(w => w.Name.ToString() == item["name"].AsString());
			if (weapon == null)
			{
				weapon = AddWeapon(GD.Load<PackedScene>(WeaponPickup.ViewPath(name)), equip: false, grantReserve: false);
				weapon.Name = item["name"].AsString();
			}
			if (weapon is FirearmView gun)
			{
				gun.RestoreMagazine(item["magazine"].AsInt32()); gun.ApplyNetworkSilencer(item["silenced"].AsBool());
				if (item.TryGetValue("burst", out var burst)) gun.ApplyNetworkBurst(burst.AsBool());
				SetReserveAmmo(gun.AmmoType, item["reserve"].AsInt32(), gun.ReserveCapacity);
			}
			if (weapon is GrenadeView grenade) grenade.RestoreAmmo(item["grenades"].AsInt32());
		}
		var active = _weapons.FirstOrDefault(w => w.Name.ToString() == selected);
		if (active != null && active != ActiveWeapon)
		{
			ActiveWeapon?.Holster(); ActiveWeapon = active;
			if (Enabled || Wielder?.IsAlive == true) { active.Equip(); active.SetInputEnabled(Enabled); }
			EmitSignal(SignalName.WeaponChanged, active);
		}
	}
}
