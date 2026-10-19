using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using Contrapoint.Network;
using Contrapoint.Gameplay;
using GDictionary = Godot.Collections.Dictionary;

namespace Contrapoint.Players;

public partial class PlayerDeathState : Node3D
{
	public bool Active { get; private set; }
	public double Age { get; private set; }
	public bool CanRespawn => !DefuseRules.Active && Active && Age >= (DeathmatchRules.Active ? DeathmatchRules.RespawnSeconds : SourceDeathRules.DeathCamSeconds);
	public SourceRagdoll? Ragdoll { get; private set; }
	public Camera3D Camera { get; private set; } = null!;
	public Player? Killer { get; private set; }
	public Vector3 Impulse { get; private set; }
	public Vector3 HitPosition { get; private set; }
	public Vector3 DeathVelocity { get; private set; }
	public SourceHitGroup HitGroup { get; private set; }
	public string Cause { get; private set; } = "";
	public bool Frozen { get; private set; }
	private Player _player = null!;
	private GDictionary _context = new();
	private Vector3 _deathForward, _travelStart, _freezePosition, _freezeLook;
	private bool _travelStarted;
	private bool _freezeEnded;
	private float _distance;
	private readonly SphereShape3D _probe = new() { Radius = 4 * SourceMovement.Unit };
	private Contrapoint.UI.DeathScreen? _screen;
	private SourceSoundPlayer? _freezeSound;
	public Player? ObserverTarget => _player.Observer?.Target;
	public bool CycleObserver(int step = 1) => _player.Observer?.Cycle(step) == true;
	public void ToggleObserverMode() => _player.Observer?.ToggleMode();
	public void EndFreezeForObserver() { Frozen = false; _freezeEnded = true; _screen?.UnfreezeView(); }
	public void Bind(Player player)
	{
		_player = player; TopLevel = true; ProcessPriority = 150;
		Camera = new Camera3D { Name = "DeathCamera", Near = .05f, Far = player.Camera.Far, CullMask = 1 };
		AddChild(Camera);
		if (player.LocalInput) { _screen = new Contrapoint.UI.DeathScreen(); AddChild(_screen); _screen.Bind(player); }
	}
	public void Prepare(Player? attacker, Vector3 position, Vector3 impulse, SourceHitGroup group, string cause)
	{
		_player.Body.EnsurePose();
		Killer = attacker; HitPosition = position; Impulse = impulse; HitGroup = group;
		DeathVelocity = _player.Velocity; Cause = cause;
		var pose = new Godot.Collections.Array<Transform3D>();
		for (var i = 0; i < _player.Body.Skeleton.GetBoneCount(); i++) pose.Add(_player.Body.Skeleton.GetBonePose(i));
		_context = new() { ["killer"] = attacker?.NetworkPeerId ?? 0, ["position"] = position, ["impulse"] = impulse,
			["velocity"] = DeathVelocity, ["group"] = (int)group, ["cause"] = cause,
			["model"] = _player.Body.ModelGlobalTransform, ["pose"] = pose, ["yaw"] = _player.Rotation.Y };
	}
	public GDictionary CaptureContext() => _context;
	public void ReceiveContext(GDictionary context)
	{
		_context = context;
		Killer = LanSession.Instance?.Players.GetValueOrDefault(context["killer"].AsInt32());
		HitPosition = context["position"].AsVector3(); Impulse = context["impulse"].AsVector3();
		DeathVelocity = context["velocity"].AsVector3(); HitGroup = (SourceHitGroup)context["group"].AsInt32(); Cause = context["cause"].AsString();
	}
	public void Begin()
	{
		if (Active) return;
		_player.Observer?.Stop();
		if (_context.Count == 0) Prepare(null, _player.GlobalPosition + Vector3.Up, Vector3.Zero, SourceHitGroup.Generic, "");
		Active = true; Age = 0; Frozen = false; _travelStarted = _freezeEnded = false; _distance = 32 * SourceMovement.Unit;
		_deathForward = -_player.Camera.GlobalBasis.Z;
		// World pickups have their own bodies; the corpse carries no attached gun.
		_player.Body.RemoveWorldWeapon();
		Ragdoll = new SourceRagdoll { Name = "Ragdoll_" + _player.NetworkPeerId };
		_player.GetParent().AddChild(Ragdoll);
		Ragdoll.Initialize(_player, DeathVelocity, _context["model"].AsTransform3D(),
			_context["pose"].AsGodotArray<Transform3D>().ToArray());
		Ragdoll.ApplyImpact(HitPosition, Impulse, HitGroup);
		_player.Body.Visible = false;
		_player.GetNode<CollisionShape3D>("CollisionShape3D").SetDeferred(CollisionShape3D.PropertyName.Disabled, true);
		if (_player.LocalInput)
		{
			_player.GetNode<CanvasLayer>("WeaponLayer").Visible = false;
			Camera.GlobalTransform = (_player.IsThirdPerson ? _player.ThirdPerson.Camera : _player.FirstPerson ?? _player.Camera).GlobalTransform;
			Camera.Fov = 75;
			Camera.MakeCurrent();
		}
		_screen?.ShowDeath();
	}
	public void Reset()
	{
		_player.Observer?.Stop();
		Active = false; Age = 0; Frozen = false; _context.Clear(); Killer = null;
		_screen?.ClearDeath();
		_freezeSound?.StopAll();
		_player.Body.Visible = true;
		_player.GetNode<CollisionShape3D>("CollisionShape3D").SetDeferred(CollisionShape3D.PropertyName.Disabled, false);
		if (_player.LocalInput)
		{
			Camera.Current = false;
			(_player.IsThirdPerson ? _player.ThirdPerson.Camera : _player.FirstPerson ?? _player.Camera).MakeCurrent();
			_player.SetMouseCaptured(_player.Menu?.IsOpen != true && _player.Console?.IsOpen != true);
			_player.GetNode<CanvasLayer>("WeaponLayer").Visible = !_player.IsThirdPerson;
		}
	}
	public bool RequestRespawn()
	{
		if (!CanRespawn) return false;
		_player.Respawn(); return true;
	}
	private Vector3 Clip(Vector3 from, Vector3 to)
	{
		var excluded = new Godot.Collections.Array<Rid>();
		foreach (Player body in GetTree().GetNodesInGroup("source_players")) excluded.Add(body.GetRid());
		var query = new PhysicsShapeQueryParameters3D { Shape = _probe, Transform = new(Basis.Identity, from), Motion = to - from, CollisionMask = 1, Exclude = excluded };
		var fractions = GetWorld3D().DirectSpaceState.CastMotion(query);
		return from.Lerp(to, fractions.Length > 0 ? Mathf.Max(0, fractions[0] - .01f) : 1);
	}
	public override void _PhysicsProcess(double delta)
	{
		if (!Active) return;
		Age += delta;
		if (!DefuseRules.Active && (DeathmatchRules.Active || SourceDeathRules.AutoRespawn) && Age >= (DeathmatchRules.Active ? DeathmatchRules.RespawnSeconds : SourceDeathRules.RespawnDelay) && !_player.PickingClass
			&& _player.Team is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist && LanSession.Instance?.IsClient != true)
		{ _player.Respawn(); return; }
		if (!_player.LocalInput) return;
		if (_player.Observer?.Following == true) return;
		if (Frozen && (SourceDeathRules.FreezeCamDisabled || Age >= SourceDeathRules.DeathCamSeconds + SourceDeathRules.FreezeTravelSeconds + SourceDeathRules.FreezeSeconds))
		{ Frozen = false; _freezeEnded = true; _screen?.UnfreezeView(); }
		if (Frozen) return;
		if (DefuseRules.Active && Age >= SourceDeathRules.DeathCamSeconds
			&& (Killer == null || _freezeEnded || SourceDeathRules.FreezeCamDisabled))
		{
			_player.Observer?.Start();
			if (_player.Observer?.Following == true) return;
		}
		var focus = GodotObject.IsInstanceValid(Ragdoll) ? Ragdoll!.Focus + Vector3.Up * (14 * SourceMovement.Unit)
			: _player.GlobalPosition + Vector3.Up * .5f;
		var killer = GodotObject.IsInstanceValid(Killer) && Killer != _player ? Killer : null;
		if (Age < SourceDeathRules.DeathCamSeconds || killer == null || SourceDeathRules.FreezeCamDisabled || _freezeEnded)
		{
			_distance = Mathf.Min(96 * SourceMovement.Unit, _distance + (float)delta * 48 * SourceMovement.Unit);
			var look = killer != null ? killer.Camera.GlobalPosition : focus + _deathForward * 3;
			var interpolation = Mathf.Clamp((float)Age / (killer != null && !SourceDeathRules.FreezeCamDisabled ? SourceDeathRules.DeathCamSeconds : SourceDeathRules.FreezeSeconds), 0, 1);
			var direction = _deathForward.Slerp((look - focus).Normalized(), interpolation).Normalized();
			Camera.GlobalPosition = Clip(focus, focus - direction * _distance);
			if (Camera.GlobalPosition.DistanceSquaredTo(look) > .001f) Camera.LookAt(look);
			return;
		}
		if (!_travelStarted)
		{
			_travelStarted = true; _travelStart = Camera.GlobalPosition;
			_screen?.ShowFreezePanel();
			_freezeSound ??= new SourceSoundPlayer();
			if (!_freezeSound.IsInsideTree()) AddChild(_freezeSound);
			_freezeSound.PlayWave("ui/freeze_cam.wav", relay: false);
			_freezeLook = killer.Camera.GlobalPosition;
			var flat = (_freezeLook - _travelStart); flat.Y = 0;
			if (flat.LengthSquared() < .001f) flat = -killer.GlobalBasis.Z;
			var distance = (float)GD.RandRange(Mathf.Min(SourceDeathRules.FreezeDistanceMin, SourceDeathRules.FreezeDistanceMax), Mathf.Max(SourceDeathRules.FreezeDistanceMin, SourceDeathRules.FreezeDistanceMax));
			_freezePosition = Clip(_freezeLook, _freezeLook - flat.Normalized() * (distance * SourceMovement.Unit));
		}
		var progress = Mathf.Clamp((float)(Age - SourceDeathRules.DeathCamSeconds) / SourceDeathRules.FreezeTravelSeconds, 0, 1);
		// CSS Interpolators::SmoothStepEnd.
		var t = progress * .5f + .5f;
		var eased = (3 * t * t - 2 * t * t * t - .5f) * 2;
		Camera.GlobalPosition = Clip(_freezeLook, _travelStart.Lerp(_freezePosition, eased));
		if (Camera.GlobalPosition.DistanceSquaredTo(_freezeLook) > .001f) Camera.LookAt(_freezeLook);
		if (progress >= 1) { Frozen = true; _screen?.FreezeView(); }
	}
}
