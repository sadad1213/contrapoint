using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.UI;
using Contrapoint.Players;

public partial class OptionsChecks : Node3D
{
	private int _checks;
	private const string Frame = "Interface/Modal/Options";
	private void Check(bool condition, string message)
	{
		_checks++;
		if (!condition) throw new InvalidOperationException(message);
	}
	private async Task Frames(int count = 3)
	{
		for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}
	private static void Press(Button button) => button.EmitSignal(Button.SignalName.Pressed);
	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		CallDeferred(MethodName.Run);
	}
	private async void Run()
	{
		try
		{
			if (!OS.GetCmdlineUserArgs().Contains("--options-test-profile")) throw new InvalidOperationException("Use --options-test-profile.");
			var read = OS.GetCmdlineUserArgs().Contains("--options-read");
			var preview = OS.GetCmdlineUserArgs().Contains("--options-preview");
			var display = OS.GetCmdlineUserArgs().Contains("--options-display-check");
			var menu = GD.Load<PackedScene>("res://scenes/main_menu.tscn").Instantiate<MainMenu>();
			AddChild(menu);
			await Frames();
			Press(menu.GetNode<Button>("Interface/MainButtons/Options"));
			Check(menu.IsOptionsOpen, "Options opens through the main-menu button.");
			var frame = menu.GetNode<Panel>(Frame);
			await Frames();
			var overflow = 0;
			foreach (var page in frame.GetNode("Sheet").GetChildren().OfType<Control>().Where(c => c is not Button))
				foreach (var control in page.GetChildren().OfType<Control>())
					if (control.Position.X < 0 || control.Position.Y < 0 || control.Position.X + control.Size.X > 528.5 || control.Position.Y + control.Size.Y > 392.5)
					{ overflow++; GD.Print($"OPTIONS_OVERFLOW {page.Name}/{control.Name} position={control.Position} size={control.Size}"); }
			Check(overflow == 0, "All Options controls fit inside their CSSO page bounds.");
			if (GamePreferences.Value("cp_resolution_override") == 0)
				Check(GetViewport().Scaling3DScale == 1, "Default graphics preserve native rendering resolution instead of silently blurring the world.");
			Check(frame.Size == new Vector2(640,480), "CSSO's original 640x480 frame.");
			Check(frame.GetNode<Control>("Sheet/VideoGraphics/TextureQualityComboBox").Position == new Vector2(268,32), "CSSO graphics coordinates.");
			Check(frame.GetNode<OptionButton>("Sheet/VideoGraphics/ShaderQualityComboBox").Disabled, "Unsupported Source shader quality is visibly disabled.");
			Check(frame.GetNode<CheckBox>("Sheet/AudioVoice/VoiceModEnableCheck").Disabled, "Unavailable voice transport is visibly disabled.");
			if (preview)
			{
				foreach (var size in new[] { new Vector2I(1280,720), new Vector2I(1920,1080), new Vector2I(800,600) })
				{
					GetWindow().Mode = Window.ModeEnum.Windowed; GetWindow().Size = size;
					foreach (var page in new[] { ("Video", "Graphics"), ("Video", "Screen"), ("Controls", "Mouse"), ("Controls", "Keyboard"), ("Game", "Crosshair"), ("Game", "Viewmodel"), ("Audio", "Volume") })
					{
						menu.SelectOptionsPage(page.Item1, page.Item2); await Frames(4);
						var physical = frame.Size * frame.Scale * GetViewport().GetStretchTransform().Scale;
						Check(physical.IsEqualApprox(new Vector2(640,480)), "CSSO dialog keeps its 640x480 pixel size at " + size);
						await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
						GetViewport().GetTexture().GetImage().SavePng($"user://options_{page.Item1}_{page.Item2}_{size.X}.png");
					}
				}
				GD.Print("OPTIONS_PREVIEWS " + ProjectSettings.GlobalizePath("user://"));
			}
			else if (display)
			{
				if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("Display checks require a real renderer.");
				menu.SelectOptionsPage("Video", "Screen");
				var before = FileAccess.GetFileAsString(ConsoleCommands.ConfigDirectory + "/config.cfg");
				var previous = new[] { "cp_window_mode", "cp_screen_width", "cp_screen_height", "cp_resolution_override" }
					.ToDictionary(k => k, k => ConsoleCommands.OptionValue(k));
				var targetWidth = previous["cp_screen_width"] == 1600 ? 1280 : 1600;
				var targetRow = targetWidth == 1600 ? 9L : 3L;
				frame.GetNode<OptionButton>("Sheet/VideoScreen/ResolutionComboBox").EmitSignal(OptionButton.SignalName.ItemSelected, targetRow);
				Press(frame.GetNode<Button>("Apply")); await Frames();
				Check(frame.GetNode<Panel>("DisplayConfirmation").Visible, "A real mode change requires confirmation before saving.");
				Check(FileAccess.GetFileAsString(ConsoleCommands.ConfigDirectory + "/config.cfg") == before, "Unconfirmed display mode does not reach disk.");
				menu._Input(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true }); await Frames();
				Check(!frame.GetNode<Panel>("DisplayConfirmation").Visible && GamePreferences.Value("cp_screen_width") == previous["cp_screen_width"]
					&& GetWindow().Size.X == previous["cp_screen_width"], "Escape restores the exact previous window size.");
				frame.GetNode<OptionButton>("Sheet/VideoScreen/ResolutionComboBox").EmitSignal(OptionButton.SignalName.ItemSelected, targetRow);
				Press(frame.GetNode<Button>("Apply"));
				Press(frame.GetNode<Button>("DisplayConfirmation/Keep")); await Frames();
				Check(GamePreferences.Value("cp_screen_width") == targetWidth && GetWindow().Size.X == targetWidth
					&& FileAccess.GetFileAsString(ConsoleCommands.ConfigDirectory + "/config.cfg").Contains("cp_screen_width " + targetWidth), "Confirmed resolution reaches runtime and disk.");
				var keptSize = GetWindow().Size; var keptPosition = GetWindow().Position;
				GamePreferences.Set("mat_picmip", GamePreferences.Value("mat_picmip") == 0 ? 2 : 0); GamePreferences.Instance!.ApplyNow(); await Frames();
				Check(GetWindow().Size == keptSize && GetWindow().Position == keptPosition, "Changing texture detail preserves window size and position.");
				ConsoleCommands.ApplyOptions(previous, ConsoleBindings.Keys.ToDictionary(p => p.Key, p => p.Value), null);
				GamePreferences.Instance!.ApplyNow();
			}
			else if (!read)
			{
				menu.SelectOptionsPage("Controls", "Mouse");
				var initial = ConsoleCommands.OptionValue("sensitivity");
				frame.GetNode<HSlider>("Sheet/ControlsMouse/SensitivitySlider").Value = initial == 6 ? 5 : 6;
				menu.CloseOptions(); menu.OpenOptions();
				Check(Mathf.IsEqualApprox(ConsoleCommands.OptionValue("sensitivity"), initial), "Cancel leaves live settings untouched.");
				Check(Mathf.IsEqualApprox((float)frame.GetNode<HSlider>("Sheet/ControlsMouse/SensitivitySlider").Value, initial), "Reopening discards the cancelled draft.");
				frame.GetNode<HSlider>("Sheet/ControlsMouse/SensitivitySlider").Value = 2.75;
				frame.GetNode<CheckBox>("Sheet/ControlsMouse/MouseReversePitchCheck").ButtonPressed = true;
				frame.GetNode<HSlider>("Sheet/AudioVolume/MasterVolumeSlider").Value = .43;
				frame.GetNode<HSlider>("Sheet/GameCrosshair/CrosshairSizeSlider").Value = 6;
				frame.GetNode<OptionButton>("Sheet/VideoGraphics/AntiAliasingComboBox").EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
				frame.GetNode<OptionButton>("Sheet/VideoGraphics/TextureQualityComboBox").EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
				frame.GetNode<OptionButton>("Sheet/VideoGraphics/TextureFilteringComboBox").EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
				frame.GetNode<HSlider>("Sheet/VideoScreen/FPSMaxSlider").Value = 177;
				var tree = frame.GetNode<Tree>("Sheet/ControlsKeyboard/KeyBindingsList");
				var previousBinding = ConsoleBindings.Keys.GetValueOrDefault("F6");
				var jump = tree.GetRoot().GetChild(4); jump.Select(0);
				Press(frame.GetNode<Button>("Sheet/ControlsKeyboard/EditBindingButton"));
				menu._Input(new InputEventKey { Keycode = Key.F6, PhysicalKeycode = Key.F6, Pressed = true });
				Check(ConsoleBindings.Keys.GetValueOrDefault("F6") == previousBinding, "Binding capture is staged until Apply.");
				Press(frame.GetNode<Button>("Apply")); await Frames();
				Check(menu.IsOptionsOpen && frame.GetNode<Button>("Apply").Disabled, "Apply keeps the dialog open and clears dirty state.");
				Check(Mathf.IsEqualApprox(AudioServer.GetBusVolumeLinear(0), .43f) && Engine.MaxFps == 177, "Menu audio/FPS apply before loading a map.");
				Check(ConsoleBindings.Keys.GetValueOrDefault("F6") == "+jump", "Key binding applies in the shared input system.");
				Check(GetViewport().Msaa3D == Viewport.Msaa.Msaa2X && GetViewport().TextureMipmapBias == 2, "Graphics change actual viewport AA and texture detail.");
				Check(FileAccess.GetFileAsString(ConsoleCommands.ConfigDirectory + "/config.cfg").Contains("sensitivity 2.75"), "Menu changes reach disk immediately.");
			}
			menu.QueueFree(); await Frames();
			if (!preview && !display)
			{
				var player = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
				AddChild(player); await Frames();
				Check(Mathf.IsEqualApprox(ConsoleCommands.OptionValue("sensitivity", player), 2.75f), "Sensitivity reaches a new player / survives process restart.");
				Check(player.MousePitch < 0 && Mathf.IsEqualApprox(AudioServer.GetBusVolumeLinear(0), .43f), "Mouse inversion and volume persist.");
				Check(player.GetNode<Crosshair>("HUD/Crosshair").ArmLength == 6 && Engine.MaxFps == 177, "Crosshair and FPS persist.");
				Check(player.GetNode<SubViewport>("WeaponLayer/Container/Viewport").Msaa3D == Viewport.Msaa.Msaa2X, "New weapon viewport inherits the selected AA.");
				var oldWidth = GamePreferences.Value("cp_screen_width"); var oldHeight = GamePreferences.Value("cp_screen_height");
				GamePreferences.Set("cp_resolution_override", 1);
				GamePreferences.Set("cp_screen_width", Math.Max(640, GetWindow().Size.X / 2));
				GamePreferences.Set("cp_screen_height", Math.Max(480, GetWindow().Size.Y / 2));
				GamePreferences.Instance!.ApplyNow(); await Frames();
				Check(player.GetNode<SubViewport>("WeaponLayer/Container/Viewport").Scaling3DScale == GetViewport().Scaling3DScale,
					"An explicitly selected lower resolution scales the world and hands equally.");
				GamePreferences.Set("cp_screen_width", oldWidth); GamePreferences.Set("cp_screen_height", oldHeight);
				GamePreferences.Set("cp_resolution_override", 0); GamePreferences.Instance.ApplyNow();
				Check(GetViewport().Msaa3D == Viewport.Msaa.Msaa2X && GetViewport().TextureMipmapBias == 2, "New player cannot overwrite graphics preferences.");
				var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/maps/source_lightmap.gdshader") };
				var mesh = new MeshInstance3D { Mesh = new BoxMesh(), MaterialOverride = material };
				AddChild(mesh); await Frames();
				Check(material.GetShaderParameter("settings_texture_bias").AsSingle() == 2 && material.GetShaderParameter("settings_filter_mode").AsInt32() == 1,
					"New map surfaces receive selected mip detail and texture filtering.");
				player.Menu!.SetOpen(true); player.Menu.OpenOptions();
				Check(GetTree().Paused, "Options can open while the offline game is paused.");
				var liveFrame = player.Menu.GetNode<Panel>(Frame);
				liveFrame.GetNode<HSlider>("Sheet/ControlsMouse/SensitivitySlider").Value = 3.5;
				Press(liveFrame.GetNode<Button>("Apply"));
				Check(Mathf.IsEqualApprox(ConsoleCommands.OptionValue("sensitivity", player), 3.5f), "In-game Apply updates the live player's sensitivity.");
				ConsoleCommands.Execute("sensitivity 2.75", player.Movement, (_, _) => { });
				player.Menu.CloseOptions(); player.Menu.SetOpen(false);
				Check(!GetTree().Paused && player.MouseCaptured, "Closing Options then resuming restores offline simulation and mouse capture.");
				player.QueueFree(); await Frames();
			}
			GD.Print($"OPTIONS_CHECKS checks={_checks} failures=0 read={read}"); GetTree().Quit();
		}
		catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
	}
}
