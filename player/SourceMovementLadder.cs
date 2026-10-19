using Godot;

namespace Contrapoint.Players;

public partial class SourceMovement
{
	public bool IsOnLadder { get; private set; }
	public Vector3 LadderNormal { get; private set; }
	private ShapeCast3D _ladderProbe = null!;
	private Vector3 _lastStandingPosition;
	private bool _hasStandingPosition;
	private float _ladderSuppression;
	private Vector3 _lastLadderPosition;
	private Vector3 _lastLadderNormal;

	private void InitializeLadderProbe()
	{
		_ladderProbe = new ShapeCast3D
		{
			Name = "LadderProbe", Enabled = false, Shape = _hull,
			CollisionMask = 16, CollideWithAreas = true, CollideWithBodies = false,
			Margin = 0.001f, MaxResults = 8
		};
		AddChild(_ladderProbe);
		_ladderProbe.AddException(_body);
	}

	private void ResetLadder()
	{
		IsOnLadder = false;
		LadderNormal = Vector3.Zero;
		_ladderSuppression = 0;
		_hasStandingPosition = false;
	}

	// CGameMovement::LadderMove and CCSGameMovement::LadderDistance.
	// Sweep the real hull against the authored brush faces; their AABB's thin
	// axis is not a surface normal (especially at ladder ends and rotated brushes).
	private bool TraceLadder(Vector3 motion, out Vector3 normal, out float fraction)
	{
		normal = Vector3.Zero;
		fraction = 1;
		_ladderProbe.GlobalTransform = _collision.GlobalTransform;
		// CastMotion ignores initial overlaps. Test contact first so an attached
		// ladder is not reported at the end of the 10-unit sweep instead.
		_ladderProbe.TargetPosition = Vector3.Zero;
		_ladderProbe.ForceShapecastUpdate();
		var touching = _ladderProbe.IsColliding();
		if (!touching)
		{
			_ladderProbe.TargetPosition = motion;
			_ladderProbe.ForceShapecastUpdate();
		}
		if (!_ladderProbe.IsColliding()) return false;
		fraction = touching ? 0 : _ladderProbe.GetClosestCollisionSafeFraction();
		for (var i = 0; i < _ladderProbe.GetCollisionCount(); i++)
		{
			var candidate = _ladderProbe.GetCollisionNormal(i);
			// CSS rejects the upper face. Only acquire a face we are moving into.
			if (candidate.Y > 0.999f || candidate.Dot(motion) >= 0) continue;
			normal = candidate.Normalized();
			break;
		}
		if (normal.IsZeroApprox()) return false;
		var query = new PhysicsShapeQueryParameters3D
		{
			Shape = _hull, Transform = _collision.GlobalTransform, Motion = motion,
			Margin = _body.SafeMargin, CollisionMask = _body.CollisionMask & ~2u,
			Exclude = new() { _body.GetRid() }
		};
		// Ignore playerclips like CSS, but do not reach through a solid wall.
		var space = _body.GetWorld3D().DirectSpaceState;
		var solidFraction = space.CastMotion(query)[0];
		query.Motion = Vector3.Zero;
		var contact = space.GetRestInfo(query);
		if (contact.Count > 0 && contact["normal"].AsVector3().Dot(motion) < -0.00001f) solidFraction = 0;
		return solidFraction * motion.Length() + 0.002f >= fraction * motion.Length();
	}

	private bool TryLadder(PlayerCommand command, bool grounded, float delta)
	{
		var wish = _camera.GlobalBasis * new Vector3(command.Move.X, 0, command.Move.Y);
		var probe = IsOnLadder ? -LadderNormal : wish.Normalized();
		var distance = (IsOnLadder ? 10 : 2) * Unit;
		if (probe.IsZeroApprox() || !TraceLadder(probe * distance, out var normal, out _))
		{
			IsOnLadder = false;
			return false;
		}
		IsOnLadder = true;
		LadderNormal = normal;
		// CSS only suppresses its automatic backwards catch, not LadderMove's
		// normal front-facing acquisition. Refresh even on the jump-off tick.
		_ladderSuppression = 1;
		_lastLadderPosition = _body.GlobalPosition;
		_lastLadderNormal = normal;
		if (command.Jump)
		{
			IsOnLadder = false;
			_body.Velocity = normal * (270 * Unit);
			// MOVETYPE_WALK processes the same command with ordinary air movement
			// and both gravity halves. There is no extra upward kick or cooldown.
			return false;
		}

		var floor = new PhysicsPointQueryParameters3D { Position = _body.GlobalPosition - Vector3.Up * Unit };
		floor.CollisionMask = 1;
		floor.Exclude = new() { _body.GetRid() };
		var onFloor = grounded || _body.GetWorld3D().DirectSpaceState.IntersectPoint(floor, 1).Count > 0;
		_body.Velocity = LadderVelocity(_camera.GlobalBasis, command, normal, onFloor);
		_jumpHeld = false;
		// FullLadderMove uses TryPlayerMove, without ground friction, floor snap,
		// stair stepping or gravity. Preserve upward speed when leaving the end.
		MoveInAir(delta, categorizeGround: _body.Velocity.Y <= 0);
		UpdateView(delta);
		return true;
	}

	// Source uses the movement buttons independently at 200 u/s, not a normalized
	// wish vector. Looking 45 degrees up can therefore climb at 282.84 u/s.
	public static Vector3 LadderVelocity(Basis view, PlayerCommand command, Vector3 normal, bool onFloor)
	{
		var speed = 200 * Unit * (command.Duck ? 0.34f : 1);
		var intended = (view.X * Mathf.Sign(command.Move.X) + view.Z * Mathf.Sign(command.Move.Y)) * speed;
		var into = intended.Dot(normal);
		var across = Vector3.Up.Cross(normal).Normalized();
		var up = normal.Cross(across);
		var lateral = intended - normal * into;
		var sideways = lateral.Dot(across);
		var incidence = (across * sideways + normal * into).Normalized().Dot(normal);
		// Default sv_ladder_angle=-0.707 and sv_ladder_dampen=0.2.
		if (incidence < -0.707f) lateral = up * lateral.Dot(up) + across * sideways * 0.2f;
		var velocity = lateral - up * into;
		if (onFloor && into > 0) velocity += normal * (200 * Unit);
		return velocity;
	}

	// CCSGameMovement::CheckForLadders: catch a ladder behind/below the ledge
	// when walking off its top. Never catch the recently dismounted ladder.
	private void CheckForLadders(bool wasOnGround)
	{
		if (wasOnGround)
		{
			_lastStandingPosition = _body.GlobalPosition;
			_hasStandingPosition = true;
			return;
		}
		if (!_hasStandingPosition || _body.GlobalPosition.Y > _lastStandingPosition.Y) return;
		var direction = _body.GlobalPosition - _lastStandingPosition;
		direction.Y = 0;
		var distance = direction.Length();
		if (distance == 0 || distance > 64 * Unit) return;
		var motion = _lastStandingPosition - direction.Normalized() * (5 * Unit + distance) - _body.GlobalPosition;
		if (!TraceLadder(motion, out var normal, out var fraction)) return;
		var position = _body.GlobalPosition + motion * fraction;
		var fromLast = position - _lastLadderPosition;
		fromLast.Y = 0;
		if (_ladderSuppression > 0 && (fromLast.LengthSquared() < 64 * 64 * Unit * Unit || normal.IsEqualApprox(_lastLadderNormal))) return;
		IsOnLadder = true;
		LadderNormal = normal;
		_body.Velocity = Vector3.Zero;
		// Repeat the move including playerclips so the catch cannot warp into one.
		var hit = new KinematicCollision3D();
		if (_body.TestMove(_body.GlobalTransform, motion * fraction, hit))
			_body.GlobalPosition += hit.GetTravel();
		else _body.GlobalPosition = position;
	}
}
