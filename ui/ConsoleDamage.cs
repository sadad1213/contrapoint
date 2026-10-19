using Godot;
using System;
using System.Globalization;
using Contrapoint.Players;
using Contrapoint.Network;

namespace Contrapoint.UI;

public static partial class ConsoleCommands
{
	private static void RegisterDamage()
	{
		RegisterVariable("violence_hblood", 1, 0, 1, _ => SourceDamageEffects.BloodEnabled ? 1 : 0,
			(_,v) => SourceDamageEffects.BloodEnabled = v != 0, "Draw human blood effects.", true, true);
		RegisterVariable("sv_cheats", 0, 0, 1, _ => SourceDamageRules.Cheats ? 1 : 0,
			(m,v) => SetCheats(v != 0, Owner(m).GetTree()),
			"Allow cheat commands on this server.", false, true, serverOnly: true);
		RegisterVariable("mp_friendlyfire", 1, 0, 1, _ => SourceDamageRules.FriendlyFire ? 1 : 0,
			(_,v) => SourceDamageRules.FriendlyFire = v != 0,
			"Allow team damage. CSS scales teammate bullets and knife damage to 35%; HE retains full damage.", false, true, serverOnly: true);
		RegisterVariable("sv_legacy_grenade_damage", 0, 0, 1, _ => SourceDamageRules.LegacyGrenadeDamage ? 1 : 0,
			(_,v) => SourceDamageRules.LegacyGrenadeDamage = v != 0,
			"Use original CSS grenade armor behavior: the thrower's current weapon armor ratio instead of the HE ratio.", false, true, serverOnly: true);
		RegisterVariable("sv_falldamage", 1, 0, 1, _ => SourceDamageRules.FallDamageEnabled ? 1 : 0,
			(_,v) => SourceDamageRules.FallDamageEnabled = v != 0,
			"Contrapoint extension: enable fall damage. 0 disables only fall damage. Stock CSS has no equivalent switch.", false, true, serverOnly: true);
		RegisterVariable("player_debug_print_damage", 0, 0, 1, _ => SourceDamageRules.DebugDamage ? 1 : 0,
			(_,v) => SourceDamageRules.DebugDamage = v != 0,
			"Print damage type, hitgroup, health and armor damage.", false, true, serverOnly: true, cheat: true);
		foreach (var mode in new[] { "god", "buddha" }) Register(mode, (args, movement, print) =>
		{
			if (args.Count > 2 || args.Count == 2 && args[1] is not ("0" or "1")) { print("Usage: " + mode + " [0|1]", true); return; }
			var player = Owner(movement);
			if (mode == "god" && LanSession.Instance?.RequestPlayerCheat(player, mode, args.Count == 2 ? args[1] : "", print) == true) return;
			var enabled = args.Count == 2 ? args[1] == "1" : !(mode == "god" ? player.GodMode : player.BuddhaMode);
			if (mode == "god") player.GodMode = enabled; else player.BuddhaMode = enabled;
			print(mode + " " + (enabled ? "ON" : "OFF"), false);
		}, cheat:true, serverOnly:mode != "god");
		Register("hurtme", (args, movement, print) =>
		{
			if (args.Count > 2 || args.Count == 2 && (!float.TryParse(args[1], NumberStyles.Float,
				CultureInfo.InvariantCulture, out var parsed) || !float.IsFinite(parsed) || parsed <= 0 || parsed > 100000))
			{ print("Usage: hurtme [positive damage, default 10]", true); return; }
			Owner(movement).TakeDebugDamage(args.Count == 1 ? 10 : float.Parse(args[1], CultureInfo.InvariantCulture));
		}, cheat:true, serverOnly:true);
	}
	// Also used for replicated rules: disabling cheats must clear client presentation.
	public static void SetCheats(bool enabled, SceneTree tree)
	{
		SourceDamageRules.Cheats = enabled;
		if (enabled) return;
		foreach (var pair in Variables)
			if (pair.Value.Cheat) Overrides.Remove(pair.Key);
		foreach (Player player in tree.GetNodesInGroup("source_players"))
		{
			player.GodMode = player.BuddhaMode = false;
			if (player.Movement.Noclip && player.Team is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist)
				player.Movement.ToggleNoclip();
			if (!player.LocalInput) continue;
			player.SetThirdPerson(false);
			foreach (var variable in Variables.Values)
				if (variable.Cheat) variable.Set(player.Movement, variable.Default);
		}
		Changed?.Invoke();
	}
}
