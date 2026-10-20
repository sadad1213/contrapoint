using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Contrapoint.Players;

namespace Contrapoint.UI;

public static partial class ConsoleCommands
{
	private static bool _menuInitialized;
	public static bool IsOption(string name) => Variables.TryGetValue(name, out var v) && v.Archive && !v.Cheat && !v.ServerOnly;
	public static (float Min, float Max, bool Integer) OptionRange(string name)
	{
		var v = Variables[name];
		return (v.Min, v.Max, v.Integer);
	}
	public static float OptionValue(string name, Player? player = null)
	{
		var v = Variables[name];
		return player != null ? v.Get(player.Movement) : v.Global || name is "volume" or "fps_max" or "mat_vsync"
			? v.Get(null!) : Overrides.GetValueOrDefault(name, v.Default);
	}
	public static float OptionDefault(string name) => Variables[name].Default;

	// Read only data from the config before a player exists. Arbitrary autoexec commands
	// still execute through Initialize(Player), once the map and HUD are ready.
	public static void InitializeMenuOptions()
	{
		if (_menuInitialized || _initialized) return;
		_menuInitialized = true;
		_ = ConsoleBindings.Keys.Count;
		var path = ConfigDirectory + "/config.cfg";
		if (!FileAccess.FileExists(path) || !TryParse(FileAccess.GetFileAsString(path), out var commands)) return;
		foreach (var a in commands)
		{
			if (a.Count == 2 && IsOption(a[0]) && float.TryParse(a[1], System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture, out var value) && ValidOption(a[0], value))
			{
				Overrides[a[0]] = value;
				var v = Variables[a[0]];
				if (v.Global || a[0] is "volume" or "fps_max" or "mat_vsync") v.Set(null!, value);
			}
			else if (a[0].Equals("unbindall", StringComparison.OrdinalIgnoreCase)) ConsoleBindings.Keys.Clear();
			else if (a.Count == 3 && a[0].Equals("bind", StringComparison.OrdinalIgnoreCase) && ConsoleBindings.Normalize(a[1], out var key))
				ConsoleBindings.Keys[key] = a[2];
			else if (a.Count == 3 && a[0].Equals("alias", StringComparison.OrdinalIgnoreCase) && !Commands.ContainsKey(a[1]))
				Aliases[a[1]] = a[2];
		}
	}
	private static bool ValidOption(string name, float value) => Variables.TryGetValue(name, out var v)
		&& float.IsFinite(value) && value >= v.Min && value <= v.Max && (!v.Integer || value == MathF.Truncate(value));

	public static Error ApplyOptions(IReadOnlyDictionary<string, float> values, IReadOnlyDictionary<string, string> bindings, Player? player)
	{
		if (values.Any(p => !IsOption(p.Key) || !ValidOption(p.Key, p.Value)) || bindings.Keys.Any(k => !ConsoleBindings.Normalize(k, out _)))
			return Error.InvalidParameter;
		player?.Bindings.ReleaseAll();
		foreach (var pair in values)
		{
			var v = Variables[pair.Key];
			Overrides[pair.Key] = pair.Value;
			if (player != null || v.Global || pair.Key is "volume" or "fps_max" or "mat_vsync") v.Set(player?.Movement!, pair.Value);
		}
		ConsoleBindings.Keys.Clear();
		foreach (var pair in bindings) ConsoleBindings.Keys[pair.Key] = pair.Value;
		Error result = Error.Ok;
		WriteConfig(new[] { "host_writeconfig" }, player?.Movement!, (_, error) => { if (error) result = Error.CantCreate; });
		Changed?.Invoke();
		return result;
	}
}
