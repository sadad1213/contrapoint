using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Contrapoint.Gameplay;
using Contrapoint.Gameplay.Bots;
using Contrapoint.Network;
using Contrapoint.Players;

// Compare the pruned selector with the original exhaustive scoring on real collision.
public partial class BotSpawnChecks : Node
{
	private async Task Until(Func<bool> condition)
	{
		var end = Time.GetTicksMsec() + 60000;
		while (!condition())
		{
			if (Time.GetTicksMsec() > end) throw new TimeoutException("Spawn check setup");
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		}
	}
	public override async void _Ready()
	{
		var checks = 0;
		try
		{
			var args = OS.GetCmdlineUserArgs();
			if (!args.Contains("--console-test-profile") || !args.Contains("--menu-test-profile"))
				throw new InvalidOperationException("Use isolated profiles.");
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			Reparent(GetTree().Root); GetTree().CurrentScene = null;
			GD.Seed(1852);
			MatchSession.Begin(new MatchSetup { ModeId = "deathmatch", Bots = new() { Difficulty = "expert", Quota = 0, DontShoot = true } });
			GetTree().ChangeSceneToFile("res://scenes/maps/dust2.tscn");
			await Until(() => LanSession.Instance!.LocalPlayer != null && BotManager.Instance != null);
			var local = LanSession.Instance!.LocalPlayer!;
			local.JoinTeam(2); local.JoinClass(1); local.GodMode = true; local.SetPhysicsProcess(false);
			var manager = BotManager.Instance!;
			manager.SetPhysicsProcess(false);
			var flags = BindingFlags.Instance | BindingFlags.NonPublic;
			var select = typeof(Player).GetMethod("ChooseMapDeathmatchSpawn", flags)!.CreateDelegate<Func<Marker3D?>>(local);
			var pointsField = typeof(Player).GetField("_deathmatchSpawns", flags)!;
			var previousField = typeof(Player).GetField("_lastDeathmatchSpawn", flags)!;
			var points = (Marker3D[])pointsField.GetValue(local)!;
			foreach (var count in new[] { 0, 4, 8, 15 })
			{
				while (manager.Bots.Count < count)
				{
					if (!manager.AddOne()) throw new Exception("Cannot add bot");
					var bot = manager.Bots[^1];
					bot.SetPhysicsProcess(false); bot.Player.Body.SetProcess(false); bot.Player.GodMode = true;
				}
				foreach (var ffa in new[] { true, false })
				{
					DeathmatchRules.FreeForAll = ffa;
					for (var seed = 0; seed < 24; seed++)
					{
						var previous = seed % 4 == 0 ? null : points[seed * 17 % points.Length];
						GD.Seed((ulong)(4000 + seed));
						var expected = Reference(local, points, previous);
						previousField.SetValue(local, previous);
						GD.Seed((ulong)(4000 + seed));
						var actual = select();
						if (expected != actual)
							throw new Exception($"Spawn mismatch bots={count} ffa={ffa} seed={seed}: expected {expected?.Name}, got {actual?.Name}");
						checks++;
					}
				}
				GD.Print($"SPAWN_EQUIVALENCE bots={count} checks={checks}");
			}
			// No floor: preserve the null result used by the normal spawn fallback.
			var invalid = new Marker3D { Position = new Vector3(10000, 10000, 10000) };
			AddChild(invalid);
			pointsField.SetValue(local, new[] { invalid });
			if (select() != null) throw new Exception("Invalid spawn must be rejected");
			pointsField.SetValue(local, points); checks++;
			GD.Print($"BOT_SPAWN_CHECKS checks={checks} failures=0"); GetTree().Quit();
		}
		catch (Exception error)
		{
			GD.PushError(error.ToString()); GD.Print($"BOT_SPAWN_CHECKS checks={checks} failures=1"); GetTree().Quit(1);
		}
	}
	private Marker3D? Reference(Player player, Marker3D[] points, Marker3D? previous)
	{
		if (points.Length == 0) return null;
		var nodes = player.GetTree().GetNodesInGroup("source_players");
		using var ownedNodes = (Godot.Collections.Array)nodes;
		var players = nodes.OfType<Player>()
			.Where(p => p != player && p.IsAlive && DeathmatchRules.Playing(p)).ToArray();
		var enemies = players.Where(p => !DeathmatchRules.Teammates(player, p)).ToArray();
		var space = player.GetWorld3D().DirectSpaceState;
		var excluded = new Godot.Collections.Array<Rid> { player.GetRid() };
		using var ownedExcluded = (Godot.Collections.Array)excluded;
		var sightExcluded = new Godot.Collections.Array<Rid>(players.Select(p => p.GetRid()).Append(player.GetRid()));
		using var ownedSightExcluded = (Godot.Collections.Array)sightExcluded;
		using var spawnHull = new BoxShape3D
		{
			Size = new(32 * SourceMovement.Unit, SourceMovement.StandingHeight, 32 * SourceMovement.Unit), Margin = .001f
		};
		using var hull = new PhysicsShapeQueryParameters3D
		{
			Shape = spawnHull, CollisionMask = 3, Exclude = excluded
		};
		// Shuffle before scoring so an empty server and equally safe positions
		// still use the whole map. Only the host/offline authority selects a point.
		var candidates = (Marker3D[])points.Clone();
		for (var i = candidates.Length - 1; i > 0; i--)
		{
			var other = GD.RandRange(0, i);
			(candidates[i], candidates[other]) = (candidates[other], candidates[i]);
		}
		var choices = new List<(Marker3D Point, int Visible, float Distance, bool Repeat)>();
		foreach (var point in candidates)
		{
			var foot = point.GlobalPosition + Vector3.Up * .04f;
			if (players.Any(p => new Vector2(p.GlobalPosition.X - foot.X, p.GlobalPosition.Z - foot.Z).LengthSquared() < 4
				&& Mathf.Abs(p.GlobalPosition.Y - foot.Y) < SourceMovement.StandingHeight)) continue;
			hull.Transform = new(Basis.Identity, foot + Vector3.Up * SourceMovement.StandingHeight / 2);
			var overlaps = space.IntersectShape(hull, 1);
			using var ownedOverlaps = (Godot.Collections.Array)overlaps;
			if (overlaps.Count > 0) continue;
			using var floorRay = PhysicsRayQueryParameters3D.Create(foot + Vector3.Up * .1f,
				foot - Vector3.Up * .2f, 3, excluded);
			using var floor = space.IntersectRay(floorRay);
			if (floor.Count == 0 || floor["normal"].AsVector3().Y < .7f) continue;
			var visible = 0;
			var distance = 24f * 24;
			foreach (var enemy in enemies)
			{
				distance = Mathf.Min(distance, foot.DistanceSquaredTo(enemy.GlobalPosition));
				var eye = enemy.GlobalPosition + Vector3.Up * SourceMovement.StandingEye;
				// World geometry blocks visibility; players do not provide spawn cover.
				var seen = false;
				for (var sample = 0; sample < 2; sample++)
				{
					var height = sample == 0 ? SourceMovement.StandingEye : SourceMovement.StandingHeight / 2;
					using var ray = PhysicsRayQueryParameters3D.Create(eye, foot + Vector3.Up * height, 1);
					ray.Exclude = sightExcluded;
					using var hit = space.IntersectRay(ray);
					if (hit.Count == 0) { seen = true; break; }
				}
				if (seen) visible++;
			}
			choices.Add((point, visible, distance, previous != null
				&& point.GlobalPosition.DistanceSquaredTo(previous.GlobalPosition) < 36));
			if (choices.Count >= 32) break;
		}
		var selected = choices.OrderBy(c => c.Visible).ThenBy(c => c.Distance < 100)
			.ThenBy(c => c.Repeat).ThenByDescending(c => c.Distance).FirstOrDefault().Point;
		if (selected != null) previous = selected;
		return selected;
	}

}

