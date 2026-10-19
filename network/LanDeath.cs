using Godot;
using Contrapoint.Players;
using GDictionary = Godot.Collections.Dictionary;

namespace Contrapoint.Network;

public partial class LanSession
{
	private (bool Auto, float Delay, float Freeze, float Travel, bool Disabled)? _localDeathRules;
	private void SendDeathRules(int peer) => RpcId(peer, MethodName.DeathRules,
		SourceDeathRules.AutoRespawn, SourceDeathRules.RespawnDelay, SourceDeathRules.FreezeSeconds, SourceDeathRules.FreezeTravelSeconds, SourceDeathRules.ServerDisableFreezeCam);
	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void DeathRules(bool automatic, float delay, float freeze, float travel, bool disabled)
	{
		if (!IsClient) return;
		_localDeathRules ??= (SourceDeathRules.AutoRespawn, SourceDeathRules.RespawnDelay, SourceDeathRules.FreezeSeconds, SourceDeathRules.FreezeTravelSeconds, SourceDeathRules.ServerDisableFreezeCam);
		SourceDeathRules.AutoRespawn = automatic; SourceDeathRules.RespawnDelay = delay;
		SourceDeathRules.FreezeSeconds = freeze; SourceDeathRules.FreezeTravelSeconds = travel;
		SourceDeathRules.ServerDisableFreezeCam = disabled;
	}
	private void RestoreDeathRules()
	{
		if (_localDeathRules is not { } rules) return;
		SourceDeathRules.AutoRespawn = rules.Auto; SourceDeathRules.RespawnDelay = rules.Delay;
		SourceDeathRules.FreezeSeconds = rules.Freeze; SourceDeathRules.FreezeTravelSeconds = rules.Travel;
		SourceDeathRules.ServerDisableFreezeCam = rules.Disabled;
		_localDeathRules = null;
	}
	public void BroadcastDeath(Player player)
	{
		if (!IsHosting || !WorldReady) return;
		foreach (var peer in _readyPeers) SendDeath(player, peer);
	}
	private void SendDeath(Player player, int peer) => RpcId(peer, MethodName.PlayerDeath,
		player.NetworkPeerId, player.NetworkTeleportRevision, player.Death.CaptureContext(), player.Armor, player.HasHelmet);
	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void PlayerDeath(int id, int revision, GDictionary context, int armor, bool helmet)
	{
		if (!IsClient || !_players.TryGetValue(id, out var player) || revision < player.NetworkTeleportRevision) return;
		player.Death.ReceiveContext(context);
		player.SetVitals(0, armor, helmet);
	}
	public void BroadcastRespawn(Player player)
	{
		if (!IsHosting || !WorldReady || !_players.TryGetValue(player.NetworkPeerId, out var registered) || registered != player) return;
		player.NetworkTeleportRevision++;
		BroadcastInventory(player);
		foreach (var peer in _readyPeers) RpcId(peer, MethodName.PlayerRespawn,
			player.NetworkPeerId, player.NetworkTeleportRevision, player.GlobalTransform);
	}
	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void PlayerRespawn(int id, int revision, Transform3D spawn)
	{
		if (!IsClient || !_players.TryGetValue(id, out var player) || revision < player.NetworkTeleportRevision) return;
		player.NetworkTeleportRevision = revision;
		var ready = WorldReady; WorldReady = false;
		// WeaponInventory arrives reliably before this event. Keep the server's
		// weapons/selection instead of creating and then discarding a default rig.
		player.SetSpawn(spawn); player.Respawn(restoreLoadout: false);
		WorldReady = ready;
	}
}
