using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.UI;

public partial class OptionsMapChecks : Node3D
{
	private int _checks;
	private Image? _firstLow;
	private Image? _high;
	private void Check(bool condition, string message)
	{
		_checks++;
		if (!condition) throw new InvalidOperationException(message);
	}
	private async Task Frames(int count = 3)
	{
		for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}
	public override void _Ready() => CallDeferred(MethodName.Run);
	private async void Run()
	{
		try
		{
			if (!OS.GetCmdlineUserArgs().Contains("--options-test-profile")) throw new InvalidOperationException("Use --options-test-profile.");
			var renderer = DisplayServer.GetName() != "headless";
			GetViewport().UseOcclusionCulling = false;
			if (renderer && !OS.GetCmdlineUserArgs().Contains("--options-native-preview")) { GetWindow().Mode = Window.ModeEnum.Windowed; GetWindow().Size = new(1280,720); }
			if (GamePreferences.Value("cp_resolution_override") == 0)
				Check(GetViewport().Scaling3DScale == 1, "Native 3D resolution remains unchanged at startup.");
			GamePreferences.Set("mat_picmip", 2); GamePreferences.Set("cp_texture_filter", 0);
			var started = Time.GetTicksMsec();
			var mapId = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--options-map="))?.Split('=')[1] ?? "de_dust2";
			var map = GD.Load<PackedScene>($"res://assets/maps/{mapId}/{mapId}.scn").Instantiate<Node3D>();
			AddChild(map); await Frames();
			foreach (var body in map.FindChildren("*", "RigidBody3D", true, false).Cast<RigidBody3D>()) body.Freeze = true;
			var materials = map.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>()
				.Where(m => m.Mesh != null).SelectMany(m => Enumerable.Range(0, m.Mesh.GetSurfaceCount()).Select(m.GetActiveMaterial))
				.OfType<ShaderMaterial>().DistinctBy(m => m.GetInstanceId()).Where(m => m.Shader.Code.Contains("uniform sampler2D lightmap_texture")).ToArray();
			Check(materials.Length > 50, "Inspect real map materials rather than a synthetic surface.");
			Check(materials.All(m => m.Shader.Code.Contains("settings_filter_mode") && m.GetShaderParameter("settings_filter_mode").AsInt32() == 0
				&& m.GetShaderParameter("settings_texture_bias").AsSingle() == 2), "Every real map material, including embedded shaders, keeps low textures with bilinear filtering.");
			Check(materials.All(m => m.GetShaderParameter("settings_base_linear").AsGodotObject() == m.GetShaderParameter("base_texture").AsGodotObject()), "Bilinear samplers receive the original mipmapped textures.");
			var shaderIds = materials.ToDictionary(m => m.GetInstanceId(), m => m.Shader.GetInstanceId());
			var camera = CreateCloseupCamera(map);
			if (renderer) await Screenshot("low");
			GamePreferences.Set("mat_picmip", 0); GamePreferences.Set("cp_texture_filter", 5); GamePreferences.Instance!.ApplyNow();
			await Frames(); if (renderer) await Screenshot("high");
			Check(materials.All(m => m.GetShaderParameter("settings_texture_bias").AsSingle() == 0), "High texture quality applies to the loaded map.");
			GamePreferences.Set("mat_picmip", 2); GamePreferences.Set("cp_texture_filter", 0); GamePreferences.Instance.ApplyNow();
			await Frames();
			Check(materials.All(m => m.GetShaderParameter("settings_texture_bias").AsSingle() == 2), "Switching back to Low applies without reloading the map.");
			Check(materials.All(m => m.Shader.GetInstanceId() == shaderIds[m.GetInstanceId()] ), "Quality/filter changes reuse shader programs instead of triggering new shader compilation.");
			if (renderer) await Screenshot("low_again");
			GD.Print($"OPTIONS_MAP_CHECKS map={mapId} checks={_checks} failures=0 materials={materials.Length} shaders={shaderIds.Values.Distinct().Count()} ms={Time.GetTicksMsec()-started}");
			GetTree().Quit();
		}
		catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
	}
	private Camera3D CreateCloseupCamera(Node3D map)
	{
		var camera = new Camera3D { Near = .01f, Fov = 70 };
		AddChild(camera);
		foreach (var mesh in map.FindChildren("*", "MeshInstance3D", true, false).Cast<MeshInstance3D>())
		{
			if (mesh.Mesh == null) continue;
			for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
			{
				if (mesh.GetActiveMaterial(surface) is not ShaderMaterial material || !material.Shader.Code.Contains("lightmap_texture")
					|| material.Shader.Code.Contains("ALPHA =")) continue;
				var arrays = mesh.Mesh.SurfaceGetArrays(surface);
				var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
				var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
				var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
				for (var i = 0; i + 2 < indices.Length; i += 3)
				{
					var a = mesh.GlobalTransform * vertices[indices[i]]; var b = mesh.GlobalTransform * vertices[indices[i+1]]; var c = mesh.GlobalTransform * vertices[indices[i+2]];
					var cross = (b-a).Cross(c-a);
					if (cross.Length() < 4) continue;
					var normal = (mesh.GlobalBasis * normals[indices[i]]).Normalized(); var center = (a+b+c)/3;
					if (MathF.Abs(normal.Y) > .2f) continue;
					camera.GlobalPosition = center + normal * .6f;
					camera.LookAt(center, MathF.Abs(normal.Dot(Vector3.Up)) > .9f ? Vector3.Right : Vector3.Up);
					camera.MakeCurrent(); return camera;
				}
			}
		}
		camera.Position = new(0,3,0); camera.MakeCurrent(); return camera;
	}
	private async Task Screenshot(string name)
	{
		await Frames(8);
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		var image = GetViewport().GetTexture().GetImage();
		image.SavePng("user://options_map_texture_" + name + ".png");
		if (name == "low") _firstLow = image;
		else if (name == "high") _high = image;
		else if (name == "low_again")
		{
			Check(!_firstLow!.GetData().SequenceEqual(_high!.GetData()), "Rendered Low and High textures are visibly different.");
			var first = _firstLow.GetData(); var again = image.GetData();
			// Fullscreen GPU dithering can change a single byte by one level.
			Check(first.Length == again.Length && first.Zip(again).Sum(p => Math.Abs(p.First - p.Second)) < first.Length / 10000,
				"Returning to Low restores the same rendered image within GPU dithering tolerance.");
		}
	}
}
