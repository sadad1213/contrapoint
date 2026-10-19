using Godot;

namespace Contrapoint.Players;

// Source-style movement in metres. See player/README.md for references and differences.
public partial class SourceMovement : Node
{
	[Signal] public delegate void JumpedEventHandler();
	[Signal] public delegate void LandedEventHandler(float speed);
	public const float Unit = 0.0254f;
	// CSS g_CSViewVectors uses GoldSrc-compatible movement bounds. These are
	// independent of the taller player model and eye positions (64 / 47).
	public const float StandingHeight = 62 * Unit;
	public const float DuckHeight = 45 * Unit;
	public const float StandingEye = 64 * Unit;
	public const float DuckEye = 47 * Unit;
	private const float WalkableNormal = 0.7f;

	[ExportGroup("Source movement (Source units)")]
	[Export] public float Gravity { get; set; } = 800;
	[Export] public float JumpImpulse { get; set; } = 301.99338f;
	[Export] public float GroundAcceleration { get; set; } = 5;
	[Export] public float AirAcceleration { get; set; } = 100;
	[Export] public float Friction { get; set; } = 4;
	[Export] public float StopSpeed { get; set; } = 75;
	[Export] public float MaxSpeed { get; set; } = 320;
	[Export] public float WaterAcceleration { get; set; } = 10;
	[Export] public float WaterFriction { get; set; } = 1;
	[Export] public float AirSpeedCap { get; set; } = 30;
	[Export] public float StepHeight { get; set; } = 18;
	[Export] public float WalkMultiplier { get; set; } = 0.52f;
	[Export] public float DuckMultiplier { get; set; } = 0.34f;
	[Export] public bool AutoBunnyHop { get; set; }
	[Export] public bool UnlimitedBunnyHop { get; set; }
	[Export] public bool UseStamina { get; set; } = true;
	[ExportGroup("Presentation")]
	[Export] public float DuckSeconds { get; set; } = 0.2f;
	[Export] public float NoclipSpeed { get; set; } = 500;

	public bool IsDucked { get; private set; }
	public bool Noclip { get; private set; }
	public float HorizontalSpeed => new Vector2(_body.Velocity.X, _body.Velocity.Z).Length() / Unit;
	public float StaminaSeconds { get; private set; }
	public int WaterLevel { get; private set; }
	public float DamageSpeedModifier { get; set; } = 1;
	private CharacterBody3D _body = null!;
	private CollisionShape3D _collision = null!;
	private BoxShape3D _hull = null!;
	private Node3D _view = null!;
	private Camera3D _camera = null!;
	private readonly BoxShape3D _standHull = new() { Size = new(32 * Unit, StandingHeight, 32 * Unit), Margin = 0.001f };
	private bool _jumpHeld;
	private bool _teleported;
	private float _eye = StandingEye;
	private float _stepOffset;

	public override void _Ready()
	{
		_body = GetParent<CharacterBody3D>();
		_collision = _body.GetNode<CollisionShape3D>("CollisionShape3D");
		_hull = (BoxShape3D)_collision.Shape.Duplicate();
		// Jolt rounds box edges by this margin. Its 4 cm default makes a
		// stair lip look like a steep slope to the downward step sweep.
		_hull.Margin = 0.001f;
		_collision.Shape = _hull; // Each player must own its crouch hull.
		InitializeLadderProbe();
		_view = _body.GetNode<Node3D>("View");
		_camera = _view.GetNode<Camera3D>("Camera3D");
		// Godot adds 0.01 radians internally; Source's cutoff is exactly normal.Y >= 0.7.
		_body.FloorMaxAngle = Mathf.Acos(WalkableNormal) - 0.01f;
		_body.FloorSnapLength = 2 * Unit;
		_body.FloorConstantSpeed = false;
		_body.FloorBlockOnWall = false;
		_body.SafeMargin = 0.001f;
		_body.MaxSlides = 5;
		// Physics props remain solid, but must not carry the player or donate
		// their linear/angular velocity when stepping or jumping off them.
		_body.PlatformFloorLayers = 0;
		_body.PlatformWallLayers = 0;
		_body.PlatformOnLeave = CharacterBody3D.PlatformOnLeaveEnum.DoNothing;
		_body.CollisionMask |= 2; // Player clips block movement, but not bullets.
		Reset();
	}

	public void Reset()
	{
		_body.Velocity = Vector3.Zero;
		_jumpHeld = false;
		_teleported = true;
		StaminaSeconds = 0;
		DamageSpeedModifier = 1;
		WaterLevel = 0;
		ResetLadder();
		Noclip = IsDucked = false;
		_collision.Disabled = false;
		SetHull(StandingHeight);
		_stepOffset = 0;
		_eye = StandingEye;
		_view.Position = Vector3.Up * _eye;
	}

	public void ToggleNoclip()
	{
		Noclip = !Noclip;
		ResetLadder();
		_collision.Disabled = Noclip;
		_body.Velocity = Vector3.Zero;
		_teleported = true;
	}

	public void ApplyNetworkDuck(bool duck)
	{
		if (IsDucked == duck) return;
		IsDucked = duck;
		SetHull(duck ? DuckHeight : StandingHeight);
		_view.Position = Vector3.Up * (duck ? DuckEye : StandingEye);
	}

	public void Simulate(PlayerCommand command, float weaponSpeed, float delta)
	{
		if (delta <= 0) return;
		weaponSpeed = Mathf.Min(weaponSpeed, MaxSpeed);
		var move = command.Move.LimitLength();
		StaminaSeconds = Mathf.Max(0, StaminaSeconds - delta);
		if (Noclip)
		{
			var direction = _camera.GlobalBasis * new Vector3(move.X, 0, move.Y);
			direction.Y += (command.Jump ? 1 : 0) - (command.Duck ? 1 : 0);
			_body.Velocity = direction.LimitLength() * NoclipSpeed * Unit * (command.Walk ? 2 : 1);
			_body.GlobalPosition += _body.Velocity * delta;
			_jumpHeld = command.Jump;
			return;
		}

		var wasTeleported = _teleported;
		// Source sweeps an axis-aligned hull regardless of view/player yaw.
		_collision.GlobalBasis = Basis.Identity;
		var grounded = !_teleported && _body.IsOnFloor() && _body.Velocity.Y <= 140 * Unit;
		if (grounded) weaponSpeed *= DamageSpeedModifier;
		_teleported = false;
		UpdateDuck(command.Duck, grounded);
		UpdateWaterLevel();
		if (WaterLevel >= 2)
		{
			IsOnLadder = false;
			Swim(command, weaponSpeed, delta);
			return;
		}
		_ladderSuppression = Mathf.Max(0, _ladderSuppression - delta);
		if (TryLadder(command, grounded, delta)) return;
		var velocity = _body.Velocity;
		if (grounded) velocity.Y = 0;
		else velocity.Y -= Gravity * Unit * delta * 0.5f;

		var jumping = grounded && command.Jump && (AutoBunnyHop || !_jumpHeld || command.JumpPressed);
		_jumpHeld = command.Jump;
		if (jumping)
		{
			EmitSignal(SignalName.Jumped);
			if (!UnlimitedBunnyHop)
			{
				var horizontal = new Vector2(velocity.X, velocity.Z).LimitLength(weaponSpeed * Unit * 1.1f);
				velocity.X = horizontal.X;
				velocity.Z = horizontal.Y;
			}
			velocity.Y = JumpImpulse * Unit * StaminaRatio();
			StaminaSeconds = UseStamina ? 25f / 19f : 0;
			// The first half of gravity is applied after the jump impulse in Source.
			velocity.Y -= Gravity * Unit * delta * 0.5f;
			grounded = false;
		}

		var wish = _body.GlobalBasis * new Vector3(move.X, 0, move.Y);
		var wishSpeed = weaponSpeed * Unit * move.Length();
		if (command.Walk) wishSpeed *= WalkMultiplier;
		if (IsDucked || command.Duck) wishSpeed *= DuckMultiplier;
		var directionFlat = wish.Normalized();
		if (grounded)
		{
			velocity = ApplyFriction(velocity, Friction, StopSpeed * Unit, delta);
			var ratio = Mathf.Pow(StaminaRatio(), delta * 70);
			velocity.X *= ratio;
			velocity.Z *= ratio;
			velocity = Accelerate(velocity, directionFlat, wishSpeed, wishSpeed, GroundAcceleration, delta);
		}
		else
		{
			// Preserve Source's uncapped wishSpeed in the acceleration term.
			var surfaceFriction = velocity.Y > 0 && velocity.Y < 140 * Unit ? 0.25f : 1f;
			velocity = Accelerate(velocity, directionFlat, wishSpeed, Mathf.Min(wishSpeed, AirSpeedCap * Unit), AirAcceleration * surfaceFriction, delta);
		}

		_body.Velocity = velocity;
		var start = _body.GlobalTransform;
		// Source stores fall speed before StartGravity's first half step.
		var fallingSpeed = Mathf.Max(0, -velocity.Y / Unit - Gravity * delta * 0.5f);
		if (grounded)
		{
			_body.MoveAndSlide();
			PushPhysicsProps();
			TryStep(start, velocity, delta);
		}
		else MoveInAir(delta);
		CheckForLadders(grounded && _body.IsOnFloor());
		if (!wasTeleported && !grounded && _body.IsOnFloor())
		{
			UpdateWaterLevel();
			// CheckFalling reduces impact speed on descending/floating ground.
			// This is independent of whether movement inherits platform velocity.
			var floor = new KinematicCollision3D();
			if (_body.TestMove(_body.GlobalTransform, Vector3.Down * (_body.FloorSnapLength + 0.01f), floor,
				safeMargin: _body.SafeMargin, recoveryAsCollision: true))
			{
				if (floor.GetCollider() is Node ground && ground.HasMeta("source_floating") && ground.GetMeta("source_floating").AsBool())
					fallingSpeed -= 200;
				var groundSpeed = floor.GetColliderVelocity().Y / Unit;
				if (groundSpeed < 0) fallingSpeed = Mathf.Max(0.1f, fallingSpeed + groundSpeed);
			}
			EmitSignal(SignalName.Landed, fallingSpeed);
		}
		velocity = _body.Velocity;
		if (IsOnLadder || _body.IsOnFloor()) velocity.Y = 0;
		else velocity.Y -= Gravity * Unit * delta * 0.5f;
		_body.Velocity = velocity;
		UpdateView(delta);
		if (_body.IsOnFloor()) DamageSpeedModifier = Mathf.Min(1, DamageSpeedModifier + delta / 3);
	}

	private bool InWater(Vector3 position)
	{
		var query = new PhysicsPointQueryParameters3D
		{
			Position = position, CollisionMask = 4, CollideWithAreas = true, CollideWithBodies = false
		};
		return _body.GetWorld3D().DirectSpaceState.IntersectPoint(query, 1).Count > 0;
	}

	private void UpdateWaterLevel()
	{
		WaterLevel = 0;
		if (!InWater(_body.GlobalPosition + Vector3.Up * Unit)) return;
		WaterLevel = 1;
		if (!InWater(_body.GlobalPosition + Vector3.Up * _hull.Size.Y * 0.5f)) return;
		WaterLevel = 2;
		if (InWater(_body.GlobalPosition + Vector3.Up * _eye)) WaterLevel = 3;
	}

	// Source CGameMovement::WaterMove: 0.8 max speed, water friction/acceleration,
	// view-relative movement and 60 u/s idle sinking. Shallow water keeps WalkMove.
	private void Swim(PlayerCommand command, float weaponSpeed, float delta)
	{
		var velocity = _body.Velocity;
		var move = command.Move.LimitLength();
		var wish = _camera.GlobalBasis * new Vector3(move.X, 0, move.Y) * weaponSpeed * Unit;
		if (command.Jump) wish.Y += weaponSpeed * Unit;
		else if (move == Vector2.Zero) wish.Y -= 60 * Unit;
		var wishSpeed = Mathf.Min(wish.Length(), weaponSpeed * Unit) * 0.8f;
		var speed = velocity.Length();
		if (speed > 0) velocity *= Mathf.Max(0, 1 - Friction * WaterFriction * delta);
		velocity = Accelerate(velocity, wish.Normalized(), wishSpeed, wishSpeed, WaterAcceleration, delta);
		if (command.Jump) velocity.Y = 100 * Unit;
		_body.Velocity = velocity;
		_body.MoveAndSlide();
		PushPhysicsProps();
		_jumpHeld = command.Jump;
		UpdateView(delta);
		if (_body.IsOnFloor()) DamageSpeedModifier = Mathf.Min(1, DamageSpeedModifier + delta / 3);
	}

	private void PushPhysicsProps()
	{
		for (var i = 0; i < _body.GetSlideCollisionCount(); i++)
			PushPhysicsProp(_body.GetSlideCollision(i));
	}

	private static void PushPhysicsProp(KinematicCollision3D hit)
	{
		if (hit.GetCollider() is RigidBody3D networkProp && Contrapoint.Network.LanSession.Instance?.RequestPropPush(networkProp, hit.GetPosition(), -hit.GetNormal() * .7f) == true) return;
		if (hit.GetCollider() is RigidBody3D { Freeze: false } prop)
			prop.ApplyImpulse(-hit.GetNormal() * 0.7f, hit.GetPosition() - prop.GlobalPosition);
	}

	public static Vector3 Accelerate(Vector3 velocity, Vector3 direction, float wishSpeed, float cap, float acceleration, float delta)
	{
		var add = cap - velocity.Dot(direction);
		return add <= 0 ? velocity : velocity + direction * Mathf.Min(add, acceleration * wishSpeed * delta);
	}

	public static Vector3 ApplyFriction(Vector3 velocity, float friction, float stopSpeed, float delta)
	{
		var speed = new Vector2(velocity.X, velocity.Z).Length();
		if (speed <= 0) return velocity;
		var scale = Mathf.Max(0, speed - Mathf.Max(speed, stopSpeed) * friction * delta) / speed;
		return new(velocity.X * scale, velocity.Y, velocity.Z * scale);
	}

	private float StaminaRatio() => UseStamina ? Mathf.Clamp(1 - StaminaSeconds * 19 / 80, 0, 1) : 1;

	private void SetHull(float height)
	{
		_hull.Size = new(32 * Unit, height, 32 * Unit);
		_collision.Position = Vector3.Up * (height / 2);
	}

	private bool CanStand(Vector3 origin)
	{
		var space = _body.GetWorld3D().DirectSpaceState;
		var excluded = new Godot.Collections.Array<Rid> { _body.GetRid() };
		foreach (var body in _body.GetCollisionExceptions()) excluded.Add(body.GetRid());
		// Allow recovery from an existing player overlap. A player above the
		// crouched hull must still block expansion, just like a low ceiling.
		using var current = new PhysicsShapeQueryParameters3D
		{
			Shape = _hull, Transform = _collision.GlobalTransform,
			CollisionMask = _body.CollisionMask, Margin = 0,
			Exclude = excluded.Duplicate()
		};
		while (true)
		{
			var overlaps = space.IntersectShape(current, 1);
			if (overlaps.Count == 0) break;
			var rid = overlaps[0]["rid"].AsRid();
			if (overlaps[0]["collider"].AsGodotObject() is Player) excluded.Add(rid);
			var visited = current.Exclude;
			visited.Add(rid);
			current.Exclude = visited;
		}
		var query = new PhysicsShapeQueryParameters3D
		{
			Shape = _standHull,
			Transform = new(Basis.Identity, origin + Vector3.Up * (StandingHeight / 2 + _body.SafeMargin)),
			CollisionMask = _body.CollisionMask,
			Margin = 0.0001f,
			Exclude = excluded
		};
		while (true)
		{
			var hits = space.IntersectShape(query, 1);
			if (hits.Count == 0) return true;
			// Pickup exceptions are registered on the weapon, so a standalone
			// shape query does not inherit them from the movement body.
			if (hits[0]["collider"].AsGodotObject() is not Contrapoint.Weapons.WeaponPickup) return false;
			excluded.Add(hits[0]["rid"].AsRid());
			query.Exclude = excluded;
		}
	}

	private void UpdateDuck(bool duck, bool grounded)
	{
		if (duck == IsDucked) return;
		var shift = grounded ? 0 : (StandingHeight - DuckHeight) * 0.5f * (duck ? 1 : -1);
		var origin = _body.GlobalPosition + Vector3.Up * shift;
		if (!duck && !CanStand(origin)) return;
		_body.GlobalPosition = origin;
		// Compensate for hull-origin movement so the eye does not pop in mid-air.
		_eye -= shift;
		IsDucked = duck;
		SetHull(duck ? DuckHeight : StandingHeight);
	}

	private void TryStep(Transform3D start, Vector3 beforeSlide, float delta)
	{
		var horizontal = new Vector3(beforeSlide.X, 0, beforeSlide.Z) * delta;
		if (horizontal.LengthSquared() < 0.0000001f) return;
		var actual = _body.GlobalPosition - start.Origin;
		actual.Y = 0;
		if (actual.Length() + 0.001f < horizontal.Length())
		{
			var up = new KinematicCollision3D();
			var rise = Vector3.Up * (StepHeight * Unit + _body.SafeMargin);
			if (_body.TestMove(start, rise, up)) rise = up.GetTravel();
			var raised = start;
			raised.Origin += rise;
			var forward = new KinematicCollision3D();
			var travel = _body.TestMove(raised, horizontal, forward) ? forward.GetTravel() : horizontal;
			raised.Origin += travel;
			var down = new KinematicCollision3D();
			if (_body.TestMove(raised, Vector3.Down * (rise.Y + 2 * Unit), down)
				&& down.GetNormal().Y >= 0.7f && travel.LengthSquared() > actual.LengthSquared() + 0.000001f)
			{
				var destination = raised.Origin + down.GetTravel();
				var step = destination.Y - _body.GlobalPosition.Y;
				if (destination.Y - start.Origin.Y > 0.001f && destination.Y - start.Origin.Y <= StepHeight * Unit + 0.003f)
				{
					_body.GlobalPosition = destination;
					_body.Velocity = new(beforeSlide.X, 0, beforeSlide.Z);
					_stepOffset -= step;
					_body.ApplyFloorSnap();
				}
			}
		}
		// Follow descending stairs only if the tick started on a walkable floor.
		if (!_body.IsOnFloor())
		{
			var down = new KinematicCollision3D();
			if (_body.TestMove(_body.GlobalTransform, Vector3.Down * (StepHeight * Unit), down) && down.GetNormal().Y >= 0.7f)
			{
				_body.GlobalPosition += down.GetTravel();
				_stepOffset -= down.GetTravel().Y;
				_body.ApplyFloorSnap();
			}
		}
	}

	private void UpdateView(float delta)
	{
		_eye = Mathf.MoveToward(_eye, IsDucked ? DuckEye : StandingEye, (StandingEye - DuckEye) * delta / Mathf.Max(DuckSeconds, 0.001f));
		_stepOffset = Mathf.MoveToward(Mathf.Clamp(_stepOffset, -StepHeight * Unit, StepHeight * Unit), 0, 4 * delta);
		_view.Position = Vector3.Up * (_eye + _stepOffset);
	}
}
