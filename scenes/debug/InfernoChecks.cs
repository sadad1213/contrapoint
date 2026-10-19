using Godot;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Contrapoint.Players;
using Contrapoint.Weapons;

public partial class InfernoChecks : Node3D
{
	private int _checks;
	private Player _player = null!;
	private Node3D _map = null!;
	private void Check(bool condition, string message)
	{
		_checks++;
		if (!condition) throw new InvalidOperationException(message);
	}
	public override async void _Ready()
	{
		try
		{
			await Run();
			GD.Print($"INFERNO_CHECKS_OK checks={_checks}");
			GetTree().Quit();
		}
		catch (Exception error)
		{
			GD.PushError(error.ToString());
			GetTree().Quit(1);
		}
	}
	private async Task Frames(int count, bool move = false)
	{
		for (var i = 0; i < count; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			if (move) _player.Simulate(default, 1f / 66);
		}
	}
	private static Vector3 Vector(JsonElement p) => new(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle());
	private static Vector3 Source(string p)
	{
		var v = p.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
		return new Vector3(v[0], v[2], -v[1]) * SourceMovement.Unit;
	}
	private async Task Run()
	{
		var data = JsonDocument.Parse(FileAccess.GetFileAsString("res://assets/maps/de_inferno/source/map_data.json")).RootElement;
		_map = GD.Load<PackedScene>("res://assets/maps/de_inferno/de_inferno.scn").Instantiate<Node3D>();
		AddChild(_map);
		_player = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		_player.LocalInput = false;
		AddChild(_player);
		_player.Combat.SetPhysicsProcess(false);
		foreach (var weapon in _player.Inventory.Weapons) weapon.SetProcess(false);
		await Frames(2);
		var spawns = _map.GetChildren().OfType<Marker3D>().ToArray();
		Check(spawns.Length == 40, "All 40 original spawn points must be present.");
		foreach (var marker in spawns)
		{
			var query = PhysicsRayQueryParameters3D.Create(marker.GlobalPosition + Vector3.Up * 0.3f, marker.GlobalPosition + Vector3.Down * 3, 1);
			query.Exclude = new() { _player.GetRid() };
			Check(GetWorld3D().DirectSpaceState.IntersectRay(query).Count > 0, "No floor under " + marker.Name);
		}
		_player.GlobalPosition = spawns[0].GlobalPosition + Vector3.Up * 0.04f;
		_player.Movement.Reset();
		await Frames(120, true);
		Check(_player.IsOnFloor() && _player.GlobalPosition.IsFinite(), "Player must settle at the original T spawn.");
		Check(_map.GetNode<StaticBody3D>("PlayerClips").CollisionLayer == 2, "Player clips must use a separate movement layer.");
		Check(_map.GetNode<StaticBody3D>("WorldBrushes").GetChildCount() == data.GetProperty("stats").GetProperty("world_collision_brushes").GetInt32(), "All compiled solid brushes, including nodraw sides, must collide.");
		var meshes = _map.FindChildren("Chunk_*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().ToArray();
		long triangles = 0;
		foreach (var mesh in meshes)
		{
			var material = (ShaderMaterial)mesh.Mesh.SurfaceGetMaterial(0);
			if (!material.ResourceName.EndsWith("_water"))
				Check(material.GetShaderParameter("base_texture").AsGodotObject() != null, "Missing base texture: " + mesh.Name);
			triangles += ((ArrayMesh)mesh.Mesh).SurfaceGetArrayLen(0) / 3;
			if (material.ResourceName.EndsWith("_water"))
			{
				Check(mesh.GetChildren().OfType<StaticBody3D>().Count() == 0, "Water must not be a solid collision surface.");
				Check(material.GetShaderParameter("normal_frames").AsGodotObject() is Texture2D, "Water animation is missing.");
			}
		}
		long expected = 0;
		foreach (var group in data.GetProperty("groups").EnumerateArray())
		{
			var owner = group.GetProperty("owner").GetString()!;
			if (owner != "" && data.GetProperty("entities").GetProperty(owner).TryGetProperty("animated_scene", out _)) continue;
			expected += group.GetProperty("vertices").GetInt64() / 3;
		}
		Check(triangles == expected, "Imported mesh triangle count differs from BSP extraction.");
		foreach (var range in data.GetProperty("displacement_ranges").EnumerateArray())
		{
			var mesh = _map.GetNode<MeshInstance3D>($"Chunk_{range.GetProperty("group").GetInt32():D4}").Mesh;
			var uvs = mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
			var low = range.GetProperty("uv_min"); var high = range.GetProperty("uv_max");
			var first = range.GetProperty("first_vertex").GetInt32();
			var count = range.GetProperty("vertices").GetInt32();
			Check(uvs.Skip(first).Take(count).All(uv => uv.X >= low[0].GetSingle() - .00001f && uv.Y >= low[1].GetSingle() - .00001f && uv.X <= high[0].GetSingle() + .00001f && uv.Y <= high[1].GetSingle() + .00001f), "Displacement lightmap samples another surface.");
		}
		Check(GetTree().GetNodesInGroup("source_water").Count == 2, "Both water volumes must be present.");
		foreach (var hull in data.GetProperty("water_hulls").EnumerateArray())
		{
			var points = hull.EnumerateArray().Select(Vector).ToArray();
			var center = points.Aggregate(Vector3.Zero, (a, b) => a+b) / points.Length;
			_player.GlobalPosition = center;
			_player.Movement.Reset();
			_player.Simulate(default, 1f / 66);
			Check(_player.Movement.WaterLevel == 1, "Original shallow pools must wet feet without forcing swimming.");
			_player.GlobalPosition = new(center.X, points.Max(p => p.Y) + .1f, center.Z);
			_player.Simulate(default, 1f / 66);
			Check(_player.Movement.WaterLevel == 0, "Water detection must end at the BSP surface.");
		}
		_player.GlobalPosition = new(0, 30, 0);
		var door = _map.GetNode<AnimatableBody3D>("brush_2");
		var closed = door.GlobalTransform;
		door.Call("source_input", "Open");
		await Frames(30);
		Check(door.Get("progress").AsSingle() > .99f && !door.GlobalTransform.IsEqualApprox(closed), "The rotating door must open around its original hinge.");
		Check(door.GetChildCount() > 3, "The moving door must retain both handles.");
		door.Call("source_input", "Close");
		await Frames(30);
		Check(door.GlobalTransform.IsEqualApprox(closed), "The door must close to its original transform.");
		_map.GetNode("brush_18").Call("on_entered", _player);
		await Frames(30);
		Check(door.Get("progress").AsSingle() > .99f, "Door touch trigger must execute the original entity output.");
		var fan = _map.GetNode<AnimatableBody3D>("brush_7");
		var rotation = fan.Rotation;
		await Frames(10);
		Check(!fan.Rotation.IsEqualApprox(rotation) && fan.GetChildren().OfType<AnimatableBody3D>().Any(), "Fan blades must be parented and rotating.");
		var bodies = _map.GetChildren().OfType<RigidBody3D>().ToArray();
		Check(bodies.Length == 199 && bodies.All(b => b.GetChildren().OfType<CollisionShape3D>().Any()), "All physics props must have original collision hulls.");
		var prop = bodies.First(b => !b.Freeze && b.Mass < 5);
		prop.Call("source_damage", 30, prop.GlobalPosition, Vector3.Right);
		await Frames(2);
		Check(prop.LinearVelocity.Length() > .01f, "Shots must impart force to physics props.");
		Check(_map.GetChildren().OfType<PinJoint3D>().Count() == 15, "Hanging lamps must retain their attachments.");
		Check(GetTree().GetNodesInGroup("source_bomb_target").Count == 2 && GetTree().GetNodesInGroup("source_buyzone").Count == 2, "Original bomb and buy volumes must be retained.");
		// Fire a real queued bullet into each original window, verifying combat dispatch.
		_player.Inventory.EquipSlot(WeaponSlot.Primary);
		var rifle = (FirearmView)_player.ActiveWeapon!;
		rifle.SpreadScale = 0;
		foreach (var pair in data.GetProperty("entities").EnumerateObject())
		{
			if (pair.Value.GetProperty("classname").GetString() != "func_breakable_surf") continue;
			var pane = _map.GetNode<AnimatableBody3D>(pair.Name);
			// Aim within one pane; the original frame has crossbars at its center.
			var center = (Source(pair.Value.GetProperty("upperleft").GetString()!) + Source(pair.Value.GetProperty("lowerright").GetString()!)) / 2 + new Vector3(0, .2f, .2f);
			_player.GlobalPosition = center - Vector3.Right * .3f - Vector3.Up * SourceMovement.StandingEye;
			_player.Recoil.Reset();
			_player.Camera.LookAt(center);
			await Frames(1);
			_player.Combat.QueueBullet(rifle, 35);
			_player.Combat._PhysicsProcess(.02);
			Check(pane.Get("broken").AsBool() && pane.CollisionLayer == 0, "Bullet must break window " + pair.Name);
		}
		Check(_map.GetNode("Soundscapes").Get("definitions").AsGodotDictionary().Count > 0, "Original soundscape definitions must be present.");
		Check(_map.GetNode("FishPool").GetChildCount() == 10, "The fountain must contain all ten original fish.");
		Check(_map.FindChildren("Rope_*", "MeshInstance3D", true, false).Count == 53, "All original rope segments must be present.");
		Check(_map.FindChildren("Glow_*", "MeshInstance3D", true, false).Count == 7 && _map.FindChildren("Smoke_*", "MeshInstance3D", true, false).Count == 1, "Lamp glows and the chimney smoke beam must be present.");
		var gate = (Node3D)GetTree().GetNodesInGroup("source_target_prop_elevatordoor_top_1")[0];
		var animation = gate.FindChildren("*", "AnimationPlayer", true, false).OfType<AnimationPlayer>().Single();
		_map.GetNode("brush_15").Call("on_entered", _player);
		await Frames(12);
		Check(animation.IsPlaying() && animation.CurrentAnimation.ToString().EndsWith("open"), "The gate trigger must play the original open animation.");
		await Frames(80);
		Check(_map.GetNodeOrNull("brush_14") == null && _map.GetNodeOrNull("brush_33") == null, "All duplicate delayed trigger outputs must remove the gate blockers.");
		_player.GlobalPosition = spawns[0].GlobalPosition + Vector3.Up * .1f;
		_player.Recoil.Reset();
		_player.Camera.Rotation = new(-Mathf.Pi * .5f, 0, 0);
		await Frames(1);
		var marks = _player.Combat.MarksCreated;
		_player.Combat.QueueBullet(rifle, 35);
		_player.Combat._PhysicsProcess(.02);
		Check(_player.Combat.MarksCreated == marks + 1, "World brush hits must resolve their per-shape surface and create an impact mark.");
		var pool = data.GetProperty("water_hulls")[1].EnumerateArray().Select(Vector).ToArray();
		var waterPoint = pool.Aggregate(Vector3.Zero, (a, b) => a+b) / pool.Length;
		Check(SourceWaterRipple.Spawn(_map, waterPoint + Vector3.Up, waterPoint + Vector3.Down), "Water ray contacts must generate a surface ripple.");
	}
}
