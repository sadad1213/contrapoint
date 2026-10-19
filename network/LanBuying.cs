using Godot;
using Contrapoint.Players;

namespace Contrapoint.Network;

public partial class LanSession
{
	public bool RequestBuy(Player player, string item)
	{
		if (!IsClient) return false;
		if (WorldReady && player == LocalPlayer) RpcId(1, MethodName.BuyRequested, _mapEpoch, item);
		return true;
	}
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void BuyRequested(int epoch, string item)
	{
		var sender = Multiplayer.GetRemoteSenderId();
		if (!IsHosting || epoch != _mapEpoch || !_readyPeers.Contains(sender) || !_players.TryGetValue(sender, out var player)) return;
		var error = player.Buy(item);
		RpcId(sender, MethodName.BuyResult, epoch, error);
	}
	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void BuyResult(int epoch, string error)
	{
		if (!IsClient || epoch != _mapEpoch) return;
		LocalPlayer?.BuyMenu?.ReceiveResult(error);
		if (error.Length > 0) LocalPlayer?.Console?.Print(error, true);
	}
}
