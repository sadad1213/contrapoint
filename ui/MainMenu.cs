using Godot;
using System;
using System.Linq;
using Contrapoint.Maps;
using Contrapoint.Players;
using Contrapoint.Gameplay;

namespace Contrapoint.UI;

// CSS main screen with the CS:SO create-game dialog; see MainMenu.Setup.cs.
public partial class MainMenu : CanvasLayer
{
	private const string Assets = "res://assets/ui/css/";
	private static readonly Color Orange = new("ff9b00");
	private static readonly MapCatalog.Entry[] Maps = MapCatalog.Discover();
	private static readonly string[] MapIds = Maps.Select(map => map.Id).ToArray();
	private static readonly string[] MapScenes = Maps.Select(map => map.ScenePath).ToArray();
	private Control _root = null!;
	private Control _menu = null!;
	private Control _titles = null!;
	private Control _modal = null!;
	private Panel _dialog = null!;
	private Panel _loading = null!;
	private Label _loadingText = null!;
	private Label _error = null!;
	private ProgressBar _progress = null!;
	private TextureRect _background = null!;
	private Texture2D _standardBackground = null!;
	private Texture2D _wideBackground = null!;
	private Button _play = null!;
	private Button _start = null!;
	private MatchSetup? _pendingSetup;
	private AudioStreamPlayer _rollover = null!;
	private AudioStreamPlayer _click = null!;
	private Player? _player;
	private DeveloperConsole? _console;
	private bool _pausedByMenu;
	private string? _pendingScene;
	private bool _changingScene;
	private bool _dragging;
	private Vector2 _dragOffset;
	public bool IsOpen => Visible;
	public bool IsSelectingMap => _modal.Visible && _dialog.Visible;
	public int SelectedMap { get; private set; }

	public override void _Ready()
	{
		Layer = 90;
		ProcessMode = ProcessModeEnum.Always;
		_player = GetParent() as Player;
		BuildInterface();
		_root.Resized += Layout;
		Layout();
		if (_player != null) Visible = false;
		else
		{
			_console = new DeveloperConsole { Name = "DeveloperConsole" };
			AddChild(_console);
			Input.MouseMode = Input.MouseModeEnum.Visible;
			_play.GrabFocus();
		}
	}

	public override void _ExitTree()
	{
		if (_pausedByMenu) GetTree().Paused = false;
	}

	public void SetOpen(bool open)
	{
		if (_player == null || open == IsOpen || _pendingScene != null || _changingScene) return;
		Visible = open;
		if (open)
		{
			_player.BuyMenu?.Close();
			_player.SetMouseCaptured(false);
			GetTree().Paused = _pausedByMenu = Contrapoint.Network.LanSession.Instance?.IsActive != true;
			CloseMapSelection();
		}
		else
		{
			GetTree().Paused = _pausedByMenu = false;
			_player.SetMouseCaptured(true);
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (HandleOptionsInput(@event)) return;
		if (_player?.TeamMenu?.IsOpen == true) return;
		if (_player?.BuyMenu?.IsOpen == true) return;
		if (_console?.HandleInput(@event) == true) return;
		if (@event is InputEventKey { Pressed: true, Echo: false } key)
		{
			var code = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
			if (code == Key.Escape && _player?.Console?.IsOpen != true)
			{
				if (_pendingScene == null && !_changingScene)
				{
					if (IsOptionsOpen) CloseOptions();
					else if (IsSelectingMap) CloseMapSelection();
					else if (_player != null) SetOpen(!IsOpen);
				}
				GetViewport().SetInputAsHandled();
			}
			else if (IsOpen && IsSelectingMap && _setupPages[0].Visible
				&& GetViewport().GuiGetFocusOwner() is not LineEdit and not OptionButton
				&& (GetViewport().GuiGetFocusOwner() == _mapList || GetViewport().GuiGetFocusOwner() == _start)
				&& code is Key.Left or Key.Right)
			{
				SelectAdjacentMap(code == Key.Left ? -1 : 1);
				GetViewport().SetInputAsHandled();
			}
		}

		if (_dragging && @event is InputEventMouseMotion)
		{
			_dialog.Position = (_root.GetLocalMousePosition() - _dragOffset).Clamp(Vector2.Zero,
				(_root.Size - _dialog.Size * _dialog.Scale).Max(Vector2.Zero));
			GetViewport().SetInputAsHandled();
		}
		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }) _dragging = false;
	}

	public void OpenMapSelection()
	{
		if (_pendingScene != null || _changingScene) return;
		_options.Hide();
		_modal.Show();
		_dialog.Show();
		_loading.Hide();
		_error.Text = "";
		_menu.MouseFilter = Control.MouseFilterEnum.Ignore;
		SelectMap(SelectedMap);
		if (_start.Disabled) _mapList.GrabFocus(); else _start.GrabFocus();
		Layout();
	}

	public void CloseMapSelection()
	{
		_dragging = false;
		SaveSetup();
		_modal.Hide();
		_menu.MouseFilter = Control.MouseFilterEnum.Pass;
		_play.GrabFocus();
	}

	public void StartSelectedMap()
	{
		if (_pendingScene != null || _changingScene) return;
		if (!CanStartSelection()) return;
		var scene = MapScenes[SelectedMap];
		if (!ResourceLoader.Exists(scene)) { _error.Text = "Map is unavailable: " + MapIds[SelectedMap]; return; }
		var result = ResourceLoader.LoadThreadedRequest(scene, "PackedScene");
		if (result != Error.Ok) { _error.Text = "Unable to load map: " + result; return; }
		SaveSetup();
		_pendingSetup = MatchSession.Draft with { MapId = MapIds[SelectedMap] };
		_pendingScene = scene;
		_dialog.Hide();
		_loading.Show();
		_progress.Value = 0;
		_loadingText.Text = "Loading " + MapIds[SelectedMap] + "...";
	}

	public override void _Process(double delta)
	{
		ProcessOptions(delta);
		if (_pendingScene == null) return;
		var progress = new Godot.Collections.Array();
		var status = ResourceLoader.LoadThreadedGetStatus(_pendingScene, progress);
		if (progress.Count > 0) _progress.Value = progress[0].AsDouble() * 100;
		if (status == ResourceLoader.ThreadLoadStatus.Loaded)
		{
			var scene = ResourceLoader.LoadThreadedGet(_pendingScene) as PackedScene;
			_pendingScene = null;
			if (scene == null) { LoadingFailed(); return; }
			_changingScene = true;
			Contrapoint.Network.LanSession.Instance?.Stop();
			if (_pendingSetup != null) MatchSession.Begin(_pendingSetup);
			_pendingSetup = null;
			GetTree().Paused = _pausedByMenu = false;
			GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToPacked, scene);
		}
		else if (status is ResourceLoader.ThreadLoadStatus.Failed or ResourceLoader.ThreadLoadStatus.InvalidResource)
		{
			_pendingScene = null;
			LoadingFailed();
		}
	}

	private void LoadingFailed()
	{
		_pendingSetup = null;
		OpenMapSelection();
		_error.Text = "Unable to load " + MapIds[SelectedMap] + ". Please try again.";
	}

	private static StyleBoxFlat Bevel(Color fill, bool pressed = false)
	{
		var box = new StyleBoxFlat { BgColor = fill, BorderColor = new(pressed ? "c8c8c8c4" : "282828c4") };
		box.SetBorderWidthAll(1);
		// VGUI's raised and sunken borders have a bright top/left and dark bottom/right.
		box.BorderBlend = false;
		box.ContentMarginLeft = 6;
		box.ContentMarginRight = 6;
		return box;
	}

	private static void Rect(Control control, float x, float y, float w, float h)
	{
		control.Position = new(x, y);
		control.Size = new(w, h);
	}

	private Label Text(Control parent, string name, string text, float x, float y, float w, float h, int fontSize = 12, bool bold = false)
	{
		var label = new Label { Name = name, Text = text, MouseFilter = Control.MouseFilterEnum.Ignore,
			VerticalAlignment = VerticalAlignment.Center };
		parent.AddChild(label);
		Rect(label, x, y, w, h);
		label.AddThemeFontSizeOverride("font_size", fontSize);
		if (bold) label.AddThemeFontOverride("font", new SystemFont { FontNames = new[] { "Tahoma" }, FontWeight = 700 });
		return label;
	}

	private Button DialogButton(Control parent, string name, string text, float x, float y, float w, Action action)
	{
		var button = new Button { Name = name, Text = text, Alignment = HorizontalAlignment.Left };
		parent.AddChild(button);
		Rect(button, x, y, w, 24);
		button.Draw += () =>
		{
			var pressed = button.IsPressed();
			var bright = new Color("c8c8c8c4");
			var dark = new Color("282828c4");
			button.DrawLine(Vector2.Zero, new(button.Size.X, 0), pressed ? dark : bright);
			button.DrawLine(Vector2.Zero, new(0, button.Size.Y), pressed ? dark : bright);
		};
		WireSound(button);
		button.Pressed += action;
		return button;
	}

	private void WireSound(Button button)
	{
		button.MouseEntered += () => { if (!button.Disabled) _rollover.Play(); };
		button.ButtonDown += () => _click.Play();
	}

	private void BuildInterface()
	{
		_rollover = new AudioStreamPlayer { Stream = GD.Load<AudioStream>(Assets + "buttonrollover.wav"), VolumeDb = -8 };
		_click = new AudioStreamPlayer { Stream = GD.Load<AudioStream>(Assets + "buttonclickrelease.wav"), VolumeDb = -8 };
		AddChild(_rollover);
		AddChild(_click);
		var theme = new Theme { DefaultFont = new SystemFont { FontNames = new[] { "Tahoma", "Arial" } }, DefaultFontSize = 12 };
		theme.SetColor("font_color", "Label", Colors.White);
		foreach (var state in new[] { "normal", "hover", "pressed", "disabled" })
		{
			theme.SetStylebox(state, "Button", Bevel(state == "hover" ? new("ffffff15") : Colors.Transparent, state == "pressed"));
			theme.SetColor("font_" + (state == "normal" ? "" : state + "_") + "color", "Button", state == "disabled" ? new("808080") : Colors.White);
		}
		var focus = new StyleBoxFlat { BgColor = Colors.Transparent, BorderColor = new("000000c4") };
		focus.SetBorderWidthAll(1);
		focus.ExpandMarginLeft = focus.ExpandMarginRight = focus.ExpandMarginTop = focus.ExpandMarginBottom = 1;
		theme.SetStylebox("focus", "Button", focus);
		_root = new Control { Name = "Interface", Theme = theme, MouseFilter = Control.MouseFilterEnum.Stop };
		AddChild(_root);
		_root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_standardBackground = GD.Load<Texture2D>(Assets + "background.png");
		_wideBackground = GD.Load<Texture2D>(Assets + "background_widescreen.png");
		_background = new TextureRect { Name = "Background", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = _player == null };
		_root.AddChild(_background);
		_background.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		var inGameShade = new ColorRect { Name = "GameBackdrop", Color = new("0000009c"),
			MouseFilter = Control.MouseFilterEnum.Ignore, Visible = _player != null };
		_root.AddChild(inGameShade);
		inGameShade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		_titles = new Control { Name = "Titles", MouseFilter = Control.MouseFilterEnum.Ignore };
		_root.AddChild(_titles);
		var logo = GD.Load<Font>(Assets + "cslogo.ttf");
		var title = Text(_titles, "CounterStrike", "COUNTER-STRIKE'", 32, 180, 520, 64, 60);
		title.AddThemeFontOverride("font", logo);
		var subtitle = Text(_titles, "Source", "source", 380, 205, 180, 64, 60);
		subtitle.AddThemeFontOverride("font", logo);
		subtitle.Modulate = new(1, 1, 1, 80 / 255f);
		_menu = new Control { Name = "MainButtons", MouseFilter = Control.MouseFilterEnum.Pass };
		_root.AddChild(_menu);
		for (var i = 0; i < 3; i++)
		{
			var titleText = new[] { "Play", "Options", "Quit" }[i];
			var button = new Button { Name = titleText, Text = titleText,
				Alignment = HorizontalAlignment.Left };
			_menu.AddChild(button);
			Rect(button, 0, i * 22, 120, 22);
			button.AddThemeFontOverride("font", new SystemFont { FontNames = new[] { "Verdana" }, FontWeight = 600 });
			foreach (var state in new[] { "normal", "hover", "pressed", "focus" })
				button.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
			button.AddThemeColorOverride("font_hover_color", new("c8c8c8"));
			button.AddThemeColorOverride("font_pressed_color", new("c0ba50"));
			button.AddThemeColorOverride("font_focus_color", Colors.White);
			WireSound(button);
			if (i == 0) { _play = button; button.Pressed += OpenMapSelection; }
			else if (i == 1) button.Pressed += OpenOptions;
			else button.Pressed += () => GetTree().Quit();
		}

		_modal = new Control { Name = "Modal", Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
		_root.AddChild(_modal);
		_modal.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		var shade = new ColorRect { Name = "Backdrop", Color = new("0000009c"), MouseFilter = Control.MouseFilterEnum.Stop };
		_modal.AddChild(shade);
		shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		BuildMatchSetup();

		_loading = new Panel { Name = "Loading", Size = new(360, 112), Visible = false };
		_loading.AddThemeStyleboxOverride("panel", Bevel(new("a0a0a080")));
		_modal.AddChild(_loading);
		Text(_loading, "Title", "Loading", 16, 6, 320, 24, 18);
		_loadingText = Text(_loading, "Status", "", 16, 38, 328, 22);
		_progress = new ProgressBar { Name = "Progress", ShowPercentage = false };
		_loading.AddChild(_progress);
		Rect(_progress, 16, 72, 328, 16);
		_progress.AddThemeStyleboxOverride("background", Bevel(new("00000080"), true));
		_progress.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = Orange });
		foreach (var frame in new[] { _dialog, _loading })
			frame.Draw += () =>
			{
				frame.DrawLine(Vector2.Zero, new(frame.Size.X, 0), new("c8c8c8c4"));
				frame.DrawLine(Vector2.Zero, new(0, frame.Size.Y), new("c8c8c8c4"));
			};
		BuildOptions();
		RestoreSetup();
	}

	private void Layout()
	{
		if (_dialog == null) return;
		var size = _root.Size;
		var menuScale = size.Y / 480f;
		_titles.Scale = _menu.Scale = Vector2.One * menuScale;
		_menu.Position = new(32 * menuScale, 248 * menuScale);
		_menu.Size = new(120, 66);
		_background.Texture = size.X / Mathf.Max(1, size.Y) >= 1.5999f ? _wideBackground : _standardBackground;
		// Desktop HL2 dialogs use fixed pixel dimensions; only shrink when the window cannot fit them.
		var dialogScale = Mathf.Min(1, Mathf.Min((size.X - 24) / _dialog.Size.X, (size.Y - 24) / _dialog.Size.Y));
		dialogScale = Mathf.Max(0.1f, dialogScale);
		_dialog.Scale = _loading.Scale = Vector2.One * dialogScale;
		_dialog.Position = (size - _dialog.Size * dialogScale) * 0.5f;
		_loading.Position = (size - _loading.Size * dialogScale) * 0.5f;
		LayoutOptions();
	}
}
