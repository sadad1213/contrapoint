using Godot;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Contrapoint.Weapons;
using Contrapoint.Players;

// Capture the original renderer before changing it, then compare the same seeds,
// physics steps, atlas samples and quad transforms against the batched renderer.
public partial class ShootingEffectsChecks : Node3D
{
	private static readonly string[] Families = ["concrete", "wood", "metal", "brick", "tile", "glass", "dirt", "sand", "plaster", "plastic", "rock", "grass", "clay", "cardboard", "computer", "cloth", "rubber", "asphalt", "snow"];
	private Camera3D _camera = null!;
	private string _output = "";
	private int _checks;
	private static object Field(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(obj)!;
	private static object BaseField(object obj, string name) => typeof(CssoExplosion).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj)!;
	private void Check(bool condition, string message)
	{
		_checks++;
		if (!condition) throw new InvalidOperationException(message);
	}
	public override async void _Ready()
	{
		try
		{
			_output = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--effects-output="))?[17..] ?? "res://.godot/shooting-validation";
			DirAccess.MakeDirRecursiveAbsolute(_output);
			GetWindow().Size = new(1280, 720);
			GetWindow().ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
			_camera = new Camera3D { Current = true, Position = new(0, 1, 8), Far = 100 };
			AddChild(_camera);
			AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new(.12f, .14f, .17f), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White } });
			var wall = new StaticBody3D { Position = new(0, 1, -.05f) };
			wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(40, 40, .1f) } });
			wall.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new(40, 40, .1f) }, MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new(.44f, .42f, .38f) } });
			AddChild(wall);
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			if (OS.GetCmdlineUserArgs().Contains("--damage-checks")) await CheckDamage();
			else if (OS.GetCmdlineUserArgs().Contains("--effects-benchmark")) await Benchmark();
			else await Compare();
			GD.Print($"SHOOTING_EFFECTS_CHECKS checks={_checks} failures=0");
			GetTree().Quit();
		}
		catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
	}
	private async Task CheckDamage()
	{
		var capture = OS.GetCmdlineUserArgs().Contains("--capture-damage");
		var path = _output.PathJoin("blood-baseline.json");
		Dictionary<string, List<float[]>>? expected = null;
		if (!capture)
		{
			using var compressed = new MemoryStream(Godot.FileAccess.GetFileAsBytes("res://scenes/debug/fixtures/shooting_blood.json.gz"));
			using var stream = new GZipStream(compressed, CompressionMode.Decompress);
			expected = JsonSerializer.Deserialize<Dictionary<string, List<float[]>>>(stream)!;
		}
		var snapshots = new Dictionary<string, List<float[]>>();
		for (var kind = 0; kind < 2; kind++)
		{
			GD.Seed(1852);
			SourceDamageEffects.Spawn(this, new(0, 1, .5f), Vector3.Forward, kind == 0 ? 30 : 0, kind == 1);
			var effect = GetChildren().OfType<SourceDamageEffects>().Last();
			effect.SetProcess(false);
			var time = 0f;
			foreach (var target in kind == 0 ? new[] { .03f, .12f, .4f, .8f } : new[] { .03f, .12f, .18f })
			{
				while (time < target - .000001f) { var step = Math.Min(1f / 120, target - time); if (GodotObject.IsInstanceValid(effect) && !effect.IsQueuedForDeletion()) effect._Process(step); time += step; }
				var rows = new List<float[]>();
				foreach (var p in (IEnumerable)Field(effect, "_particles"))
				{
					var sprite = p.GetType().GetField("Sprite")?.GetValue(p) as Sprite3D;
					if (sprite != null)
					{
						var scale = sprite.Scale * new Vector3(sprite.Texture.GetWidth() * sprite.PixelSize, sprite.Texture.GetHeight() * sprite.PixelSize, 1);
						var c = sprite.Modulate;
						c = new(Mathf.Floor(c.R * 255) / 255, Mathf.Floor(c.G * 255) / 255, Mathf.Floor(c.B * 255) / 255, Mathf.Floor(c.A * 255) / 255);
						rows.Add(Values(new(_camera.GlobalBasis * Basis.FromScale(scale), sprite.GlobalPosition), c.SrgbToLinear(), Vector4.Zero, Vector4.Zero, Vector4.Zero, 0));
					}
					else
					{
						var batch = Field(p, "Batch"); var slot = (int)Field(p, "Slot");
						var multi = ((MultiMeshInstance3D)Field(batch, "Instance")).Multimesh;
						rows.Add(Values(multi.GetInstanceTransform(slot), multi.GetInstanceColor(slot), Vector4.Zero, Vector4.Zero, Vector4.Zero, 0));
					}
				}
				var key = FormattableString.Invariant($"{kind}/{target}");
				if (capture) snapshots.Add(key, rows);
				else
				{
					Check(rows.Count == expected![key].Count, "Blood count " + key);
					for (var p = 0; p < rows.Count; p++)
					for (var v = 0; v < rows[p].Length; v++) Check(Math.Abs(rows[p][v] - expected[key][p][v]) < .00003f, $"Blood {key}/{p}/{v}: {rows[p][v]} != {expected[key][p][v]}");
				}
				if (DisplayServer.GetName() != "headless")
				{
					await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
					await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
					GetViewport().GetTexture().GetImage().SavePng(_output.PathJoin($"blood_{(capture ? "before" : "after")}_{kind}_{target:F2}.png"));
				}
			}
			if (GodotObject.IsInstanceValid(effect)) effect.Free();
		}
		if (capture) Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write).StoreString(JsonSerializer.Serialize(snapshots));
	}
	private CssoImpact Create(string family, Vector3 normal, Vector3 position, ulong seed)
	{
		var effect = new CssoImpact();
		((RandomNumberGenerator)BaseField(effect, "_random")).Seed = seed;
		AddChild(effect);
		effect.Initialize(position, normal, family);
		effect.SetProcess(false); effect.SetPhysicsProcess(false);
		effect._PhysicsProcess(1.0 / 66);
		return effect;
	}
	private static float[] Values(Transform3D transform, Color tint, Vector4 frame, Vector4 next, Vector4 second, float blend) =>
	[
		transform.Basis.X.X, transform.Basis.X.Y, transform.Basis.X.Z,
		transform.Basis.Y.X, transform.Basis.Y.Y, transform.Basis.Y.Z,
		transform.Basis.Z.X, transform.Basis.Z.Y, transform.Basis.Z.Z,
		transform.Origin.X, transform.Origin.Y, transform.Origin.Z,
		tint.R, tint.G, tint.B, tint.A,
		frame.X, frame.Y, frame.Z, frame.W, next.X, next.Y, next.Z, next.W,
		second.X, second.Y, second.Z, second.W, blend
	];
	private static List<float[]> Snapshot(CssoImpact effect)
	{
		var result = new List<float[]>();
		foreach (var p in (IEnumerable)BaseField(effect, "_particles"))
		{
			var mesh = Field(p, "Mesh") as MeshInstance3D;
			if (mesh != null)
			{
				result.Add(!mesh.Visible ? [] : Values(mesh.GlobalTransform,
					mesh.GetInstanceShaderParameter("tint").AsColor().SrgbToLinear(),
					mesh.GetInstanceShaderParameter("frame_uv").AsVector4(), mesh.GetInstanceShaderParameter("next_uv").AsVector4(),
					mesh.GetInstanceShaderParameter("second_uv").AsVector4(), mesh.GetInstanceShaderParameter("frame_blend").AsSingle()));
				continue;
			}
			var slot = (int)Field(p, "Slot");
			if (slot < 0) { result.Add([]); continue; }
			var batch = Field(p, "Batch");
			var multi = ((MultiMeshInstance3D)Field(batch, "Instance")).Multimesh;
			var custom = multi.GetInstanceCustomData(slot);
			var rects = (Vector4[])Field(Field(batch, "Sheet"), "Rects");
			result.Add(Values(((MultiMeshInstance3D)Field(batch, "Instance")).GlobalTransform * multi.GetInstanceTransform(slot),
				multi.GetInstanceColor(slot), rects[(int)custom.R], rects[(int)custom.G], rects[(int)custom.B], custom.A));
		}
		return result;
	}
	private async Task Compare()
	{
		var baseline = OS.GetCmdlineUserArgs().Contains("--capture-baseline");
		var path = _output.PathJoin("baseline.json");
		var snapshots = new Dictionary<string, List<float[]>>();
		Dictionary<string, List<float[]>>? expected = null;
		if (!baseline)
		{
			using var compressed = new MemoryStream(Godot.FileAccess.GetFileAsBytes("res://scenes/debug/fixtures/shooting_particles.json.gz"));
			using var stream = new GZipStream(compressed, CompressionMode.Decompress);
			expected = JsonSerializer.Deserialize<Dictionary<string, List<float[]>>>(stream)!;
		}
		// All particles are in the frustum for the numerical comparison, including
		// flecks which have already fallen outside the screenshot camera.
		_camera.Position = new(0, 1, 10000); _camera.Far = 20000;
		var normals = new[] { Vector3.Back, Vector3.Right, Vector3.Up, Vector3.Down };
		foreach (var family in Families)
		for (var n = 0; n < normals.Length; n++)
		{
			var effect = Create(family, normals[n], new(0, 1, .01f), 1852);
			var time = 0f;
			foreach (var target in new[] { .008333334f, .03f, .12f, .4f, 1f, 2f })
			{
				while (time < target - .000001f) { var step = Math.Min(1f / 120, target - time); effect._Process(step); time += step; }
				var key = FormattableString.Invariant($"{family}/{n}/{target}");
				var actual = Snapshot(effect);
				if (baseline) snapshots.Add(key, actual);
				else
				{
					Check(actual.Count == expected![key].Count, key + " particle count");
					for (var p = 0; p < actual.Count; p++)
					{
						var reference = expected[key][p];
						Check(actual[p].Length == reference.Length, $"{key} particle {p} visibility");
						for (var v = 0; v < reference.Length; v++)
							Check(Math.Abs(actual[p][v] - reference[v]) < .00003f, $"{key} particle {p} value {v}: {actual[p][v]} != {reference[v]}");
					}
				}
			}
			effect._Process(13);
			Check(effect.IsQueuedForDeletion(), family + " expires");
			effect.Free();
		}
		if (baseline) Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write).StoreString(JsonSerializer.Serialize(snapshots));
		GD.Print($"PARTICLE_PARITY baseline={baseline} samples={Families.Length * normals.Length * 6}");
		_camera.Position = new(0, 1, 8); _camera.Far = 100;
		if (DisplayServer.GetName() != "headless")
		{
			var effects = new[] { "concrete", "wood", "metal" }.Select((f, i) => Create(f, Vector3.Back, new((i - 1) * 2.7f, 1.4f, .01f), (ulong)(1852 + i))).ToArray();
			var time = 0f;
			foreach (var target in new[] { .03f, .12f, .4f, 1f })
			{
				while (time < target - .000001f) { var step = Math.Min(1f / 120, target - time); foreach (var effect in effects) effect._Process(step); time += step; }
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
				GetViewport().GetTexture().GetImage().SavePng(_output.PathJoin($"{(baseline ? "before" : "after")}_{target:F2}.png"));
			}
			foreach (var effect in effects) effect.Free();
		}
		if (!baseline)
		{
			CheckCulling();
			if (DisplayServer.GetName() != "headless") await CheckOcclusion();
		}
	}
	private async Task CheckOcclusion()
	{
		GetViewport().UseOcclusionCulling = true;
		var occluder = new OccluderInstance3D { Position = new(0, 1, 4), Occluder = new BoxOccluder3D { Size = new(40, 40, .2f) } };
		AddChild(occluder);
		var effect = Create("metal", Vector3.Back, new(0, 1, .01f), 1852);
		effect._Process(.04);
		async Task<int> Draws()
		{
			await ToSignal(GetTree().CreateTimer(.25), SceneTreeTimer.SignalName.Timeout);
			for (var i = 0; i < 8; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
			return GetViewport().GetRenderInfo(Viewport.RenderInfoType.Visible, Viewport.RenderInfo.DrawCallsInFrame);
		}
		var hidden = await Draws();
		occluder.Visible = false;
		var visible = await Draws();
		Check(hidden < visible, $"Occluded batches are culled by the renderer: hidden={hidden}, visible={visible}");
		GD.Print($"PARTICLE_OCCLUSION hidden_draws={hidden} visible_draws={visible}");
		effect.Free(); occluder.Free();
	}
	private void CheckCulling()
	{
		var reference = Create("concrete", Vector3.Back, new(0, 1, .01f), 1852);
		var hidden = Create("concrete", Vector3.Back, new(0, 1, .01f), 1852);
		reference._Process(.04);
		_camera.Rotation = new(0, Mathf.Pi, 0);
		hidden._Process(.04);
		Check(hidden.GetChildren().OfType<MultiMeshInstance3D>().All(m => m.Multimesh.VisibleInstanceCount == 0), "No particles submitted behind the camera");
		_camera.Rotation = Vector3.Zero;
		reference._Process(.01); hidden._Process(.01);
		var expected = Snapshot(reference); var actual = Snapshot(hidden);
		for (var p = 0; p < actual.Count; p++)
		{
			Check(actual[p].Length == expected[p].Length, "Re-entry visibility");
			for (var v = 0; v < actual[p].Length; v++) Check(Math.Abs(actual[p][v] - expected[p][v]) < .00003f, "Hidden particles advance without resetting on re-entry");
		}
		Check(actual.Any(p => p.Length > 0), "Particles reappear after turning the camera");
		reference.Free(); hidden.Free();
		// The emitter may be outside the screen while the edge of a large puff is visible.
		var edge = Create("concrete", Vector3.Back, new(12f, 1, .01f), 1852);
		foreach (var p in (IEnumerable)BaseField(edge, "_particles"))
		{
			p.GetType().GetField("Radius")!.SetValue(p, 5f);
			p.GetType().GetField("Trail")!.SetValue(p, 0f);
		}
		Check(!_camera.IsPositionInFrustum(edge.GlobalPosition), "Edge test emitter is outside the frustum");
		edge._Process(.04);
		Check(edge.GetChildren().OfType<MultiMeshInstance3D>().Any(m => m.Multimesh.VisibleInstanceCount > 0), "Partially visible quads survive frustum culling");
		edge.Free();
	}
	private async Task Benchmark()
	{
		Engine.MaxFps = 0;
		DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
		foreach (var hidden in new[] { false, true })
		{
			var effects = new List<CssoImpact>();
			var samples = new List<double>();
			var wallTimes = new List<double>();
			var draws = 0d;
			ulong previous = Time.GetTicksUsec();
			for (var frame = 0; frame < 480; frame++)
			{
				var start = Time.GetTicksUsec();
				if (frame % 12 == 0)
					effects.Add(Create(Families[(frame / 12) % 3], Vector3.Back, new((frame / 12 % 5 - 2) * 1.2f, 1.4f, hidden ? 16 : .01f), (ulong)(frame * 10)));
				foreach (var effect in effects) effect._Process(1.0 / 120);
				effects.RemoveAll(e => { if (!e.IsQueuedForDeletion()) return false; e.Free(); return true; });
				var cost = (Time.GetTicksUsec() - start) / 1000.0;
				await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
				var now = Time.GetTicksUsec();
				if (frame >= 120) { samples.Add(cost); wallTimes.Add((now - previous) / 1000.0); draws += GetViewport().GetRenderInfo(Viewport.RenderInfoType.Visible, Viewport.RenderInfo.DrawCallsInFrame); }
				previous = now;
			}
			samples.Sort(); wallTimes.Sort();
			GD.Print("SHOOTING_BENCH " + JsonSerializer.Serialize(new { hidden, cpu_ms = samples.Average(), cpu_p95_ms = samples[(int)(samples.Count * .95)], fps = 1000 / wallTimes.Average(), frame_p95_ms = wallTimes[(int)(wallTimes.Count * .95)], draw_calls = draws / samples.Count }));
			foreach (var effect in effects) effect.Free();
		}
	}
}
