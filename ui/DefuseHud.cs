using Godot;
using System;
using Contrapoint.Gameplay;
using Contrapoint.Players;

namespace Contrapoint.UI;

public partial class DefuseHud : CanvasLayer
{
	private Player _player = null!;
	private Control _ink = null!;
	private Font _font = null!, _icons = null!;
	private CssRoundPanel _roundPanel = null!;
	public void Bind(Player player)
	{
		_player = player; Layer = 13;
		_font = GD.Load<FontFile>("res://assets/ui/css/team/verdana.ttf");
		_icons = GD.Load<FontFile>("res://assets/ui/css/hud/cstrike.ttf");
		_ink = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
		AddChild(_ink); _ink.Draw += Draw;
		_roundPanel = new CssRoundPanel { Name = "RoundPanel" };
		AddChild(_roundPanel);
	}
	public override void _Process(double delta)
	{
		Transform = GetViewport().GetStretchTransform().AffineInverse();
		Visible = DefuseRules.Active && _player.GetNode<CssHud>("HUD/Status").Enabled && _player.Menu?.IsOpen != true;
		if (DefuseState.Instance is { } state)
			_roundPanel.UpdatePanel(state, delta, GetWindow().Size);
		_roundPanel.Visible &= Visible && !_player.ShowMatchScores;
		if (Visible) _ink.QueueRedraw();
	}
	private void Draw()
	{
		var de = DefuseState.Instance;
		if (de == null) return;
		var size = (Vector2)GetWindow().Size; var scale = size.Y / 480;
		var amber = new Color("ffb000");
		void Center(string text, float y, int fontSize = 12)
		{
			var pixels = Mathf.RoundToInt(fontSize * scale);
			_ink.DrawString(_font, new Vector2((size.X - _font.GetStringSize(text, fontSize: pixels).X) / 2, y * scale), text,
				fontSize: pixels, modulate: amber);
		}
		if (de.Phase == DefusePhase.Waiting) Center("Waiting for players...", 170);
		var subject = _player.HudPlayer;
		if (subject.IsAlive)
		{
			var icon = de.Carrier == subject ? "j" : subject.HasDefuser ? "f" : "";
			var color = de.Carrier == subject && de.BombSite(subject) != null && Time.GetTicksMsec() % 600 < 300
				? new Color(.65f, 0, 0) : new Color(0, .63f, 0);
			if (icon.Length > 0) _ink.DrawString(_icons, new Vector2(16, 263) * scale, icon,
				fontSize: Mathf.RoundToInt(28 * scale / 1.129f), modulate: color);
		}
		if (de.BombPlanted)
		{
			var pulse = (float)(.5 + .5 * Math.Cos(Time.GetTicksMsec() / Math.Max(60, de.BombRemaining * 8)));
			_ink.DrawString(_icons, new Vector2(size.X / 2 - 90 * scale, 466 * scale), "d",
				fontSize: Mathf.RoundToInt(28 * scale / 1.129f), modulate: new Color(.7f, 0, 0, .5f + pulse * .5f));
		}
		var planting = de.Planter == subject; var defusing = de.Defuser == subject;
		if (planting || defusing)
		{
			var progress = planting ? de.PlantProgress / 3 : de.DefuseProgress / (subject.HasDefuser ? 5 : 10);
			var rect = new Rect2(new Vector2(size.X / 2 - 60 * scale, 280 * scale), new Vector2(120, 10) * scale);
			_ink.DrawRect(rect, new Color(0, 0, 0, .5f)); _ink.DrawRect(rect, amber, false, scale);
			var fill = rect.Grow(-2 * scale); fill.Size = new(fill.Size.X * (float)Math.Clamp(progress, 0, 1), fill.Size.Y);
			_ink.DrawRect(fill, amber);
		}
		if (!_player.IsAlive && _player.Observer?.FirstPerson != true && !_player.ShowMatchScores && de.Phase != DefusePhase.Waiting)
		{
			var seconds = RoundState.Instance!.DisplaySeconds;
			Center($"{seconds / 60}:{seconds % 60:00}", 465, 16);
		}
	}
}
