using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Contrapoint.Players;

namespace Contrapoint.UI;

public partial class MainMenu
{
	private const string OptionsAssets = "res://assets/ui/csso/options/";
	private const string UnavailableOption = "This CSSO feature is not available in Contrapoint yet.";
	private static readonly string[][] OptionSections =
	[
		["Game", "Gameplay", "Viewmodel", "Crosshair", "GrenadeCrosshair", "Multiplayer"],
		["Controls", "Keyboard", "Mouse", "Advanced"],
		["Audio", "Volume", "Voice", "Advanced"],
		["Video", "Screen", "Graphics", "Interface"]
	];
	private Panel _options = null!;
	private Control _optionContent = null!;
	private Button _optionApply = null!;
	private Label _optionStatus = null!;
	private readonly Dictionary<string, Control> _optionPages = new();
	private readonly Dictionary<string, Button> _optionTabs = new();
	private readonly List<Action> _optionRefresh = new();
	private readonly HashSet<string> _optionNames = new();
	private Dictionary<string, float> _optionDraft = new(StringComparer.OrdinalIgnoreCase);
	private Dictionary<string, string> _bindingDraft = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, string> _optionStrings = JsonSerializer.Deserialize<Dictionary<string, string>>(FileAccess.GetFileAsString(OptionsAssets + "english.json"))!;
	private bool _refreshingOptions;
	private bool _draggingOptions;
	private Vector2 _optionsDragOffset;
	private string _optionSection = "Game", _optionPage = "Gameplay";
	private Tree _bindingList = null!;
	private bool _capturingBinding;
	private string? _captureCommand;
	private Control? _crosshairPreview;
	private Panel _displayConfirmation = null!;
	private Label _displayCountdown = null!;
	private Dictionary<string, float>? _previousDisplay;
	private double _displaySeconds;
	private bool _closeAfterDisplay;
	private Vector2 _optionCanvasSize, _optionCanvasStretch;
	public bool IsOptionsOpen => _options != null && _modal.Visible && _options.Visible;

	private static string Property(JsonElement data, string name, string fallback = "") => data.EnumerateObject()
		.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Value is var value
		&& value.ValueKind == JsonValueKind.String ? value.GetString()! : fallback;
	private static float Numeric(JsonElement data, string name, float fallback = 0) =>
		float.TryParse(Property(data, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : fallback;
	private string OptionText(string token) => _optionStrings.GetValueOrDefault(token.TrimStart('#'), token.TrimStart('#'));
	private static string SourceOptionName(string name) => name == "cl_use_new_viewmodel_bob" ? "cl_bob_style" : name;
	private float DraftValue(string name) => _optionDraft.GetValueOrDefault(name, ConsoleCommands.IsOption(name) ? ConsoleCommands.OptionDefault(name) : 0);
	private void SetDraft(string name, float value)
	{
		if (_refreshingOptions) return;
		_optionDraft[name] = value;
		_optionApply.Disabled = false;
		_optionStatus.Text = "";
		_crosshairPreview?.QueueRedraw();
	}
	private void AddOption(string name) { if (ConsoleCommands.IsOption(name)) _optionNames.Add(name); }

	private void BuildOptions()
	{
		_options = new Panel { Name = "Options", Size = new(640, 480), Theme = _dialog.Theme, Visible = false };
		_options.AddThemeStyleboxOverride("panel", Bevel(new("a0a0a080")));
		_modal.AddChild(_options);
		_options.Draw += () =>
		{
			_options.DrawLine(Vector2.Zero, new(640, 0), new("c8c8c8c4"));
			_options.DrawLine(Vector2.Zero, new(0, 480), new("c8c8c8c4"));
		};
		var title = Text(_options, "Title", "Options", 16, 4, 592, 24, 18);
		title.MouseFilter = Control.MouseFilterEnum.Stop;
		title.GuiInput += input =>
		{
			if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
			{ _draggingOptions = true; _optionsDragOffset = _root.GetLocalMousePosition() - _options.Position; }
		};
		DialogButton(_options, "Close", "×", 612, 8, 18, CloseOptions).Alignment = HorizontalAlignment.Center;
		_optionContent = new Panel { Name = "Sheet", ClipContents = true };
		_optionContent.AddThemeStyleboxOverride("panel", Bevel(Colors.Transparent));
		_options.AddChild(_optionContent);
		Rect(_optionContent, 104, 32, 528, 416);
		using var layouts = JsonDocument.Parse(FileAccess.GetFileAsString(OptionsAssets + "layout.json"));
		for (var i = 0; i < OptionSections.Length; i++)
		{
			var section = OptionSections[i];
			var group = section[0];
			var groupTab = DialogButton(_options, "Tab" + group, OptionText("GameUI_Options_" + group), 8, 32 + i * 24, 96,
				() => SelectOptionsPage(group, section[1]));
			_optionTabs[group] = groupTab;
			var subX = 4f;
			for (var j = 1; j < section.Length; j++)
			{
				var sub = section[j];
				var subTitle = OptionText("GameUI_Options_" + group + "_" + sub);
				var tabWidth = Mathf.Max(64, _options.Theme.DefaultFont.GetStringSize(subTitle, fontSize: 12).X + 18);
				var tab = DialogButton(_optionContent, group + sub + "Tab", subTitle, subX, 0, tabWidth,
					() => SelectOptionsPage(group, sub));
				_optionTabs[group + "/" + sub] = tab;
				subX += tabWidth;
				var page = new Control { Name = group + sub, Visible = false, ClipContents = true };
				_optionContent.AddChild(page);
				Rect(page, 0, 24, 528, 392);
				_optionPages[group + "/" + sub] = page;
				if (group == "Controls" && sub == "Keyboard") BuildBindingPage(page);
				else BuildSourceOptionsPage(page, layouts.RootElement.GetProperty(("csoptionsdialogsub" + group + "sub" + sub).ToLowerInvariant()));
			}
		}
		var divider = new ColorRect { Color = new("ffffff24"), MouseFilter = Control.MouseFilterEnum.Ignore };
		_options.AddChild(divider);
		Rect(divider, 8, 444, 96, 1);
		_optionStatus = Text(_options, "Status", "", 12, 446, 332, 30);
		_optionStatus.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		_optionStatus.AddThemeColorOverride("font_color", new("ffb080"));
		DialogButton(_options, "OK", "OK", 360, 448, 80, () => ApplyOptions(true)).Alignment = HorizontalAlignment.Center;
		DialogButton(_options, "Cancel", "Cancel", 456, 448, 80, CloseOptions).Alignment = HorizontalAlignment.Center;
		_optionApply = DialogButton(_options, "Apply", "Apply", 552, 448, 80, () => ApplyOptions(false));
		_optionApply.Alignment = HorizontalAlignment.Center;
		_optionApply.Disabled = true;
		BuildDisplayConfirmation();
		SelectOptionsPage("Game", "Gameplay");
	}

	public void OpenOptions()
	{
		if (_pendingScene != null || _changingScene) return;
		ConsoleCommands.InitializeMenuOptions();
		_dialog.Hide();
		_loading.Hide();
		_modal.Show();
		_options.Show();
		_menu.MouseFilter = Control.MouseFilterEnum.Ignore;
		_optionDraft = _optionNames.ToDictionary(name => name, name => ConsoleCommands.OptionValue(name, _player), StringComparer.OrdinalIgnoreCase);
		_bindingDraft = new(ConsoleBindings.Keys, StringComparer.OrdinalIgnoreCase);
		_optionStatus.Text = "";
		RefreshOptions();
		_optionApply.Disabled = true;
		LayoutOptions();
		_optionTabs[_optionSection].GrabFocus();
	}
	public void CloseOptions()
	{
		if (_previousDisplay != null) RevertDisplay();
		_capturingBinding = _draggingOptions = false;
		_options.Hide();
		_modal.Hide();
		_menu.MouseFilter = Control.MouseFilterEnum.Pass;
		_menu.GetNode<Button>("Options").GrabFocus();
	}
	public void SelectOptionsPage(string section, string sub)
	{
		if (!_optionPages.ContainsKey(section + "/" + sub)) return;
		_optionSection = section; _optionPage = sub;
		foreach (var pair in _optionPages) pair.Value.Visible = pair.Key == section + "/" + sub;
		foreach (var pair in _optionTabs)
		{
			if (pair.Key.Contains('/')) pair.Value.Visible = pair.Key.StartsWith(section + "/");
			pair.Value.AddThemeStyleboxOverride("normal", Bevel(pair.Key == section || pair.Key == section + "/" + sub ? new("ffffff15") : Colors.Transparent));
		}
	}
	private void LayoutOptions()
	{
		if (_options == null) return;
		var stretch = GetViewport().GetStretchTransform().Scale;
		var pixels = _root.Size * stretch;
		var scale = Mathf.Clamp(Mathf.Min((pixels.X - 24) / 640, (pixels.Y - 24) / 480), .1f, 1);
		_options.Scale = new(scale / stretch.X, scale / stretch.Y);
		_options.Position = (_root.Size - _options.Size * _options.Scale) * .5f;
	}
	private void RefreshOptions()
	{
		_refreshingOptions = true;
		try { foreach (var refresh in _optionRefresh) refresh(); RefreshBindings(); }
		finally { _refreshingOptions = false; }
		_crosshairPreview?.QueueRedraw();
	}

	private void BuildSourceOptionsPage(Control page, JsonElement layout)
	{
		foreach (var entry in layout.EnumerateObject())
		{
			var data = entry.Value;
			if (data.ValueKind != JsonValueKind.Object || Property(data, "visible", "1") == "0") continue;
			var name = entry.Name;
			var type = Property(data, "controlName");
			var x = Numeric(data, "xpos"); var y = Numeric(data, "ypos");
			// CSSO repeats wide=512 on several 32-pixel text entries. Clip that typo to the sheet.
			var width = Mathf.Min(Numeric(data, "wide", 252), 520 - x); var height = Numeric(data, "tall", 24);
			var text = OptionText(Property(data, "labelText"));
			var cvar = SourceOptionName(Property(data, "cvar_name"));
			if (type == "Label" || name.EndsWith("Label"))
			{
				if (name == "DXLevelLabel") text = "Godot · Mobile renderer";
				if (name == "RecommendedTipLabel") text = "Unavailable features are disabled.";
				if (name.EndsWith("ImageTipLabel")) text = "";
				var label = Text(page, name, text, x, y, width, height);
				label.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
				label.TooltipText = text;
				continue;
			}
			if (type == "Divider")
			{
				var line = new ColorRect { Name = name, Color = new("ffffff24"), MouseFilter = Control.MouseFilterEnum.Ignore };
				page.AddChild(line); Rect(line, x, y, width, 1); continue;
			}
			if (name == "CrosshairImage") { BuildCrosshairPreview(page, x, y, width, height); continue; }
			if (name.EndsWith("TextEntry")) continue; // Built together with its slider.
			if (BuildSpecialOption(page, name, x, y, width, height)) continue;
			if (type.Contains("ComboBox") || name.EndsWith("ComboBox"))
			{
				var choices = ConsoleCommands.IsOption(cvar) && data.TryGetProperty("values", out var list) ? list.EnumerateObject()
					.Select(p => (OptionText(p.Name), float.Parse(p.Value.GetString()!, CultureInfo.InvariantCulture))).ToArray() : Array.Empty<(string, float)>();
				BuildOptionChoice(page, name, cvar, x, y, width, choices); continue;
			}
			if (type.Contains("Slider") || name.EndsWith("Slider"))
			{
				var paired = layout.TryGetProperty(name.Replace("Slider", "TextEntry"), out _);
				BuildOptionSlider(page, name, cvar, x, y, width, height,
					Numeric(data, "minvalue", Numeric(data, "rangeMin", float.NaN)), Numeric(data, "maxvalue", Numeric(data, "rangeMax", float.NaN)), paired);
				continue;
			}
			if (type.Contains("Check") || name.Contains("Check"))
			{
				var supported = ConsoleCommands.IsOption(cvar);
				var check = new CheckBox { Name = name, Text = text, Disabled = !supported, TooltipText = supported ? OptionText(Property(data, "tooltiptext")) : UnavailableOption };
				page.AddChild(check); Rect(check, x, y, width, height);
				if (supported)
				{
					AddOption(cvar);
					var selected = Numeric(data, "cvar_value_selected", 1);
					var reverse = cvar is "m_pitch" or "m_yaw";
					_optionRefresh.Add(() => check.SetPressedNoSignal(reverse ? DraftValue(cvar) < 0 : DraftValue(cvar) == selected));
					check.Toggled += value => SetDraft(cvar, reverse ? MathF.Abs(DraftValue(cvar)) * (value ? -1 : 1) : value ? selected : 0);
				}
				continue;
			}
			if (type.Contains("Button") || name.EndsWith("Button"))
			{
				var button = DialogButton(page, name, text, x, y, width, () => { });
				button.Disabled = true; button.TooltipText = UnavailableOption;
				continue;
			}
			if (name.Contains("Image") || name.Contains("ProgressBar"))
			{
				var panel = new Panel { Name = name, TooltipText = UnavailableOption };
				page.AddChild(panel); Rect(panel, x, y, width, height);
				panel.AddThemeStyleboxOverride("panel", Bevel(new("00000060"), true));
				Text(panel, "Unavailable", "Not available", 8, 0, width - 16, height).HorizontalAlignment = HorizontalAlignment.Center;
			}
		}
	}

	private void BuildOptionChoice(Control page, string name, string cvar, float x, float y, float width, (string Text, float Value)[] choices)
	{
		var supported = ConsoleCommands.IsOption(cvar) && choices.Length > 0;
		var choice = new OptionButton { Name = name, Disabled = !supported, FitToLongestItem = false, TooltipText = supported ? "" : UnavailableOption };
		page.AddChild(choice); Rect(choice, x, y, width, 24);
		if (!supported) { choice.AddItem("Not available"); return; }
		foreach (var item in choices) choice.AddItem(item.Text);
		AddOption(cvar);
		_optionRefresh.Add(() => choice.Select(Math.Max(0, Array.FindIndex(choices, c => Mathf.IsEqualApprox(c.Value, DraftValue(cvar))))));
		choice.ItemSelected += index => SetDraft(cvar, choices[(int)index].Value);
	}
	private void BuildOptionSlider(Control page, string name, string cvar, float x, float y, float width, float height, float min, float max, bool entry)
	{
		var supported = ConsoleCommands.IsOption(cvar);
		var limits = supported ? ConsoleCommands.OptionRange(cvar) : (Min: 0f, Max: 1f, Integer: false);
		min = float.IsNaN(min) ? limits.Min : Math.Max(min, limits.Min);
		max = float.IsNaN(max) ? limits.Max : Math.Min(max, limits.Max);
		var slider = new HSlider { Name = name, MinValue = min, MaxValue = max, Step = limits.Integer ? 1 : .01,
			Editable = supported, TooltipText = supported ? cvar : UnavailableOption };
		page.AddChild(slider); Rect(slider, x, y, width, height);
		if (!supported) return;
		AddOption(cvar);
		_optionRefresh.Add(() => slider.SetValueNoSignal(Math.Clamp(DraftValue(cvar), min, max)));
		slider.ValueChanged += value => SetDraft(cvar, (float)value);
		if (entry)
		{
			// Keep numeric editing in the original right-hand cell; unlike the slider,
			// it accepts the complete valid console range (e.g. sensitivity > 8).
			var edit = new LineEdit { Name = name.Replace("Slider", "TextEntry"), SelectAllOnFocus = true };
			edit.AddThemeConstantOverride("minimum_character_width", 1);
			page.AddChild(edit); Rect(edit, 480, y + 8, 40, 24);
			slider.Size = new(464, height);
			void Refresh() => edit.Text = DraftValue(cvar).ToString("0.##", CultureInfo.InvariantCulture);
			void Commit()
			{
				if (float.TryParse(edit.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
					&& float.IsFinite(value) && value >= limits.Min && value <= limits.Max && (!limits.Integer || value == MathF.Truncate(value)))
				{ SetDraft(cvar, value); slider.SetValueNoSignal(Math.Clamp(value, min, max)); }
				Refresh();
			}
			_optionRefresh.Add(Refresh);
			slider.ValueChanged += _ => Refresh();
			edit.TextSubmitted += _ => Commit(); edit.FocusExited += Commit;
		}
		else
		{
			var valueText = Text(page, name + "Value", "", x + width - 48, y, 48, height, 10);
			valueText.HorizontalAlignment = HorizontalAlignment.Right;
			valueText.MouseFilter = Control.MouseFilterEnum.Ignore;
			slider.Size = new(Math.Max(24, width - 52), height);
			void UpdateText() => valueText.Text = DraftValue(cvar).ToString("0.##", CultureInfo.InvariantCulture);
			_optionRefresh.Add(UpdateText); slider.ValueChanged += _ => UpdateText();
		}
	}

	private bool BuildSpecialOption(Control page, string name, float x, float y, float width, float height)
	{
		(string Cvar, (string, float)[] Choices)? mapping = name switch
		{
			"WindowModeComboBox" => ("cp_window_mode", [("Windowed", 0), ("Fullscreen", 1), ("Fullscreen windowed", 2)]),
			"ModelQualityComboBox" => ("r_rootlod", [("Low", 2), ("Medium", 1), ("High", 0)]),
			"TextureQualityComboBox" => ("mat_picmip", [("Low", 2), ("Medium", 1), ("High", 0)]),
			"ShadowQualityComboBox" => ("cp_shadow_quality", [("Low", 0), ("Medium", 1), ("High", 2)]),
			"WaterQualityComboBox" => ("cp_water_quality", [("Simple reflections", 0), ("Reflect world", 1)]),
			"TextureFilteringComboBox" => ("cp_texture_filter", [("Bilinear", 0), ("Trilinear", 1), ("Anisotropic 2X", 2), ("Anisotropic 4X", 3), ("Anisotropic 8X", 4), ("Anisotropic 16X", 5)]),
			"AntiAliasingComboBox" => ("cp_antialias", [("None", 0), ("2X MSAA", 1), ("4X MSAA", 2), ("8X MSAA", 3)]),
			_ => null
		};
		if (mapping is { } info)
		{
			BuildOptionChoice(page, name, info.Cvar, x, y, width, info.Choices);
			var control = page.GetNode<Control>(name);
			if (name == "ModelQualityComboBox") control.TooltipText = "Controls automatic LOD on meshes with imported levels of detail.";
			if (name == "ShadowQualityComboBox") control.TooltipText = "Controls dynamic shadow resolution and filtering. Map lightmaps contain baked shadows.";
			if (name == "WaterQualityComboBox") control.TooltipText = "Simple water, or refractive water with the imported world cubemap.";
			return true;
		}
		if (name == "ResolutionComboBox")
		{
			var sizes = new List<Vector2I> { new(640,480), new(800,600), new(1024,768), new(1280,720), new(1280,800), new(1280,960), new(1280,1024),
				new(1366,768), new(1440,900), new(1600,900), new(1680,1050), new(1920,1080), new(1920,1200), new(2560,1440), new(3840,2160) };
			var choice = new OptionButton { Name = name, TooltipText = "Window size in windowed mode; 3D render resolution in fullscreen." };
			page.AddChild(choice); Rect(choice, x, y, width, 24);
			AddOption("cp_screen_width"); AddOption("cp_screen_height"); AddOption("cp_resolution_override");
			_optionRefresh.Add(() =>
			{
				var current = new Vector2I((int)DraftValue("cp_screen_width"), (int)DraftValue("cp_screen_height"));
				if (!sizes.Contains(current)) sizes.Add(current);
				choice.Clear(); foreach (var size in sizes) choice.AddItem($"{size.X} x {size.Y}");
				choice.Select(sizes.IndexOf(current));
			});
			choice.ItemSelected += index => { SetDraft("cp_screen_width", sizes[(int)index].X); SetDraft("cp_screen_height", sizes[(int)index].Y); SetDraft("cp_resolution_override", 1); };
			return true;
		}
		if (name == "FPSMaxCheckButton")
		{
			var check = new CheckBox { Name = name, Text = OptionText("GameUI_Options_Video_FPSMax") };
			page.AddChild(check); Rect(check, x, y, width, height); AddOption("fps_max");
			_optionRefresh.Add(() => check.SetPressedNoSignal(DraftValue("fps_max") != 0));
			check.Toggled += value => { SetDraft("fps_max", value ? 300 : 0); RefreshOptions(); };
			return true;
		}
		return false;
	}

	private void BuildCrosshairPreview(Control page, float x, float y, float width, float height)
	{
		var panel = new Panel { Name = "CrosshairImage" };
		panel.AddThemeStyleboxOverride("panel", Bevel(new("20262b"), true));
		page.AddChild(panel); Rect(panel, x, y, width, height);
		_crosshairPreview = panel;
		panel.Draw += () =>
		{
			var center = panel.Size * .5f;
			var preset = (int)DraftValue("cl_crosshaircolor");
			var color = preset switch { 0 => Colors.Red, 1 => Colors.Green, 2 => Colors.Yellow, 3 => Colors.Blue, 4 => Colors.Cyan,
				_ => new Color(DraftValue("cl_crosshaircolor_r") / 255, DraftValue("cl_crosshaircolor_g") / 255, DraftValue("cl_crosshaircolor_b") / 255) };
			color.A = DraftValue("cl_crosshairusealpha") != 0 ? DraftValue("cl_crosshairalpha") / 255 : 1;
			var length = Mathf.Round(DraftValue("cl_crosshairsize") * 1.5f);
			var thickness = Mathf.Max(1, Mathf.Round(DraftValue("cl_crosshairthickness") * 1.5f));
			var gap = (int)(4 + DraftValue("cl_crosshairgap")) + thickness / 2;
			void Arm(Rect2 rect)
			{
				if (DraftValue("cl_crosshair_drawoutline") != 0) panel.DrawRect(rect.Grow(1), new(0, 0, 0, color.A));
				panel.DrawRect(rect, color);
			}
			Arm(new(center + new Vector2(-thickness / 2, -gap - length), new(thickness, length)));
			Arm(new(center + new Vector2(-thickness / 2, gap), new(thickness, length)));
			Arm(new(center + new Vector2(-gap - length, -thickness / 2), new(length, thickness)));
			Arm(new(center + new Vector2(gap, -thickness / 2), new(length, thickness)));
			if (DraftValue("cl_crosshairdot") != 0) Arm(new(center - Vector2.One * thickness / 2, Vector2.One * thickness));
		};
	}

	private void BuildDisplayConfirmation()
	{
		_displayConfirmation = new Panel { Name = "DisplayConfirmation", Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
		_displayConfirmation.AddThemeStyleboxOverride("panel", Bevel(new("747474")));
		_options.AddChild(_displayConfirmation); Rect(_displayConfirmation, 140, 160, 360, 144);
		Text(_displayConfirmation, "Title", "Keep these display settings?", 16, 8, 328, 28, 16);
		_displayCountdown = Text(_displayConfirmation, "Countdown", "", 16, 44, 328, 28);
		DialogButton(_displayConfirmation, "Keep", "Keep", 172, 104, 80, KeepDisplaySettings).Alignment = HorizontalAlignment.Center;
		DialogButton(_displayConfirmation, "Revert", "Revert", 264, 104, 80, RevertDisplay).Alignment = HorizontalAlignment.Center;
	}
	private void ApplyOptions(bool close)
	{
		if (_previousDisplay != null) return;
		if (GetViewport().GuiGetFocusOwner() is LineEdit edit) edit.ReleaseFocus();
		var displayKeys = new[] { "cp_screen_width", "cp_screen_height", "cp_window_mode", "cp_resolution_override" };
		var previous = displayKeys.ToDictionary(k => k, k => ConsoleCommands.OptionValue(k, _player));
		if (displayKeys.Any(k => previous[k] != DraftValue(k)) && DisplayServer.GetName() != "headless")
		{
			_previousDisplay = previous;
			_closeAfterDisplay = close;
			_displaySeconds = 15;
			foreach (var key in displayKeys) GamePreferences.Set(key, DraftValue(key));
			GamePreferences.Instance?.ApplyNow();
			_displayConfirmation.Show();
			SetOptionsEditing(false);
			_displayConfirmation.GetNode<Button>("Keep").GrabFocus();
			return;
		}
		SaveOptions(close);
	}
	private void SaveOptions(bool close)
	{
		var result = ConsoleCommands.ApplyOptions(_optionDraft, _bindingDraft, _player);
		GamePreferences.Instance?.ApplyNow();
		if (result != Error.Ok) { _optionStatus.Text = "Could not save settings: " + result; return; }
		_optionApply.Disabled = true;
		_optionStatus.Text = "";
		if (close) CloseOptions();
	}
	private void KeepDisplaySettings()
	{
		_previousDisplay = null;
		_displayConfirmation.Hide(); SetOptionsEditing(true);
		SaveOptions(_closeAfterDisplay);
	}
	private void RevertDisplay()
	{
		if (_previousDisplay == null) return;
		foreach (var pair in _previousDisplay) { GamePreferences.Set(pair.Key, pair.Value); _optionDraft[pair.Key] = pair.Value; }
		_previousDisplay = null;
		GamePreferences.Instance?.ApplyNow();
		_displayConfirmation.Hide(); SetOptionsEditing(true); RefreshOptions();
	}
	private void SetOptionsEditing(bool enabled)
	{
		// An opaque mouse shield also prevents clicks behind the confirmation;
		// keyboard focus stays inside it until Keep/Revert/Escape.
		foreach (var child in _options.GetChildren().OfType<Control>())
			if (child != _displayConfirmation) child.MouseFilter = enabled ? Control.MouseFilterEnum.Pass : Control.MouseFilterEnum.Ignore;
		_options.GetNode<Button>("OK").Disabled = !enabled;
		_options.GetNode<Button>("Cancel").Disabled = !enabled;
		_optionApply.Disabled = !enabled;
	}
	private bool HandleOptionsInput(InputEvent input)
	{
		if (!IsOptionsOpen) return false;
		if (_previousDisplay != null)
		{
			if (input is InputEventKey { Pressed: true, Echo: false } key)
			{
				if (key.Keycode == Key.Escape || key.PhysicalKeycode == Key.Escape) RevertDisplay();
				else if (key.Keycode == Key.Enter || key.PhysicalKeycode == Key.Enter) KeepDisplaySettings();
				GetViewport().SetInputAsHandled(); return true;
			}
			if (input is InputEventMouseButton mouse && !_displayConfirmation.GetGlobalRect().HasPoint(_options.GetGlobalMousePosition()))
			{ GetViewport().SetInputAsHandled(); return true; }
		}
		if (_capturingBinding)
		{
			if (input.IsPressed() && input is not InputEventKey { Echo: true })
			{
				var key = ConsoleBindings.EventKey(input);
				if (key != null)
				{
					if (key != "ESCAPE" && _captureCommand != null)
					{
						_bindingDraft[key] = _captureCommand;
						_optionApply.Disabled = false;
					}
					_capturingBinding = false; _optionStatus.Text = ""; RefreshBindings();
				}
			}
			GetViewport().SetInputAsHandled(); return true;
		}
		if (_draggingOptions && input is InputEventMouseMotion)
		{
			_options.Position = (_root.GetLocalMousePosition() - _optionsDragOffset).Clamp(Vector2.Zero, (_root.Size - _options.Size * _options.Scale).Max(Vector2.Zero));
			GetViewport().SetInputAsHandled(); return true;
		}
		if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }) _draggingOptions = false;
		return false;
	}
	private void ProcessOptions(double delta)
	{
		if (IsOptionsOpen)
		{
			var stretch = GetViewport().GetStretchTransform().Scale;
			if (_optionCanvasSize != _root.Size || _optionCanvasStretch != stretch)
			{
				LayoutOptions();
				_optionCanvasSize = _root.Size; _optionCanvasStretch = stretch;
			}
		}
		if (_previousDisplay == null) return;
		_displaySeconds -= delta;
		_displayCountdown.Text = $"Reverting in {(int)Math.Ceiling(_displaySeconds)} seconds.";
		if (_displaySeconds <= 0) RevertDisplay();
	}
}
