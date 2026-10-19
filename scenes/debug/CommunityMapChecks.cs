using Godot;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Contrapoint.Maps;
using Contrapoint.Players;
using Contrapoint.Gameplay;
using Contrapoint.UI;

public partial class CommunityMapChecks : Node3D
{
	private int _checks;
	private Player _player = null!;
	private void Check(bool ok, string message)
	{ _checks++; if (!ok) throw new Exception(message); }
	private static int CountEntities(JsonElement data, string className) => data.GetProperty("entities").EnumerateObject()
		.Count(entry => entry.Value.TryGetProperty("classname", out var value) && value.GetString() == className);
	private async Task Frames(int n, bool simulate = false)
	{
		for (int i = 0; i < n; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			if (simulate) _player.Simulate(default, 1f / 66);
		}
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}
	public override async void _Ready()
	{
		try
		{
			await Verify("de_winter_dust2");
			GD.Print($"COMMUNITY_MAP_CHECKS_OK checks={_checks}"); GetTree().Quit();
		}
		catch (Exception e) { GD.PushError(e.ToString()); GetTree().Quit(1); }
	}
	private async Task Verify(string id)
	{
		GD.Print("COMMUNITY_MAP_CHECK ", id);
		using var metadata = JsonDocument.Parse(FileAccess.GetFileAsString($"res://assets/maps/{id}/source/map_data.json"));
		var data = metadata.RootElement;
		Check(data.GetProperty("material_substitutions").GetArrayLength() == 0, "no missing or substituted materials");
		var entry = MapCatalog.Find(id)!;
		Check(entry.IsCombat && ConsoleCommands.GetCompletions("map " + id).Contains("map " + id), "catalog and console");
		var menu = GD.Load<PackedScene>("res://scenes/main_menu.tscn").Instantiate<MainMenu>();
		AddChild(menu); menu.OpenMapSelection(); menu.SelectMode("sandbox");
		menu.SelectMap(Array.FindIndex(MapCatalog.Discover(), m => m.Id == id));
		Check(menu.GetNode<Label>("Interface/Modal/MapSelection/Preview/PreviewId").Text == id, "menu selection");
		Check(menu.GetNode<TextureRect>("Interface/Modal/MapSelection/Preview/MapPreview").Texture != null, "rendered thumbnail");
		menu.QueueFree(); await Frames(1);
		var game = GD.Load<PackedScene>(entry.ScenePath).Instantiate<Node3D>();
		_player = game.GetNode<Player>("Player"); _player.LocalInput = false; AddChild(game);
		var map = game.GetNode<Node3D>("Map");
		var ts = map.FindChildren("Spawn_terrorist_*", "Marker3D", true, false).OfType<Marker3D>().ToArray();
		Check(ts.Any(s => s.GlobalPosition.DistanceTo(_player.GlobalPosition - Vector3.Up * .04f) < .001f), "initial original T spawn");
		await Frames(3);
		Check(map.GetNode("WorldBrushes").GetChildCount() == data.GetProperty("stats").GetProperty("world_collision_brushes").GetInt32(), "compiled world collision");
		long triangles = 0;
		foreach (var chunk in map.FindChildren("Chunk_*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
		{
			triangles += ((ArrayMesh)chunk.Mesh).SurfaceGetArrayLen(0) / 3;
			Check(((ShaderMaterial)chunk.Mesh.SurfaceGetMaterial(0)).GetShaderParameter("base_texture").AsGodotObject() is Texture2D, "textured chunk");
		}
		Check(triangles == data.GetProperty("stats").GetProperty("triangles").GetInt64(), "all triangles preserved");
		foreach (var team in new[] { PlayerTeam.Terrorist, PlayerTeam.CounterTerrorist })
		{
			_player.Team = team;
			var sourceTeam = team == PlayerTeam.Terrorist ? "terrorist" : "counterterrorist";
			var markers = map.FindChildren("Spawn_" + sourceTeam + "_*", "Marker3D", true, false).OfType<Marker3D>().ToArray();
			Check(markers.Length == data.GetProperty("spawns").EnumerateArray().Count(s => s.GetProperty("team").GetString() == sourceTeam), "all team spawns");
			foreach (var marker in markers)
			{
				_player.SourceTeleport(marker.GlobalPosition + Vector3.Up * .04f, marker.GlobalRotation.Y, 0);
				await Frames(45, true);
				Check(_player.IsOnFloor(), $"{id} floor under {marker.Name}: {_player.GlobalPosition}");
			}
			for (int i = 0; i < 5; i++)
			{
				_player.Respawn();
				Check(markers.Any(s => s.GlobalPosition.DistanceTo(_player.GlobalPosition - Vector3.Up * .04f) < .001f && s.GlobalBasis.IsEqualApprox(_player.GlobalBasis)), "respawn position and rotation");
				await Frames(45, true); Check(_player.IsOnFloor(), "respawn floor");
			}
		}
		var sites = GetTree().GetNodesInGroup("source_bomb_target").OfType<Area3D>().ToArray();
		Check(sites.Length == CountEntities(data, "func_bomb_target"), "original site count");
		Check(GetTree().GetNodesInGroup("source_buyzone").Count == CountEntities(data, "func_buyzone"), "original buyzones");
		foreach (var site in sites)
		{
			var points = ((ConvexPolygonShape3D)site.GetChild<CollisionShape3D>(0).Shape).Points;
			var min = points.Aggregate(Vector3.Inf, (a, p) => a.Min(p));
			var max = points.Aggregate(-Vector3.Inf, (a, p) => a.Max(p));
			var center = site.ToGlobal((min + max) / 2);
			var floor = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(center, center + Vector3.Down * 30, 1));
			Check(floor.Count > 0, "site floor");
			_player.SourceTeleport(floor["position"].AsVector3() + Vector3.Up * .04f, 0, 0);
			await Frames(5, true);
			Check(site.OverlapsBody(_player) && DefuseState.Instance?.BombSite(_player) == site, "physical objective recognition");
			_player.SourceTeleport(center + Vector3.Right * ((max - min).X + 4), 0, 0); await Frames(4);
			Check(!site.OverlapsBody(_player) && DefuseState.Instance?.BombSite(_player) == null, "leave objective volume");
		}
		var props = map.FindChildren("*", "RigidBody3D", true, false).OfType<RigidBody3D>().ToArray();
		Check(props.Length == data.GetProperty("stats").GetProperty("physics_props").GetInt32(), "all physics props and brush boxes");
		var prop = props.First(p => !p.Freeze); var before = prop.GlobalPosition;
		prop.Sleeping = false; prop.ApplyCentralImpulse(Vector3.Right * prop.Mass * 3); await Frames(12);
		Check(prop.GlobalPosition.DistanceTo(before) > .03f, "physics impulse");
		foreach (var target in map.FindChildren("*", "AnimatableBody3D", true, false).OfType<AnimatableBody3D>().Where(n => n.GetMeta("source_class", "").AsString() == "func_breakable_surf"))
		{
			var points = ((ConvexPolygonShape3D)target.GetChildren().OfType<CollisionShape3D>().First().Shape).Points;
			var min = points.Aggregate(Vector3.Inf, (a, p) => a.Min(p)); var max = points.Aggregate(-Vector3.Inf, (a, p) => a.Max(p));
			var center = target.ToGlobal((min + max) / 2); var hitTarget = false;
			foreach (var axis in new[] { Vector3.Back, Vector3.Forward, Vector3.Right, Vector3.Left })
			{
				var start = center + axis * ((max - min).Abs().Dot(axis.Abs()) / 2 + .5f);
				var ray = PhysicsRayQueryParameters3D.Create(start, center, 1); ray.Exclude = new() { _player.GetRid() };
				var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
				if (hit.Count == 0 || hit["collider"].AsGodotObject() != target) continue;
				_player.SourceTeleport(start - Vector3.Up * _player.Camera.Position.Y, 0, 0); await Frames(1);
				_player.Camera.GlobalPosition = start; _player.Camera.LookAt(center);
				_player.Combat.QueueKnife(_player.ActiveWeapon!, false); await Frames(15);
				Check(target.Get("broken").AsBool() && !target.Visible && target.CollisionLayer == 0, "knife breaks " + target.Name);
				hitTarget = true; break;
			}
			Check(hitTarget, "reachable window " + target.Name);
		}
		var ladders = GetTree().GetNodesInGroup("source_ladder").OfType<Area3D>().ToArray();
		Check(ladders.Length == data.GetProperty("ladder_hulls").GetArrayLength(), "authored ladders");
		foreach (var ladder in ladders)
		{
			var center = ladder.GetMeta("ladder_center").AsVector3(); var normal = ladder.GetMeta("ladder_normal").AsVector3(); var climbed = false;
			foreach (var placement in new[] { (1, .48f, 1.4f), (-1, .48f, 1.4f), (1, .48f, .6f), (-1, .48f, .6f), (1, .53f, 2f), (-1, .53f, 2f), (1, .53f, 0f), (-1, .53f, 0f) })
			{
				var side = placement.Item1;
				var facing = -normal * side; var foot = center + normal * side * placement.Item2 - Vector3.Up * placement.Item3;
				var collider = _player.GetNode<CollisionShape3D>("CollisionShape3D");
				var query = new PhysicsShapeQueryParameters3D { Shape = collider.Shape,
					Transform = new Transform3D(Basis.Identity, foot + collider.Position), CollisionMask = 3, Exclude = new() { _player.GetRid() } };
				if (GetWorld3D().DirectSpaceState.IntersectShape(query).Count > 0) continue;
				_player.SourceTeleport(foot, Mathf.Atan2(-facing.X, -facing.Z), 0); await Frames(1);
				var height = _player.GlobalPosition.Y;
				for (int frame = 0; frame < 15; frame++) { await Frames(1); _player.Simulate(new(new(0, -1), false, false, false), 1f / 66); }
				climbed = _player.Movement.IsOnLadder && _player.GlobalPosition.Y > height + .25f;
				if (!climbed) continue;
				var contact = _player.Movement.LadderNormal; _player.Simulate(new(Vector2.Zero, true, false, false), 1f / 66);
				Check(!_player.Movement.IsOnLadder && _player.Velocity.Dot(contact) > 1, $"ladder jump {ladder.Name} normal={contact} velocity={_player.Velocity} at={_player.GlobalPosition}"); break;
			}
			Check(climbed, "climb " + ladder.Name);
		}
		Check(map.FindChildren("Spawn_deathmatch_*", "Marker3D", true, false).Count >= 50, "reachable DM spawn coverage");
		Check(GD.Load<NavigationMesh>($"res://assets/maps/{id}/bot_navigation.tres").GetPolygonCount() > 100, "baked bot navigation");
		game.QueueFree(); await Frames(3);
	}
}
