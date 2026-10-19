using Godot;
using Contrapoint.Gameplay;

namespace Contrapoint.Weapons;

public partial class C4View : WeaponView
{
	public override WeaponSlot Slot => WeaponSlot.Objective;
	protected override double DeploySeconds => 1;
	private bool _planting;
	private bool _held;
	private bool _placing;
	public override void _Ready()
	{
		base._Ready();
		// The view GLB needs the same specular correction as the world C4.
		WorldWeaponMaterials.Apply(Model);
		foreach (MeshInstance3D mesh in Model.FindChildren("*", "MeshInstance3D", true, false))
			for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
				if (mesh.GetActiveMaterial(surface) is StandardMaterial3D material)
				{
					material.CullMode = BaseMaterial3D.CullModeEnum.Back;
					if (material.ResourceName == "v_hands") { material.Roughness = .9f; material.MetallicSpecular = .1f; }
				}
	}
	protected override void OnEquip() { _planting = _held = _placing = false; Animations.Play("draw"); }
	protected override void OnHolster() { _held = false; if (Wielder != null) Wielder.SetObjectiveAttack(false); }
	protected override void OnInputDisabled() { _held = false; if (Wielder != null) Wielder.SetObjectiveAttack(false); }
	public override bool HandleCommand(string command, bool down)
	{
		if (command != "attack" || !Equipped || Wielder == null) return false;
		_held = down; Wielder.SetObjectiveAttack(down && DeployRemaining <= 0);
		return true;
	}
	public override void _Process(double delta)
	{
		AdvanceDeploy(delta);
		if (!Equipped) return;
		if (Wielder != null && Wielder.ObjectiveAttackHeld != (_held && DeployRemaining <= 0)) Wielder.SetObjectiveAttack(_held && DeployRemaining <= 0);
		var planting = Wielder != null && DefuseState.Instance?.Planter == Wielder;
		if (planting != _planting)
		{
			_planting = planting; _placing = false; Animations.Play(planting ? "pressbutton" : "idle1");
		}
		else if (planting && !_placing && DefuseState.Instance!.PlantProgress >= 2.25)
		{ _placing = true; Animations.Play("drop"); }
		else if (!planting && !Animations.IsPlaying()) Animations.Play("idle1");
	}
}
