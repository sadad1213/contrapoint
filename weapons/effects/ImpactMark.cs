using Godot;

namespace Contrapoint.Weapons;

public partial class ImpactMark : MeshInstance3D
{
	private double _age;
	private double _lifetime;
	private ShaderMaterial _material = null!;

	public void Initialize(Texture2D texture, Vector2 size, Vector3 point, Vector3 normal, float roll, double lifetime)
	{
		_lifetime = lifetime;
		Layers = 1;
		CastShadow = ShadowCastingSetting.Off;
		Mesh = new QuadMesh { Size = size };
		_material = new ShaderMaterial { Shader = GD.Load<Shader>("res://weapons/effects/impact_mark.gdshader") };
		_material.SetShaderParameter("mark_texture", texture);
		MaterialOverride = _material;
		var reference = Mathf.Abs(normal.Dot(Vector3.Up)) > 0.99f ? Vector3.Right : Vector3.Up;
		var tangent = reference.Cross(normal).Normalized();
		var basis = new Basis(tangent, normal.Cross(tangent), normal).Rotated(normal, roll);
		GlobalTransform = new(basis, point + normal * 0.0015f);
	}

	public override void _Process(double delta)
	{
		_age += delta;
		if (_age >= _lifetime) { QueueFree(); return; }
		if (_age > _lifetime - 5)
			_material.SetShaderParameter("opacity", (float)((_lifetime - _age) / 5));
	}
}
