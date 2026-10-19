using Godot;
using System.Collections.Generic;

namespace Contrapoint.Players;

// FX_CS_BloodSpray core/gore/drop textures, sizes, lifetimes and gravity.
// Godot sprites replace Source's particle emitter and ambient-light sampling.
public partial class SourceDamageEffects : Node3D
{
	public static bool BloodEnabled { get; set; } = true;
	private sealed class Particle
	{
		public Sprite3D Sprite = null!;
		public Vector3 Velocity;
		public float Age, Life, Start, End, Gravity;
		public Color Color;
	}
	private readonly List<Particle> _particles = new();
	private static readonly Dictionary<string, Texture2D> Textures = new();
	private float Rand(float min, float max) => (float)GD.RandRange(min, max);
	private Vector3 RandomVector() => new(Rand(-1, 1), Rand(-1, 1), Rand(-1, 1));
	public static void Spawn(Node3D parent, Vector3 position, Vector3 direction, float magnitude, bool helmet)
	{
		if ((!BloodEnabled || magnitude <= 0) && !helmet) return;
		var effect = new SourceDamageEffects { Name = "DamageImpact" };
		parent.AddChild(effect); effect.GlobalPosition = position;
		effect.Emit(-direction.Normalized(), magnitude, helmet);
	}
	private void Add(string name, Vector3 at, Vector3 velocity, float life, float start, float end, float gravity, Color color)
	{
		if (!Textures.TryGetValue(name, out var texture))
		{
			texture = GD.Load<Texture2D>($"res://assets/effects/css_damage/{name}.png"); Textures.Add(name, texture);
		}
		var sprite = new Sprite3D
		{
			Texture = texture, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			PixelSize = SourceMovement.Unit / texture.GetWidth(), Position = at * SourceMovement.Unit,
			Modulate = color, Scale = Vector3.One * start * 2, Axis = Vector3.Axis.Z
		};
		AddChild(sprite);
		_particles.Add(new() { Sprite = sprite, Velocity = velocity * SourceMovement.Unit,
			Life = life, Start = start * 2, End = end * 2, Gravity = gravity * SourceMovement.Unit, Color = color });
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
		for (var i = 0; i < _particles.Count;)
		{
			var particle = _particles[i]; particle.Age += (float)delta;
			if (particle.Age >= particle.Life) { particle.Sprite.QueueFree(); _particles.RemoveAt(i); continue; }
			var t = particle.Age / particle.Life;
			particle.Sprite.Position += particle.Velocity * (float)delta;
			particle.Velocity += Vector3.Down * particle.Gravity * (float)delta;
			particle.Sprite.Scale = Vector3.One * Mathf.Lerp(particle.Start, particle.End, t);
			var color = particle.Color; color.A = 1 - t; particle.Sprite.Modulate = color;
			i++;
		}
		if (_particles.Count == 0) QueueFree();
	}
}
