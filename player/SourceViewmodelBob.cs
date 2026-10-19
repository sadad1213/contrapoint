using Godot;
using Contrapoint.Weapons;

namespace Contrapoint.Players;

// CSSO CalcViewModelBobHelper/AddViewModelBobHelper and its legacy CS:S path.
// Provenance, installed settings and adaptation notes: VIEWMODEL.md.
// Keep one clock per player so switching weapons preserves the gait phase.
public partial class SourceViewmodelBob : Node
{
	[Export(PropertyHint.Range, "0.1,2,0.01")]
	public float CycleSeconds { get; set; } = 0.98f;
	[Export] public bool UseNewBob { get; set; } = true;
	[Export] public float OldCycleSeconds { get; set; } = 0.8f;
	[Export] public float VerticalAmount { get; set; } = 0.25f;
	[Export] public float LateralAmount { get; set; } = 0.4f;
	[Export] public float LowerAmount { get; set; } = 13;
	[Export] public float ShiftLeftAmount { get; set; } = 1.5f;
	[Export] public float ShiftRightAmount { get; set; } = 0.75f;
	[Export] public float SwayScale { get; set; } = 1.6f;
	[Export] public float SwayInterpolation { get; set; } = 0.1f;
	[Export(PropertyHint.Range, "0.01,0.99,0.01")]
	public float UpFraction { get; set; } = 0.5f;
	[Export(PropertyHint.Range, "0,3,0.05")]
	public float Amount { get; set; } = 1;
	public Transform3D Offset { get; private set; } = Transform3D.Identity;
	public Transform3D MotionOffset { get; private set; } = Transform3D.Identity;
	public Vector3 SwayOffset { get; private set; }
	public float SmoothedSpeed { get; private set; }
	public double PhaseSeconds { get; private set; }
	private Player _player = null!;
	private readonly SourceViewmodelLag _lag = new();
	private FirearmView? _accuracyWeapon;
	private float _accuracyPosition;
	private float _airLowerAngle;

	public override void _Ready() => _player = GetParent<Player>();

	public void Reset()
	{
		SmoothedSpeed = 0;
		PhaseSeconds = 0;
		Offset = Transform3D.Identity;
		MotionOffset = Transform3D.Identity;
		SwayOffset = Vector3.Zero;
		_accuracyWeapon = null;
		_accuracyPosition = _airLowerAngle = 0;
		_lag.Reset();
	}

	public override void _Process(double delta)
		=> Advance(delta, false);
	internal void AdvanceObserved(double delta) => Advance(delta, true);
	internal void ApplyNetworkOffset(Transform3D offset)
	{
		if (!_player.LocalInput && offset.IsFinite() && offset.Origin.LengthSquared() < 4
			&& Mathf.Abs(offset.Basis.Determinant() - 1) < .1f) MotionOffset = offset;
	}
	private void Advance(double delta, bool observing)
	{
		if (delta <= 0) return;
		// Source continues the cycle in the air using horizontal momentum.
		var speed = (_player.HasLocalControl || observing) && !_player.Movement.Noclip
			? _player.Movement.HorizontalSpeed : 0;
		SmoothedSpeed = Mathf.MoveToward(SmoothedSpeed, Mathf.Clamp(speed, 0, 320), (UseNewBob ? 640 : 320) * (float)delta);
		PhaseSeconds += delta * SmoothedSpeed / 320;
		var period = UseNewBob ? CycleSeconds : OldCycleSeconds;
		if (period <= 0 || UpFraction <= 0 || UpFraction >= 1)
		{
			Offset = Transform3D.Identity;
		}
		else UpdateBob((float)delta, period);
		// Source samples the viewmodel angles after bob, not raw mouse counts.
		var basis = _player.Camera.GlobalBasis * Offset.Basis;
		SwayOffset = _lag.Sample(basis, delta, SwayInterpolation, SwayScale);
		MotionOffset = new(Offset.Basis, Offset.Origin + Offset.Basis * SwayOffset);
	}

	private void UpdateBob(float delta, float period)
	{
		float vertical, lateral;
		if (UseNewBob)
		{
			period *= Mathf.Max(1, 1000 - _player.MaxMoveSpeed) / 3500;
			var multiplier = _player.IsOnFloor() ? 0.00625f : 0.00125f;
			var scoped = _player.ActiveWeapon is FirearmView { ZoomLevel: > 0 };
			// The standalone clamp() in CSSO's helper does not assign its result.
			var lowering = scoped ? 0 : LowerAmount * 0.2f * SmoothedSpeed * 0.006f;
			UpdateAccuracy(delta);
			vertical = Mathf.Clamp(Sample(period) * multiplier * VerticalAmount - lowering + _accuracyPosition * 0.2f, -7, 4);
			lateral = Mathf.Clamp(Sample(period * 2) * multiplier * LateralAmount + _accuracyPosition * 0.25f, -8, 8);
			var airborne = _player.IsAlive && !_player.Movement.Noclip && !_player.IsOnFloor();
			_airLowerAngle = Mathf.MoveToward(_airLowerAngle, airborne ? 2 : 0, 6 * delta);
		}
		else
		{
			vertical = Mathf.Clamp(Sample(period) * 0.005f, -7, 4);
			lateral = Mathf.Clamp(Sample(period * 2) * 0.005f, -7, 4);
			_airLowerAngle = 0;
		}
		vertical *= Amount;
		lateral *= Amount;
		// Source forward -> Godot -Z; Source world up -> camera-local world up.
		var up = _player.Camera.GlobalBasis.Inverse() * Vector3.Up;
		var position = (Vector3.Forward * (vertical * 0.4f) + up * (vertical * 0.1f - _airLowerAngle * 0.4f * Amount)
			+ Vector3.Right * (UseNewBob ? lateral * 0.2f : 0)) * SourceMovement.Unit;
		// Source pitch is positive down; Godot pitch is positive up.
		var rotation = new Vector3(vertical * 0.4f + _airLowerAngle * 0.2f * Amount, -lateral * 0.3f, -vertical * 0.5f) * (Mathf.Pi / 180);
		Offset = new(Basis.FromEuler(rotation), position);
	}

	private void UpdateAccuracy(float delta)
	{
		var gun = _player.ActiveWeapon as FirearmView;
		if (gun != _accuracyWeapon) { _accuracyWeapon = gun; _accuracyPosition = 0; }
		var difference = 0f;
		if (gun is { Busy: false } && gun.Kind != FirearmView.FirearmKind.Elite && CssoRifleBallistics.Supports(gun.Kind)
			&& _player.HasLocalControl && !_player.Movement.Noclip)
		{
			var data = CssoRifleBallistics.Data(gun);
			var accuracy = _player.Movement.IsDucked ? data.Crouch : CssoRifleBallistics.Inaccuracy(gun, _player);
			var multiplier = accuracy < data.Stand ? (gun.IsSniper ? .15f : 18) * ShiftLeftAmount : ShiftRightAmount;
			if (accuracy >= data.Stand) accuracy = Mathf.Min(accuracy, .082f);
			difference = Mathf.Max((accuracy - data.Stand) * multiplier, -.1f);
		}
		_accuracyPosition = Mathf.Lerp(_accuracyPosition, difference * 80, 1 - Mathf.Exp(-4 * delta));
	}

	private float Sample(float period)
	{
		var cycle = (float)((PhaseSeconds % period) / period);
		var angle = cycle < UpFraction
			? Mathf.Pi * cycle / UpFraction
			: Mathf.Pi + Mathf.Pi * (cycle - UpFraction) / (1 - UpFraction);
		return SmoothedSpeed * (0.3f + 0.7f * Mathf.Sin(angle));
	}
}
