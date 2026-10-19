using Contrapoint.Gameplay;
using Contrapoint.Network;

namespace Contrapoint.UI;

public static partial class ConsoleCommands
{
	private static void RegisterDeathmatch()
	{
		Register("+showscores", (_, m, _) => Owner(m).Bindings.Button("showscores", true));
		Register("-showscores", (_, m, _) => Owner(m).Bindings.Button("showscores", false));
		RegisterVariable("mp_timelimit", 10, 0, 99, _ => DeathmatchRules.TimeLimitMinutes,
			(_, v) => { DeathmatchRules.TimeLimitMinutes = v; RoundState.Instance?.RefreshTimeLimit(); },
			"DM map duration in minutes; 0 disables the map timer. Changes include time already played.", false, serverOnly: true);
		RegisterVariable("mp_teammates_are_enemies", 1, 0, 1, _ => DeathmatchRules.FreeForAll ? 1 : 0,
			(_, v) => DeathmatchRules.FreeForAll = v != 0, "DM: 1 free-for-all, 0 T vs CT.", false, true, serverOnly: true);
		RegisterVariable("dm_respawn_delay", 1, .1f, 30, _ => DeathmatchRules.RespawnSeconds,
			(_, v) => DeathmatchRules.RespawnSeconds = v, "DM automatic and earliest manual respawn delay in seconds.", false, serverOnly: true);
		RegisterVariable("dm_spawn_protection", 2, 0, 30, _ => DeathmatchRules.ProtectionSeconds,
			(_, v) => DeathmatchRules.ProtectionSeconds = v, "DM spawn protection in seconds; attacking cancels protection.", false, serverOnly: true);
		RegisterVariable("dm_spawn_armor", 100, 0, 100, _ => DeathmatchRules.SpawnArmor,
			(_, v) => DeathmatchRules.SpawnArmor = (int)v, "DM armor on next spawn.", false, true, serverOnly: true);
		RegisterVariable("dm_spawn_helmet", 1, 0, 1, _ => DeathmatchRules.SpawnHelmet ? 1 : 0,
			(_, v) => DeathmatchRules.SpawnHelmet = v != 0, "DM helmet on next spawn.", false, true, serverOnly: true);
		RegisterVariable("dm_kill_health", 15, 0, 100, _ => DeathmatchRules.KillHealth,
			(_, v) => DeathmatchRules.KillHealth = (int)v, "Health per enemy kill, capped at maximum HP.", false, true, serverOnly: true);
		RegisterVariable("dm_kill_ammo", 15, 0, 100, _ => DeathmatchRules.KillAmmo,
			(_, v) => DeathmatchRules.KillAmmo = (int)v, "Rounds added to the active firearm magazine per enemy kill, capped at clip capacity.", false, true, serverOnly: true);
		RegisterVariable("dm_restart_delay", 5, 0, 60, _ => DeathmatchRules.RestartDelay,
			(_, v) => DeathmatchRules.RestartDelay = v, "Countdown before reloading the map when its time limit expires.", false, serverOnly: true);
		Register("mp_restartgame", (a, _, print) =>
		{
			if (a.Count != 2 || !int.TryParse(a[1], out var seconds) || seconds is < 0 or > 60)
			{ print("Usage: mp_restartgame <0..60 seconds>; 0 cancels a scheduled restart.", true); return; }
			var round = RoundState.Instance;
			if (DefuseRules.Active) { DefuseState.Instance?.ScheduleRestart(seconds); return; }
			if (!DeathmatchRules.Active || round == null) { print("A deathmatch must be running.", true); return; }
			if (seconds == 0) round.CancelRestart(); else round.ScheduleRestart(seconds);
		}, serverOnly: true);
		Register("dm_status", (_, _, print) =>
		{
			var round = RoundState.Instance;
			print(DeathmatchRules.Active ? $"{(DeathmatchRules.FreeForAll ? "FFA" : "TDM")} | {round!.DisplaySeconds / 60}:{round.DisplaySeconds % 60:00} | T {round.TerroristKills} : CT {round.CounterTerroristKills} | restart {round.RestartPending}" : "Deathmatch is not running.", false);
		});
	}
}
