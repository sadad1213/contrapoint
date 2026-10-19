using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using Contrapoint.Players;
using Contrapoint.Weapons;
using GDictionary = Godot.Collections.Dictionary;

namespace Contrapoint.Network;

public partial class LanSession
{
	public bool RequestUse()
	{
		if (!IsClient) return false;
		if (WorldReady) RpcId(1, MethodName.UseRequested, LocalPlayer!.CaptureNetworkState());
		return true;
	}
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void UseRequested(GDictionary state)
	{
		var player = ActionPlayer(state);
		if (player != null) player.UseWorld();
	}

	private Player? ActionPlayer(GDictionary state)
	{
		var sender = Multiplayer.GetRemoteSenderId();
		if (!IsHosting || !_readyPeers.Contains(sender) || !_players.TryGetValue(sender, out var player)) return null;
		if (!state.TryGetValue("map_epoch", out var epoch) || epoch.AsInt32() != _mapEpoch) return null;
		if (_lastAction.TryGetValue(sender, out var previous) && _clock - previous < .025) return null;
		_lastAction[sender] = _clock;
		if (!state.ContainsKey("revision") || state["revision"].AsInt32() != player.NetworkTeleportRevision) return null;
		player.ApplyNetworkState(state, false);
		return player.IsAlive ? player : null;
	}

	public void RequestAttack(Player player, WeaponView weapon, Transform3D aim, bool knife, bool stab)
	{
		if (IsClient && WorldReady) RpcId(1, MethodName.AttackRequested, player.CaptureNetworkState(), weapon.Name.ToString(), aim, knife, stab);
	}
	public void RequestPellets(Player player, FirearmView weapon, Transform3D[] pellets)
	{
		if (IsClient && WorldReady) RpcId(1, MethodName.PelletsRequested, player.CaptureNetworkState(),
			weapon.Name.ToString(), new Godot.Collections.Array<Transform3D>(pellets));
	}
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void PelletsRequested(GDictionary state, string weaponName, Godot.Collections.Array<Transform3D> pellets)
	{
		var player = ActionPlayer(state);
		if (player == null || Contrapoint.Gameplay.DefuseRules.Active && Contrapoint.Gameplay.DefuseState.Instance?.CanAttack(player) == false || player.Inventory.Weapons.FirstOrDefault(w => w.Name.ToString() == weaponName)
			is not FirearmView { IsShotgun: true } gun || pellets.Count != gun.PelletCount) return;
		foreach (var aim in pellets)
			if (!aim.Origin.IsFinite() || !aim.Basis.IsFinite() || aim.Origin.DistanceTo(player.Camera.GlobalPosition) > .5f) return;
		player.EndSpawnProtection();
		foreach (var aim in pellets) player.Combat.ResolveNetworkAttack(gun, aim, false, false);
	}
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void AttackRequested(GDictionary state, string weaponName, Transform3D aim, bool knife, bool stab)
	{
		var player = ActionPlayer(state);
		if (player == null || Contrapoint.Gameplay.DefuseRules.Active && Contrapoint.Gameplay.DefuseState.Instance?.CanAttack(player) == false || !aim.Origin.IsFinite() || !aim.Basis.IsFinite() || aim.Origin.DistanceTo(player.Camera.GlobalPosition) > .5f) return;
		var weapon = player.Inventory.Weapons.FirstOrDefault(w => w.Name.ToString() == weaponName);
		if (weapon == null || knife != (weapon is KnifeView)) return;
		player.EndSpawnProtection();
		player.Combat.ResolveNetworkAttack(weapon, aim, knife, stab, state.TryGetValue("zoom", out var zoom) && zoom.AsInt32() > 0);
	}

	public void BroadcastImpact(Player owner, Transform3D aim, Vector3 end, Vector3 normal, Node3D? target, bool knife, bool stab)
	{
		if (!IsHosting) return;
		foreach (var peer in _readyPeers)
			if (peer != owner.NetworkPeerId) RpcId(peer, MethodName.AttackImpact, owner.NetworkPeerId, aim, end, normal, target != null ? _scene!.GetPathTo(target).ToString() : "", knife, stab);
	}
	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void AttackImpact(int owner, Transform3D aim, Vector3 end, Vector3 normal, string target, bool knife, bool stab)
	{
		if (!IsClient || LocalPlayer == null) return;
		var source = _players.GetValueOrDefault(owner);
		(source ?? LocalPlayer).Combat.PresentNetworkImpact(aim, end, normal, target.Length > 0 ? _scene?.GetNodeOrNull<Node3D>(target) : null, knife, stab);
	}

	public bool RequestGrenade(GrenadeView weapon, bool drop)
	{
		if (!IsClient) return false;
		if (WorldReady) RpcId(1, MethodName.GrenadeRequested, LocalPlayer!.CaptureNetworkState(), weapon.Name.ToString(), drop);
		return true;
	}
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void GrenadeRequested(GDictionary state, string name, bool drop)
	{
		var player = ActionPlayer(state);
		if (player == null) return;
		if (Contrapoint.Gameplay.DefuseRules.Active && Contrapoint.Gameplay.DefuseState.Instance?.CanAttack(player) == false) return;
		var weapon = player.Inventory.Weapons.OfType<GrenadeView>().FirstOrDefault(w => w.Name.ToString() == name);
		if (weapon != null) { player.EndSpawnProtection(); GrenadeProjectile.Throw(weapon, player.Camera, drop); }
	}

	public bool ForwardTeleport(Player player, Vector3 position, float yaw, float pitch)
	{
		if (!IsHosting || player.LocalInput || !_readyPeers.Contains(player.NetworkPeerId)) return false;
		player.NetworkTeleportRevision++;
		RpcId(player.NetworkPeerId, MethodName.TeleportPlayer, player.NetworkTeleportRevision, position, yaw, pitch);
		return false;
	}
	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void TeleportPlayer(int revision, Vector3 position, float yaw, float pitch)
	{
		if (!IsClient || LocalPlayer == null) return;
		LocalPlayer.NetworkTeleportRevision = revision; LocalPlayer.SourceTeleport(position, yaw, pitch);
	}

	public bool RequestPropPush(RigidBody3D prop, Vector3 position, Vector3 impulse)
	{
		if (!IsClient) return false;
		if (WorldReady && _scene != null) RpcId(1, MethodName.PushRequested, _scene.GetPathTo(prop).ToString(), position, impulse);
		return true;
	}
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = 2)]
	private void PushRequested(string path, Vector3 position, Vector3 impulse)
	{
		var sender = Multiplayer.GetRemoteSenderId();
		if (!IsHosting || !_readyPeers.Contains(sender) || !_players.TryGetValue(sender, out var player) || !position.IsFinite() || !impulse.IsFinite()) return;
		if (position.DistanceTo(player.GlobalPosition + Vector3.Up * .7f) > 2.5f) return;
		if (_entities.TryGetValue(path, out var entity) && entity.Node is RigidBody3D { Freeze: false } body && position.DistanceTo(body.GlobalPosition) < 5)
			body.ApplyImpulse(impulse.LimitLength(.7f), position - body.GlobalPosition);
	}

	public void SendSound(Player player, string path, float gain)
	{
		if (!IsActive || !WorldReady) return;
		if (IsClient) RpcId(1, MethodName.SoundRequested, path, gain);
		else RelaySound(player.NetworkPeerId, player.GlobalPosition + Vector3.Up, path, gain);
	}
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SoundRequested(string path, float gain)
	{
		var sender = Multiplayer.GetRemoteSenderId();
		if (!IsHosting || !_readyPeers.Contains(sender) || !_players.TryGetValue(sender, out var player)) return;
		if (ValidSound(path, gain)) SourceSoundPlayer.NotifySound(player, player.GlobalPosition + Vector3.Up, path);
		RelaySound(sender, player.GlobalPosition + Vector3.Up, path, gain);
	}
	private void RelaySound(int owner, Vector3 position, string path, float gain)
	{
		if (!ValidSound(path, gain)) return;
		if (owner > 1 && _scene is Node3D world) SourceSoundPlayer.PlayWorldWave(world, position, path, gain);
		foreach (var peer in _readyPeers) if (peer != owner) RpcId(peer, MethodName.WorldSound, position, path, gain);
	}
	private static bool ValidSound(string path, float gain) => path.Length < 200 && !path.Contains("..") && !path.Contains(':') && !path.StartsWith('/') && !path.Contains('\\') && float.IsFinite(gain) && gain is > 0 and <= 4 && ResourceLoader.Exists("res://assets/audio/css/" + path);
	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void WorldSound(Vector3 position, string path, float gain)
	{
		if (IsClient && _scene is Node3D world && ValidSound(path, gain)) SourceSoundPlayer.PlayWorldWave(world, position, path, gain);
	}

	public void Flash(Player player, float intensity, float facing)
	{
		if (IsHosting && !player.LocalInput && _readyPeers.Contains(player.NetworkPeerId)) RpcId(player.NetworkPeerId, MethodName.FlashPlayer, intensity, facing);
	}
	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void FlashPlayer(float intensity, float facing) { if (IsClient) LocalPlayer?.GrenadeVision.Flash(intensity, facing); }

	public void BroadcastWorldScript(Vector3 position, string name)
	{
		if (IsHosting) foreach (var peer in _readyPeers) RpcId(peer, MethodName.WorldScriptSound, position, name);
	}
	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void WorldScriptSound(Vector3 position, string name)
	{
		if (IsClient && _scene is Node3D world) SourceSoundPlayer.PlayWorld(world, position, name);
	}
	public void MapSound(string path, Vector3 position)
	{
		if (IsHosting) foreach (var peer in _readyPeers) RpcId(peer, MethodName.MapSoundEvent, path, position);
	}
	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void MapSoundEvent(string path, Vector3 position)
	{
		if (!IsClient || _scene == null || !path.StartsWith("res://assets/maps/", StringComparison.Ordinal) || path.Contains("..") || !ResourceLoader.Exists(path)) return;
		var voice = new AudioStreamPlayer3D { Stream = GD.Load<AudioStream>(path), UnitSize = 3 };
		_scene.AddChild(voice); voice.GlobalPosition = position; voice.Finished += voice.QueueFree; voice.Play();
	}
}
