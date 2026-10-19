using Godot;
using System;
using Contrapoint.Players;
using Contrapoint.Weapons;

namespace Contrapoint.UI;

// Stock scripts/hudlayout.res, resource/clientscheme.res and mod_textures.txt.
// Source uses height / 480 for proportional coordinates, also on widescreen.
public partial class CssHud : Control
{
	public bool Enabled { get; set; } = true;
	public HudSnapshot Snapshot { get; private set; }
	public CssRadar Radar { get; private set; } = null!;
	private Player _player = null!;
	private FontFile _font = null!, _grenadeFont = null!;
	private Texture2D _ammoAtlas = null!;
	private Control _ink = null!;
	private CanvasLayer _layer = null!;
	private HudSnapshot _drawnSnapshot;
	private WeaponView? _drawnWeapon;
	private Vector2 _drawnSize;
	private bool _hasDrawing, _wasPulsing;
	private float _scale, _width;
	private int _fontSize;
	private int _oldHealth = 100;
	private double _damageAge = 10;
	private static readonly Color Amber = new(1, 176 / 255f, 0, 120 / 255f);
	private static readonly Color Red = new(160 / 255f, 0, 0, 1);
	private readonly StyleBoxFlat _panel = new() { BgColor = new(0, 0, 0, 96 / 255f) };

	public override void _Ready()
	{
		_player = GetParent().GetParent<Player>();
		_layer = GetParent<CanvasLayer>();
		MouseFilter = MouseFilterEnum.Ignore;
		_font = GD.Load<FontFile>("res://assets/ui/css/hud/cstrike.ttf");
		_grenadeFont = GD.Load<FontFile>("res://assets/ui/css/hud/cs.ttf");
		_ammoAtlas = GD.Load<Texture2D>("res://assets/ui/css/hud/640hud1.png");
		// Source marks its HUD fonts and sprite atlas as additive. Keep the
		// translucent panel backgrounds on the normal blend canvas.
		_ink = new Control
		{
			Name = "Foreground", MouseFilter = MouseFilterEnum.Ignore,
			Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add }
		};
		AddChild(_ink);
		_ink.Draw += DrawForeground;
		Radar = new CssRadar { Name = "Radar" };
		AddChild(Radar);
		Radar.Bind(_player);
	}

	public override void _Process(double delta)
	{
		// Crosshair and status HUD share this native-pixel canvas layer.
		var canvasTransform = GetViewport().GetStretchTransform().AffineInverse();
		if (_layer.Transform != canvasTransform) _layer.Transform = canvasTransform;
		Size = GetWindow().Size;
		var subject = _player.HudPlayer;
		Visible = Enabled && _player.LocalInput && subject.IsAlive && subject.Team is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist;
		Snapshot = HudSnapshot.Capture(subject);
		if (subject != _player) Snapshot = Snapshot with { CanBuy = false };
		if (Snapshot.Health < _oldHealth) _damageAge = 0;
		_oldHealth = Snapshot.Health;
		_damageAge += delta;
		_scale = Size.Y / 480f;
		_width = Size.X / _scale;
		// GDI's positive font height includes the Windows font cell (1129 units/em).
		_fontSize = Math.Max(1, Mathf.RoundToInt(28 * _scale / 1.129f));
		Radar.Visible = Visible && Radar.Enabled && Radar.HasOverview;
		if (!Visible) return;
		// Godot retains draw commands. Rebuild text and panels only when their
		// contents change; animated warning colours still redraw every frame.
		var changed = !_hasDrawing || Snapshot != _drawnSnapshot || Size != _drawnSize || subject.ActiveWeapon != _drawnWeapon;
		var pulsing = Snapshot.Health <= 25 || _damageAge < 1.1 || Snapshot.TimerRunning && Snapshot.Seconds <= 30;
		if (changed) QueueRedraw();
		if (changed || pulsing || _wasPulsing) _ink.QueueRedraw();
		_drawnSnapshot = Snapshot; _drawnSize = Size; _drawnWeapon = subject.ActiveWeapon;
		_hasDrawing = true; _wasPulsing = pulsing;
	}

	private Vector2 Point(float x, float y) => new(Mathf.Round(x * _scale), Mathf.Round(y * _scale));
	private void Text(float x, float top, string value, Color color, Font? font = null)
	{
		font ??= _font;
		_ink.DrawString(font, Point(x, top) + new Vector2(0, font.GetAscent(_fontSize)), value,
			fontSize: _fontSize, modulate: color);
	}
	private float TextWidth(string value) => _font.GetStringSize(value, fontSize: _fontSize).X / _scale;
	private void Number(float x, float top, int value, Color color)
	{
		var text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
		Text(x + Math.Max(0, 3 - text.Length) * TextWidth("0"), top, text, color);
	}
	private void Panel(float x, float y, float width, float height)
	{
		var radius = Mathf.RoundToInt(4 * _scale);
		_panel.SetCornerRadiusAll(radius);
		DrawStyleBox(_panel, new Rect2(Point(x, y), Point(width, height)));
	}
	public override void _Draw()
	{
		if (_font == null || _scale <= 0) return;
		Panel(8, 446, 80, 25);
		Panel(148, 446, 80, 25);
		Panel(_width / 2 - 28, 446, 98, 25);
		if (Snapshot.HasAmmo) Panel(_width - 157, 446, 142, 25);
	}

	private void DrawForeground()
	{
		if (_font == null || _scale <= 0) return;
		var hpColor = Amber;
		if (Snapshot.Health <= 25 || _damageAge < 1.1)
		{
			var pulse = (float)(.5 + .5 * Math.Cos(_damageAge * Math.PI * 8));
			hpColor = Amber.Lerp(Red, pulse);
		}
		Text(16, 442, "b", hpColor);
		Number(43, 442, Snapshot.Health, hpColor);
		Text(156, 442, Snapshot.Helmet && Snapshot.Armor > 0 ? "l" : "a", Amber);
		Number(182, 442, Snapshot.Armor, Amber);
		var timerX = _width / 2 - 28;
		var clockColor = Snapshot.TimerRunning && Snapshot.Seconds <= 30
			? Amber.Lerp(Red, (float)(.5 + .5 * Math.Cos(Time.GetTicksMsec() / 160.0))) : Amber;
		Text(timerX + 8, 442, "e", clockColor);
		Text(timerX + 42, 442, Snapshot.Clock, clockColor);
		// Money has its stock transparent background and a right-aligned value.
		Text(_width - 114, 410, "$", Amber);
		var money = Snapshot.Money.ToString(System.Globalization.CultureInfo.InvariantCulture);
		Text(_width - 23 - TextWidth(money), 410, money, Amber);
		// HudShoppingCart: stock Icons/cstrike.ttf 'c', green, at (64, 240).
		if (Snapshot.CanBuy) Text(64, 240, "c", new Color(0, 160 / 255f, 0, 1));
		if (!Snapshot.HasAmmo) return; // CSS hides ammo completely for knives.
		var ammoX = _width - 157;
		Number(ammoX + (Snapshot.UsesMagazine ? 8 : 63), 442, Snapshot.Magazine, Amber);
		if (Snapshot.UsesMagazine)
		{
			_ink.DrawRect(new Rect2(Point(ammoX + 53, 449), Point(2, 20)), Amber);
			Number(ammoX + 63, 442, Snapshot.Reserve, Amber);
		}
		DrawAmmoIcon(ammoX + 110, 448);
	}

	private void DrawAmmoIcon(float x, float y)
	{
		if (_player.HudPlayer.ActiveWeapon is GrenadeView grenade)
		{
			Text(x, y - 6, grenade.Kind switch { GrenadeKind.HE => "h", GrenadeKind.Flashbang => "g", _ => "i" }, Amber, _grenadeFont);
			return;
		}
		if (_player.HudPlayer.ActiveWeapon is not FirearmView gun) return;
		var region = gun.AmmoType switch
		{
			AmmunitionType.Acp45 => new Rect2(182, 0, 26, 24),
			AmmunitionType.Ae50 => new Rect2(182, 48, 26, 26),
			AmmunitionType.Nato556 => new Rect2(157, 74, 25, 24),
			AmmunitionType.Magnum338 => new Rect2(182, 74, 26, 24),
			_ => new Rect2(232, 48, 24, 26)
		};
		_ink.DrawTextureRectRegion(_ammoAtlas, new Rect2(Point(x, y), Point(region.Size.X * 23 / region.Size.Y, 23)), region, Amber);
	}
}
