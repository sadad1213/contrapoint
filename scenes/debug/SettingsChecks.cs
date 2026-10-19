using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.Players;
using Contrapoint.UI;

public partial class SettingsChecks : Node3D
{
	private int _checks;
	private int _failures;
	private void Check(bool condition, string message)
	{
		_checks++;
		if (condition) return;
		_failures++;
		GD.PushError(message);
	}

	public override async void _Ready()
	{
		try
		{
			if (!OS.GetCmdlineUserArgs().Contains("--console-test-profile"))
				throw new InvalidOperationException("Settings checks require --console-test-profile.");
			var player = CreatePlayer();
			if (!OS.GetCmdlineUserArgs().Contains("--settings-check-read"))
			{
				ConsoleCommands.Execute("sensitivity 9; host_writeconfig", player.Movement, (_, _) => { });
				ConsoleCommands.Execute("sensitivity 1.375; m_pitch -0.022; volume 0.37; cl_crosshairsize 5.5; fps_max 144; " +
					"alias saved_jump +jump; bind F6 saved_jump; bind F7 +duck; unbind F7", player.Movement, (_, _) => { });
				var config = ConsoleCommands.ConfigDirectory + "/config.cfg";
				ConsoleCommands.Execute("sv_cheats 0; viewmodel_fov 75", player.Movement, (_, _) => { });
				Check(FileAccess.FileExists(config) && FileAccess.GetFileAsString(config).Contains("viewmodel_fov 75"),
					"Changing viewmodel FOV with cheats disabled saves immediately.");
				Check(FileAccess.FileExists(config) && FileAccess.GetFileAsString(config).Contains("sensitivity 1.375"),
					"Settings must reach disk before the player exits or the application closes.");
				var saved = FileAccess.GetFileAsString(config);
				ConsoleCommands.Execute("sensitivity NaN; volume 2; sv_gravity 700", player.Movement, (_, _) => { });
				Check(FileAccess.GetFileAsString(config) == saved,
					"Rejected values and non-archived server settings must not rewrite the config.");
				ConsoleCommands.Execute("alias saved_jump +duck", player.Movement, (_, _) => { });
				Check(FileAccess.GetFileAsString(config).Contains("alias \"saved_jump\" \"+duck\""),
					"Changing only an alias saves immediately.");
				ConsoleCommands.Execute("bind F7 +duck", player.Movement, (_, _) => { });
				Check(FileAccess.GetFileAsString(config).Contains("bind \"F7\" \"+duck\""),
					"Changing only a binding saves immediately.");
				ConsoleCommands.Execute("unbind F7", player.Movement, (_, _) => { });
				Check(!FileAccess.GetFileAsString(config).Contains("bind \"F7\""), "Unbinding saves immediately.");
				ConsoleCommands.Execute("unbindall", player.Movement, (_, _) => { });
				Check(!FileAccess.GetFileAsString(config).Contains("\nbind "), "Unbinding all keys saves immediately.");
				ConsoleCommands.Execute("exec default; alias saved_jump +jump; bind F6 saved_jump", player.Movement, (_, _) => { });
				player.QueueFree();
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				player = CreatePlayer();
			}
			Check(Mathf.IsEqualApprox(player.MouseSensitivity, Mathf.DegToRad(.022f * 1.375f)),
				"Sensitivity survives player recreation and application restart.");
			Check(Mathf.IsEqualApprox(player.MousePitch, -.022f), "Inverted mouse pitch survives restart.");
			Check(Mathf.IsEqualApprox(AudioServer.GetBusVolumeLinear(0), .37f), "Volume survives restart.");
			Check(Mathf.IsEqualApprox(player.GetNode<Crosshair>("HUD/Crosshair").ArmLength, 5.5f),
				"Crosshair settings survive restart.");
			Check(Engine.MaxFps == 144, "FPS limit survives restart.");
			Check(player.GetNode<Camera3D>("WeaponLayer/Container/Viewport/Camera3D").Fov == 75,
				"Viewmodel FOV survives player recreation and application restart.");
			Check(ConsoleCommands.IsAlias("saved_jump") && ConsoleBindings.Keys.TryGetValue("F6", out var binding) &&
				binding == "saved_jump" && !ConsoleBindings.Keys.ContainsKey("F7"), "Aliases, binds and unbinds survive restart.");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
		catch (Exception exception) { _failures++; GD.PushError(exception.ToString()); }
		GD.Print($"SETTINGS_CHECKS checks={_checks} failures={_failures}");
		GetTree().Quit(_failures == 0 ? 0 : 1);
	}

	private Player CreatePlayer()
	{
		var player = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		AddChild(player);
		return player;
	}
}
