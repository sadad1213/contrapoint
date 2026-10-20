using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Contrapoint.UI;

// Global rendering preferences also serve menus and newly instantiated maps/weapons.
public partial class GamePreferences : Node
{
	public sealed record Definition(string Name, float Default, float Min, float Max, bool Integer, string Help);
	public static readonly Definition[] Definitions =
	[
		new("cp_window_mode", 1, 0, 2, true, "0 windowed, 1 fullscreen, 2 borderless fullscreen."),
		new("cp_screen_width", 1280, 640, 7680, true, "Window / fullscreen 3D rendering width."),
		new("cp_screen_height", 720, 480, 4320, true, "Window / fullscreen 3D rendering height."),
		new("cp_resolution_override", 0, 0, 1, true, "Only scale 3D resolution after the user explicitly selects a resolution."),
		new("r_rootlod", 0, 0, 2, true, "Mesh detail: 0 high, 1 medium, 2 low; affects meshes with imported LODs."),
		new("mat_picmip", 0, 0, 2, true, "Texture mip bias: 0 high, 1 medium, 2 low."),
		new("cp_shadow_quality", 2, 0, 2, true, "Dynamic shadows: low, medium, high. Baked map lighting is preserved."),
		new("cp_texture_filter", 5, 0, 5, true, "Bilinear, trilinear, anisotropic 2x / 4x / 8x / 16x."),
		new("cp_antialias", 2, 0, 3, true, "MSAA disabled, 2x, 4x, 8x (world and first-person weapon)."),
		new("cp_water_quality", 1, 0, 1, true, "Simple water or refractive water with cubemap reflection."),
		new("mat_monitorgamma", 2.2f, 1.6f, 2.6f, false, "Brightness, applied to scene exposure; lower is brighter."),
		new("snd_mute_losefocus", 0, 0, 1, true, "Mute the master bus while the game window is unfocused."),
		new("con_enable", 1, 0, 1, true, "Enable the developer-console key binding."),
		new("r_drawtracers_firstperson", 1, 0, 1, true, "Draw local first-person bullet tracers."),
		new("cl_new_impact_effects", 1, 0, 1, true, "Draw CSSO impact particles; bullet-hole decals remain enabled.")
	];
	private static readonly Dictionary<string, float> Values = new(StringComparer.OrdinalIgnoreCase);
	public static GamePreferences? Instance { get; private set; }
	private readonly HashSet<Node> _newNodes = new();
	private readonly Dictionary<string, Shader> _lightmapShaders = new();
	private readonly HashSet<ulong> _appliedMaterials = new();
	private bool _dirty = true;
	private bool _displayDirty = true;
	private int _appliedWindowMode = -1;
	public static float Value(string name, float fallback = 0) => Values.GetValueOrDefault(name, fallback);
	public void NormalizeAutomaticResolution()
	{
		if (Value("cp_resolution_override") != 0 || Value("cp_window_mode") == 0 || DisplayServer.GetName() == "headless") return;
		var native = DisplayServer.ScreenGetSize();
		Values["cp_screen_width"] = native.X;
		Values["cp_screen_height"] = native.Y;
	}
	public static void Set(string name, float value)
	{
		if (Values.TryGetValue(name, out var old) && old == value) return;
		Values[name] = value;
		if (Instance == null) return;
		Instance._dirty = true;
		if (name.StartsWith("cp_screen_") || name is "cp_window_mode" or "cp_resolution_override") Instance._displayDirty = true;
	}
	public override void _Ready()
	{
		Instance = this;
		ProcessMode = ProcessModeEnum.Always;
		foreach (var d in Definitions) Values.TryAdd(d.Name, d.Default);
		ConsoleCommands.InitializeMenuOptions();
		if (OS.GetCmdlineUserArgs().Contains("--options-test-profile") && OS.GetCmdlineUserArgs().Contains("--options-offscreen"))
		{
			Values["cp_window_mode"] = 0;
			Values["cp_screen_width"] = GetWindow().Size.X;
			Values["cp_screen_height"] = GetWindow().Size.Y;
			Values["cp_resolution_override"] = 1;
		}
		// The old 1280x720 defaults described a window, not a render scale.
		// Migrate configs written before explicit resolution selection existed.
		NormalizeAutomaticResolution();
		_appliedWindowMode = GetWindow().Mode is Window.ModeEnum.Fullscreen or Window.ModeEnum.ExclusiveFullscreen ? 1 : 0;
		GetTree().NodeAdded += QueueNode;
		GetWindow().SizeChanged += UpdateRenderResolution;
		ApplyNow();
	}
	public override void _ExitTree()
	{
		GetTree().NodeAdded -= QueueNode;
		GetWindow().SizeChanged -= UpdateRenderResolution;
		if (Instance == this) Instance = null;
	}
	private void QueueNode(Node node)
	{
		if (node is Viewport or MeshInstance3D or Light3D or WorldEnvironment) _newNodes.Add(node);
	}
	public override void _Process(double delta)
	{
		if (_dirty) ApplyNow();
		else
		{
			_appliedMaterials.Clear();
			foreach (var node in _newNodes)
				if (GodotObject.IsInstanceValid(node) && node.IsInsideTree()) ApplyNode(node);
			_newNodes.Clear();
		}
		AudioServer.SetBusMute(0, Value("snd_mute_losefocus") != 0 && DisplayServer.GetName() != "headless" && !GetWindow().HasFocus());
	}
	public void ApplyNow()
	{
		_dirty = false;
		_appliedMaterials.Clear();
		if (_displayDirty && DisplayServer.GetName() != "headless")
		{
			_displayDirty = false;
			var window = GetWindow();
			var mode = (int)Value("cp_window_mode");
			// Changing texture/AA preferences must never leave and re-enter fullscreen.
			// Resolution edits in fullscreen only change the 3D buffer, not the OS mode.
			if (_appliedWindowMode != mode)
			{
				window.Borderless = mode == 2;
				window.Mode = mode == 0 ? Window.ModeEnum.Windowed : Window.ModeEnum.Fullscreen;
				_appliedWindowMode = mode;
			}
			if (mode == 0)
			{
				var requested = new Vector2I((int)Value("cp_screen_width"), (int)Value("cp_screen_height"));
				if (window.Size != requested) window.Size = requested;
			}
		}
		UpdateRenderResolution();
		var quality = (int)Value("cp_shadow_quality");
		RenderingServer.DirectionalShadowAtlasSetSize(quality == 2 ? 4096 : 2048, true);
		var filter = quality == 2 ? RenderingServer.ShadowQuality.SoftMedium : RenderingServer.ShadowQuality.Hard;
		RenderingServer.DirectionalSoftShadowFilterSetQuality(filter);
		RenderingServer.PositionalSoftShadowFilterSetQuality(filter);
		Visit(GetTree().Root);
		_newNodes.Clear();
	}
	private void UpdateRenderResolution()
	{
		var window = GetWindow();
		// Godot's fullscreen window uses the monitor's native mode. Scale the 3D
		// framebuffer to the selected resolution without lowering UI readability.
		window.Scaling3DScale = Value("cp_resolution_override") == 0 || Value("cp_window_mode") == 0 ? 1 : Mathf.Clamp(Mathf.Min(
			Value("cp_screen_width") / Math.Max(1, window.Size.X), Value("cp_screen_height") / Math.Max(1, window.Size.Y)), .25f, 2f);
	}
	private void Visit(Node node)
	{
		ApplyNode(node);
		foreach (var child in node.GetChildren()) Visit(child);
	}
	public void ApplyNode(Node node)
	{
		if (node is Viewport viewport)
		{
			viewport.Msaa3D = (Viewport.Msaa)(int)Value("cp_antialias");
			viewport.MeshLodThreshold = Value("r_rootlod") switch { 2 => 8, 1 => 4, _ => 1 };
			viewport.TextureMipmapBias = Value("mat_picmip");
			viewport.AnisotropicFilteringLevel = (Viewport.AnisotropicFiltering)(int)Math.Clamp(Value("cp_texture_filter") - 1, 0, 4);
			viewport.PositionalShadowAtlasSize = Value("cp_shadow_quality") == 2 ? 4096 : 2048;
		}
		if (node is Light3D light)
		{
			if (!light.HasMeta("options_original_shadow")) light.SetMeta("options_original_shadow", light.ShadowEnabled);
			light.ShadowEnabled = light.GetMeta("options_original_shadow").AsBool() && Value("cp_shadow_quality") > 0;
		}
		if (node is WorldEnvironment world && world.Environment is { } environment)
		{
			if (!environment.HasMeta("options_original_exposure")) environment.SetMeta("options_original_exposure", environment.TonemapExposure);
			environment.TonemapExposure = environment.GetMeta("options_original_exposure").AsSingle() * (2.2f / Value("mat_monitorgamma", 2.2f));
		}
		if (node is MeshInstance3D mesh && mesh.Mesh != null)
			for (var i = 0; i < mesh.Mesh.GetSurfaceCount(); i++) ApplyMaterial(mesh.GetActiveMaterial(i));
	}
	private void ApplyMaterial(Material? material)
	{
		if (material == null || !_appliedMaterials.Add(material.GetInstanceId())) return;
		var mode = (int)Value("cp_texture_filter");
		if (material is BaseMaterial3D standard)
			standard.TextureFilter = mode switch { 0 or 1 => BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
				_ => BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic };
		if (material is not ShaderMaterial custom || custom.Shader == null) return;
		if (!custom.HasMeta("options_original_shader")) custom.SetMeta("options_original_shader", custom.Shader);
		var original = custom.GetMeta("options_original_shader").As<Shader>();
		// Packed .scn maps also embed two-sided and transparent copies of the
		// Source shader. Identify its interface, rather than only an external path.
		if (original.Code.Contains("uniform sampler2D lightmap_texture") && original.Code.Contains("uniform sampler2D base_texture"))
		{
			if (!_lightmapShaders.TryGetValue(original.Code, out var shader))
			{
				if (original.Code.Contains("settings_filter_mode")) shader = original;
				else
				{
					var canonical = GD.Load<Shader>("res://assets/maps/source_lightmap.gdshader").Code;
					var begin = canonical.IndexOf("// SETTINGS_BEGIN", StringComparison.Ordinal);
					var end = canonical.IndexOf("// SETTINGS_END", StringComparison.Ordinal);
					var block = canonical[begin..end];
					var code = original.Code.Replace("uniform float settings_texture_bias = 0.0;", "")
						.Replace("void fragment()", block + "\nvoid fragment()")
						.Replace("texture(base_texture, UV, settings_texture_bias)", "settings_sample(base_texture, settings_base_linear, UV)")
						.Replace("texture(second_texture, UV, settings_texture_bias)", "settings_sample(second_texture, settings_second_linear, UV)")
						.Replace("texture(base_texture, UV)", "settings_sample(base_texture, settings_base_linear, UV)")
						.Replace("texture(second_texture, UV)", "settings_sample(second_texture, settings_second_linear, UV)");
					shader = new Shader { Code = code };
				}
				_lightmapShaders[original.Code] = shader;
			}
			custom.Shader = shader;
			custom.SetShaderParameter("settings_texture_bias", Value("mat_picmip"));
			custom.SetShaderParameter("settings_filter_mode", mode);
			custom.SetShaderParameter("settings_base_linear", custom.GetShaderParameter("base_texture"));
			custom.SetShaderParameter("settings_second_linear", custom.GetShaderParameter("second_texture"));
		}
		else if (original.ResourcePath.EndsWith("source_water.gdshader"))
			custom.SetShaderParameter("settings_simple_water", Value("cp_water_quality") == 0);
	}
}
