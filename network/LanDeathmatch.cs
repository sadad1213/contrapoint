using Godot;
using System.Collections.Generic;
using System.Linq;
using Contrapoint.Gameplay;
using Contrapoint.Players;
using Contrapoint.Weapons;
using GDictionary = Godot.Collections.Dictionary;
using GArray = Godot.Collections.Array;

namespace Contrapoint.Network;

public partial class LanSession
{
	private sealed record MatchIdentity(PlayerTeam Team, string Agent, string Weapon, GArray? Contents = null,
		int Money = 800, int Armor = 0, bool Helmet = false, bool Kit = false, int Kills = 0, int Deaths = 0);
	private readonly Dictionary<int, MatchIdentity> _restartRoster = new();
	private readonly HashSet<int> _waitingMapReady = new();
	private readonly HashSet<int> _waitingHello = new();
	private bool _restartingMap;
	private int _mapEpoch;
	private GDictionary? _localMatchRules;

	private static GDictionary MatchRules() => new()
	{
		["minutes"] = DeathmatchRules.TimeLimitMinutes, ["respawn"] = DeathmatchRules.RespawnSeconds,
		["protection"] = DeathmatchRules.ProtectionSeconds, ["delay"] = DeathmatchRules.RestartDelay,
		["armor"] = DeathmatchRules.SpawnArmor, ["helmet"] = DeathmatchRules.SpawnHelmet,
		["health"] = DeathmatchRules.KillHealth, ["ammo"] = DeathmatchRules.KillAmmo,
		["ffa"] = DeathmatchRules.FreeForAll,
		["de_round"] = DefuseRules.RoundMinutes, ["de_freeze"] = DefuseRules.FreezeSeconds,
		["de_buy"] = DefuseRules.BuyMinutes, ["de_bomb"] = DefuseRules.BombSeconds,
		["de_restart"] = DefuseRules.RestartSeconds, ["de_money"] = DefuseRules.StartMoney,
		["de_maxrounds"] = DefuseRules.MaxRounds, ["de_winlimit"] = DefuseRules.WinLimit,
		["de_limitteams"] = DefuseRules.LimitTeams
	};
	private static void ApplyMatchRules(GDictionary rules)
	{
		DeathmatchRules.TimeLimitMinutes = rules["minutes"].AsSingle();
		DeathmatchRules.RespawnSeconds = rules["respawn"].AsSingle();
		DeathmatchRules.ProtectionSeconds = rules["protection"].AsSingle();
		DeathmatchRules.RestartDelay = rules["delay"].AsSingle();
		DeathmatchRules.SpawnArmor = rules["armor"].AsInt32(); DeathmatchRules.SpawnHelmet = rules["helmet"].AsBool();
		DeathmatchRules.KillHealth = rules["health"].AsInt32(); DeathmatchRules.KillAmmo = rules["ammo"].AsInt32();
		DeathmatchRules.FreeForAll = rules["ffa"].AsBool();
		DefuseRules.RoundMinutes = rules["de_round"].AsSingle(); DefuseRules.FreezeSeconds = rules["de_freeze"].AsSingle();
		DefuseRules.BuyMinutes = rules["de_buy"].AsSingle(); DefuseRules.BombSeconds = rules["de_bomb"].AsSingle();
		DefuseRules.RestartSeconds = rules["de_restart"].AsSingle(); DefuseRules.StartMoney = rules["de_money"].AsInt32();
		DefuseRules.MaxRounds = rules["de_maxrounds"].AsInt32(); DefuseRules.WinLimit = rules["de_winlimit"].AsInt32();
		DefuseRules.LimitTeams = rules["de_limitteams"].AsInt32();
	}
	public void BroadcastMatchState()
	{
		if (!IsHosting || !WorldReady) return;
		foreach (var peer in _readyPeers) SendMatchState(peer);
	}
	private void SendMatchState(int peer)
	{
		var round = RoundState.Instance!;
		var scores = new GArray();
		foreach (var entry in _players)
		{
			entry.Value.MatchPingMilliseconds = entry.Key == 1 || entry.Value.IsBot ? 0 : (int)_peer!.GetPeer(entry.Key).GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTime);
			scores.Add(new GDictionary { ["id"] = entry.Key, ["kills"] = entry.Value.MatchKills,
				["deaths"] = entry.Value.MatchDeaths, ["ping"] = entry.Value.MatchPingMilliseconds, ["kit"] = entry.Value.HasDefuser });
		}
		RpcId(peer, MethodName.MatchState, _mapEpoch, MatchRules(), round.ModeId, round.RemainingSeconds,
			round.TimerRunning, round.RestartPending, round.MatchNumber, round.TerroristKills, round.CounterTerroristKills, scores,
			DefuseRules.Active ? DefuseState.Instance!.Capture() : new GDictionary());
	}
	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void MatchState(int epoch, GDictionary rules, string mode, double seconds, bool running, bool restart, int number, int t, int ct, GArray scores, GDictionary objective)
	{
		if (!IsClient || epoch != _mapEpoch) return;
		_localMatchRules ??= MatchRules();
		ApplyMatchRules(rules);
		RoundState.Instance?.Receive(mode, DeathmatchRules.FreeForAll, seconds, running, restart, number, t, ct);
		if (mode == "defuse" && WorldReady) DefuseState.Instance?.Receive(objective);
		foreach (var item in scores)
		{
			var score = item.AsGodotDictionary();
			if (!_players.TryGetValue(score["id"].AsInt32(), out var player)) continue;
			player.MatchKills = score["kills"].AsInt32(); player.MatchDeaths = score["deaths"].AsInt32();
			player.MatchPingMilliseconds = score["ping"].AsInt32();
			player.HasDefuser = score["kit"].AsBool();
		}
	}
	public void SendKillAmmunition(Player player, FirearmView gun, int amount)
	{
		if (IsHosting && _readyPeers.Contains(player.NetworkPeerId))
			RpcId(player.NetworkPeerId, MethodName.KillAmmunition, _mapEpoch, player.NetworkTeleportRevision, gun.Name.ToString(), amount);
	}
	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void KillAmmunition(int epoch, int revision, string weapon, int amount)
	{
		if (!IsClient || !WorldReady || epoch != _mapEpoch || LocalPlayer == null || LocalPlayer.NetworkTeleportRevision != revision) return;
		LocalPlayer.Inventory.Weapons.OfType<FirearmView>().FirstOrDefault(w => w.Name.ToString() == weapon)?.AddMagazineAmmo(amount);
	}

	// Keep the ENet peer and stable RPC endpoint alive while all map nodes are replaced.
	public void RestartCurrentMap()
	{
		if (IsClient || LocalPlayer == null || _restartingMap) return;
		var scene = GetTree().CurrentScene;
		if (scene == null || !AllowedMap(scene.SceneFilePath)) return;
		var path = scene.SceneFilePath;
		_restartRoster.Clear();
		foreach (var entry in _players) _restartRoster[entry.Key] = Identity(entry.Value);
		_restartRoster[IsHosting ? 1 : 0] = Identity(LocalPlayer);
		var mode = DefuseRules.Active ? "defuse" : DeathmatchRules.FreeForAll ? "deathmatch" : "teamdeathmatch";
		if (DefuseRules.Active)
			foreach (var bot in GetTree().GetNodesInGroup("source_players").OfType<Player>().Where(p => p.IsBot))
				_restartRoster[bot.NetworkPeerId] = Identity(bot);
		var map = Contrapoint.Maps.MapCatalog.Find(path)!;
		MatchSession.Begin(new MatchSetup { ModeId = mode, MapId = map.Id, FriendlyFire = SourceDamageRules.FriendlyFire,
			FallDamage = SourceDamageRules.FallDamageEnabled, Cheats = SourceDamageRules.Cheats,
			Bots = Contrapoint.Gameplay.Bots.BotManager.Instance?.Settings ?? new() });
		_mapEpoch++;
		if (IsHosting)
		{
			foreach (var peer in Multiplayer.GetPeers())
			{
				_joining[peer] = _clock + 90;
				RpcId(peer, MethodName.LoadMap, path, MapFingerprint(path), _mapEpoch);
			}
		}
		_restartingMap = true;
		ClearMapReplication();
		GetTree().Paused = false;
		CallDeferred(MethodName.ChangeMap, path);
	}
	private static MatchIdentity Identity(Player p) => DefuseRules.Active && DefuseState.Instance?.ReloadingRound == true
		? new(p.Team, p.AgentId, p.ActiveWeapon?.Name.ToString() ?? "", p.IsAlive ? p.Inventory.CaptureContents() : null,
			p.Money, p.IsAlive ? p.Armor : 0, p.IsAlive && p.HasHelmet, p.IsAlive && p.HasDefuser, p.MatchKills, p.MatchDeaths)
		: new(p.Team, p.AgentId, p.ActiveWeapon?.Name.ToString() ?? "");
	private void ClearMapReplication()
	{
		WorldReady = false; LocalPlayer = null; _scene = null; _print = null;
		_players.Clear(); _readyPeers.Clear(); _lastAction.Clear();
		_entities.Clear(); _grenades.Clear(); _smokes.Clear(); _worldSent.Clear();
		_interpolatingEntities.Clear();
		_detonated.Clear(); _projectileSequences.Clear(); _receivedWorld = false;
	}
	private void RestoreRestartIdentity(int id, Player player)
	{
		if (_restartRoster.Remove(id, out var identity))
		{
			player.RestoreMatchIdentity(identity.Team, identity.Agent, identity.Weapon);
			if (DefuseRules.Active)
			{
				player.Money = identity.Money; player.Armor = identity.Armor; player.HasHelmet = identity.Helmet;
				player.HasDefuser = identity.Kit; player.MatchKills = identity.Kills; player.MatchDeaths = identity.Deaths;
				if (identity.Contents != null)
					player.Inventory.ApplyContents(player.Inventory.NetworkRevision, identity.Contents, identity.Weapon);
				LanSession.Instance?.BroadcastInventory(player); LanSession.Instance?.BroadcastVitals(player);
			}
		}
	}
	private void RestoreMatchRules()
	{
		if (_localMatchRules != null) ApplyMatchRules(_localMatchRules);
		_localMatchRules = null;
		RoundState.Instance?.BeginMatch("sandbox");
	}
}
