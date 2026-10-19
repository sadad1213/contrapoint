using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Contrapoint.Players;

// CPU-only population scaling probe. Uses real player rigs, without map/render cost.
public partial class MultiplayerPerformance : Node3D
{
	private readonly List<Player> _players = [];
	private readonly List<object> _results = [];
	private object Measure(string operation, int count, Action work)
	{
		for (var i = 0; i < 20; i++) work();
		const int samples = 180;
		var times = new double[samples];
		var allocated = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < samples; i++)
		{
			var start = Time.GetTicksUsec();
			work();
			times[i] = (Time.GetTicksUsec() - start) / 1000.0;
		}
		allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
		Array.Sort(times);
		var row = new { operation, players = count, median_ms = times[samples / 2], p95_ms = times[(int)(samples * .95)], max_ms = times[^1], allocated_bytes_per_update = allocated / samples };
		_results.Add(row);
		GD.Print("MULTIPLAYER_BENCH " + JsonSerializer.Serialize(row));
		return row;
	}
	public override async void _Ready()
	{
		try
		{
			var args = OS.GetCmdlineUserArgs();
			if (!args.Contains("--console-test-profile")) throw new InvalidOperationException("Use --console-test-profile.");
			foreach (var count in new[] { 1, 4, 8, 16 })
			{
				while (_players.Count < count)
				{
					var player = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
					player.LocalInput = false; player.DebugControls = false;
					player.NetworkPeerId = _players.Count + 2;
					AddChild(player);
					player.SetPhysicsProcess(false);
					player.Velocity = Vector3.Forward * 5;
					_players.Add(player);
				}
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				Measure("body_pose", count, () => { foreach (var p in _players) p.Body._Process(1.0 / 120); });
				Measure("capture", count, () => { foreach (var p in _players) { using var state = p.CaptureNetworkState(); } });
				var states = _players.Select(p => p.CaptureNetworkState()).ToArray();
				Measure("apply", count, () => { for (var i = 0; i < count; i++) _players[i].ApplyNetworkState(states[i], false); });
				foreach (var state in states) state.Dispose();
			}
			GD.Print("MULTIPLAYER_BENCH_COMPLETE " + JsonSerializer.Serialize(_results));
			GetTree().Quit();
		}
		catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
	}
}
