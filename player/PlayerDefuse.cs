using Godot;
using Contrapoint.Gameplay;
using Contrapoint.Network;

namespace Contrapoint.Players;

public partial class Player
{
	public bool HasDefuser { get; set; }
	public bool ObjectiveAttackHeld { get; set; }
	public bool ObjectiveUseHeld { get; set; }
	public ulong ObjectiveInputDeadline { get; set; }
	private double _objectivePublish;
	public void SetObjectiveAttack(bool down)
	{
		ObjectiveAttackHeld = down;
		if (LocalInput) LanSession.Instance?.SendObjectiveInput(this);
	}
	public void SetObjectiveUse(bool down)
	{
		ObjectiveUseHeld = down;
		if (LocalInput) LanSession.Instance?.SendObjectiveInput(this);
	}
	private void TickObjectiveInput(double delta)
	{
		if (!DefuseRules.Active) return;
		if (!IsAlive || !HasLocalControl) { ObjectiveAttackHeld = false; ObjectiveUseHeld = false; }
		_objectivePublish -= delta;
		if (_objectivePublish <= 0) { _objectivePublish = .1; LanSession.Instance?.SendObjectiveInput(this); }
	}
	public void RespawnForDefuse(bool survivor)
	{
		var contents = survivor ? Inventory.CaptureContents() : null;
		var armor = survivor ? Armor : 0; var helmet = survivor && HasHelmet; var kit = survivor && HasDefuser;
		ObjectiveAttackHeld = ObjectiveUseHeld = false;
		Respawn(restoreLoadout: !survivor);
		if (contents != null) Inventory.ApplyContents(Inventory.NetworkRevision, contents, ActiveWeapon?.Name.ToString() ?? "");
		Armor = armor; HasHelmet = helmet; HasDefuser = kit;
		LanSession.Instance?.BroadcastInventory(this);
		LanSession.Instance?.BroadcastVitals(this);
	}
	public void TakeBombDamage(float damage, Vector3 position) => ApplyDamage(damage, PlayerDamageKind.Bomb,
		position: position, direction: (GlobalPosition - position).Normalized());
}
