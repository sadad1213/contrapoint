using Godot;
using System.Collections.Generic;
using System.Linq;
using Contrapoint.Network;
using Contrapoint.Weapons;

namespace Contrapoint.Players;

public partial class PlayerInventory : Node3D
{
	[Signal] public delegate void WeaponChangedEventHandler(WeaponView weapon);
	public WeaponView? ActiveWeapon { get; private set; }
	public IReadOnlyList<WeaponView> Weapons => _weapons.AsReadOnly();
	public Player? Wielder { get; private set; }
	public bool Enabled { get; private set; } = true;
	private readonly List<WeaponView> _weapons = [];
	private readonly Dictionary<WeaponSlot, WeaponView> _slotSelection = [];
	private WeaponView? _previous;
	private readonly Dictionary<AmmunitionType, int> _reserveAmmo = [];
	private string[] _spawnLoadout = [];
	public bool AutoSwitch { get; set; } = true;
	private int _knifeStyle;
	public int KnifeStyle
	{
		get => _knifeStyle;
		set
		{
			_knifeStyle = value;
			foreach (var knife in _weapons.OfType<KnifeView>()) knife.SetViewStyle(value);
		}
	}
	public int NetworkRevision { get; set; }
	internal WeaponView? FindWeapon(string name)
	{
		foreach (var weapon in _weapons)
		{
			using var weaponName = weapon.Name;
			if (weaponName.ToString() == name) return weapon;
		}
		return null;
	}
	public void SetReserveAmmo(AmmunitionType type, int amount, int capacity) => _reserveAmmo[type] = System.Math.Clamp(amount, 0, capacity);

	// The shared rig is also an arsenal preview. Playing maps use CSS slot limits.
	public void ConfigureSourceLoadout()
	{
		foreach (var slot in new[] { WeaponSlot.Primary, WeaponSlot.Secondary })
			foreach (var weapon in _weapons.Where(w => w.Slot == slot).Skip(1).ToArray()) RemoveWeapon(weapon);
		foreach (var type in _reserveAmmo.Keys.Where(type => !_weapons.OfType<FirearmView>().Any(w => w.AmmoType == type)).ToArray()) _reserveAmmo.Remove(type);
		_spawnLoadout = _weapons.Select(w => w.SceneFilePath).ToArray();
	}

	public void RestoreSpawnLoadout()
	{
		if (_spawnLoadout.Length == 0) return;
		foreach (var weapon in _weapons.ToArray()) RemoveWeapon(weapon, selectReplacement: false);
		foreach (var path in _spawnLoadout) AddWeapon(PlayerSceneCache.Load(path), equip: false);
		EquipSlot(WeaponSlot.Melee);
	}
	public void RememberSpawnLoadout() => _spawnLoadout = _weapons.Select(w => w.SceneFilePath).ToArray();

	public void ConfigureBuyLoadout()
	{
		if (Contrapoint.Gameplay.DeathmatchRules.Active) return;
		var pistol = Wielder?.Team == PlayerTeam.Terrorist ? "glock" : "usp";
		_spawnLoadout = _weapons.Where(w => w.Slot == WeaponSlot.Melee).Select(w => w.SceneFilePath)
			.Append("res://weapons/" + pistol + "_view.tscn").ToArray();
	}
	public int GetReserveAmmo(AmmunitionType type) => _reserveAmmo.GetValueOrDefault(type);
	public int TakeReserveAmmo(AmmunitionType type, int requested)
	{
		var amount = System.Math.Clamp(requested, 0, GetReserveAmmo(type));
		_reserveAmmo[type] = GetReserveAmmo(type) - amount;
		return amount;
	}
	public void ResetAmmunition()
	{
		_reserveAmmo.Clear();
		foreach (var weapon in _weapons)
			if (weapon is FirearmView gun)
			{
				gun.ResetAmmunition();
				if (gun.Equipped) gun.Equip();
				_reserveAmmo[gun.AmmoType] = gun.ReserveCapacity;
			}
	}

	public override void _Ready()
	{
		foreach (var child in GetChildren())
			if (child is WeaponView weapon) Register(weapon);
		EquipSlot(WeaponSlot.Melee);
		if (ActiveWeapon == null && _weapons.Count > 0) Equip(_weapons[0]);
		_previous = _weapons.Find(w => w.Slot == WeaponSlot.Primary && w != ActiveWeapon);
	}

	public void Bind(Player player)
	{
		Wielder = player;
		foreach (var weapon in _weapons) weapon.Wielder = player;
	}

	private void Register(WeaponView weapon)
	{
		if (_weapons.Contains(weapon)) return;
		_weapons.Add(weapon);
		weapon.Wielder = Wielder;
		if (weapon is KnifeView knife) knife.SetViewStyle(KnifeStyle);
		if (weapon is FirearmView gun) _reserveAmmo.TryAdd(gun.AmmoType, gun.ReserveCapacity);
	}

	// The scene owns its animation/view; inventory owns its lifetime and selection.
	public WeaponView AddWeapon(PackedScene scene, bool equip = true, bool grantReserve = true)
	{
		var weapon = scene.Instantiate<WeaponView>();
		// Match stable CSS entity identities across peers and dynamically added views.
		weapon.Name = scene.GetState().GetNodeName(0);
		AddChild(weapon);
		var existingReserve = weapon is FirearmView firearm ? GetReserveAmmo(firearm.AmmoType) : 0;
		Register(weapon);
		if (!grantReserve && weapon is FirearmView gun) _reserveAmmo[gun.AmmoType] = existingReserve;
		foreach (MeshInstance3D mesh in weapon.FindChildren("*", "MeshInstance3D", true, false)) mesh.Layers = 2;
		if (Wielder is { LocalInput: false, IsBot: false } && LanSession.Instance?.IsActive == true
			|| Wielder?.LocalInput == false && LanSession.Instance?.IsClient == true)
		{ weapon.SetProcess(false); weapon.SetPhysicsProcess(false); }
		if (equip) Equip(weapon);
		return weapon;
	}

	public bool RemoveWeapon(WeaponView weapon, bool selectReplacement = true)
	{
		if (!_weapons.Remove(weapon)) return false;
		if (_slotSelection.GetValueOrDefault(weapon.Slot) == weapon) _slotSelection.Remove(weapon.Slot);
		if (_previous == weapon) _previous = null;
		if (ActiveWeapon == weapon)
		{
			weapon.Holster();
			ActiveWeapon = null;
			if (Enabled && selectReplacement) EquipBest();
			if (ActiveWeapon == null) EmitSignal(SignalName.WeaponChanged, (WeaponView)null!);
		}
		if (_previous == weapon) _previous = null;
		weapon.Wielder = null;
		RemoveChild(weapon);
		weapon.QueueFree();
		return true;
	}

	public bool TryPickup(WeaponPickup pickup)
	{
		if (Wielder == null) return false;
		var owned = _weapons.FirstOrDefault(w => WeaponPickup.ClassOf(w) == pickup.WeaponClass);
		if (pickup.Slot == WeaponSlot.Grenade && owned is GrenadeView grenade)
		{
			var maximum = grenade.Kind == GrenadeKind.Flashbang ? 2 : 1;
			if (grenade.Ammo >= maximum) return false;
			grenade.RestoreAmmo(grenade.Ammo + pickup.GrenadeAmmo);
		}
		else
		{
			if (pickup.Slot != WeaponSlot.Grenade && _weapons.Any(w => w.Slot == pickup.Slot)) return false;
			var weapon = AddWeapon(GD.Load<PackedScene>(WeaponPickup.ViewPath(pickup.WeaponClass)), equip: false, grantReserve: false);
			if (weapon is FirearmView gun)
			{
				gun.RestoreMagazine(pickup.Magazine);
				gun.ApplyNetworkSilencer(pickup.Silenced);
				gun.ApplyNetworkBurst(pickup.BurstMode);
				if (pickup.ExtraAmmo != 0) SetReserveAmmo(gun.AmmoType,
					pickup.ExtraAmmo < 0 ? gun.ReserveCapacity : pickup.ExtraAmmo, gun.ReserveCapacity);
			}
			if (weapon is GrenadeView newGrenade) newGrenade.RestoreAmmo(pickup.GrenadeAmmo);
			if (ActiveWeapon == null || AutoSwitch && weapon.Slot < ActiveWeapon.Slot) Equip(weapon);
		}
		if (Wielder.LocalInput) ActiveWeapon?.Sounds.PlayWave("items/itempickup.wav", relay: false);
		LanSession.Instance?.BroadcastInventory(Wielder, pickupSound: true);
		return true;
	}

	public WeaponPickup? DropWeapon(WeaponView weapon, bool fromDeath = false)
	{
		if (Wielder == null || !_weapons.Contains(weapon) || !WeaponPickup.IsSupported(WeaponPickup.ClassOf(weapon))
			|| !fromDeath && (weapon is not FirearmView || !weapon.CanHolster || !Wielder.IsAlive)) return null;
		var player = Wielder;
		var gun = weapon as FirearmView;
		var reserve = 0;
		if (gun != null && (fromDeath || !_weapons.OfType<FirearmView>().Any(w => w != gun && w.AmmoType == gun.AmmoType)))
			reserve = TakeReserveAmmo(gun.AmmoType, gun.ReserveCapacity);
		var transform = player.Body?.GetWorldWeaponTransform(weapon) ?? new Transform3D(player.GlobalBasis, player.Camera.GlobalPosition - Vector3.Up * (12 * SourceMovement.Unit));
		var velocity = player.Velocity + (fromDeath ? Vector3.Zero : player.WeaponDropVelocity());
		var world = player.GetTree().CurrentScene as Node3D ?? player.GetParent<Node3D>();
		var pickup = WeaponPickup.Spawn(world, WeaponPickup.ClassOf(weapon), transform, velocity,
			new Vector3(1, 1, -1) * Mathf.DegToRad(200), player, gun?.MagazineAmmo ?? -1, reserve, gun?.Silenced ?? false,
			weapon is GrenadeView grenade ? System.Math.Min(1, grenade.Ammo) : 1, gun?.BurstMode ?? false);
		RemoveWeapon(weapon, selectReplacement: !fromDeath);
		LanSession.Instance?.BroadcastInventory(player);
		return pickup;
	}

	public bool Equip(WeaponView weapon, bool purchased = false)
	{
		if ((!Enabled && !purchased && Wielder?.Console?.IsOpen != true) || !_weapons.Contains(weapon) || !weapon.CanEquip) return false;
		if (ActiveWeapon == weapon) return true;
		if (ActiveWeapon?.CanHolster == false) return false;
		ActiveWeapon?.Holster();
		_previous = ActiveWeapon;
		ActiveWeapon = weapon;
		_slotSelection[weapon.Slot] = weapon;
		weapon.Equip();
		weapon.SetInputEnabled(Enabled);
		EmitSignal(SignalName.WeaponChanged, weapon);
		return true;
	}

	public bool EquipSlot(WeaponSlot slot)
	{
		var candidates = _weapons.FindAll(w => w.Slot == slot && w.CanEquip);
		if (candidates.Count == 0) return false;
		if (ActiveWeapon?.Slot == slot)
			return Equip(candidates[(candidates.IndexOf(ActiveWeapon) + 1) % candidates.Count]);
		var remembered = _slotSelection.GetValueOrDefault(slot);
		return Equip(remembered != null && candidates.Contains(remembered) ? remembered : candidates[0]);
	}

	public void EquipNetwork(WeaponView weapon)
	{
		if (Wielder?.LocalInput != false || ActiveWeapon == weapon) return;
		ActiveWeapon?.Holster(); ActiveWeapon = weapon;
		weapon.Equip(); weapon.SetInputEnabled(false);
		EmitSignal(SignalName.WeaponChanged, weapon);
	}

	public bool EquipBest() => EquipSlot(WeaponSlot.Primary) || EquipSlot(WeaponSlot.Secondary) || EquipSlot(WeaponSlot.Melee);

	public void ResetGrenades()
	{
		foreach (var weapon in _weapons)
			if (weapon is GrenadeView grenade) grenade.ResetAmmo();
	}

	public bool EquipPrevious()
	{
		if (_previous != null && _previous != ActiveWeapon && _weapons.Contains(_previous) && _previous.CanEquip)
			return Equip(_previous);
		// Respawning or losing a weapon can clear lastinv's history. Seed it
		// from the remaining loadout so Q works before the first manual switch.
		foreach (var slot in new[] { WeaponSlot.Primary, WeaponSlot.Secondary, WeaponSlot.Melee })
		{
			var alternative = _weapons.Find(w => w != ActiveWeapon && w.Slot == slot && w.CanEquip);
			if (alternative != null) return Equip(alternative);
		}
		return false;
	}

	public void SetEnabled(bool enabled, bool keepVisible = false)
	{
		Enabled = enabled;
		if (!enabled && !keepVisible)
		{
			if (ActiveWeapon?.Equipped == true) ActiveWeapon.Holster();
		}
		else if (ActiveWeapon != null)
		{
			if (!ActiveWeapon.Equipped) ActiveWeapon.Equip();
			ActiveWeapon.SetInputEnabled(enabled);
		}
		else if (enabled && _weapons.Count > 0) Equip(_weapons[0]);
	}

	public bool HandleInput(InputEvent @event)
	{
		if (!Enabled || !@event.IsPressed() || @event is InputEventKey { Echo: true }) return false;
		var key = ConsoleBindings.EventKey(@event);
		if (key == null || !ConsoleBindings.Keys.TryGetValue(key, out var command)) return false;
		if (command == "lastinv") return EquipPrevious();
		if (command.StartsWith("slot", System.StringComparison.OrdinalIgnoreCase) && int.TryParse(command[4..], out var slot) && slot is >= 1 and <= 5)
			return EquipSlot((WeaponSlot)(slot - 1));
		return false;
	}
}
