using Godot;
using Contrapoint.Players;

namespace Contrapoint.Network;

public partial class LanSession
{
	public bool RequestTeamSelection(Player player, int team, int playerClass)
	{
		if (!IsClient || player != LocalPlayer) return false;
		if (WorldReady) RpcId(1, MethodName.TeamSelectionRequested, team, playerClass);
		return true;
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void TeamSelectionRequested(int team, int playerClass)
	{
		var sender = Multiplayer.GetRemoteSenderId();
		if (!IsHosting || !_readyPeers.Contains(sender) || !_players.TryGetValue(sender, out var player)) return;
		if (team is >= 0 and <= 3 && playerClass == -1) player.JoinTeam(team);
		else if (team == -1 && playerClass is >= 0 and <= 8) player.JoinClass(playerClass);
		else if (team == -1 && playerClass == -2) player.CancelClassSelection();
	}

	public void BroadcastTeamSelection(Player player)
	{
		if (!IsHosting || !WorldReady || !_players.ContainsKey(player.NetworkPeerId)) return;
		player.NetworkTeleportRevision++;
		foreach (var peer in _readyPeers) RpcId(peer, MethodName.TeamSelection,
			player.NetworkPeerId, player.NetworkTeleportRevision, (int)player.Team, player.AgentId, player.PickingClass, player.ClassSelectionRequired);
	}

	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void TeamSelection(int id, int revision, int team, string agent, bool picking, bool required)
	{
		if (!IsClient || !_players.TryGetValue(id, out var player) || revision < player.NetworkTeleportRevision) return;
		player.NetworkTeleportRevision = revision;
		player.ApplyTeamSelection((PlayerTeam)team, agent, picking, required);
	}
}
