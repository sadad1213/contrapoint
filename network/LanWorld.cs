using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Contrapoint.Players;
using Contrapoint.Weapons;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

namespace Contrapoint.Network;

public partial class LanSession
{
	private sealed class WorldEntity
	{
		public required Node3D Node;
		public string[] Properties = [];
		public bool PhysicsProcess, Process, Freeze, Replica;
		public int Sequence = -1;
		public Transform3D Target;
		public bool HasTarget;
		public bool Interpolating;
		public (AnimationPlayer Player, string Path)[]? Animations;
		public AudioStreamPlayer3D? Voice;
		public bool VoiceCached;
	}
	private readonly record struct WorldSnapshot(string Path, GDictionary State, byte[] Bytes);
	private readonly Dictionary<string, WorldEntity> _entities = new(StringComparer.Ordinal);
	private readonly List<WorldEntity> _interpolatingEntities = new();
	private readonly Dictionary<int, GrenadeProjectile> _grenades = new();
	private readonly Dictionary<int, GrenadeSmoke> _smokes = new();
	private readonly Dictionary<int, Dictionary<string, (byte[] Bytes, double Time)>> _worldSent = new();
	private int _nextProjectile;
	private static readonly HashSet<string> WorldProperties = new(StringComparer.Ordinal)
	{
		"progress", "opened", "close_remaining", "health", "broken", "button_remaining",
		"enabled", "remaining", "fired", "cooldown", "age", "running", "elapsed", "finished"
	};

	private static IEnumerable<Node> Descendants(Node root)
	{
		foreach (var child in root.GetChildren())
		{
			if (child is Player) continue;
			yield return child;
			foreach (var nested in Descendants(child)) yield return nested;
		}
	}

	private void CollectWorld()
	{
		_entities.Clear();
		_interpolatingEntities.Clear();
		if (_scene == null) return;
		foreach (var pickup in Descendants(_scene).OfType<WeaponPickup>().ToArray())
		{
			if (IsClient) pickup.QueueFree(); else RegisterWeaponPickup(pickup);
		}
		foreach (var node in Descendants(_scene).OfType<Node3D>().Prepend(_scene as Node3D).Where(n => n != null))
		{
			if (node is WeaponPickup) continue;
			var script = node!.GetScript().AsGodotObject() as Script;
			if (node is not RigidBody3D && (script == null || !script.ResourcePath.StartsWith("res://assets/maps/source_", StringComparison.Ordinal)
				|| script.ResourcePath.EndsWith("source_soundscapes.gd") || script.ResourcePath.EndsWith("source_beam.gd"))) continue;
			var properties = node.GetPropertyList().Select(p => p["name"].AsString()).Where(WorldProperties.Contains).ToArray();
			var entity = new WorldEntity { Node = node, Properties = properties, PhysicsProcess = node.IsPhysicsProcessing(), Process = node.IsProcessing(), Freeze = node is RigidBody3D r && r.Freeze, Replica = IsClient };
			_entities[_scene.GetPathTo(node).ToString()] = entity;
			if (!IsClient) continue;
			node.SetPhysicsProcess(false);
			// Fish and the KZ display can render locally from their synchronized clock.
			if (script == null || !script.ResourcePath.EndsWith("source_fish.gd") && !script.ResourcePath.EndsWith("source_kz.gd")) node.SetProcess(false);
			if (node is RigidBody3D body) { body.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic; body.Freeze = true; }
		}
	}

	private void RestoreWorld()
	{
		ClearWeaponPickups();
		foreach (var entity in _entities.Values)
		{
			if (!entity.Replica || !GodotObject.IsInstanceValid(entity.Node)) continue;
			entity.Node.SetPhysicsProcess(entity.PhysicsProcess); entity.Node.SetProcess(entity.Process);
			if (entity.Node is RigidBody3D body) body.Freeze = entity.Freeze;
		}
		_entities.Clear(); _grenades.Clear(); _smokes.Clear(); _worldSent.Clear();
		_interpolatingEntities.Clear();
	}

	private void InterpolateWorld(double delta)
	{
		var weight = 1 - Mathf.Exp(-30 * (float)delta);
		for (var i = _interpolatingEntities.Count - 1; i >= 0; i--)
		{
			var entity = _interpolatingEntities[i];
			if (GodotObject.IsInstanceValid(entity.Node) && !entity.Node.IsQueuedForDeletion())
			{
				var transform = entity.Node.Transform.InterpolateWith(entity.Target, weight);
				if (!transform.IsEqualApprox(entity.Target)) { entity.Node.Transform = transform; continue; }
				entity.Node.Transform = entity.Target;
			}
			entity.Interpolating = false;
			_interpolatingEntities.RemoveAt(i);
		}
	}

	private void InvalidateWorldPresentation(Node node)
	{
		if (node is AnimationPlayer)
		{
			foreach (var entity in _entities.Values)
				if (GodotObject.IsInstanceValid(entity.Node) && (entity.Node.IsAncestorOf(node)
					|| entity.Animations?.Any(a => a.Player == node) == true)) entity.Animations = null;
		}
		else if (node is AudioStreamPlayer3D)
		{
			var parent = node.GetParent();
			foreach (var entity in _entities.Values)
				if (entity.Node == parent || entity.Voice == node) entity.VoiceCached = false;
		}
	}

	private static AudioStreamPlayer3D? WorldVoice(WorldEntity entity)
	{
		if (!entity.VoiceCached)
		{
			entity.Voice = entity.Node.GetChildren().OfType<AudioStreamPlayer3D>().FirstOrDefault();
			entity.VoiceCached = true;
		}
		return GodotObject.IsInstanceValid(entity.Voice) ? entity.Voice : null;
	}

	private GDictionary CaptureEntity(string path, WorldEntity entity)
	{
		var node = entity.Node;
		var state = new GDictionary { ["path"] = path };
		if (!GodotObject.IsInstanceValid(node) || node.IsQueuedForDeletion()) { state["gone"] = true; return state; }
		state["transform"] = node.Transform; state["visible"] = node.Visible;
		if (node is CollisionObject3D collision) { state["layer"] = collision.CollisionLayer; state["mask"] = collision.CollisionMask; }
		if (node is RigidBody3D body)
		{
			state["linear"] = body.LinearVelocity; state["angular"] = body.AngularVelocity;
			state["freeze"] = body.Freeze; state["sleeping"] = body.Sleeping;
		}
		var properties = new GDictionary();
		foreach (var name in entity.Properties) properties[name] = node.Get(name);
		state["properties"] = properties;
		var animations = new GArray();
		entity.Animations ??= node.FindChildren("*", "AnimationPlayer", true, false).OfType<AnimationPlayer>()
			.Select(a => (a, node.GetPathTo(a).ToString())).ToArray();
		foreach (var (animation, animationPath) in entity.Animations)
			if (GodotObject.IsInstanceValid(animation) && !animation.AssignedAnimation.IsEmpty)
				animations.Add(new GDictionary { ["path"] = animationPath, ["clip"] = animation.AssignedAnimation.ToString(), ["time"] = animation.CurrentAnimationPosition, ["playing"] = animation.IsPlaying() });
		state["animations"] = animations;
		var voice = WorldVoice(entity);
		if (voice != null) state["sound"] = voice.Playing;
		return state;
	}

	private List<WorldSnapshot> CaptureWorld()
	{
		var snapshot = new List<WorldSnapshot>(_entities.Count);
		foreach (var entry in _entities)
		{
			var state = CaptureEntity(entry.Key, entry.Value);
			snapshot.Add(new(entry.Key, state, GD.VarToBytes(state)));
		}
		return snapshot;
	}

	private void SendWorld(int peer, bool initial, List<WorldSnapshot>? snapshot = null)
	{
		if (initial) SendWeaponPickups(peer);
		// Chunks bound datagram size. Per-entity sequence numbers allow unordered chunks.
		var batch = new GArray();
		var bytes = 0;
		var sequence = ++_sequence;
		if (!_worldSent.TryGetValue(peer, out var sent)) _worldSent[peer] = sent = new(StringComparer.Ordinal);
		foreach (var entry in snapshot ?? CaptureWorld())
		{
			var state = entry.State;
			var encoded = entry.Bytes;
			if (!initial && sent.TryGetValue(entry.Path, out var last) && _clock - last.Time < 1 && encoded.AsSpan().SequenceEqual(last.Bytes)) continue;
			sent[entry.Path] = (encoded, _clock);
			var size = encoded.Length;
			if (bytes + size > 1000 && batch.Count > 0)
			{
				RpcId(peer, initial ? MethodName.InitialWorld : MethodName.WorldStates, _mapEpoch, sequence, batch);
				batch = new(); bytes = 0;
			}
			if (size > 1000) { RpcId(peer, MethodName.InitialWorld, _mapEpoch, sequence, new GArray { state }); continue; }
			batch.Add(state); bytes += size;
		}
		if (batch.Count > 0) RpcId(peer, initial ? MethodName.InitialWorld : MethodName.WorldStates, _mapEpoch, sequence, batch);
		foreach (var smoke in _smokes.Where(p => GodotObject.IsInstanceValid(p.Value)))
			RpcId(peer, MethodName.SmokeState, _mapEpoch, smoke.Key, smoke.Value.GlobalPosition, smoke.Value.Age);
	}

	private void SendProjectiles(int peer)
	{
		foreach (var projectile in _grenades.Values.Where(GodotObject.IsInstanceValid))
			RpcId(peer, MethodName.ProjectileState, _mapEpoch, ++_sequence, projectile.NetworkId, (int)projectile.Kind, projectile.Thrower?.NetworkPeerId ?? 1, projectile.GlobalTransform, projectile.VisualRotation, projectile.Velocity);
	}

	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void InitialWorld(int epoch, int sequence, GArray batch) { if (IsClient && epoch == _mapEpoch) { ApplyWorld(sequence, batch); _receivedWorld = true; } }
	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = 2)]
	private void WorldStates(int epoch, int sequence, GArray batch) { if (IsClient && epoch == _mapEpoch && _receivedWorld) ApplyWorld(sequence, batch); }
	private void ApplyWorld(int sequence, GArray batch)
	{
		foreach (var item in batch)
		{
			var state = item.AsGodotDictionary();
			if (!_entities.TryGetValue(state["path"].AsString(), out var entity) || sequence <= entity.Sequence) continue;
			entity.Sequence = sequence;
			if (!GodotObject.IsInstanceValid(entity.Node)) continue;
			var node = entity.Node;
			if (state.ContainsKey("gone")) { node.QueueFree(); continue; }
			entity.Target = state["transform"].AsTransform3D();
			if (!WorldReady || !entity.HasTarget || node.Position.DistanceTo(entity.Target.Origin) > 4) node.Transform = entity.Target;
			if (!entity.Interpolating && !node.Transform.IsEqualApprox(entity.Target))
			{
				entity.Interpolating = true;
				_interpolatingEntities.Add(entity);
			}
			entity.HasTarget = true; node.Visible = state["visible"].AsBool();
			if (node is RigidBody3D body) { body.LinearVelocity = state["linear"].AsVector3(); body.AngularVelocity = state["angular"].AsVector3(); }
			if (node is CollisionObject3D collision) { collision.CollisionLayer = state["layer"].AsUInt32(); collision.CollisionMask = state["mask"].AsUInt32(); }
			foreach (var property in state["properties"].AsGodotDictionary()) node.Set(property.Key.AsString(), property.Value);
			foreach (var value in state["animations"].AsGodotArray())
			{
				var animation = value.AsGodotDictionary();
				var player = node.GetNodeOrNull<AnimationPlayer>(animation["path"].AsString());
				if (player == null || !player.HasAnimation(animation["clip"].AsString())) continue;
				player.Play(animation["clip"].AsString()); player.Seek(animation["time"].AsDouble(), true);
				if (!animation["playing"].AsBool()) player.Pause();
			}
			if (state.ContainsKey("sound") && WorldVoice(entity) is { } voice && voice.Playing != state["sound"].AsBool())
			{ if (state["sound"].AsBool()) voice.Play(); else voice.Stop(); }
		}
	}

	public void RegisterProjectile(GrenadeProjectile projectile)
	{
		if (!IsHosting) return;
		projectile.NetworkId = ++_nextProjectile; _grenades[projectile.NetworkId] = projectile;
	}
	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = 2)]
	private void ProjectileState(int epoch, int sequence, int id, int kind, int owner, Transform3D transform, Vector3 rotation, Vector3 velocity)
	{
		if (!IsClient || epoch != _mapEpoch || _scene == null) return;
		if (_detonated.Contains(id)) return;
		if (_projectileSequences.TryGetValue(id, out var previous) && previous >= sequence) return;
		_projectileSequences[id] = sequence;
		if (!_grenades.TryGetValue(id, out var projectile) || !GodotObject.IsInstanceValid(projectile))
		{
			projectile = new GrenadeProjectile { Name = "Grenade_" + id, NetworkId = id, NetworkReplica = true };
			projectile.ConfigureNetwork((GrenadeKind)kind, _players.GetValueOrDefault(owner));
			_scene.AddChild(projectile); _grenades[id] = projectile;
		}
		projectile.GlobalTransform = transform; projectile.VisualRotation = rotation; projectile.Velocity = velocity;
	}
	private readonly HashSet<int> _detonated = new();
	private readonly Dictionary<int, int> _projectileSequences = new();
	private void CleanProjectiles()
	{
		foreach (var id in _grenades.Where(p => !GodotObject.IsInstanceValid(p.Value)).Select(p => p.Key).ToArray())
		{
			_grenades.Remove(id);
			foreach (var peer in _readyPeers) RpcId(peer, MethodName.ProjectileRemoved, id);
		}
		foreach (var id in _smokes.Where(p => !GodotObject.IsInstanceValid(p.Value)).Select(p => p.Key).ToArray()) _smokes.Remove(id);
	}
	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ProjectileRemoved(int id)
	{
		if (!IsClient) return;
		_detonated.Add(id);
		if (_grenades.Remove(id, out var projectile) && GodotObject.IsInstanceValid(projectile)) projectile.QueueFree();
	}
	public void ProjectileDetonated(GrenadeProjectile projectile)
	{
		if (!IsHosting) return;
		foreach (var peer in _readyPeers) RpcId(peer, MethodName.DetonateProjectile, projectile.NetworkId, (int)projectile.Kind, projectile.GlobalPosition);
		_grenades.Remove(projectile.NetworkId);
	}
	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void DetonateProjectile(int id, int kind, Vector3 position)
	{
		if (!IsClient || _scene is not Node3D world || !_detonated.Add(id)) return;
		if (_grenades.Remove(id, out var projectile) && GodotObject.IsInstanceValid(projectile)) projectile.QueueFree();
		if ((GrenadeKind)kind != GrenadeKind.Smoke) GrenadeBurst.Spawn(world, position, (GrenadeKind)kind == GrenadeKind.Flashbang);
		SourceSoundPlayer.PlayWorld(world, position, (GrenadeKind)kind switch { GrenadeKind.HE => "basegrenade.explode", GrenadeKind.Flashbang => "flashbang.explode", _ => "basesmokeeffect.sound" });
	}
	public void RegisterSmoke(GrenadeSmoke smoke) { if (IsHosting) _smokes[++_nextProjectile] = smoke; }
	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = 2)]
	private void SmokeState(int epoch, int id, Vector3 position, double age)
	{
		if (!IsClient || epoch != _mapEpoch || _scene == null) return;
		if (!_smokes.TryGetValue(id, out var cloud) || !GodotObject.IsInstanceValid(cloud))
		{
			cloud = new GrenadeSmoke { Name = "CSSSmoke_" + id }; _scene.AddChild(cloud); cloud.GlobalPosition = position; _smokes[id] = cloud;
		}
		cloud.SetNetworkAge(age);
	}
}
