using System;
using Contrapoint.Gameplay;
using Contrapoint.Players;
using Contrapoint.Weapons;

namespace Contrapoint.UI;

// One read-only boundary between gameplay state and presentation.
public readonly record struct HudSnapshot(int Health, int Armor, bool Helmet, int Money,
	int Seconds, bool TimerRunning, bool HasAmmo, bool UsesMagazine, int Magazine, int Reserve, bool CanBuy = false)
{
	public string Clock => $"{Seconds / 60}:{Seconds % 60:00}";
	public static HudSnapshot Capture(Player player)
	{
		var gun = player.ActiveWeapon as FirearmView;
		var grenade = player.ActiveWeapon as GrenadeView;
		return new(Math.Max(0, player.Health), Math.Clamp(player.Armor, 0, 100), player.HasHelmet,
			Math.Max(0, player.Money), RoundState.Instance?.DisplaySeconds ?? 0,
			RoundState.Instance?.TimerRunning ?? false, gun != null || grenade != null, gun != null,
			gun?.MagazineAmmo ?? grenade?.Ammo ?? 0, gun?.ReserveAmmo ?? 0, BuyCatalog.CanOpen(player));
	}
}
