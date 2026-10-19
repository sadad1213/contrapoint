namespace Contrapoint.Gameplay;

// Vanilla CSS cs_gamerules.cpp defaults (not CS:GO competitive presets).
public static class DefuseRules
{
	public static bool Active => RoundState.Instance?.ModeId == "defuse";
	public static float RoundMinutes { get; set; } = 2.5f;
	public static float FreezeSeconds { get; set; } = 6;
	public static float BuyMinutes { get; set; } = 1.5f;
	public static float BombSeconds { get; set; } = 45;
	public static float RestartSeconds { get; set; } = 5;
	public static int StartMoney { get; set; } = 800;
	public static int MaxRounds { get; set; }
	public static int WinLimit { get; set; }
	public static int LimitTeams { get; set; } = 2;
	public const double PlantSeconds = 3;
	public const float UseDistance = 96 * .0254f;
}
