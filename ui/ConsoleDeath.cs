using Contrapoint.Players;

namespace Contrapoint.UI;

public static partial class ConsoleCommands
{
	private static void RegisterDeath()
	{
		Register("jpeg", (_, m, print) => ConsoleScreenshot.Save(Owner(m), true, print));
		Register("screenshot", (_, m, print) => ConsoleScreenshot.Save(Owner(m), false, print));
		RegisterVariable("cl_ragdoll_physics_enable", 1, 0, 1, _ => SourceDeathRules.RagdollPhysics ? 1 : 0,
			(_,v) => SourceDeathRules.RagdollPhysics = v != 0, "Enable skeletal corpse physics for newly created ragdolls.", true, true);
		RegisterVariable("cl_disablefreezecam", 0, 0, 1, _ => SourceDeathRules.DisableFreezeCam ? 1 : 0,
			(_,v) => SourceDeathRules.DisableFreezeCam = v != 0, "Use the corpse death camera instead of the killer freeze camera.", true, true);
		RegisterVariable("sv_disablefreezecam", 0, 0, 1, _ => SourceDeathRules.ServerDisableFreezeCam ? 1 : 0,
			(_,v) => SourceDeathRules.ServerDisableFreezeCam = v != 0, "Turn on/off freezecam on server.", false, true, serverOnly: true);
		RegisterVariable("spec_freeze_distance_min", 80, 1, 1000, _ => SourceDeathRules.FreezeDistanceMin,
			(_,v) => SourceDeathRules.FreezeDistanceMin = v, "Minimum random freeze-camera distance in Source units.", false, cheat: true);
		RegisterVariable("spec_freeze_distance_max", 90, 1, 1000, _ => SourceDeathRules.FreezeDistanceMax,
			(_,v) => SourceDeathRules.FreezeDistanceMax = v, "Maximum random freeze-camera distance in Source units.", false, cheat: true);
		RegisterVariable("spec_freeze_time", 5, 0, 30, _ => SourceDeathRules.FreezeSeconds,
			(_,v) => SourceDeathRules.FreezeSeconds = v, "CSS killer freeze-camera hold time.", false, serverOnly: true, cheat: true);
		RegisterVariable("spec_freeze_traveltime", .7f, .01f, 5, _ => SourceDeathRules.FreezeTravelSeconds,
			(_,v) => SourceDeathRules.FreezeTravelSeconds = v, "CSS killer freeze-camera travel time.", false, serverOnly: true, cheat: true);
		RegisterVariable("sv_autorespawn", 1, 0, 1, _ => SourceDeathRules.AutoRespawn ? 1 : 0,
			(_,v) => SourceDeathRules.AutoRespawn = v != 0, "Contrapoint: automatically respawn in the one-team sandbox.", false, true, serverOnly: true);
		RegisterVariable("sv_respawn_delay", 6.5f, .8f, 120, _ => SourceDeathRules.RespawnDelay,
			(_,v) => SourceDeathRules.RespawnDelay = v, "Contrapoint: seconds from death to automatic respawn.", false, serverOnly: true);
	}
}
