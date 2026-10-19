using Godot;
using System.Collections.Generic;
using Contrapoint.Players;

namespace Contrapoint.Weapons;

// The Source smoke effect uses a grid of billboards, a growing spherical volume,
// cosine fade from 17 to 22 seconds, and a fog overlay when the eye is inside.
public partial class GrenadeSmoke : Node3D
{
	public const float SpacingRadius = 165 * SourceMovement.Unit;
	public const float Radius = SpacingRadius * 2;
	public const float ExpandSeconds = 5.5f;
	public const float FadeStart = 17, FadeEnd = 22;
	public double Age { get; private set; }
	public void SetNetworkAge(double age) { if (double.IsFinite(age)) Age = age; }
	public float Density { get; private set; }
	private readonly List<(Sprite3D Sprite, Vector3 Offset, float Phase)> _particles = new();
	private bool _placed;

	public override void _Ready() => AddToGroup("source_smoke");

	public override void _PhysicsProcess(double delta)
	{
		if (_placed) return;
		_placed = true;
		var texture = GD.Load<Texture2D>("res://assets/effects/grenades/particle_smokegrenade.png");
		var random = new RandomNumberGenerator { Seed = GetInstanceId() };
		for (var x = 0; x < 6; x++)
			for (var y = 0; y < 6; y++)
				for (var z = 0; z < 6; z++)
				{
					var offset = (new Vector3(x, y, z) / 5 * 2 - Vector3.One) * SpacingRadius;
					var ray = PhysicsRayQueryParameters3D.Create(GlobalPosition + Vector3.Up * 0.05f, GlobalPosition + offset, 1);
					// Avoid placing smoke on the opposite side of a wall or under the floor.
					var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
					if (hit.Count > 0) continue;
					var shade = random.RandfRange(0.5f, 0.6f);
					var sprite = new Sprite3D
					{
						Texture = texture, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
						// Map decals use priority 1; smoke must composite after them.
						RenderPriority = 2,
						PixelSize = (55 * SourceMovement.Unit * 2) / texture.GetWidth(),
						Shaded = true, DoubleSided = true, Modulate = new Color(shade, shade, shade, 0),
						Position = offset, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
					};
					AddChild(sprite);
					_particles.Add((sprite, offset, random.RandfRange(0, Mathf.Tau)));
				}
	}

	public override void _Process(double delta)
	{
		Age += delta;
		var expansion = Mathf.Sin(Mathf.Clamp((float)Age / ExpandSeconds, 0, 1) * Mathf.Pi * 0.5f);
		var fade = Age < FadeStart ? 1 : (Mathf.Cos(Mathf.Clamp(((float)Age - FadeStart) / (FadeEnd - FadeStart), 0, 1) * Mathf.Pi) + 1) * 0.5f;
		Density = fade * expansion;
		var radius = Radius * expansion;
		foreach (var (sprite, offset, phase) in _particles)
		{
			var length = offset.Length();
			sprite.Position = length > radius * 0.5f ? offset.Normalized() * radius * 0.5f : offset;
			sprite.Rotation = new Vector3(0, 0, phase + (float)Age * Mathf.Sin(phase) * 0.6f);
			var color = sprite.Modulate;
			color.A = radius > 0 ? Mathf.Clamp((1 - length / radius) / 0.7f, 0, 1) * Density : 0;
			sprite.Modulate = color;
		}
		if (Age >= FadeEnd) QueueFree();
	}

	public float EyeDensity(Vector3 eye)
	{
		var distance = GlobalPosition.DistanceTo(eye);
		var radius = Radius * Mathf.Sin(Mathf.Clamp((float)Age / ExpandSeconds, 0, 1) * Mathf.Pi * 0.5f);
		if (radius <= 0 || distance >= radius) return 0;
		return Density * Mathf.Clamp((1 - distance / radius) / 0.85f, 0, 1);
	}
}
