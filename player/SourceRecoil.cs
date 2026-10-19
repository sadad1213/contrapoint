using Godot;
using Contrapoint.Weapons;

namespace Contrapoint.Players;

// Firearms use installed CSSO table-driven recoil, velocity decay and tracking.
// Angles retain Source pitch-down/yaw-left units.
public partial class SourceRecoil : Node
{
	public Vector2 PunchDegrees { get; private set; }
	public Vector2 ViewPunchDegrees { get; private set; }
	public Vector3 DamagePunchDegrees { get; private set; }
	public int ShotsInBurst { get; private set; }
	public int Direction { get; private set; }
	private Player _player = null!;
	private bool _delayFire, _pistol;
	private double _time, _decreaseAt;
	private Vector2 _lastApplied;
	private Vector2 _viewTarget;
	private bool _rifle;
	private Vector2 _rifleVelocity, _rifleViewKick;

	public override void _Ready()
	{
		_player = GetParent<Player>();
	}

	public void Reset()
	{
		PunchDegrees = Vector2.Zero;
		DamagePunchDegrees = Vector3.Zero;
		ViewPunchDegrees = Vector2.Zero;
		_viewTarget = Vector2.Zero;
		_rifle = false;
		_rifleVelocity = _rifleViewKick = Vector2.Zero;
		Direction = 0;
		_time = 0;
		foreach (var weapon in _player.Inventory.Weapons)
			if (weapon is FirearmView gun && CssoRifleBallistics.Supports(gun.Kind)) gun.RifleAccuracy.Reset();
		ResetBurst();
		ApplyView();
	}

	// Deploy/reload resets the shot counter, while the remaining punch decays.
	public void ResetBurst()
	{
		ShotsInBurst = 0;
		_delayFire = false;
		_decreaseAt = _time;
	}

	public void AddShot(FirearmView.FirearmKind kind, bool silenced = false, float patternIndex = -1)
	{
		var previousPunch = PunchDegrees;
		ShotsInBurst++;
		_delayFire = true;
		_pistol = FirearmData.For(kind).Automatic == false;
		if (CssoRifleBallistics.Supports(kind))
		{
			if (!_rifle) _rifleViewKick = ViewPunchDegrees - PunchDegrees * .9f;
			_rifle = true;
			var impulse = CssoRifleBallistics.Impulse(kind, silenced, patternIndex < 0 ? ShotsInBurst - 1 : patternIndex);
			_rifleVelocity += impulse;
			_rifleViewKick += impulse * .055f;
			UpdateRifleView();
			return;
		}
		_rifle = false;
		_rifleVelocity = _rifleViewKick = Vector2.Zero;
		PunchDegrees += new Vector2(-2, 0);
		_viewTarget = ViewPunchDegrees + PunchDegrees - previousPunch;
		ViewPunchDegrees = _viewTarget;
		ApplyView();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (delta <= 0) return;
		var hadDamagePunch = DamagePunchDegrees != Vector3.Zero;
		var damageTime = delta;
		while (damageTime > .000001)
		{
			var dt = (float)System.Math.Min(damageTime, 1.0 / 66);
			var damageLength = DamagePunchDegrees.Length();
			if (damageLength > 0) DamagePunchDegrees *= Mathf.Max(0, damageLength - (10 + damageLength * .5f) * dt) / damageLength;
			damageTime -= dt;
		}
		_time += delta;
		var held = _player.ActiveWeapon is FirearmView { TriggerHeld: true };
		if (!held)
		{
			if (_delayFire)
			{
				_delayFire = false;
				ShotsInBurst = Mathf.Min(ShotsInBurst, 15);
				_decreaseAt = _time + 0.4;
			}
			if (_pistol) ShotsInBurst = 0;
			else if (ShotsInBurst > 0 && _decreaseAt < _time)
			{
				_decreaseAt = _time + 0.0225;
				ShotsInBurst--;
			}
		}
		if (_rifle)
		{
			// CSSO integrates aim velocity and decays the real aim; the camera reads
			// that state directly (tracking .45 * recoil scale 2), with no follow lerp.
			var remaining = delta;
			while (remaining > 0.000001)
			{
				var dt = (float)System.Math.Min(remaining, 1.0 / 66);
				var decayed = PunchDegrees * Mathf.Exp(-8 * dt);
				PunchDegrees = decayed.MoveToward(Vector2.Zero, 18 * dt);
				PunchDegrees += _rifleVelocity * dt * .5f;
				_rifleVelocity *= Mathf.Exp(-4.5f * dt);
				PunchDegrees += _rifleVelocity * dt * .5f;
				_rifleViewKick *= Mathf.Exp(-18 * dt);
				remaining -= dt;
			}
			if (_rifleVelocity.LengthSquared() < .00000001f && PunchDegrees.LengthSquared() < .00000001f)
				PunchDegrees = _rifleVelocity = _rifleViewKick = Vector2.Zero;
			UpdateRifleView();
			return;
		}
		var length = PunchDegrees.Length();
		if (length > 0)
		{
			var remaining = Mathf.Max(0, length - (10 + length * 0.5f) * (float)delta);
			PunchDegrees = PunchDegrees / length * remaining;
			_viewTarget *= remaining / length;
		}
		ViewPunchDegrees = _viewTarget;
		if (ViewPunchDegrees != Vector2.Zero || _lastApplied != Vector2.Zero || hadDamagePunch) ApplyView();
	}

	public void SetDamagePunch(Vector3 punch)
	{
		// TraceAttack sets pitch, preserves existing yaw, and sets roll on headshots.
		DamagePunchDegrees = new(punch.X - PunchDegrees.X, DamagePunchDegrees.Y,
			punch.Z == 0 ? DamagePunchDegrees.Z : punch.Z);
		ApplyView();
	}

	public void AddLandingPunch(float speed)
	{
		DamagePunchDegrees = new(DamagePunchDegrees.X, DamagePunchDegrees.Y, speed * .013f);
		ApplyView();
	}

	private void UpdateRifleView()
	{
		ViewPunchDegrees = PunchDegrees * .9f + _rifleViewKick;
		ApplyView();
	}

	private Vector3 Rotation => new(-Mathf.DegToRad(PunchDegrees.X + DamagePunchDegrees.X),
		Mathf.DegToRad(PunchDegrees.Y + DamagePunchDegrees.Y), -Mathf.DegToRad(DamagePunchDegrees.Z));
	private Vector3 ViewRotation => new(-Mathf.DegToRad(ViewPunchDegrees.X + DamagePunchDegrees.X),
		Mathf.DegToRad(ViewPunchDegrees.Y + DamagePunchDegrees.Y), -Mathf.DegToRad(DamagePunchDegrees.Z));

	public void ApplyView()
	{
		var rotation = new Vector3(_player.AimPitch, 0, 0) + ViewRotation;
		_player.Camera.Rotation = rotation;
		_lastApplied = ViewPunchDegrees;
	}

	public Transform3D GetBulletAim()
	{
		// Strip cosmetic camera motion, then apply Source's 2x ballistic punch.
		// This is sampled before this shot adds its new recoil impulse.
		var camera = _player.Camera;
		var basis = camera.GetParent<Node3D>().GlobalBasis * Basis.FromEuler(camera.Rotation - ViewRotation + Rotation * 2);
		return new(basis, camera.GlobalPosition);
	}
}
