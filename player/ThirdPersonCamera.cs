using Godot;

namespace Contrapoint.Players;

// Keep Player.Camera at the eyes: combat, recoil, use and grenade traces use it.
// Only the rendered view moves behind the character.
public partial class ThirdPersonCamera : Node3D
{
	public const uint BodyLayer = 4;
	[Export] public float Distance { get; set; } = 2.5f;
	[Export] public float Shoulder { get; set; } = 0.35f;
	public bool Enabled { get; private set; }
	public Camera3D Camera { get; private set; } = null!;
	public float ActualDistance { get; private set; }
	private Player _player = null!;
	private CanvasLayer _weaponLayer = null!;
	private SubViewport _weapons = null!;
	private readonly SphereShape3D _probe = new() { Radius = 0.16f };
	private Vector3 _previousEye, _currentEye, _previousBody, _currentBody;
	private float _previousDistance, _aimDistance = 2000;

	public void Bind(Player player)
	{
		_player = player;
		ProcessPriority = 90;
		ProcessPhysicsPriority = 100; // Sample after player movement and recoil.
		TopLevel = true;
		PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
		Camera = new Camera3D { Name = "Camera3D", Near = 0.05f, Far = player.Camera.Far };
		AddChild(Camera);
		_weaponLayer = player.GetNode<CanvasLayer>("WeaponLayer");
		_weapons = player.GetNode<SubViewport>("WeaponLayer/Container/Viewport");
		if (player.LocalInput) player.Camera.CullMask &= ~BodyLayer;
		SetEnabled(false);
	}

	public void SetEnabled(bool enabled)
	{
		Enabled = enabled && _player.LocalInput;
		if (Enabled) _player.Body.EnsurePose();
		Camera.Current = Enabled;
		if (!Enabled && _player.LocalInput) _player.RenderCamera.MakeCurrent();
		_weaponLayer.Visible = _player.LocalInput && !Enabled;
		_weapons.RenderTargetUpdateMode = _player.LocalInput && !Enabled
			? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
		ResetHistory();
		if (!Enabled) _player.Body.Position = Vector3.Zero;
	}

	public void ResetHistory()
	{
		_previousEye = _currentEye = _player.Camera.GlobalPosition;
		_previousBody = _currentBody = _player.GlobalPosition;
		_previousDistance = ActualDistance = 0;
		_aimDistance = 2000;
		_Process(0);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_player == null || !Enabled) return;
		_previousEye = _currentEye;
		_currentEye = _player.Camera.GlobalPosition;
		_previousBody = _currentBody;
		_currentBody = _player.GlobalPosition;
		_previousDistance = ActualDistance;
		// Shape casts also protect the camera's near-plane corners at wall edges.
		var eyes = _player.Camera.GlobalTransform;
		var offset = eyes.Basis * new Vector3(Shoulder, 0.12f, Distance);
		var query = new PhysicsShapeQueryParameters3D
		{
			Shape = _probe, Transform = new(Basis.Identity, eyes.Origin), Motion = offset,
			CollisionMask = 1, Exclude = new() { _player.GetRid() }
		};
		var space = GetWorld3D().DirectSpaceState;
		var fractions = space.CastMotion(query);
		var safe = fractions.Length > 0 ? fractions[0] : 1;
		query.Motion = Vector3.Zero;
		if (space.IntersectShape(query, 1).Count > 0) safe = 0;
		var target = Mathf.Max(0, offset.Length() * safe - 0.04f);
		// Retract immediately; ease out after an obstacle has cleared.
		ActualDistance = target < ActualDistance ? target
			: Mathf.Lerp(ActualDistance, target, 1 - Mathf.Exp(-12 * (float)delta));
		// Keep space queries on physics ticks; render with the latest mouse angles.
		var ray = PhysicsRayQueryParameters3D.Create(eyes.Origin, eyes.Origin - eyes.Basis.Z * 2000, 1);
		ray.Exclude = new() { _player.GetRid() };
		var hit = space.IntersectRay(ray);
		_aimDistance = hit.TryGetValue("position", out var position)
			? eyes.Origin.DistanceTo(position.AsVector3()) : 2000;
	}

	public override void _Process(double delta)
	{
		if (_player == null || !Enabled) return;
		var fraction = Mathf.Clamp((float)Engine.GetPhysicsInterpolationFraction(), 0, 1);
		var eyes = new Transform3D(_player.Camera.GlobalBasis, _previousEye.Lerp(_currentEye, fraction));
		var offset = eyes.Basis * new Vector3(Shoulder, 0.12f, Distance);
		// A new obstruction must retract the camera immediately, without lerping
		// through the wall. Only extension uses the interpolated distance.
		var distance = Mathf.Min(Mathf.Lerp(_previousDistance, ActualDistance, fraction), ActualDistance);
		Camera.GlobalTransform = new(eyes.Basis, eyes.Origin + offset.Normalized() * distance);
		Camera.Fov = _player.Camera.Fov;
		Camera.CullMask = _player.Camera.CullMask | (distance > 0.55f ? BodyLayer : 0);
		// Match the visible body to the camera's movement sample, leaving the
		// CharacterBody and gameplay eye at their current physics positions.
		_player.Body.GlobalPosition = _previousBody.Lerp(_currentBody, fraction);
		var aim = eyes.Origin - eyes.Basis.Z * _aimDistance;
		if (Camera.GlobalPosition.DistanceSquaredTo(aim) > 0.01f)
			Camera.LookAt(aim, eyes.Basis.Y);
	}
}
