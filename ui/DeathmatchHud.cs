using Godot;
using Contrapoint.Gameplay;
using Contrapoint.Players;

namespace Contrapoint.UI;

// Keep the stock status HUD intact. TAB uses the imported CSS scoreboard.
public partial class DeathmatchHud : CanvasLayer
{
	private Player _player = null!;
	private CssScoreboard _board = null!;
	private Label _notice = null!, _clock = null!;
	public void Bind(Player player)
	{
		_player = player; Layer = 12;
		var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
		AddChild(root); root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		_notice = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
		root.AddChild(_notice); _notice.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
		_notice.OffsetLeft = -300; _notice.OffsetRight = 300; _notice.OffsetTop = 170; _notice.OffsetBottom = 210;
		_notice.AddThemeFontOverride("font", GD.Load<FontFile>("res://assets/ui/css/team/verdana.ttf"));
		_notice.AddThemeColorOverride("font_color", new Color("ffb000"));
		_clock = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
		root.AddChild(_clock); _clock.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom);
		_clock.OffsetLeft = -90; _clock.OffsetRight = 90; _clock.OffsetTop = -46; _clock.OffsetBottom = -14;
		_clock.AddThemeFontOverride("font", GD.Load<FontFile>("res://assets/ui/css/hud/cstrike.ttf"));
		_clock.AddThemeFontSizeOverride("font_size", 28);
		_clock.AddThemeColorOverride("font_color", new Color(1, 176 / 255f, 0, 120 / 255f));
		_board = new CssScoreboard { Name = "Scoreboard" };
		root.AddChild(_board); _board.Bind(player);
	}
	public override void _Process(double delta)
	{
		Visible = _player.GetNode<CssHud>("HUD/Status").Enabled;
		if (!Visible) return;
		var round = RoundState.Instance!;
		_notice.Visible = DeathmatchRules.Active && round.RestartPending;
		_notice.Text = round.RestartPending ? $"Game will restart in {round.DisplaySeconds} seconds" : "";
		_clock.Visible = DeathmatchRules.Active && !_player.IsAlive && !_player.ShowMatchScores && !round.RestartPending;
		_clock.Text = round.TimerRunning ? $"e  {round.DisplaySeconds / 60}:{round.DisplaySeconds % 60:00}" : "";
		_board.Visible = _player.ShowMatchScores || DeathmatchRules.Active && round.RestartPending;
	}
}
