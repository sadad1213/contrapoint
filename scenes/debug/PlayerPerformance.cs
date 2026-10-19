using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Contrapoint.Players;
using Contrapoint.UI;

// Diagnostic only. Always run with -- --console-test-profile OUTPUT_DIRECTORY.
public partial class PlayerPerformance : Node
{
	private readonly List<object> _results = new();
	private Player _player = null!;
	private SubViewport _weaponViewport = null!;
	private SubViewportContainer _container = null!;
	private string _output = "";
	private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
	public override async void _Ready()
	{
		try { await Run(); GetTree().Quit(); }
		catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
	}
	private async Task Measure(string name)
	{
		await Frames(90);
		const int count = 240;
		var times = new List<double>();
		var before = Time.GetTicksUsec();
		double cpu = 0, gpu = 0, weaponCpu = 0, weaponGpu = 0;
		for (var i = 0; i < count; i++)
		{
			await Frames(1);
			var now = Time.GetTicksUsec(); times.Add((now - before) / 1000.0); before = now;
			cpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(GetViewport().GetViewportRid());
			gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid());
			weaponCpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(_weaponViewport.GetViewportRid());
			weaponGpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(_weaponViewport.GetViewportRid());
		}
		times.Sort();
		var row = new { scenario = name, fps = count * 1000 / times.Sum(), median_ms = times[count / 2], p95_ms = times[(int)(count * .95)], render_cpu_ms = cpu / count, render_gpu_ms = gpu / count, weapon_cpu_ms = weaponCpu / count, weapon_gpu_ms = weaponGpu / count };
		_results.Add(row); GD.Print("PLAYER_BENCH " + JsonSerializer.Serialize(row));
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		GetViewport().GetTexture().GetImage().SavePng(_output.PathJoin(name + ".png"));
	}
	private async Task Run()
	{
		var args = OS.GetCmdlineUserArgs();
		if (!args.Contains("--console-test-profile")) throw new InvalidOperationException("Use --console-test-profile to protect user settings.");
		_output = args.Last(); DirAccess.MakeDirRecursiveAbsolute(_output);
		var window = GetWindow(); window.Mode = Window.ModeEnum.Windowed; window.Size = new(1920, 1080);
		window.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
		var game = GD.Load<PackedScene>("res://scenes/maps/dust2.tscn").Instantiate<Node3D>(); AddChild(game);
		_player = game.GetNode<Player>("Player");
		await Frames(12);
		_player.JoinTeam(2); _player.JoinClass(1); _player.TeamMenu?.Close(); _player.Menu?.SetOpen(false);
		_player.SetMouseCaptured(false);
		_player.Stats.ShowFps = false; _player.Stats.ShowPosition = false; _player.Stats.NetGraph.Mode = 0;
		Engine.MaxFps = 0; DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
		// Keep the same camera and map in every scenario, including spectator.
		_player.SetPhysicsProcess(false);
		var eye = new Vector3(-480, 130, -400) * .0254f;
		_player.SourceTeleport(eye - Vector3.Up * 1.6256f, 0, 0);
		_player.Camera.LookAt(new Vector3(-300, 0, -1800) * .0254f);
		_player.FirstPerson!.ResetHistory();
		_weaponViewport = _player.GetNode<SubViewport>("WeaponLayer/Container/Viewport");
		_container = _player.GetNode<SubViewportContainer>("WeaponLayer/Container");
		RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
		RenderingServer.ViewportSetMeasureRenderTime(_weaponViewport.GetViewportRid(), true);
		await Measure("player");
		_player.Body.SetProcess(false); await Measure("body_pose_disabled"); _player.Body.SetProcess(true);
		_weaponViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
		await Measure("weapon_render_disabled"); _weaponViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
		var hud = _player.GetNode<CanvasLayer>("HUD");
		hud.Visible = false; hud.ProcessMode = ProcessModeEnum.Disabled; await Measure("hud_disabled"); hud.Visible = true; hud.ProcessMode = ProcessModeEnum.Inherit;
		var weaponMsaa = _weaponViewport.Msaa3D;
		_weaponViewport.Msaa3D = Viewport.Msaa.Disabled; await Measure("weapon_msaa_disabled"); _weaponViewport.Msaa3D = weaponMsaa;
		var worldMsaa = GetViewport().Msaa3D;
		GetViewport().Msaa3D = Viewport.Msaa.Disabled; await Measure("world_msaa_disabled"); GetViewport().Msaa3D = worldMsaa;
		_container.StretchShrink = 2; await Measure("weapon_half_resolution"); _container.StretchShrink = 1;
		await Measure("player_repeat");
		_player.BeginInitialTeamSelection(); _player.JoinTeam(1);
		_player.Camera.LookAt(new Vector3(-300, 0, -1800) * .0254f); _player.FirstPerson.ResetHistory();
		await Measure("spectator");
		System.IO.File.WriteAllText(_output.PathJoin("results.json"), JsonSerializer.Serialize(_results, new JsonSerializerOptions { WriteIndented = true }));
		GD.Print("PLAYER_BENCH_COMPLETE " + _output);
		// Skip settings save; this diagnostic must not change any profile on exit.
		_player.LocalInput = false;
	}
}
