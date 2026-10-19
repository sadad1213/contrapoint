using Godot;

namespace Contrapoint.UI;

public partial class BuildVersion : CanvasLayer
{
	public override void _Ready()
	{
		Layer = 110;
		ProcessMode = ProcessModeEnum.Always;
		var label = new Label
		{
			Name = "VersionLabel",
			Text = ProjectSettings.GetSetting("application/config/version").AsString(),
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Bottom,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			SelfModulate = new Color(1, 1, 1, 0.5f)
		};
		label.AddThemeFontSizeOverride("font_size", 8);
		label.AddThemeColorOverride("font_color", Colors.White);
		label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
		label.AddThemeConstantOverride("shadow_offset_x", 1);
		label.AddThemeConstantOverride("shadow_offset_y", 1);
		AddChild(label);
		label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight);
		label.OffsetLeft = -220;
		label.OffsetTop = -30;
		label.OffsetRight = -6;
		label.OffsetBottom = -5;
	}
}
