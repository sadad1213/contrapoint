using Godot;
using Contrapoint.Gameplay;

namespace Contrapoint.Weapons;

public partial class C4View : WeaponView
{
	public override WeaponSlot Slot => WeaponSlot.Objective;
	protected override double DeploySeconds => 1;
	private bool _planting;
	protected override void OnEquip() { _planting = false; Animations.Play("draw"); }
	protected override void OnHolster() { if (Wielder != null) Wielder.SetObjectiveAttack(false); }
	protected override void OnInputDisabled() { if (Wielder != null) Wielder.SetObjectiveAttack(false); }
	public override bool HandleCommand(string command, bool down)
	{
		if (command != "attack" || !Equipped || Wielder == null) return false;
		Wielder.SetObjectiveAttack(down && DeployRemaining <= 0);
		return true;
	}
	public override void _Process(double delta)
	{
		AdvanceDeploy(delta);
		if (!Equipped) return;
		var planting = Wielder != null && DefuseState.Instance?.Planter == Wielder;
		if (planting != _planting)
		{
			_planting = planting; Animations.Play(planting ? "pressbutton" : "idle1");
		}
		else if (!planting && !Animations.IsPlaying()) Animations.Play("idle1");
	}
}
