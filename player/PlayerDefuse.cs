using Godot;
using Contrapoint.Gameplay;
using Contrapoint.Network;

namespace Contrapoint.Players;

public partial class Player
{
	private Transform3D GroundRoundSpawn(Transform3D spawn)
	{
		var excluded = new Godot.Collections.Array<Rid>();
		foreach (Player p in GetTree().GetNodesInGroup("source_players")) excluded.Add(p.GetRid());
		// Sweep the standing hull: a point ray under its centre sinks into slopes
		// and misses floors that support only an edge of the player's feet.
		using var hull = new BoxShape3D { Size = new(32 * SourceMovement.Unit, SourceMovement.StandingHeight, 32 * SourceMovement.Unit), Margin = .001f };
		var motion = Vector3.Down * (128 * SourceMovement.Unit);
		using var query = new PhysicsShapeQueryParameters3D { Shape = hull, Exclude = excluded, CollisionMask = 3,
			Transform = new(Basis.Identity, spawn.Origin + Vector3.Up * SourceMovement.StandingHeight / 2), Motion = motion, Margin = .001f };
		var sweep = GetWorld3D().DirectSpaceState.CastMotion(query);
		if (sweep.Length == 2 && sweep[0] < 1) spawn.Origin += motion * sweep[0];
		return spawn;
	}
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
		// CSS CCSPlayer::Spawn stocks firearm ammunition even for survivors.
		Inventory.ResetAmmunition();
		Armor = armor; HasHelmet = helmet; HasDefuser = kit;
		LanSession.Instance?.BroadcastInventory(this);
		LanSession.Instance?.BroadcastVitals(this);
	}
	public void TakeBombDamage(float damage, Vector3 position) => ApplyDamage(damage, PlayerDamageKind.Bomb,
		position: position, direction: (GlobalPosition - position).Normalized());
}
