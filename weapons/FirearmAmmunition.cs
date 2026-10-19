using Godot;
using System;

namespace Contrapoint.Weapons;

public enum AmmunitionType { Nato762, Nato556, Acp45, Ae50, Magnum338, Nato556Box, Mm9, Sig357, Mm57, Buckshot }

public partial class FirearmView
{
	public int MagazineCapacity => FirearmData.For(Kind).Magazine;
	public AmmunitionType AmmoType => FirearmData.For(Kind).Ammo;
	public int ReserveCapacity => FirearmData.For(Kind).Reserve;
	public int MagazineAmmo { get; private set; }
	public int ReserveAmmo => Wielder?.Inventory.GetReserveAmmo(AmmoType) ?? _standaloneReserve;
	private int _standaloneReserve;
	public void RestoreMagazine(int amount) => MagazineAmmo = Math.Clamp(amount < 0 ? MagazineCapacity : amount, 0, MagazineCapacity);
	// Kill rewards never draw from reserve ammunition or overflow the weapon's clip.
	public void AddMagazineAmmo(int amount) => MagazineAmmo = Math.Clamp(MagazineAmmo + Math.Max(0, amount), 0, MagazineCapacity);

	public void ResetAmmunition()
	{
		MagazineAmmo = MagazineCapacity;
		_standaloneReserve = ReserveCapacity;
	}

	private void CompleteReload()
	{
		var amount = Math.Min(MagazineCapacity - MagazineAmmo, ReserveAmmo);
		if (Wielder is { } player) amount = player.Inventory.TakeReserveAmmo(AmmoType, amount);
		else _standaloneReserve -= amount;
		MagazineAmmo += amount;
	}

	private void LoadShell()
	{
		if (MagazineAmmo >= MagazineCapacity || ReserveAmmo == 0) return;
		if (Wielder is { } player) player.Inventory.TakeReserveAmmo(AmmoType, 1);
		else _standaloneReserve--;
		MagazineAmmo++;
	}
}
