using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Contrapoint.Gameplay;
using Contrapoint.Network;
using Contrapoint.Players;
using Contrapoint.Weapons;

namespace Contrapoint.UI;

// Stock BuyMenu_*.res and classes/*.res, in Source's 640 x 480 coordinates.
public partial class CssBuyMenu : CanvasLayer
{
	private const string Assets = "res://assets/ui/css/buy/";
	private static readonly Color Orange = new("ffb000"), Dull = new(1, 176 / 255f, 0, .5f);
	private Player _player = null!;
	private JsonElement _data;
	private Control _root = null!, _page = null!, _info = null!, _loadout = null!;
	private Label _message = null!;
	private AudioStreamPlayer _hover = null!, _click = null!;
	private readonly List<(Button Button, string Command, bool Supported)> _buttons = new();
	private readonly Dictionary<int, string[]> _favorites = new();
	private readonly Dictionary<int, string> _favoriteNames = new();
	private string _favoriteSide = "";
	private int _confirmSlot;
	private bool _skipSaveConfirmation;
	private CheckBox? _dontAsk;
	private string _category = "", _preview = "";
	private int _inventoryRevision = -1;
	private bool _pending;
	public bool IsOpen => Visible;
	public string Category => _category;
	public string PreviewItem => _preview;
	private string PresetPath => OS.GetCmdlineUserArgs().Contains("--menu-test-profile") ? "user://buy_presets_test.cfg" : "user://buy_presets.cfg";
	private static string S(JsonElement data, string key) => data.TryGetProperty(key, out var value) ? value.GetString() ?? "" : "";
	private static float N(JsonElement data, string key) => float.TryParse(S(data, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0;
	private string Localize(string token) => token.StartsWith('#') && _data.GetProperty("strings").GetProperty("english")
		.TryGetProperty(token[1..].ToLowerInvariant(), out var value) ? value.GetString()!.Replace("&", "") : token;

	public void Bind(Player player)
	{
		_player = player; Layer = 79; Visible = false;
		using var document = JsonDocument.Parse(FileAccess.GetFileAsString(Assets + "menus.json"));
		_data = document.RootElement.Clone();
		_root = new Control { Name = "Root", MouseFilter = Control.MouseFilterEnum.Stop };
		AddChild(_root); _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_root.Resized += Layout;
		_hover = new AudioStreamPlayer { Stream = GD.Load<AudioStream>("res://assets/ui/css/buttonrollover.wav") };
		_click = new AudioStreamPlayer { Stream = GD.Load<AudioStream>("res://assets/ui/css/buttonclick.wav") };
		AddChild(_hover); AddChild(_click);
	}
	private void LoadFavorites()
	{
		var side = _player.Team == PlayerTeam.CounterTerrorist ? "ct" : "ter";
		if (_favoriteSide == side) return;
		_favoriteSide = side;
		var config = new ConfigFile(); config.Load(PresetPath);
		_skipSaveConfirmation = config.GetValue("settings", "dont_ask", false).AsBool();
		var section = "favorites_" + side;
		var primary = new[] { side == "ct" ? "m4a1" : "ak47", "awp", "xm1014", "p90" };
		var names = new[] { "RIFLE", "SNIPER", "SHOTGUN", "SMG" };
		for (var i = 1; i <= 4; i++)
		{
			// Stock cstrike/cfg/buypresets_{ct,ter}.vdf, version 4.
			var defaults = primary[i - 1] + " " + (side == "ct" ? "usp" : "glock") + " vesthelm hegrenade";
			var value = config.GetValue(section, i.ToString(), config.GetValue("favorites", i.ToString(), defaults)).AsString();
			_favorites[i] = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(id => BuyCatalog.Find(id) != null).ToArray();
			_favoriteNames[i] = config.GetValue(section, i + "_name", names[i - 1]).AsString();
		}
	}
	public void Open(string category = "")
	{
		if (!BuyCatalog.CanOpen(_player) || _player.TeamMenu?.IsOpen == true) return;
		LoadFavorites();
		_player.Menu?.SetOpen(false);
		Visible = true; _pending = false;
		_player.SetMouseCaptured(false);
		Build(category);
	}
	public void Close()
	{
		if (!IsOpen) return;
		Visible = false; _pending = false;
		_player.SetMouseCaptured(true);
	}
	public override void _Process(double delta)
	{
		if (!IsOpen) return;
		if (!BuyCatalog.CanOpen(_player)) { Close(); return; }
		RefreshButtons();
		if (_category == "" && _inventoryRevision != _player.Inventory.NetworkRevision) DrawLoadout();
	}
	public bool HandleInput(InputEvent input)
	{
		if (!IsOpen) return false;
		if (input is InputEventKey { Pressed: true, Echo: false } key)
		{
			var code = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
			var bound = ConsoleBindings.EventKey(input);
			if (_confirmSlot != 0)
			{
				if (code == Key.Escape) Build("");
				else if (code is Key.Enter or Key.KpEnter) ConfirmFavorite();
				GetViewport().SetInputAsHandled(); return true;
			}
			if (bound != null && ConsoleBindings.Keys.TryGetValue(bound, out var buyCommand) && buyCommand is "autobuy" or "rebuy")
			{
				Activate(buyCommand); GetViewport().SetInputAsHandled(); return true;
			}
			if (code == Key.Escape || bound != null && ConsoleBindings.Keys.TryGetValue(bound, out var command) && command.Equals("buymenu", StringComparison.OrdinalIgnoreCase)) Close();
			else if (code is Key.Backspace or Key.Left) { if (_category != "") Build(""); }
			else if (code is Key.Up or Key.Down or Key.Tab)
			{
				var available = _buttons.Where(b => !b.Button.Disabled && b.Button.Visible).Select(b => b.Button).ToArray();
				var index = Array.IndexOf(available, GetViewport().GuiGetFocusOwner());
				if (available.Length > 0) available[Mathf.PosMod(index + (code == Key.Up ? -1 : 1), available.Length)].GrabFocus();
			}
			else if (code is Key.Enter or Key.KpEnter && GetViewport().GuiGetFocusOwner() is Button focused && !focused.Disabled)
				focused.EmitSignal(BaseButton.SignalName.Pressed);
			else
			{
				var hotkey = code is >= Key.Key0 and <= Key.Key9 ? ((int)(code - Key.Key0)).ToString()
					: code is >= Key.Kp0 and <= Key.Kp9 ? ((int)(code - Key.Kp0)).ToString() : OS.GetKeycodeString(code);
				var match = _buttons.FirstOrDefault(b => !b.Button.Disabled && b.Button.Visible && b.Button.Text.StartsWith(hotkey + " ", StringComparison.OrdinalIgnoreCase));
				match.Button?.EmitSignal(BaseButton.SignalName.Pressed);
			}
			GetViewport().SetInputAsHandled();
		}
		return true; // Mouse still reaches the GUI; gameplay receives none of it.
	}
	public void ReceiveResult(string error)
	{
		_pending = false;
		if (!IsOpen) return;
		if (error.Length == 0) Close();
		else { _message.Text = error; RefreshButtons(); }
	}
	private void Activate(string command)
	{
		_click.Play();
		if (command == "vguicancel") { Close(); return; }
		if (command.StartsWith("category ")) { Build(command[9..]); return; }
		if (command == "favorite_confirm") { ConfirmFavorite(); return; }
		if (command == "favorite_cancel") { Build(""); return; }
		if (command.StartsWith("favorite_set ")) { QuerySaveFavorite(int.Parse(command[13..])); return; }
		if (command.StartsWith("favorite "))
		{ _player.BuyPackage(_favorites.GetValueOrDefault(int.Parse(command[9..])) ?? Array.Empty<string>()); Close(); return; }
		if (command is "autobuy" or "rebuy")
		{
			if (command == "rebuy") _player.Rebuy(); else _player.AutoBuy();
			Close(); return;
		}
		if (!command.StartsWith("buy ") || _pending) return;
		_pending = LanSession.Instance?.IsClient == true;
		var error = _player.Buy(command[4..]);
		if (!_pending) ReceiveResult(error);
		else RefreshButtons();
	}
	private void Build(string category)
	{
		_category = category; _preview = "";
		_confirmSlot = 0; _dontAsk = null;
		if (_page != null) { _root.RemoveChild(_page); _page.QueueFree(); }
		_buttons.Clear();
		_page = new Control { Name = "BuyPage", Size = new(640, 480) }; _root.AddChild(_page);
		Background();
		var side = _player.Team == PlayerTeam.CounterTerrorist ? "ct" : "ter";
		var layoutName = category == "" ? "buymenu_" + side : "buy" + category + "_" + side;
		var layouts = _data.GetProperty("layouts");
		var layout = layouts.TryGetProperty(layoutName, out var sideLayout) ? sideLayout : layouts.GetProperty("buy" + category);
		foreach (var entry in layout.EnumerateObject())
		{
			var fields = entry.Value; var type = S(fields, "controlname");
			if (S(fields, "visible") == "0" && !entry.Name.StartsWith("buypresetbutton")) continue;
			Control control;
			if (type is "MouseOverPanelButton" or "Button")
			{
				var command = S(fields, "command"); var text = Localize(S(fields, "labeltext"));
				var supported = true;
				if (command.StartsWith("Resource/", StringComparison.OrdinalIgnoreCase))
				{
					supported = entry.Name is "pistols" or "rifles" or "equipment" or "shotguns" or "submachineguns" or "machineguns";
					command = "category " + entry.Name;
				}
				else if (command.StartsWith("buy ")) supported = BuyCatalog.Find(command[4..]) != null;
				else if (command.StartsWith("cl_buy_favorite_query_set ")) command = "favorite_set " + command.Split(' ')[1];
				else if (command.StartsWith("cl_buy_favorite "))
				{
					var slot = int.Parse(command.Split(' ')[1]); command = "favorite " + slot;
					text = "SDFG"[slot - 1] + " " + FavoriteName(slot);
				}
				else supported = command is "vguicancel" or "autobuy" or "rebuy";
				if (command is "autobuy" or "rebuy")
				{
					var key = ConsoleBindings.Keys.FirstOrDefault(b => b.Value == command && b.Key.StartsWith('F')).Key;
					if (key != null) text += " (" + key + ")";
				}
				var button = MakeButton(text, command, supported);
				if (command.StartsWith("favorite_set ")) button.AddThemeFontSizeOverride("font_size", 10);
				if (command.StartsWith("favorite "))
				{
					var slot = int.Parse(command[9..]);
					button.TooltipText = string.Join(", ", _favorites[slot].Select(id => id.ToUpperInvariant()));
				}
				if (command.StartsWith("buy "))
				{
					var id = command[4..];
					button.MouseEntered += () => ShowInfo(id);
					button.FocusEntered += () => ShowInfo(id);
				}
				control = button;
			}
			else if (type == "Label") control = MakeLabel(Localize(S(fields, "labeltext")), S(fields, "font") == "MenuTitle");
			else continue;
			control.Name = entry.Name;
			control.Position = new(N(fields, "xpos"), N(fields, "ypos"));
			control.Size = new(N(fields, "wide"), N(fields, "tall"));
			// Some stock .res rows use xpos=0 (TER USP, kevlar+helmet, etc.).
			// Align the item column consistently instead of displaying those raw offsets.
			if (category != "" && control is Button) control.Position = new(52, control.Position.Y);
			_page.AddChild(control);
		}
		_info = new Control { Name = "ItemInfo", Position = new(244, 116), Size = new(360, 300), MouseFilter = Control.MouseFilterEnum.Ignore };
		_page.AddChild(_info);
		_loadout = new Control { Name = "Loadout", Position = new(272, 102), Size = new(284, 112), MouseFilter = Control.MouseFilterEnum.Ignore };
		_page.AddChild(_loadout);
		_message = MakeLabel(""); _message.Position = new(244, 420); _message.Size = new(352, 24); _page.AddChild(_message);
		RefreshButtons();
		if (category == "") DrawLoadout();
		else
		{
			var first = _buttons.FirstOrDefault(b => b.Supported && b.Command.StartsWith("buy "));
			if (first.Button != null) ShowInfo(first.Command[4..]);
		}
		Layout();
	}
	private Button MakeButton(string text, string command, bool supported)
	{
		var button = new Button { Text = text, Alignment = HorizontalAlignment.Left, ClipText = true, MouseDefaultCursorShape = Control.CursorShape.Arrow };
		button.AddThemeFontOverride("font", GD.Load<FontFile>("res://assets/ui/css/team/verdana.ttf"));
		button.AddThemeFontSizeOverride("font_size", 12);
		foreach (var state in new[] { "normal", "hover", "pressed", "focus", "hover_pressed", "disabled" })
			button.AddThemeStyleboxOverride(state, new StyleBoxFlat { BgColor = state is "normal" or "disabled" ? new(0, 0, 0, 64 / 255f) : new(192 / 255f, 28 / 255f, 0, 140 / 255f), ContentMarginLeft = 6, ContentMarginRight = 3, ContentMarginTop = 0, ContentMarginBottom = 0 });
		foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color" }) button.AddThemeColorOverride(state, Orange);
		button.AddThemeColorOverride("font_disabled_color", Dull);
		button.Pressed += () => Activate(command);
		button.MouseEntered += () => { if (!button.Disabled) { _hover.Play(); button.GrabFocus(); } };
		_buttons.Add((button, command, supported));
		return button;
	}
	private Label MakeLabel(string text, bool title = false, bool small = false)
	{
		var label = new Label { Text = text, ClipText = true, MouseFilter = Control.MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
		label.AddThemeFontOverride("font", GD.Load<FontFile>(title ? "res://assets/ui/css/death/verdanab.ttf" : "res://assets/ui/css/team/verdana.ttf"));
		label.AddThemeFontSizeOverride("font_size", title ? 15 : small ? 10 : 12);
		label.AddThemeColorOverride("font_color", Orange);
		return label;
	}
	private void RefreshButtons()
	{
		foreach (var (button, command, supported) in _buttons)
		{
			// Theme inheritance can retain the default button height for its first frame.
			if (button.Size.Y != 20) button.Size = new(button.Size.X, 20);
			var item = command.StartsWith("buy ") ? BuyCatalog.Find(command[4..]) : null;
			button.Disabled = !supported || _pending || item != null && BuyCatalog.Unavailable(_player, item) != null
				|| command.StartsWith("favorite ") && (_favorites.GetValueOrDefault(int.Parse(command[9..]))?.Length ?? 0) == 0;
		}
	}
	private void ShowInfo(string id)
	{
		if (_info == null || BuyCatalog.Find(id) is not { } item) return;
		_preview = id;
		foreach (var child in _info.GetChildren()) { _info.RemoveChild(child); child.QueueFree(); }
		foreach (var entry in _data.GetProperty("cards").GetProperty(item.Card).EnumerateObject())
		{
			var fields = entry.Value;
			if (S(fields, "visible") == "0") continue;
			Control control;
			if (S(fields, "controlname") == "Label")
			{
				var text = Localize(S(fields, "labeltext"));
				if (entry.Name == "price") text = Regex.Replace(text, @"\d[\d,]*", BuyCatalog.Price(_player, item).ToString(CultureInfo.InvariantCulture));
				var label = MakeLabel(text, small: S(fields, "font") == "DefaultSmall");
				if (entry.Name == "description") { label.AutowrapMode = TextServer.AutowrapMode.WordSmart; label.VerticalAlignment = VerticalAlignment.Top; }
				control = label;
			}
			else if (entry.Name == "classimage") control = Image(_info, Assets + item.Card + ".png", new(N(fields, "xpos"), N(fields, "ypos")), new(N(fields, "wide"), N(fields, "tall")));
			else if (entry.Name == "imagebg") control = new ColorRect { Color = new(0, 0, 0, 64 / 255f), MouseFilter = Control.MouseFilterEnum.Ignore };
			else continue;
			control.Name = entry.Name; control.Position = new(N(fields, "xpos"), N(fields, "ypos")); control.Size = new(N(fields, "wide"), N(fields, "tall"));
			if (control.GetParent() == null) _info.AddChild(control);
		}
	}
	private TextureRect Image(Control parent, string path, Vector2 position, Vector2 size)
	{
		var image = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, Texture = GD.Load<Texture2D>(path), Position = position, Size = size, MouseFilter = Control.MouseFilterEnum.Ignore };
		parent.AddChild(image); return image;
	}
	private void Background()
	{
		var shared = "res://assets/ui/css/team/";
		foreach (var (corner, position) in new[] { ("nw", new Vector2(20, 20)), ("ne", new Vector2(610, 20)), ("sw", new Vector2(20, 450)), ("se", new Vector2(610, 450)) })
			Image(_page, shared + "round_corner_" + corner + ".png", position, new(10, 10)).Modulate = new(1, 1, 1, 196 / 255f);
		foreach (var (position, size) in new[] { (new Vector2(30, 20), new Vector2(580, 10)), (new Vector2(20, 30), new Vector2(600, 42)), (new Vector2(20, 74), new Vector2(600, 376)), (new Vector2(30, 450), new Vector2(580, 10)) })
			Image(_page, shared + "solid_background.png", position, size).Modulate = new(1, 1, 1, 196 / 255f);
	}
	private void Layout()
	{
		if (_page == null) return;
		var scale = Math.Min(_root.Size.Y / 480, _root.Size.X / 640);
		_page.Scale = Vector2.One * scale; _page.Position = (_root.Size - new Vector2(640, 480) * scale) / 2;
	}
	private void DrawLoadout()
	{
		_inventoryRevision = _player.Inventory.NetworkRevision;
		foreach (var child in _loadout.GetChildren()) { _loadout.RemoveChild(child); child.QueueFree(); }
		var y = 0f;
		foreach (var slot in new[] { WeaponSlot.Primary, WeaponSlot.Secondary })
		{
			var weapon = _player.Inventory.Weapons.FirstOrDefault(w => w.Slot == slot);
			var item = weapon == null ? null : BuyCatalog.Find(WeaponPickup.ClassOf(weapon)[7..]);
			if (item != null)
				Image(_loadout, Assets + item.Card + ".png", new(14, y), new(256, 56)).StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
			y += 56;
		}
	}
	private string FavoriteName(int slot) => (_favorites.GetValueOrDefault(slot)?.Length ?? 0) > 0 ? _favoriteNames[slot] : Localize("#Cstrike_BuyPresetBlank");
	private void QuerySaveFavorite(int slot)
	{
		if (_skipSaveConfirmation) { SaveFavorite(slot); return; }
		_confirmSlot = slot;
		var shade = new ColorRect { Name = "SavePresetConfirmation", Size = new(640, 480), Color = new(0, 0, 0, .7f), MouseFilter = Control.MouseFilterEnum.Stop };
		_page.AddChild(shade);
		var panel = new ColorRect { Position = new(144, 162), Size = new(352, 156), Color = new(.12f, .12f, .12f), MouseFilter = Control.MouseFilterEnum.Stop }; shade.AddChild(panel);
		var title = MakeLabel(Localize("#Cstrike_BuyPresetSet"), title: true); title.Position = new(12, 6); title.Size = new(328, 24); panel.AddChild(title);
		var prompt = MakeLabel(Localize("#Cstrike_BuyPresetSetConfirm")); prompt.Position = new(12, 38); prompt.Size = new(328, 40); prompt.AutowrapMode = TextServer.AutowrapMode.WordSmart; panel.AddChild(prompt);
		_dontAsk = new CheckBox { Text = Localize("#Cstrike_BuyPresetSetDontAsk"), Position = new(10, 82), Size = new(330, 24) };
		_dontAsk.AddThemeFontOverride("font", GD.Load<FontFile>("res://assets/ui/css/team/verdana.ttf")); _dontAsk.AddThemeFontSizeOverride("font_size", 12); _dontAsk.AddThemeColorOverride("font_color", Orange); panel.AddChild(_dontAsk);
		foreach (var (text, command, x) in new[] { ("OK", "favorite_confirm", 158), (Localize("#Cstrike_BuyPresetExit"), "favorite_cancel", 254) })
		{
			var button = MakeButton(text, command, true); button.Position = new(x, 122); button.Size = new(86, 20); panel.AddChild(button);
		}
	}
	private void ConfirmFavorite()
	{
		_skipSaveConfirmation = _dontAsk?.ButtonPressed == true;
		SaveFavorite(_confirmSlot);
	}
	private void SaveFavorite(int slot)
	{
		_favorites[slot] = _player.CaptureBuyPackage();
		var config = new ConfigFile();
		config.Load(PresetPath);
		foreach (var (index, items) in _favorites)
		{
			config.SetValue("favorites_" + _favoriteSide, index.ToString(), string.Join(' ', items));
			config.SetValue("favorites_" + _favoriteSide, index + "_name", _favoriteNames[index]);
		}
		config.SetValue("settings", "dont_ask", _skipSaveConfirmation);
		var result = config.Save(PresetPath); Build("");
		if (result != Error.Ok) _message.Text = "Could not save favorites: " + result;
	}
}
