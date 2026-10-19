using Godot;
using Contrapoint.Players;

namespace Contrapoint.Network;

public partial class LanSession
{
	private (bool FriendlyFire, bool Fall, bool Legacy, bool Cheats, bool Spectators)? _localDamageRules;
	private void RestoreDamageRules()
	{
		if (_localDamageRules is not { } rules) return;
		SourceDamageRules.FriendlyFire = rules.FriendlyFire; SourceDamageRules.FallDamageEnabled = rules.Fall;
		SourceDamageRules.LegacyGrenadeDamage = rules.Legacy; Contrapoint.UI.ConsoleCommands.SetCheats(rules.Cheats, GetTree());
		Player.AllowSpectators = rules.Spectators;
		_localDamageRules = null;
	}
	public void BroadcastDamageRules()
	{
		BroadcastMatchState();
		if (!IsHosting || !WorldReady) return;
		foreach (var peer in _readyPeers) SendDamageRules(peer);
	}
	private void SendDamageRules(int peer)
	{
		RpcId(peer, MethodName.DamageRules, SourceDamageRules.FriendlyFire, SourceDamageRules.FallDamageEnabled, SourceDamageRules.LegacyGrenadeDamage, SourceDamageRules.Cheats, Player.AllowSpectators);
		SendDeathRules(peer);
	}
	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void DamageRules(bool friendlyFire, bool fall, bool legacy, bool cheats, bool spectators)
	{
		if (!IsClient) return;
		_localDamageRules ??= (SourceDamageRules.FriendlyFire, SourceDamageRules.FallDamageEnabled,
			SourceDamageRules.LegacyGrenadeDamage, SourceDamageRules.Cheats, Player.AllowSpectators);
		SourceDamageRules.FriendlyFire = friendlyFire; SourceDamageRules.FallDamageEnabled = fall;
		SourceDamageRules.LegacyGrenadeDamage = legacy; Contrapoint.UI.ConsoleCommands.SetCheats(cheats, GetTree());
		Player.AllowSpectators = spectators;
	}

	public void BroadcastDamageFeedback(Player player, PlayerDamageKind kind, SourceHitGroup group,
		int healthDamage, int armorDamage, Vector3 punch, float modifier, string sound, string deathSound,
		Vector3 position, Vector3 direction, float bloodMagnitude, bool helmetHit)
	{
		if (!IsHosting || !WorldReady) return;
		foreach (var peer in _readyPeers)
			RpcId(peer, MethodName.DamageFeedback, player.NetworkPeerId, (int)kind, (int)group, healthDamage,
				armorDamage, punch, modifier, sound, deathSound, position, direction, bloodMagnitude, helmetHit);
	}

	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void DamageFeedback(int id, int kind, int group, int healthDamage, int armorDamage,
		Vector3 punch, float modifier, string sound, string deathSound, Vector3 position, Vector3 direction, float bloodMagnitude, bool helmetHit)
	{
		if (IsClient && WorldReady && _players.TryGetValue(id, out var player))
			player.PresentDamageFeedback((PlayerDamageKind)kind, (SourceHitGroup)group, healthDamage,
				armorDamage, punch, modifier, sound, deathSound, position, direction, bloodMagnitude, helmetHit);
	}

	public void BroadcastVitals(Player player)
	{
		if (!IsHosting || !WorldReady) return;
		foreach (var peer in _readyPeers)
			RpcId(peer, MethodName.PlayerVitals, player.NetworkPeerId, player.Health, player.Armor, player.HasHelmet, player.Money);
	}

	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void PlayerVitals(int id, int health, int armor, bool helmet, int money)
	{
		if (IsClient && WorldReady && _players.TryGetValue(id, out var player)) { player.Money = money; player.SetVitals(health, armor, helmet); }
	}

	public bool RequestFallDamage(Player player, float speed)
	{
		if (!IsClient) return false;
		if (WorldReady && player == LocalPlayer && speed > 580)
			RpcId(1, MethodName.FallDamageRequested, speed, player.NetworkTeleportRevision);
		return true;
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void FallDamageRequested(float speed, int revision)
	{
		var sender = Multiplayer.GetRemoteSenderId();
		if (!IsHosting || !_readyPeers.Contains(sender) || !_players.TryGetValue(sender, out var player)
			|| !float.IsFinite(speed) || speed <= 580 || speed > 10000 || revision != player.NetworkTeleportRevision) return;
		// Movement is owner-simulated in this LAN implementation; only the host changes HP.
		player.ApplyFallDamage(speed);
	}

	public bool RequestRespawn(Player player)
	{
		if (!IsClient || !WorldReady || player != LocalPlayer) return false;
		RpcId(1, MethodName.RespawnRequested);
		return true;
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void RespawnRequested()
	{
		var sender = Multiplayer.GetRemoteSenderId();
		if (!IsHosting || !_readyPeers.Contains(sender) || !_players.TryGetValue(sender, out var player)) return;
		if (player.IsAlive && !SourceDamageRules.Cheats || !player.IsAlive && !player.Death.CanRespawn) return;
		player.Respawn();
	}

	public bool RequestKill(Player player, int amount)
	{
		if (!IsClient) return false;
		if (WorldReady && player == LocalPlayer && amount >= player.Health) RpcId(1, MethodName.KillRequested);
		return true;
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void KillRequested()
	{
		var sender = Multiplayer.GetRemoteSenderId();
		if (IsHosting && _readyPeers.Contains(sender) && _players.TryGetValue(sender, out var player)) player.TakeDamage(player.Health);
	}
}
