using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Contrapoint.Players;
using Contrapoint.Maps;

namespace Contrapoint.UI;

// The console changes the same movement property used by every physics tick.
public static partial class ConsoleCommands
{
	public const string AirAccelerate = "sv_airaccelerate";
	public delegate void CommandHandler(IReadOnlyList<string> arguments, SourceMovement movement, Action<string, bool> print);
	private sealed record Command(CommandHandler Execute, Func<IEnumerable<string>>? CompleteArguments, bool Cheat, bool ServerOnly);
	private static readonly Dictionary<string, Command> Commands = new(StringComparer.OrdinalIgnoreCase);
	public static event Action? Changed;

	static ConsoleCommands()
	{
		Register("map", ExecuteMap, () => MapCatalog.Discover().Select(map => map.Id));
		RegisterBasics();
		RegisterWeapons();
		RegisterDamage();
		RegisterDeath();
		RegisterDeathmatch();
		RegisterDefuse();
		RegisterBots();
		RegisterHud();
	}

	// Register once: execution and autocomplete always use the same command catalog.
	public static void Register(string name, CommandHandler execute, Func<IEnumerable<string>>? completeArguments = null,
		bool cheat = false, bool serverOnly = false)
	{
		if (string.IsNullOrWhiteSpace(name) || name.Any(c => char.IsWhiteSpace(c) || c is ';' or '"'))
			throw new ArgumentException("Command names must be a single token.", nameof(name));
		ArgumentNullException.ThrowIfNull(execute);
		Commands[name] = new(execute, completeArguments, cheat, serverOnly);
		Changed?.Invoke();
	}

	public static IReadOnlyList<string> GetCompletions(string input)
	{
		// Complete only the last command, preserving earlier commands and quoted semicolons.
		var start = 0;
		var quoted = false;
		for (var i = 0; i < input.Length; i++)
		{
			if (quoted && input[i] == '\\' && i + 1 < input.Length && input[i + 1] is '\\' or '"') { i++; continue; }
			if (input[i] == '"') quoted = !quoted;
			if (input[i] == ';' && !quoted) start = i + 1;
		}
		while (start < input.Length && char.IsWhiteSpace(input[start])) start++;
		var prefix = input[start..];
		if (prefix.Length == 0) return Array.Empty<string>();
		var separator = 0;
		while (separator < prefix.Length && !char.IsWhiteSpace(prefix[separator])) separator++;
		IEnumerable<string> matches;
		if (separator == prefix.Length)
			matches = Commands.Keys.Concat(Aliases.Keys).Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
		else
		{
			var name = prefix[..separator];
			if (!Commands.TryGetValue(name, out var command) || command.CompleteArguments == null)
				return Array.Empty<string>();
			var argument = prefix[separator..].TrimStart().Trim('"');
			matches = command.CompleteArguments().Where(value => value.StartsWith(argument, StringComparison.OrdinalIgnoreCase))
				.Select(value => name + " " + (value.Any(char.IsWhiteSpace) ? "\"" + value + "\"" : value));
		}
		return matches.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
			.Select(value => input[..start] + value).ToArray();
	}

	public static string Describe(SourceMovement movement) =>
		$"\"{AirAccelerate}\" = \"{DisplayNumber(movement.AirAcceleration)}\" ( def. \"100\" )";

	public static bool TryParse(string input, out List<List<string>> commands)
	{
		// Parse the complete line first: malformed quotes must not execute half a batch.
		commands = new List<List<string>>();
		var arguments = new List<string>();
		var token = new StringBuilder();
		var quoted = false;
		var started = false;
		for (var i = 0; i <= input.Length; i++)
		{
			var character = i == input.Length ? ';' : input[i];
			if (quoted && character == '\\' && i + 1 < input.Length && input[i + 1] is '\\' or '"')
			{ token.Append(input[++i]); started = true; continue; }
			if (character == '"') { quoted = !quoted; started = true; continue; }
			if (!quoted && character == '/' && i + 1 < input.Length && input[i + 1] == '/')
			{
				while (i < input.Length && input[i] != '\n') i++;
				character = '\n';
			}
			if (!quoted && (char.IsWhiteSpace(character) || character == ';'))
			{
				if (started) { arguments.Add(token.ToString()); token.Clear(); started = false; }
				if (character is ';' or '\n' or '\r' && arguments.Count > 0) { commands.Add(arguments); arguments = new(); }
			}
			else { token.Append(character); started = true; }
		}
		return !quoted;
	}

	public static void Execute(string input, SourceMovement movement, Action<string, bool> print)
	{
		if (!TryParse(input, out var commands)) { print("Unterminated quoted string.", true); return; }
		if (_executionDepth >= 32) { print("Command recursion limit reached (alias/exec).", true); return; }
		if (_executionDepth == 0) _commandBudget = 4096;
		_executionDepth++;
		try { foreach (var command in commands)
		{
			if (--_commandBudget < 0) { print("Command execution limit reached.", true); return; }
			if (Commands.TryGetValue(command[0], out var registered))
			{
				if (registered.ServerOnly && Contrapoint.Network.LanSession.Instance?.IsClient == true)
				{ print("This command requires the server host console.", true); continue; }
				if (registered.Cheat && !SourceDamageRules.Cheats)
				{ print(command[0] + " requires sv_cheats 1.", true); continue; }
				registered.Execute(command, movement, print);
			}
			else if (Aliases.TryGetValue(command[0], out var alias)) Execute(alias, movement, print);
			else
				print($"Unknown command: {command[0]}", true);
		} } finally
		{
			_executionDepth--;
			// Persist a complete command batch while the player and HUD are alive.
			// Do not depend on scene teardown or an orderly application shutdown.
			if (_executionDepth == 0 && _configDirty && !_loading && _initialized)
				Save(Owner(movement));
		}
	}

	public static void ExecuteInMenu(string input, DeveloperConsole console, Action<string, bool> print)
	{
		if (!TryParse(input, out var commands)) { print("Unterminated quoted string.", true); return; }
		if (_executionDepth >= 32) { print("Command recursion limit reached (alias/exec).", true); return; }
		if (_executionDepth == 0) _commandBudget = 4096;
		_executionDepth++;
		try { foreach (var command in commands)
		{
			if (--_commandBudget < 0) { print("Command execution limit reached.", true); return; }
			var name = command[0].ToLowerInvariant();
			if (name == "toggleconsole") console.Toggle();
			else if (name == "clear") console.Clear();
			else if (name is "quit" or "exit") console.GetTree().Quit();
			else if (name == "map") ExecuteMap(command, null!, print);
			else if (Aliases.TryGetValue(name, out var alias)) ExecuteInMenu(alias, console, print);
			else if (!Commands.TryGetValue(name, out var registered)) print($"Unknown command: {command[0]}", true);
			else if (name is "connect" or "lan" or "echo" or "maps" or "bind" or "bindtoggle" or "unbind" or "unbindall" or "key_listboundkeys" or "key_findbinding" or "alias" or "cmdlist")
				registered.Execute(command, null!, print);
			else print($"Command {command[0]} requires a loaded map.", true);
		} } finally { _executionDepth--; }
	}

	private static void ExecuteMap(IReadOnlyList<string> command, SourceMovement movement, Action<string, bool> print)
	{
		if (command.Count != 2) { print("Usage: map <" + string.Join('|', MapCatalog.Discover().Select(map => map.Id)) + ">", true); return; }
		var name = command[1];
		var map = MapCatalog.Find(name);
		if (map == null)
		{ print("Unknown map. Available: " + string.Join(", ", MapCatalog.Discover().Select(entry => entry.Id)), true); return; }
		print("Loading " + command[1], false);
		Contrapoint.Network.LanSession.Instance?.Stop();
		((Godot.SceneTree)Godot.Engine.GetMainLoop()).CallDeferred(Godot.SceneTree.MethodName.ChangeSceneToFile, map.ScenePath);
	}

}
