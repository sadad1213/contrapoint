using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Contrapoint.UI;

namespace Contrapoint.Players;

// Source keys.cpp/in_main.cpp: execute binds on key-down and release +commands
// on key-up. Track the original press, including alias expansions and rebinding.
public sealed class ConsoleBindings
{
	public static readonly Dictionary<string, string> Keys = new(StringComparer.OrdinalIgnoreCase);
	static ConsoleBindings()
	{
		// Defaults are data, shared by gameplay and standalone weapon previews.
		if (!ConsoleCommands.TryParse(FileAccess.GetFileAsString("res://cfg/default.cfg"), out var commands)) return;
		foreach (var tokens in commands)
		{
			if (tokens.Count == 3 && tokens[0].Equals("bind", StringComparison.OrdinalIgnoreCase) && Normalize(tokens[1], out var key)) Keys[key] = tokens[2];
		}
	}
	private readonly Player _player;
	private readonly Dictionary<string, List<string>> _pressed = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, HashSet<string>> _actions = new();
	private readonly Dictionary<string, int> _lookStates = new();
	private string _source = "console";
	public ConsoleBindings(Player player) => _player = player;

	public static void RemoveDefaultInputEvents()
	{
		foreach (var action in new[] { "move_forward", "move_back", "move_left", "move_right", "jump", "jump_scroll", "duck", "walk" })
		{
			if (!InputMap.HasAction(action)) InputMap.AddAction(action);
			InputMap.ActionEraseEvents(action);
		}
	}

	public static bool Normalize(string name, out string normalized)
	{
		normalized = name.ToUpperInvariant();
		normalized = normalized switch { "SEMICOLON" => ";", "BACKQUOTE" or "TILDE" => "`", "ESC" => "ESCAPE", "RETURN" => "ENTER", "CONTROL" => "CTRL", _ => normalized };
		normalized = normalized switch
		{
			"UPARROW" => "UP", "DOWNARROW" => "DOWN", "LEFTARROW" => "LEFT", "RIGHTARROW" => "RIGHT",
			"INS" => "INSERT", "DEL" => "DELETE", "PGUP" => "PAGEUP", "PGDN" => "PAGEDOWN",
			"KP_INS" => "KP 0", "KP_END" => "KP 1", "KP_DOWNARROW" => "KP 2", "KP_PGDN" => "KP 3",
			"KP_LEFTARROW" => "KP 4", "KP_5" => "KP 5", "KP_RIGHTARROW" => "KP 6", "KP_HOME" => "KP 7",
			"KP_UPARROW" => "KP 8", "KP_PGUP" => "KP 9", "KP_ENTER" => "KP ENTER", "KP_DEL" => "KP PERIOD",
			"KP_PLUS" => "KP ADD", "KP_MINUS" => "KP SUBTRACT", "KP_MULTIPLY" => "KP MULTIPLY", "KP_SLASH" => "KP DIVIDE", _ => normalized
		};
		if (normalized is "MOUSE1" or "MOUSE2" or "MOUSE3" or "MOUSE4" or "MOUSE5" or "MWHEELUP" or "MWHEELDOWN" or "`" or ";") return true;
		return OS.FindKeycodeFromString(normalized) != Key.None;
	}
	public static string? EventKey(InputEvent input) => input switch
	{
		InputEventKey key => KeyName(key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode),
		InputEventMouseButton mouse => mouse.ButtonIndex switch
		{
			MouseButton.Left => "MOUSE1", MouseButton.Right => "MOUSE2", MouseButton.Middle => "MOUSE3",
			MouseButton.Xbutton1 => "MOUSE4", MouseButton.Xbutton2 => "MOUSE5",
			MouseButton.WheelUp => "MWHEELUP", MouseButton.WheelDown => "MWHEELDOWN", _ => null
		}, _ => null
	};
	private static string KeyName(Key key) => key switch
	{
		Key.Quoteleft => "`", Key.Semicolon => ";", _ => OS.GetKeycodeString(key).ToUpperInvariant()
	};

	public bool Handle(InputEvent input)
	{
		var key = EventKey(input);
		if (key == null) return false;
		if (!input.IsPressed()) return Release(key);
		if (input is InputEventKey { Echo: true }) return Keys.ContainsKey(key);
		if (!Keys.TryGetValue(key, out var command)) return false;
		if (_pressed.ContainsKey(key)) return true;
		// Console and screenshots remain available in the death camera.
		if (!_player.HasLocalControl && !command.Equals("toggleconsole", StringComparison.OrdinalIgnoreCase)
			&& !command.Equals("chooseteam", StringComparison.OrdinalIgnoreCase)
			&& !command.Equals("+showscores", StringComparison.OrdinalIgnoreCase)
			&& !command.Equals("jpeg", StringComparison.OrdinalIgnoreCase) && !command.Equals("screenshot", StringComparison.OrdinalIgnoreCase)) return true;
		_pressed[key] = new();
		var first = command.Split(new[] { ' ', ';', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
		if (first?.StartsWith('+') == true && ConsoleCommands.IsAlias(first)) _pressed[key].Add("-" + first[1..]);
		var previous = _source;
		_source = key;
		try { ConsoleCommands.Execute(command, _player.Movement, Print); }
		finally { _source = previous; }
		if (key.StartsWith("MWHEEL", StringComparison.Ordinal)) Release(key);
		return true;
	}
	private void Print(string text, bool error) => _player.Console?.Print(text, error);
	private bool Release(string key)
	{
		if (!_pressed.Remove(key, out var releases)) return false;
		var previous = _source;
		_source = key;
		try { foreach (var command in releases) ConsoleCommands.Execute(command, _player.Movement, Print); }
		finally { _source = previous; }
		return true;
	}
	public void ReleaseAll()
	{
		foreach (var key in _pressed.Keys.ToArray()) Release(key);
		foreach (var action in _actions.Keys.ToArray()) SetAction(action, false, true);
		_lookStates.Clear();
		if (_player.Stats != null) _player.Stats.NetGraph.GraphHeld = false;
		_player.ShowMatchScores = false;
	}
	public void SyncWeapon()
	{
		foreach (var button in new[] { "attack", "attack2" })
			if (_actions.TryGetValue(button, out var sources) && sources.Count > 0)
				_player.ActiveWeapon?.HandleCommand(button, true);
	}
	public void Button(string command, bool down)
	{
		if (down && !_player.HasLocalControl && command != "showscores") return;
		if (down && _pressed.TryGetValue(_source, out var releases) && !releases.Contains("-" + command)) releases.Add("-" + command);
		var action = command switch
		{
			"forward" => "move_forward", "back" => "move_back", "moveleft" => "move_left", "moveright" => "move_right",
			"jump" => "jump", "duck" => "duck", "speed" => "walk", _ => command
		};
		var changed = SetAction(action, down);
		if (down && command == "jump") _player.QueueJump();
		if (!changed) return;
		if (command == "graph" && _player.Stats != null) _player.Stats.NetGraph.GraphHeld = down;
		if (command == "showscores") _player.ShowMatchScores = down;
		if (command == "use") _player.SetObjectiveUse(down);
		if (down && command is "attack" or "attack2" && Contrapoint.Gameplay.DefuseRules.Active
			&& Contrapoint.Gameplay.DefuseState.Instance?.CanAttack(_player) == false) return;
		_player.ActiveWeapon?.HandleCommand(command, down);
		if (down && command == "use") _player.Use();
	}
	private bool SetAction(string action, bool down, bool all = false)
	{
		if (!_actions.TryGetValue(action, out var sources)) _actions[action] = sources = new(StringComparer.OrdinalIgnoreCase);
		var wasDown = sources.Count > 0;
		if (all) sources.Clear();
		else if (down) sources.Add(_source);
		else sources.Remove(_source);
		var isDown = sources.Count > 0;
		if (wasDown != isDown && action is "left" or "right" or "lookup" or "lookdown")
		{
			_lookStates.TryGetValue(action, out var state);
			_lookStates[action] = isDown ? state | 3 : (state & ~1) | 4;
		}
		if (InputMap.HasAction(action))
		{
			if (isDown) Input.ActionPress(action);
			else Input.ActionRelease(action);
		}
		return wasDown != isDown;
	}

	// Source CInput::KeyState: half a tick on press, quarter on a quick tap.
	public float SampleLookButton(string action)
	{
		_lookStates.TryGetValue(action, out var state);
		_lookStates[action] = state & 1;
		return state switch { 1 => 1, 3 => .5f, 6 => .25f, 7 => .75f, _ => 0 };
	}
}
