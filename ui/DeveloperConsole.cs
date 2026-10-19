using Godot;
using System;
using System.Collections.Generic;
using Contrapoint.Players;

namespace Contrapoint.UI;

// Source's VGUI console, recreated with Godot controls. Colors and fonts follow
// CSS hl2/resource/SourceScheme.res -> platform/resource/SourceSchemeBase.res.
public partial class DeveloperConsole : CanvasLayer
{
	private const int MaxHistory = 128;
	private const int MaxOutputLines = 512;
	private static readonly Color TextColor = new("dddddd");
	private static readonly Color Orange = new("ff9b00");
	private readonly List<string> _history = new();
	private Player? _player;
	private Control _root = null!;
	private Panel _frame = null!;
	private RichTextLabel _output = null!;
	private LineEdit _entry = null!;
	private ItemList _suggestions = null!;
	private IReadOnlyList<string> _completions = Array.Empty<string>();
	private int _historyIndex;
	private string _draft = "";
	private bool _restoreCapture;
	private bool _dragging;
	private bool _resizing;
	private Vector2 _pointerStart;
	private Vector2 _frameStart;
	private int _outputLines;
	public bool IsOpen => Visible;

	public override void _Ready()
	{
		_player = GetParent() as Player;
		Layer = 100;
		Visible = false;
		BuildInterface();
		ConsoleCommands.Changed += UpdateSuggestion;
		Print("Contrapoint\n");
	}

	public override void _ExitTree() => ConsoleCommands.Changed -= UpdateSuggestion;

	public void Toggle() => SetOpen(!IsOpen);

	public void SetOpen(bool open)
	{
		if (open == IsOpen) return;
		Visible = open;
		_dragging = _resizing = false;
		if (open)
		{
			_restoreCapture = _player?.MouseCaptured == true;
			_player?.SetMouseCaptured(false);
			ClampFrame();
			_entry.GrabFocus();
			_entry.CaretColumn = _entry.Text.Length;
			UpdateSuggestion();
		}
		else
		{
			_entry.ReleaseFocus();
			// Headless checks use Player's explicit control state, without an OS window.
			if (_restoreCapture && (DisplayServer.GetName() == "headless" || GetWindow().HasFocus()))
				_player?.SetMouseCaptured(true);
		}
	}

	// Called before Player's gameplay input, so text and UI clicks cannot leak into it.
	public bool HandleInput(InputEvent @event)
	{
		var boundKey = ConsoleBindings.EventKey(@event);
		if (boundKey != null && ConsoleBindings.Keys.TryGetValue(boundKey, out var binding) &&
			binding.Equals("toggleconsole", StringComparison.OrdinalIgnoreCase))
		{
			if (@event.IsPressed() && @event is not InputEventKey { Echo: true }) Toggle();
			GetViewport().SetInputAsHandled();
			return true;
		}
		if (@event is InputEventKey key)
		{
			var code = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
			if (IsOpen && key.Pressed)
			{
				switch (code)
				{
					case Key.Escape: SetOpen(false); break;
					case Key.Up when _entry.HasFocus(): Browse(-1); break;
					case Key.Down when _entry.HasFocus(): Browse(1); break;
					case Key.Tab: Complete(); break;
					case Key.Pageup: Scroll(-1); break;
					case Key.Pagedown: Scroll(1); break;
					default: return true; // GUI still receives ordinary typing / selection shortcuts.
				}
				GetViewport().SetInputAsHandled();
				return true;
			}
		}
		if (IsOpen && (_dragging || _resizing))
		{
			if (@event is InputEventMouseMotion)
			{
				var difference = _root.GetLocalMousePosition() - _pointerStart;
				if (_dragging) _frame.Position = _frameStart + difference;
				else _frame.Size = _frameStart + difference;
				ClampFrame();
				GetViewport().SetInputAsHandled();
			}
			if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
				_dragging = _resizing = false;
		}
		return IsOpen;
	}

	public void Execute(string command)
	{
		command = command.Trim();
		if (command.Length == 0) return;
		Print("] " + command);
		if (_history.Count == 0 || _history[^1] != command) _history.Add(command);
		if (_history.Count > MaxHistory) _history.RemoveAt(0);
		_historyIndex = _history.Count;
		_draft = "";
		if (_player != null) ConsoleCommands.Execute(command, _player.Movement, Print);
		else ConsoleCommands.ExecuteInMenu(command, this, Print);
	}

	public void Print(string text, bool error = false)
	{
		// BBCode is disabled; commands and engine text are always literal text.
		_output.PushColor(error ? new Color("ff6464") : TextColor);
		_output.AddText(text + "\n");
		_output.Pop();
		_outputLines += text.Split('\n').Length;
		while (_outputLines > MaxOutputLines)
		{
			_output.RemoveParagraph(0);
			_outputLines--;
		}
	}

	public void Clear()
	{
		_output.Clear();
		_outputLines = 0;
	}

	private void Submit()
	{
		Execute(_entry.Text);
		_entry.Clear();
		UpdateSuggestion();
		_entry.GrabFocus();
	}

	private void BrowseHistory(int direction)
	{
		if (_history.Count == 0) return;
		if (_historyIndex == _history.Count) _draft = _entry.Text;
		_historyIndex = Math.Clamp(_historyIndex + direction, 0, _history.Count);
		_entry.Text = _historyIndex == _history.Count ? _draft : _history[_historyIndex];
		_entry.CaretColumn = _entry.Text.Length;
		UpdateSuggestion();
	}

	private void Browse(int direction)
	{
		if (!_suggestions.Visible) { BrowseHistory(direction); return; }
		var selected = _suggestions.GetSelectedItems();
		var index = selected.Length == 0 ? 0 : selected[0];
		_suggestions.Select((index + direction + _completions.Count) % _completions.Count);
		_suggestions.EnsureCurrentIsVisible();
	}

	private void Complete(int index = -1)
	{
		if (_completions.Count == 0) return;
		var selected = _suggestions.GetSelectedItems();
		if (index < 0) index = selected.Length == 0 ? 0 : selected[0];
		_entry.Text = _completions[index] + " ";
		_entry.GrabFocus();
		_entry.CaretColumn = _entry.Text.Length;
		UpdateSuggestion();
	}

	private void UpdateSuggestion()
	{
		_completions = ConsoleCommands.GetCompletions(_entry.Text);
		_suggestions.Clear();
		foreach (var completion in _completions) _suggestions.AddItem(ConsoleCommands.GetCompletionText(completion, _player?.Movement));
		_suggestions.Visible = _completions.Count > 0;
		if (_suggestions.Visible) _suggestions.Select(0);
		ClampFrame();
	}

	private void PositionSuggestions()
	{
		_suggestions.Position = _frame.Position + new Vector2(_entry.Position.X, _frame.Size.Y + 2);
		_suggestions.Size = new(_entry.Size.X,
			Mathf.Min(160, Mathf.Min(_completions.Count * 24 + 8, _root.Size.Y - _suggestions.Position.Y)));
	}

	private void Scroll(int direction)
	{
		var scroll = _output.GetVScrollBar();
		scroll.Value += direction * scroll.Page * 0.8;
	}

	private void ClampFrame()
	{
		var available = _root.Size;
		if (available.X <= 0 || available.Y <= 0) return;
		if (_suggestions.Visible) available.Y -= 36; // Keep the dropdown below the frame even at the screen edge.
		_frame.Size = new(Mathf.Clamp(_frame.Size.X, Mathf.Min(480, available.X), available.X),
			Mathf.Clamp(_frame.Size.Y, Mathf.Min(300, available.Y), available.Y));
		_frame.Position = new(Mathf.Clamp(_frame.Position.X, 0, available.X - _frame.Size.X),
			Mathf.Clamp(_frame.Position.Y, 0, available.Y - _frame.Size.Y));
		PositionSuggestions();
	}

	private static StyleBoxFlat Box(Color color, bool inset = false)
	{
		var style = new StyleBoxFlat { BgColor = color, BorderColor = inset ? new("282828c4") : new("c8c8c8c4") };
		style.SetBorderWidthAll(1);
		style.ContentMarginLeft = style.ContentMarginRight = 5;
		style.ContentMarginTop = style.ContentMarginBottom = 3;
		return style;
	}

	private static void Place(Control control, float left, float top, float right, float bottom,
		float anchorRight = 0, float anchorBottom = 0, float anchorTop = 0, float anchorLeft = 0)
	{
		control.AnchorLeft = anchorLeft;
		control.AnchorTop = anchorTop;
		control.AnchorRight = anchorRight;
		control.AnchorBottom = anchorBottom;
		control.OffsetLeft = left;
		control.OffsetTop = top;
		control.OffsetRight = right;
		control.OffsetBottom = bottom;
	}

	private void BuildInterface()
	{
		var uiFont = new SystemFont { FontNames = new[] { "Tahoma", "Arial" } };
		var consoleFont = new SystemFont { FontNames = new[] { "Lucida Console", "Courier New", "monospace" } };
		var theme = new Theme { DefaultFont = uiFont, DefaultFontSize = 14 };
		theme.SetColor("font_color", "Label", TextColor);
		foreach (var state in new[] { "normal", "hover", "pressed", "focus" })
			theme.SetStylebox(state, "Button", Box(state == "hover" ? new("aaaaaa60") : new("00000020")));
		theme.SetColor("font_color", "Button", Colors.White);
		theme.SetColor("font_hover_color", "Button", Colors.White);
		theme.SetStylebox("normal", "LineEdit", Box(new("00000080"), true));
		theme.SetStylebox("focus", "LineEdit", Box(new("00000080"), true));
		theme.SetFont("font", "LineEdit", consoleFont);
		theme.SetFontSize("font_size", "LineEdit", 12);
		theme.SetColor("font_color", "LineEdit", TextColor);
		theme.SetColor("caret_color", "LineEdit", Colors.White);
		theme.SetColor("selection_color", "LineEdit", Orange);
		theme.SetColor("font_selected_color", "LineEdit", Colors.Black);
		theme.SetStylebox("scroll", "VScrollBar", Box(new("00000040"), true));
		foreach (var state in new[] { "grabber", "grabber_highlight", "grabber_pressed" })
			theme.SetStylebox(state, "VScrollBar", Box(new("a0a0a080")));

		_root = new Control { Name = "Modal", Theme = theme, MouseFilter = Control.MouseFilterEnum.Stop };
		AddChild(_root);
		_root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_frame = new Panel { Name = "ConsoleWindow", Position = new(28, 28), Size = new(720, 480) };
		_frame.AddThemeStyleboxOverride("panel", Box(new("a0a0a080")));
		_root.AddChild(_frame);
		_root.Resized += ClampFrame;

		var title = new Label { Text = "Console", MouseFilter = Control.MouseFilterEnum.Stop };
		_frame.AddChild(title);
		Place(title, 12, 2, -32, 28, 1);
		title.AddThemeColorOverride("font_color", Colors.White);
		title.GuiInput += @event =>
		{
			if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) return;
			_dragging = true;
			_pointerStart = _root.GetLocalMousePosition();
			_frameStart = _frame.Position;
		};
		var close = new Button { Name = "Close", TooltipText = "Close", FocusMode = Control.FocusModeEnum.None };
		_frame.AddChild(close);
		Place(close, -27, 5, -6, 25, 1, anchorLeft: 1);
		close.Draw += () =>
		{
			var center = close.Size * 0.5f;
			close.DrawLine(center - new Vector2(3, 3), center + new Vector2(3, 3), Colors.White);
			close.DrawLine(center + new Vector2(-3, 3), center + new Vector2(3, -3), Colors.White);
		};
		close.Pressed += () => SetOpen(false);

		_output = new RichTextLabel { Name = "Output", SelectionEnabled = true, ScrollFollowing = true,
			BbcodeEnabled = false, ContextMenuEnabled = true, FocusMode = Control.FocusModeEnum.Click };
		_frame.AddChild(_output);
		Place(_output, 8, 32, -8, -42, 1, 1);
		_output.AddThemeStyleboxOverride("normal", Box(new("00000080"), true));
		_output.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
		_output.AddThemeFontOverride("normal_font", consoleFont);
		_output.AddThemeFontSizeOverride("normal_font_size", 12);
		_output.AddThemeConstantOverride("line_separation", 0);
		_output.AddThemeColorOverride("selection_color", Orange);
		_output.AddThemeColorOverride("font_selected_color", Colors.Black);

		_entry = new LineEdit { Name = "Command", MaxLength = 1024, KeepEditingOnTextSubmit = true };
		_frame.AddChild(_entry);
		Place(_entry, 8, -34, -100, -10, 1, 1, 1);
		_entry.TextSubmitted += _ => Submit();
		_entry.TextChanged += _ => UpdateSuggestion();
		var submit = new Button { Name = "Submit", Text = "Submit", FocusMode = Control.FocusModeEnum.None };
		_frame.AddChild(submit);
		Place(submit, -92, -34, -22, -10, 1, 1, 1, 1);
		submit.Pressed += Submit;

		_suggestions = new ItemList { Name = "Completions", Visible = false,
			FocusMode = Control.FocusModeEnum.None, ZIndex = 1 };
		_root.AddChild(_suggestions);
		_suggestions.AddThemeStyleboxOverride("panel", Box(new("303030")));
		_suggestions.AddThemeStyleboxOverride("selected", Box(new("805000")));
		_suggestions.AddThemeStyleboxOverride("selected_focus", Box(new("805000")));
		_suggestions.AddThemeColorOverride("font_color", TextColor);
		_suggestions.AddThemeFontOverride("font", consoleFont);
		_suggestions.AddThemeFontSizeOverride("font_size", 12);
		_suggestions.FixedIconSize = Vector2I.Zero;
		_suggestions.AddThemeConstantOverride("v_separation", 8);
		_suggestions.ItemClicked += (index, _, button) =>
		{
			if (button == (long)MouseButton.Left) Complete((int)index);
		};

		var grip = new Control { Name = "ResizeGrip", MouseFilter = Control.MouseFilterEnum.Stop,
			MouseDefaultCursorShape = Control.CursorShape.Fdiagsize };
		_frame.AddChild(grip);
		Place(grip, -14, -14, -2, -2, 1, 1, 1, 1);
		grip.Draw += () =>
		{
			for (var length = 3; length <= 9; length += 3)
				grip.DrawLine(grip.Size - new Vector2(length, 2), grip.Size - new Vector2(2, length), TextColor);
		};
		grip.GuiInput += @event =>
		{
			if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) return;
			_resizing = true;
			_pointerStart = _root.GetLocalMousePosition();
			_frameStart = _frame.Size;
		};
	}
}
