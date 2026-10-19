using Godot;

namespace Contrapoint.Players;

public partial class SourceWaterRipple : MeshInstance3D
{
	private float _age;
	private ShaderMaterial _material = null!;
	public override void _Ready()
	{
		Mesh = new PlaneMesh { Size = new(1.2f, 1.2f) };
		CastShadow = ShadowCastingSetting.Off;
		_material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/maps/source_ripple.gdshader") };
		MaterialOverride = _material;
	}
	public override void _Process(double delta)
	{
		_age += (float)delta;
		_material.SetShaderParameter("age", _age);
		if (_age >= 1) QueueFree();
	}
	public static bool Spawn(Node3D parent, Vector3 from, Vector3 to)
	{
		var ray = PhysicsRayQueryParameters3D.Create(from, to, 4);
		ray.CollideWithAreas = true;
		ray.CollideWithBodies = false;
		var hit = parent.GetWorld3D().DirectSpaceState.IntersectRay(ray);
		if (hit.Count == 0 || hit["normal"].AsVector3().Y < .7f) return false;
		var ripple = new SourceWaterRipple { TopLevel = true };
		parent.AddChild(ripple);
		ripple.GlobalPosition = hit["position"].AsVector3() + Vector3.Up * .004f;
		return true;
	}
}
