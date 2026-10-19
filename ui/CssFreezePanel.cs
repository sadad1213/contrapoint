using Godot;
using System;
using System.Globalization;
using System.Collections.Generic;
using System.Text.Json;
using Contrapoint.Players;

namespace Contrapoint.UI;

// Coordinates, typefaces, colors and localized strings: installed CSS
// FreezePanel_Basic.res/ClientScheme. Rounded pixels and health gauge follow
// backgroundpanel.cpp and cs_hud_freezepanel.cpp, including fixed 10px corners.
public partial class CssFreezePanel : Control
{
	private JsonElement _definition, _layout;
	private FontFile _font = null!;
	private readonly Dictionary<string, Control> _controls = new();
	private float _scale, _health;
	private Vector2I _screenSize;
	private Control _gauge = null!;
	private static float Number(JsonElement data, string key) => float.Parse(data.GetProperty(key).GetString()!, CultureInfo.InvariantCulture);
	private static Color ColorValue(JsonElement data, string key)
	{
		var values = data.GetProperty(key).GetString()!.Split(' ');
		return new(float.Parse(values[0]) / 255, float.Parse(values[1]) / 255, float.Parse(values[2]) / 255, float.Parse(values[3]) / 255);
	}
	public void Initialize()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		_definition = JsonDocument.Parse(FileAccess.GetFileAsString("res://assets/ui/css/death/freezepanel.json")).RootElement.Clone();
		_layout = _definition.GetProperty("panel");
		_font = GD.Load<FontFile>("res://assets/ui/css/death/verdanab.ttf");
		foreach (var item in _layout.EnumerateObject())
		{
			if (item.Value.ValueKind != JsonValueKind.Object) continue;
			var data = item.Value;
			Control child;
			switch (data.GetProperty("controlname").GetString())
			{
				case "Label":
					var label = new Label { HorizontalAlignment = HorizontalAlignment.Center, ClipText = true,
						VerticalAlignment = data.GetProperty("textalignment").GetString() switch
						{ "south" => VerticalAlignment.Bottom, "north" => VerticalAlignment.Top, _ => VerticalAlignment.Center } };
					label.AddThemeFontOverride("font", _font);
					label.AddThemeColorOverride("font_color", ColorValue(data, "fgcolor_override"));
					label.AddThemeConstantOverride("outline_size", 0); label.AddThemeConstantOverride("shadow_offset_x", 0); label.AddThemeConstantOverride("shadow_offset_y", 0);
					child = label; break;
				case "ImagePanel":
					var texture = data.GetProperty("image").GetString()!.Split('/')[^1];
					child = Image(texture);
					if (item.Name == "avatarglow")
					{
						child.Modulate = ColorValue(data, "drawcolor");
						child.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
					}
					break;
				case "CAvatarImagePanel": child = Image("avatar_default-t_64"); break;
				case "HorizontalGauge":
					_gauge = new Control(); _gauge.Draw += DrawHealth; child = _gauge; break;
				default: continue;
			}
			child.Name = item.Name; child.MouseFilter = MouseFilterEnum.Ignore;
			child.ZIndex = (int)Number(data, "zpos");
			AddChild(child); _controls[item.Name] = child;
		}
		// Ordinary kills hide the domination icon, as in CSS player_death.
		_controls["dominationicon"].Visible = false;
		SetText("infolabel1", Localized("freezepanel_killer1"));
		SetText("infolabel2", Localized("freezepanel_killer2"));
		SetText("screenshotlabel", Localized("freezepanel_screenshot"));
		UpdateLayout();
	}
	private static TextureRect Image(string texture) => new() { Texture = GD.Load<Texture2D>("res://assets/ui/css/death/" + texture + ".png"),
		ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale };
	private string Localized(string key) => _definition.GetProperty("strings").GetProperty("russian").GetProperty(key).GetString()!;
	private void SetText(string control, string text) => ((Label)_controls[control]).Text = text;
	public void SetKiller(Player killer)
	{
		SetText("killername", killer.Name);
		_health = killer.IsAlive ? Mathf.Clamp(killer.Health / (float)Math.Max(1, killer.MaxHealth), 0, 1) : 0;
		((TextureRect)_controls["avatarimage"]).Texture = GD.Load<Texture2D>("res://assets/ui/css/death/" +
			(killer.Team == PlayerTeam.Terrorist ? "avatar_default-t_64" : "avatar_default_64") + ".png");
		_gauge.QueueRedraw();
	}
	private Vector2 Point(float x, float y) => new((int)(x * _scale), (int)(y * _scale));
	private void UpdateLayout()
	{
		_screenSize = GetWindow().Size; var screen = (Vector2)_screenSize; _scale = screen.Y / 480f;
		var x = _layout.GetProperty("xpos").GetString()!;
		Position = new((int)(screen.X / 2) + (int)(float.Parse(x[1..], CultureInfo.InvariantCulture) * _scale), (int)(Number(_layout, "ypos") * _scale));
		Size = Point(Number(_layout, "wide"), Number(_layout, "tall"));
		foreach (var (name, child) in _controls)
		{
			var data = _layout.GetProperty(name);
			if (child is Label label)
			{
				var font = _definition.GetProperty("fonts").GetProperty(data.GetProperty("font").GetString()!.ToLowerInvariant());
				label.AddThemeFontSizeOverride("font_size", Math.Max(1, (int)(Number(font, "tall") * _scale * _definition.GetProperty("font_cell_to_em").GetSingle())));
			}
			child.Position = Point(Number(data, "xpos"), Number(data, "ypos"));
			child.Size = Point(Number(data, "wide"), Number(data, "tall"));
		}
		QueueRedraw(); _gauge.QueueRedraw();
	}
	public override void _Process(double delta) { if (_screenSize != GetWindow().Size) UpdateLayout(); }
	private void Rect(float x1, float y1, float x2, float y2, Color color)
	{
		if (x2 > x1 && y2 > y1) DrawRect(new(x1, y1, x2 - x1, y2 - y1), color);
	}
	public override void _Draw()
	{
		if (_font == null) return;
		var background = ColorValue(_layout, "bgcolor_override"); var border = ColorValue(_layout, "fgcolor_override");
		var w = Size.X; var h = Size.Y;
		int[] coord = { 0, 1, 2, 3, 4, 6, 9, 10 };
		Rect(0, 10, 10, h - 10, background); Rect(10, 0, w - 10, h, background); Rect(w - 10, 10, w, h - 10, background);
		for (var i = 0; i < 7; i++)
		{
			var a = coord[i]; var b = coord[i + 1]; var c = coord[6 - i]; var d = coord[7 - i];
			Rect(a, d, b, 10, background); Rect(w - b, d, w - a, 10, background);
			Rect(a, h - 10, b, h - d, background); Rect(w - b, h - 10, w - a, h - d, background);
			Rect(a, c, b, d, border); Rect(w - b, c, w - a, d, border);
			Rect(a, h - d, b, h - c, border); Rect(w - b, h - d, w - a, h - c, border);
		}
		Rect(10, 0, w - 10, 1, border); Rect(10, h - 1, w - 10, h, border);
		Rect(0, 10, 1, h - 10, border); Rect(w - 1, 10, w, h - 10, border);
	}
	private void DrawHealth()
	{
		var size = _gauge.Size;
		_gauge.DrawRect(new(Vector2.Zero, size), new(0, 0, 0, 128 / 255f));
		_gauge.DrawRect(new(Vector2.Zero, size), Colors.Black, false, 1);
		var width = (int)((size.X - 1) * _health) - 1;
		if (width > 0) _gauge.DrawRect(new(1, 1, width, size.Y - 2), new Color(192 / 255f, 32 / 255f, 32 / 255f).Lerp(new(32 / 255f, 1, 32 / 255f), _health));
	}
}
