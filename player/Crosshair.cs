using Godot;
using Contrapoint.UI;
using Contrapoint.Weapons;

namespace Contrapoint.Players;

public partial class Crosshair : Control
{
	// CSSO cfg/config.cfg + cfg/migrated_settings.cfg: classic static (4),
	// custom RGB 0/255/0, alpha 255, size 2, thickness 1, gap -3.75,
	// no dot/outline/recoil or weapon-dependent gap. Size/thickness use 480p units.
	[Export] public Color CrosshairColor { get; set; } = new(0, 1, 0, 1);
	[Export] public float ArmLength { get; set; } = 2;
	[Export] public float Thickness { get; set; } = 1;
	[Export] public float Gap { get; set; } = -3.75f;
	public bool Enabled { get; set; } = true;
	public bool Dot { get; set; }
	public bool Outline { get; set; }
	public bool UseAlpha { get; set; } = true;
	public int ColorPreset { get; set; } = 5;
	private Player _player = null!;
	private CanvasLayer _layer = null!;
	private CssHud _hud = null!;
	private readonly record struct DrawState(Vector2 Size, Color Color, float Length, float Thickness, float Gap, bool Dot, bool Outline, bool Alpha, int Preset);
	private DrawState? _drawState;

	public override void _Ready()
	{
		_player = GetParent().GetParent<Player>();
		_layer = GetParent<CanvasLayer>();
		_hud = _player.GetNode<CssHud>("HUD/Status");
		Visible = _player.LocalInput;
		MouseFilter = MouseFilterEnum.Ignore;
		Resized += QueueRedraw;
		SyncResolution();
	}

	public override void _Process(double delta)
	{
		var subject = _player.HudPlayer;
		Visible = Enabled && _player.LocalInput && subject.IsAlive && subject.Team is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist
			&& _hud.Enabled && subject.ActiveWeapon is not FirearmView { IsSniper: true };
		SyncResolution();
		var state = new DrawState(Size, CrosshairColor, ArmLength, Thickness, Gap, Dot, Outline, UseAlpha, ColorPreset);
		if (state != _drawState) { _drawState = state; QueueRedraw(); }
	}

	private void SyncResolution()
	{
		// Draw at native pixel resolution, with Source's height-based scaling,
		// independently of the project's canvas_items UI stretch.
		var canvasTransform = GetViewport().GetStretchTransform().AffineInverse();
		if (_layer.Transform != canvasTransform) _layer.Transform = canvasTransform;
		Size = (Vector2)GetWindow().Size;
	}

	public override void _Draw()
	{
		var center = (Size * 0.5f).Floor();
		var scale = Size.Y / 480f;
		var length = Mathf.Max(0, Mathf.RoundToInt(ArmLength * scale));
		var thickness = Mathf.Max(1, Mathf.RoundToInt(Thickness * scale));
		var distance = (int)(4 + Gap);
		var half = thickness / 2;
		var near = distance + half;
		var far = distance + thickness - half;
		var color = ColorPreset switch { 0 => Colors.Red, 1 => Colors.Green, 2 => Colors.Yellow, 3 => Colors.Blue, 4 => Colors.Cyan, _ => CrosshairColor };
		color.A = UseAlpha ? CrosshairColor.A : 1;
		void Arm(Rect2 rect)
		{
			if (Outline) DrawRect(rect.Grow(1), new Color(0, 0, 0, color.A));
			DrawRect(rect, color);
		}
		Arm(new Rect2(center + new Vector2(-half, -near - length), new(thickness, length)));
		Arm(new Rect2(center + new Vector2(-half, far), new(thickness, length)));
		Arm(new Rect2(center + new Vector2(-near - length, -half), new(length, thickness)));
		Arm(new Rect2(center + new Vector2(far, -half), new(length, thickness)));
		if (Dot) Arm(new Rect2(center - new Vector2(half, half), new(thickness, thickness)));
	}
}
