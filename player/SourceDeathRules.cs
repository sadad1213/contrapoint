namespace Contrapoint.Players;

public static class SourceDeathRules
{
	public const float DeathCamSeconds = .8f;
	// CSTRIKE_DLL defaults, rather than the generic Source/TF branch.
	public static float FreezeTravelSeconds { get; set; } = .7f;
	public static float FreezeSeconds { get; set; } = 5;
	public static float FreezeDistanceMin { get; set; } = 80;
	public static float FreezeDistanceMax { get; set; } = 90;
	public static bool DisableFreezeCam { get; set; }
	public static bool ServerDisableFreezeCam { get; set; }
	public static bool FreezeCamDisabled => DisableFreezeCam || ServerDisableFreezeCam;
	public static bool RagdollPhysics { get; set; } = true;
	public static float RagdollLifetime { get; set; } = 30;
	// One-team sandbox: respawn instead of waiting for a competitive round.
	public static bool AutoRespawn { get; set; } = true;
	public static float RespawnDelay { get; set; } = 6.5f;
}
