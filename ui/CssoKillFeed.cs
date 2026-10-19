using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Contrapoint.Players;

namespace Contrapoint.UI;

// Installed CSSO HudDeathNotice: 1080p proportional base, Stratum2 Bold,
// original SVGs/corner masks and layout colors. Lives above the death frame
// so notices continue to arrive and expire while the camera is frozen.
public partial class CssoKillFeed : CanvasLayer
{
	public sealed class Entry(KillNotice notice)
	{
		public KillNotice Notice { get; } = notice;
		public double Age { get; set; }
	}
	public IReadOnlyList<Entry> Entries => _entries;
	public float Duration { get; set; } = 6;
	public float UiScale { get; set; } = 1.25f;
	public bool CaptureSuppressed { get; set; }
	private readonly List<Entry> _entries = new();
	private readonly Dictionary<string, Texture2D> _icons = new();
	private readonly Texture2D[] _corners = new Texture2D[4];
	private Player _player = null!;
	private Control _ink = null!;
	private FontFile _font = null!;
	private JsonElement _definition, _layout;
	private float _scale;
	private int _fontSize;
	private const string Root = "res://assets/ui/csso/killfeed/";
	private float Number(string key) => float.Parse(_layout.GetProperty(key).GetString()!, CultureInfo.InvariantCulture);
	private Color ColorValue(string key)
	{
		var values = _layout.GetProperty(key).GetString()!.Split(' ').Select(s => float.Parse(s, CultureInfo.InvariantCulture) / 255).ToArray();
		return new(values[0], values[1], values[2], values[3]);
	}
	public void Bind(Player player)
	{
		_player = player; Layer = 7;
		_definition = JsonDocument.Parse(FileAccess.GetFileAsString(Root + "layout.json")).RootElement.Clone();
		_layout = _definition.GetProperty("layout");
		_font = GD.Load<FontFile>(Root + "stratum2-bold.ttf");
		foreach (var name in new[] { "ak47", "m4a1_silencer", "m4a1_silencer_off", "usp_silencer", "usp_silencer_off", "deagle", "awp", "ssg08",
			"aug", "elite", "famas", "fiveseven", "g3sg1", "galilar", "glock18", "m249", "nova", "mac10", "mp5sd",
			"p250", "p90", "scar20", "sg553", "mp9", "ump45", "xm1014",
			"knife", "knife_t", "hegrenade", "suicide", "headshot", "penetrate", "noscope", "blind", "thrusmoke", "inair" })
			_icons[name] = GD.Load<Texture2D>(Root + name + ".svg");
		for (var i = 0; i < 4; i++) _corners[i] = GD.Load<Texture2D>(Root + $"deathnotice_corner{i + 1}.png");
		_ink = new Control { Name = "HudDeathNotice", MouseFilter = Control.MouseFilterEnum.Ignore };
		AddChild(_ink); _ink.Draw += Paint;
	}
	public void Add(KillNotice notice)
	{
		if (Duration <= 0) return;
		_entries.Add(new(notice));
		while (_entries.Count > (int)Number("entry_limit")) _entries.RemoveAt(0);
		_ink.QueueRedraw();
	}
	public void Clear() { _entries.Clear(); _ink.QueueRedraw(); }
	public override void _Process(double delta)
	{
		foreach (var entry in _entries) entry.Age += delta;
		_entries.RemoveAll(e => e.Age >= Duration);
		Visible = !CaptureSuppressed && _player.LocalInput && _player.GetNode<CssHud>("HUD/Status").Enabled;
		Transform = GetViewport().GetStretchTransform().AffineInverse();
		_scale = GetWindow().Size.Y / (float)_definition.GetProperty("base_height").GetInt32() * UiScale;
		var fontHeight = float.Parse(_definition.GetProperty("font").GetProperty("tall").GetString()!, CultureInfo.InvariantCulture);
		_fontSize = Math.Max(1, (int)(fontHeight * _scale * _definition.GetProperty("font_cell_to_em").GetSingle()));
		_ink.QueueRedraw();
	}
	private Color TeamColor(PlayerTeam team) => team switch
	{
		PlayerTeam.Terrorist => ColorValue("name_t_fgcolor"), PlayerTeam.CounterTerrorist => ColorValue("name_ct_fgcolor"), _ => Colors.White
	};
	private static string CleanName(string name) => string.Concat(name.Where(c => !char.IsControl(c)).Take(32));
	private float Width(string text) => _font.GetStringSize(text, fontSize: _fontSize).X;
	private float IconWidth(string icon, float height) => height * _icons[icon].GetWidth() / _icons[icon].GetHeight();
	private void Icon(string icon, Rect2 rect, Color color) => _ink.DrawTextureRect(_icons[icon], rect, false, color);
	private void Background(Rect2 rect, Color color)
	{
		var radius = Mathf.Max(1, Number("roundedcornerradius1") * _scale);
		_ink.DrawRect(new(rect.Position + new Vector2(radius, 0), rect.Size - new Vector2(radius * 2, 0)), color);
		_ink.DrawRect(new(rect.Position + new Vector2(0, radius), new(radius, rect.Size.Y - radius * 2)), color);
		_ink.DrawRect(new(rect.Position + new Vector2(rect.Size.X - radius, radius), new(radius, rect.Size.Y - radius * 2)), color);
		Vector2[] points = [rect.Position, rect.Position + new Vector2(rect.Size.X - radius, 0),
			rect.End - new Vector2(radius, radius), rect.Position + new Vector2(0, rect.Size.Y - radius)];
		for (var i = 0; i < 4; i++) _ink.DrawTextureRect(_corners[i], new(points[i], new(radius, radius)), false, color);
	}
	private void Paint()
	{
		if (_scale <= 0) return;
		var gap = Number("entry_margin") * _scale; var rowHeight = Number("entry_tall") * _scale;
		var iconHeight = Number("icon_tall") * _scale; var padding = Number("entry_wide") * _scale / 2;
		var y = Number("entry_ypos") * _scale;
		foreach (var entry in _entries)
		{
			var notice = entry.Notice;
			var killer = CleanName(notice.KillerName); var victim = CleanName(notice.VictimName);
			var weapon = _icons.ContainsKey(notice.Weapon) ? notice.Weapon : "suicide";
			var tokens = new List<string>();
			if (notice.Flags.HasFlag(KillFlags.Blind)) tokens.Add("blind");
			if (notice.Flags.HasFlag(KillFlags.NoScope)) tokens.Add("noscope");
			tokens.Add(weapon);
			if (notice.Flags.HasFlag(KillFlags.Penetrated)) tokens.Add("penetrate");
			if (notice.Flags.HasFlag(KillFlags.Headshot)) tokens.Add("headshot");
			if (notice.Flags.HasFlag(KillFlags.ThroughSmoke)) tokens.Add("thrusmoke");
			var width = padding * 2 + Width(victim) + tokens.Sum(t => IconWidth(t, iconHeight) + gap);
			if (killer.Length > 0) width += Width(killer) + gap;
			var x = Number("right_side") != 0 ? GetWindow().Size.X - Number("entry_xpos") * _scale - width : Number("entry_xpos") * _scale;
			var rect = new Rect2(new(Mathf.Round(x), Mathf.Round(y)), new(Mathf.Ceil(width), Mathf.Ceil(rowHeight)));
			var alpha = Mathf.Clamp((float)(Duration - entry.Age) / .5f, 0, 1);
			Color Fade(Color color) => new(color.R, color.G, color.B, color.A * alpha);
			var ownDeath = notice.VictimId == KillNotice.Identity(_player); var ownKill = notice.KillerId == KillNotice.Identity(_player);
			Background(rect, Fade(ColorValue(ownDeath ? "entry_death_bgcolor" : "entry_bgcolor")));
			if (ownKill)
			{
				var border = new StyleBoxFlat { BgColor = Colors.Transparent, BorderColor = Fade(ColorValue("entry_kill_border_color")), DrawCenter = false };
				border.SetBorderWidthAll(Math.Max(1, (int)(Number("entry_kill_border_thick") * _scale)));
				border.SetCornerRadiusAll(Math.Max(1, (int)(Number("roundedcornerradius1") * _scale)));
				_ink.DrawStyleBox(border, rect); border.Dispose();
			}
			var baseline = rect.Position.Y + (rowHeight - _font.GetHeight(_fontSize)) / 2 + _font.GetAscent(_fontSize);
			var cursor = rect.Position.X + padding;
			if (killer.Length > 0)
			{
				_ink.DrawString(_font, new(cursor, baseline), killer, fontSize: _fontSize, modulate: Fade(TeamColor(notice.KillerTeam)));
				cursor += Width(killer) + gap;
			}
			foreach (var icon in tokens)
			{
				var iconWidth = IconWidth(icon, iconHeight);
				Icon(icon, new(cursor, rect.Position.Y + (rowHeight - iconHeight) / 2, iconWidth, iconHeight), Fade(Colors.White));
				if (icon == weapon && notice.Flags.HasFlag(KillFlags.InAir))
				{
					var height = iconHeight / 2;
					Icon("inair", new(cursor + Number("icon_inair_xpos") * _scale, rect.Position.Y + Number("icon_inair_ypos") * _scale,
						IconWidth("inair", height), height), Fade(Colors.White));
				}
				cursor += iconWidth + gap;
			}
			_ink.DrawString(_font, new(cursor, baseline), victim, fontSize: _fontSize, modulate: Fade(TeamColor(notice.VictimTeam)));
			y += rowHeight + gap;
		}
	}
}
