using Godot;
using System;
using Contrapoint.Players;

namespace Contrapoint.Weapons;

// Source's swept 4-unit hull and custom fly bounce, simulated at the world's
// 66 Hz tick rate. Jolt restitution would give a different trajectory.
public partial class GrenadeProjectile : CharacterBody3D
{
	public int NetworkId { get; set; }
	public bool NetworkReplica { get; set; }
	public Vector3 VisualRotation { get => _model.Rotation; set => _model.Rotation = value; }
	public void ConfigureNetwork(GrenadeKind kind, Player? thrower) { Kind = kind; Thrower = thrower; }
	public GrenadeKind Kind { get; private set; }
	public Player? Thrower { get; private set; }
	public double Age { get; private set; }
	public bool Detonated { get; private set; }
	public int Bounces { get; private set; }
	public Vector3 InitialVelocity { get; private set; }
	public const float HullHalfSize = 2 * SourceMovement.Unit;
	public const float Elasticity = 0.45f;
	public const float FuseSeconds = 1.5f;
	private Node3D _model = null!;
	private Vector3 _spin;
	private bool _resting, _ownerExcluded;
	private double _nextBounceSound, _nextSmokeCheck = FuseSeconds;
	private double _nextWaterThink;
	private float Gravity => (GodotObject.IsInstanceValid(Thrower) ? Thrower!.Movement.Gravity : 800) * 0.4f * SourceMovement.Unit;

	public static Vector3 ThrowVelocity(Basis aim, Vector3 inheritedVelocity)
	{
		var forward = -aim.Z.Normalized();
		var pitch = Mathf.Asin(Mathf.Clamp(forward.Y, -1, 1));
		pitch += Mathf.DegToRad(10) * (1 - Mathf.Abs(pitch) / (Mathf.Pi * 0.5f));
		var horizontal = new Vector3(forward.X, 0, forward.Z).Normalized();
		if (horizontal.IsZeroApprox()) horizontal = new Vector3(-aim.Z.X, 0, -aim.Z.Z).Normalized();
		var direction = horizontal * Mathf.Cos(pitch) + Vector3.Up * Mathf.Sin(pitch);
		return direction * (750 * 0.9f * SourceMovement.Unit) + inheritedVelocity * 1.25f;
	}

	public static GrenadeProjectile Throw(GrenadeView weapon, Camera3D camera, bool drop = false)
	{
		weapon.Wielder?.EndSpawnProtection();
		var velocity = drop ? weapon.Wielder?.Velocity ?? Vector3.Zero
			: ThrowVelocity(camera.GlobalBasis, weapon.Wielder?.Velocity ?? Vector3.Zero);
		var direction = drop ? -camera.GlobalBasis.Z : ThrowVelocity(camera.GlobalBasis, Vector3.Zero).Normalized();
		var world = weapon.GetTree().CurrentScene as Node3D;
		if (world == null)
		{
			world = weapon;
			while (world.GetParent() is Node3D parent) world = parent;
		}
		var projectile = new GrenadeProjectile { Name = weapon.AssetName + "_projectile", Kind = weapon.Kind, Thrower = weapon.Wielder };
		world.AddChild(projectile);
		var eye = camera.GlobalPosition;
		var motion = direction * (22 * SourceMovement.Unit);
		var query = new PhysicsShapeQueryParameters3D
		{
			Shape = new BoxShape3D { Size = Vector3.One * (HullHalfSize * 2) },
			Transform = new Transform3D(Basis.Identity, eye), Motion = motion, CollisionMask = 1,
			Margin = 0.001f
		};
		if (weapon.Wielder != null) query.Exclude = new() { weapon.Wielder.GetRid() };
		var fractions = camera.GetWorld3D().DirectSpaceState.CastMotion(query);
		projectile.GlobalPosition = eye + motion * fractions[0] - direction * (6 * SourceMovement.Unit);
		projectile.Velocity = projectile.InitialVelocity = velocity;
		Contrapoint.Network.LanSession.Instance?.RegisterProjectile(projectile);
		return projectile;
	}

	public override void _Ready()
	{
		CollisionLayer = 0;
		CollisionMask = 1; // Player clips and triggers do not block grenades.
		SafeMargin = 0.001f;
		AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = Vector3.One * HullHalfSize * 2 } });
		var asset = Kind switch { GrenadeKind.HE => "hegrenade", GrenadeKind.Flashbang => "flashbang", _ => "smokegrenade" };
		_model = GD.Load<PackedScene>($"res://assets/weapons/{asset}/thrown/{asset}_thrown.glb").Instantiate<Node3D>();
		AddChild(_model);
		WorldWeaponMaterials.Apply(_model);
		_spin = new Vector3(Mathf.DegToRad(600), Mathf.DegToRad(GD.RandRange(-1200, 1200)), 0);
		if (GodotObject.IsInstanceValid(Thrower))
		{
			AddCollisionExceptionWith(Thrower!);
			_ownerExcluded = true;
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (NetworkReplica) return;
		if (Detonated) return;
		Age += delta;
		if (_ownerExcluded && GodotObject.IsInstanceValid(Thrower))
		{
			var relative = GlobalPosition - Thrower!.GlobalPosition;
			if (Mathf.Abs(relative.X) > 18 * SourceMovement.Unit || Mathf.Abs(relative.Z) > 18 * SourceMovement.Unit
				|| relative.Y < -HullHalfSize || relative.Y > SourceMovement.StandingHeight + HullHalfSize)
			{
				RemoveCollisionExceptionWith(Thrower);
				_ownerExcluded = false;
			}
		}
		if (!_resting) Fly((float)delta);
		if (Age >= _nextWaterThink)
		{
			_nextWaterThink = Age + 0.2;
			if (InWater(GlobalPosition)) Velocity *= 0.5f;
		}
		if (Kind == GrenadeKind.Smoke)
		{
			if (Age >= _nextSmokeCheck)
			{
				_nextSmokeCheck = Age + 0.2;
				if (Velocity.Length() <= 0.1f * SourceMovement.Unit) Detonate();
			}
		}
		else if (Age + 0.000001 >= FuseSeconds) Detonate();
		if (Age > 60 || GlobalPosition.Y < -200) QueueFree();
	}

	private void Fly(float delta)
	{
		var velocity = Velocity;
		var newY = velocity.Y - Gravity * delta;
		var motion = new Vector3(velocity.X * delta, (velocity.Y + newY) * 0.5f * delta, velocity.Z * delta);
		velocity.Y = newY;
		Velocity = velocity;
		_model.RotateObjectLocal(_spin.Normalized(), _spin.Length() * delta);
		for (var hitIndex = 0; hitIndex < 4 && !motion.IsZeroApprox(); hitIndex++)
		{
			var collision = MoveAndCollide(motion);
			if (collision == null) break;
			var normal = collision.GetNormal();
			var target = collision.GetCollider() as Node;
			var incoming = Velocity;
			if (target != null && target.HasMethod("source_damage"))
			{
				target.Call("source_damage", 10, collision.GetPosition(), incoming.Normalized());
				if (target is CollisionObject3D { CollisionLayer: 0 })
				{
					Velocity = incoming * 0.4f;
					motion = collision.GetRemainder() * 0.4f;
					continue;
				}
			}
			var reflected = incoming - normal * incoming.Dot(normal) * 2;
			reflected *= Elasticity * (target is Player ? 0.3f : 1);
			for (var axis = 0; axis < 3; axis++)
				if (Mathf.Abs(reflected[axis]) < 0.1f * SourceMovement.Unit) reflected[axis] = 0;
			Velocity = reflected;
			Bounces++;
			if (Age >= _nextBounceSound && incoming.Length() > 30 * SourceMovement.Unit)
			{
				var name = Kind switch { GrenadeKind.HE => "hegrenade.bounce", GrenadeKind.Flashbang => "flashbang.bounce", _ => "smokegrenade.bounce" };
				SourceSoundPlayer.PlayWorld(GetParent<Node3D>(), GlobalPosition, name);
				Contrapoint.Network.LanSession.Instance?.BroadcastWorldScript(GlobalPosition, name);
				_nextBounceSound = Age + 0.1;
			}
			if (normal.Y > 0.7f && reflected.LengthSquared() < Mathf.Pow(20 * SourceMovement.Unit, 2))
			{
				_resting = true;
				Velocity = Vector3.Zero;
				break;
			}
			// Complete the untravelled part of the tick with the reflected velocity.
			var fraction = motion.Length() > 0 ? collision.GetRemainder().Length() / motion.Length() : 0;
			delta *= fraction;
			motion = Velocity * delta;
		}
	}

	private bool InWater(Vector3 at)
	{
		var point = new PhysicsPointQueryParameters3D
		{
			Position = at, CollisionMask = 4, CollideWithAreas = true, CollideWithBodies = false
		};
		return GetWorld3D().DirectSpaceState.IntersectPoint(point, 1).Count > 0;
	}

	private bool CanReach(Player player, Vector3 target)
	{
		var ray = PhysicsRayQueryParameters3D.Create(GlobalPosition + Vector3.Up * SourceMovement.Unit, target, 1);
		var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
		return hit.Count == 0 || hit["collider"].AsGodotObject() == player;
	}

	private int PlayerWaterLevel(Player player)
	{
		// Remote players are not movement-simulated on the host. Query their
		// current hull/eye so water separation also works for LAN explosions.
		if (!InWater(player.GlobalPosition + Vector3.Up * SourceMovement.Unit)) return 0;
		var middle = player.GlobalPosition + Vector3.Up * (player.Movement.IsDucked ? 27 : 36) * SourceMovement.Unit;
		if (!InWater(middle)) return 1;
		return InWater(player.Camera.GlobalPosition) ? 3 : 2;
	}

	public void Detonate()
	{
		if (Detonated || NetworkReplica) return;
		Detonated = true;
		Contrapoint.Network.LanSession.Instance?.ProjectileDetonated(this);
		var world = GetParent<Node3D>();
		var name = Kind switch { GrenadeKind.HE => "basegrenade.explode", GrenadeKind.Flashbang => "flashbang.explode", _ => "basesmokeeffect.sound" };
		SourceSoundPlayer.PlayWorld(world, GlobalPosition, name);
		if (Kind == GrenadeKind.Smoke)
		{
			var cloud = new GrenadeSmoke { Name = "CSSSmoke" };
			world.AddChild(cloud);
			cloud.GlobalPosition = GlobalPosition;
			Contrapoint.Network.LanSession.Instance?.RegisterSmoke(cloud);
		}
		else
		{
			GrenadeBurst.Spawn(world, GlobalPosition, Kind == GrenadeKind.Flashbang);
			foreach (var node in GetTree().GetNodesInGroup("source_players"))
			{
				if (node is not Player { IsAlive: true } player) continue;
				var target = Kind == GrenadeKind.Flashbang ? player.Camera.GlobalPosition
					: player.GlobalPosition + Vector3.Up * (player.Movement.IsDucked ? 27 : 36) * SourceMovement.Unit;
				var distance = GlobalPosition.DistanceTo(target) / SourceMovement.Unit;
				var radius = Kind == GrenadeKind.Flashbang ? 1500 : 350;
				if (Kind == GrenadeKind.Flashbang && (distance >= radius || !CanReach(player, target))) continue;
				var underwater = InWater(GlobalPosition);
				var waterLevel = PlayerWaterLevel(player);
				if ((underwater && waterLevel == 0) || (!underwater && waterLevel == 3)) continue;
				if (Kind == GrenadeKind.HE)
				{
					var source = GlobalPosition + Vector3.Up * SourceMovement.Unit;
					var center = target;
					// CBasePlayer::BodyTarget(false) is the eye; the default noisy
					// BodyTarget used for falloff samples 70–100% of the eye offset.
					var eye = player.Camera.GlobalPosition;
					target = player.GlobalPosition + (eye - player.GlobalPosition) * (float)GD.RandRange(0.7, 1.0);
					// Source's sphere query selects intersecting entity bounds, not just centres.
					var half = new Vector3(16, player.Movement.IsDucked ? 27 : 36, 16) * SourceMovement.Unit;
					var nearest = center + (source - center).Clamp(-half, half);
					if (source.DistanceTo(nearest) > radius * SourceMovement.Unit) continue;
					player.TakeHeDamage(Player.HeDamage(source.DistanceTo(target) / SourceMovement.Unit,
						HeExposure(player, source, eye)), GodotObject.IsInstanceValid(Thrower) ? Thrower : null, source);
				}
				else
				{
					var facing = (-player.Camera.GlobalBasis.Z).Dot((GlobalPosition - target).Normalized());
					var intensity = 4 * (1 - distance / radius);
					player.GrenadeVision.Flash(intensity, facing);
					Contrapoint.Network.LanSession.Instance?.Flash(player, intensity, facing);
				}
			}
		}
		QueueFree();
	}

	private float HeExposure(Player player, Vector3 source, Vector3 chest)
	{
		var side = player.GlobalBasis.X.Normalized() * (16 * SourceMovement.Unit);
		return 0.4f * ExplosionAdjustment(player, source, chest)
			+ 0.2f * ExplosionAdjustment(player, source, player.GlobalPosition + Vector3.Up * (72 * SourceMovement.Unit))
			+ 0.2f * ExplosionAdjustment(player, source, player.GlobalPosition)
			+ 0.1f * ExplosionAdjustment(player, source, chest - side)
			+ 0.1f * ExplosionAdjustment(player, source, chest + side);
	}

	private float ExplosionAdjustment(Player player, Vector3 source, Vector3 target)
	{
		var ray = PhysicsRayQueryParameters3D.Create(source, target, 1);
		ray.Exclude = new() { player.GetRid(), GetRid() };
		ray.HitFromInside = true;
		var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
		if (hit.Count == 0) return 1;
		var blocker = hit["collider"].AsGodotObject();
		if (blocker is not RigidBody3D && blocker is not Player) return 0;
		// Trace world geometry again, excluding dynamic entities, as CONTENTS_SOLID does.
		var ignored = ray.Exclude;
		while (hit.Count > 0)
		{
			if (hit["collider"].AsGodotObject() is not CollisionObject3D body || body is not (RigidBody3D or Player)) return 0;
			ignored.Add(body.GetRid()); ray.Exclude = ignored;
			hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
		}
		return blocker is RigidBody3D prop ? 1 - Mathf.Clamp(SourceSurfaceDensity.For(prop) / 3000, 0, 1) : 0.75f;
	}
}
