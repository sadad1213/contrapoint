using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Contrapoint.Gameplay;
using Contrapoint.Gameplay.Bots;
using Contrapoint.Network;
using Contrapoint.Players;

// Rendered whole-frame diagnostic. Requires isolated console/menu profiles.
public partial class GamePerformance : Node
{
	private readonly List<object> _results = [];
	private Player _local = null!;
	private SubViewport _weaponViewport = null!;
	private string _output = "";
	private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
	private async Task Until(Func<bool> condition)
	{
		var deadline = Time.GetTicksMsec() + 60000;
		while (!condition()) { if (Time.GetTicksMsec() > deadline) throw new TimeoutException("Game benchmark setup"); await Frames(1); }
	}
	private async Task Measure(string scenario)
	{
		await Frames(90);
		var times = new double[240];
		double cpu = 0, gpu = 0, weaponCpu = 0, weaponGpu = 0, draws = 0, primitives = 0;
		var previous = Time.GetTicksUsec();
		for (var i = 0; i < times.Length; i++)
		{
			await Frames(1);
			var now = Time.GetTicksUsec(); times[i] = (now - previous) / 1000.0; previous = now;
			cpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(GetViewport().GetViewportRid());
			gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid());
			weaponCpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(_weaponViewport.GetViewportRid());
			weaponGpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(_weaponViewport.GetViewportRid());
			draws += GetViewport().GetRenderInfo(Viewport.RenderInfoType.Visible, Viewport.RenderInfo.DrawCallsInFrame);
			primitives += GetViewport().GetRenderInfo(Viewport.RenderInfoType.Visible, Viewport.RenderInfo.PrimitivesInFrame);
		}
		Array.Sort(times);
		var n = times.Length;
		var row = new { scenario, fps = 1000 / times.Average(), median_ms = times[n / 2], p95_ms = times[(int)(n * .95)], render_cpu_ms = cpu / n, render_gpu_ms = gpu / n, weapon_cpu_ms = weaponCpu / n, weapon_gpu_ms = weaponGpu / n, draw_calls = draws / n, primitives = primitives / n };
		_results.Add(row); GD.Print("GAME_BENCH " + JsonSerializer.Serialize(row));
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		GetViewport().GetTexture().GetImage().SavePng(_output.PathJoin(scenario + ".png"));
	}
	private static Vector3 Source(string value)
	{
		var xyz = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
		return new Vector3(xyz[0], xyz[2], -xyz[1]) * SourceMovement.Unit;
	}
	public override async void _Ready()
	{
		try { await Run(); GetTree().Quit(); }
		catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
	}
	private async Task Run()
	{
		var args = OS.GetCmdlineUserArgs();
		if (!args.Contains("--console-test-profile") || !args.Contains("--menu-test-profile")) throw new InvalidOperationException("Use isolated profiles.");
		_output = args.Last(); DirAccess.MakeDirRecursiveAbsolute(_output);
		await Frames(1); Reparent(GetTree().Root); GetTree().CurrentScene = null;
		GD.Seed(1852);
		MatchSession.Begin(new MatchSetup { MapId = "de_cpl_strike", ModeId = "deathmatch", Bots = new() { Difficulty = "expert", Quota = 0, DontShoot = true } });
		GetTree().ChangeSceneToFile("res://scenes/maps/de_cpl_strike.tscn");
		await Until(() => LanSession.Instance!.LocalPlayer != null && BotManager.Instance != null);
		_local = LanSession.Instance!.LocalPlayer!;
		_local.JoinTeam(2); _local.JoinClass(1); _local.TeamMenu?.Close(); _local.Menu?.SetOpen(false);
		_local.GodMode = true; _local.SetMouseCaptured(false); _local.SetPhysicsProcess(false);
		_local.Stats.ShowFps = false; _local.Stats.ShowPosition = false; _local.Stats.NetGraph.Mode = 0;
		var window = GetWindow(); window.Mode = Window.ModeEnum.Windowed; window.Size = new(1920, 1080); window.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
		Engine.MaxFps = 0; DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
		using var data = JsonDocument.Parse(FileAccess.GetFileAsString("res://assets/maps/de_cpl_strike/source/map_data.json"));
		var cameraName = args.FirstOrDefault(a => a.StartsWith("--perf-camera="))?.Split('=')[1] ?? "spawn_0";
		var camera = data.RootElement.GetProperty("point_entities").EnumerateArray().FirstOrDefault(e => e.GetProperty("classname").GetString() == "point_devshot_camera" && e.TryGetProperty("cameraname", out var name) && name.GetString() == cameraName);
		var eye = _local.Camera.GlobalPosition;
		var angles = new float[] { 0, 0, 0 };
		if (camera.ValueKind != JsonValueKind.Undefined)
		{
			eye = Source(camera.GetProperty("origin").GetString()!);
			angles = camera.GetProperty("angles").GetString()!.Split(' ').Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
		}
		else
		{
			var spawn = data.RootElement.GetProperty("spawns")[0].GetProperty("position");
			eye = new Vector3(spawn[0].GetSingle(), spawn[1].GetSingle(), spawn[2].GetSingle()) + Vector3.Up * SourceMovement.StandingEye;
			if (cameraName.StartsWith("spawn_")) angles[1] = float.Parse(cameraName[6..], System.Globalization.CultureInfo.InvariantCulture);
		}
		_local.SourceTeleport(eye - Vector3.Up * SourceMovement.StandingEye, Mathf.DegToRad(angles[1] - 90), -Mathf.DegToRad(angles[0]));
		_local.FirstPerson!.ResetHistory();
		_weaponViewport = _local.GetNode<SubViewport>("WeaponLayer/Container/Viewport");
		RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
		RenderingServer.ViewportSetMeasureRenderTime(_weaponViewport.GetViewportRid(), true);
		GD.Print($"GAME_BENCH_SETUP adapter={RenderingServer.GetVideoAdapterName()} camera={cameraName} eye={eye}");
		await Measure("solo");
		var manager = BotManager.Instance!;
		if (args.Contains("--perf-population"))
		{
			SourceDamageRules.Cheats = true;
			foreach (var count in new[] { 2, 5, 10 })
			{
				while (manager.Bots.Count < count) { if (!manager.AddOne()) throw new InvalidOperationException("Cannot add benchmark bot"); }
				await Until(() => manager.NavigationReady);
				foreach (var bot in manager.Bots) bot.Player.GodMode = true;
				await Measure($"bots_{count}_live");
			}
			manager.Configure(manager.Settings with { DontShoot = false });
			await Measure("bots_10_combat");
			foreach (var bot in manager.Bots.Skip(5).ToArray()) manager.Kick(bot.Player.DisplayName);
			manager.Configure(manager.Settings with { Quota = 5, DontShoot = true });
			await Measure("bots_5_repeat");
			var index = 0;
			foreach (var bot in manager.Bots)
			{
				bot.SetPhysicsProcess(false);
				var foot = _local.Camera.GlobalPosition - _local.Camera.GlobalBasis.Z * (2.5f + index * .2f)
					+ _local.Camera.GlobalBasis.X * ((index - 2) * .35f) - Vector3.Up * SourceMovement.StandingEye;
				bot.Player.SourceTeleport(foot, _local.Rotation.Y + Mathf.Pi, 0);
				if (!bot.Player.Movement.Noclip) bot.Player.Movement.ToggleNoclip();
				bot.Player.Velocity = Vector3.Forward * 5; index++;
			}
			await Measure("bots_5_visible");
			foreach (var bot in manager.Bots) bot.Player.Position += _local.Camera.GlobalBasis.Z * 20;
			await Measure("bots_5_hidden");
			System.IO.File.WriteAllText(_output.PathJoin("results.json"), JsonSerializer.Serialize(_results, new JsonSerializerOptions { WriteIndented = true }));
			GD.Print("GAME_BENCH_COMPLETE " + _output); _local.LocalInput = false; return;
		}
		while (manager.Bots.Count < 5) { if (!manager.AddOne()) throw new InvalidOperationException("Cannot add benchmark bot"); }
		await Until(() => manager.NavigationReady);
		foreach (var bot in manager.Bots) bot.Player.GodMode = true;
		await Measure("five_bots_live");
		foreach (var bot in manager.Bots) bot.SetPhysicsProcess(false);
		await Measure("brains_disabled");
		var players = manager.Bots.Select(b => b.Player).Prepend(_local).ToArray();
		foreach (var player in players) player.Body.SetProcess(false);
		await Measure("body_poses_disabled");
		foreach (var player in players) player.Body.SetProcess(true);
		foreach (var player in players) foreach (var weapon in player.Inventory.Weapons) weapon.SetProcess(false);
		await Measure("weapon_animation_disabled");
		foreach (var player in players) foreach (var weapon in player.Inventory.Weapons) weapon.SetProcess(true);
		_weaponViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
		await Measure("weapon_render_disabled");
		_weaponViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
		var hud = _local.GetNode<CanvasLayer>("HUD");
		hud.Visible = false; hud.ProcessMode = ProcessModeEnum.Disabled;
		await Measure("hud_disabled");
		hud.Visible = true; hud.ProcessMode = ProcessModeEnum.Inherit;
		var map = _local.GetNode<Node3D>(_local.SpawnPointsRootPath);
		map.Visible = false; await Measure("map_hidden"); map.Visible = true;
		await Measure("five_bots_repeat");
		System.IO.File.WriteAllText(_output.PathJoin("results.json"), JsonSerializer.Serialize(_results, new JsonSerializerOptions { WriteIndented = true }));
		GD.Print("GAME_BENCH_COMPLETE " + _output);
		_local.LocalInput = false;
	}
}
