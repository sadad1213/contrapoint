using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.Network;
using Contrapoint.Players;

public partial class TeamLanChecks : Node
{
	private LanSession Net => LanSession.Instance!;
	private Player Player => Net.LocalPlayer!;
	private static string RunId => OS.GetCmdlineUserArgs().First(s => s.StartsWith("--team-run="))[11..];
	private static string Root => "user://team_checks/" + RunId + "/";
	private static void Write(string name) { using var file = FileAccess.Open(Root + name, FileAccess.ModeFlags.Write); file.StoreString("ok"); }
	private async Task Until(Func<bool> condition, string message)
	{
		var end = Time.GetTicksMsec() + 45000;
		while (!condition())
		{
			if (Time.GetTicksMsec() > end) throw new TimeoutException(message);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
	}
	private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
	public override async void _Ready()
	{
		var host = OS.GetCmdlineUserArgs().Contains("--team-host");
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		Reparent(GetTree().Root); GetTree().CurrentScene = null;
		try
		{
			DirAccess.MakeDirRecursiveAbsolute(Root);
			if (host)
			{
				GetTree().ChangeSceneToFile("res://scenes/maps/dust2.tscn");
				await Until(() => Net.LocalPlayer?.TeamMenu?.IsOpen == true, "initial host menu");
				Player.JoinTeam(2); Player.JoinClass(1);
				Net.Open((line, _) => GD.Print(line)); Write("ready");
				await Until(() => Net.Players.Count == 2 && Net.Players.Values.Any(p => !p.LocalInput && p.AgentId == "gsg9" && p.Team == PlayerTeam.CounterTerrorist), "host observes CT selection");
				var remote = Net.Players.Values.First(p => !p.LocalInput);
				var map = Player.GetNode<Node3D>(Player.SpawnPointsRootPath);
				var ctSpawns = map.FindChildren("Spawn_counterterrorist_*", "Marker3D", true, false).OfType<Marker3D>().ToArray();
				Check(ctSpawns.Any(m => (remote.GlobalPosition - m.GlobalPosition).Length() < .5f), "host places client at CT spawn");
				Write("ct_seen");
				await Until(() => remote.AgentId == "arctic" && remote.Team == PlayerTeam.Terrorist && remote.IsAlive, "host observes T switch");
				Write("t_seen");
				await Until(() => FileAccess.FileExists(Root + "client_passed"), "client finish");
			}
			else
			{
				await Until(() => FileAccess.FileExists(Root + "ready"), "host ready");
				Net.Connect("127.0.0.1:" + LanSession.Port, (line, _) => GD.Print(line));
				await Until(() => Net.WorldReady && Net.LocalPlayer != null && Player.TeamMenu?.IsOpen == true, "client initial menu");
				var spawns = 0;
				Player.Respawned += () => spawns++;
				Check(!Player.IsAlive && Player.AwaitingInitialClass && !Player.Body.Visible && !Player.Death.Active, "network join starts as an observer");
				Player.JoinTeam(3);
				await Until(() => Player.Team == PlayerTeam.CounterTerrorist && Player.TeamMenu!.IsClassSelection, "CT class menu from host");
				Check(!Player.IsAlive && spawns == 0 && !Player.Body.Visible, "team confirmation must not spawn before class selection");
				Player.JoinClass(6);
				await Until(() => Player.AgentId == "gsg9" && Player.IsAlive && !Player.TeamMenu!.IsOpen, "client CT spawn");
				await Until(() => FileAccess.FileExists(Root + "ct_seen"), "CT observed");
				Player.JoinTeam(2);
				await Until(() => Player.Team == PlayerTeam.Terrorist && Player.TeamMenu!.IsClassSelection, "T class menu from host");
				Player.JoinClass(3);
				await Until(() => Player.AgentId == "arctic" && Player.IsAlive && !Player.TeamMenu!.IsOpen, "client T spawn");
				await Until(() => FileAccess.FileExists(Root + "t_seen"), "T observed");
				Write("client_passed");
			}
			GD.Print($"TEAM_LAN_CHECKS {(host ? "host" : "client")} failures=0"); Net.Stop(); GetTree().Quit();
		}
		catch (Exception error) { GD.PushError($"TEAM_LAN_CHECKS {(host ? "host" : "client")}: {error}"); Net.Stop(); GetTree().Quit(1); }
	}
}
