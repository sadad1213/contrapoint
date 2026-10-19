using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.Gameplay;
using Contrapoint.Gameplay.Bots;
using Contrapoint.Network;
using Contrapoint.Players;
using Contrapoint.UI;
using Contrapoint.Weapons;

public partial class BotChecks : Node
{
	private int _checks;
	private Player Local => LanSession.Instance!.LocalPlayer!;
	private BotManager Manager => BotManager.Instance!;
	private const string SyncRoot = "user://bot_checks/";
	private static bool Exists(string name) => FileAccess.FileExists(SyncRoot + name);
	private static void Write(string name) { using var file = FileAccess.Open(SyncRoot + name, FileAccess.ModeFlags.Write); file.StoreString("ok"); }
	private void Check(bool condition, string message)
	{ _checks++; if (!condition) throw new Exception(message); GD.Print("BOT_CHECK ", message); }
	private async Task Until(Func<bool> condition, string message, double seconds = 30)
	{
		var end = Time.GetTicksMsec() + (ulong)(seconds * 1000);
		while (!condition())
		{
			if (Time.GetTicksMsec() > end) throw new TimeoutException(message);
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		}
	}
	private async Task Delay(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
	private void Command(string text) => ConsoleCommands.Execute(text, Local.Movement,
		(line, error) => { GD.Print(line); if (error) throw new Exception(line); });
	public override async void _Ready()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		Reparent(GetTree().Root); GetTree().CurrentScene = null;
		try
		{
			DirAccess.MakeDirRecursiveAbsolute(SyncRoot);
			if (OS.GetCmdlineUserArgs().Contains("--bot-role=client")) await Client(); else await Host();
			GD.Print($"BOT_CHECKS checks={_checks} failures=0"); GetTree().Quit();
		}
		catch (Exception error) { GD.PushError(error.ToString()); GD.Print($"BOT_CHECKS checks={_checks} failures=1"); GetTree().Quit(1); }
	}
	private async Task Host()
	{
		GD.Seed(1852);
		Check(BotProfile.ForDifficulty("easy").Any(p => p.Reaction == .5f && p.AttackDelay == 1.5f), "stock easy profile values");
		Check(BotProfile.ForDifficulty("expert").Any(p => p.Skill == 1 && p.Reaction == .2f), "stock elite profile values");
		Check(BotManager.DesiredCount(new() { Difficulty = "hard", Quota = 7, QuotaMode = "fill" }, 2) == 5
			&& BotManager.DesiredCount(new() { Difficulty = "hard", Quota = 2, QuotaMode = "match" }, 3) == 6, "fill and match quotas");
		var mapId = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--bot-map="))?.Split('=')[1] ?? "de_dust2";
		var mapScene = mapId switch { "de_dust2" => "dust2", "de_inferno" => "inferno", "de_cpl_strike" => "de_cpl_strike", "de_cache" => "de_cache", "2000" => "2000",
			_ => throw new ArgumentException("Unsupported bot check map: " + mapId) };
		GD.Print("BOT_CHECK_MAP ", mapId);
		MatchSession.Begin(new MatchSetup { MapId = mapId, ModeId = "deathmatch", FriendlyFire = false, FallDamage = false,
			Bots = new() { Difficulty = "expert", Quota = 2, Rifles = true, Snipers = false, Prefix = "[BOT] " } });
		GetTree().ChangeSceneToFile($"res://scenes/maps/{mapScene}.tscn");
		await Until(() => LanSession.Instance!.LocalPlayer != null && BotManager.Instance != null, "map attached");
		await Delay(.6);
		Check(Manager.Bots.Count == 0, "join-after-player waits for team selection");
		Local.JoinTeam(2); Local.JoinClass(1); Local.SetPhysicsProcess(false); Local.GodMode = true; SourceDamageRules.Cheats = true;
		await Until(() => Manager.Bots.Count == 2, "menu quota creates bots");
		Check(Manager.NavigationReady && Manager.Bots.All(b => b.Player.IsBot && !b.Player.LocalInput && b.Player.NetworkPeerId < 0
			&& b.Player.DisplayName.StartsWith("[BOT] ") && b.Player.ActiveWeapon is FirearmView), "menu settings create real armed players with CSS names");
		Check(Manager.Bots.All(b => b.Player.Inventory.Weapons.OfType<GrenadeView>().All(g => g.Ammo == 0)), "DM bots have no grenades");
		var bot = Manager.Bots[0]; var actor = bot.Player;
		await Until(() => bot.PathsFound > 0, "bot builds patrol route");
		var origin = actor.GlobalPosition;
		await Until(() => actor.GlobalPosition.DistanceTo(origin) > 3, "bot moves through imported map collision", 15);
		Check(actor.IsOnFloor() && actor.GlobalPosition.IsFinite(), "bot follows floor without teleport movement");
		// Use an actual open path segment on the selected map for a reproducible combat fixture.
		bot.SetPhysicsProcess(false); Manager.Bots[1].SetPhysicsProcess(false);
		var map = Local.GetNode<Node3D>(Local.SpawnPointsRootPath);
		var markers = map.FindChildren("Spawn_deathmatch_*", "Marker3D", true, false).OfType<Marker3D>().ToArray();
		Vector3 start = default, end = default; var found = false;
		foreach (var marker in markers)
		{
			var path = Manager.FindPath(marker.GlobalPosition, markers[(Array.IndexOf(markers, marker) + 31) % markers.Length].GlobalPosition);
			for (var i = 1; i < path.Length; i++)
			{
				if (path[i].DistanceTo(path[i - 1]) < 8 || Mathf.Abs(path[i].Y - path[i - 1].Y) > .2f) continue;
				start = path[i - 1].Lerp(path[i], .15f); end = path[i - 1].Lerp(path[i], .85f); found = true; break;
			}
			if (found) break;
		}
		Check(found, "map has open combat segment");
		// Discard patrol memory before moving the shooter into the combat fixture.
		actor.Respawn();
		Place(actor, start); Place(Local, end);
		actor.EndSpawnProtection(); Local.EndSpawnProtection(); Local.SetVitals(100, 0, false);
		var direction = end - start; actor.SetBotAim(Mathf.Atan2(-direction.X, -direction.Z), 0);
		await Delay(.1);
		Check(bot.CanSee(Local), "visible opponent can be detected");
		if (OS.GetCmdlineUserArgs().Contains("--bot-preview"))
		{
			var look = start - end;
			Local.SourceTeleport(Local.GlobalPosition, Mathf.Atan2(-look.X, -look.Z), 0);
			await Delay(.4);
			await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
			GetViewport().GetTexture().GetImage().SavePng("res://.godot/bot-match.png");
			Local.ShowMatchScores = true;
			await Delay(.2);
			await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
			GetViewport().GetTexture().GetImage().SavePng("res://.godot/bot-scoreboard.png");
			Local.ShowMatchScores = false;
		}
		actor.SetBotAim(actor.Rotation.Y + Mathf.Pi, 0);
		Check(!bot.CanSee(Local), "opponent behind bot is outside FOV");
		actor.SetBotAim(actor.Rotation.Y + Mathf.Pi, 0);
		Local.GodMode = false;
		// Keep the shooter on the fixture while waiting for reaction and weapon timers.
		// Otherwise it can walk past the future occluder on shorter map segments.
		Manager.Configure(Manager.Settings with { Stop = true });
		// Face the known sound while the reaction timer runs instead of turning
		// back toward a random patrol waypoint before the target is acquired.
		bot.Hear(Local, end, 100);
		bot.SetPhysicsProcess(true);
		var shots = bot.ShotsFired;
		await Until(() => bot.ShotsFired > shots, "bot fires through shared firearm controller", 15);
		await Until(() => Local.Health < 100, "bot traces damage real player", 15);
		Check(actor.ActiveWeapon is FirearmView gun && gun.MagazineAmmo < gun.MagazineCapacity, "bot shooting consumes ammunition");
		Local.GodMode = true;
		bot.SetPhysicsProcess(false);
		var health = Local.Health;
		actor.SetBotAim(actor.Rotation.Y + Mathf.Pi, 0);
		Place(Manager.Bots[1].Player, (start + end) / 2);
		await Delay(.1);
		Check(bot.CanSee(Local, false), "shared sight exclusions ignore intervening player hulls");
		var wall = new StaticBody3D { CollisionLayer = 1, Position = (start + end) / 2 + Vector3.Up };
		wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(4, 4, 4) } });
		GetTree().CurrentScene.AddChild(wall);
		await Delay(.1);
		Check(!bot.CanSee(Local, false), "world geometry occludes opponent");
		wall.QueueFree();
		await Delay(.1);
		var smoke = new GrenadeSmoke { Position = (start + end) / 2 + Vector3.Up };
		smoke.SetPhysicsProcess(false); smoke.SetProcess(false);
		GetTree().CurrentScene.AddChild(smoke);
		smoke._Process(6);
		await Delay(.1);
		Check(!bot.CanSee(Local, false), "shared smoke snapshot blocks sight");
		smoke.QueueFree();
		await Delay(.1);
		Check(bot.CanSee(Local, false), "sight snapshot drops removed smoke");
		Manager.Configure(Manager.Settings with { Stop = false });
		DeathmatchRules.FreeForAll = false;
		Local.Team = actor.Team;
		Check(!bot.IsEnemy(Local), "TDM ignores teammates");
		DeathmatchRules.FreeForAll = true;
		Check(bot.IsEnemy(Local), "FFA targets same-model opponents");
		var deaths = actor.MatchDeaths;
		actor.TakeDamage(1000);
		Check(!actor.IsAlive && actor.MatchDeaths == deaths + 1, "bot uses shared death and scoring");
		await Until(() => actor.IsAlive, "bot automatically respawns");
		Check(actor.Armor == 100 && actor.HasHelmet && actor.ActiveWeapon is FirearmView, "bot respawn restores loadout and armor");
		if (OS.GetCmdlineUserArgs().Contains("--bot-role=host"))
		{
			foreach (var file in new[] { "roster", "motion", "dead", "respawned" }) DirAccess.RemoveAbsolute(SyncRoot + file);
			Command("lan 1");
			Check(LanSession.Instance!.Players.Values.Count(p => p.IsBot) == 2, "existing offline bots join LAN roster");
			GD.Print("BOT_HOST_READY");
			await Until(() => LanSession.Instance!.Players.Values.Any(p => !p.IsBot && p != Local), "LAN client joins", 60);
			await Until(() => Exists("roster"), "client acknowledges roster");
			foreach (var brain in Manager.Bots) brain.SetPhysicsProcess(true);
			await Until(() => Exists("motion"), "client observes server bot movement");
			actor.TakeDamage(1000);
			Check(!actor.IsAlive, "host kills bot before replicating death");
			await Until(() => Exists("dead"), "client receives bot death");
			await Until(() => Exists("respawned"), "client receives bot respawn");
			Check(actor.IsAlive, "bot death and respawn synchronize over LAN");
		}
		if (OS.GetCmdlineUserArgs().Contains("--bot-soak"))
		{
			foreach (var brain in Manager.Bots) brain.SetPhysicsProcess(true);
			Command("bot_quota 8; bot_difficulty 2; mp_timelimit 0");
			await Until(() => Manager.Bots.Count == 8, "soak population");
			var positions = Manager.Bots.ToDictionary(b => b, b => b.Player.GlobalPosition);
			var travelled = Manager.Bots.ToDictionary(b => b, _ => 0f);
			for (var i = 0; i < 30; i++)
			{
				await Delay(1);
				foreach (var brain in Manager.Bots)
				{
					if (brain.Player.IsAlive) travelled[brain] += Mathf.Min(6, brain.Player.GlobalPosition.DistanceTo(positions[brain]));
					positions[brain] = brain.Player.GlobalPosition;
				}
			}
			foreach (var brain in Manager.Bots)
				GD.Print($"BOT_SOAK {brain.Player.DisplayName} travelled={travelled[brain]:F1} paths={brain.PathsFound} shots={brain.ShotsFired} kills={brain.Player.MatchKills} deaths={brain.Player.MatchDeaths} state={brain.State}");
			Check(Manager.Bots.Count(b => travelled[b] > 10) >= 6, "bots cover the map during a thirty-second match");
			Check(Manager.Bots.Sum(b => b.ShotsFired) > 20 && Manager.Bots.Sum(b => b.Player.MatchKills) > 0, "bots fight and score during an unattended match");
		}
		Command("bot_kick");
		await Until(() => Manager.Bots.Count == 0, "bot_kick removes bots");
		Command("bot_difficulty 0; bot_add_ct");
		await Until(() => Manager.Bots.Count == 1, "bot_add restores population");
		Check(Manager.Bots[0].Player.Team == PlayerTeam.CounterTerrorist && Manager.Bots[0].Profile.Difficulty.Contains("easy"), "console selects team and difficulty");
		Command("bot_add_t; bot_add_ct");
		Check(Manager.Bots.Count == 3 && Manager.Bots[1].Player.Team == PlayerTeam.Terrorist
			&& Manager.Bots[2].Player.Team == PlayerTeam.CounterTerrorist, "batched bot_add commands each create a bot on the requested team");
		// Disable the other primary categories before asserting a pistol-only loadout.
		Manager.Configure(Manager.Settings with { Shotguns = false, SubmachineGuns = false, MachineGuns = false });
		Command("bot_stop 1; bot_dont_shoot 1; bot_join_after_player 0; bot_allow_rifles 0; bot_allow_snipers 0; bot_allow_pistols 1; bot_prefix \"Test \"; bot_join_team t");
		Check(Manager.Settings.Stop && Manager.Settings.DontShoot && !Manager.Settings.JoinAfterPlayer && !Manager.Settings.Rifles
			&& !Manager.Settings.Snipers && Manager.Settings.Prefix == "Test " && Manager.Settings.Team == "t", "console configures movement, attacks, weapons, prefix and team");
		Check(ConsoleCommands.GetCompletions("bot_allow_").Count == 3 && ConsoleCommands.GetCompletions("bot_join_team c").Contains("bot_join_team ct"), "bot commands participate in console autocomplete");
		Command("bot_kick; bot_add");
		await Until(() => Manager.Bots.Count == 1, "pistol-only bot created");
		Check(Manager.Bots[0].Player.Team == PlayerTeam.Terrorist && Manager.Bots[0].Player.DisplayName.StartsWith("Test ")
			&& Manager.Bots[0].Player.ActiveWeapon is FirearmView { Slot: WeaponSlot.Secondary }, $"new bots obey console loadout and name settings: team={Manager.Bots[0].Player.Team} name={Manager.Bots[0].Player.DisplayName} weapon={Manager.Bots[0].Player.ActiveWeapon?.Name} slot={Manager.Bots[0].Player.ActiveWeapon?.Slot}");
		var instance = Manager.GetInstanceId();
		LanSession.Instance!.RestartCurrentMap();
		await Until(() => BotManager.Instance != null && Manager.GetInstanceId() != instance && Manager.Bots.Count == 1, "map restart preserves bot settings", 40);
		Check(Manager.Bots[0].Player.ActiveWeapon is FirearmView, "restarted bot is armed");
		Command("bot_kick");
	}
	private static void Place(Player player, Vector3 point)
	{
		// Navigation heights include voxel clearance: locate the actual collision floor.
		using var ray = PhysicsRayQueryParameters3D.Create(point + Vector3.Up, point + Vector3.Down * 2, 3,
			new Godot.Collections.Array<Rid> { player.GetRid() });
		var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(ray);
		player.SourceTeleport(hit.Count > 0 ? hit["position"].AsVector3() + Vector3.Up * .04f : point, 0, 0);
	}
	private async Task Client()
	{
		LanSession.Instance!.Connect("127.0.0.1", (s, e) => { GD.Print(s); if (e) throw new Exception(s); });
		await Until(() => LanSession.Instance.WorldReady && LanSession.Instance.Players.Values.Count(p => p.IsBot) == 2, "client receives bot roster", 60);
		Check(LanSession.Instance.Players.Values.Where(p => p.IsBot).All(p => !p.LocalInput && p.DisplayName.StartsWith("[BOT] ")
			&& p.GetNodeOrNull<BotController>("Brain") == null && p.Inventory.Weapons.All(w => !w.IsProcessing())), "client bots have names and inventory without AI or weapon simulation");
		Local.JoinTeam(3); Local.JoinClass(1);
		var actor = LanSession.Instance.Players[-1];
		actor.HealthChanged += hp => GD.Print("BOT_CLIENT_HEALTH ", hp, " revision=", actor.NetworkTeleportRevision);
		var position = actor.GlobalPosition; var yaw = actor.Rotation.Y;
		Write("roster");
		await Until(() => actor.GlobalPosition.DistanceTo(position) > .5 || Mathf.Abs(actor.Rotation.Y - yaw) > .2, "server bot state moves client replica");
		Write("motion");
		await Until(() => !actor.IsAlive && actor.Death.Active, "server bot death reaches client");
		var deathRevision = actor.NetworkTeleportRevision;
		var stale = actor.CaptureNetworkState(); stale["health"] = 100;
		actor.ApplyNetworkState(stale, false);
		Check(!actor.IsAlive && actor.Death.Active, "stale alive snapshot cannot resurrect a dead bot");
		Write("dead");
		await Until(() => actor.IsAlive && !actor.Death.Active && actor.NetworkTeleportRevision > deathRevision, "server bot respawn reaches client");
		Check(actor.ActiveWeapon is FirearmView && actor.Armor == 100, "client receives bot respawn inventory and vitals");
		Write("respawned");
		var re