using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.Gameplay;
using Contrapoint.Gameplay.Bots;
using Contrapoint.Network;
using Contrapoint.Players;

public partial class DefuseBotChecks : Node
{
	private int _checks;
	private int _lastReport = -1;
	private DefuseState De => DefuseState.Instance!;
	private void Check(bool condition, string message) { _checks++; if (!condition) throw new Exception(message); GD.Print("DE_BOT_OK " + message); }
	private async Task Until(Func<bool> condition, string message, double seconds = 90)
	{
		var end = Time.GetTicksMsec() + (ulong)(seconds * 1000);
		while (!condition())
		{
			if (Time.GetTicksMsec() > end) throw new TimeoutException(message);
			var t = (int)De.RoundElapsed / 15;
			if (t != _lastReport && BotManager.Instance != null)
			{
				_lastReport = t;
				foreach (var b in BotManager.Instance.Bots)
					GD.Print($"DE_BOT_STATE {De.RoundElapsed:0.0} {b.Player.Team} {b.State} at={b.Player.GlobalPosition} goal={De.BotGoal(b.Player)} paths={b.PathsFound} planted={De.BombPlanted} progress={De.PlantProgress:0.0}/{De.DefuseProgress:0.0}");
			}
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		}
	}
	public override async void _Ready()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		Reparent(GetTree().Root); GetTree().CurrentScene = null;
		try
		{
			GD.Seed(1977);
			var mapId = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--de-bot-map="))?.Split('=')[1] ?? "de_dust2";
			var entry = Contrapoint.Maps.MapCatalog.Find(mapId) ?? throw new ArgumentException("Unknown map: " + mapId);
			MatchSession.Begin(new MatchSetup { MapId = mapId, ModeId = "defuse", FallDamage = false, FriendlyFire = false,
				Bots = new() { Difficulty = "harmless", Quota = 2, JoinAfterPlayer = false, DontShoot = true } });
			GetTree().ChangeSceneToFile(entry.ScenePath);
			await Until(() => LanSession.Instance!.LocalPlayer != null && BotManager.Instance?.Bots.Count == 2, "spawn two DE bots");
			LanSession.Instance!.LocalPlayer!.JoinTeam(1);
			DefuseRules.RestartSeconds = 15; DeathmatchRules.TimeLimitMinutes = 0;
			Check(BotManager.Instance!.Bots.Count(b => b.Player.Team == PlayerTeam.Terrorist) == 1, "bots populate both teams");
			await Until(() => De.BombPlanted, "T bot reaches a site and plants without intervention");
			Check(De.Phase == DefusePhase.Live, "T bot plants using the real round objective");
			await Until(() => De.Result != DefuseResult.None, "bots finish objective");
			Check(De.Result == DefuseResult.BombDefused, "CT bot navigates to the bomb and holds use to defuse");
			GD.Print($"DE_BOT_CHECKS checks={_checks} failures=0"); GetTree().Quit();
		}
		catch (Exception e) { GD.PushError(e.ToString()); GD.Print($"DE_BOT_CHECKS checks={_checks} failures=1"); GetTree().Quit(1); }
	}
}
