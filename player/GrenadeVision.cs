using Godot;
using Contrapoint.Weapons;

namespace Contrapoint.Players;

public partial class GrenadeVision : CanvasLayer
{
	public double FlashRemaining => System.Math.Max(0, _hold + _fade - _elapsed);
	private Player _player = null!;
	private ColorRect _white = null!, _fog = null!;
	private TextureRect _afterimage = null!;
	private ImageTexture? _capturedFrame;
	private bool _capturePending;
	private double _hold, _fade, _elapsed;
	private float _alpha;

	public override void _Ready()
	{
		_player = GetParent<Player>();
		Layer = 5; // Cover the weapon viewport and HUD, below menus/console.
		_fog = Overlay(new Color(0.3f, 0.3f, 0.3f, 0));
		_afterimage = new TextureRect
		{
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			Modulate = new Color(1, 1, 1, 0)
		};
		AddChild(_afterimage);
		_afterimage.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_white = Overlay(new Color(1, 1, 1, 0));
		Visible = _player.LocalInput;
	}

	private ColorRect Overlay(Color color)
	{
		var rect = new ColorRect { Color = color, MouseFilter = Control.MouseFilterEnum.Ignore };
		AddChild(rect);
		rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		return rect;
	}

	public void Flash(float strength, float facing)
	{
		var fade = strength * (facing >= 0.5f ? 2.5f : facing >= -0.5f ? 1.75f : 1);
		var hold = strength * (facing >= 0.5f ? 1.25f : 1 / 3.5f);
		if (hold + fade < FlashRemaining) return;
		_hold = hold;
		_fade = fade;
		_alpha = facing >= -0.5f ? 1 : 200 / 255f;
		_elapsed = 0;
		if (_player.LocalInput && !_capturePending && DisplayServer.GetName() != "headless")
		{
			_capturePending = true;
			RenderingServer.FramePostDraw += CaptureFrame;
		}
	}

	private void CaptureFrame()
	{
		RenderingServer.FramePostDraw -= CaptureFrame;
		_capturePending = false;
		using var image = GetViewport().GetTexture().GetImage();
		_afterimage.Texture = null;
		_capturedFrame?.Dispose();
		_capturedFrame = ImageTexture.CreateFromImage(image);
		_afterimage.Texture = _capturedFrame;
	}

	public void Reset()
	{
		_hold = _fade = _elapsed = 0;
		_white.Color = new Color(1, 1, 1, 0);
		_fog.Color = new Color(0.3f, 0.3f, 0.3f, 0);
		_afterimage.Modulate = new Color(1, 1, 1, 0);
		_afterimage.Texture = null;
		_capturedFrame?.Dispose();
		_capturedFrame = null;
		if (_capturePending) RenderingServer.FramePostDraw -= CaptureFrame;
		_capturePending = false;
	}

	public override void _Process(double delta)
	{
		_elapsed += delta;
		if (!_player.LocalInput) return;
		var alpha = _elapsed < _hold ? _alpha : _fade > 0 ? _alpha * Mathf.Clamp(1 - (float)((_elapsed - _hold) / _fade), 0, 1) : 0;
		_white.Color = new Color(1, 1, 1, _capturePending ? 0 : alpha);
		_afterimage.Modulate = new Color(1, 1, 1, _capturePending ? 0 : alpha);
		var smoke = 0f;
		foreach (var node in GetTree().GetNodesInGroup("source_smoke"))
			if (node is GrenadeSmoke cloud) smoke += cloud.EyeDensity(_player.Camera.GlobalPosition);
		_fog.Color = new Color(0.3f, 0.3f, 0.3f, Mathf.Clamp(smoke, 0, 1));
		Visible = _player.LocalInput && _player.IsAlive;
	}

	public override void _ExitTree()
	{
		if (_capturePending) RenderingServer.FramePostDraw -= CaptureFrame;
		_afterimage.Texture = null;
		_capturedFrame?.Dispose();
	}
}
