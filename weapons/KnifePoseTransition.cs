using Godot;

// Inertialization: play the incoming animation at full strength and decay only
// the difference from the outgoing pose/velocity. Bone scale is never blended.
internal sealed class KnifePoseTransition
{
	private readonly Skeleton3D _skeleton;
	private readonly Transform3D[] _raw, _displayed, _outgoing, _incoming;
	private readonly Vector3[] _linearVelocity, _angularVelocity;
	private readonly Vector3[] _positionOffset, _rotationOffset, _positionRate, _rotationRate;
	private readonly Vector3[] _boneScales;
	private double _elapsed, _duration;
	private bool _restoreRequired;

	public KnifePoseTransition(Skeleton3D skeleton)
	{
		_skeleton = skeleton;
		var count = skeleton.GetBoneCount();
		_raw = new Transform3D[count];
		_displayed = new Transform3D[count];
		_outgoing = new Transform3D[count];
		_incoming = new Transform3D[count];
		_linearVelocity = new Vector3[count];
		_angularVelocity = new Vector3[count];
		_positionOffset = new Vector3[count];
		_rotationOffset = new Vector3[count];
		_positionRate = new Vector3[count];
		_rotationRate = new Vector3[count];
		_boneScales = new Vector3[count];
		for (var i = 0; i < count; i++)
		{
			_raw[i] = _displayed[i] = skeleton.GetBonePose(i);
			_boneScales[i] = skeleton.GetBonePoseScale(i);
		}
	}

	public void CaptureOutgoingMotion()
	{
		for (var i = 0; i < _raw.Length; i++)
			_outgoing[i] = _skeleton.GetBonePose(i);
	}

	public void RestoreAnimationPose()
	{
		if (!_restoreRequired) return;
		// Constant tracks were removed by import. Don't feed a previous visual
		// correction back into those unanimated properties on the next frame.
		for (var i = 0; i < _raw.Length; i++)
		{
			_skeleton.SetBonePosePosition(i, _raw[i].Origin);
			_skeleton.SetBonePoseRotation(i, Rotation(_raw[i]));
			// These clips have no scale tracks. Preserve the imported bone
			// lengths instead of accumulating matrix decomposition roundoff.
			_skeleton.SetBonePoseScale(i, _boneScales[i]);
		}
		_restoreRequired = false;
	}

	public void CaptureIncomingPose()
	{
		for (var i = 0; i < _raw.Length; i++)
			_incoming[i] = _skeleton.GetBonePose(i);
	}

	public void Start(double duration, double sampleTime)
	{
		_elapsed = 0;
		_duration = duration;
		var dt = (float)sampleTime;
		for (var i = 0; i < _raw.Length; i++)
		{
			var future = _skeleton.GetBonePose(i);
			var outgoingRotation = Rotation(_outgoing[i]);
			var incomingRotation = Rotation(_incoming[i]);
			_positionOffset[i] = _outgoing[i].Origin - _incoming[i].Origin;
			_positionRate[i] = _linearVelocity[i] - (future.Origin - _incoming[i].Origin) / dt;
			_rotationOffset[i] = Log(outgoingRotation * incomingRotation.Inverse());
			var predictedOutgoing = Exp(_angularVelocity[i] * dt) * outgoingRotation;
			var futureOffset = Log(predictedOutgoing * Rotation(future).Inverse());
			// A spinning inspect can cross the +/-PI logarithm boundary.
			// Use the equivalent neighboring rotation, not a full-turn impulse.
			if (futureOffset.LengthSquared() > 0.000001f)
			{
				var alternate = futureOffset - futureOffset.Normalized() * Mathf.Tau;
				if (alternate.DistanceSquaredTo(_rotationOffset[i]) < futureOffset.DistanceSquaredTo(_rotationOffset[i]))
					futureOffset = alternate;
			}
			_rotationRate[i] = (futureOffset - _rotationOffset[i]) / dt;
		}
	}

	public void Update(double delta)
	{
		_elapsed += delta;
		var s = _duration > 0 ? Mathf.Clamp((float)(_elapsed / _duration), 0, 1) : 1;
		var s2 = s * s;
		var s3 = s2 * s;
		// Exact outgoing pose/velocity at zero, zero correction and derivatives
		// at the end. Only this short residual is smoothed, not the strike.
		var poseWeight = 1 - 10 * s3 + 15 * s3 * s - 6 * s3 * s2;
		var rateWeight = (float)_duration * (s - 6 * s3 + 8 * s3 * s - 3 * s3 * s2);
		var correcting = s < 1;
		for (var i = 0; i < _raw.Length; i++)
		{
			var target = _skeleton.GetBonePose(i);
			_raw[i] = target;
			// After the residual has decayed, AnimationPlayer already owns the
			// final pose. Keep measuring velocity for the next interruption without
			// rewriting every bone or restoring it on the next frame.
			if (!correcting)
			{
				if (delta > 0.000001)
				{
					_linearVelocity[i] = (target.Origin - _displayed[i].Origin) / (float)delta;
					_angularVelocity[i] = Log(Rotation(target) * Rotation(_displayed[i]).Inverse()) / (float)delta;
				}
				_displayed[i] = target;
				continue;
			}
			var position = target.Origin + _positionOffset[i] * poseWeight + _positionRate[i] * rateWeight;
			var correction = _rotationOffset[i] * poseWeight + _rotationRate[i] * rateWeight;
			var rotation = (Exp(correction) * Rotation(target)).Normalized();
			_skeleton.SetBonePosePosition(i, position);
			_skeleton.SetBonePoseRotation(i, rotation);
			var displayed = _skeleton.GetBonePose(i);
			if (delta > 0.000001)
			{
				_linearVelocity[i] = (position - _displayed[i].Origin) / (float)delta;
				_angularVelocity[i] = Log(rotation * Rotation(_displayed[i]).Inverse()) / (float)delta;
			}
			_displayed[i] = displayed;
		}
		_restoreRequired = correcting;
	}

	private static Quaternion Rotation(Transform3D pose) => pose.Basis.GetRotationQuaternion().Normalized();

	private static Vector3 Log(Quaternion rotation)
	{
		rotation = rotation.Normalized();
		if (rotation.W < 0)
			rotation = -rotation;
		var vector = new Vector3(rotation.X, rotation.Y, rotation.Z);
		var length = vector.Length();
		return length < 0.000001f ? vector * 2 : vector * (2 * Mathf.Atan2(length, rotation.W) / length);
	}

	private static Quaternion Exp(Vector3 rotation)
	{
		var angle = rotation.Length();
		return angle < 0.000001f ? Quaternion.Identity : new Quaternion(rotation / angle, angle);
	}
}
