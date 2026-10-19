using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.Maps;
using Contrapoint.Players;

public partial class CacheChecks : Node3D
{
	private int _checks;
	private Player _player = null!;
	private void Check(bool condition, string message)
	{
		_checks++;
		if (!condition) throw new InvalidOperationException(message);
	}
	private async Task Frames(int count, bool simulate = false)
	{
		for (var i = 0; i < count; i++)
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
			var entry = MapCatalog.Find("de_cache");
			Check(entry?.IsCombat == true, "Cache discovered as a combat map");
			Check(Contrapoint.UI.ConsoleCommands.GetCompletions("map de_ca").Contains("map de_cache"), "console discovers Cache");
			Check(GD.Load<NavigationMesh>("res://assets/maps/de_cache/bot_navigation.tres").GetPolygonCount() > 0, "baked navigation");
			var game = GD.Load<PackedScene>(entry!.ScenePath).Instantiate<Node3D>();
			_player = game.GetNode<Player>("Player");
			_player.LocalInput = false;
			AddChild(game);
			var map = game.GetNode<Node3D>("Cache");
			var ts = map.FindChildren("Spawn_terrorist_*", "Marker3D", true, false).OfType<Marker3D>().ToArray();
			Check(ts.Any(m => _player.GlobalPosition.DistanceTo(m.GlobalPosition + Vector3.Up * .04f) < .001f), "initial entry uses original T spawn");
			await Frames(3);
			Check(map.GetNode("WorldBrushes").GetChildCount() == 1302, "1302 compiled collision brushes");
			Check(map.GetNode("PlayerClips").GetChildCount() == 173, "173 player clip hulls");
			Check(map.FindChildren("Glow_*", "MeshInstance3D", true, false).Count == 15, "15 light glows");
			Check(GetTree().GetNodesInGroup("source_buyzone").Count == 2, "both buy zones");
			Check(map.FindChildren("Spawn_deathmatch_*", "Marker3D", true, false).Count >= 30, "reachable deathmatch spawns");
			var props = map.FindChildren("*", "RigidBody3D", true, false).OfType<RigidBody3D>().ToArray();
			Check(props.Length == 52, "52 authored physics props");
			var prop = props.First(p => !p.Freeze);
			var initialProp = prop.GlobalPosition;
			prop.Sleeping = false;
			prop.ApplyCentralImpulse(Vector3.Right * prop.Mass * 2);
			await Frames(12);
			Check(prop.GlobalPosition.DistanceTo(initialProp) > .03f, "physics props respond to impulses");
			long triangles = 0;
			foreach (var chunk in map.FindChildren("Chunk_*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
			{
				triangles += ((ArrayMesh)chunk.Mesh).SurfaceGetArrayLen(0) / 3;
				Check(((ShaderMaterial)chunk.Mesh.SurfaceGetMaterial(0)).GetShaderParameter("base_texture").AsGodotObject() is Texture2D, "chunk has texture: " + chunk.Name);
			}
			Check(triangles == 219816, "all extracted triangles packed");
			foreach (var team in new[] { PlayerTeam.Terrorist, PlayerTeam.CounterTerrorist })
			{
				_player.Team = team;
				var pattern = team == PlayerTeam.Terrorist ? "Spawn_terrorist_*" : "Spawn_counterterrorist_*";
				var markers = map.FindChildren(pattern, "Marker3D", true, false).OfType<Marker3D>().ToArray();
				Check(markers.Length == 14, "14 original spawns for " + team);
				foreach (var marker in markers)
				{
					_player.SourceTeleport(marker.GlobalPosition + Vector3.Up * .04f, marker.GlobalRotation.Y, 0);
					await Frames(45, true);
					Check(_player.IsOnFloor(), "floor below " + marker.Name);
				}
				for (var i = 0; i < 10; i++)
				{
					_player.Respawn();
					Check(markers.Any(m => _player.GlobalPosition.DistanceTo(m.GlobalPosition + Vector3.Up * .04f) < .001f && _player.GlobalBasis.IsEqualApprox(m.GlobalBasis)), "respawn preserves team position and yaw");
					await Frames(45, true);
					Check(_player.IsOnFloor(), "respawn settles on floor");
				}
			}
			var ladders = GetTree().GetNodesInGroup("source_ladder").OfType<Area3D>().ToArray();
			Check(ladders.Length == 2, "both authored ladders");
			foreach (var ladder in ladders)
			{
				var center = ladder.GetMeta("ladder_center").AsVector3();
				var normal = ladder.GetMeta("ladder_normal").AsVector3();
				var climbed = false;
				foreach (var side in new[] { 1, -1 })
				{
					var facing = -normal * side;
					_player.SourceTeleport(center + normal * side * .42f - Vector3.Up * .6f, Mathf.Atan2(-facing.X, -facing.Z), 0);
					await Frames(1);
					var height = _player.GlobalPosition.Y;
					for (var frame = 0; frame < 15; frame++)
					{
						await Frames(1);
						_player.Simulate(new(new(0, -1), false, false, false), 1f / 66);
					}
					climbed = _player.Movement.IsOnLadder && _player.GlobalPosition.Y > height + .25f;
					if (!climbed) continue;
					var contactNormal = _player.Movement.LadderNormal;
					_player.Simulate(new(Vector2.Zero, true, false, false), 1f / 66);
					Check(!_player.Movement.IsOnLadder && _player.Velocity.Dot(contactNormal) > 1, $"jump detaches from {ladder.Name}: normal={contactNormal} velocity={_player.Velocity}");
					break;
				}
				Check(climbed, "physical movement climbs " + ladder.Name);
			}
			var sites = GetTree().GetNodesInGroup("source_bomb_target").OfType<Area3D>().ToArray();
			Check(sites.Length == 2, "both original bomb targets");
			foreach (var site in sites)
			{
				Check(site.Get("source_data").AsGodotDictionary()["classname"].AsString() == "func_bomb_target", "bomb target retains Source metadata");
				var points = ((ConvexPolygonShape3D)site.GetChild<CollisionShape3D>(0).Shape).Points;
				var min = points.Aggregate(Vector3.Inf, (a, p) => a.Min(p));
				var max = points.Aggregate(-Vector3.Inf, (a, p) => a.Max(p));
				var center = site.ToGlobal((min + max) / 2);
				var floor = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(center + Vector3.Up, center + Vector3.Down * 4, 1));
				Check(floor.Count > 0, "bomb target has collision floor: " + site.Name);
				_player.SourceTeleport(floor["position"].AsVector3() + Vector3.Up * .04f, 0, 0);
				await Frames(5, true);
				Check(site.OverlapsBody(_player), "physical player overlap inside bomb target: " + site.Name);
				_player.SourceTeleport(center + Vector3.Right * (max.X - min.X + 3), 0, 0);
				await Frames(4);
				Check(!site.OverlapsBody(_player), "physical player leaves bomb target: " + site.Name);
			}
			var door = map.GetNode<Node3D>("brush_37");
			_player.SourceTeleport(door.GlobalPosition + new Vector3(.6f, -1.4f, 1), 0, 0);
			await Frames(2);
			_player.Camera.LookAt(door.GlobalPosition + Vector3.Right * .6f);
			_player.UseWorld();
			await Frames(40);
			Check(door.Get("opened").AsBool() && !door.Transform.IsEqualApprox(door.Get("closed_transform").AsTransform3D()), "actual use ray opens A door");
			await Frames(600);
			Check(!door.Get("opened").AsBool(), "A door automatically closes after authored wait");
			foreach (var id in new[] { "brush_12", "brush_13", "brush_31" })
			{
				var target = map.GetNode<AnimatableBody3D>(id);
				var points = ((ConvexPolygonShape3D)target.GetChildren().OfType<CollisionShape3D>().First().Shape).Points;
				var min = points.Aggregate(Vector3.Inf, (a, p) => a.Min(p));
				var max = points.Aggregate(-Vector3.Inf, (a, p) => a.Max(p));
				var center = target.ToGlobal((min + max) / 2);
				var hitTarget = false;
				foreach (var axis in new[] { Vector3.Back, Vector3.Forward, Vector3.Right, Vector3.Left, Vector3.Down })
				{
					var start = center + axis * ((max - min).Abs().Dot(axis.Abs()) / 2 + .5f);
					var ray = PhysicsRayQueryParameters3D.Create(start, center, 1);
					ray.Exclude = new() { _player.GetRid() };
					var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
					if (hit.Count == 0 || hit["collider"].AsGodotObject() != target) continue;
					_player.SourceTeleport(start - Vector3.Up * _player.Camera.Position.Y, 0, 0);
					_player.Camera.GlobalPosition = start;
					_player.Camera.LookAt(center, Math.Abs(axis.Y) > .9f ? Vector3.Right : Vector3.Up);
					_player.Combat.QueueKnife(_player.ActiveWeapon!, false);
					await Frames(15);
					Check(target.Get("broken").AsBool() && !target.Visible && target.CollisionLayer == 0, "knife breaks " + id);
					hitTarget = true;
					break;
				}
				Check(hitTarget, "reachable knife trace for " + id);
			}
			GD.Print($"CACHE_CHECKS_OK checks={_checks}");
			GetTree().Quit();
		}
		catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
	}
}
