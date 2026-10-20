using Godot;
using System.Collections.Generic;
using Contrapoint.Weapons;

namespace Contrapoint.Players;

// FX_CS_BloodSpray core/gore/drop textures, sizes, lifetimes and gravity.
// Batched billboards preserve the existing sprites' motion, scale and tint.
public partial class SourceDamageEffects : Node3D
{
	public static bool BloodEnabled { get; set; } = true;
	private sealed class Particle
	{
		public CssoParticleBatch Batch = null!;
		public int Slot = -1;
		public Vector3 Origin, Velocity;
		public float Aspect;
		public float Age, Life, Start, End, Gravity;
		public Color Color;
	}
	private readonly List<Particle> _particles = new();
	private readonly Dictionary<string, CssoParticleBatch> _batches = new();
	private readonly Plane[] _frustum = new Plane[6];
	private static readonly Dictionary<string, (ShaderMaterial Material, float Aspect)> Materials = new();
	private static readonly QuadMesh Quad = new() { Size = Vector2.One };
	private float Rand(float min, float max) => (float)GD.RandRange(min, max);
	private Vector3 RandomVector() => new(Rand(-1, 1), Rand(-1, 1), Rand(-1, 1));
	public static void Spawn(Node3D parent, Vector3 position, Vector3 direction, float magnitude, bool helmet)
	{
		if ((!BloodEnabled || magnitude <= 0) && !helmet) return;
		var effect = new SourceDamageEffects { Name = "DamageImpact" };
		parent.AddChild(effect); effect.GlobalPosition = position;
		effect.Emit(-direction.Normalized(), magnitude, helmet);
		foreach (var batch in effect._batches.Values) batch.Allocate(batch.Count);
	}
	private void Add(string name, Vector3 at, Vector3 velocity, float life, float start, float end, float gravity, Color color)
	{
		if (!Materials.TryGetValue(name, out var info))
		{
			var texture = GD.Load<Texture2D>($"res://assets/effects/css_damage/{name}.png");
			var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://weapons/effects/source_damage_batch.gdshader") };
			material.SetShaderParameter("sheet", texture);
			info = (material, (float)texture.GetHeight() / texture.GetWidth());
			Materials.Add(name, info);
		}
		if (!_batches.TryGetValue(name, out var batch))
		{
			batch = new(this, name, Quad, info.Material);
			_batches.Add(name, batch);
		}
		batch.Count++;
		_particles.Add(new() { Batch = batch, Origin = at * SourceMovement.Unit, Velocity = velocity * SourceMovement.Unit, Aspect = info.Aspect,
			Life = life, Start = start * 2, End = end * 2, Gravity = gravity * SourceMovement.Unit,
			Color = new Color(Mathf.Floor(color.R * 255) / 255, Mathf.Floor(color.G * 255) / 255, Mathf.Floor(color.B * 255) / 255).SrgbToLinear() });
	}
	private void Emit(Vector3 normal, float magnitude, bool helmet)
	{
		if (BloodEnabled && magnitude > 0)
		{
			var scale = .5f + Mathf.Clamp(magnitude / 50, 0, 1);
			var color = new Color(64f / 255, 0, 4f / 255, 1);
			var direction = normal * RandomVector() * .5f;
			Add("blood_core", normal * 2, direction * Rand(16, 32) + Vector3.Down * Rand(8, 16), .75f, 8, 32, 0, color);
			for (var i = 0; i < 4; i++)
			{
				var size = scale * Rand(4, 8);
				Add("blood_gore", normal * 2, (direction * Rand(16, 32) + Vector3.Down * Rand(16, 32)) * (i + 1),
					Rand(.75f, 1), size, size * 4, 0, color);
			}
			for (var i = 0; i < 32; i++)
			{
				var size = scale * Rand(1, 3);
				Add("blood_drop", Vector3.Zero, RandomVector() * Rand(32, 128), Rand(.5f, 1), size, size * .2f, 400, color);
			}
		}
		if (helmet) for (var i = 0; i < 6; i++)
			Add("spark", normal * 2, (normal + RandomVector() * .5f) * Rand(100, 250), Rand(.08f, .18f), 1.5f, 0, 400, new(1, .75f, .35f));
	}
	public override void _Process(double delta)
	{
		var camera = GetViewport().GetCamera3D();
		var basis = camera?.GlobalBasis ?? Basis.Identity;
		var origin = GlobalPosition;
		if (camera != null) camera.GetFrustum().CopyTo(_frustum, 0);
		foreach (var batch in _batches.Values) batch.Begin();
		for (var i = 0; i < _particles.Count;)
		{
			var particle = _particles[i]; particle.Age += (float)delta;
			if (particle.Age >= particle.Life) { _particles.RemoveAt(i); continue; }
			var t = particle.Age / particle.Life;
			particle.Origin += particle.Velocity * (float)delta;
			particle.Velocity += Vector3.Down * particle.Gravity * (float)delta;
			i++;
			particle.Slot = -1;
			if (camera == null) continue;
			var scale = Mathf.Lerp(particle.Start, particle.End, t);
			var width = scale * SourceMovement.Unit;
			var height = width * particle.Aspect;
			var center = origin + particle.Origin;
			var radius = .5f * new Vector2(width, height).Length();
			var visible = true;
			foreach (var plane in _frustum)
				if (plane.DistanceTo(center) > radius) { visible = false; break; }
			if (!visible) continue;
			// Sprite3D stored modulate in an 8-bit vertex color. Preserve its
			// truncation and sRGB conversion when replacing it with float instances.
			var color = particle.Color; color.A = Mathf.Floor((1 - t) * 255) / 255;
			particle.Slot = particle.Batch.Add(new(basis * Basis.FromScale(new(width, height, scale)), center), color, 0, 0, 0, 0);
		}
		foreach (var batch in _batches.Values) batch.Flush();
		if (_particles.Count == 0) QueueFree();
	}
}
