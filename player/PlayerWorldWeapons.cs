using Godot;
using System.Linq;
using Contrapoint.Network;
using Contrapoint.Weapons;
using Contrapoint.Gameplay;

namespace Contrapoint.Players;

public partial class Player
{
	public void DropActiveWeapon()
	{
		if (!IsAlive || Team is not (PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist)) return;
		if (LanSession.Instance?.RequestWeaponDrop(this) == true) return;
		if (ActiveWeapon is C4View) { DefuseState.Instance?.DropBomb(this); return; }
		if (ActiveWeapon is not FirearmView) return;
		Inventory.DropWeapon(ActiveWeapon);
	}

	public Vector3 WeaponDropVelocity()
	{
		// CSWeaponDrop / h_ai.cpp::VecCheckToss: maximum arc height is 0.2
		// of the distance above the higher endpoint; this is not a fixed time.
		var start = Camera.GlobalPosition - Vector3.Up * (12 * SourceMovement.Unit);
		var height = Movement.IsDucked ? SourceMovement.DuckHeight : SourceMovement.StandingHeight;
		var target = GlobalPosition + Vector3.Up * height / 2 - Camera.GlobalBasis.Z * (64 * SourceMovement.Unit);
		if (target.Y - start.Y > 500 * SourceMovement.Unit || Movement.Gravity <= 0) return Vector3.Zero;
		var excluded = new Godot.Collections.Array<Rid> { GetRid() };
		var brushExcluded = new Godot.Collections.Array<Rid>(excluded);
		foreach (var body in GetTree().GetNodesInGroup("source_players").OfType<Player>())
			if (body != this) brushExcluded.Add(body.GetRid());
		foreach (var body in GetTree().GetNodesInGroup("source_weapon_pickups").OfType<WeaponPickup>()) brushExcluded.Add(body.GetRid());
		var middle = (start + target) / 2;
		var ceiling = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(middle,
			middle + Vector3.Up * (300 * SourceMovement.Unit), 1, brushExcluded));
		var apexY = ceiling.Count > 0 ? ceiling["position"].AsVector3().Y - 15 * SourceMovement.Unit : middle.Y + 300 * SourceMovement.Unit;
		apexY = Mathf.Min(apexY, Mathf.Max(start.Y, target.Y) + .2f * start.DistanceTo(target));
		if (apexY < start.Y || apexY < target.Y) return Vector3.Zero;
		var gravity = Movement.Gravity * SourceMovement.Unit;
		var time1 = Mathf.Sqrt((apexY - start.Y) / (.5f * gravity));
		var time2 = Mathf.Sqrt((apexY - target.Y) / (.5f * gravity));
		if (time1 < .1f) return Vector3.Zero;
		var velocity = (target - start) / (time1 + time2); velocity.Y = gravity * time1;
		var apex = start + velocity * time1; apex.Y = apexY;
		if (GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(start, apex, 1, excluded)).Count > 0
			|| GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(target, apex, 1, brushExcluded)).Count > 0) return Vector3.Zero;
		return velocity;
	}

	private void DropDeathWeapons()
	{
		if (DeathmatchRules.Active || LanSession.Instance?.IsClient == true) return;
		// CSS drops the rifle, or the pistol without a rifle, and one grenade
		// in HE -> flash -> smoke order.
		var gun = Inventory.Weapons.FirstOrDefault(w => w.Slot == WeaponSlot.Primary)
			?? Inventory.Weapons.FirstOrDefault(w => w.Slot == WeaponSlot.Secondary);
		if (gun != null) Inventory.DropWeapon(gun, fromDeath: true);
		var grenade = Inventory.Weapons.OfType<GrenadeView>().Where(w => w.Ammo > 0 && !w.PinPulled)
			.OrderBy(w => w.Kind == GrenadeKind.HE ? 0 : w.Kind == GrenadeKind.Flashbang ? 1 : 2).FirstOrDefault();
		if (grenade != null) Inventory.DropWeapon(grenade, fromDeath: true);
	}

	public WeaponPickup? CreateWeaponEntity(string name)
	{
		if (!WeaponPickup.IsSupported(name) || LanSession.Instance?.IsClient == true) return null;
		var start = Camera.GlobalPosition;
		var end = start - Camera.GlobalBasis.Z * (80 * SourceMovement.Unit);
		var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(start, end, 1, new() { GetRid() }));
		if (hit.Count > 0) end = hit["position"].AsVector3() + hit["normal"].AsVector3() * (16 * SourceMovement.Unit);
		return WeaponPickup.Spawn(GetTree().CurrentScene as Node3D ?? GetParent<Node3D>(), name, new(GlobalBasis, end));
	}
}
