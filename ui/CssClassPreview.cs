using Godot;
using System;
using Contrapoint.Players;
using Contrapoint.Weapons;

namespace Contrapoint.UI;

// clientmode_csnormal.cpp: UpdateClassImageEntity walks in place with a
// bonemerged rifle, or the living local player's currently held world weapon.
public partial class CssClassPreview : Node3D
{
	private Skeleton3D _skeleton = null!;
	private AnimationPlayer _animations = null!;
	private Node3D _weapon = null!;
	private Skeleton3D? _weaponSkeleton;
	private Transform3D _weaponBindInverse = Transform3D.Identity;
	private bool[] _lower = [];
	private Vector3[] _positions = [];
	private Quaternion[] _rotations = [];
	private Basis _upperPelvis;
	private int _spine, _weaponBone;
	private double _time;
	private string _upper = "Walk_Upper_M4";

	public void Initialize(string agent, Player player)
	{
		var model = GD.Load<PackedScene>($"res://assets/players/{agent}/{agent}.glb").Instantiate<Node3D>();
		AddChild(model);
		_skeleton = (Skeleton3D)model.FindChild("Skeleton3D", true, false);
		_animations = (AnimationPlayer)model.FindChild("AnimationPlayer", true, false);
		_animations.AddAnimationLibrary("arsenal", CssWeaponPoses.Library);
		_animations.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
		_spine = _skeleton.FindBone("ValveBiped.Bip01_Spine");
		_weaponBone = _skeleton.FindBone("ValveBiped.weapon_bone");
		var count = _skeleton.GetBoneCount(); _lower = new bool[count]; _positions = new Vector3[count]; _rotations = new Quaternion[count];
		for (var i = 0; i < count; i++)
		{
			_lower[i] = _skeleton.GetBoneName(i).ToString().Contains("Pelvis");
			for (var bone = i; bone >= 0; bone = _skeleton.GetBoneParent(bone))
				if (_skeleton.GetBoneName(bone).ToString().Contains("Thigh")) { _lower[i] = true; break; }
		}
		Sample("Idle_lower", 0);
		_upperPelvis = _skeleton.GetBoneGlobalPose(_skeleton.GetBoneParent(_spine)).Basis;
		var id = PlayerBody.AgentTeam(agent) == PlayerTeam.CounterTerrorist ? "m4a1" : "ak47";
		var extension = id == "m4a1" ? "M4" : "AK";
		if (player.IsAlive && !player.ClassSelectionRequired && player.Team is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist)
		{
			id = player.ActiveWeapon is FirearmView firearm ? firearm.Kind.ToString().ToLowerInvariant()
				: player.ActiveWeapon is GrenadeView grenade ? grenade.AssetName : "knife";
			extension = player.ActiveWeapon is FirearmView gun ? gun.PlayerAnimationExtension
				: player.ActiveWeapon is KnifeView ? "KNIFE" : "GREN";
		}
		_upper = "Walk_Upper_" + extension;
		var path = id == "knife" ? "res://assets/players/knife/knife_world.glb" : $"res://assets/weapons/{id}/world/{id}_world.glb";
		_weapon = GD.Load<PackedScene>(path).Instantiate<Node3D>(); _skeleton.AddChild(_weapon);
		_weaponSkeleton = _weapon.FindChild("Skeleton3D", true, false) as Skeleton3D;
		if (_weaponSkeleton != null)
		{
			var bone = _weaponSkeleton.FindBone("ValveBiped.weapon_bone");
			if (bone >= 0) _weaponBindInverse = (_weapon.GlobalTransform.AffineInverse() * _weaponSkeleton.GlobalTransform * _weaponSkeleton.GetBoneGlobalRest(bone)).AffineInverse();
		}
		_Process(0);
	}
	private void Sample(string name, double time)
	{
		if (!_animations.HasAnimation(name)) name = "arsenal/" + name;
		_animations.Play(name); _animations.Seek(time % Math.Max(.001, _animations.GetAnimation(name).Length), true); _animations.Advance(0);
	}
	public override void _Process(double delta)
	{
		if (_animations == null) return;
		_time += delta;
		Sample("a_WalkN", _time);
		for (var i = 0; i < _lower.Length; i++)
			if (_lower[i]) { _positions[i] = _skeleton.GetBonePosePosition(i); _rotations[i] = _skeleton.GetBonePoseRotation(i); }
		Sample(_upper, _time);
		var upperBasis = _upperPelvis * new Basis(_skeleton.GetBonePoseRotation(_spine));
		for (var i = 0; i < _lower.Length; i++)
			if (_lower[i]) { _skeleton.SetBonePosePosition(i, _positions[i]); _skeleton.SetBonePoseRotation(i, _rotations[i]); }
		var parentBasis = _skeleton.GetBoneGlobalPose(_skeleton.GetBoneParent(_spine)).Basis;
		_skeleton.SetBonePoseRotation(_spine, (parentBasis.Inverse() * upperBasis).GetRotationQuaternion().Normalized());
		var axis = parentBasis.Inverse() * Vector3.Right;
		_skeleton.SetBonePoseRotation(_spine, new Quaternion(axis.Normalized(), Mathf.DegToRad(-10)) * _skeleton.GetBonePoseRotation(_spine));
		_weapon.Transform = _skeleton.GetBoneGlobalPose(_weaponBone) * _weaponBindInverse;
		if (_weaponSkeleton == null) return;
		var intoWeapon = _weaponSkeleton.GlobalTransform.AffineInverse() * _skeleton.GlobalTransform;
		for (var i = 0; i < _weaponSkeleton.GetBoneCount(); i++)
		{
			var bone = _skeleton.FindBone(_weaponSkeleton.GetBoneName(i));
			if (bone < 0) continue;
			var pose = intoWeapon * _skeleton.GetBoneGlobalPose(bone);
			var parent = _weaponSkeleton.GetBoneParent(i);
			if (parent >= 0) pose = _weaponSkeleton.GetBoneGlobalPose(parent).AffineInverse() * pose;
			_weaponSkeleton.SetBonePosePosition(i, pose.Origin);
			_weaponSkeleton.SetBonePoseRotation(i, pose.Basis.GetRotationQuaternion().Normalized());
		}
	}
}
