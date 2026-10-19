using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Contrapoint.Maps;
using Contrapoint.Network;
using Contrapoint.Players;

public partial class StrikeChecks : Node3D
{
	private int _checks;
	private Player _player = null!;
	private void Check(bool condition, string message)
	{
		_checks++;
		if (!condition) throw new InvalidOperationException(message);
	}
	private async Task Frames(int count, bool move = false)
	{
		for (var i = 0; i < count; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			if (move) _player.Simulate(default, 1f / 66);
		}
	}
	public override async void _Ready()
	{
		try { await Run(); GD.Print($"STRIKE_CHECKS_OK checks={_checks}"); GetTree().Quit(); }
		catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
	}
	private async Task Run()
	{
		var allowed = typeof(LanSession).GetMethod("AllowedMap", BindingFlags.NonPublic | BindingFlags.Static)!;
		foreach (var map in MapCatalog.Discover())
			Check((bool)allowed.Invoke(null, new object[] { map.ScenePath })!, "LAN must accept every installed map: " + map.Id);
		Check(!(bool)allowed.Invoke(null, new object[] { "res://scenes/player/player.tscn" })!, "LAN accepts only installed map scenes");
		// A new wrapper is discovered without editing any map list.
		const string probe = "res://scenes/maps/catalog_discovery_probe.tscn";
		try
		{
			using (var file = FileAccess.Open(probe, FileAccess.ModeFlags.Write)) file.StoreString("[gd_scene format=3]\n[node name=\"Probe\" type=\"Node3D\"]\n");
			Check(MapCatalog.Find("catalog_discovery_probe")?.ScenePath == probe, "New map wrappers must be discovered automatically");
			Check((bool)allowed.Invoke(null, new object[] { probe })!, "Newly discovered maps must be allowed in LAN");
		}
		finally { DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(probe)); }
		Check(Contrapoint.UI.ConsoleCommands.GetCompletions("map de_cpl_").Contains("map de_cpl_strike"), "Console completes Strike from the shared catalog");
		var scene = GD.Load<PackedScene>("res://scenes/maps/de_cpl_strike.tscn").Instantiate<Node3D>();
		_player = scene.GetNode<Player>("Player");
		_player.LocalInput = false;
		AddChild(scene);
		var root = scene.GetNode<Node3D>("Strike");
		await Frames(3);
		var markers = root.GetChildren().OfType<Marker3D>().ToArray();
		Check(markers.Length == 22, "All 22 authored spawns must survive");
		foreach (var marker in markers)
		{
			var ray = PhysicsRayQueryParameters3D.Create(marker.GlobalPosition + Vector3.Up * .3f, marker.GlobalPosition + Vector3.Down * 3, 1);
			ray.Exclude = new() { _player.GetRid() };
			Check(GetWorld3D().DirectSpaceState.IntersectRay(ray).Count > 0, "Missing spawn floor: " + marker.Name);
		}
		await Frames(120, true);
		Check(_player.IsOnFloor(), "Player must settle on the spawn floor");
		Check(root.GetNode("WorldBrushes").GetChildCount() == 4048, "All compiled world collision hulls must survive");
		Check(root.GetNode("PlayerClips").GetChildCount() == 254, "All player clip hulls must survive");
		Check(GetTree().GetNodesInGroup("source_bomb_target").Count == 2, "Both bomb zones must be present");
		Check(GetTree().GetNodesInGroup("source_buyzone").Count == 2, "Both buy zones must be present");
		Check(root.FindChildren("Glow_*", "MeshInstance3D", true, false).Count == 18, "All original light glows must be present");
		long triangles = 0;
		foreach (var chunk in root.FindChildren("Chunk_*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
		{
			triangles += ((ArrayMesh)chunk.Mesh).SurfaceGetArrayLen(0) / 3;
			var material = (ShaderMaterial)chunk.Mesh.SurfaceGetMaterial(0);
			Check(material.GetShaderParameter("base_texture").AsGodotObject() is Texture2D, "Missing texture: " + chunk.Name);
		}
		using var metadata = System.Text.Json.JsonDocument.Parse(FileAccess.GetFileAsString("res://assets/maps/de_cpl_strike/source/map_data.json"));
		Check(triangles == metadata.RootElement.GetProperty("stats").GetProperty("triangles").GetInt64(), "All extracted geometry must be packed");
		var props = root.FindChildren("*", "RigidBody3D", true, false).OfType<RigidBody3D>().ToArray();
		Check(props.Length == 14, "All 14 physics props must survive");
		var prop = props.First(p => !p.Freeze);
		var initialProp = prop.GlobalPosition;
		prop.Sleeping = false;
		prop.ApplyCentralImpulse(Vector3.Right * prop.Mass * 2);
		await Frames(12);
		Check(prop.GlobalPosition.DistanceTo(initialProp) > .03f, "Physical props must respond to impulses");
		var fan = root.GetNode<Node3D>("brush_39");
		var initialFan = fan.Transform;
		await Frames(8);
		Check(!fan.Transform.IsEqualApprox(initialFan) && fan.FindChildren("Chunk_*", "MeshInstance3D", true, false).Count > 0, "Fan and its attached blades must rotate");
		var ladders = root.GetChildren().OfType<Area3D>().Where(n => n.CollisionLayer == 16).ToArray();
		Check(ladders.Length == 2, "Both authored ladders must survive");
		foreach (var ladder in ladders)
		{
			var normal = ladder.GetMeta("ladder_normal").AsVector3();
			var center = ladder.GetMeta("ladder_center").AsVector3();
			var probeShape = new PhysicsShapeQueryParameters3D
			{
				Shape = _player.GetNode<CollisionShape3D>("CollisionShape3D").Shape,
				Transform = new Transform3D(Basis.Identity, center + normal * .42f - Vector3.Up * .5f),
				CollisionMask = 3, Exclude = new() { _player.GetRid() }
			};
			if (GetWorld3D().DirectSpaceState.IntersectShape(probeShape).Count > 0) normal = -normal;
			_player.SourceTeleport(center + normal * .42f - Vector3.Up * .5f, 0, 0);
			_player.LookAt(_player.GlobalPosition - normal);
			var height = _player.GlobalPosition.Y;
			for (var i = 0; i < 15; i++) { await Frames(1); _player.Simulate(new PlayerCommand(new Vector2(0, -1), false, false, false), 1f / 66); }
			Check(_player.GlobalPosition.Y > height + .25f, "Forward movement must climb " + ladder.Name);
			_player.Simulate(new PlayerCommand(Vector2.Zero, true, false, false), 1f / 66);
			Check(_player.Velocity.Dot(normal) > 1, $"Jump must detach from {ladder.Name}; normal={normal} velocity={_player.Velocity} position={_player.GlobalPosition}");
		}
		var breakables = root.FindChildren("*", "AnimatableBody3D", true, false).OfType<AnimatableBody3D>()
			.Where(n => n.GetMeta("source_class").AsString() == "func_breakable").ToArray();
		Check(breakables.Length == 14, "All 14 breakable brushes must survive");
		foreach (var brush in breakables)
		{
			// Four nodraw helper brushes belong to the visible wooden panels.
			var visualRoot = brush.Get("source_data").AsGodotDictionary().ContainsKey("parentname") ? brush.GetParent() : brush;
			Check(visualRoot.FindChildren("Chunk_*", "MeshInstance3D", true, false).Count > 0, brush.Name + ": breakable must have visible geometry");
		}
		// Use the actual knife trace and damage path against exposed wooden/metal brushes.
		foreach (var id in new[] { "brush_15", "brush_16", "brush_17", "brush_18", "brush_25", "brush_26" })
		{
			var target = root.GetNode<AnimatableBody3D>(id);
			var hull = (ConvexPolygonShape3D)target.GetChildren().OfType<CollisionShape3D>().First().Shape;
			var min = hull.Points.Aggregate(Vector3.Inf, (a, p) => a.Min(p));
			var max = hull.Points.Aggregate(-Vector3.Inf, (a, p) => a.Max(p));
			var center = target.ToGlobal((min + max) / 2);
			var shot = false;
			foreach (var axis in new[] { Vector3.Back, Vector3.Forward, Vector3.Right, Vector3.Left, Vector3.Up })
			{
				var reach = (max - min).Abs().Dot(axis.Abs()) / 2 + .5f;
				var start = center + axis * reach;
				var ray = PhysicsRayQueryParameters3D.Create(start, center, 1);
				ray.Exclude = new() { _player.GetRid() };
				var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
				if (hit.Count == 0 || hit["collider"].AsGodotObject() != target) continue;
				_player.SourceTeleport(start - Vector3.Up * _player.Camera.Position.Y, 0, 0);
				_player.Camera.GlobalPosition = start;
				_player.Camera.LookAt(center, Math.Abs(axis.Y) > .9f ? Vector3.Right : Vector3.Up);
				_player.Combat.QueueKnife(_player.ActiveWeapon!, false);
				await Frames(12);
				Check(target.Get("broken").AsBool() && !target.Visible && target.CollisionLayer == 0, id + ": weapon hit must remove rendering and collision");
				shot = true;
				break;
			}
			Check(shot, "No reachable weapon trace for " + id);
		}
		var protectedBox = root.GetNode<Node3D>("brush_58");
		Check(breakables.Count(n => n.Get("broken").AsBool()) == 10, "All eight wooden brushes and both metal vents must break through weapon hits");
		protectedBox.Call("source_damage", 999, protectedBox.GlobalPosition, Vector3.Zero);
		Check(!protectedBox.Get("broken").AsBool(), "Bomb-event boxes must ignore ordinary weapon damage");
		scene.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}
}
