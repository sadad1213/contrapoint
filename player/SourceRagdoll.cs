using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Contrapoint.Players;

// Stock CSS PHY shapes/masses/limits simulated with Godot's skeletal physics.
public partial class SourceRagdoll : Node3D
{
	public Skeleton3D Skeleton { get; private set; } = null!;
	public PhysicalBoneSimulator3D Simulator { get; private set; } = null!;
	public IReadOnlyList<SourceRagdollBone> Bones => _bones;
	public Vector3 InitialVelocity { get; private set; }
	public Vector3 AppliedImpulse { get; private set; }
	public string ImpulseBone { get; private set; } = "";
	public int ContactSounds { get; private set; }
	public Vector3 Focus => _bones.Count > 0 ? _bones[0].GlobalPosition : GlobalPosition;
	private readonly List<SourceRagdollBone> _bones = new();
	private double _age, _lastContact = -.2;
	private bool _started;
	private static readonly Dictionary<string, JsonElement> Definitions = new();
	private static float Number(JsonElement item, string name) => float.Parse(item.GetProperty(name).GetString()!, CultureInfo.InvariantCulture);
	private static Vector3 Point(JsonElement value) => new(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle());

	public void Initialize(Player player, Vector3 velocity, Transform3D? modelTransform = null, Transform3D[]? pose = null)
	{
		InitialVelocity = velocity;
		var model = player.Body.CloneForRagdoll();
		AddChild(model);
		model.GlobalTransform = modelTransform ?? player.Body.ModelGlobalTransform;
		Skeleton = (Skeleton3D)model.FindChild("Skeleton3D", true, false);
		for (var i = 0; i < Skeleton.GetBoneCount(); i++)
			Skeleton.SetBonePose(i, pose != null && i < pose.Length ? pose[i] : player.Body.Skeleton.GetBonePose(i));
		foreach (var animation in model.FindChildren("*", "AnimationPlayer", true, false))
			((AnimationPlayer)animation).Active = false;
		foreach (MeshInstance3D mesh in model.FindChildren("*", "MeshInstance3D", true, false)) mesh.Layers = 1;
		Simulator = new PhysicalBoneSimulator3D { Name = "RagdollPhysics", Active = false };
		Skeleton.AddChild(Simulator);
		if (!Definitions.TryGetValue(player.AgentId, out var definition))
		{
			definition = JsonDocument.Parse(FileAccess.GetFileAsString($"res://assets/physics/css_ragdolls/{player.AgentId}.json")).RootElement.Clone();
			Definitions.Add(player.AgentId, definition);
		}
		foreach (var solid in definition.GetProperty("solids").EnumerateArray())
		{
			var name = solid.GetProperty("name").GetString()!;
			var hulls = solid.GetProperty("hulls").EnumerateArray().Select(h => h.EnumerateArray().Select(Point).ToArray()).ToArray();
			var points = hulls.SelectMany(h => h).ToArray();
			var center = points.Aggregate(Vector3.Zero, (sum, p) => sum + p) / points.Length;
			var bone = new SourceRagdollBone
			{
				Name = "Physics_" + name, Ragdoll = this, Mass = Number(solid, "mass"),
				LinearDamp = Number(solid, "damping"), AngularDamp = Number(solid, "rotdamping"),
				GravityScale = player.Movement.Gravity * SourceMovement.Unit / 9.8f,
				CollisionLayer = 32, CollisionMask = 1, Friction = .8f, Bounce = 0,
				BodyOffset = new(Basis.Identity, center), JointOffset = new(Basis.Identity, -center)
			};
			Simulator.AddChild(bone); bone.Set("bone_name", name);
			foreach (var hull in hulls)
				bone.AddChild(new CollisionShape3D { Shape = new ConvexPolygonShape3D { Points = hull.Select(p => p - center).ToArray(), Margin = .002f } });
			PhysicsServer3D.BodySetMaxContactsReported(bone.GetRid(), 4);
			_bones.Add(bone);
		}
		foreach (var joint in definition.GetProperty("constraints").EnumerateArray())
		{
			var bone = _bones[(int)Number(joint, "child")]; bone.JointType = PhysicalBone3D.JointTypeEnum.Type6Dof;
			foreach (var axis in new[] { "x", "y", "z" })
			{
				bone.Set($"joint_constraints/{axis}/linear_limit_enabled", true);
				bone.Set($"joint_constraints/{axis}/linear_limit_lower", 0f);
				bone.Set($"joint_constraints/{axis}/linear_limit_upper", 0f);
				bone.Set($"joint_constraints/{axis}/angular_limit_enabled", true);
				bone.Set($"joint_constraints/{axis}/angular_limit_lower", Number(joint, axis + "min"));
				bone.Set($"joint_constraints/{axis}/angular_limit_upper", Number(joint, axis + "max"));
			}
		}
		foreach (Player body in GetTree().GetNodesInGroup("source_players")) Simulator.PhysicalBonesAddCollisionException(body.GetRid());
		Simulator.Active = SourceDeathRules.RagdollPhysics;
		if (SourceDeathRules.RagdollPhysics)
		{
			Simulator.PhysicalBonesStartSimulation();
			foreach (var bone in _bones) bone.LinearVelocity = velocity;
		}
		_started = true;
		AddToGroup("source_ragdolls");
		// Bound corpse accumulation even with instant manual respawns.
		var corpses = GetTree().GetNodesInGroup("source_ragdolls");
		for (var i = 0; i < corpses.Count - 12; i++) corpses[i].QueueFree();
	}

	public void ApplyImpact(Vector3 position, Vector3 impulse, SourceHitGroup group)
	{
		if (impulse.IsZeroApprox() || _bones.Count == 0 || !SourceDeathRules.RagdollPhysics) return;
		var bone = group == SourceHitGroup.Head ? _bones.First(b => b.Get("bone_name").AsString().EndsWith("Head1"))
			: _bones.MinBy(b => b.GlobalPosition.DistanceSquaredTo(position))!;
		AppliedImpulse = impulse; ImpulseBone = bone.Get("bone_name").AsString();
		bone.ApplyImpulse(impulse, position - bone.GlobalPosition);
	}
	public void Contact(Vector3 position, float speed)
	{
		if (!_started || _age - _lastContact < .18) return;
		_lastContact = _age;
		CallDeferred(MethodName.PlayContact, position, speed);
	}
	private void PlayContact(Vector3 position, float speed)
	{
		if (IsQueuedForDeletion()) return;
		SourceSoundPlayer.PlayWorld(this, position, speed > 2.5f ? "flesh.impacthard" : "flesh.impactsoft");
		ContactSounds++;
	}
	public override void _PhysicsProcess(double delta)
	{
		_age += delta;
		if (_age > SourceDeathRules.RagdollLifetime) QueueFree();
	}
	public override void _ExitTree()
	{
		if (GodotObject.IsInstanceValid(Simulator)) Simulator.PhysicalBonesStopSimulation();
	}
}
