using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Contrapoint.Gameplay;

namespace Contrapoint.UI;

public partial class MainMenu
{
	// Based on the installed CS:SO 2.0 resources, copied to references/csso.
	// The original 640x480 dialog is widened to keep a permanent map preview.
	private readonly Control[] _setupPages = new Control[3];
	private readonly Button[] _setupTabs = new Button[3];
	private readonly List<int> _visibleMaps = new();
	private readonly Dictionary<string, Texture2D?> _previews = new();
	private ItemList _mapList = null!;
	private OptionButton _gameType = null!, _gameMode = null!;
	private CheckBox _allMaps = null!;
	private TextureRect _mapPreview = null!;
	private Label _previewTitle = null!, _previewId = null!, _previewFallback = null!;
	private Label _modeDescription = null!, _modeStatus = null!, _mapCount = null!;
	private int _activeSetupTab;
	private string[] _typeChoices = [];
	private string[] _modeChoices = [];
	private GameModeCatalog.Mode SelectedMode => GameModeCatalog.Find(MatchSession.Draft.ModeId);
	public string SelectedModeId => SelectedMode.Id;
	public int VisibleMapCount => _visibleMaps.Count;
	public string SelectedMapId => SelectedMap >= 0 ? MapIds[SelectedMap] : "";
	private const string FutureMode = "This mode is not available yet";

	private static StyleBoxFlat SetupBox(string fill, string border = "ffffff24")
	{
		var box = new StyleBoxFlat { BgColor = new(fill), BorderColor = new(border) };
		box.SetBorderWidthAll(1);
		box.SetContentMarginAll(7);
		return box;
	}

	private static Texture2D VguiIcon(string drawing, int width, int height)
	{
		using var image = new Image();
		image.LoadSvgFromString($"<svg xmlns='http://www.w3.org/2000/svg' width='{width}' height='{height}'>{drawing}</svg>");
		return ImageTexture.CreateFromImage(image);
	}

	private void BuildMatchSetup()
	{
		// GameUI uses SourceScheme, not ClientScheme (the in-game HUD skin).
		var theme = new Theme { DefaultFont = _root.Theme.DefaultFont, DefaultFontSize = 12 };
		theme.SetColor("font_color", "Label", new("dddddd"));
		foreach (var type in new[] { "Button", "OptionButton" })
		{
			foreach (var state in new[] { "normal", "hover", "pressed", "disabled" })
			{
				theme.SetStylebox(state, type, Bevel(state == "hover" ? new("ffffff0c") : Colors.Transparent, state == "pressed"));
				theme.SetColor(state == "normal" ? "font_color" : "font_" + state + "_color", type,
					new(state == "disabled" ? "808080" : "ffffff"));
			}
			theme.SetColor("font_focus_color", type, Colors.White);
			theme.SetColor("font_hover_pressed_color", type, Colors.White);
			theme.SetStylebox("focus", type, SetupBox("00000000", "000000c4"));
		}
		foreach (var type in new[] { "LineEdit", "ItemList", "PopupMenu" })
		{
			theme.SetStylebox(type == "PopupMenu" ? "panel" : "normal", type,
				Bevel(new(type == "PopupMenu" ? "727272f5" : "00000080"), true));
			theme.SetStylebox("focus", type, SetupBox("00000000", "000000c4"));
			theme.SetColor("font_color", type, new("dddddd"));
			theme.SetColor("font_selected_color", type, Colors.Black);
		}
		var mapHighlight = SetupBox("ff9b0080", "00000000");
		var focusedMapHighlight = SetupBox("ff9b00", "00000000");
		foreach (var state in new[] { "selected", "hovered", "hovered_selected" })
			theme.SetStylebox(state, "ItemList", mapHighlight);
		foreach (var state in new[] { "selected_focus", "hovered_selected_focus" })
			theme.SetStylebox(state, "ItemList", focusedMapHighlight);
		theme.SetColor("font_hovered_color", "ItemList", Colors.Black);
		theme.SetColor("font_hovered_selected_color", "ItemList", Colors.Black);
		theme.SetStylebox("hovered", "PopupMenu", SetupBox("ff9b00", "00000000"));
		theme.SetColor("font_hover_color", "PopupMenu", Colors.Black);
		theme.SetConstant("v_separation", "ItemList", 4);
		// Check boxes have a bordered square, not a full-width button frame.
		foreach (var state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
			theme.SetStylebox(state, "CheckBox", new StyleBoxEmpty());
		theme.SetStylebox("focus", "CheckBox", new StyleBoxEmpty());
		foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color",
			"checkbox_checked_color", "checkbox_unchecked_color" })
			theme.SetColor(state, "CheckBox", Colors.White);
		const string square = "<path fill='#000' fill-opacity='.5' d='M0 0h12v12H0z'/><path fill='none' stroke='#282828' d='M.5 11V.5H11'/><path fill='none' stroke='#c8c8c8' d='M1 11.5h10.5V1'/>";
		var uncheckedIcon = VguiIcon(square, 12, 12);
		var checkedIcon = VguiIcon(square + "<path fill='none' stroke='#ddd' stroke-width='1.5' d='m2.5 6 2.5 2.5 4.5-5'/>", 12, 12);
		theme.SetIcon("unchecked", "CheckBox", uncheckedIcon);
		theme.SetIcon("checked", "CheckBox", checkedIcon);
		theme.SetIcon("unchecked_disabled", "CheckBox", uncheckedIcon);
		theme.SetIcon("checked_disabled", "CheckBox", checkedIcon);
		var knob = VguiIcon("<path fill='#6c6c6c' stroke='#282828' d='M.5.5h9v15h-9z'/><path fill='none' stroke='#c8c8c8' d='M1 15V1h8'/>", 10, 16);
		foreach (var icon in new[] { "grabber", "grabber_highlight", "grabber_disabled" }) theme.SetIcon(icon, "HSlider", knob);
		foreach (var state in new[] { "slider", "grabber_area", "grabber_area_highlight" })
			theme.SetStylebox(state, "HSlider", new StyleBoxFlat { BgColor = new("1f1f1f"), ContentMarginTop = 1, ContentMarginBottom = 1 });
		_dialog = new Panel { Name = "MapSelection", Size = new(900, 480), Theme = theme };
		_dialog.AddThemeStyleboxOverride("panel", Bevel(new("a0a0a080")));

		_modal.AddChild(_dialog);
		var caption = Text(_dialog, "Title", "Create Server", 16, 4, 840, 24, 18);
		caption.MouseFilter = Control.MouseFilterEnum.Stop;
		caption.GuiInput += @event =>
		{
			if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) return;
			_dragging = true;
			_dragOffset = _root.GetLocalMousePosition() - _dialog.Position;
		};
		var close = DialogButton(_dialog, "Close", "×", 872, 8, 18, CloseMapSelection);
		close.Alignment = HorizontalAlignment.Center;
		var names = new[] { "Server", "Gameplay", "Bots" };
		var sheet = new Panel { Name = "Sheet", MouseFilter = Control.MouseFilterEnum.Ignore };
		sheet.AddThemeStyleboxOverride("panel", Bevel(Colors.Transparent));
		_dialog.AddChild(sheet);
		Rect(sheet, 104, 32, 788, 408);
		for (var i = 0; i < names.Length; i++)
		{
			var index = i;
			_setupTabs[i] = DialogButton(_dialog, "Tab" + i, names[i], 8, 32 + 24 * i, 96, () => SelectSetupTab(index));
			_setupTabs[i].Size = new(96, 24);
			_setupPages[i] = new Control { Name = new[] { "Server", "Gameplay", "Bots" }[i] };
			_dialog.AddChild(_setupPages[i]);
			Rect(_setupPages[i], 104, 32, 528, 416);
		}
		BuildServerPage();
		BuildPreview();
		BuildGameplayPage();
		BuildBotsPage();
		var divider = new ColorRect { Color = new("ffffff24"), MouseFilter = Control.MouseFilterEnum.Ignore };
		_dialog.AddChild(divider);
		Rect(divider, 8, 440, 884, 1);
		_error = Text(_dialog, "Error", "", 16, 448, 650, 24, 12);
		_error.AddThemeColorOverride("font_color", new("ffb080"));
		_start = DialogButton(_dialog, "Start", "Start", 720, 448, 80, StartSelectedMap);
		_start.Alignment = HorizontalAlignment.Center;
		var cancel = DialogButton(_dialog, "Cancel", "Cancel", 812, 448, 80, CloseMapSelection);
		cancel.Alignment = HorizontalAlignment.Center;
		SelectSetupTab(0);
	}

	public void SelectSetupTab(int index)
	{
		if (index < 0 || index >= _setupPages.Length) return;
		_activeSetupTab = index;
		for (var i = 0; i < _setupPages.Length; i++)
		{
			_setupPages[i].Visible = i == index;
			var selected = i == index;
			_setupTabs[i].AddThemeStyleboxOverride("normal", Bevel(selected ? new("ffffff10") : Colors.Transparent, !selected));
			_setupTabs[i].AddThemeStyleboxOverride("hover", Bevel(new(selected ? "ffffff18" : "ffffff0c"), !selected));
			_setupTabs[i].AddThemeStyleboxOverride("pressed", Bevel(new("ffffff10"), !selected));
		}
		UpdateModeDescription();
	}

	private void UpdateModeDescription()
	{
		_modeDescription.Text = _activeSetupTab == 2 ? "Bots play Bomb Defusal, Deathmatch and Team Deathmatch. Select a difficulty and quota." : SelectedMode.Description;
		_modeStatus.Text = _activeSetupTab == 2 ? "Available on Dust II, Inferno, Mirage and Cache." : SelectedMode.Available ? "Ready to play" : FutureMode + ".";
	}

	private OptionButton Choice(Control parent, string name, string label, float x, float y, float width,
		(string Id, string Title)[] choices, string selected, Action<string>? changed)
	{
		Text(parent, name + "Label", label, x, y, width, 24);
		var option = new OptionButton { Name = name, FitToLongestItem = false };
		parent.AddChild(option);
		Rect(option, x, y + 24, width, 24);
		foreach (var choice in choices) option.AddItem(choice.Title);
		option.Select(Math.Max(0, Array.FindIndex(choices, choice => choice.Id == selected)));
		if (changed != null) option.ItemSelected += index => { _click.Play(); changed(choices[(int)index].Id); };
		return option;
	}

	private Label Paragraph(Control parent, string name, string value, float x, float y, float width, float height)
	{
		var label = Text(parent, name, value, x, y, width, height);
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		label.VerticalAlignment = VerticalAlignment.Top;
		return label;
	}

	private CheckBox Toggle(Control parent, string name, string title, float x, float y, float width, bool value, Action<bool> changed)
	{
		var toggle = new CheckBox { Name = name, Text = title, ClipText = true };
		parent.AddChild(toggle);
		Rect(toggle, x, y, width, 24);
		toggle.SetPressedNoSignal(value);
		toggle.Toggled += next => changed(next);
		return toggle;
	}

	private void BuildServerPage()
	{
		var page = _setupPages[0];
		var types = GameModeCatalog.Types.Where(type => GameModeCatalog.Modes.Any(mode => mode.TypeId == type.Id && mode.Available)).ToArray();
		_typeChoices = types.Select(type => type.Id).ToArray();
		_gameType = Choice(page, "GameType", "Game type", 8, 8, 252,
			types.Select(type => (type.Id, type.Title)).ToArray(), SelectedMode.TypeId, type =>
				SelectMode(GameModeCatalog.Modes.First(mode => mode.TypeId == type && mode.Available).Id));
		_gameMode = Choice(page, "GameMode", "Game mode", 8, 56, 252,
			new[] { ("sandbox", "Free Play") }, "sandbox", null);
		_gameMode.ItemSelected += index => SelectMode(_modeChoices[(int)index]);
		var divider = new ColorRect { Color = new("282828c4"), MouseFilter = Control.MouseFilterEnum.Ignore };
		page.AddChild(divider);
		Rect(divider, 8, 112, 252, 1);
		// Keep the original server fields in place for the later hosting flow.
		Text(page, "HostNameLabel", "Server name", 8, 120, 252, 24);
		var hostName = new LineEdit { Name = "HostName", Text = "Contrapoint", Editable = false };
		page.AddChild(hostName);
		Rect(hostName, 8, 144, 252, 24);
		Text(page, "PasswordLabel", "Server password", 8, 168, 252, 24);
		var password = new LineEdit { Name = "Password", Secret = true, Editable = false };
		page.AddChild(password);
		Rect(password, 8, 192, 252, 24);
		Text(page, "MaxPlayersLabel", "Max players", 8, 216, 252, 24);
		var maxPlayers = new HSlider { Name = "MaxPlayers", MinValue = 1, MaxValue = 16, Value = 16, Editable = false };
		page.AddChild(maxPlayers);
		Rect(maxPlayers, 8, 240, 220, 32);
		Text(page, "MaxPlayersValue", "16", 236, 240, 24, 32);
		Text(page, "MapLabel", "Map", 268, 8, 252, 24);
		_mapList = new ItemList { Name = "MapList", AllowReselect = true, SelectMode = ItemList.SelectModeEnum.Single };
		page.AddChild(_mapList);
		Rect(_mapList, 268, 32, 252, 352);
		_mapList.ItemSelected += row => { _click.Play(); SelectMap(_visibleMaps[(int)row]); };
		_mapList.ItemActivated += row => { SelectMap(_visibleMaps[(int)row]); StartSelectedMap(); };
		_allMaps = Toggle(page, "AllMaps", "All maps", 268, 384, 252, MatchSession.Draft.AllMaps, value =>
		{
			MatchSession.Draft = MatchSession.Draft with { AllMaps = value };
			RefreshMapList();
		});
	}

	private void BuildPreview()
	{
		var preview = new Control { Name = "Preview" };
		_dialog.AddChild(preview);
		Rect(preview, 624, 32, 268, 416);
		Text(preview, "PreviewLabel", "Preview", 8, 8, 252, 24);
		var border = new Panel { Name = "PreviewBorder", MouseFilter = Control.MouseFilterEnum.Ignore };
		preview.AddChild(border);
		border.AddThemeStyleboxOverride("panel", Bevel(new("00000080"), true));
		Rect(border, 8, 32, 252, 144);
		_mapPreview = new TextureRect { Name = "MapPreview", ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = Control.MouseFilterEnum.Ignore };
		preview.AddChild(_mapPreview);
		Rect(_mapPreview, 10, 34, 248, 140);
		_previewFallback = Text(preview, "PreviewFallback", "No preview available", 8, 32, 252, 144);
		_previewFallback.HorizontalAlignment = HorizontalAlignment.Center;
		_previewTitle = Text(preview, "PreviewTitle", "", 8, 184, 252, 24, 12, true);
		_previewTitle.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		_previewId = Text(preview, "PreviewId", "", 8, 208, 252, 24);
		_modeDescription = Paragraph(preview, "ModeDescription", "", 8, 256, 252, 72);
		_modeStatus = Paragraph(preview, "ModeStatus", "", 8, 336, 252, 40);
		_mapCount = Text(preview, "MapCount", "", 8, 384, 252, 24);
	}

	private void BuildGameplayPage()
	{
		var page = _setupPages[1];
		Text(page, "Heading", "Gameplay settings", 8, 8, 512, 24, 12);
		Paragraph(page, "Hint", "These settings apply when starting a new local game.", 8, 40, 512, 48);
		Toggle(page, "FriendlyFire", "Friendly fire", 8, 96, 512, MatchSession.Draft.FriendlyFire,
			value => MatchSession.Draft = MatchSession.Draft with { FriendlyFire = value });
		Toggle(page, "FallDamage", "Fall damage", 8, 128, 512, MatchSession.Draft.FallDamage,
			value => MatchSession.Draft = MatchSession.Draft with { FallDamage = value });
		Toggle(page, "Cheats", "Enable cheats (sv_cheats)", 8, 160, 512, MatchSession.Draft.Cheats,
			value => MatchSession.Draft = MatchSession.Draft with { Cheats = value });
		Paragraph(page, "RulesHint", "DM: 10 minutes, armor and fast respawns. Enemy kills grant +15 HP and magazine ammo. Host console: mp_timelimit, dm_respawn_delay, dm_kill_health, dm_kill_ammo. Team damage follows Friendly fire.",
			8, 250, 512, 80).AddThemeColorOverride("font_color", new("999999"));
	}

	private void BotChoice(Control page, string name, string label, float x, float y, (string, string)[] choices,
		string selected, Func<BotSetup, string, BotSetup> change) =>
		Choice(page, name, label, x, y, 252, choices, selected,
			value => MatchSession.Draft = MatchSession.Draft with { Bots = change(MatchSession.Draft.Bots, value) });

	private void BotToggle(Control page, string name, string label, float x, float y, bool value, Func<BotSetup, bool, BotSetup> change) =>
		Toggle(page, name, label, x, y, 252, value,
			next => MatchSession.Draft = MatchSession.Draft with { Bots = change(MatchSession.Draft.Bots, next) });

	private void BuildBotsPage()
	{
		var page = _setupPages[2];
		var content = page;
		var bots = MatchSession.Draft.Bots;
		BotChoice(content, "Difficulty", "Difficulty", 8, 8,
			new[] { ("none", "No bots"), ("harmless", "Harmless"), ("easy", "Easy"), ("normal", "Normal"), ("hard", "Hard"), ("expert", "Expert") },
			bots.Difficulty, (b, v) => b with { Difficulty = v });
		BotChoice(content, "QuotaMode", "Quota mode", 268, 8,
			new[] { ("normal", "Normal"), ("fill", "Fill to number"), ("match", "Match ratio") }, bots.QuotaMode, (b, v) => b with { QuotaMode = v });
		Text(content, "QuotaLabel", "Quota", 8, 56, 512, 24);
		var quota = new HSlider { Name = "Quota", MinValue = 0, MaxValue = 15, Step = 1, Value = bots.Quota };
		content.AddChild(quota);
		Rect(quota, 8, 80, 480, 32);
		var count = Text(content, "QuotaValue", bots.Quota.ToString(), 496, 80, 24, 32);
		quota.ValueChanged += value =>
		{
			MatchSession.Draft = MatchSession.Draft with { Bots = MatchSession.Draft.Bots with { Quota = (int)value } };
			count.Text = ((int)value).ToString();
		};
		Text(content, "PrefixLabel", "Name prefix", 8, 128, 252, 24);
		var prefix = new LineEdit { Name = "Prefix", Text = bots.Prefix, MaxLength = 32 };
		content.AddChild(prefix);
		Rect(prefix, 8, 152, 252, 24);
		prefix.TextChanged += value => MatchSession.Draft = MatchSession.Draft with { Bots = MatchSession.Draft.Bots with { Prefix = value } };
		BotChoice(content, "JoinTeam", "Join team", 8, 176,
			new[] { ("any", "Any"), ("ct", "Counter-Terrorists"), ("t", "Terrorists") }, bots.Team, (b, v) => b with { Team = v });
		BotChoice(content, "Chatter", "Chatter", 268, 128,
			new[] { ("normal", "Normal"), ("minimal", "Minimal"), ("radio", "Use radio"), ("off", "Off") }, bots.Chatter, (b, v) => b with { Chatter = v });
		BotChoice(content, "Voices", "Voice", 268, 176,
			new[] { ("old", "Old voice"), ("new", "New voices") }, bots.NewVoices ? "new" : "old", (b, v) => b with { NewVoices = v == "new" });
		BotToggle(content, "Controllable", "Bots are controllable", 8, 240, bots.Controllable, (b, v) => b with { Controllable = v });
		BotToggle(content, "JoinAfterPlayer", "Join after player", 8, 264, bots.JoinAfterPlayer, (b, v) => b with { JoinAfterPlayer = v });
		BotToggle(content, "DeferGoals", "Defer goals to humans", 8, 288, bots.DeferGoals, (b, v) => b with { DeferGoals = v });
		BotToggle(content, "DeferItems", "Defer items to humans", 8, 312, bots.DeferItems, (b, v) => b with { DeferItems = v });
		BotToggle(content, "AllowRogues", "Allow rogues", 8, 336, bots.AllowRogues, (b, v) => b with { AllowRogues = v });
		BotToggle(content, "VoicePitch", "Difficulty affects voice pitch", 8, 360, bots.VoicePitch, (b, v) => b with { VoicePitch = v });
		BotToggle(content, "Pistols", "Allow pistols", 268, 240, bots.Pistols, (b, v) => b with { Pistols = v });
		BotToggle(content, "Shotguns", "Allow shotguns", 268, 264, bots.Shotguns, (b, v) => b with { Shotguns = v });
		BotToggle(content, "SubmachineGuns", "Allow submachine guns", 268, 288, bots.SubmachineGuns, (b, v) => b with { SubmachineGuns = v });
		BotToggle(content, "Rifles", "Allow rifles", 268, 312, bots.Rifles, (b, v) => b with { Rifles = v });
		BotToggle(content, "MachineGuns", "Allow machine guns", 268, 336, bots.MachineGuns, (b, v) => b with { MachineGuns = v });
		BotToggle(content, "Grenades", "Allow grenades", 268, 360, bots.Grenades, (b, v) => b with { Grenades = v });
		BotToggle(content, "Snipers", "Allow sniper rifles", 268, 384, bots.Snipers, (b, v) => b with { Snipers = v });
		foreach (var name in new[] { "Chatter", "Voices", "Controllable", "DeferGoals", "DeferItems", "AllowRogues", "VoicePitch",
			"Shotguns", "SubmachineGuns", "MachineGuns", "Grenades" })
		{
			var control = content.GetNode<BaseButton>(name);
			control.Disabled = true;
			control.TooltipText = "Not available for Deathmatch bots.";
		}
	}

	private void RestoreSetup()
	{
		SelectedMap = Array.FindIndex(MapIds, id => id == MatchSession.Draft.MapId);
		SelectMode(MatchSession.Draft.ModeId);
	}

	public void SelectMode(string id)
	{
		var mode = GameModeCatalog.Find(id);
		if (!mode.Available) mode = GameModeCatalog.Find("sandbox");
		MatchSession.Draft = MatchSession.Draft with { ModeId = mode.Id };
		_gameType.Select(Array.IndexOf(_typeChoices, mode.TypeId));
		var choices = GameModeCatalog.Modes.Where(entry => entry.TypeId == mode.TypeId && entry.Available).ToArray();
		_modeChoices = choices.Select(entry => entry.Id).ToArray();
		_gameMode.Clear();
		foreach (var entry in choices) _gameMode.AddItem(entry.Title);
		_gameMode.Select(Array.IndexOf(_modeChoices, mode.Id));
		UpdateModeDescription();
		RefreshMapList();
	}

	private void RefreshMapList()
	{
		_visibleMaps.Clear();
		_mapList.Clear();
		for (var i = 0; i < Maps.Length; i++)
		{
			var map = Maps[i];
			if (!_allMaps.ButtonPressed && !SelectedMode.Supports(map)) continue;
			_visibleMaps.Add(i);
			_mapList.AddItem(map.Id);
			_mapList.SetItemTooltipEnabled(_visibleMaps.Count - 1, false);
		}
		_mapCount.Text = _visibleMaps.Count == 0 ? "No maps available" : $"Maps: {_visibleMaps.Count} / {Maps.Length}";
		SelectMap(_visibleMaps.Contains(SelectedMap) ? SelectedMap : _visibleMaps.FirstOrDefault(-1));
	}

	public void SelectMap(int index)
	{
		SelectedMap = _visibleMaps.Contains(index) ? index : -1;
		if (SelectedMap >= 0)
		{
			var map = Maps[SelectedMap];
			_mapList.Select(_visibleMaps.IndexOf(SelectedMap));
			_mapList.EnsureCurrentIsVisible();
			if (!_previews.TryGetValue(map.Id, out var texture))
			{
				texture = ResourceLoader.Exists(map.ThumbnailPath) ? GD.Load<Texture2D>(map.ThumbnailPath) : null;
				// Existing CSS thumbnails have 32 blank bottom rows; keep their original crop.
				if (texture != null && texture.GetSize() == new Vector2(128, 128))
					texture = new AtlasTexture { Atlas = texture, Region = new(0, 12, 128, 72) };
				_previews[map.Id] = texture;
			}
			_mapPreview.Texture = texture;
			_previewTitle.Text = map.Title;
			_previewId.Text = map.Id;
			_previewFallback.Text = "No preview available";
			MatchSession.Draft = MatchSession.Draft with { MapId = map.Id };
		}
		else
		{
			_mapList.DeselectAll();
			_mapPreview.Texture = null;
			_previewTitle.Text = "No map selected";
			_previewId.Text = "Enable All maps to see more maps";
			_previewFallback.Text = "No maps available";
		}
		_previewFallback.Visible = _mapPreview.Texture == null;
		_start.Disabled = !CanStartSelection();
		_error.Text = "";
	}

	private void SelectAdjacentMap(int direction)
	{
		if (_visibleMaps.Count == 0) return;
		var row = Math.Clamp(_visibleMaps.IndexOf(SelectedMap) + direction, 0, _visibleMaps.Count - 1);
		SelectMap(_visibleMaps[row]);
	}

	private bool CanStartSelection() => SelectedMode.Available && SelectedMap >= 0
		&& _visibleMaps.Contains(SelectedMap) && ResourceLoader.Exists(MapScenes[SelectedMap])
		&& (SelectedMode.Id != "defuse" || SelectedMode.Supports(Maps[SelectedMap]));

	private void SaveSetup()
	{
		if (!MatchSession.Save()) _error.Text = "Could not save game settings.";
	}
}
