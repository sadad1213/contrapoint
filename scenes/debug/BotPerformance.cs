using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Contrapoint.Gameplay;
using Contrapoint.Gameplay.Bots;
using Contrapoint.Network;

// CPU probe on the real Dust II collision/navigation, with shooting disabled.
public partial class BotPerformance : Node
{
	private async Task Until(Func<bool> condition)
	{
		var deadline = Time.GetTicksMsec() + 60000;
		while (!condition())
		{
			if (Time.GetTicksMsec() > deadline) throw new TimeoutException("Bot benchmark setup");
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		}
	}
	private void MeasureSpawns(int count, Contrapoint.Players.Player player)
	{
		// Measure only the real spawn selection, without weapon/model creation.
		var select = typeof(Contrapoint.Players.Player).GetMethod("ChooseMapDeathmatchSpawn", BindingFlags.Instance | BindingFlags.NonPublic)!
			.CreateDelegate<Func<Marker3D?>>(player);
		var samples = new double[64];
		long bytes = 0;
		for (var i = -8; i < samples.Length; i++)
		{
			var allocated = GC.GetAllocatedBytesForCurrentThread();
			var start = Time.GetTicksUsec();
			if (select() == null) throw new InvalidOperationException("No safe map spawn");
			var elapsed = (Time.GetTicksUsec() - start) / 1000.0;
			if (i >= 0) { samples[i] = elapsed; bytes += GC.GetAllocatedBytesForCurrentThread() - allocated; }
		}
		Array.Sort(samples);
		GD.Print("SPAWN_BENCH " + JsonSerializer.Serialize(new { bots = count, mean_ms = samples.Average(), p95_ms = samples[(int)(samples.Length * .95)], max_ms = samples[^1], allocated_bytes_per_spawn = bytes / samples.Length }));
	}
	public override async void _Ready()
	{
		try
		{
			if (!OS.GetCmdlineUserArgs().Contains("--console-test-profile") || !OS.GetCmdlineUserArgs().Contains("--menu-test-profile"))
				throw new InvalidOperationException("Use --console-test-profile and --menu-test-profile.");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			Reparent(GetTree().Root); GetTree().CurrentScene = null;
			GD.Seed(1852);
			MatchSession.Begin(new MatchSetup { ModeId = "deathmatch", Bots = new() { Difficulty = "expert", Quota = 0, DontShoot = true } });
			GetTree().ChangeSceneToFile("res://scenes/maps/dust2.tscn");
			await Until(() => LanSession.Instance!.LocalPlayer != null && BotManager.Instance != null);
			var local = LanSession.Instance!.LocalPlayer!;
			local.JoinTeam(2); local.JoinClass(1); local.GodMode = true; local.SetPhysicsProcess(false);
			var manager = BotManager.Instance!;
			foreach (var count in new[] { 4, 8, 15 })
			{
				while (manager.Bots.Count < count) manager.AddOne();
				await Until(() => manager.NavigationReady);
				foreach (var bot in manager.Bots) { bot.SetPhysicsProcess(false); bot.Player.Body.SetProcess(false); bot.Player.GodMode = true; }
				manager.SetPhysicsProcess(false);
				if (OS.GetCmdlineUserArgs().Contains("--perf-spawns-only")) { MeasureSpawns(count, local); continue; }
				var samples = new double[660];
				long bytes = 0;
				for (var i = -66; i < samples.Length; i++)
				{
					await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
					var allocated = GC.GetAllocatedBytesForCurrentThread();
					var start = Time.GetTicksUsec();
					foreach (var bot in manager.Bots) bot._PhysicsProcess(1.0 / 66);
					var elapsed = (Time.GetTicksUsec() - start) / 1000.0;
					if (i >= 0) { samples[i] = elapsed; bytes += GC.GetAllocatedBytesForCurrentThread() - allocated; }
				}
				Array.Sort(samples);
				GD.Print("BOT_BENCH " + JsonSerializer.Serialize(new { bots = count, mean_ms = samples.Average(), median_ms = samples[samples.Length / 2], p95_ms = samples[(int)(samples.Length * .95)], max_ms = samples[^1], allocated_bytes_per_tick = bytes / samples.Length, paths = manager.Bots.Sum(b => b.PathsFound) }));
			}
			GetTree().Quit();
		}
		catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
	}
}
