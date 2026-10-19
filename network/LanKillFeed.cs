using Godot;
using Contrapoint.Players;
using GDictionary = Godot.Collections.Dictionary;

namespace Contrapoint.Network;

public partial class LanSession
{
	public void BroadcastKillNotice(KillNotice notice)
	{
		if (!IsHosting || !WorldReady) return;
		foreach (var peer in _readyPeers) RpcId(peer, MethodName.PlayerKilled, notice.Pack());
	}
	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void PlayerKilled(GDictionary notice)
	{
		if (IsClient && WorldReady) KillNotice.Deliver(this, KillNotice.Unpack(notice));
	}
}
