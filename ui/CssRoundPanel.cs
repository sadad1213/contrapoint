using Godot;
using System;
using System.Text.Json;
using Contrapoint.Gameplay;
using Contrapoint.Players;

namespace Contrapoint.UI;

// resource/ui/win_round.res and scripts/hudlayout.res, in Source's 480-unit canvas.
public partial class CssRoundPanel : Control
{
	private const string Assets = "res://assets/ui/css/round/";
	private JsonElement _layout, _strings;
	private Font _font = null!;
	private Texture2D _star = null!, _glow = null!, _avatar = null!, _ct = null!, _t = null!;
	private Control _middle = null!;
	private Label _title = null!, _description = null!, _mvp = null!, _fact = null!;
	private TextureRect _portrait = null!, _halo = null!, _badge = null!;
	private float _alpha;
	private DefuseResult _result;
	private PlayerTeam _winner;
	private Godot.Collections.Dictionary? _summary;
	public float Opacity => _alpha;
	public string Title => _title.Text;
	public string Description => _description.Text;
	public string MvpText => _mvp.Text;
	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		using var doc = JsonDocument.Parse(FileAccess.GetFileAsString(Assets + "round.json"));
		_layout = doc.RootElement.GetProperty("layout").Clone();
		_strings = doc.RootElement.GetProperty("strings").GetProperty(OS.GetLocaleLanguage() == "ru" ? "russian" : "english").Clone();
		_font = GD.Load<FontFile>("res://assets/ui/css/scoreboard/verdanab.ttf");
		_star = GD.Load<Texture2D>(Assets + "win_panel_bgstar.png");
		_glow = GD.Load<Texture2D>(Assets + "avatar_glow_64.png");
		_avatar = GD.Load<Texture2D>(Assets + "avatar_default_64.png");
		_ct = GD.Load<Texture2D>("res://assets/ui/css/scoreboard/avatar_default_64.png");
		_t = GD.Load<Texture2D>("res://assets/ui/css/scoreboard/avatar_default-t_64.png");
		_middle = new Control { ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
		AddChild(_middle);
		_middle.Draw += () => {
			var s = Size.X / 220;
			_middle.DrawTextureRect(_star, new Rect2(new Vector2(-5, -10) * s, new Vector2(100, 100) * s), false, new Color(0, 40 / 255f, 50 / 255f, 80 / 255f));
		};
		_title = MakeLabel("WinLabel", 14);
		_description = MakeLabel("WinDescription", 9);
		_halo = Image("MVP_AvatarGlow", _glow, new Color(80 / 255f, 80 / 255f, 80 / 255f));
		_halo.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
		_portrait = Image("MVP_Avatar", _avatar, Colors.White);
		_mvp = MakeLabel("MVP_Text", 9);
		_badge = Image("MVP_Foreground_Star", GD.Load<Texture2D>(Assets + "win_panel_mvpstar.png"), Colors.White);
		_fact = MakeLabel("FunFact", 9);
		Visible = false;
	}
	private string Localize(string key) => _strings.TryGetProperty(key, out var value) ? value.GetString()! : "";
	private Label MakeLabel(string name, int height)
	{
		var label = new Label { Name = name, MouseFilter = MouseFilterEnum.Ignore,
			VerticalAlignment = name == "FunFact" ? VerticalAlignment.Top : VerticalAlignment.Center,
			HorizontalAlignment = name is "WinLabel" or "WinDescription" ? HorizontalAlignment.Center : HorizontalAlignment.Left,
			AutowrapMode = name is "MVP_Text" or "FunFact" ? TextServer.AutowrapMode.WordSmart : TextServer.AutowrapMode.Off,
			ClipText = true };
		label.AddThemeFontOverride("font", _font);
		label.AddThemeColorOverride("font_color", new Color("cccccc"));
		label.SetMeta("height", height);
		AddChild(label); return label;
	}
	private TextureRect Image(string name, Texture2D texture, Color color)
	{
		var node = new TextureRect { Name = name, Texture = texture, Modulate = color,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = MouseFilterEnum.Ignore };
		AddChild(node); return node;
	}
	private Rect2 Field(string name, float scale)
	{
		var f = _layout.GetProperty(name.ToLowerInvariant());
		float N(string key) => float.Parse(f.GetProperty(key).GetString()!, System.Globalization.CultureInfo.InvariantCulture);
		return new Rect2(new Vector2(N("xpos"), N("ypos")) * scale, new Vector2(N("wide"), N("tall")) * scale);
	}
	public void UpdatePanel(DefuseState state, double delta, Vector2 screen)
	{
		var ended = state.Phase is DefusePhase.Ended or DefusePhase.Intermission;
		_alpha = ended ? 1 : Math.Max(0, _alpha - (float)delta / 1.5f);
		Visible = _alpha > 0;
		Modulate = new Color(1, 1, 1, _alpha);
		if (!Visible) return;
		var scale = screen.Y / 480;
		Position = new Vector2(screen.X / 2 - 110 * scale, 287 * scale);
		var newSize = new Vector2(220, 138) * scale;
		if (Size != newSize)
		{
			Size = newSize; QueueRedraw(); _middle.QueueRedraw();
			var middle = Field("MiddlePanel", scale);
			_middle.Position = middle.Position; _middle.Size = middle.Size;
			foreach (var child in GetChildren())
			{
				if (child is not Control control || child == _middle) continue;
				if (control is Label label) label.AddThemeFontSizeOverride("font_size", Math.Max(1, Mathf.RoundToInt(label.GetMeta("height").AsInt32() * scale / 1.129f)));
				var rect = Field(control.Name, scale); control.Position = rect.Position; control.Size = rect.Size;
			}
		}
		if (!ended || _result == state.Result && _winner == state.Winner && ReferenceEquals(_summary, state.RoundSummary)) return;
		_result = state.Result; _winner = state.Winner; _summary = state.RoundSummary;
		_title.Text = Localize(_winner == PlayerTeam.Terrorist ? "winpanel_t_win" : _winner == PlayerTeam.CounterTerrorist ? "winpanel_ct_win" : "winpanel_draw").ToUpperInvariant();
		_title.AddThemeColorOverride("font_color", _winner == PlayerTeam.Terrorist ? new Color("b80000") : _winner == PlayerTeam.CounterTerrorist ? new Color("4798ed") : new Color("cccccc"));
		var token = _result switch { DefuseResult.BombExploded => "target_bombed", DefuseResult.BombDefused => "bomb_defused", DefuseResult.TargetSaved => "target_saved", DefuseResult.TerroristsEliminated => "cts_win", DefuseResult.CounterTerroristsEliminated => "terrorists__kill", _ => "" };
		_description.Text = Localize("winpanel_end_" + token).ToUpperInvariant();
		string Value(string key) => _summary.TryGetValue(key, out var v) ? v.ToString() : "";
		var name = Value("mvp");
		_mvp.Text = name.Length > 0 ? Localize(Value("reason")).Replace("%s1", name) : "";
		_portrait.Visible = _halo.Visible = _badge.Visible = name.Length > 0;
		_portrait.Texture = _winner == PlayerTeam.Terrorist ? _t : _ct;
		_fact.Text = Localize(Value("fact")).Replace("%s1", Value("player")).Replace("%s2", Value("value"));
	}
	public override void _Draw()
	{
		var s = Size.X / 220;
		var panel = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 212 / 255f) };
		panel.SetCornerRadiusAll(Mathf.RoundToInt(8 * s));
		DrawStyleBox(panel, new Rect2(Vector2.Zero, Size));
		panel.BgColor = new Color(80 / 255f, 80 / 255f, 80 / 255f, 160 / 255f);
		panel.CornerRadiusBottomLeft = panel.CornerRadiusBottomRight = 0;
		DrawStyleBox(panel, Field("UpperPanel", s));
		panel.SetCornerRadiusAll(0); DrawStyleBox(panel, Field("MiddlePanel", s));
		panel.CornerRadiusBottomLeft = panel.CornerRadiusBottomRight = Mathf.RoundToInt(8 * s);
		DrawStyleBox(panel, Field("LowerPanel", s));
	}
}
