using Contrapoint.Players;

namespace Contrapoint.UI;

public static partial class ConsoleCommands
{
	private static CssHud Hud(SourceMovement movement) => Owner(movement).GetNode<CssHud>("HUD/Status");
	private static void RegisterHud()
	{
		RegisterVariable("cl_drawhud", 1, 0, 1, m => Hud(m).Enabled ? 1 : 0,
			(m, v) => Hud(m).Enabled = v != 0, "Draw the game HUD.", archive: false, integer: true, cheat: true);
		RegisterVariable("hud_deathnotice_time", 6, 0, 60, m => Owner(m).KillFeed?.Duration ?? 6,
			(m, v) => { if (Owner(m).KillFeed is { } feed) feed.Duration = v; }, "Seconds to show a kill-feed entry; 0 disables notices.", archive: false);
		RegisterVariable("hud_deathnotice_scale", 1.25f, .5f, 3, m => Owner(m).KillFeed?.UiScale ?? 1.25f,
			(m, v) => { if (Owner(m).KillFeed is { } feed) feed.UiScale = v; }, "Contrapoint: scale the CSSO kill-feed text, icons and rows together.");
		RegisterVariable("cl_radaralpha", 200, 0, 255, m => Hud(m).Radar.MapAlpha * 255,
			(m, v) => Hud(m).Radar.MapAlpha = v / 255, "Radar map opacity.", integer: true);
		RegisterVariable("cl_radar_locked", 0, 0, 1, m => Hud(m).Radar.RotateMap ? 0 : 1,
			(m, v) => Hud(m).Radar.RotateMap = v == 0, "Keep north at the top of the radar.", integer: true);
		Register("hideradar", (_, m, _) => Hud(m).Radar.Enabled = false);
		Register("drawradar", (_, m, _) => Hud(m).Radar.Enabled = true);
	}
}
