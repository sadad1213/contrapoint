using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Contrapoint.Players;
using Contrapoint.UI;
using Contrapoint.Weapons;
using Contrapoint.Maps;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

namespace Contrapoint.Network;

// One stable RPC path across the menu, map loading and every peer's player tree.
public partial class LanSession : Node
{
	public const int Protocol = 9, MaxPlayers = 16;
	public static int Port { get; } = TestPort();
	private static int TestPort()
	{
		var argument = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--lan-port="));
		return argument != null && int.TryParse(argument[11..], out var port) && port is > 0 and <= 65535 ? port : 27015;
	}
	public static LanSession? Instance { get; private set; }
	public bool IsActive => _peer != null;
	public bool IsClient => IsActive && !_hosting;
	public bool IsHosting => IsActive && _hosting;
	public bool WorldReady { get; private set; }
	public int MapEpoch => _mapEpoch;
	public Player? LocalPlayer { get; private set; }
	public IReadOnlyDictionary<int, Player> Players => _players;
	private ENetMultiplayerPeer? _peer;
	private bool _hosting;
	private Node? _scene;
	private readonly Dictionary<int, Player> _players = new();
	private readonly HashSet<int> _readyPeers = new();
	private readonly Dictionary<int, double> _joining = new();
	private readonly Dictionary<int, double> _lastAction = new();
	private double _clock, _sendAt, _deadline, _worldSendAt;
	private string _pendingMap = "", _failure = "";
	private bool _loading, _receivedWorld;
	private Action<string, bool>? _print;
	private int _sequence;

	public override void _Ready()
	{
		Instance = this;
		ProcessMode = ProcessModeEnum.Always;
		GetTree().NodeAdded += InvalidateWorldPresentation;
		GetTree().NodeRemoved += InvalidateWorldPresentation;
		Multiplayer.PeerConnected += PeerConnected;
		Multiplayer.PeerDisconnected += PeerDisconnected;
		Multiplayer.ConnectedToServer += () => RpcId(1, MethodName.Hello, Protocol);
		Multiplayer.ConnectionFailed += () => Fail("Connection failed.");
		Multiplayer.ServerDisconnected += () => Fail("Server disconnected.");
		ConsoleCommands.Register("lan", (a, _, p) =>
		{
			if (a.Count == 1) { p("\"lan\" = \"" + (IsHosting ? 1 : 0) + "\"", false); return; }
			if (a.Count != 2 || a[1] is not ("0" or "1")) { p("Usage: lan <0|1>", true); return; }
			if (IsClient) { p("Only the host can change LAN state.", true); return; }
			if (a[1] == "1") Open(p); else { Stop(); p("LAN closed.", false); }
		}, () => new[] { "0", "1" });
		ConsoleCommands.Register("connect", (a, _, p) =>
		{
			if (a.Count != 2) { p("Usage: connect <address[:port]>", true); return; }
			Connect(a[1], p);
		});
	}

	public static bool TryEndpoint(string input, out string address, out int port)
	{
		address = input; port = Port;
		var parts = input.Split(':');
		if (parts.Length > 2 || parts.Length == 2 && (!int.TryParse(parts[1], out port) || port is < 1 or > 65535)) return false;
		address = parts[0];
		return address.Length > 0 && Uri.CheckHostName(address) is UriHostNameType.IPv4 or UriHostNameType.Dns;
	}

	public static IEnumerable<string> Addresses()
	{
		// Include every active IPv4 adapter, including virtual adapters. No vendor labels.
		return NetworkInterface.GetAllNetworkInterfaces()
			.Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
			.SelectMany(n => n.GetIPProperties().UnicastAddresses)
			.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
			.Select(a => a.Address.ToString()).Distinct().OrderBy(a => a, StringComparer.Ordinal);
	}

	public void Open(Action<string, bool> print)
	{
		if (LocalPlayer == null || !GodotObject.IsInstanceValid(LocalPlayer) || !AllowedMap(LocalPlayer.GetTree().CurrentScene?.SceneFilePath ?? ""))
		{ print("LAN requires a loaded map.", true); return; }
		_print = print;
		if (!IsHosting)
		{
			var peer = new ENetMultiplayerPeer();
			var error = peer.CreateServer(Port, MaxPlayers - 1, 3);
			if (error != Error.Ok) { peer.Dispose(); print("Could not open LAN: " + error, true); return; }
			_peer = peer;
			_hosting = true;
			Multiplayer.MultiplayerPeer = peer;
			GetTree().Paused = false;
			_scene = GetTree().CurrentScene;
			_players[1] = LocalPlayer;
			LocalPlayer.NetworkPeerId = 1;
			CollectWorld();
			WorldReady = true;
			foreach (var bot in GetTree().GetNodesInGroup("source_players").OfType<Player>().Where(p => p.IsBot)) RegisterBot(bot);
			foreach (var projectile in Descendants(_scene!).OfType<GrenadeProjectile>()) RegisterProjectile(projectile);
			foreach (var cloud in Descendants(_scene!).OfType<GrenadeSmoke>()) RegisterSmoke(cloud);
		}
		print("LAN " + Port, false);
		try { foreach (var address in Addresses()) print(address + ":" + Port, false); }
		catch (NetworkInformationException) { print("Could not enumerate local addresses.", true); }
	}

	public void Connect(string endpoint, Action<string, bool> print)
	{
		if (!TryEndpoint(endpoint, out var address, out var port)) { print("Invalid server address.", true); return; }
		Stop();
		_print = print;
		var peer = new ENetMultiplayerPeer();
		var error = peer.CreateClient(address, port, 3);
		if (error != Error.Ok) { peer.Dispose(); print("Could not connect: " + error, true); return; }
		_peer = peer;
		_hosting = false;
		Multiplayer.MultiplayerPeer = peer;
		_deadline = _clock + 12;
		GetTree().Paused = false;
		print("Connecting to " + address + ":" + port, false);
	}

	private static bool AllowedMap(string path) => MapCatalog.Discover().Any(map => map.ScenePath == path);
	private void PeerConnected(long id) { if (IsHosting) _joining[(int)id] = _clock + 90; }
	private void PeerDisconnected(long id)
	{
		var key = (int)id;
		_joining.Remove(key); _readyPeers.Remove(key); _lastAction.Remove(key);
		_worldSent.Remove(key);
		_restartRoster.Remove(key); _waitingMapReady.Remove(key); _waitingHello.Remove(key);
		RemovePlayer(key);
		if (IsHosting) CallDeferred(MethodName.NotifyPlayerLeft, key);
	}
	private void NotifyPlayerLeft(int key)
	{
		// Defer until the multiplayer poll has removed every departing peer.
		// Two clients can leave in the same poll; neither should receive a notice.
		if (IsHosting) foreach (var peer in _readyPeers)
			if (_peer!.GetPeer(peer).GetState() == ENetPacketPeer.PeerState.Connected) RpcId(peer, MethodName.PlayerLeft, key);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void Hello(int protocol)
	{
		var sender = Multiplayer.GetRemoteSenderId();
		if (!IsHosting || !_joining.ContainsKey(sender)) return;
		if (protocol != Protocol) { RpcId(sender, MethodName.Rejected, "Incompatible network protocol."); return; }
		if (_scene != null) RpcId(sender, MethodName.LoadMap, _scene.SceneFilePath, MapFingerprint(_scene.SceneFilePath), _mapEpoch);
		else _waitingHello.Add(sender);
	}

	private static string MapFingerprint(string path)
	{
		var files = new[] { path, "res://assets/maps/source_brush.gd", "res://assets/maps/source_prop.gd", "res://assets/maps/source_trigger.gd" };
		var map = MapCatalog.Find(path) ?? throw new ArgumentException("Unknown map scene.", nameof(path));
		return string.Join(":", files.Append(map.NativeScenePath).Select(FileAccess.GetSha256));
	}

	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void Rejected(string reason) => Fail(reason);

	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void LoadMap(string path, string fingerprint, int epoch)
	{
		if (!IsClient || !AllowedMap(path)) return;
		if (epoch == _mapEpoch && _pendingMap == path && (_loading || WorldReady)) return;
		if (MapFingerprint(path) != fingerprint) { Fail("Map version mismatch."); return; }
		if (WorldReady && LocalPlayer != null) _restartRoster[LocalPlayer.NetworkPeerId] = Identity(LocalPlayer);
		_mapEpoch = epoch;
		ClearMapReplication();
		_pendingMap = path; _deadline = _clock + 90; _loading = true;
		_print = null; // The old console is destroyed during the scene switch.
		LocalPlayer = null;
		GetTree().Paused = false;
		CallDeferred(MethodName.ChangeMap, path);
	}

	private void ChangeMap(string path)
	{
		if (GetTree().ChangeSceneToFile(path) != Error.Ok) Fail("Could not load server map.");
	}

	public void Attach(Player player)
	{
		LocalPlayer = player;
		if (_restartingMap && !IsClient)
		{
			_restartingMap = false;
			_scene = GetTree().CurrentScene;
			var id = IsHosting ? 1 : 0;
			player.NetworkPeerId = id;
			if (IsHosting) _players[id] = player;
			RestoreRestartIdentity(id, player);
			if (IsHosting)
			{
				CollectWorld(); WorldReady = true;
				foreach (var peer in _waitingHello) RpcId(peer, MethodName.LoadMap, _scene!.SceneFilePath, MapFingerprint(_scene.SceneFilePath), _mapEpoch);
				_waitingHello.Clear();
				foreach (var peer in _waitingMapReady.ToArray()) CompleteMapReady(peer);
				_waitingMapReady.Clear();
			}
		}
		if (_loading && IsClient && GetTree().CurrentScene?.SceneFilePath == _pendingMap)
		{
			_loading = false;
			_scene = GetTree().CurrentScene;
			player.NetworkPeerId = Multiplayer.GetUniqueId();
			_players[player.NetworkPeerId] = player;
			CollectWorld();
			player.SetPhysicsProcess(false);
			RpcId(1, MethodName.MapReady, _mapEpoch);
		}
		if (_failure.Length > 0) { player.Console?.Print(_failure, true); _failure = ""; }
	}

	public void Detach(Player player) { if (LocalPlayer == player) LocalPlayer = null; }

	public override void _ExitTree()
	{
		GetTree().NodeAdded -= InvalidateWorldPresentation;
		GetTree().NodeRemoved -= InvalidateWorldPresentation;
		_peer?.Close(); _peer?.Dispose(); _peer = null;
		LocalPlayer = null; _scene = null; _print = null;
		ClearWeaponPickups();
		_players.Clear(); _entities.Clear(); _grenades.Clear(); _smokes.Clear();
		_interpolatingEntities.Clear();
		Instance = null;
		PlayerSceneCache.Release();
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void MapReady(int epoch)
	{
		var sender = Multiplayer.GetRemoteSenderId();
		if (!IsHosting || epoch != _mapEpoch || !_joining.ContainsKey(sender)) return;
		if (!WorldReady || _scene == null) { _waitingMapReady.Add(sender); return; }
		CompleteMapReady(sender);
	}
	private void CompleteMapReady(int sender)
	{
		if (!_joining.Remove(sender)) return;
		var player = SpawnPlayer(sender);
		RestoreRestartIdentity(sender, player);
		SendDamageRules(sender);
		SendMatchState(sender);
		foreach (var entry in _players) RpcId(sender, MethodName.PlayerJoined, entry.Key, entry.Value.CaptureNetworkState(true));
		foreach (var entry in _players.Values) if (entry.Death.Active) SendDeath(entry, sender);
		foreach (var peer in _readyPeers) RpcId(peer, MethodName.PlayerJoined, sender, player.CaptureNetworkState(true));
		SendWorld(sender, true);
		RpcId(sender, MethodName.TeamSelection, sender, player.NetworkTeleportRevision, (int)player.Team, player.AgentId, player.PickingClass, player.ClassSelectionRequired);
		RpcId(sender, MethodName.BeginPlay, player.GlobalTransform, player.ActiveWeapon?.Name.ToString() ?? "");
		_readyPeers.Add(sender);
	}

	private Player SpawnPlayer(int id)
	{
		if (_players.TryGetValue(id, out var existing)) return existing;
		var player = PlayerSceneCache.Load("res://scenes/player/player.tscn").Instantiate<Player>();
		player.Name = "Peer_" + id; player.LocalInput = false; player.NetworkPeerId = id;
		player.Team = PlayerTeam.Unassigned;
		player.SpawnPointsRootPath = LocalPlayer!.SpawnPointsRootPath;
		_scene!.AddChild(player);
		foreach (var weapon in player.Inventory.Weapons) { weapon.SetProcess(false); weapon.SetPhysicsProcess(false); }
		_players[id] = player;
		// Prediction does not resolve player/player penetration; keep LAN tests nonblocking.
		foreach (var other in _players.Values)
		{
			if (other == player) continue;
			other.AddCollisionExceptionWith(player); player.AddCollisionExceptionWith(other);
		}
		return player;
	}

	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void PlayerJoined(int id, GDictionary state)
	{
		if (!IsClient || _scene == null) return;
		var player = id == Multiplayer.GetUniqueId() ? LocalPlayer! : SpawnPlayer(id);
		if (state.TryGetValue("bot", out var bot)) player.IsBot = bot.AsBool();
		if (state.TryGetValue("display_name", out var displayName)) player.BotDisplayName = displayName.AsString();
		if (player.LocalInput) player.NetworkTeleportRevision = state["revision"].AsInt32();
		if (state.TryGetValue("money", out var money)) player.Money = money.AsInt32();
		if (state.TryGetValue("inventory", out var inventory)) player.Inventory.ApplyContents(state["inventory_revision"].AsInt32(), inventory.AsGodotArray(), state["weapon"].AsString());
		if (!player.LocalInput) player.ApplyNetworkState(state, true);
	}

	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void BeginPlay(Transform3D spawn, string weapon)
	{
		if (!IsClient || LocalPlayer == null) return;
		LocalPlayer.SetSpawn(spawn); LocalPlayer.Respawn(restoreLoadout: !Contrapoint.Gameplay.DefuseRules.Active);
		if (_restartRoster.Remove(LocalPlayer.NetworkPeerId, out var identity)) weapon = identity.Weapon;
		var selected = LocalPlayer.Inventory.Weapons.FirstOrDefault(w => w.Name.ToString() == weapon);
		if (selected != null) LocalPlayer.Inventory.Equip(selected);
		WorldReady = true; _deadline = 0;
		LocalPlayer.SetPhysicsProcess(true);
		if (LocalPlayer.AwaitingInitialClass) LocalPlayer.TeamMenu?.OpenInitial();
		LocalPlayer.Console?.Print("Connected.");
	}

	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void PlayerLeft(int id) => RemovePlayer(id);
	private void RemovePlayer(int id)
	{
		if (_players.Remove(id, out var player) && GodotObject.IsInstanceValid(player) && player != LocalPlayer) player.QueueFree();
	}

	public override void _Process(double delta)
	{
		_clock += delta;
		if (IsClient && _receivedWorld) InterpolateWorld(delta);
		if (_failure.Length > 0 && GetTree().CurrentScene?.SceneFilePath == "res://scenes/main_menu.tscn")
		{
			var console = GetTree().CurrentScene.FindChild("DeveloperConsole", true, false) as DeveloperConsole;
			if (console != null) { console.Print(_failure, true); console.SetOpen(true); _failure = ""; }
		}
		if (!IsActive) return;
		if (_deadline > 0 && _clock > _deadline) { Fail("Connection timed out."); return; }
		if (_scene != null && !GodotObject.IsInstanceValid(_scene)) { Stop(); return; }
		if (IsHosting) foreach (var peer in _joining.Where(p => p.Value < _clock).Select(p => p.Key).ToArray()) _peer!.DisconnectPeer(peer);
		if (!WorldReady || LocalPlayer == null || _clock < _sendAt) return;
		_sendAt = _clock + 1.0 / 30;
		if (IsClient)
		{
			using var state = LocalPlayer.CaptureNetworkState();
			using var wireState = Variant.From(state);
			RpcId(1, MethodName.SubmitPlayer, wireState);
		}
		else
		{
			if (_readyPeers.Count == 0) return;
			CleanProjectiles();
			var worldUpdate = _clock >= _worldSendAt;
			if (worldUpdate) _worldSendAt = _clock + .1;
			var world = worldUpdate ? CaptureWorld() : null;
			var players = _players.Select(entry => (Id: entry.Key, State: entry.Value.CaptureNetworkState())).ToArray();
			try
			{
				foreach (var entry in players)
				{
					// RpcId serializes immediately. Share one Variant across recipients,
					// then release the native reference without waiting for finalizers.
					using var wireState = Variant.From(entry.State);
					foreach (var peer in _readyPeers)
						if (entry.Id != peer) RpcId(peer, MethodName.PlayerStates, entry.Id, wireState);
				}
			}
			finally { foreach (var entry in players) entry.State.Dispose(); }
			foreach (var peer in _readyPeers)
			{
				if (worldUpdate) SendWorld(peer, false, world);
				SendProjectiles(peer);
				SendWeaponPhysics(peer);
			}
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 1)]
	private void SubmitPlayer(GDictionary state)
	{
		var sender = Multiplayer.GetRemoteSenderId();
		if (!IsHosting || !_readyPeers.Contains(sender) || !_players.TryGetValue(sender, out var player)) return;
		if (!state.TryGetValue(PlayerStateKeys.MapEpoch, out var epoch) || epoch.AsInt32() != _mapEpoch) return;
		if (state.TryGetValue(PlayerStateKeys.Revision, out var revision) && revision.AsInt32() == player.NetworkTeleportRevision) player.ApplyNetworkState(state, false);
	}

	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 1)]
	private void PlayerStates(int id, GDictionary state)
	{
		if (!WorldReady) return;
		if (!state.TryGetValue(PlayerStateKeys.MapEpoch, out var epoch) || epoch.AsInt32() != _mapEpoch) return;
		if (_players.TryGetValue(id, out var player) && !player.LocalInput) player.ApplyNetworkState(state, false);
	}

	private void Fail(string reason)
	{
		var wasClient = IsClient;
		var print = _print;
		Stop();
		if (wasClient) CallDeferred(MethodName.ConnectionEnded, reason);
		else { print?.Invoke(reason, true); _failure = ""; }
	}

	private void ConnectionEnded(string reason)
	{
		GetTree().ChangeSceneToFile("res://scenes/main_menu.tscn");
		_failure = reason;
	}

	public void Stop()
	{
		var wasClient = IsClient;
		RestoreDamageRules();
		RestoreDeathRules();
		if (wasClient) RestoreMatchRules();
		WorldReady = false;
		if (_peer != null)
		{
			_peer.Close();
			using var offline = new OfflineMultiplayerPeer();
			Multiplayer.MultiplayerPeer = offline;
		}
		_peer?.Dispose(); _peer = null;
		foreach (var id in _players.Keys.ToArray())
		{
			if (!wasClient && _players[id].IsBot) _players.Remove(id);
			else RemovePlayer(id);
		}
		_players.Clear(); _readyPeers.Clear(); _joining.Clear(); _lastAction.Clear();
		_restartRoster.Clear(); _waitingMapReady.Clear(); _waitingHello.Clear(); _restartingMap = false;
		RestoreWorld(); _detonated.Clear(); _projectileSequences.Clear();
		_scene = null; _pendingMap = ""; _loading = false; _receivedWorld = false; _deadline = 0;
		if (GodotObject.IsInstanceValid(LocalPlayer)) { LocalPlayer!.NetworkPeerId = 0; LocalPlayer.SetPhysicsProcess(true); }
		_print = null;
		GetTree().Paused = false;
	}
}
