using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Contrapoint.Players;

public partial class SpawnChecks : Node3D
{
	private int _checks;
	private void Check(bool value, string message)
	{
		_checks++;
		if (!value) throw new InvalidOperationException(message);
	}
	public override async void _Ready()
	{
		try
		{
			var requested = OS.GetCmdlineUserArgs();
			foreach (var id in requested.Length > 0 ? requested : new[] { "dust2", "inferno", "kz_jayverb000", "de_cpl_strike", "hns_sewer" }) await CheckMap(id);
			GD.Print($"SPAWN_CHECKS_OK checks={_checks}");
			GetTree().Quit();
		}
		catch (Exception error)
		{
			GD.PushError(error.ToString());
			GetTree().Quit(1);
		}
	}
	private async Task CheckMap(string id)
	{
		var scene = GD.Load<PackedScene>($"res://scenes/maps/{id}.tscn").Instantiate<Node3D>();
		var player = scene.GetNode<Player>("Player");
		player.LocalInput = false;
		AddChild(scene);
		var map = player.GetNode<Node3D>(player.SpawnPointsRootPath);
		var markers = map.FindChildren("Spawn_terrorist_*", "Marker3D", true, false).OfType<Marker3D>().ToArray();
		Check(markers.Length == (id is "kz_jayverb000" or "hns_sewer" ? 24 : id == "de_cpl_strike" ? 11 : 20), id + ": original T spawn count");
		Marker3D? Selected() => markers.FirstOrDefault(marker =>
			player.GlobalPosition.DistanceTo(marker.GlobalPosition + Vector3.Up * .04f) < .001f);
		Check(Selected() != null, id + ": initial spawn must be a T marker");
		GD.Seed(20261017);
		var visited = new HashSet<ulong>();
		for (var i = 0; i < 64; i++)
		{
			player.Velocity = new(1, -10, 3);
			player.Respawn();
			var marker = Selected();
			Check(marker != null, id + ": respawn must stay on a T marker");
			Check(player.GlobalBasis.IsEqualApprox(marker!.GlobalBasis), id + ": preserve marker orientation");
			Check(player.Velocity.IsZeroApprox(), id + ": clear velocity on respawn");
			visited.Add(marker.GetInstanceId());
		}
		Check(visited.Count > 1, id + ": repeated respawns must select different T markers");
		for (var i = 0; i < 120; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			player.Simulate(default, 1f / 66);
		}
		Check(player.IsOnFloor(), id + ": settle on spawn floor");
		if (id == "hns_sewer")
		{
			var ct = map.FindChildren("Spawn_counterterrorist_*", "Marker3D", true, false).OfType<Marker3D>().ToArray();
			Check(ct.Length == 28, "Sewer: original CT spawn count");
			foreach (var team in new[] { PlayerTeam.Terrorist, PlayerTeam.CounterTerrorist })
			{
				player.Team = team;
				var teamMarkers = team == PlayerTeam.Terrorist ? markers : ct;
				foreach (var spawn in teamMarkers)
				{
					player.SourceTeleport(spawn.GlobalPosition + Vector3.Up * .04f, spawn.GlobalRotation.Y, 0);
					for (var frame = 0; frame < 45; frame++)
					{
						await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
						player.Simulate(default, 1f / 66);
					}
					Check(player.IsOnFloor(), $"Sewer: floor below {spawn.Name}");
				}
				player.Respawn();
				Check(teamMarkers.Any(marker => player.GlobalPosition.DistanceTo(marker.GlobalPosition + Vector3.Up * .04f) < .001f), "Sewer: respawn on team marker");
			}
			var ladders = GetTree().GetNodesInGroup("source_ladder").OfType<Area3D>().ToArray();
			Check(ladders.Length == 7, "Sewer: seven authored ladder volumes");
			foreach (var ladder in ladders)
			{
				var center = ladder.GetMeta("ladder_center").AsVector3();
				var normal = ladder.GetMeta("ladder_normal").AsVector3();
				var caught = false;
				foreach (var side in new[] { 1, -1 })
				{
					var facing = -normal * side;
					player.SourceTeleport(center + normal * side * .42f - Vector3.Up * .6f, Mathf.Atan2(-facing.X, -facing.Z), 0);
					await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
					for (var frame = 0; frame < 3; frame++) player.Simulate(new(new(0, -1), false, false, false), 1f / 66);
					caught |= player.Movement.IsOnLadder && player.Velocity.Y > 0;
					if (caught) break;
				}
				Check(caught, "Sewer: climb " + ladder.Name);
			}
			var water = GetTree().GetNodesInGroup("source_water").OfType<Area3D>().Single();
			var waterPoints = ((ConvexPolygonShape3D)water.GetChild<CollisionShape3D>(0).Shape).Points;
			var waterCenter = waterPoints.Aggregate(Vector3.Zero, (sum, point) => sum + point) / waterPoints.Length;
			player.SourceTeleport(waterCenter, 0, 0);
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			player.Simulate(default, 1f / 66);
			Check(player.Movement.WaterLevel > 0, "Sewer: player detects authored water");
			var button = map.GetNode<Node3D>("brush_1");
			var radio = map.GetNode<Node3D>("Point_9045");
			foreach (var side in new[] { 1, -1 })
			{
				player.SourceTeleport(button.GlobalPosition + Vector3.Right * side * .9f - Vector3.Up * 1.6f, 0, 0);
				await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
				player.Camera.LookAt(button.GlobalPosition);
				player.UseWorld();
				if (button.Get("opened").AsBool()) break;
			}
			Check(button.Get("opened").AsBool(), "Sewer: actual player use ray presses radio button");
			Check(radio.Get("voice").As<AudioStreamPlayer3D>().Playing, "Sewer: button output plays radio audio");
		}
		if (id == "kz_jayverb000")
			Check(player.GlobalPosition.Y < 0 && player.GlobalPosition.Z < 0, $"KZ: remain inside the spawn lobby, got {player.GlobalPosition}");
		var fixedSpawn = new Transform3D(Basis.Identity, new Vector3(0, 50, 0));
		player.SetSpawn(fixedSpawn);
		player.Respawn();
		Check(player.GlobalTransform.IsEqualApprox(fixedSpawn), id + ": explicit SetSpawn overrides random selection");
		player.Respawn();
		Check(player.GlobalTransform.IsEqualApprox(fixedSpawn), id + ": explicit spawn persists");
		GD.Print($"SPAWN_MAP_OK map={id} visited={visited.Count}/{markers.Length}");
		scene.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}
}
