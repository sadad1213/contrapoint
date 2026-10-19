using Godot;
using System;
using Contrapoint.Players;

namespace Contrapoint.UI;

public partial class ConsoleStats : CanvasLayer
{
	public bool ShowFps { get; set; }
	public bool ShowPosition { get; set; }
	public NetGraphPanel NetGraph { get; private set; } = null!;
	private Label _label = null!;
	public override void _Ready()
	{
		Layer = 80;
		NetGraph = new NetGraphPanel { Name = "NetGraph" };
		AddChild(NetGraph);
		_label = new Label { Position = new(12, 12), MouseFilter = Control.MouseFilterEnum.Ignore };
		_label.AddThemeColorOverride("font_color", Colors.LightGreen);
		AddChild(_label);
	}
	public override void _Process(double delta)
	{
		_label.Visible = ShowFps || ShowPosition;
		if (!_label.Visible) return;
		var player = GetParent<Player>();
		_label.Text = ShowFps ? $"fps: {Engine.GetFramesPerSecond()}\n" : "";
		if (ShowPosition)
		{
			var position = player.GlobalPosition / SourceMovement.Unit;
			_label.Text += FormattableString.Invariant($"pos: {position.X:F2} {position.Y:F2} {position.Z:F2}\nang: {Mathf.RadToDeg(player.AimPitch):F2} {player.RotationDegrees.Y:F2}\nvel: {player.Movement.HorizontalSpeed:F2}");
		}
	}
}
