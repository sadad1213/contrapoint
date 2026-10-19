using Godot;
using System;
using System.Linq;
using Contrapoint.Network;
using Contrapoint.Players;
using Contrapoint.Weapons;

namespace Contrapoint.Gameplay.Bots;

public enum BotState { Patrol, Investigate, Engage, Search, Objective }

public partial class BotController : Node
{
	public Player Player { get; private set; } = null!;
	public BotProfile Profile { get; private set; } = null!;
	public BotState State { get; private set; }
	public Player? Target { get; private set; }
	public int PathsFound { get; private set; }
	public int ShotsFired { get; private set; }
	public Vector3 LastKnownPosition { get; private set; }
	private BotManager _manager = null!;
	private Player? _candidate;
	private Vector3 _aimSpot, _goal, _lastProgress, _aimError, _aimErrorGoal;
	private Vector3[] _path = [];
	private int _waypoint;
	private double _time, _thinkAt, _candidateSince, _acquiredAt, _lastSeen = -100, _memoryUntil;
	private double _repathAt, _progressAt, _recoverUntil, _nextShot, _offsetAt, _focusAt, _strafeAt;
	private double _reacquireAt;
	private double _patrolAt;
	private int _boughtRound = -1;
	private PhysicsRayQueryParameters3D _sightRay = null!, _floorRay = null!;
	private float _yawVelocity, _pitchVelocity, _strafe;
	private bool _visible, _crouchAndHold, _shouldDodge;
	public void Initialize(BotManager manager, Player player, BotProfile profile)
	{ _manager = manager; Player = player; Profile = profile; }
	public override void _Ready()
	{
		Player.Respawned += Reset;
		_sightRay = new() { CollisionMask = 1 };
		_floorRay = new() { CollisionMask = 3, Exclude = new() { Player.GetRid() } };
		Reset();
		_thinkAt = GD.RandRange(0, .1);
	}
	public override void _ExitTree()
	{
		if (IsInstanceValid(Player)) Player.Respawned -= Reset;
		_sightRay.Dispose(); _floorRay.Dispose();
	}
	private void Reset()
	{
		Target = _candidate = null; _visible = false; _path = []; _waypoint = 0;
		_lastSeen = -100; _memoryUntil = 0; _goal = Player.GlobalPosition;
		_lastProgress = Player.GlobalPosition; _progressAt = _time; _recoverUntil = 0;
		_yawVelocity = _pitchVelocity = 0; _aimError = _aimErrorGoal = Vector3.Zero;
		_nextShot = _time + .5; _repathAt = 0; State = BotState.Patrol;
		_patrolAt = 0;
		_crouchAndHold = _shouldDodge = false;
		ConfigureWeapon();
	}
	private void ConfigureWeapon()
	{
		if (DefuseRules.Active) { Player.Inventory.EquipBest(); return; }
		var settings = _manager.Settings;
		bool Allowed(string weapon) => weapon switch
		{
			"ak47" or "m4a1" or "famas" or "galil" or "aug" or "sg552" => settings.Rifles,
			"awp" or "scout" or "g3sg1" or "sg550" => settings.Snipers,
			"mac10" or "mp5navy" or "tmp" or "ump45" or "p90" => settings.SubmachineGuns,
			"m3" or "xm1014" => settings.Shotguns, "m249" => settings.MachineGuns, _ => false
		};
		var primary = Profile.Weapons.FirstOrDefault(Allowed);
		primary ??= settings.Rifles ? (Player.Team == PlayerTeam.Terrorist ? "ak47" : "m4a1") : settings.Snipers ? "awp"
			: settings.SubmachineGuns ? "mp5navy" : settings.Shotguns ? "m3" : settings.MachineGuns ? "m249" : null;
		foreach (var weapon in Player.Inventory.Weapons.ToArray())
			if (weapon is FirearmView || weapon is GrenadeView) Player.Inventory.RemoveWeapon(weapon, false);
		if (settings.Pistols) Player.Inventory.AddWeapon(PlayerSceneCache.Load(WeaponPickup.ViewPath("weapon_" + (Player.Team == PlayerTeam.Terrorist ? "deagle" : "usp"))), false);
		if (primary != null) Player.Inventory.AddWeapon(PlayerSceneCache.Load(WeaponPickup.ViewPath("weapon_" + primary)), false);
		Player.Inventory.ResetAmmunition();
		Player.Inventory.EquipBest();
	}
	public bool IsEnemy(Player other) => other != Player && other.IsAlive && DeathmatchRules.Playing(other)
		&& !DeathmatchRules.Teammates(Player, other);
	public bool CanSee(Player other, bool fieldOfView = true)
	{
		if (!IsEnemy(other) || Player.GrenadeVision.FlashRemaining > .2) return false;
		var eye = Player.Camera.GlobalPosition;
		var center = other.GlobalPosition + Vector3.Up * (other.Movement.IsDucked ? .65f : 1.15f);
		if (fieldOfView && (-Player.Camera.GlobalBasis.Z).Dot((center - eye).Normalized()) < Mathf.Cos(Mathf.DegToRad(45))) return false;
		foreach (var smoke in _manager.Smoke)
			if (smoke.Density > .25f)
			{
				var segment = center - eye;
				var nearest = eye + segment * Mathf.Clamp((smoke.GlobalPosition - eye).Dot(segment) / Mathf.Max(.001f, segment.LengthSquared()), 0, 1);
				if (smoke.EyeDensity(nearest) > .3f) return false;
			}
		_sightRay.Exclude = _manager.SightExcluded;
		_sightRay.From = eye;
		for (var sample = 0; sample < 2; sample++)
		{
			var point = sample == 0 ? center : other.Camera.GlobalPosition;
			_sightRay.To = point;
			using var hit = Player.GetWorld3D().DirectSpaceState.IntersectRay(_sightRay);
			if (hit.Count == 0) { _aimSpot = point; return true; }
		}
		return false;
	}
	public void Hear(Player source, Vector3 position, float range)
	{
		if (!Player.IsAlive || !IsEnemy(source) || _visible || Player.GlobalPosition.DistanceTo(position) > range) return;
		// Sound supplies an uncertain location, never a tracked entity behind a wall.
		if (_time < _lastSeen + 1 || State == BotState.Investigate && _memoryUntil > _time + 3) return;
		LastKnownPosition = position + new Vector3((float)GD.RandRange(-1.5, 1.5), 0, (float)GD.RandRange(-1.5, 1.5));
		_goal = LastKnownPosition; _memoryUntil = _time + 5; _repathAt = 0; State = BotState.Investigate;
	}
	private void Think()
	{
		var previouslyVisible = _visible;
		_visible = Target != null && IsInstanceValid(Target) && CanSee(Target);
		var targetSpot = _aimSpot;
		var nearestSpot = _aimSpot;
		Player? nearest = null;
		var distance = float.MaxValue;
		foreach (var other in _manager.Players)
		{
			var d = Player.GlobalPosition.DistanceSquaredTo(other.GlobalPosition);
			if (d >= distance || !(other == Target ? _visible : CanSee(other))) continue;
			nearest = other; distance = d; nearestSpot = other == Target ? targetSpot : _aimSpot;
		}
		if (nearest != _candidate) { _candidate = nearest; _candidateSince = _time; }
		_aimSpot = targetSpot;
		if (nearest != null && _time - _candidateSince >= Profile.Reaction && (!_visible || nearest == Target))
		{
			if (Target != nearest)
			{
				Target = nearest; _acquiredAt = _time; _focusAt = _time;
				ChooseAttackPosture(nearest);
			}
			_visible = true; _aimSpot = nearestSpot;
		}
		if (_visible && Target != null)
		{
			if (!previouslyVisible) _reacquireAt = _time + Profile.Reaction;
			if (State != BotState.Engage || _goal.DistanceSquaredTo(Target.GlobalPosition) > 1) _repathAt = 0;
			LastKnownPosition = Target.GlobalPosition; _lastSeen = _time; _memoryUntil = _time + 6;
			State = BotState.Engage; _goal = LastKnownPosition;
		}
		else if (_time < _memoryUntil)
		{
			if (State == BotState.Engage) { State = BotState.Search; _repathAt = 0; }
			_goal = LastKnownPosition;
		}
		else
		{
			Target = null;
			if (State is not (BotState.Patrol or BotState.Objective)) { _path = []; _patrolAt = _repathAt = 0; }
			State = BotState.Patrol;
		}
		if (Player.ActiveWeapon is FirearmView gun)
		{
			if (DefuseRules.Active && DefuseState.Instance is { } de && (!_visible && _time >= _memoryUntil || UrgentDefuse(de)))
			{
				State = BotState.Objective;
				var objective = de.BotGoal(Player);
				if (_goal.DistanceSquaredTo(objective) > 1) { _goal = objective; _repathAt = 0; }
			}
			if (gun.MagazineAmmo == 0 && gun.ReserveAmmo == 0)
			{
				var replacement = Player.Inventory.Weapons.OfType<FirearmView>().FirstOrDefault(w => w != gun && (w.MagazineAmmo > 0 || w.ReserveAmmo > 0));
				if (replacement != null) Player.Inventory.Equip(replacement); else Player.Inventory.EquipSlot(WeaponSlot.Melee);
			}
			else if (!_visible && _time > _lastSeen + 1.5 && gun.MagazineAmmo < gun.MagazineCapacity / 2)
				gun.HandleCommand("reload", true);
		}
	}
	private void ChooseAttackPosture(Player enemy)
	{
		var sniper = Player.ActiveWeapon is FirearmView { IsSniper: true };
		var enemySniper = enemy.ActiveWeapon is FirearmView { IsSniper: true };
		var range = Player.GlobalPosition.DistanceTo(enemy.GlobalPosition) / SourceMovement.Unit;
		var crouchChance = sniper || range > 750 ? .5 : .2 * (1 - Profile.Aggression);
		_crouchAndHold = false;
		if (!enemySniper && Player.ActiveWeapon is FirearmView && GD.Randf() < crouchChance)
		{
			_sightRay.Exclude = _manager.SightExcluded;
			_sightRay.From = Player.GlobalPosition + Vector3.Up * SourceMovement.DuckEye;
			_sightRay.To = enemy.Camera.GlobalPosition;
			using var hit = Player.GetWorld3D().DirectSpaceState.IntersectRay(_sightRay);
			_crouchAndHold = hit.Count == 0;
		}
		_shouldDodge = Profile.Skill > .5f && enemySniper || GD.Randf() <= .8f * Profile.Skill;
	}
	public override void _PhysicsProcess(double delta)
	{
		if (!Player.IsAlive || LanSession.Instance?.IsClient == true || !(DeathmatchRules.Active || DefuseRules.Active)) return;
		if (DefuseRules.Active && DefuseState.Instance is { } de)
		{
			if (de.Phase is DefusePhase.Freeze or DefusePhase.Waiting)
			{
				if (_boughtRound != de.RoundNumber && BuyCatalog.CanOpen(Player))
				{
					_boughtRound = de.RoundNumber;
					if (Player.Team == PlayerTeam.CounterTerrorist) Player.Buy("defuser");
					Player.AutoBuy();
				}
				Player.Simulate(default, (float)delta); return;
			}
			if (de.Phase is DefusePhase.Ended or DefusePhase.Intermission) { Player.Simulate(default, (float)delta); return; }
			if (TickObjective(de, (float)delta)) return;
		}
		_time += delta;
		if (_time >= _thinkAt) { _thinkAt = _time + .1; Think(); }
		var dt = (float)delta;
		var knife = Player.ActiveWeapon is KnifeView;
		var sniper = Player.ActiveWeapon is FirearmView { IsSniper: true };
		var attack = _visible && Target != null && !(DefuseRules.Active && DefuseState.Instance is { } objective && UrgentDefuse(objective));
		var destination = attack && !knife ? _goal : FollowPath();
		var range = attack ? Player.Camera.GlobalPosition.DistanceTo(_aimSpot) : 0;
		var move = destination - Player.GlobalPosition; move.Y = 0;
		if (move.LengthSquared() > .01f) move = move.Normalized();
		if (attack && !knife)
		{
			if (_time >= _strafeAt) { _strafeAt = _time + GD.RandRange(.8, 2); _strafe = GD.RandRange(-1, 1); }
			// Stop for accurate shots. Rifles may sidestep between firing windows.
			move = !sniper && !_crouchAndHold && _shouldDodge && _time + .18 < _nextShot ? Player.GlobalBasis.X * _strafe : Vector3.Zero;
		}
		if (State == BotState.Search && _time - _lastSeen < 2 + 2 * (1 - Profile.Aggression)
			+ (sniper ? 3 : _crouchAndHold ? 1 : 0)) move = Vector3.Zero;
		if (move != Vector3.Zero)
		{
			foreach (var other in _manager.Players)
			{
				if (other == Player || !other.IsAlive) continue;
				var away = Player.GlobalPosition - other.GlobalPosition; away.Y = 0;
				if (away.LengthSquared() is > .001f and < 2 && Mathf.Abs(other.GlobalPosition.Y - Player.GlobalPosition.Y) < 1.5f)
					move += away.Normalized() * (1.4f - away.Length());
			}
			move = move.LimitLength(1);
		}
		var stuck = move.LengthSquared() > .2f && _time - _progressAt > 1;
		if (Player.GlobalPosition.DistanceSquaredTo(_lastProgress) > .3f || move.LengthSquared() < .1f)
		{ _lastProgress = Player.GlobalPosition; _progressAt = _time; }
		if (stuck) { _recoverUntil = _time + .6; _progressAt = _time; _repathAt = 0; Player.UseWorld(); }
		if (_time < _recoverUntil) move = (move + Player.GlobalBasis.X * ((_time % 2 < 1) ? 1 : -1)).Normalized();
		if (attack)
		{
			UpdateAimError(range, dt);
			LookAt(_aimSpot + _aimError, true, dt);
		}
		else LookAt(destination + Vector3.Up * SourceMovement.StandingEye, false, dt);
		// Guard strafe/recovery movement against walking off the navigation surface.
		if (move != Vector3.Zero && !HasFloor(Player.GlobalPosition + move * .75f)) move = Vector3.Zero;
		if (_manager.Settings.Stop) move = Vector3.Zero;
		var local = Player.GlobalBasis.Inverse() * move;
		Player.Simulate(new(new(local.X, local.Z), !_manager.Settings.Stop && stuck && Player.IsOnFloor(), attack && _crouchAndHold && !knife, false), dt);
		Player.ActiveWeapon?.HandleCommand("attack", false);
		if (attack) Fire(range, sniper, knife);
	}
	private bool TickObjective(DefuseState de, float dt)
	{
		if (_manager.Settings.Stop) { Player.ObjectiveAttackHeld = Player.ObjectiveUseHeld = false; return false; }
		if (de.Carrier == Player && de.BombSite(Player) != null && (!_visible || de.Planter == Player))
		{
			Player.Inventory.EquipSlot(WeaponSlot.Objective);
			Player.ActiveWeapon?.HandleCommand("attack", true);
			Player.Simulate(default, dt); return true;
		}
		Player.ObjectiveAttackHeld = false;
		if (de.BombPlanted && Player.Team == PlayerTeam.CounterTerrorist
			&& Player.GlobalPosition.DistanceTo(de.BombPosition) < DefuseRules.UseDistance * .9f
			&& (!_visible || de.Defuser == Player || de.BombRemaining < 12))
		{
			LookAt(de.BombPosition, false, dt);
			Player.ObjectiveUseHeld = true;
			Player.Simulate(default, dt); return true;
		}
		Player.ObjectiveUseHeld = false;
		if (Player.ActiveWeapon is C4View) Player.Inventory.EquipBest();
		return false;
	}
	private bool UrgentDefuse(DefuseState de) => de.BombPlanted && Player.Team == PlayerTeam.CounterTerrorist
		&& de.BombRemaining < (Player.HasDefuser ? 10 : 15);
	private Vector3 FollowPath()
	{
		if (State == BotState.Patrol && _time >= _patrolAt && (_path.Length == 0 || FlatDistance(Player.GlobalPosition, _goal) < 1))
		{
			_goal = _manager.PatrolGoal(Player.GlobalPosition, out _path); _waypoint = 0;
			_patrolAt = _time + 1;
			_repathAt = _path.Length > 1 ? double.PositiveInfinity : _patrolAt;
			if (_path.Length > 1) PathsFound++;
		}
		if (_time >= _repathAt)
		{
			_path = _manager.FindPath(Player.GlobalPosition, _goal); _waypoint = 0;
			_repathAt = State == BotState.Patrol && _path.Length > 1 ? double.PositiveInfinity : _time + 1;
			if (_path.Length > 1) PathsFound++;
			else if (State == BotState.Patrol) _goal = Player.GlobalPosition;
		}
		while (_waypoint < _path.Length && FlatDistance(Player.GlobalPosition, _path[_waypoint]) < .45f
			&& Mathf.Abs(Player.GlobalPosition.Y - _path[_waypoint].Y) < .8f) _waypoint++;
		return _waypoint < _path.Length ? _path[_waypoint] : Player.GlobalPosition;
	}
	private static float FlatDistance(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();
	private bool HasFloor(Vector3 foot)
	{
		_floorRay.From = foot + Vector3.Up * .5f;
		_floorRay.To = foot + Vector3.Down;
		using var hit = Player.GetWorld3D().DirectSpaceState.IntersectRay(_floorRay);
		return hit.Count > 0;
	}
	private void UpdateAimError(float range, float dt)
	{
		if (Mathf.Abs(_yawVelocity) > 100) _focusAt = _time;
		if (_time >= _offsetAt)
		{
			var focus = Mathf.Min(.75f, (float)(_time - _focusAt) / Mathf.Max(5 * (1 - Profile.Skill), 2));
			var accuracy = Mathf.Max(Profile.Skill, focus);
			var error = Player.Camera.Fov / 75 * .05f * range * (1 - accuracy);
			_aimErrorGoal = new((float)GD.RandRange(-error, error), (float)GD.RandRange(-error, error), (float)GD.RandRange(-error, error));
			_offsetAt = _time + GD.RandRange(.25, 1);
		}
		_aimError = _aimError.Lerp(_aimErrorGoal, 1 - Mathf.Exp(-6.6f * dt));
	}
	private void LookAt(Vector3 point, bool attacking, float dt)
	{
		var direction = point - Player.Camera.GlobalPosition;
		if (direction.LengthSquared() < .01f) return;
		var yaw = Mathf.RadToDeg(Mathf.Atan2(-direction.X, -direction.Z));
		var pitch = Mathf.RadToDeg(Mathf.Atan2(direction.Y, new Vector2(direction.X, direction.Z).Length()));
		var currentYaw = Mathf.RadToDeg(Player.Rotation.Y); var currentPitch = Mathf.RadToDeg(Player.AimPitch);
		var stiffness = attacking ? 300f : 200f; var damping = attacking ? 30f : 25f;
		var difference = Mathf.Wrap(yaw - currentYaw, -180, 180);
		if (Mathf.Abs(difference) < 1) { currentYaw = yaw; _yawVelocity = 0; }
		else { _yawVelocity += dt * Mathf.Clamp(stiffness * difference - damping * _yawVelocity, -3000, 3000); currentYaw += dt * _yawVelocity; }
		_pitchVelocity += dt * Mathf.Clamp(2 * stiffness * (pitch - currentPitch) - damping * _pitchVelocity, -3000, 3000);
		Player.SetBotAim(Mathf.DegToRad(currentYaw), Mathf.DegToRad(Mathf.Clamp(currentPitch + dt * _pitchVelocity, -89, 89)));
	}
	private void Fire(float range, bool sniper, bool knife)
	{
		if (_manager.Settings.Difficulty == "harmless" || _manager.Settings.DontShoot) return;
		if (Player.ActiveWeapon is FirearmView { Busy: true }) return;
		if (_time < _acquiredAt + Profile.AttackDelay || _time < _reacquireAt || _time < _nextShot || Target == null || !CanSee(Target)) return;
		var direction = (_aimSpot - Player.Camera.GlobalPosition).Normalized();
		var halfWidth = (sniper ? 16 : 32) * SourceMovement.Unit;
		if ((-Player.Camera.GlobalBasis.Z).Dot(direction) < Mathf.Cos(Mathf.Atan(halfWidth / Mathf.Max(.1f, range)))) return;
		// Keep the muzzle clear of allies, including when friendly fire is disabled.
		foreach (var friend in _manager.Players)
		{
			if (!friend.IsAlive || !DeathmatchRules.Teammates(Player, friend)) continue;
			var offset = friend.Camera.GlobalPosition - Player.Camera.GlobalPosition;
			var along = offset.Dot(direction);
			if (along > 0 && along < range && (offset - direction * along).Length() < .6f) return;
		}
		if (knife)
		{
			if (range < 1.25f) { Player.ActiveWeapon?.HandleCommand("attack", true); _nextShot = _time + .5; }
			return;
		}
		if (Player.ActiveWeapon is not FirearmView gun || gun.Busy) return;
		if (sniper)
		{
			if (gun.ZoomLevel == 0) { gun.HandleCommand("attack2", true); _nextShot = _time + Profile.Reaction; return; }
			if (Player.Movement.HorizontalSpeed > 10 || Mathf.Abs(_yawVelocity) > 10) return;
		}
		var before = gun.ShotsFired;
		gun.HandleCommand("attack", true);
		gun.HandleCommand("attack", false);
		if (gun.ShotsFired == before) return;
		ShotsFired++;
		var units = range / SourceMovement.Unit;
		var pistol = gun.Slot == WeaponSlot.Secondary;
		var pause = pistol ? Profile.Skill > .75f && units < 360 ? .05 : GD.RandRange(.15, .4)
			: Profile.Skill < .5f || units < 400 ? .05 : !sniper && units > 800 ? GD.RandRange(.3, .7) : GD.RandRange(.15, .25);
		_nextShot = _time + Math.Max(.05, pause - .1);
	}
}
