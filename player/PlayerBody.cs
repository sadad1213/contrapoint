using Godot;
using System;
using System.Collections.Generic;
using Contrapoint.Weapons;

namespace Contrapoint.Players;

// CSS uses an independent lower-body gait and weapon-specific upper-body poses.
// Sample both authored clips, retaining the pelvis/legs from locomotion.
public partial class PlayerBody : Node3D
{
	public const uint ObservedLayer = 1u << 19;
	public bool ObservedFirstPerson { get; private set; }
	public void SetObservedFirstPerson(bool observed)
	{
		if (ObservedFirstPerson == observed) return;
		ObservedFirstPerson = observed;
		foreach (MeshInstance3D mesh in FindChildren("*", "MeshInstance3D", true, false))
			mesh.Layers = observed ? ObservedLayer : _player.LocalInput ? ThirdPersonCamera.BodyLayer : 1;
	}
	public static readonly string[] TerroristAgentIds = ["phoenix", "leet", "arctic", "guerilla"];
	public static readonly string[] CounterTerroristAgentIds = ["urban", "gsg9", "sas", "gign"];
	public static readonly string[] AgentIds = ["phoenix", "arctic", "guerilla", "leet", "urban", "gsg9", "sas", "gign"];
	public static PlayerTeam AgentTeam(string id) => Array.IndexOf(CounterTerroristAgentIds, id) >= 0
		? PlayerTeam.CounterTerrorist : Array.IndexOf(TerroristAgentIds, id) >= 0 ? PlayerTeam.Terrorist : PlayerTeam.Unassigned;
	public static string[] AgentsForTeam(PlayerTeam team) => team == PlayerTeam.CounterTerrorist ? CounterTerroristAgentIds : TerroristAgentIds;
	public string LowerClip { get; private set; } = "Idle_lower";
	public string UpperClip { get; private set; } = "Idle_Upper_KNIFE";
	public Skeleton3D Skeleton { get; private set; } = null!;
	public SourceHitboxes Hitboxes { get; private set; } = null!;
	public Transform3D ModelGlobalTransform => _model.GlobalTransform;
	public Node3D CloneForRagdoll() { EnsurePose(); return (Node3D)_model.Duplicate(); }
	public Node3D? WorldWeapon => _worldWeapon;
	private Player _player = null!;
	private Node3D _model = null!;
	private AnimationPlayer _animations = null!;
	private WeaponView? _weapon;
	private AnimationPlayer? _viewAnimations;
	private Node3D? _worldWeapon;
	private Node3D? _muzzle, _silencedMuzzle;
	private Skeleton3D? _worldSkeleton;
	private readonly List<(int World, int Player)> _mergedBones = new();
	private Transform3D _weaponBindInverse = Transform3D.Identity;
	private int _weaponBone = -1, _spine = -1;
	private bool[] _lower = [];
	private Vector3[] _positions = [], _previousPositions = [];
	private Vector3[] _scales = [], _previousScales = [];
	private int[] _parents = [];
	private PlayerBodyPose _pose = null!;
	private VisibleOnScreenNotifier3D _visibility = null!;
	private bool _headless, _poseDirty;
	private double _poseDelta;
	private ulong _posePhysicsFrame;
	private Quaternion[] _rotations = [], _previousRotations = [];
	private readonly Dictionary<string, PackedScene> _weaponScenes = new();
	private sealed record Clip(StringName Name, Animation Animation);
	private readonly Dictionary<string, Clip?> _clips = new(StringComparer.Ordinal);
	private static readonly string[] Directions = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];
	private double _phase, _upperTime, _deathTime;
	private bool _snap = true;
	private string _previousUpper = "";
	private int _shots;
	private float _kick;
	private float _jumpTime;
	private bool _wasGrounded;
	private bool _wasAlive;
	private Quaternion _previousUpperSpineRotation = Quaternion.Identity;
	private Basis _standingPelvisBasis = Basis.Identity, _crouchingPelvisBasis = Basis.Identity;

	public void Bind(Player player)
	{
		_player = player;
		ProcessPriority = 50;
		_headless = DisplayServer.GetName() == "headless";
		_visibility = new VisibleOnScreenNotifier3D
		{
			Name = "PoseVisibility", Aabb = new(new(-1, -.5f, -1), new(2, 3, 2)),
			Layers = player.LocalInput ? ThirdPersonCamera.BodyLayer : 1
		};
		AddChild(_visibility);
		_visibility.ScreenEntered += EnsurePose;
		if (player.Team is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist && AgentTeam(player.AgentId) != player.Team)
			player.AgentId = AgentsForTeam(player.Team)[0];
		if (!SetAgent(player.AgentId)) { player.AgentId = AgentsForTeam(player.Team)[0]; SetAgent(player.AgentId); }
	}

	public bool SetAgent(string id)
	{
		if (Array.IndexOf(AgentIds, id) < 0) return false;
		var scene = PlayerSceneCache.Load($"res://assets/players/{id}/{id}.glb");
		if (scene == null) return false;
		ClearClips();
		if (_model != null) { RemoveChild(_model); _model.QueueFree(); }
		_model = scene.Instantiate<Node3D>();
		_model.Name = "Model";
		_model.Rotation = new(0, Mathf.Pi, 0);
		AddChild(_model);
		Skeleton = (Skeleton3D)_model.FindChild("Skeleton3D", true, false);
		// Host hit detection follows the received pose, excluding the body's render offset.
		Hitboxes = new(Skeleton, id, () => Contrapoint.Network.LanSession.Instance?.IsHosting == true
			? _player.GlobalTransform * GlobalTransform.AffineInverse() * Skeleton.GlobalTransform
			: Skeleton.GlobalTransform, EnsurePose,
			() => (Contrapoint.Network.LanSession.Instance?.IsHosting == true ? _player.GlobalPosition : GlobalPosition) + Vector3.Up);
		_animations = (AnimationPlayer)_model.FindChild("AnimationPlayer", true, false);
		_animations.AddAnimationLibrary("arsenal", CssWeaponPoses.Library);
		_animations.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
		_animations.Stop();
		var count = Skeleton.GetBoneCount();
		_lower = new bool[count];
		_weaponBone = _spine = -1;
		_positions = new Vector3[count];
		_rotations = new Quaternion[count];
		_previousPositions = new Vector3[count];
		_previousRotations = new Quaternion[count];
		_scales = new Vector3[count];
		_previousScales = new Vector3[count];
		_parents = new int[count];
		for (var i = 0; i < count; i++)
		{
			_parents[i] = Skeleton.GetBoneParent(i);
			var name = Skeleton.GetBoneName(i).ToString();
			_lower[i] = name.Contains("Pelvis");
			for (var bone = i; bone >= 0; bone = Skeleton.GetBoneParent(bone))
				if (Skeleton.GetBoneName(bone).ToString().Contains("Thigh")) { _lower[i] = true; break; }
			if (name.EndsWith("weapon_bone")) _weaponBone = i;
			if (name.EndsWith("Bip01_Spine")) _spine = i;
		}
		if (_spine >= 0)
		{
			var pelvis = Skeleton.GetBoneParent(_spine);
			// Upper clips have zero pelvis weight in Source. Their baked root is
			// a bind pose, not the orientation to use for an upright character.
			Sample("Idle_lower", 0, false);
			_standingPelvisBasis = Skeleton.GetBoneGlobalPose(pelvis).Basis;
			Sample("Crouch_Idle_Lower", 0, false);
			_crouchingPelvisBasis = Skeleton.GetBoneGlobalPose(pelvis).Basis;
		}
		for (var i = 0; i < count; i++)
		{
			_positions[i] = Skeleton.GetBonePosePosition(i);
			_rotations[i] = Skeleton.GetBonePoseRotation(i);
			_previousScales[i] = _scales[i] = Skeleton.GetBonePoseScale(i);
		}
		_pose = new(Skeleton, _lower);
		ConfigureMeshes(_model);
		_worldWeapon = null;
		_muzzle = _silencedMuzzle = null;
		_worldSkeleton = null;
		_mergedBones.Clear();
		_weapon = null;
		ResetPose();
		return true;
	}

	private void ConfigureMeshes(Node root)
	{
		foreach (MeshInstance3D mesh in root.FindChildren("*", "MeshInstance3D", true, false))
		{
			mesh.Layers = ObservedFirstPerson ? ObservedLayer : _player.LocalInput ? ThirdPersonCamera.BodyLayer : 1;
			for (var i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
				if (mesh.GetActiveMaterial(i) is StandardMaterial3D source)
				{
					var material = (StandardMaterial3D)source.Duplicate();
					material.Metallic = 0;
					material.Roughness = 0.85f;
					material.MetallicSpecular = 0.15f;
					mesh.SetSurfaceOverrideMaterial(i, material);
				}
		}
	}

	public void ResetPose()
	{
		_phase = _upperTime = _deathTime = 0;
		_jumpTime = _kick = 0;
		_snap = true;
		_poseDelta = 0;
		_wasAlive = true;
		_previousUpper = "";
		Tick(0, true);
	}

	public override void _Process(double delta)
	{
		if (_player == null) return;
		Tick(delta);
	}

	// Hidden rigs retain animation clocks. Gameplay callers request current bones
	// before hit detection, weapon attachments or a death pose need them.
	public void EnsurePose()
	{
		if (_poseDirty || _posePhysicsFrame != Engine.GetPhysicsFrames() || _weapon != _player.ActiveWeapon) Tick(0, true);
	}
	private void Tick(double delta, bool force = false)
	{
		if (!_player.IsAlive)
		{
			_wasAlive = false;
			return;
		}
		if (_weapon != _player.ActiveWeapon) AttachWeapon(_player.ActiveWeapon);
		// A cosmetic knife switch keeps the entity and replaces its animation rig.
		_viewAnimations = _weapon?.NetworkAnimation;
		if (!_wasAlive) ResetPose();
		_poseDelta += delta;
		_poseDirty = true;
		var speed = _player.Movement.HorizontalSpeed;
		var crouch = _player.Movement.IsDucked;
		var grounded = !_player.LocalInput && !_player.IsServerBot && _player.NetworkPeerId != 0
			? _player.NetworkGrounded : _player.IsOnFloor() || _player.Movement.Noclip;
		if (_wasGrounded && !grounded) _jumpTime = 0;
		_wasGrounded = grounded;
		_jumpTime += (float)delta;
		var moving = speed > 5;
		// Acceleration and counter-strafing do not change the requested gait.
		var walking = _player.MovementIntent.Walk;
		var gait = crouch ? "Crouch_walk" : walking ? "Walk" : "Run";
		_phase += delta * Mathf.Clamp(speed / (crouch ? 70 : walking ? 110 : 220), 0.05f, 2);
		if (!grounded) LowerClip = "Jump";
		else if (!moving) LowerClip = crouch ? "Crouch_Idle_Lower" : "Idle_lower";
		else
		{
			var velocity = _player.GlobalBasis.Inverse() * _player.Velocity;
			var sector = Mathf.PosMod(Mathf.RoundToInt(Mathf.Atan2(velocity.X, -velocity.Z) / (Mathf.Pi / 4)), 8);
			LowerClip = "a_" + gait + Directions[sector];
		}

		var kind = _weapon is FirearmView firearm ? firearm.PlayerAnimationExtension
			: _weapon is GrenadeView ? "GREN" : "KNIFE";
		var stance = crouch ? moving ? "Crouch_Walk" : "Crouch_Idle" : moving ? walking ? "Walk" : "Run" : "Idle";
		using var assignedAnimation = _viewAnimations?.AssignedAnimation;
		var viewClip = assignedAnimation?.ToString() ?? "";
		var reload = viewClip.Contains("reload", StringComparison.OrdinalIgnoreCase);
		UpperClip = stance + (reload ? "_Reload_" : "_Upper_") + kind;
		// Shotguns reload shell by shell and have no matching Reload body clip.
		// Keep their weapon-specific holding pose while the weapon clock runs.
		var reloadClip = reload ? FindClip(UpperClip) : null;
		if (reload && reloadClip == null) { reload = false; UpperClip = stance + "_Upper_" + kind; }
		if (_previousUpper != UpperClip) { _upperTime = 0; _previousUpper = UpperClip; }
		_upperTime += delta;
		var sampleTime = reload && _viewAnimations != null
			? _viewAnimations.CurrentAnimationPosition / Math.Max(0.001, _viewAnimations.CurrentAnimationLength)
				* reloadClip!.Animation.Length : _upperTime;
		var loopUpper = !reload;
		if (_weapon is GrenadeView && viewClip == "throw")
		{
			UpperClip = "Idle_Shoot_GREN2";
			sampleTime = _viewAnimations!.CurrentAnimationPosition;
			loopUpper = false;
		}
		else if (_weapon != null && _viewAnimations != null &&
			(viewClip.Contains("fire", StringComparison.OrdinalIgnoreCase) ||
			 viewClip.StartsWith("shoot", StringComparison.OrdinalIgnoreCase) ||
			 viewClip.StartsWith("midslash", StringComparison.OrdinalIgnoreCase) ||
			 viewClip.StartsWith("light_", StringComparison.OrdinalIgnoreCase) ||
			 viewClip.StartsWith("heavy_", StringComparison.OrdinalIgnoreCase) ||
			 viewClip.StartsWith("stab", StringComparison.OrdinalIgnoreCase)))
		{
			var attack = stance + "_Shoot_" + (kind == "PISTOL" ? "Pistol" : kind);
			if (FindClip(attack) is { } attackClip && _viewAnimations.CurrentAnimationPosition < attackClip.Animation.Length)
			{
				UpperClip = attack;
				sampleTime = _viewAnimations.CurrentAnimationPosition;
				loopUpper = false;
			}
		}
		if (_weapon is FirearmView gun && gun.ShotsFired != _shots) { _shots = gun.ShotsFired; _kick = 0.045f; }
		_kick = Mathf.MoveToward(_kick, 0, (float)delta * 0.4f);
		// The local body is also hidden by the first-person camera's layer mask.
		// Keep its clocks live, but evaluate bones only when visible or requested.
		if (!force && !_headless && !_visibility.IsOnScreen()) return;
		_poseDirty = false;
		_posePhysicsFrame = Engine.GetPhysicsFrames();
		var poseDelta = _poseDelta;
		_poseDelta = 0;
		SampleLayer(LowerClip, !grounded ? Math.Min(_jumpTime, 0.6) : _phase, grounded, true);
		SampleLayer(UpperClip, sampleTime, loopUpper, false);
		var upperSpineBasis = _spine >= 0
			? (crouch ? _crouchingPelvisBasis : _standingPelvisBasis) * new Basis(_rotations[_spine])
			: Basis.Identity;
		var parentBasis = _spine >= 0 ? PoseBasis(_parents[_spine], _rotations) : Basis.Identity;
		// Pitch around the character's right axis in the spine's parent frame.
		if (_spine >= 0)
		{
			// The legs own pelvis yaw. Counter-rotate the spine at the layer boundary
			// so that yaw cannot turn the weapon and torso away from the aim direction.
			var upperLocal = parentBasis.Inverse() * upperSpineBasis;
			_rotations[_spine] = upperLocal.GetRotationQuaternion().Normalized();
			var axis = parentBasis.Inverse() * (Skeleton.GlobalBasis.Inverse() * _player.GlobalBasis.X);
			var pitch = Mathf.Clamp(_player.AimPitch, -1.15f, 1.15f);
			_rotations[_spine] = new Quaternion(axis.Normalized(), pitch + _kick) * _rotations[_spine];
		}
		var upperSpineRotation = _spine >= 0
			? (parentBasis * new Basis(_rotations[_spine]).ScaledLocal(_scales[_spine])).GetRotationQuaternion().Normalized() : Quaternion.Identity;
		var blend = _snap ? 1 : 1 - Mathf.Exp(-24 * (float)poseDelta);
		for (var i = 0; i < _lower.Length; i++)
		{
			var position = _previousPositions[i].Lerp(_positions[i], blend);
			var rotation = _snap ? _rotations[i] : _previousRotations[i].Slerp(_rotations[i], blend).Normalized();
			if (_snap || position != _previousPositions[i]) Skeleton.SetBonePosePosition(i, position);
			if (_snap || rotation != _previousRotations[i]) Skeleton.SetBonePoseRotation(i, rotation);
			if (_scales[i] != _previousScales[i]) Skeleton.SetBonePoseScale(i, _scales[i]);
			_previousPositions[i] = position;
			_previousRotations[i] = rotation;
			_previousScales[i] = _scales[i];
		}
		if (_spine >= 0)
		{
			// Blend torso orientation independently of the moving pelvis. Blending
			// both local rotations would briefly reintroduce hip yaw during reversals.
			var rotation = _snap ? upperSpineRotation
				: _previousUpperSpineRotation.Slerp(upperSpineRotation, blend).Normalized();
			parentBasis = PoseBasis(_parents[_spine], _previousRotations);
			_previousRotations[_spine] = (parentBasis.Inverse() * new Basis(rotation)).GetRotationQuaternion().Normalized();
			Skeleton.SetBonePoseRotation(_spine, _previousRotations[_spine]);
			_previousUpperSpineRotation = rotation;
		}
		_snap = false;
		UpdateWeaponTransform();
	}
	private Basis PoseBasis(int bone, Quaternion[] rotations)
	{
		if (bone < 0) return Basis.Identity;
		return PoseBasis(_parents[bone], rotations) * new Basis(rotations[bone]).ScaledLocal(_scales[bone]);
	}
	private void SampleLayer(string clip, double time, bool loop, bool lower)
	{
		var animation = (FindClip(clip) ?? FindClip("Idle_Upper_KNIFE")!).Animation;
		var length = Math.Max(.001, animation.Length);
		_pose.Sample(animation, loop ? time % length : Math.Clamp(time, 0, length), lower, _positions, _rotations, _scales);
	}

	private void Sample(string clip, double time, bool loop)
	{
		var cached = FindClip(clip) ?? FindClip("Idle_Upper_KNIFE")!;
		var length = Math.Max(0.001, cached.Animation.Length);
		_animations.Play(cached.Name);
		_animations.Seek(loop ? time % length : Math.Clamp(time, 0, length), true);
	}

	private Clip? FindClip(string name)
	{
		if (_clips.TryGetValue(name, out var clip)) return clip;
		var key = new StringName(name);
		if (_animations.HasAnimation(key)) clip = new(key, _animations.GetAnimation(key));
		else
		{
			key.Dispose();
			key = new StringName("arsenal/" + name);
			if (_animations.HasAnimation(key)) clip = new(key, _animations.GetAnimation(key));
			else key.Dispose();
		}
		_clips[name] = clip;
		return clip;
	}

	private void ClearClips()
	{
		foreach (var clip in _clips.Values) clip?.Name.Dispose();
		_clips.Clear();
	}

	public override void _ExitTree() => ClearClips();

	public void RemoveWorldWeapon()
	{
		_muzzle = _silencedMuzzle = null;
		if (_worldWeapon != null) { _worldWeapon.GetParent().RemoveChild(_worldWeapon); _worldWeapon.QueueFree(); _worldWeapon = null; }
		_weapon = null;
		_viewAnimations = null;
		_worldSkeleton = null;
		_mergedBones.Clear();
	}

	private void AttachWeapon(WeaponView? weapon)
	{
		RemoveWorldWeapon();
		_weapon = weapon;
		_viewAnimations = weapon?.NetworkAnimation;
		_shots = (weapon as FirearmView)?.ShotsFired ?? 0;
		var id = weapon is FirearmView gun ? gun.Kind.ToString().ToLowerInvariant()
			: weapon is GrenadeView grenade ? grenade.AssetName : weapon is C4View ? "c4" : "knife";
		var path = id == "knife" ? "res://assets/players/knife/knife_world.glb"
			: $"res://assets/weapons/{id}/world/{id}_world.glb";
		if (!_weaponScenes.TryGetValue(path, out var scene))
		{
			if (!ResourceLoader.Exists(path)) return;
			_weaponScenes[path] = scene = GD.Load<PackedScene>(path);
		}
		_worldWeapon = scene.Instantiate<Node3D>();
		_worldWeapon.Name = "WorldWeapon";
		Skeleton.AddChild(_worldWeapon);
		_muzzle = _worldWeapon.FindChild("muzzle_flash", true, false) as Node3D;
		_silencedMuzzle = _worldWeapon.FindChild("muzzle_flash_silencer", true, false) as Node3D;
		ConfigureMeshes(_worldWeapon);
		_weaponBindInverse = Transform3D.Identity;
		if (_worldWeapon.FindChild("Skeleton3D", true, false) is Skeleton3D skeleton)
		{
			_worldSkeleton = skeleton;
			for (var i = 0; i < skeleton.GetBoneCount(); i++)
			{
				var playerBone = Skeleton.FindBone(skeleton.GetBoneName(i));
				if (playerBone >= 0) _mergedBones.Add((i, playerBone));
				if (skeleton.GetBoneName(i).ToString().EndsWith("weapon_bone"))
				{
					_weaponBindInverse = (_worldWeapon.GlobalTransform.AffineInverse() * skeleton.GlobalTransform * skeleton.GetBoneGlobalRest(i)).AffineInverse();
				}
			}
		}
	}

	public bool TryGetMuzzlePosition(out Vector3 position)
	{
		// A shot RPC can arrive before the next presentation frame after a switch.
		EnsurePose();
		UpdateWeaponTransform();
		var muzzle = _weapon is FirearmView { Silenced: true } && _silencedMuzzle != null ? _silencedMuzzle : _muzzle;
		position = muzzle?.GlobalPosition ?? default;
		return muzzle != null;
	}

	public Transform3D? GetWorldWeaponTransform(WeaponView weapon)
	{
		EnsurePose();
		if (_weapon != weapon) AttachWeapon(weapon);
		UpdateWeaponTransform();
		var transform = _worldWeapon?.GlobalTransform;
		if (!_player.IsAlive) RemoveWorldWeapon();
		return transform;
	}

	private void UpdateWeaponTransform()
	{
		if (_worldWeapon == null || _weaponBone < 0) return;
		_worldWeapon.Visible = _player.IsAlive && _weapon?.Equipped == true;
		_worldWeapon.Transform = Skeleton.GetBoneGlobalPose(_weaponBone) * _weaponBindInverse;
		// Source bonemerge also moves the magazine/bolt with the player's rig.
		if (_worldSkeleton == null) return;
		var intoWeapon = _worldSkeleton.GlobalTransform.AffineInverse() * Skeleton.GlobalTransform;
		foreach (var (worldBone, playerBone) in _mergedBones)
		{
			var pose = intoWeapon * Skeleton.GetBoneGlobalPose(playerBone);
			var parent = _worldSkeleton.GetBoneParent(worldBone);
			if (parent >= 0) pose = _worldSkeleton.GetBoneGlobalPose(parent).AffineInverse() * pose;
			_worldSkeleton.SetBonePosePosition(worldBone, pose.Origin);
			_worldSkeleton.SetBonePoseRotation(worldBone, pose.Basis.GetRotationQuaternion().Normalized());
		}
	}
}
