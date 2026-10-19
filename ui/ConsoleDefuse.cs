using Contrapoint.Gameplay;

namespace Contrapoint.UI;

public static partial class ConsoleCommands
{
	private static void RegisterDefuse()
	{
		RegisterVariable("mp_roundtime", 2.5f, 1, 9, _ => DefuseRules.RoundMinutes, (_, v) => DefuseRules.RoundMinutes = v,
			"CSS round length in minutes (next round).", false, serverOnly: true);
		RegisterVariable("mp_freezetime", 6, 0, 60, _ => DefuseRules.FreezeSeconds, (_, v) => DefuseRules.FreezeSeconds = v,
			"CSS freeze period in seconds (next round).", false, serverOnly: true);
		RegisterVariable("mp_buytime", 1.5f, .25f, 9, _ => DefuseRules.BuyMinutes, (_, v) => DefuseRules.BuyMinutes = v,
			"CSS buy period in minutes after freeze time; buying requires a team buy zone.", false, serverOnly: true);
		RegisterVariable("mp_c4timer", 45, 10, 90, _ => DefuseRules.BombSeconds, (_, v) => DefuseRules.BombSeconds = v,
			"CSS bomb fuse in seconds (next plant).", false, serverOnly: true);
		RegisterVariable("mp_round_restart_delay", 5, 0, 60, _ => DefuseRules.RestartSeconds, (_, v) => DefuseRules.RestartSeconds = v,
			"Seconds between a round result and the next round.", false, serverOnly: true);
		RegisterVariable("mp_startmoney", 800, 800, 16000, _ => DefuseRules.StartMoney, (_, v) => DefuseRules.StartMoney = (int)v,
			"Starting money on joining or a complete match restart.", false, true, serverOnly: true);
		RegisterVariable("mp_maxrounds", 0, 0, 999, _ => DefuseRules.MaxRounds, (_, v) => DefuseRules.MaxRounds = (int)v,
			"Maximum rounds before a match restart; 0 disables.", false, true, serverOnly: true);
		RegisterVariable("mp_winlimit", 0, 0, 999, _ => DefuseRules.WinLimit, (_, v) => DefuseRules.WinLimit = (int)v,
			"Team round wins before a match restart; 0 disables.", false, true, serverOnly: true);
		RegisterVariable("mp_limitteams", 2, 0, 30, _ => DefuseRules.LimitTeams, (_, v) => DefuseRules.LimitTeams = (int)v,
			"Maximum team size difference when joining; 0 disables.", false, true, serverOnly: true);
		Register("de_status", (_, _, print) =>
		{
			var de = DefuseState.Instance;
			print(DefuseRules.Active && de != null ? $"DE | round {de.RoundNumber} | {de.Phase} | T {de.TerroristWins} : CT {de.CounterTerroristWins} | {de.Result} | C4 {de.BombPlanted} {de.BombRemaining:0.0}s" : "Bomb Defusal is not running.", false);
		});
	}
}
