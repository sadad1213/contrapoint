using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Contrapoint.Players;

// Stock studio hitbox set 0, in bone-local Source units (not movement hull bands).
public sealed class SourceHitboxes
{
	internal const float BoundsRadius = 4;
	private readonly record struct Box(int Bone, SourceHitGroup Group, Vector3 Min, Vector3 Max);
	private readonly List<Box> _boxes = new();
	private readonly Skeleton3D _skeleton;
	private readonly Action? _ensurePose;
	private readonly Func<Vector3>? _boundsCenter;
	private readonly Func<Transform3D> _skeletonTransform;
	private static readonly Dictionary<string, JsonElement> Sets = new();
	public int Count => _boxes.Count;
	public SourceHitboxes(Skeleton3D skeleton, string agent, Func<Transform3D>? skeletonTransform = null, Action? ensurePose = null, Func<Vector3>? boundsCenter = null)
	{
		_skeleton = skeleton;
		_ensurePose = ensurePose;
		_boundsCenter = boundsCenter;
		_skeletonTransform = skeletonTransform ?? (() => skeleton.GlobalTransform);
		if (!Sets.TryGetValue(agent, out var set))
		{
			set = JsonDocument.Parse(FileAccess.GetFileAsString($"res://assets/physics/css_damage/{agent}_hitboxes.json")).RootElement.Clone();
			Sets.Add(agent, set);
		}
		foreach (var box in set.EnumerateArray())
		{
			var name = box.GetProperty("bone").GetString()!;
			var bone = skeleton.FindBone(name);
			if (bone < 0) throw new InvalidOperationException($"CSS hitbox bone missing: {agent}/{name}");
			var min = box.GetProperty("min"); var max = box.GetProperty("max");
			_boxes.Add(new(bone, (SourceHitGroup)box.GetProperty("group").GetInt32(),
				new Vector3(min[0].GetSingle(), min[1].GetSingle(), min[2].GetSingle()) * SourceMovement.Unit,
				new Vector3(max[0].GetSingle(), max[1].GetSingle(), max[2].GetSingle()) * SourceMovement.Unit));
		}
	}
	public Vector3 GroupCenter(SourceHitGroup group)
	{
		_ensurePose?.Invoke();
		if (group == SourceHitGroup.Head)
			foreach (var box in _boxes)
				if (box.Group == group && _skeleton.GetBoneName(box.Bone).ToString().EndsWith("Head1"))
					return (_skeletonTransform() * _skeleton.GetBoneGlobalPose(box.Bone)) * ((box.Min + box.Max) * .5f);
		foreach (var box in _boxes)
			if (box.Group == group) return (_skeletonTransform() * _skeleton.GetBoneGlobalPose(box.Bone)) * ((box.Min + box.Max) * .5f);
		throw new ArgumentException($"No CSS hitbox for {group}");
	}
	public bool Trace(Vector3 from, Vector3 to, out float fraction, out SourceHitGroup group, out Vector3 normal)
	{
		fraction = 1; group = SourceHitGroup.Generic; normal = Vector3.Zero;
		if (_boundsCenter != null)
		{
			// A generous 4 m radius contains the stock CSS poses and lets distant
			// misses avoid requesting an invisible player's bones at all.
			var segment = to - from;
			var center = _boundsCenter();
			var along = Mathf.Clamp((center - from).Dot(segment) / Mathf.Max(segment.LengthSquared(), .000001f), 0, 1);
			if (center.DistanceSquaredTo(from + segment * along) > BoundsRadius * BoundsRadius) return false;
		}
		_ensurePose?.Invoke();
		var found = false;
		var skeletonTransform = _skeletonTransform();
		foreach (var box in _boxes)
		{
			var transform = skeletonTransform * _skeleton.GetBoneGlobalPose(box.Bone);
			var inverse = transform.AffineInverse();
			var start = inverse * from; var direction = inverse * to - start;
			var enter = 0f; var exit = fraction; var face = Vector3.Zero;
			var intersects = true;
			for (var axis = 0; axis < 3; axis++)
			{
				if (Mathf.Abs(direction[axis]) < .0000001f)
				{
					if (start[axis] < box.Min[axis] || start[axis] > box.Max[axis]) { intersects = false; break; }
					continue;
				}
				var near = (box.Min[axis] - start[axis]) / direction[axis];
				var far = (box.Max[axis] - start[axis]) / direction[axis];
				if (near > far) (near, far) = (far, near);
				if (near > enter) { enter = near; face = Vector3.Zero; face[axis] = -Mathf.Sign(direction[axis]); }
				exit = Mathf.Min(exit, far);
				if (enter > exit) { intersects = false; break; }
			}
			if (!intersects || enter < 0 || enter >= fraction) continue;
			fraction = enter; group = box.Group; normal = (transform.Basis * face).Normalized(); found = true;
		}
		return found;
	}
}
