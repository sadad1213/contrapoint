using Godot;
using System;

namespace Contrapoint.Players;

public partial class SourceMovement
{
	// Source TryPlayerMove: sweep the hull, clip velocity, then spend the remaining
	// tick along the plane. Godot's grounded slide deliberately suppresses this
	// conversion of falling speed to slope speed, which is what surf relies on.
	private void MoveInAir(float delta, bool categorizeGround = true)
	{
		var velocity = _body.Velocity;
		// MoveAndCollide does not update CharacterBody3D's contact flags. Clear them
		// with a stationary floating move, then categorize walkable ground below.
		// This keeps IsOnFloor consistent for weapons, sounds and player animation.
		_body.MotionMode = CharacterBody3D.MotionModeEnum.Floating;
		_body.Velocity = Vector3.Zero;
		_body.MoveAndSlide();
		_body.MotionMode = CharacterBody3D.MotionModeEnum.Grounded;

		var primal = velocity;
		var original = velocity;
		Span<Vector3> planes = stackalloc Vector3[5];
		var planeCount = 0;
		var timeLeft = delta;
		for (var bump = 0; bump < 4 && timeLeft > 0 && velocity.LengthSquared() > 0; bump++)
		{
			var motion = velocity * timeLeft;
			var hit = _body.MoveAndCollide(motion, safeMargin: _body.SafeMargin);
			if (hit == null) break;
			PushPhysicsProp(hit);
			// Use the untraveled motion rather than recovery displacement to measure
			// time: a contact can also push the hull out of a tiny initial overlap.
			var remaining = Mathf.Clamp(hit.GetRemainder().Dot(motion) / motion.LengthSquared(), 0, 1);
			if (remaining < 1)
			{
				original = velocity;
				planeCount = 0;
			}
			timeLeft *= remaining;
			if (planeCount == planes.Length)
			{
				velocity = Vector3.Zero;
				break;
			}
			planes[planeCount++] = hit.GetNormal();
			if (planeCount == 1)
			{
				velocity = ClipVelocity(original, planes[0]);
				original = velocity;
				continue;
			}

			var valid = false;
			for (var i = 0; i < planeCount; i++)
			{
				velocity = ClipVelocity(original, planes[i]);
				valid = true;
				for (var j = 0; j < planeCount; j++)
					if (j != i && velocity.Dot(planes[j]) < 0)
					{
						valid = false;
						break;
					}
				if (valid) break;
			}
			if (!valid)
			{
				if (planeCount != 2)
				{
					velocity = Vector3.Zero;
					break;
				}
				var crease = planes[0].Cross(planes[1]).Normalized();
				velocity = crease * crease.Dot(velocity);
			}
			if (velocity.Dot(primal) <= 0)
			{
				velocity = Vector3.Zero;
				break;
			}
		}

		_body.Velocity = velocity;
		// Source CategorizePosition never grounds a rapidly rising player. Steep
		// planes remain airborne, without floor friction, snapping or jump impulses.
		if (categorizeGround && velocity.Y <= 140 * Unit)
		{
			var floor = new KinematicCollision3D();
			if (_body.TestMove(_body.GlobalTransform, Vector3.Down * _body.FloorSnapLength, floor,
				safeMargin: _body.SafeMargin, recoveryAsCollision: true) && floor.GetNormal().Y >= WalkableNormal)
				_body.ApplyFloorSnap();
		}
	}

	private static Vector3 ClipVelocity(Vector3 velocity, Vector3 normal)
	{
		var clipped = velocity - normal * velocity.Dot(normal);
		var adjust = clipped.Dot(normal);
		return adjust < 0 ? clipped - normal * adjust : clipped;
	}
}
