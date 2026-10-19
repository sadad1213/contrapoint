using Godot;

namespace Contrapoint.Weapons;

public partial class ShotTracer : MeshInstance3D
{
	public Vector3 StartPosition => _start;
	private readonly ImmediateMesh _mesh = new();
	private readonly StandardMaterial3D _material = new()
	{
		ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		AlbedoColor = new Color(1, 0.8f, 0.4f, 0.85f)
	};
	private Vector3 _start, _end;
	private Camera3D _camera = null!;
	private float _distance, _duration, _age;

	public void Initialize(Vector3 start, Vector3 end, Camera3D camera)
	{
		_start = start;
		_end = end;
		_camera = camera;
		_distance = start.DistanceTo(end);
		_duration = Mathf.Clamp(_distance / 1200, 0.035f, 0.25f);
		Layers = 1;
		CastShadow = ShadowCastingSetting.Off;
		Mesh = _mesh;
		DrawSegment(0.1f);
	}

	public override void _Process(double delta)
	{
		_age += (float)delta;
		if (_age > _duration + 0.025f) { QueueFree(); return; }
		DrawSegment(Mathf.Min(1, _age / _duration));
	}

	private void DrawSegment(float fraction)
	{
		var direction = (_end - _start).Normalized();
		var head = _start.Lerp(_end, fraction);
		var tail = head - direction * Mathf.Min(_distance * fraction, 4);
		var view = GodotObject.IsInstanceValid(_camera) ? _camera.GlobalPosition - head : Vector3.Up;
		var side = direction.Cross(view).Normalized() * 0.0035f;
		if (side.LengthSquared() < 0.00000001f) side = Vector3.Right * 0.0035f;
		_material.AlbedoColor = new(1, 0.8f, 0.4f, 0.85f * Mathf.Clamp((_duration + 0.025f - _age) / 0.025f, 0, 1));
		_mesh.ClearSurfaces();
		_mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, _material);
		foreach (var vertex in new[] { tail - side, head - side, head + side, tail - side, head + side, tail + side })
			_mesh.SurfaceAddVertex(vertex);
		_mesh.SurfaceEnd();
	}
}
