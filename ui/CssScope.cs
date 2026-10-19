using Godot;
using Contrapoint.Players;
using Contrapoint.Weapons;

namespace Contrapoint.UI;

// CS:S CHudScope::Paint: original quarter-circle mask, lens and pixel reticle.
// https://github.com/nillerusr/source-engine/blob/master/game/client/cstrike/cs_hud_scope.cpp
public partial class CssScope : Control
{
	private Player _player = null!;
	private CssHud _hud = null!;
	private Texture2D _arc = null!, _lens = null!;
	private Vector2 _drawnSize;

	public override void _Ready()
	{
		_player = GetParent().GetParent<Player>();
		_hud = _player.GetNode<CssHud>("HUD/Status");
		_arc = GD.Load<Texture2D>("res://assets/ui/css/scope/scope_arc.png");
		_lens = GD.Load<Texture2D>("res://assets/ui/css/scope/scope_lens.png");
		MouseFilter = MouseFilterEnum.Ignore;
		TextureFilter = TextureFilterEnum.Linear;
		ProcessPriority = 110;
		Visible = false;
	}

	public override void _Process(double delta)
	{
		Size = GetWindow().Size; // HUD canvas uses native pixels (see CssHud).
		Visible = _player.LocalInput && _player.IsAlive && !_player.IsThirdPerson && _hud.Enabled
			&& _player.Team is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist
			&& _player.ActiveWeapon is FirearmView { ZoomLevel: > 0, IsSniper: true };
		if (Size != _drawnSize) { _drawnSize = Size; QueueRedraw(); }
	}

	private void Quad(Texture2D texture, float left, float top, float right, float bottom,
		float uLeft, float vTop, float uRight, float vBottom, Color color)
	{
		DrawPolygon([new(left, top), new(right, top), new(right, bottom), new(left, bottom)],
			[color, color, color, color],
			[new(uLeft, vTop), new(uRight, vTop), new(uRight, vBottom), new(uLeft, vBottom)], texture);
	}

	public override void _Draw()
	{
		if (_arc == null || _lens == null) return;
		int width = (int)Size.X, height = (int)Size.Y;
		int inset = height / 16, x1 = (width - height) / 2 + inset;
		int x2 = width - x1, y2 = height - inset, x = width / 2, y = height / 2;
		const float uv1 = .5f / 256, uv2 = 1 - uv1;
		// Source maps the dust texture over the whole screen with inverted V.
		Quad(_lens, 0, 0, x * 2, y * 2, uv1, uv2, uv2, uv1, Colors.White);
		DrawRect(new Rect2(0, y, width, 1), Colors.Black);
		DrawRect(new Rect2(x, 0, 1, height), Colors.Black);
		Quad(_arc, x, y, x2, y2, uv1, uv1, uv2, uv2, Colors.Black);
		Quad(_arc, x - 1, inset, x2, y + 1, uv1, uv2, uv2, uv1, Colors.Black);
		Quad(_arc, x1, y, x, y2, uv2, uv1, uv1, uv2, Colors.Black);
		Quad(_arc, x1, inset, x, y, uv2, uv2, uv1, uv1, Colors.Black);
		DrawRect(new Rect2(0, 0, width, inset), Colors.Black);
		DrawRect(new Rect2(0, y2, width, height - y2), Colors.Black);
		if (x1 > 0) DrawRect(new Rect2(0, inset, x1, height - inset), Colors.Black);
		if (x2 < width) DrawRect(new Rect2(x2, inset, width - x2, height - inset), Colors.Black);
	}
}
