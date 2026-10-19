using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Contrapoint.Network;
using Contrapoint.Players;
using Contrapoint.Weapons;

namespace Contrapoint.Gameplay.Bots;

// Scene-owned: AI and navigation disappear together on map switches.
public partial class BotManager : Node
{
	public static BotManager? Instance { get; private set; }
	public static BotSetup Defaults { get; set; } = new();
	public BotSetup Settings { get; private set; } = new();
	public IReadOnlyList<BotController> Bots => _bots;
	public bool NavigationReady => _region != null && NavigationServer3D.MapGetIterationId(_navigation) > 0;
	public bool Supported => DeathmatchRules.Active && NavigationPath(_map.SceneFilePath) != null;
	private static string? NavigationPath(string scenePath) => scenePath switch
	{
		"res://assets/maps/2000/2000.scn" => "res://assets/maps/2000/bot_navigation.tres",
		"res://assets/maps/de_cache/de_cache.scn" => "res://assets/maps/de_cache/bot_navigation.tres",
		"res://assets/maps/de_dust2/de_dust2.scn" => "res://assets/maps/de_dust2/bot_navigation.tres",
		"res://assets/maps/de_inferno/de_inferno.scn" => "res://assets/maps/de_inferno/bot_navigation.tres",
		"res://assets/maps/de_cpl_strike/de_cpl_strike.scn" => "res://assets/maps/de_cpl_strike/bot_navigation.tres",
		_ => null
	};
	private readonly List<BotController> _bots = [];
	private readonly List<Player> _players = [];
	private readonly List<GrenadeSmoke> _smoke = [];
	private readonly Godot.Collections.Array<Rid> _sightExcluded = new();
	private ulong _perceptionFrame = ulong.MaxValue;
	internal List<Player> Players { get { RefreshPerception(); return _players; } }
	internal List<GrenadeSmoke> Smoke { get { RefreshPerception(); return _smoke; } }
	internal Godot.Collections.Array<Rid> SightExcluded { get { RefreshPerception(); return _sightExcluded; } }
	private Player _local = null!;
	private Node3D _map = null!;
	private NavigationRegion3D? _region;
	private Rid _navigation;
	private Marker3D[] _patrolPoints = [];
	private double _reconcileAt;
	private int _nextId = -1;
	private bool _lastClient;
	public static void Attach(Player player)
	{
		if (Instance != null || player.SpawnPointsRootPath.IsEmpty) return;
		var map = player.GetNodeOrNull<Node3D>(player.SpawnPointsRootPath);
		if (map == null || NavigationPath(map.SceneFilePath) == null) return;
		var manager = new BotManager { Name = "Bots", _local = player, _map = map,
			Settings = MatchSession.Current?.Bots ?? Defaults };
		player.GetParent().AddChild(manager);
	}
	public override void _Ready()
	{
		Instance = this;
		SourceSoundPlayer.Emitted += Hear;
		_patrolPoints = _map.FindChildren("Spawn_deathmatch_*", "Marker3D", true, false).OfType<Marker3D>().ToArray();
	}
	public override void _ExitTree()
	{
		SourceSoundPlayer.Emitted -= Hear;
		((Godot.Collections.Array)_sightExcluded).Dispose();
		if (Instance == this) Instance = null;
	}
	// Share scene queries between all brains for this physics tick. Rebuild the
	// native exclusion array only when the roster changes, not for every ray.
	private void RefreshPerception()
	{
		var frame = Engine.GetPhysicsFrames();
		if (_perceptionFrame == frame) return;
		_perceptionFrame = frame;
		var nodes = GetTree().GetNodesInGroup("source_players");
		using var ownedNodes = (Godot.Collections.Array)nodes;
		var count = 0;
		var changed = false;
		foreach (var node in nodes)
		{
			if (node is not Player player || player.IsQueuedForDeletion()) continue;
			if (count == _players.Count) { _players.Add(player); changed = true; }
			else if (_players[count] != player) { _players[count] = player; changed = true; }
			count++;
		}
		if (count < _players.Count) { _players.RemoveRange(count, _players.Count - count); changed = true; }
		if (changed)
		{
			_sightExcluded.Clear();
			foreach (var player in _players) _sightExcluded.Add(player.GetRid());
		}
		_smoke.Clear();
		var smokeNodes = GetTree().GetNodesInGroup("source_smoke");
		using var ownedSmokeNodes = (Godot.Collections.Array)smokeNodes;
		foreach (var node in smokeNodes)
			if (node is GrenadeSmoke smoke && !smoke.IsQueuedForDeletion()) _smoke.Add(smoke);
	}
	public void Configure(BotSetup settings) { Defaults = Settings = settings; _reconcileAt = 0; }
	public static int DesiredCount(BotSetup settings, int humans) => settings.Difficulty == "none" ? 0 :
		Math.Clamp(settings.QuotaMode switch { "fill" => settings.Quota - humans,
			"match" => settings.Quota * humans, _ => settings.Quota }, 0, Math.Max(0, LanSession.MaxPlayers - humans));
	public override void _PhysicsProcess(double delta)
	{
		var client = LanSession.Instance?.IsClient == true;
		if (client != _lastClient) { Clear(); _lastClient = client; }
		if (client) return;
		if (!Supported) { if (_bots.Count > 0) Clear(); return; }
		if (_region == null && (Settings.Difficulty == "none" || Settings.Quota <= 0) && _bots.Count == 0) return;
		if (_region == null)
		{
			_navigation = _map.GetWorld3D().NavigationMap;
			NavigationServer3D.MapSetCellSize(_navigation, .2f);
			NavigationServer3D.MapSetCellHeight(_navigation, .1f);
			_region = new NavigationRegion3D { Name = "BotNavigation", NavigationMesh =
				GD.Load<NavigationMesh>(NavigationPath(_map.SceneFilePath)!) };
			_map.AddChild(_region);
			return;
		}
		if (!NavigationReady) return;
		_reconcileAt -= delta;
		if (_reconcileAt > 0) return;
		_reconcileAt = .5;
		_bots.RemoveAll(b => !IsInstanceValid(b) || b.IsQueuedForDeletion());
		var humans = GetTree().GetNodesInGroup("source_players").OfType<Player>().Where(p => !p.IsBot).ToArray();
		var count = Settings.JoinAfterPlayer && !humans.Any(p => DeathmatchRules.Playing(p)) ? 0 : DesiredCount(Settings, humans.Length);
		while (_bots.Count > count) Remove(_bots[^1]);
		// Spread model/inventory creation across ticks on slower hosts.
		if (_bots.Count < count) Add();
	}
	public bool AddOne(string? team = null)
	{
		if (!Supported || LanSession.Instance?.IsClient == true) return false;
		var humans = GetTree().GetNodesInGroup("source_players").OfType<Player>().Count(p => !p.IsBot);
		if (_bots.Count >= Math.Min(15, LanSession.MaxPlayers - humans)) return false;
		Configure(Settings with { Difficulty = Settings.Difficulty == "none" ? "normal" : Settings.Difficulty,
			Quota = Math.Min(15, Math.Max(_bots.Count, DesiredCount(Settings, humans)) + 1), QuotaMode = "normal" });
		Add(team);
		return true;
	}
	private void Add(string? requestedTeam = null)
	{
		var profiles = BotProfile.ForDifficulty(Settings.Difficulty == "harmless" ? "easy" : Settings.Difficulty);
		var used = _bots.Select(b => b.Profile.Name).ToHashSet();
		var available = profiles.Where(p => !used.Contains(p.Name)).ToArray();
		if (available.Length == 0) available = profiles;
		if (available.Length == 0) return;
		var profile = available[GD.RandRange(0, available.Length - 1)];
		var team = (requestedTeam ?? Settings.Team) switch { "t" => PlayerTeam.Terrorist, "ct" => PlayerTeam.CounterTerrorist,
			_ => GetTree().GetNodesInGroup("source_players").OfType<Player>().Count(p => p.Team == PlayerTeam.Terrorist)
				<= GetTree().GetNodesInGroup("source_players").OfType<Player>().Count(p => p.Team == PlayerTeam.CounterTerrorist)
				? PlayerTeam.Terrorist : PlayerTeam.CounterTerrorist };
		var player = PlayerSceneCache.Load("res://scenes/player/player.tscn").Instantiate<Player>();
		player.Name = "Bot_" + -_nextId;
		player.IsBot = true; player.BotDisplayName = Settings.Prefix + profile.Name;
		player.LocalInput = false; player.DebugControls = false; player.NetworkPeerId = _nextId--;
		player.Team = team; player.AgentId = PlayerBody.AgentsForTeam(team)[0];
		player.SpawnPointsRootPath = _local.SpawnPointsRootPath;
		_local.GetParent().AddChild(player);
		_perceptionFrame = ulong.MaxValue;
		var agents = PlayerBody.AgentsForTeam(team);
		player.SetAgent(agents[profile.Skin > 0 ? (profile.Skin - 1) % agents.Length : GD.RandRange(0, agents.Length - 1)]);
		var controller = new BotController { Name = "Brain" };
		controller.Initialize(this, player, profile);
		player.AddChild(controller);
		_bots.Add(controller);
		LanSession.Instance?.RegisterBot(player);
	}
	private void Remove(BotController bot)
	{
		_bots.Remove(bot);
		LanSession.Instance?.UnregisterBot(bot.Player);
		bot.Player.QueueFree();
		_perceptionFrame = ulong.MaxValue;
	}
	public void Clear() { foreach (var bot in _bots.ToArray()) if (IsInstanceValid(bot)) Remove(bot); }
	public void Kick(string name)
	{
		if (name is "" or "all") { Configure(Settings with { Quota = 0 }); Clear(); return; }
		var bot = _bots.FirstOrDefault(b => b.Player.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase));
		if (bot != null) { Configure(Settings with { Quota = Math.Max(0, Settings.Quota - 1) }); Remove(bot); }
	}
	public Vector3[] FindPath(Vector3 from, Vector3 to)
	{
		if (!NavigationReady) return [];
		var start = NavigationServer3D.MapGetClosestPoint(_navigation, from);
		var end = NavigationServer3D.MapGetClosestPoint(_navigation, to);
		var path = NavigationServer3D.MapGetPath(_navigation, start, end, true);
		return path.Length > 0 && path[^1].DistanceTo(end) < .5f ? path : [];
	}
	public Vector3 PatrolGoal(Vector3 from) => PatrolGoal(from, out _);
	internal Vector3 PatrolGoal(Vector3 from, out Vector3[] route)
	{
		route = [];
		for (var attempt = 0; attempt < 12; attempt++)
		{
			if (_patrolPoints.Length == 0) break;
			var point = _patrolPoints[GD.RandRange(0, _patrolPoints.Length - 1)].GlobalPosition;
			if (point.DistanceSquaredTo(from) <= 144) continue;
			var path = FindPath(from, point);
			if (path.Length > 1) { route = path; return point; }
		}
		return from;
	}
	private void Hear(Player source, Vector3 position, string path)
	{
		if (LanSession.Instance?.IsClient == true) return;
		var range = path.Contains("footsteps/") ? 16f : path.StartsWith("weapons/") ? 60f : 12f;
		foreach (var bot in _bots) if (IsInstanceValid(bot)) bot.Hear(source, position, range);
	}
}
