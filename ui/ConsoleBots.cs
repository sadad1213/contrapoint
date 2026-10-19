using System;
using System.Linq;
using Contrapoint.Gameplay;
using Contrapoint.Gameplay.Bots;

namespace Contrapoint.UI;

public static partial class ConsoleCommands
{
	private static BotSetup BotSettings => BotManager.Instance?.Settings ?? BotManager.Defaults;
	private static void ConfigureBots(BotSetup settings)
	{
		BotManager.Defaults = settings;
		BotManager.Instance?.Configure(settings);
	}
	private static void RegisterBots()
	{
		void Toggle(string name, Func<BotSetup, bool> read, Func<BotSetup, bool, BotSetup> write, string description) =>
			RegisterVariable(name, read(new()) ? 1 : 0, 0, 1, _ => read(BotSettings) ? 1 : 0,
				(_, value) => ConfigureBots(write(BotSettings, value != 0)), description, false, true, serverOnly: true);
		void Choice(string name, string[] choices, Func<BotSetup, string> read, Func<BotSetup, string, BotSetup> write) =>
			Register(name, (a, _, print) =>
			{
				if (a.Count == 1) { print($"\"{name}\" = \"{read(BotSettings)}\"", false); return; }
				if (a.Count != 2 || !choices.Contains(a[1])) { print("Usage: " + name + " <" + string.Join('|', choices) + ">", true); return; }
				ConfigureBots(write(BotSettings, a[1]));
			}, () => choices, serverOnly: true);
		Toggle("bot_stop", b => b.Stop, (b, v) => b with { Stop = v }, "Stop bot movement; perception and firing continue.");
		Toggle("bot_dont_shoot", b => b.DontShoot, (b, v) => b with { DontShoot = v }, "Disable bot attacks immediately.");
		Toggle("bot_join_after_player", b => b.JoinAfterPlayer, (b, v) => b with { JoinAfterPlayer = v }, "Wait until a human has joined a playing team.");
		Toggle("bot_allow_pistols", b => b.Pistols, (b, v) => b with { Pistols = v }, "Allow pistols in bot loadouts on their next spawn.");
		Toggle("bot_allow_rifles", b => b.Rifles, (b, v) => b with { Rifles = v }, "Allow AK-47 and M4A1 on the next bot spawn.");
		Toggle("bot_allow_snipers", b => b.Snipers, (b, v) => b with { Snipers = v }, "Allow AWP and Scout on the next bot spawn.");
		Choice("bot_join_team", ["any", "t", "ct"], b => b.Team, (b, v) => b with { Team = v });
		Register("bot_prefix", (a, _, print) =>
		{
			if (a.Count == 1) { print($"\"bot_prefix\" = \"{BotSettings.Prefix}\"", false); return; }
			if (a.Count != 2 || a[1].Length > 32) { print("Usage: bot_prefix \"text\" (up to 32 characters; new bots only)", true); return; }
			ConfigureBots(BotSettings with { Prefix = a[1] });
		}, serverOnly: true);
		RegisterVariable("bot_quota", 0, 0, 15, _ => BotSettings.Quota,
			(_, value) => ConfigureBots(BotSettings with { Quota = (int)value,
				Difficulty = BotSettings.Difficulty == "none" ? "normal" : BotSettings.Difficulty }),
			"Number of DM bots on Dust II, Inferno and Mirage.", false, true, serverOnly: true);
		RegisterVariable("bot_difficulty", 1, 0, 3,
			_ => BotSettings.Difficulty switch { "easy" => 0, "hard" => 2, "expert" => 3, _ => 1 },
			(_, value) => ConfigureBots(BotSettings with { Difficulty = new[] { "easy", "normal", "hard", "expert" }[(int)value] }),
			"CSS difficulty of newly added bots: 0 easy, 1 normal, 2 hard, 3 expert.", false, true, serverOnly: true);
		foreach (var command in new[] { "bot_add", "bot_add_t", "bot_add_ct" })
			Register(command, (a, _, print) =>
			{
				var manager = BotManager.Instance;
				if (manager?.Supported != true) { print("Bots require Deathmatch or Team Deathmatch on Dust II, Inferno and Mirage.", true); return; }
				if (a.Count != 1) { print("Usage: " + command, true); return; }
				if (!manager.AddOne(command == "bot_add_t" ? "t" : command == "bot_add_ct" ? "ct" : null))
					print(manager.NavigationReady ? "The player limit has been reached." : "Bot navigation is still loading.", true);
			}, serverOnly: true);
		Register("bot_kick", (a, _, print) =>
		{
			if (a.Count > 2) { print("Usage: bot_kick [name|all]", true); return; }
			if (BotManager.Instance is { } manager) manager.Kick(a.Count == 2 ? a[1] : "all");
			else if (a.Count == 1 || a[1] == "all") ConfigureBots(BotSettings with { Quota = 0 });
		}, serverOnly: true);
		Register("bot_quota_mode", (a, _, print) =>
		{
			if (a.Count == 1) { print(BotSettings.QuotaMode, false); return; }
			if (a.Count != 2 || a[1] is not ("normal" or "fill" or "match")) { print("Usage: bot_quota_mode <normal|fill|match>", true); return; }
			ConfigureBots(BotSettings with { QuotaMode = a[1] });
		}, () => new[] { "normal", "fill", "match" }, serverOnly: true);
		Register("bot_status", (_, _, print) =>
		{
			var manager = BotManager.Instance;
			if (manager?.Supported != true) { print("Bots require DM/TDM on Dust II, Inferno and Mirage.", false); return; }
			print($"Bots: {manager.Bots.Count}; quota {manager.Settings.Quota} ({manager.Settings.QuotaMode}); difficulty {manager.Settings.Difficulty}; stop {(manager.Settings.Stop ? 1 : 0)}; dont_shoot {(manager.Settings.DontShoot ? 1 : 0)}", false);
			foreach (var bot in manager.Bots)
				print($"{bot.Player.DisplayName}: {bot.Player.Team}, {bot.State}, HP {bot.Player.Health}, shots {bot.ShotsFired}, kills {bot.Player.MatchKills}, deaths {bot.Player.MatchDeaths}", false);
		});
	}
}
