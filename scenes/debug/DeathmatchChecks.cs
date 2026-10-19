using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.Gameplay;
using Contrapoint.Network;
using Contrapoint.Players;
using Contrapoint.UI;
using Contrapoint.Weapons;

public partial class DeathmatchChecks : Node
{
	private int _checks;
	private string _role = "";
	private LanSession Net => LanSession.Instance!;
	private Player Player => Net.LocalPlayer!;
	private RoundState Round => RoundState.Instance!;
	private static string Arg(string name, string fallback) => OS.GetCmdlineUserArgs().FirstOrDefault(s => s.StartsWith(name + "="))?[(name.Length + 1)..] ?? fallback;
	private static string Root => "user://dm_checks/" + Arg("--dm-run", "default") + "/";
	private static bool Exists(string name) => FileAccess.FileExists(Root + name);
	private static void Write(string name) { using var file = FileAccess.Open(Root + name, FileAccess.ModeFlags.Write); file.StoreString("ok"); }
	private void Check(bool result, string message) { _checks++; if (!result) throw new Exception(message); }
	private async Task Until(Func<bool> condition, string message, double seconds = 35)
	{
		var end = Time.GetTicksMsec() + (ulong)(seconds * 1000);
		while (!condition()) { if (Time.GetTicksMsec() > end) throw new TimeoutException(_role + ": " + message); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
	}
	private async Task Delay(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
	private void Command(string command) => ConsoleCommands.Execute(command, Player.Movement, (line, error) => { GD.Print(line); if (error) throw new Exception(line); });
	private void CheckMapSpawn(Player player, string message)
	{
		var map = player.GetNode<Node3D>(player.SpawnPointsRootPath);
		Check(map.FindChildren("Spawn_deathmatch_*", "Marker3D", true, false).OfType<Marker3D>()
			.Any(point => player.GlobalPosition.DistanceTo(point.GlobalPosition + Vector3.Up * .04f) < .15f), message);
	}
	public override async void _Ready()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		Reparent(GetTree().Root); GetTree().CurrentScene = null;
		_role = Arg("--dm-role", "offline");
		DirAccess.MakeDirRecursiveAbsolute(Root);
		try
		{
			if (_role is "host" or "offline") await Host(); else await Client();
			GD.Print($"DM_CHECKS role={_role} checks={_checks} failures=0"); Write(_role + "_passed"); GetTree().Quit();
		}
		catch (Exception error) { GD.PushError(error.ToString()); GD.Print($"DM_CHECKS role={_role} checks={_checks} failures=1"); Net.Stop(); GetTree().Quit(1); }
	}
	private async Task Host()
	{
		MatchSession.Begin(new MatchSetup { ModeId = "deathmatch", FriendlyFire = false, FallDamage = false });
		GetTree().ChangeSceneToFile("res://scenes/maps/dust2.tscn");
		await Until(() => Net.LocalPlayer != null, "map attached");
		Player.JoinTeam(2); Player.JoinClass(1); Player.SetPhysicsProcess(false);
		CheckMapSpawn(Player, "initial authoritative DM spawn uses map-wide marker");
		Check(DeathmatchRules.Active && DeathmatchRules.FreeForAll && Player.Health == 100 && Player.Armor == 100 && Player.HasHelmet, "menu snapshot starts FFA with full armor and helmet");
		Check(Player.Inventory.Weapons.OfType<GrenadeView>().All(grenade => grenade.Ammo == 0 && !grenade.CanEquip)
			&& !Player.Inventory.EquipSlot(WeaponSlot.Grenade), "DM spawns without usable grenades");
		Check(Round.TimerRunning && Round.DisplaySeconds is >= 599 and <= 600, "ten-minute authoritative timer");
		var protectedHealth = Player.Health; Player.TakeHeDamage(40, null);
		Check(Player.Health == protectedHealth, "spawn protection blocks damage"); Player.EndSpawnProtection();
		Command("dm_spawn_protection 0; dm_respawn_delay 0.3; mp_timelimit 0");
		Check(!Round.TimerRunning, "zero time limit disables automatic restart");
		if (_role == "offline")
		{
			Player.TakeDamage(100);
			await Until(() => Player.IsAlive, "offline automatic DM respawn");
			var respawnWeapon = Player.ActiveWeapon;
			Check(Player.Inventory.EquipPrevious() && Player.ActiveWeapon != respawnWeapon,
				"lastinv works immediately after automatic DM respawn");
			Check(Player.Inventory.EquipPrevious() && Player.ActiveWeapon == respawnWeapon,
				"lastinv toggles back after automatic DM respawn");
			if (OS.GetCmdlineUserArgs().Contains("--dm-preview"))
			{
				Command("mp_timelimit 10"); Player.ShowMatchScores = true;
				await Delay(.2); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
				GetViewport().GetTexture().GetImage().SavePng("res://.godot/dm-preview.png");
				Player.ShowMatchScores = false;
			}
			var old = Player.GetInstanceId(); var number = Round.MatchNumber;
			Command("mp_timelimit 0.01; dm_restart_delay 0.2");
			await Until(() => Net.LocalPlayer != null && Player.GetInstanceId() != old && Round.MatchNumber > number, "offline automatic map reload");
			Check(Player.IsAlive && Player.Team == PlayerTeam.Terrorist && Player.AgentId == "phoenix" && Player.Armor == 100, "offline restart keeps identity and restores vitals");
			Check(Player.Inventory.Weapons.OfType<GrenadeView>().All(grenade => grenade.Ammo == 0 && !grenade.CanEquip), "DM map restart does not restore grenades");
			Command("mp_timelimit 0"); return;
		}
		Command("lan 1"); Write("host_ready");
		await Until(() => Net.Players.Count == 2 && Net.Players.Values.Any(p => p != Player && p.IsAlive), "client joined FFA");
		var enemy = Net.Players.Values.First(p => p != Player);
		Player.EndSpawnProtection(); enemy.EndSpawnProtection();
		await Until(() => Exists("client_ammo_ready"), "client has spent real ammunition");
		enemy.SetVitals(60, 100, true); Net.BroadcastVitals(enemy);
		Player.SetVitals(10, 0, false);
		Player.TakeTraceDamage(100, enemy, SourceHitGroup.Chest, Player.GlobalPosition + Vector3.Up, Vector3.Forward, 1);
		Check(!Player.IsAlive && enemy.Health == 75 && enemy.MatchKills == 1 && Player.MatchDeaths == 1, "same-team FFA enemy kill grants exactly fifteen HP and one frag");
		Player.TakeDamage(100); Check(enemy.MatchKills == 1 && enemy.Health == 75, "duplicate lethal damage cannot award twice");
		await Until(() => Exists("client_reward_received"), "owning client receives ammo and health reward");
		await Until(() => Player.IsAlive, "DM automatic respawn");
		Check(Player.Armor == 100 && Player.HasHelmet, "respawn restores armor and helmet");
		Check(Player.Inventory.Weapons.OfType<GrenadeView>().All(grenade => grenade.Ammo == 0 && !grenade.CanEquip), "DM respawn does not restore grenades");
		CheckMapSpawn(Player, "automatic respawn uses map-wide marker");
		Command("mp_teammates_are_enemies 0");
		Player.EndSpawnProtection(); Player.SetVitals(100, 0, false);
		Player.TakeTraceDamage(40, enemy, SourceHitGroup.Chest, Player.GlobalPosition, Vector3.Forward, 1);
		Check(Player.Health == 100, "TDM blocks team damage when friendly fire is off");
		Command("mp_friendlyfire 1");
		Player.TakeTraceDamage(1000, enemy, SourceHitGroup.Chest, Player.GlobalPosition, Vector3.Forward, 1);
		Check(!Player.IsAlive && enemy.MatchKills == 1 && enemy.Health == 75, "teamkill grants neither frag nor reward");
		await Until(() => Player.IsAlive, "respawn after teamkill");
		Command("mp_friendlyfire 0");
		Write("tdm_ready"); await Until(() => Exists("client_tdm"), "client receives TDM settings");
		Write("late_join"); await Until(() => Net.Players.Count == 3 && Exists("late_ready"), "late join sees running rules");
		var peers = Net.Players.Keys.Order().ToArray(); var oldScene = GetTree().CurrentScene.GetInstanceId(); var oldNumber = Round.MatchNumber;
		Command("mp_timelimit 0.05; dm_restart_delay 0.5");
		await Until(() => Net.WorldReady && Net.Players.Count == 3 && Round.MatchNumber > oldNumber, "automatic reload preserves all connections", 60);
		Check(Net.IsHosting && peers.SequenceEqual(Net.Players.Keys.Order()) && oldScene != GetTree().CurrentScene.GetInstanceId(), "real map reload retains ENet peer IDs");
		Command("mp_timelimit 0");
		Check(!DeathmatchRules.FreeForAll && Net.Players.Values.All(p => p.IsAlive && p.Team == PlayerTeam.Terrorist && p.Armor == 100 && p.MatchKills == 0 && p.MatchDeaths == 0), "restart preserves teams and clears score and vitals");
		foreach (var peer in peers.Where(id => id != 1))
		{
			Net.RpcId(peer, "SmokeState", Net.MapEpoch - 1, 987654, Player.GlobalPosition, 1.0);
			Net.RpcId(peer, "ProjectileState", Net.MapEpoch - 1, int.MaxValue, 987655, (int)GrenadeKind.HE, 1, Transform3D.Identity, Vector3.Zero, Vector3.Zero);
		}
		Write("stale_sent");
		await Until(() => Exists("client_restart") && Exists("late_restart"), "both clients resume after restart");
		oldNumber = Round.MatchNumber;
		Command("mp_restartgame 1");
		await Until(() => Net.WorldReady && Net.Players.Count == 3 && Round.MatchNumber > oldNumber, "second restart through console", 60);
		await Until(() => Exists("client_second") && Exists("late_second"), "both clients resume after console restart");
		Check(Net.IsHosting && peers.SequenceEqual(Net.Players.Keys.Order()), "second reload retains roster");
		Write("done"); await Delay(.5); Net.Stop();
	}
	private async Task Client()
	{
		await Until(() => Exists(_role == "late" ? "late_join" : "host_ready"), "host ready");
		GetTree().ChangeSceneToFile("res://scenes/main_menu.tscn"); await Delay(.1);
		Net.Connect("127.0.0.1:" + LanSession.Port, (line, _) => GD.Print(line));
		await Until(() => Net.WorldReady, "initial connection", 60);
		Check(DeathmatchRules.Active && DeathmatchRules.FreeForAll == (_role != "late") && DeathmatchRules.RespawnSeconds == .3f, "join negotiates game mode and settings");
		var errors = 0; ConsoleCommands.Execute("dm_kill_health 99; mp_timelimit 1; mp_restartgame 1", Player.Movement, (_, e) => { if (e) errors++; });
		Check(errors == 3 && DeathmatchRules.KillHealth == 15, "client cannot change authoritative match rules");
		Player.JoinTeam(2); await Until(() => Player.PickingClass, "team accepted"); Player.JoinClass(2);
		await Until(() => Player.IsAlive && !Player.PickingClass, "class spawned");
		Player.SetPhysicsProcess(false); Player.EndSpawnProtection();
		CheckMapSpawn(Player, "client receives authoritative map-wide spawn");
		Check(Player.Armor == 100 && Player.HasHelmet, "network spawn applies DM armor");
		var gun = Player.Inventory.Weapons.OfType<FirearmView>().First(g => g.Kind == FirearmView.FirearmKind.AK47);
		Check(Player.Inventory.Equip(gun), "initial firearm selection");
		if (_role != "late")
		{
			Player.SourceTeleport(Player.GlobalPosition + Vector3.Up * 40, 0, -1);
			await Delay(1);
			gun.HandleCommand("attack", true); await Until(() => gun.MagazineAmmo <= 10, "real ammo consumption"); gun.HandleCommand("attack", false);
			var before = gun.MagazineAmmo; Write("client_ammo_ready");
			await Until(() => Player.Health == 75 && gun.MagazineAmmo == Math.Min(gun.MagazineCapacity, before + 15) && Player.MatchKills == 1, "authoritative kill reward reaches owner");
			Check(gun.ReserveAmmo == gun.ReserveCapacity, "kill ammo comes from reward, not reserve");
			Write("client_reward_received");
			await Until(() => Exists("tdm_ready") && !DeathmatchRules.FreeForAll, "live TDM switch"); Write("client_tdm");
		}
		else { Check(Round.TerroristKills == 1, "late join receives current team score"); Write("late_ready"); }
		var id = Player.NetworkPeerId; var number = Round.MatchNumber; var old = GetTree().CurrentScene.GetInstanceId();
		await Until(() => Net.WorldReady && Net.LocalPlayer != null && Round.MatchNumber > number && GetTree().CurrentScene.GetInstanceId() != old && Net.Players.Count == 3, "restart resumes network match", 65);
		Check(Player.NetworkPeerId == id && Player.IsAlive && Player.Team == PlayerTeam.Terrorist && Player.AgentId == "leet" && Player.Armor == 100 && !DeathmatchRules.FreeForAll, "restart keeps client identity, mode and armor");
		CheckMapSpawn(Player, "client receives authoritative TDM spawn after map reload");
		Check(Player.ActiveWeapon is FirearmView && Player.MatchKills == 0 && Player.MatchDeaths == 0, $"restart restores selected weapon and clears score: weapon={Player.ActiveWeapon?.Name}, K={Player.MatchKills}, D={Player.MatchDeaths}, enabled={Player.Inventory.Enabled}");
		await Until(() => Exists("stale_sent"), "old-map packets sent"); await Delay(.2);
		Check(GetTree().CurrentScene.FindChild("CSSSmoke_987654", true, false) == null && GetTree().CurrentScene.FindChild("Grenade_987655", true, false) == null, "old-map smoke and grenade packets cannot recreate deleted effects after restart");
		Player.SetPhysicsProcess(false); Write(_role + "_restart"); number = Round.MatchNumber;
		await Until(() => Net.WorldReady && Net.LocalPlayer != null && Round.MatchNumber > number && Net.Players.Count == 3, "second reload", 65);
		Check(Player.IsAlive && Player.NetworkPeerId == id && Player.Armor == 100, "second reload resumes player");
		Write(_role + "_second"); await Until(() => Exists("done"), "host completed");
		await Until(() => !Net.IsActive, "host gracefully closes the test session");
	}
}
