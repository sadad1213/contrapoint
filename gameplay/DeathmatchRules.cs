using Contrapoint.Players;

namespace Contrapoint.Gameplay;

// Server-owned settings. DM deliberately does not overwrite the sandbox's death rules.
public static class DeathmatchRules
{
	public static bool Active => RoundState.Instance?.ModeId is "deathmatch" or "teamdeathmatch";
	public static bool FreeForAll { get; set; } = true;
	public static float TimeLimitMinutes { get; set; } = 10;
	public static float RespawnSeconds { get; set; } = 1;
	public static float ProtectionSeconds { get; set; } = 2;
	public static float RestartDelay { get; set; } = 5;
	public static int SpawnArmor { get; set; } = 100;
	public static bool SpawnHelmet { get; set; } = true;
	public static int KillHealth { get; set; } = 15;
	public static int KillAmmo { get; set; } = 15;
	public static bool Playing(Player player) => player.Team is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist;
	public static bool Teammates(Player first, Player second) => first != second && Playing(first)
		&& first.Team == second.Team && !(Active && FreeForAll);
}
