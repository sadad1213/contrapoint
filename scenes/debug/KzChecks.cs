using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.Players;

public partial class KzChecks : Node3D
{
	private int _checks;
	private Player _player = null!;
	private Node3D _map = null!;
	private void Check(bool value, string message)
	{
		_checks++;
		if (!value) throw new InvalidOperationException(message);
	}
	private async Task Frames(int count, bool simulate = false)
	{
		for (var i = 0; i < count; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			if (simulate) _player.Simulate(default, 1f / 66);
		}
	}
	public override async void _Ready()
	{
		try
		{
			await Run();
			GD.Print($"KZ_CHECKS_OK checks={_checks}");
			GetTree().Quit();
		}
		catch (Exception error)
		{
			GD.PushError(error.ToString());
			GetTree().Quit(1);
		}
	}
	private async Task Run()
	{
		var scene = GD.Load<PackedScene>("res://scenes/maps/kz_jayverb000.tscn").Instantiate<Node3D>();
		_player = scene.GetNode<Player>("Player");
		_player.LocalInput = false;
		AddChild(scene);
		_map = scene.GetNode<Node3D>("kz_jayverb000");
		await Frames(3);
		var entities = _map.GetChildren().OfType<Node3D>().Where(n => n.HasMeta("source_class")).ToArray();
		var teleports = entities.OfType<Area3D>().Where(n => n.GetMeta("source_class").AsString() == "trigger_teleport").ToArray();
		var platforms = entities.Where(n => n.GetMeta("source_class").AsString() == "func_door").ToArray();
		var buttons = entities.Where(n => n.GetMeta("source_class").AsString() == "func_button").ToArray();
		Check(teleports.Length == 39 && platforms.Length == 21 && buttons.Length == 2, "Original interactive entities must survive scene packing.");
		Check(_map.GetNode("WorldBrushes").GetChildCount() == 415, "All compiled solid world brushes must collide.");
		var ladders = _map.GetChildren().OfType<Area3D>().Where(n => n.CollisionLayer == 16).ToArray();
		Check(ladders.Length == 5, "All five Source ladder volumes must be present.");
		var markers = _map.GetChildren().OfType<Marker3D>().ToArray();
		Check(markers.Length == 44, "All 44 spawn points must be present.");
		foreach (var spawn in markers)
		{
			var query = PhysicsRayQueryParameters3D.Create(spawn.GlobalPosition + Vector3.Up * .3f, spawn.GlobalPosition + Vector3.Down * 3, 1);
			query.Exclude = new() { _player.GetRid() };
			Check(GetWorld3D().DirectSpaceState.IntersectRay(query).Count > 0, "Missing floor below " + spawn.Name);
		}
		await Frames(120, true);
		Check(_player.IsOnFloor() && _player.GlobalPosition.IsFinite(), "Player must settle on the original spawn floor.");
		foreach (var area in teleports) area.Call("source_input", "Disable", "");
		// Exercise actual physics overlaps, including delayed entity outputs.
		foreach (var area in teleports)
		{
			_player.SourceTeleport(new Vector3(0, 100, 0), 0, 0);
			await Frames(3);
			area.Call("source_input", "Enable", "");
			var data = area.Get("source_data").AsGodotDictionary();
			var target = GetTree().GetNodesInGroup("source_target_" + data["target"].AsString()).OfType<Node3D>().First();
			var points = ((ConvexPolygonShape3D)area.GetChildren().OfType<CollisionShape3D>().First().Shape).Points;
			var center = points.Aggregate(Vector3.Zero, (sum, p) => sum + p) / points.Length;
			_player.SourceTeleport(area.ToGlobal(center) - Vector3.Up * SourceMovement.StandingHeight * .5f, 0, 0);
			await Frames(6);
			Check(_player.GlobalPosition.DistanceTo(target.GlobalPosition) < .05f, "Physics teleport failed: " + area.Name);
			Check(_player.Velocity.IsZeroApprox(), "Teleport must clear falling velocity: " + area.Name);
			await Frames(65);
			area.Call("source_input", "Disable", "");
		}
		// Use the same raycast as E, so a visible button must have usable geometry.
		var start = buttons.First(n => n.GetMeta("source_target").AsString() == "climb_startbutton");
		_player.SourceTeleport(start.GlobalPosition + Vector3.Back * .8f - Vector3.Up * SourceMovement.StandingEye, 0, 0);
		_player.Camera.LookAt(start.GlobalPosition);
		await Frames(3);
		_player.LocalInput = true;
		_player.SetMouseCaptured(true);
		_player.Use();
		_player.SetMouseCaptured(false);
		_player.LocalInput = false;
		await Frames(3);
		Check(_map.Get("running").AsBool(), "E on the start button must start the KZ timer.");
		await Frames(75);
		var finish = buttons.First(n => n.GetMeta("source_target").AsString() == "climb_endbutton");
		finish.Call("source_use", _player);
		Check(!_map.Get("running").AsBool() && _map.Get("finished").AsBool() && _map.Get("elapsed").AsDouble() > 0, "Finish button must freeze a nonzero run time.");
		start.Call("source_use", _player);
		Check(_map.Get("running").AsBool() && !_map.Get("finished").AsBool(), "Start button must reset a completed run.");
		// A standing player touches and lowers a bhop platform; it later resets.
		var platform = platforms[0];
		var hull = ((ConvexPolygonShape3D)platform.GetChildren().OfType<CollisionShape3D>().First().Shape).Points;
		var min = hull.Aggregate(Vector3.Inf, (a, p) => a.Min(p));
		var max = hull.Aggregate(-Vector3.Inf, (a, p) => a.Max(p));
		var origin = platform.GlobalPosition;
		_player.SourceTeleport(platform.ToGlobal(new Vector3((min.X + max.X) / 2, max.Y + .01f, (min.Z + max.Z) / 2)), 0, 0);
		await Frames(12, true);
		Check(platform.Get("opened").AsBool() && platform.GlobalPosition.Y < origin.Y, "Landing must lower the bhop platform.");
		_player.SourceTeleport(new Vector3(0, 100, 0), 0, 0);
		await Frames(150);
		Check(platform.GlobalPosition.DistanceTo(origin) < .01f, "Bhop platform must return after its authored delay.");
		// Check a real ladder contact and jump detachment through player movement.
		var ladder = ladders[0];
		var normal = ladder.GetMeta("ladder_normal").AsVector3();
		var centerLadder = ladder.GetMeta("ladder_center").AsVector3();
		_player.SourceTeleport(centerLadder + normal * .42f - Vector3.Up * .5f, 0, 0);
		_player.LookAt(_player.GlobalPosition - normal);
		var height = _player.GlobalPosition.Y;
		for (var i = 0; i < 15; i++)
		{
			await Frames(1);
			_player.Simulate(new PlayerCommand(new Vector2(0, -1), false, false, false), 1f / 66);
		}
		Check(_player.GlobalPosition.Y > height + .25f, "Forward input must climb the authored ladder volume.");
		_player.Simulate(new PlayerCommand(Vector2.Zero, true, false, false), 1f / 66);
		Check(_player.Velocity.Dot(normal) > 1, "Jump must detach from the ladder.");
		var train = entities.First(n => n.GetMeta("source_class").AsString() == "func_tanktrain");
		var trainStart = train.GlobalPosition;
		await Frames(30);
		Check(train.GlobalPosition.DistanceTo(trainStart) > .1f, "Skybox helicopter must follow its path track.");
		Check(_map.FindChildren("Chunk_*", "MeshInstance3D", true, false).Count > 400, "Imported geometry must remain present.");
		Check(Contrapoint.UI.ConsoleCommands.GetCompletions("map kz_").Contains("map kz_jayverb000"), "Console autocomplete must expose the KZ map.");
		scene.QueueFree();
		await Frames(3);
		GC.Collect();
		GC.WaitForPendingFinalizers();
		await Frames(3);
	}
}
