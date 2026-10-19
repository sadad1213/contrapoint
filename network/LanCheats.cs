using Godot;
using System;
using Contrapoint.Players;
using Contrapoint.UI;
using Contrapoint.Weapons;
using GDictionary = Godot.Collections.Dictionary;

namespace Contrapoint.Network;

public partial class LanSession
{
	public bool RequestPlayerCheat(Player player, string command, string argument, Action<string, bool> print)
	{
		if (!IsClient) return false;
		if (WorldReady && player == LocalPlayer)
			RpcId(1, MethodName.PlayerCheatRequested, player.CaptureNetworkState(), command, argument);
		else print("Wait until the server finishes loading the map.", true);
		return true;
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void PlayerCheatRequested(GDictionary state, string command, string argument)
	{
		var sender = Multiplayer.GetRemoteSenderId();
		if (!IsHosting || !WorldReady || !_readyPeers.Contains(sender) || !_players.TryGetValue(sender, out var player)) return;
		if (!state.TryGetValue("map_epoch", out var epoch) || epoch.AsInt32() != _mapEpoch
			|| !state.TryGetValue("revision", out var revision) || revision.AsInt32() != player.NetworkTeleportRevision) return;
		// Only these two commands may run on behalf of a connected player.
		// Validate tokens before building a console line; never accept arbitrary console input.
		if (command == "god" ? argument is not ("" or "0" or "1")
			: command != "ent_create" || !WeaponPickup.IsSupported(argument)) return;
		player.ApplyNetworkState(state, false);
		ConsoleCommands.Execute(command + " " + argument, player.Movement,
			(line, error) => RpcId(sender, MethodName.PlayerCheatResult, _mapEpoch, player.GodMode, line, error));
	}

	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void PlayerCheatResult(int epoch, bool god, string line, bool error)
	{
		if (!IsClient || !WorldReady || epoch != _mapEpoch || LocalPlayer == null) return;
		LocalPlayer.GodMode = god;
		LocalPlayer.Console?.Print(line, error);
	}
}
