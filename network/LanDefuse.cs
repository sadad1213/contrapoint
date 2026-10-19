using Godot;
using Contrapoint.Gameplay;
using Contrapoint.Players;
using GDictionary = Godot.Collections.Dictionary;

namespace Contrapoint.Network;

public partial class LanSession
{
	public void SendObjectiveInput(Player player)
	{
		if (!DefuseRules.Active || !IsClient || !WorldReady || player != LocalPlayer) return;
		RpcId(1, MethodName.ObjectiveInput, _mapEpoch, player.CaptureNetworkState(), player.ObjectiveAttackHeld, player.ObjectiveUseHeld);
	}
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ObjectiveInput(int epoch, GDictionary state, bool attack, bool use)
	{
		var sender = Multiplayer.GetRemoteSenderId();
		if (!IsHosting || !WorldReady || epoch != _mapEpoch || !DefuseRules.Active || !_readyPeers.Contains(sender)
			|| !_players.TryGetValue(sender, out var p) || !p.IsAlive || !state.TryGetValue("revision", out var rev)
			|| rev.AsInt32() != p.NetworkTeleportRevision) return;
		p.ApplyNetworkState(state, false);
		p.ObjectiveAttackHeld = attack; p.ObjectiveUseHeld = use;
		p.ObjectiveInputDeadline = Time.GetTicksMsec() + 750;
	}
	public void BroadcastDefuseAnnouncement(string path)
	{
		if (IsHosting) foreach (var peer in _readyPeers) RpcId(peer, MethodName.DefuseAnnouncement, _mapEpoch, path);
	}
	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void DefuseAnnouncement(int epoch, string path)
	{
		if (IsClient && epoch == _mapEpoch && path.StartsWith("radio/") && ValidSound(path, 1))
			LocalPlayer?.MovementAudio.Sounds.PlayWave(path, relay: false);
	}
}
