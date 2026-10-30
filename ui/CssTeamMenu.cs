using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Contrapoint.Players;

namespace Contrapoint.UI;

// TeamMenu.res/ClassMenu_*.res and CTeamMenu/CClassMenu/CCSTeamMenu.
public partial class CssTeamMenu : CanvasLayer
{
	private const string Assets = "res://assets/ui/css/team/";
	private Player _player = null!;
	private JsonElement _data;
	private Control _root = null!, _page = null!, _info = null!;
	private readonly List<Button> _buttons = new();
	private readonly Dictionary<string, Control> _controls = new();
	private SubViewport? _preview;
	private Node3D? _model;
	private AudioStreamPlayer _hover = null!, _click = null!;
	private bool _classes, _initial;
	public bool IsOpen => Visible;
	public bool IsClassSelection => IsOpen && _classes;
	public string PreviewAgent { get; private set; } = "";
	private static float N(JsonElement data, string key) => data.TryGetProperty(key, out var value)
		? float.Parse(value.GetString()!, CultureInfo.InvariantCulture) : 0;
	private static string S(JsonElement data, string key) => data.TryGetProperty(key, out var value) ? value.GetString() ?? "" : "";
	private string Localize(string token) => token.StartsWith('#') && _data.GetProperty("strings").GetProperty("english")
		.TryGetProperty(token[1..].ToLowerInvariant(), out var text) ? text.GetString()!.Replace("&", "") : token;
	private Color SchemeColor(string name, string fallback)
	{
		var settings = _data.GetProperty("settings"); var colors = _data.GetProperty("colors");
		var value = settings.TryGetProperty(name.ToLowerInvariant(), out var setting) ? setting.GetString()! : fallback;
		if (colors.TryGetProperty(value.ToLowerInvariant(), out var color)) value = color.GetString()!;
		var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(float.Parse).ToArray();
		return new(parts[0] / 255, parts[1] / 255, parts[2] / 255, parts[3] / 255);
	}

	public void Bind(Player player)
	{
		_player = player;
		Layer = 80;
		Visible = false;
		_data = JsonDocument.Parse(FileAccess.GetFileAsString(Assets + "menus.json")).RootElement.Clone();
		_root = new Control { Name = "Root", MouseFilter = Control.MouseFilterEnum.Stop };
		AddChild(_root); _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_root.Resized += Layout;
		_hover = new AudioStreamPlayer { Stream = GD.Load<AudioStream>("res://assets/ui/css/buttonrollover.wav") };
		_click = new AudioStreamPlayer { Stream = GD.Load<AudioStream>("res://assets/ui/css/buttonclick.wav") };
		AddChild(_hover); AddChild(_click);
		Build(false);
	}

	public void OpenInitial()
	{
		_initial = true;
		_player.BeginInitialTeamSelection();
		Open();
	}
	public void Open()
	{
		_player.BuyMenu?.Close();
		_player.Menu?.SetOpen(false);
		Visible = true;
		_player.SetMouseCaptured(false);
		Build(false);
	}
	public void ShowClasses()
	{
		Visible = true;
		_player.SetMouseCaptured(false);
		Build(true);
	}
	public void Close()
	{
		Visible = false;
		if (_preview != null) _preview.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
		if (_player.Team != PlayerTeam.Unassigned && !_player.PickingClass) _initial = false;
		_player.SetMouseCaptured(true);
	}
	public bool HandleInput(InputEvent input)
	{
		if (!IsOpen) return false;
		if (input is InputEventKey { Pressed: true, Echo: false } key)
		{
			var code = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
			if (code == Key.Escape) { if (CanCancel()) Cancel(); }
			else if (code is Key.Enter or Key.KpEnter)
			{
				if (_classes && GetViewport().GuiGetFocusOwner() is Button focused && _buttons.Contains(focused)) focused.EmitSignal(BaseButton.SignalName.Pressed);
				else Activate(_classes ? "joinclass 0" : "jointeam 0");
			}
			else if (code is >= Key.Key0 and <= Key.Key9 || code is >= Key.Kp0 and <= Key.Kp9)
			{
				var digit = code >= Key.Kp0 ? (int)(code - Key.Kp0) : (int)(code - Key.Key0);
				var button = _buttons.FirstOrDefault(b => b.Visible && b.Text.StartsWith(digit + " ", StringComparison.Ordinal));
				button?.EmitSignal(BaseButton.SignalName.Pressed);
			}
			else if (code is Key.Up or Key.Down or Key.Left or Key.Right or Key.Tab)
			{
				var visible = _buttons.Where(b => b.Visible).ToArray();
				var current = Array.IndexOf(visible, GetViewport().GuiGetFocusOwner());
				var direction = code is Key.Up or Key.Left ? -1 : 1;
				if (visible.Length > 0) visible[Mathf.PosMod(current + direction, visible.Length)].GrabFocus();
			}
			else return true;
			GetViewport().SetInputAsHandled();
		}
		return true;
	}
	private bool CanCancel() => !_initial && (!_classes || !_player.ClassSelectionRequired);
	private void Cancel() { if (_classes) _player.CancelClassSelection(); else Close(); }

	private void Activate(string command)
	{
		_click.Play();
		if (command == "vguicancel") { if (CanCancel()) Cancel(); return; }
		ConsoleCommands.Execute(command, _player.Movement, (line, error) => _player.Console?.Print(line, error));
	}
	private void Build(bool classes)
	{
		_classes = classes;
		if (_page != null) { _root.RemoveChild(_page); _page.QueueFree(); }
		_buttons.Clear(); _controls.Clear(); _preview = null; _model = null;
		_info = null!;
		_page = new Control { Name = classes ? "ClassPage" : "TeamPage", Size = new(640, 480) };
		_root.AddChild(_page);
		Background();
		var name = classes ? _player.Team == PlayerTeam.CounterTerrorist ? "classmenu_ct" : "classmenu_ter" : "teammenu";
		foreach (var item in _data.GetProperty("layouts").GetProperty(name).EnumerateObject())
		{
			var data = item.Value;
			var type = S(data, "controlname");
			Control control;
			if (type is "Button" or "MouseOverPanelButton")
			{
				var command = S(data, "command");
				var button = new Button { Text = Localize(S(data, "labeltext")), Alignment = HorizontalAlignment.Left,
					ClipText = true, MouseDefaultCursorShape = Control.CursorShape.Arrow };
				StyleButton(button);
				button.Pressed += () => Activate(command);
				button.MouseEntered += () => { _hover.Play(); button.GrabFocus(); if (classes) ShowClassInfo(item.Name); };
				button.FocusEntered += () => { if (classes) ShowClassInfo(item.Name); };
				_buttons.Add(button); control = button;
			}
			else if (type == "Label") control = Label(Localize(S(data, "labeltext")), S(data, "font") == "MenuTitle" ? 18 : 14, S(data, "font") == "MenuTitle");
			else if (type == "RichText")
			{
				var map = _player.GetNodeOrNull<Node>( _player.SpawnPointsRootPath)?.SceneFilePath.GetFile().GetBaseName() ?? "";
				var label = Label(_data.GetProperty("maps").TryGetProperty(map, out var description) ? description.GetString()! : "", 14);
				label.AutowrapMode = TextServer.AutowrapMode.WordSmart; label.VerticalAlignment = VerticalAlignment.Top;
				control = label;
			}
			else continue;
			control.Name = item.Name;
			control.Position = new(N(data, "xpos"), N(data, "ypos"));
			control.Size = new(N(data, "wide"), N(data, "tall"));
			control.Visible = S(data, "visible") != "0";
			if (item.Name == "vipbutton") control.Visible = false;
			if (item.Name == "specbutton") control.Visible = _player.CanSpectate;
			if (item.Name == "cancelbutton") control.Visible = CanCancel();
			_page.AddChild(control); _controls[item.Name] = control;
		}
		_info = new Control { Name = "ClassInfo", Position = new(244, 116), Size = new(400, 380), MouseFilter = Control.MouseFilterEnum.Ignore };
		_page.AddChild(_info);
		if (classes) ShowClassInfo(_player.Team == PlayerTeam.CounterTerrorist ? "urban" : "terror");
		var auto = classes ? _player.Team == PlayerTeam.CounterTerrorist ? "autoselect_ct" : "autoselect_t" : "autobutton";
		if (_controls.TryGetValue(auto, out var defaultButton)) defaultButton.GrabFocus();
		Layout();
	}

	private Label Label(string text, int height, bool bold = false)
	{
		var label = new Label { Text = text, ClipText = true, MouseFilter = Control.MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
		label.AddThemeFontOverride("font", GD.Load<FontFile>(bold ? "res://assets/ui/css/death/verdanab.ttf" : Assets + "verdana.ttf"));
		label.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(height * .84f));
		label.AddThemeColorOverride("font_color", SchemeColor("label.textcolor", "255 176 0 255"));
		return label;
	}
	private void StyleButton(Button button)
	{
		button.AddThemeFontOverride("font", GD.Load<FontFile>(Assets + "verdana.ttf"));
		button.AddThemeFontSizeOverride("font_size", 12);
		foreach (var state in new[] { "normal", "hover", "pressed", "focus", "hover_pressed" })
		{
			var armed = state != "normal";
			var style = new StyleBoxFlat { BgColor = SchemeColor(armed ? "button.armedbgcolor" : "button.bgcolor", "0 0 0 64"),
				ContentMarginLeft = 6, ContentMarginRight = 3, ContentMarginTop = 0, ContentMarginBottom = 0 };
			button.AddThemeStyleboxOverride(state, style);
		}
		foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color" })
			button.AddThemeColorOverride(state, SchemeColor("button.textcolor", "255 176 0 255"));
	}
	private TextureRect Image(Control parent, string file, Vector2 position, Vector2 size)
	{
		var image = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			Texture = GD.Load<Texture2D>(Assets + file + ".png"), Position = position, Size = size,
			MouseFilter = Control.MouseFilterEnum.Ignore };
		parent.AddChild(image); return image;
	}
	private void Background()
	{
		foreach (var (name, position) in new[] { ("nw", new Vector2(20, 20)), ("ne", new Vector2(610, 20)),
			("sw", new Vector2(20, 450)), ("se", new Vector2(610, 450)) })
			Image(_page, "round_corner_" + name, position, new(10, 10)).Modulate = new(1, 1, 1, 196 / 255f);
		foreach (var (position, size) in new[] { (new Vector2(30, 20), new Vector2(580, 10)),
			(new Vector2(20, 30), new Vector2(600, 42)), (new Vector2(20, 74), new Vector2(600, 376)),
			(new Vector2(30, 450), new Vector2(580, 10)) })
			Image(_page, "solid_background", position, size).Modulate = new(1, 1, 1, 196 / 255f);
		Image(_page, "cs_logo", new(25, 25), new(42, 42)).Modulate = new("ffb000");
	}
	private void Layout()
	{
		if (_page == null) return;
		var scale = Math.Min(_root.Size.Y / 480f, _root.Size.X / 640f);
		_page.Scale = Vector2.One * scale;
		_page.Position = (_root.Size - new Vector2(640, 480) * scale) / 2;
	}

	private void ShowClassInfo(string name)
	{
		if (_info == null || !_data.GetProperty("classes").TryGetProperty(name, out var definition)) return;
		foreach (var child in _info.GetChildren()) { _info.RemoveChild(child); child.QueueFree(); }
		_preview = null; _model = null;
		PreviewAgent = name == "terror" ? "phoenix" : name;
		foreach (var item in definition.EnumerateObject())
		{
			var data = item.Value;
			var type = S(data, "controlname");
			var position = new Vector2(N(data, "xpos"), N(data, "ypos"));
			var size = new Vector2(N(data, "wide"), N(data, "tall"));
			if (type == "Label")
			{
				var label = Label(Localize(S(data, "labeltext")), S(data, "font") == "DefaultSmall" ? 12 : 14);
				label.Position = position; label.Size = size; label.VerticalAlignment = VerticalAlignment.Top;
				label.AutowrapMode = TextServer.AutowrapMode.WordSmart; _info.AddChild(label);
			}
			else if (type == "ImagePanel" && S(data, "image") != "") Image(_info, S(data, "image").GetFile(), position, size);
			else if (type == "CSClassImagePanel") CreatePreview(position, size);
		}
	}
	private void CreatePreview(Vector2 position, Vector2 size)
	{
		var container = new SubViewportContainer { Position = position, Size = size, Stretch = true, MouseFilter = Control.MouseFilterEnum.Ignore };
		_info.AddChild(container);
		_preview = new SubViewport { Size = (Vector2I)(size * 2), OwnWorld3D = true, TransparentBg = true,
			RenderTargetUpdateMode = IsOpen ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled };
		container.AddChild(_preview);
		var actor = new CssClassPreview { RotationDegrees = new(0, 210, 0) };
		_model = actor; _preview.AddChild(actor); actor.Initialize(PreviewAgent, _player);
		// Source view.fov=54, view.origin=(-110,-5,-5)+bounds_center*.55.
		var camera = new Camera3D { Position = new(-5 * .0254f, .94f, -110 * .0254f), Fov = 54,
			KeepAspect = Camera3D.KeepAspectEnum.Width, Current = true };
		_preview.AddChild(camera); camera.RotationDegrees = new(0, 180, 0);
		var environment = new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.ClearColor,
			AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = .4f } };
		_preview.AddChild(environment);
	}
}
