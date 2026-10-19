using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.Gameplay;
using Contrapoint.Network;
using Contrapoint.Players;
using Contrapoint.UI;
using Contrapoint.Weapons;

public partial class DefuseLanChecks : Node
{
	private string _role = "";
	private int _checks;
	private LanSession Net => LanSession.Instance!;
	private Player P => Net.LocalPlayer!;
	private DefuseState De => DefuseState.Instance!;
	private static string Arg(string key, string fallback) => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith(key + "="))?[(key.Length + 1)..] ?? fallback;
	private static string Root => "user://de_checks/" + Arg("--de-run", "default") + "/";
	private static bool Exists(string file) => FileAccess.FileExists(Root + file);
	private static void Write(string name) { using var f = FileAccess.Open(Root + name, FileAccess.ModeFlags.Write); f.StoreString("ok"); }
	private void Check(bool test, string message) { _checks++; if (!test) throw new Exception(_role + ": " + message); GD.Print("DE_LAN_OK " + message); }
	private async Task Until(Func<bool> test, string message, double seconds = 60)
	{
		var until = Time.GetTicksMsec() + (ulong)(seconds * 1000);
		while (!test()) { if (Time.GetTicksMsec() > until) throw new TimeoutException(_role + ": " + message); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
	}
	private async Task Delay(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
	public override async void _Ready()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		Reparent(GetTree().Root); GetTree().CurrentScene = null;
		_role = Arg("--de-role", "host"); DirAccess.MakeDirRecursiveAbsolute(Root);
		try
		{
			if (_role == "host") await Host(); else await Client();
			GD.Print($"DE_LAN_CHECKS role={_role} checks={_checks} failures=0"); Write(_role + "_passed"); GetTree().Quit();
		}
		catch (Exception e) { GD.PushError(e.ToString()); GD.Print($"DE_LAN_CHECKS role={_role} checks={_checks} failures=1"); GetTree().Quit(1); }
	}
	private async Task Host()
	{
		MatchSession.Begin(new MatchSetup { ModeId = "defuse", FriendlyFire = false, FallDamage = false });
		GetTree().ChangeSceneToFile("res://scenes/maps/dust2.tscn");
		await Until(() => Net.LocalPlayer?.AwaitingInitialClass == true, "host map");
		DefuseRules.FreezeSeconds = 1; DefuseRules.RestartSeconds = 30; DeathmatchRules.TimeLimitMinutes = 0;
		P.JoinTeam(2); P.JoinClass(1);
		Net.Open((line, _) => GD.Print(line)); Write("ready");
		await Until(() => Exists("client_ready") && De.Phase == DefusePhase.Live && De.Carrier == P, "client and live round");
		var ct = Net.Players.Values.Single(p => p.Team == PlayerTeam.CounterTerrorist);
		Check(ct.HasDefuser && ct.Money == 600, "server owns kit purchase and money");
		Check(Net.Players.Values.Sum(p => p.Inventory.Weapons.OfType<C4View>().Count()) == 1, "exactly one C4 across network players");
		P.SourceTeleport(De.BotGoal(P) + Vector3.Up * .06f, 0, 0);
		await Until(() => P.IsOnFloor() && De.BombSite(P) != null, "host at actual bombsite");
		P.Inventory.EquipSlot(WeaponSlot.Objective); await Delay(1.1);
		P.ActiveWeapon!.HandleCommand("attack", true); await Until(() => De.BombPlanted, "host planted");
		Write("late_join"); await Until(() => Exists("late_ready"), "late client gets planted objective");
		ct.SourceTeleport(De.BombPosition + new Vector3(.8f, .06f, 0), 0, 0);
		Write("defuse_go");
		await Until(() => De.Result == DefuseResult.BombDefused, "remote kit defuse", 15);
		Check(De.CounterTerroristWins == 1 && ct.MatchKills == 3, "remote use resolves defuse and scores on authority");
		await Until(() => Exists("client_defused"), "owner gets result");
		var ids = Net.Players.Keys.Order().ToArray(); var epoch = Net.MapEpoch; var old = GetTree().CurrentScene.GetInstanceId();
		De._PhysicsProcess(31);
		await Until(() => Net.WorldReady && Net.MapEpoch > epoch && Net.Players.Count == 3 && Exists("client_round2") && Exists("late_round2"), "all clients resume next round");
		Check(GetTree().CurrentScene.GetInstanceId() != old && ids.SequenceEqual(Net.Players.Keys.Order()), "round reload resets scene while retaining all ENet peers");
		Check(P.Money == 3000 && De.CounterTerroristWins == 1 && De.RoundNumber == 2, "round income and score survive reload");
		// A stale client action is tested by the owning client.
		Write("done");
		await Until(() => Exists("client_passed") && Exists("late_passed"), "clients finish");
	}
	private async Task Client()
	{
		await Until(() => Exists(_role == "late" ? "late_join" : "ready"), "host ready");
		Net.Connect("127.0.0.1:" + LanSession.Port, (line, _) => GD.Print(line));
		await Until(() => Net.WorldReady && DefuseRules.Active, "join DE");
		Check(DefuseRules.BombSeconds == 45 && DefuseRules.RoundMinutes == 2.5f && !DeathmatchRules.Active, "join receives CSS DE rules");
		if (_role == "client")
		{
			P.JoinTeam(3); await Until(() => P.PickingClass, "CT team"); P.JoinClass(5);
			await Until(() => P.IsAlive && !P.PickingClass, "CT spawned");
			P.Money = 99999; P.Buy("defuser");
			await Until(() => P.HasDefuser && P.Money == 600, "authoritative kit and balance");
			var errors = 0;
			ConsoleCommands.Execute("mp_c4timer 10; mp_roundtime 1; mp_restartgame 1", P.Movement, (_, e) => { if (e) errors++; });
			Check(errors == 3 && DefuseRules.BombSeconds == 45, "client cannot change match rules");
			Write("client_ready");
			await Until(() => Exists("defuse_go") && De.BombPlanted && P.GlobalPosition.DistanceTo(De.BombPosition) < 1.5f, "planted C4 and server teleport");
			await Until(() => P.IsOnFloor(), "defuser landed");
			var aim = De.BombPosition - P.Camera.GlobalPosition;
			P.SourceTeleport(P.GlobalPosition, Mathf.Atan2(-aim.X, -aim.Z), Mathf.Atan2(aim.Y, new Vector2(aim.X, aim.Z).Length()));
			P.SetMouseCaptured(true); P.SetObjectiveUse(true);
			await Until(() => De.Defuser == P && De.DefuseProgress > .2, "server replicates continuous use progress");
			Check(De.BombRemaining > 0, "client sees ticking bomb while defusing");
			await Until(() => De.Result == DefuseResult.BombDefused, "defuse result");
			P.SetObjectiveUse(false);
			Check(De.CounterTerroristWins == 1 && P.MatchKills == 3, "client gets authoritative round and scoreboard"); Write("client_defused");
		}
		else
		{
			await Until(() => De.BombPlanted && Net.Players.Values.Any(p => p.HasDefuser), "late objective and kit snapshot");
			Check(De.Carrier == null && De.BombRemaining is > 0 and < 45, "late join sees consumed C4 and remaining fuse");
			P.JoinTeam(1); await Until(() => P.Team == PlayerTeam.Spectator, "late spectator");
			Check(P.Observer!.Cycle() && P.Observer.Target?.NetworkPeerId == 1, "late spectator selects host in first person");
			await Until(() => P.Observer.FirstPerson && P.GetNode<CssHud>("HUD/Status").Visible, "host observer HUD");
			Check(P.Observer.Viewmodel.Model != null && !P.Observer.Target!.LocalInput && P.Observer.Target.Body.Visible,
				"LAN observer renders remote weapon without hiding target globally");
			Write("late_ready");
			await Until(() => Exists("defuse_go"), "observer sees defuse start");
			P.Observer.Cycle();
			await Until(() => P.Observer.Target?.HasDefuser == true && De.DefuseProgress > .2, "spectate remote defuser");
			Check(P.HudPlayer == De.Defuser && P.GetNode<CssHud>("HUD/Status").Snapshot.Health == P.HudPlayer.Health,
				"observer follows defuser and receives live HUD state");
		}
		var epoch = Net.MapEpoch; var peer = P.NetworkPeerId;
		await Until(() => Net.WorldReady && Net.MapEpoch > epoch && De.RoundNumber == 2 && Net.Players.Count == 3, "round two reload");
		Check(P.NetworkPeerId == peer && De.CounterTerroristWins == 1 && !De.BombPlanted, "round reload preserves identity and score, removes old planted bomb");
		if (_role == "late")
		{
			await Until(() => P.Observer?.FirstPerson == true && P.Observer.Target?.HasDefuser == true, "observer resumes selected player after map reload");
			Check(P.Team == PlayerTeam.Spectator && P.HudPlayer.Team == PlayerTeam.CounterTerrorist,
				"spectator mode and selected teammate survive round transition");
		}
		if (_role == "client")
		{
			await Until(() => P.HasDefuser && P.Money == 3850, "survivor economy and kit");
			Check(P.IsAlive && P.Team == PlayerTeam.CounterTerrorist && P.MatchKills == 3, "CT survivor keeps kit and stats with $3250 round award");
			Net.RpcId(1, "ObjectiveInput", epoch, P.CaptureNetworkState(), true, true);
		}
		Write(_role + "_round2");
		await Until(() => Exists("done"), "host finished");
	}
}
