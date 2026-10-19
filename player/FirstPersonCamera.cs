using Godot;

namespace Contrapoint.Players;

// Interpolate only the rendered eye position. Gameplay keeps the current-tick
// camera for movement, traces and networking; mouse look stays immediate.
public partial class FirstPersonCamera : Camera3D
{
	private Player _player = null!;
	private Vector3 _previousEye, _currentEye;

	public void Bind(Player player)
	{
		_player = player;
		TopLevel = true;
		PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
		ProcessPriority = 100;
		ProcessPhysicsPriority = 100; // Sample after movement and eye-height updates.
		ResetHistory();
		MakeCurrent();
	}

	public void ResetHistory()
	{
		_previousEye = _currentEye = _player.Camera.GlobalPosition;
		_Process(0);
	}

	public override void _PhysicsProcess(double delta)
	{
		_previousEye = _currentEye;
		_currentEye = _player.Camera.GlobalPosition;
	}

	public override void _Process(double delta)
	{
		var source = _player.Camera;
		var fraction = Mathf.Clamp((float)Engine.GetPhysicsInterpolationFraction(), 0, 1);
		GlobalTransform = new(source.GlobalBasis, _previousEye.Lerp(_currentEye, fraction));
		Fov = source.Fov;
		Near = source.Near;
		Far = source.Far;
		CullMask = source.CullMask;
		KeepAspect = source.KeepAspect;
		Environment = source.Environment;
		Attributes = source.Attributes;
	}
}
