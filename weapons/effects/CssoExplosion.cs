using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Contrapoint.Weapons;

// CSSO's PCF children, using the original VTF sheets and Source-unit parameters.
// Source operators have equivalents here; camera-only overlays are omitted.
public partial class CssoExplosion : Node3D
{
	protected virtual string Root => "res://assets/effects/csso_explosion/";
	protected virtual string DataFile => "explosion.json";
	protected virtual bool IsImpact => false;
	protected virtual Basis EmissionBasis => Basis.Identity;
	private const float Unit = 0.0254f;
	private static readonly Dictionary<string, JsonDocument> Documents = new();
	protected JsonDocument Data
	{
		get
		{
			var path = Root + DataFile;
			if (!Documents.TryGetValue(path, out var data)) Documents[path] = data = JsonDocument.Parse(FileAccess.GetFileAsString(path));
			return data;
		}
	}
	private static readonly Dictionary<string, ShaderMaterial> Materials = new();
	private static readonly QuadMesh Quad = new() { Size = Vector2.One };
	private readonly List<Particle> _particles = new();
	private readonly RandomNumberGenerator _random = new();
	private float _age;
	private bool _initialized;

	private sealed class Particle
	{
		public MeshInstance3D Mesh = null!;
		public JsonElement Sequence, SecondSequence;
		public Vector3 Origin, Velocity, Gravity;
		public Color Color;
		public float Birth, Life, Radius, StartScale, EndScale, ScaleEnd, Bias, Drag;
		public float FadeIn, FadeOut, Rotation, Spin, AnimationRate, SecondRate, Trail, TrailFadeIn;
		public float CullDistance, CullPlane, MaxTrail;
		public bool SurfaceAligned;
		public float RetireAge = float.PositiveInfinity;
		public float RetireSeconds;
		public bool Refract, Collide, FitAnimation, FpsAnimation;
	}

	protected static float F(JsonElement e, string key, float fallback = 0) =>
		e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var value) ? value.GetSingle() : fallback;
	private static bool B(JsonElement e, string key, bool fallback = false) =>
		e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var value) ? value.GetBoolean() : fallback;
	private static Vector3 V(JsonElement e, string key, Vector3 fallback = default)
	{
		if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(key, out var v)) return fallback;
		// Source Z-up to Godot Y-up.
		return new(v[0].GetSingle(), v[2].GetSingle(), -v[1].GetSingle());
	}
	private static Color C(JsonElement e, string key, Color fallback)
	{
		if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(key, out var c)) return fallback;
		return new(c[0].GetSingle() / 255, c[1].GetSingle() / 255, c[2].GetSingle() / 255);
	}
	private static JsonElement Op(JsonElement e, string group, string name)
	{
		if (e.TryGetProperty(group, out var ops))
			foreach (var op in ops.EnumerateArray())
				if (op.GetProperty("functionName").GetString()!.Equals(name, StringComparison.OrdinalIgnoreCase)) return op;
		return default;
	}
	private float Range(JsonElement e, string min, string max, float low, float high) => _random.RandfRange(F(e, min, low), F(e, max, high));
	private Vector3 Between(Vector3 a, Vector3 b) => new(_random.RandfRange(a.X, b.X), _random.RandfRange(a.Y, b.Y), _random.RandfRange(a.Z, b.Z));
	private Vector3 Direction()
	{
		var y = _random.RandfRange(-1, 1);
		var angle = _random.RandfRange(0, Mathf.Tau);
		return new(Mathf.Sqrt(1 - y * y) * Mathf.Cos(angle), y, Mathf.Sqrt(1 - y * y) * Mathf.Sin(angle));
	}

	private ShaderMaterial Material(string name, JsonElement info)
	{
		if (Materials.TryGetValue(Root + name, out var cached)) return cached;
		var refract = B(info, "refract");
		var shader = GD.Load<Shader>(refract ? "res://weapons/effects/csso_refract.gdshader" : "res://weapons/effects/csso_particle.gdshader");
		// Impact sheets pack small neighboring sprites; atlas mipmaps bleed between them.
		if (IsImpact) shader = new Shader { Code = shader.Code.Replace("filter_linear_mipmap", "filter_linear") };
		if (B(info, "additive") || B(info, "mod2x"))
		{
			var code = shader.Code.Replace("depth_draw_never;", "depth_draw_never, " + (B(info, "mod2x") ? "blend_mul;" : "blend_add;"));
			if (B(info, "mod2x"))
				// Multiplicative factors must bypass Mobile's half-luminance encoding.
				code = code.Replace("ALBEDO = tex.rgb * tint.rgb;", "ALBEDO = mix(vec3(1.0), max(tex.rgb / vec3(0.21586), vec3(1.0)), tint.a);\n#if CURRENT_RENDERER == RENDERER_MOBILE\n\tALBEDO *= 2.0;\n#endif")
					.Replace("ALPHA = tex.a * tint.a * fade;", "ALPHA = 1.0;");
			shader = new Shader { Code = code };
		}
		// Map decals use priority 1; explosion smoke must composite after them.
		var material = new ShaderMaterial { Shader = shader, RenderPriority = 2 };
		material.SetShaderParameter("sheet", GD.Load<Texture2D>(Root + info.GetProperty("texture").GetString()));
		if (!refract)
		{
			material.SetShaderParameter("soft_particle", B(info, "depth_blend"));
			material.SetShaderParameter("dual_sequence", B(info, "dual"));
		}
		Materials.Add(Root + name, material);
		return material;
	}

	private JsonElement Sequence(JsonElement material, int index)
	{
		var sequences = material.GetProperty("sequences");
		return sequences.TryGetProperty(index.ToString(), out var sequence) ? sequence : sequences.GetProperty("0");
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_initialized) return;
		_initialized = true;
		InitializeSystems();
	}

	protected virtual void InitializeSystems()
	{
		// CSSO has separate enclosed-space and outdoor dust explosions.
		var ceiling = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(GlobalPosition + Vector3.Up * 0.1f, GlobalPosition + Vector3.Up * 6, 1));
		var system = ceiling.Count > 0 ? "explosion_hegrenade_interior" : "explosion_hegrenade_dirt";
		foreach (var child in Data.RootElement.GetProperty("systems").GetProperty(system).GetProperty("children").EnumerateArray())
		{
			var definition = child.GetProperty("child");
			if (definition.GetProperty("name").GetString()!.StartsWith("explosion_screen_")) continue;
			Emit(definition, F(child, "delay"));
		}
	}

	protected void EmitHierarchy(JsonElement definition, float delay = 0)
	{
		if (definition.GetProperty("name").GetString()!.StartsWith("impact_screen_")) return;
		Emit(definition, delay);
		foreach (var child in definition.GetProperty("children").EnumerateArray())
			EmitHierarchy(child.GetProperty("child"), delay + F(child, "delay"));
	}

	private void Emit(JsonElement definition, float delay)
	{
		// Some PCF parents only dispatch children and have no renderer of their own.
		if (definition.GetProperty("renderers").GetArrayLength() == 0) return;
		var name = definition.GetProperty("material").GetString()!.Replace('\\', '/');
		var material = Data.RootElement.GetProperty("materials").GetProperty(name);
		var renderer = definition.GetProperty("renderers")[0];
		foreach (var emitter in definition.GetProperty("emitters").EnumerateArray())
		{
			var continuous = emitter.GetProperty("functionName").GetString() == "emit_continuously";
			var duration = F(emitter, "emission_duration");
			var count = continuous ? Mathf.CeilToInt(F(emitter, "emission_rate", 100) * duration) : (int)F(emitter, "num_to_emit", 100);
			if (IsImpact && !continuous) count = _random.RandiRange((int)F(emitter, "num_to_emit_minimum", count), count);
			count = Math.Min(count, (int)F(definition, "max_particles", 1000));
			for (var i = 0; i < count; i++)
			{
				JsonElement Init(string op) => Op(definition, "initializers", op);
				JsonElement Oper(string op) => Op(definition, "operators", op);
				var lifetime = Init("Lifetime Random");
				var life = Range(lifetime, "lifetime_min", "lifetime_max", 1, 1);
				if (IsImpact) life = Mathf.Lerp(F(lifetime, "lifetime_min", 1), F(lifetime, "lifetime_max", 1), Mathf.Pow(_random.Randf(), F(lifetime, "lifetime_random_exponent", 1)));
				var birth = delay + F(emitter, "emission_start_time") + (continuous ? i / F(emitter, "emission_rate", 100) : 0);
				var sphere = Init("Position Within Sphere Random");
				var direction = Direction();
				var bias = V(sphere, "distance_bias", Vector3.One);
				direction = (direction * bias).Normalized();
				var absolute = V(sphere, "distance_bias_absolute_value");
				for (var axis = 0; axis < 3; axis++) if (absolute[axis] > 0) direction[axis] = Mathf.Abs(direction[axis]);
				var offset = direction * Range(sphere, "distance_min", "distance_max", 0, 0);
				var box = Init("Position Within Box Random");
				offset += Between(V(box, "min"), V(box, "max"));
				var modify = Init("Position Modify Offset Random");
				var radiusOp = Init("Radius Random");
				var radius = radiusOp.ValueKind == JsonValueKind.Object
					? Range(radiusOp, "radius_min", "radius_max", 1, 1) : F(definition, "radius", 1);
				var modification = Between(V(modify, "offset min"), V(modify, "offset max")) * (B(modify, "offset proportional to radius 0/1") ? radius : 1);
				offset = EmissionBasis * offset + (B(modify, "offset in local space 0/1") ? EmissionBasis * modification : modification);
				var velocity = direction * Range(sphere, "speed_min", "speed_max", 0, 0)
					+ Between(V(sphere, "speed_in_local_coordinate_system_min"), V(sphere, "speed_in_local_coordinate_system_max"));
				if (IsImpact)
				{
					velocity = EmissionBasis * velocity;
					// Approximate Source's spatial velocity noise with bounded random velocity.
					var noise = Init("Velocity Noise");
					if (noise.ValueKind == JsonValueKind.Object)
					{
						var v = Between(V(noise, "output minimum"), V(noise, "output maximum", Vector3.One));
						velocity += B(noise, "Apply Velocity in Local Space (0/1)") ? EmissionBasis * v : v;
					}
				}
				var warp = Init("Position Modify Warp Random");
				if (warp.ValueKind == JsonValueKind.Object)
				{
					var fraction = Mathf.Clamp((birth - F(warp, "warp transition start time")) / Math.Max(0.001f, F(warp, "warp transition time (treats min/max as start/end sizes)", 1)), 0, 1);
					offset *= V(warp, "warp min", Vector3.One).Lerp(V(warp, "warp max", Vector3.One), fraction);
				}
				// Remap initial creation time, used for the rising smoke column's radius/life.
				foreach (var init in definition.GetProperty("initializers").EnumerateArray())
					if (init.GetProperty("functionName").GetString()!.Equals("remap initial scalar", StringComparison.OrdinalIgnoreCase))
					{
						var t = Mathf.Clamp((birth - F(init, "input minimum")) / Math.Max(0.001f, F(init, "input maximum", 1) - F(init, "input minimum")), 0, 1);
						var value = Mathf.Lerp(F(init, "output minimum"), F(init, "output maximum", 1), t);
						if (F(init, "output field", 3) == 3) radius = B(init, "output is scalar of initial random range") ? radius * value : value;
						if (F(init, "output field", 3) == 7) life = value;
					}
				var colorOp = Init("Color Random");
				var color = C(colorOp, "color1", C(definition, "color", Colors.White)).Lerp(C(colorOp, "color2", C(definition, "color", Colors.White)), _random.Randf());
				color.A = Range(Init("Alpha Random"), "alpha_min", "alpha_max", 255, 255) / 255;
				var movement = Oper("Movement Basic");
				var scale = Oper("Radius Scale");
				var fadeIn = Oper("Alpha Fade In Random");
				var fadeOut = Oper("Alpha Fade Out Random");
				var alphaDecay = Oper("Alpha Fade and Decay");
				var simpleFade = Oper("Alpha Fade Out Simple");
				var haze = !IsImpact && !B(material, "additive") && !B(material, "mod2x") && !B(material, "refract")
					&& !definition.GetProperty("name").GetString()!.Contains("shrapnel")
					&& !definition.GetProperty("name").GetString()!.Contains("clumps");
				var sequence = Init("Sequence Random");
				var second = Init("Sequence Two Random");
				var mesh = new MeshInstance3D { Name = definition.GetProperty("name").GetString()!, Mesh = Quad, MaterialOverride = Material(name, material), Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
				AddChild(mesh);
				_particles.Add(new Particle
				{
					Mesh = mesh, Birth = birth, Life = life, Origin = offset * Unit, Velocity = velocity * Unit,
					Gravity = V(movement, "gravity") * Unit, Drag = F(movement, "drag"), Color = color, Radius = radius * Unit,
					StartScale = F(scale, "radius_start_scale", 1), EndScale = F(scale, "radius_end_scale", 1), ScaleEnd = F(scale, "end_time", 1), Bias = F(scale, "scale_bias", 0.5f),
					FadeIn = IsImpact && alphaDecay.ValueKind == JsonValueKind.Object ? F(alphaDecay, "end_fade_in_time", 0.5f) * life : Range(fadeIn, "fade in time min", "fade in time max", 0, 0) * (B(fadeIn, "proportional 0/1", true) ? life : 1),
					FadeOut = Math.Max(haze ? Math.Min(1.2f, life * 0.6f) : 0,
						(IsImpact && alphaDecay.ValueKind == JsonValueKind.Object ? 1 - F(alphaDecay, "start_fade_out_time", 0.5f) : IsImpact && simpleFade.ValueKind == JsonValueKind.Object ? 1 - F(simpleFade, "start_fade_out_time", 0.5f) : Range(fadeOut, "fade out time min", "fade out time max", 0.25f, 0.25f)) * (B(fadeOut, "proportional 0/1", true) ? life : 1)),
					RetireSeconds = haze ? 0.8f : 0.08f,
					Rotation = Mathf.DegToRad(Range(Init("Rotation Random"), "rotation_offset_min", "rotation_offset_max", 0, 360)),
					Spin = Mathf.DegToRad(Range(Init("Rotation Speed Random"), "rotation_speed_random_min", "rotation_speed_random_max", 0, 0) + (IsImpact ? F(Oper("Rotation Spin Roll"), "spin_rate_degrees") : 0)),
					Sequence = Sequence(material, _random.RandiRange((int)F(sequence, "sequence_min"), (int)F(sequence, "sequence_max"))),
					SecondSequence = Sequence(material, _random.RandiRange((int)F(second, "sequence_min"), (int)F(second, "sequence_max"))),
					AnimationRate = F(renderer, "animation rate", 1), SecondRate = F(renderer, "second sequence animation rate", 1),
					FitAnimation = B(renderer, "animation_fit_lifetime"), FpsAnimation = B(renderer, "use animation rate as FPS"),
					Refract = B(material, "refract"),
					Trail = renderer.GetProperty("functionName").GetString() == "render_sprite_trail" ? Range(Init("Trail Length Random"), "length_min", "length_max", 0.025f, 0.085f) : 0,
					TrailFadeIn = F(renderer, "length fade in time"),
					MaxTrail = F(renderer, "max length", float.MaxValue) * Unit,
					SurfaceAligned = IsImpact && F(renderer, "orientation_type") == 2,
					CullDistance = F(Oper("Cull when crossing sphere"), "Cull Distance", float.MaxValue) * Unit,
					CullPlane = F(Oper("Cull when crossing plane"), "Cull plane offset", float.MinValue) * Unit,
					Collide = (IsImpact && Op(definition, "constraints", "Collision via traces").ValueKind == JsonValueKind.Object) || definition.GetProperty("name").GetString()!.Contains("shrapnel") || definition.GetProperty("name").GetString()!.Contains("clumps")
				});
			}
		}
	}

	private static Vector4 Uv(JsonElement frame)
	{
		var uv = frame.GetProperty("uv");
		return new(uv[0].GetSingle(), uv[1].GetSingle(), uv[2].GetSingle(), uv[3].GetSingle());
	}
	private static (Vector4 Current, Vector4 Next, float Blend) Sample(JsonElement sequence, float time)
	{
		var frames = sequence.GetProperty("frames");
		var duration = F(sequence, "duration", 1);
		time = B(sequence, "clamp") ? Math.Min(time, duration - 0.0001f) : time % duration;
		for (var i = 0; i < frames.GetArrayLength(); i++)
		{
			var length = F(frames[i], "duration", 1);
			if (time <= length || i == frames.GetArrayLength() - 1)
			{
				var next = i + 1 < frames.GetArrayLength() ? i + 1 : B(sequence, "clamp") ? i : 0;
				return (Uv(frames[i]), Uv(frames[next]), Mathf.Clamp(time / Math.Max(length, 0.0001f), 0, 1));
			}
			time -= length;
		}
		return (Uv(frames[0]), Uv(frames[0]), 0);
	}

	public override void _Process(double delta)
	{
		if (!_initialized) return;
		_age += (float)delta;
		if (_age > 12) { QueueFree(); return; }
		var camera = GetViewport().GetCamera3D();
		if (camera == null) return;
		var alive = false;
		foreach (var p in _particles)
		{
			var age = _age - p.Birth;
			var end = Math.Min(p.Life, p.RetireAge + p.RetireSeconds);
			p.Mesh.Visible = age >= 0 && age < end;
			if (age < end) alive = true;
			if (!p.Mesh.Visible) continue;
			var step = Math.Min((float)delta, age);
			// Source drag is a per-second damping coefficient scaled by its 30 Hz reference.
			var old = p.Origin;
			var drag = p.Drag * 30;
			if (drag > 0.0001f)
			{
				var damping = Mathf.Exp(-drag * step);
				var integrated = (1 - damping) / drag;
				p.Origin += p.Velocity * integrated + p.Gravity * ((step - integrated) / drag);
				p.Velocity = p.Velocity * damping + p.Gravity * integrated;
			}
			else
			{
				p.Origin += p.Velocity * step + p.Gravity * (step * step * 0.5f);
				p.Velocity += p.Gravity * step;
			}
			if (float.IsPositiveInfinity(p.RetireAge)
				&& (p.Origin.Length() > p.CullDistance || (old.Y > p.CullPlane && p.Origin.Y <= p.CullPlane)))
			{
				// Retire at the boundary without cutting a visible layer out of the cloud.
				p.RetireAge = age;
			}
			if (p.Collide && old.DistanceSquaredTo(p.Origin) > 0.00001f)
			{
				var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(GlobalPosition + old, GlobalPosition + p.Origin, 1));
				if (hit.Count > 0)
				{
					var normal = hit["normal"].AsVector3();
					p.Origin = hit["position"].AsVector3() - GlobalPosition + normal * 0.005f;
					p.Velocity = p.Velocity.Bounce(normal) * 0.25f;
				}
			}
			var t = Mathf.Clamp(age / p.Life / Math.Max(p.ScaleEnd, 0.001f), 0, 1);
			var bias = Mathf.Clamp(p.Bias, 0.001f, 0.999f);
			t = t / ((1 / bias - 2) * (1 - t) + 1);
			var sizeScale = IsImpact ? 1f / 1.5f : 1f;
			var size = Math.Max(0.001f, p.Radius * 2 * Mathf.Lerp(p.StartScale, p.EndScale, t)) * sizeScale;
			var color = p.Color;
			color.A *= (p.FadeIn > 0 ? Mathf.SmoothStep(0, 1, age / p.FadeIn) : 1)
				* (p.FadeOut > 0 ? Mathf.SmoothStep(0, 1, (p.Life - age) / p.FadeOut) : 1)
				* (1 - Mathf.SmoothStep(0, 1, (age - p.RetireAge) / p.RetireSeconds));
			var angle = p.Rotation + p.Spin * age;
			var basis = p.SurfaceAligned ? new Basis(EmissionBasis.X, -EmissionBasis.Z, EmissionBasis.Y) : camera.GlobalBasis;
			var scale = new Vector3(size, size, 1);
			var center = p.Origin;
			if (p.Trail > 0)
			{
				var screenVelocity = basis.Inverse() * p.Velocity;
				angle = Mathf.Atan2(-screenVelocity.X, screenVelocity.Y);
				// A trail moving toward the camera must not become a vertical streak.
				var projectedSpeed = new Vector2(screenVelocity.X, screenVelocity.Y).Length();
				scale.Y = Math.Max(size, projectedSpeed * p.Trail * (p.TrailFadeIn > 0 ? Mathf.Clamp(age / p.TrailFadeIn, 0, 1) : 1) * sizeScale);
				scale.Y = Math.Min(scale.Y, p.MaxTrail * sizeScale);
				if (projectedSpeed > .0001f)
					center -= (basis * new Vector3(screenVelocity.X, screenVelocity.Y, 0)).Normalized() * scale.Y * 0.5f;
			}
			// Scale the quad along its own axes, then rotate it into the velocity direction.
			p.Mesh.GlobalTransform = new Transform3D(basis * new Basis(Vector3.Back, angle) * Basis.FromScale(scale), GlobalPosition + center);
			p.Mesh.SetInstanceShaderParameter("tint", color);
			if (p.Refract) continue;
			var animationTime = p.FitAnimation ? age / p.Life * F(p.Sequence, "duration", 1) : age * p.AnimationRate * (p.FpsAnimation ? 1 : F(p.Sequence, "duration", 1));
			var sample = Sample(p.Sequence, animationTime);
			p.Mesh.SetInstanceShaderParameter("frame_uv", sample.Current);
			p.Mesh.SetInstanceShaderParameter("next_uv", sample.Next);
			p.Mesh.SetInstanceShaderParameter("frame_blend", sample.Blend);
			p.Mesh.SetInstanceShaderParameter("second_uv", Sample(p.SecondSequence, age * p.SecondRate * F(p.SecondSequence, "duration", 1)).Current);
		}
		if (!alive) QueueFree();
	}
}
