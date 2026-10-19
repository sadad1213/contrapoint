using Godot;
using System.Linq;
using Contrapoint.Players;

namespace Contrapoint.Network;

public partial class LanSession
{
	public void RegisterBot(Player player)
	{
		RestoreRestartIdentity(player.NetworkPeerId, player);
		if (!IsHosting || !WorldReady || !player.IsBot || player.NetworkPeerId >= 0) return;
		if (_players.TryGetValue(player.NetworkPeerId, out var existing) && existing == player) return;
		_players[player.NetworkPeerId] = player;
		foreach (var other in _players.Values.Where(p => p != player))
		{ other.AddCollisionExceptionWith(player); player.AddCollisionExceptionWith(other); }
		foreach (var peer in _readyPeers) RpcId(peer, MethodName.PlayerJoined, player.NetworkPeerId, player.CaptureNetworkState(true));
	}
	public void UnregisterBot(Player player)
	{
		if (!IsHosting || !_players.Remove(player.NetworkPeerId)) return;
		foreach (var peer in _readyPeers) RpcId(peer, MethodName.PlayerLeft, player.NetworkPeerId);
	}
}
