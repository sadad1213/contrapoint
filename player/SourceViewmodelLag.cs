using Godot;
using System.Collections.Generic;

namespace Contrapoint.Players;

// CSSO CPredictedViewModel::CalcViewModelLag: interpolate angle history,
// then project the forward-vector difference into viewmodel space.
internal sealed class SourceViewmodelLag
{
	private readonly List<(double Time, Vector3 Angles)> _history = new();
	private double _time;
	private float _interpolation = -1;

	public void Reset()
	{
		_history.Clear();
		_time = 0;
		_interpolation = -1;
	}

	public Vector3 Sample(Basis basis, double delta, float interpolation, float scale)
	{
		if (delta <= 0) return Vector3.Zero;
		_time += delta;
		var angles = basis.GetEuler();
		if (_history.Count == 0 || !Mathf.IsEqualApprox(interpolation, _interpolation))
		{
			_history.Clear();
			_history.Add((_time - interpolation, angles));
			_interpolation = interpolation;
		}
		_history.Add((_time, angles));
		var sampleTime = _time - interpolation;
		while (_history.Count > 2 && _history[1].Time <= sampleTime) _history.RemoveAt(0);
		var first = _history[0];
		var second = _history[1];
		var fraction = (float)System.Math.Clamp((sampleTime - first.Time) / System.Math.Max(.000001, second.Time - first.Time), 0, 1);
		var lagged = new Vector3(Mathf.LerpAngle(first.Angles.X, second.Angles.X, fraction),
			Mathf.LerpAngle(first.Angles.Y, second.Angles.Y, fraction), Mathf.LerpAngle(first.Angles.Z, second.Angles.Z, fraction));
		var pitch = Mathf.AngleDifference(angles.X, lagged.X);
		var yaw = Mathf.AngleDifference(angles.Y, lagged.Y);
		var fade = Mathf.Clamp(Mathf.Abs(basis.Y.Dot(Vector3.Up)) - .02f, 0, 1);
		// Source forward/right/up -> Godot -Z/+X/+Y. Positive Source pitch is down.
		return new Vector3(-Mathf.Cos(pitch) * Mathf.Sin(yaw), Mathf.Sin(pitch),
			Mathf.Cos(pitch) * Mathf.Cos(yaw) - 1) * (scale * fade * SourceMovement.Unit);
	}
}
