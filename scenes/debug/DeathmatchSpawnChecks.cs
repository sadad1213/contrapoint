using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Contrapoint.Gameplay;
using Contrapoint.Players;

public partial class DeathmatchSpawnChecks : Node3D
{
	private int _checks;
	private void Check(bool result, string message)
	{
		_checks++;
		if (!result) throw new InvalidOperationException(message);
	}
	public override async void _Ready()
	{
		try
		{
			foreach (var id in new[] { "dust2", "inferno", "de_cpl_strike" }) await CheckMap(id);
			await CheckSafety();
			GD.Print($"DM_SPAWN_CHECKS_OK checks={_checks}");
			GetTree().Quit();
		}
		catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
	}
	private async Task PhysicsReady()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
	}
	private async Task CheckMap(string id)
	{
		RoundState.Instance!.BeginMatch("deathmatch");
		var scene = GD.Load<PackedScene>($"res://scenes/maps/{id}.tscn").Instantiate<Node3D>();
		var player = scene.GetNode<Player>("Player");
		player.LocalInput = false;
		AddChild(scene);
		player.SetPhysicsProcess(false);
		await PhysicsReady();
		var map = player.GetNode<Node3D>(player.SpawnPointsRootPath);
		var dm = map.FindChildren("Spawn_deathmatch_*", "Marker3D", true, false).OfType<Marker3D>().ToArray();
		var original = map.FindChildren("Spawn_terrorist_*", "Marker3D", true, false).OfType<Marker3D>()
			.Concat(map.FindChildren("Spawn_counterterrorist_*", "Marker3D", true, false).OfType<Marker3D>()).ToArray();
		Check(dm.Length >= 30, id + ": map-wide DM points exist");
		Check(dm.Count(p => original.All(s => s.GlobalPosition.DistanceTo(p.GlobalPosition) > 15)) > 10,
			id + ": points extend beyond team bases");
		var excluded = new Godot.Collections.Array<Rid> { player.GetRid() };
		using var hull = new BoxShape3D { Size = new(.8128f, SourceMovement.StandingHeight, .8128f), Margin = .001f };
		foreach (var point in dm)
		{
			var foot = point.GlobalPosition + Vector3.Up * .04f;
			var query = new PhysicsShapeQueryParameters3D { Shape = hull, CollisionMask = 3, Exclude = excluded,
				Transform = new(Basis.Identity, foot + Vector3.Up * SourceMovement.StandingHeight / 2) };
			Check(player.GetWorld3D().DirectSpaceState.IntersectShape(query, 1).Count == 0, id + ": standing hull fits " + point.Name);
			var floor = player.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(
				foot + Vector3.Up * .1f, foot - Vector3.Up * .2f, 3, excluded));
			Check(floor.Count > 0 && floor["normal"].AsVector3().Y >= .7f, id + ": walkable floor " + point.Name);
		}
		var visited = new HashSet<ulong>();
		foreach (var mode in new[] { "deathmatch", "teamdeathmatch" })
		{
			RoundState.Instance.BeginMatch(mode);
			foreach (var team in new[] { PlayerTeam.Terrorist, PlayerTeam.CounterTerrorist })
			{
				player.Team = team;
				for (var i = 0; i < 32; i++)
				{
					var previous = player.GlobalPosition;
					player.Velocity = Vector3.One;
					player.Respawn();
					var selected = dm.FirstOrDefault(p => p.GlobalPosition.DistanceTo(player.GlobalPosition - Vector3.Up * .04f) < .001f);
					Check(selected != null, id + ": " + mode + " uses DM markers for " + team);
					Check(player.Velocity.IsZeroApprox(), id + ": respawn resets velocity");
					Check(player.GlobalPosition.DistanceTo(previous) >= 6, id + ": avoid immediate repeat nearby");
					visited.Add(selected!.GetInstanceId());
				}
			}
		}
		Check(visited.Count > 20, id + ": respawns cover multiple map areas");
		// Let several actual respawns settle, catching floors that queries alone miss.
		for (var sample = 0; sample < 4; sample++)
		{
			player.Respawn();
			var start = player.GlobalPosition;
			for (var frame = 0; frame < 20; frame++)
			{
				await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
				player.Simulate(default, 1f / 66);
			}
			Check(player.IsOnFloor() && player.GlobalPosition.DistanceTo(start) < .2f, id + ": settle safely after respawn");
		}
		RoundState.Instance.BeginMatch("sandbox");
		player.Team = PlayerTeam.Terrorist;
		player.Respawn();
		Check(original.Any(p => p.Name.ToString().StartsWith("Spawn_terrorist_")
			&& p.GlobalPosition.DistanceTo(player.GlobalPosition - Vector3.Up * .04f) < .001f), id + ": Free Play retains original team spawns");
		RoundState.Instance.BeginMatch("deathmatch");
		var fixedSpawn = new Transform3D(Basis.Identity, new Vector3(0, 50, 0));
		player.SetSpawn(fixedSpawn);
		player.Respawn(); player.Respawn();
		Check(player.GlobalTransform.IsEqualApprox(fixedSpawn), id + ": explicit server/SetSpawn transform remains authoritative");
		GD.Print($"DM_SPAWN_MAP_OK map={id} points={dm.Length} visited={visited.Count}");
		scene.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}
	private StaticBody3D Box(Node3D parent, Vector3 center, Vector3 size)
	{
		var body = new StaticBody3D { Position = center };
		body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size, Margin = .001f } });
		parent.AddChild(body);
		return body;
	}
	private async Task CheckSafety()
	{
		RoundState.Instance!.BeginMatch("deathmatch");
		var arena = new Node3D();
		AddChild(arena);
		Box(arena, new(0, -.5f, 0), new(60, 1, 60));
		Box(arena, new(0, 2, 0), new(1, 4, 50));
		var points = new Node3D { Name = "Points" };
		arena.AddChild(points);
		var positions = new[] { new Vector3(-10, 0, 0), new(-10, 0, 10), new(10, 0, 0), new(10, 0, 10) };
		for (var i = 0; i < positions.Length; i++)
			points.AddChild(new Marker3D { Name = "Spawn_deathmatch_" + i, Position = positions[i] });
		Box(arena, positions[1] + Vector3.Up, new(2, 2, 2));
		var enemy = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		enemy.LocalInput = false; enemy.Position = positions[3]; arena.AddChild(enemy); enemy.SetPhysicsProcess(false);
		var player = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		player.LocalInput = false; player.SpawnPointsRootPath = new("../Points");
		arena.AddChild(player); player.SetPhysicsProcess(false);
		await PhysicsReady();
		for (var i = 0; i < 12; i++)
		{
			player.Respawn();
			Check(player.GlobalPosition.DistanceTo(positions[0] + Vector3.Up * .04f) < .001f,
				"prefer cover from FFA opponent, reject blocked and occupied points");
		}
		Box(arena, positions[0] + Vector3.Up, new(2, 2, 2));
		await PhysicsReady();
		player.Respawn();
		Check(player.GlobalPosition.DistanceTo(positions[2] + Vector3.Up * .04f) < .001f,
			"use clear visible point when covered points become obstructed");
		arena.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}
}
