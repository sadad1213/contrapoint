using Godot;
using System;
using System.Linq;
using Contrapoint.Players;

namespace Contrapoint.Weapons;

// Shared clip controller; ammunition and damage remain separate.
public partial class FirearmView : WeaponView
{
	public enum FirearmKind { AK47, Deagle, USP, M4A1, AWP, Scout, AUG, Elite, Famas, FiveSeven,
		G3SG1, Galil, Glock, M249, M3, MAC10, MP5Navy, P228, P90, SG550, SG552, TMP, UMP45, XM1014 }
	[Export] public FirearmKind Kind { get; set; }
	public bool IsPistol => Kind is FirearmKind.Deagle or FirearmKind.USP or FirearmKind.Elite
		or FirearmKind.FiveSeven or FirearmKind.Glock or FirearmKind.P228;
	public bool IsSniper => Kind is FirearmKind.AWP or FirearmKind.Scout or FirearmKind.G3SG1 or FirearmKind.SG550;
	public bool IsShotgun => Kind is FirearmKind.M3 or FirearmKind.XM1014;
	public bool BurstMode { get; private set; }
	public void ApplyNetworkBurst(bool value) { BurstMode = value && Kind is FirearmKind.Glock or FirearmKind.Famas; }
	public bool AccuracyAlternate => HasSilencer ? Silenced : HasZoom ? ZoomLevel > 0 : BurstMode;
	public int PelletCount => FirearmData.For(Kind).Pellets;
	public string PlayerAnimationExtension => FirearmData.For(Kind).Extension;
	public override WeaponSlot Slot => IsPistol ? WeaponSlot.Secondary : WeaponSlot.Primary;
	public override float MovementSpeed => ZoomLevel == 0 ? FirearmData.For(Kind).Speed : Kind switch
	{
		FirearmKind.AWP => 150, FirearmKind.Scout => 220,
		FirearmKind.G3SG1 or FirearmKind.SG550 => 150, _ => FirearmData.For(Kind).Speed
	};
	[Export(PropertyHint.Range, "0,0.3,0.005")]
	public double ReturnTransitionSeconds { get; set; } = 0.12;
	private bool? _silenced;
	public bool Silenced { get => _silenced ?? HasSilencer; private set => _silenced = value; }
	public void ApplyNetworkSilencer(bool value) { Silenced = value; }
	public int ZoomLevel { get; private set; }
	// Source uses a horizontal FOV at 4:3; mouse scaling uses those same angles.
	public float SourceZoomFov => ZoomLevel == 0 ? 90 : !IsSniper ? 55 : ZoomLevel == 1 ? 40 : Kind == FirearmKind.AWP ? 10 : 15;
	public int ShotsFired { get; private set; }
	public bool TriggerHeld => _triggerHeld;
	internal CssoRifleAccuracy RifleAccuracy { get; } = new();
	// Retained as an API alias; stock damage comes from the installed CSS script.
	public int BulletDamage => EffectiveBulletDamage;
	public int EffectiveBulletDamage => SourceDamageRules.BulletDamage(this);
	// CS:S GetAmmoDef / CalculateBulletDamageForce, phys_pushscale = 1.
	// https://github.com/nillerusr/source-engine/blob/master/game/shared/cstrike/cs_gamerules.cpp
	public float BulletPhysicsImpulse => (AmmoType switch
	{
		AmmunitionType.Acp45 or AmmunitionType.Mm9 or AmmunitionType.Sig357 or AmmunitionType.Mm57 => 2100f,
		AmmunitionType.Magnum338 => 2800f,
		AmmunitionType.Buckshot => 600f,
		_ => 2400f
	}) * SourceMovement.Unit;
	[Export(PropertyHint.Range, "0,2,0.05")] public float SpreadScale { get; set; } = 1;
	public bool Busy => _action is Action.Reload or Action.Silencer or Action.ReloadStart or Action.ReloadShell;

	private enum Action { Draw, Idle, Fire, Reload, Silencer, ReloadStart, ReloadShell, ReloadEnd }
	private Action _action;
	private double _fireRemaining;
	private double _zoomRemaining;
	// CSS keeps the magnification of a scoped shot (player m_iLastZoom) and the
	// pending restore flag (player m_bResumeZoom) on the player.
	private int _resumeZoom;
	private bool _resumeZoomPending;
	private double _animationLength;
	private bool _animationLoops;
	private bool _triggerHeld;
	private bool _secondaryHeld;
	private int _fireIndex;
	private int _burstRemaining;
	private double _burstDelay;
	private double _shellRemaining;
	private Camera3D _camera = null!;
	private float _defaultFov;
	private string _draw = "draw", _idle = "idle", _reload = "reload";
	private string[] _shots = [];
	private bool HasSilencer => Kind is FirearmKind.USP or FirearmKind.M4A1;
	private bool HasZoom => IsSniper || Kind is FirearmKind.AUG or FirearmKind.SG552;
	// CSS releases the scope after a scoped shot only for the AWP and the Scout
	// (game/shared/cstrike/weapon_awp.cpp, weapon_scout.cpp).
	private bool HasResumeZoom => Kind is FirearmKind.AWP or FirearmKind.Scout;
	private bool Automatic => FirearmData.For(Kind).Automatic;
	private double BurstShotInterval => Kind == FirearmKind.Glock ? .06 : .075;
	public double FireInterval => BurstMode ? Kind == FirearmKind.Glock ? .5 : .55
		: Kind is FirearmKind.AUG or FirearmKind.SG552 && ZoomLevel > 0 ? .135 : FirearmData.For(Kind).Cycle;
	protected override double DeploySeconds => Animations.GetAnimation(Clip(_draw)).Length;
	private string Clip(string name) => HasSilencer && !Silenced ? name + "_unsil" : name;

	public override void _Ready()
	{
		base._Ready();
		_camera = GetParent().GetParent<Camera3D>();
		_defaultFov = _camera.Fov;
		(_draw, _idle, _reload, _shots) = Kind switch
		{
			FirearmKind.AK47 => ("ak47_draw", "ak47_idle", "ak47_reload", new[] { "ak47_fire1", "ak47_fire2", "ak47_fire3" }),
			FirearmKind.Deagle => ("draw", "idle1", "reload", new[] { "shoot1", "shoot2" }),
			FirearmKind.AWP => ("awm_draw", "awm_idle", "awm_reload", new[] { "awm_fire" }),
			FirearmKind.Scout => ("draw", "idle1", "reload", new[] { "shoot" }),
			FirearmKind.Glock => ("glock_draw", "glock_idle", "glock_reload", new[] { "glock_firesingle" }),
			FirearmKind.MAC10 => ("mac10_draw", "mac10_idle", "mac10_reload", new[] { "mac10_fire", "mac10_fire2", "mac10_fire3" }),
			_ => ("draw", Animations.HasAnimation("idle") ? "idle" : "idle1",
				IsShotgun ? "start_reload" : Animations.HasAnimation("reload") ? "reload" : "reload1",
				Animations.GetAnimationList().Select(n => n.ToString()).Where(n =>
					(n.StartsWith("shoot", StringComparison.Ordinal) || n.StartsWith("fire", StringComparison.Ordinal))
					&& !n.Contains("empty") && !n.Contains("last") && !n.Contains("unsil")).Order().ToArray())
		};
		foreach (MeshInstance3D mesh in Model.FindChildren("*", "MeshInstance3D", true, false))
		{
			for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
			{
				if (mesh.GetActiveMaterial(surface) is not StandardMaterial3D source) continue;
				var material = (StandardMaterial3D)source.Duplicate();
				// Mirrored, double-sided viewmodels receive inward lighting in Forward+.
				material.CullMode = BaseMaterial3D.CullModeEnum.Back;
				// Match the matte AK finish across firearms; hands and scope glass
				// retain their own material treatment.
				if (source.ResourceName is not ("v_hands" or "v_AWP_scope"))
				{
					material.Metallic = 0;
					material.Roughness = 0.85f;
					material.MetallicSpecular = 0.2f;
				}
				mesh.SetSurfaceOverrideMaterial(surface, material);
			}
		}
		Animations.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
		Animations.GetAnimation(_idle).LoopMode = Animation.LoopModeEnum.Linear;
		if (HasSilencer) Animations.GetAnimation(_idle + "_unsil").LoopMode = Animation.LoopModeEnum.Linear;
		Animations.AnimationFinished += OnAnimationFinished;
		ResetAmmunition();
	}

	protected override void OnEquip()
	{
		RifleAccuracy.Reset();
		_triggerHeld = false;
		_secondaryHeld = false;
		_burstRemaining = 0;
		ClearResumeZoom();
		SetZoom(0);
		Play(Clip(_draw), Action.Draw);
	}

	protected override void OnInputDisabled() { _triggerHeld = _secondaryHeld = false; _burstRemaining = 0; }

	protected override void OnHolster()
	{
		_triggerHeld = false;
		_secondaryHeld = false;
		_burstRemaining = 0;
		ClearResumeZoom();
		SetZoom(0);
		_action = Action.Idle; // An interrupted silencer change keeps the committed state.
		Animations.Stop();
	}

	public override void _Process(double delta)
	{
		if (CssoRifleBallistics.Supports(Kind) && Wielder is { } owner)
			RifleAccuracy.Advance(this, owner, delta);
		// Carry the fractional cooldown across held automatic shots. Resetting it
		// to a full cycle on each rendered frame makes SMGs fire too slowly.
		var sustainFire = Equipped && _triggerHeld && Automatic && !Busy && DeployRemaining <= 0;
		_fireRemaining = Math.Max(sustainFire ? -FireInterval : 0, _fireRemaining - delta);
		_zoomRemaining = Math.Max(0, _zoomRemaining - delta);
		AdvanceDeploy(delta);
		if (!Equipped) return;
		if (_burstRemaining > 0)
		{
			_burstDelay -= delta;
			while (_burstRemaining > 0 && _burstDelay <= 0)
			{
				if (MagazineAmmo == 0) { _burstRemaining = 0; break; }
				FireRound(animate: Kind != FirearmKind.Glock); _burstRemaining--; _burstDelay += BurstShotInterval;
			}
		}
		Sounds.AdvanceAnimation(Math.Min(Animations.CurrentAnimationPosition + delta * Animations.SpeedScale, Animations.CurrentAnimationLength));
		if (Wielder?.IsServerBot == true)
		{
			// Bots use the world rig. Keep reload/fire/deploy clocks and sounds,
			// without evaluating a second, invisible first-person skeleton.
			var next = Animations.CurrentAnimationPosition + delta * Animations.SpeedScale;
			if (Math.Abs(next - _animationLength) < .0000001) next = _animationLength;
			Animations.Seek(_animationLoops ? next % _animationLength : Math.Min(next, _animationLength), false);
			if (!_animationLoops && next >= _animationLength) FinishAnimation();
		}
		else Animations.Advance(delta);
		if (_action is Action.ReloadStart or Action.ReloadShell)
		{
			_shellRemaining -= delta;
			if (_shellRemaining <= 0) FinishAnimation(scheduled: true);
		}
		if (_action == Action.Idle && MagazineAmmo == 0 && _fireRemaining == 0) Reload();
		// Source ItemPostFrame repeats IN_ATTACK2 while held. Secondary attack
		// takes priority when ready; releasing the button keeps the current zoom.
		if (HasZoom && _secondaryHeld && DeployRemaining <= 0 && !Busy
			&& _fireRemaining <= 0 && _zoomRemaining <= .000001) Secondary();
		else if (_triggerHeld && Automatic) TryFire();
		// CSS CWeaponCSBaseGun::ItemPostFrame: the scope returns to the
		// magnification of the last scoped shot once the weapon can fire again.
		if (_resumeZoomPending && _fireRemaining <= 0 && !Busy && DeployRemaining <= 0)
		{
			_resumeZoomPending = false;
			// An empty magazine starts the reload, which keeps the scope off.
			if (MagazineAmmo > 0) SetZoom(_resumeZoom);
		}
	}

	private void Play(string clip, Action action)
	{
		_action = action;
		if (action is Action.ReloadStart or Action.ReloadShell) _shellRemaining = .5;
		// Keep the outgoing clip for the return blend. Stopping here discards
		// its pose and makes the first idle frame snap into place.
		if (action != Action.Idle) Animations.Stop(true);
		Animations.Play(clip, customBlend: action == Action.Idle ? ReturnTransitionSeconds : 0);
		var animation = Animations.GetAnimation(clip);
		_animationLength = Math.Max(.001, animation.Length);
		_animationLoops = animation.LoopMode != Animation.LoopModeEnum.None;
		if (Wielder?.IsServerBot == true) Animations.Seek(0, false);
		else Animations.Advance(0);
		Sounds.StartAnimation(Kind.ToString().ToLowerInvariant(), clip, animation.Length);
	}

	private void TryFire()
	{
		if (Wielder != null && Contrapoint.Gameplay.DefuseRules.Active && Contrapoint.Gameplay.DefuseState.Instance?.CanAttack(Wielder) == false) return;
		if (IsShotgun && _action == Action.ReloadShell && MagazineAmmo > 0 && _fireRemaining <= 0)
			_action = Action.Idle; // CSS permits firing during insertion, cancelling only the uncommitted shell.
		if (DeployRemaining > 0 || Busy || _fireRemaining > 0) return;
		if (MagazineAmmo == 0)
		{
			if (ReserveAmmo > 0) Reload();
			else
			{
				_fireRemaining = .2;
				Sounds.PlayScript(IsPistol ? "default.clipempty_pistol" : "default.clipempty_rifle");
			}
			return;
		}
		_fireRemaining = Math.Max(0, FireInterval + Math.Min(0, _fireRemaining));
		FireRound();
		if (BurstMode) { _burstRemaining = Math.Min(2, MagazineAmmo); _burstDelay = BurstShotInterval; }
	}

	private void FireRound(bool animate = true)
	{
		MagazineAmmo--;
		ShotsFired++;
		Wielder?.Combat.QueueBullet(this, EffectiveBulletDamage);
		Wielder?.Recoil.AddShot(Kind, AccuracyAlternate, IsPistol ? GD.RandRange(0, 63) : RifleAccuracy.Index);
		if (CssoRifleBallistics.Supports(Kind)) RifleAccuracy.Fired(this);
		var shot = Kind == FirearmKind.USP && Silenced ? "weapon_usp.silencedshot"
			: Kind == FirearmKind.M4A1 && Silenced ? "weapon_m4a1.silenced"
			: FirearmData.For(Kind).Sound;
		Sounds.PlayScript(shot);
		// CSS: a scoped shot drops the scope and remembers the magnification it
		// was fired from; the scope returns to it once the weapon is ready again.
		if (HasResumeZoom && ZoomLevel > 0)
		{
			_resumeZoom = ZoomLevel;
			_resumeZoomPending = true;
			SetZoom(0);
		}
		if (!animate) return;
		var clip = Clip(_shots[_fireIndex++ % _shots.Length]);
		if (Kind == FirearmKind.Elite)
			clip = "shoot_" + (MagazineAmmo % 2 == 1 ? "left" : "right") + (MagazineAmmo <= 1 ? "last" : "1");
		else if (Kind == FirearmKind.Glock)
			clip = BurstMode ? "glock_fireburst1" : MagazineAmmo == 0 ? "glock_firelast" : "glock_firesingle";
		else if (MagazineAmmo == 0 && Animations.HasAnimation("shoot_empty")) clip = "shoot_empty";
		else if (MagazineAmmo == 0 && Kind == FirearmKind.USP) clip = Clip("shootlast");
		Play(clip, Action.Fire);
	}

	private void Reload()
	{
		if (DeployRemaining > 0 || Busy || _burstRemaining > 0 || MagazineAmmo >= MagazineCapacity || ReserveAmmo == 0 || ((IsSniper || IsShotgun) && _fireRemaining > 0)) return;
		Wielder?.Recoil.ResetBurst();
		RifleAccuracy.Reset();
		// A reload unscopes for good; the pending scope resume does not survive it.
		ClearResumeZoom();
		SetZoom(0);
		Play(Clip(_reload), IsShotgun ? Action.ReloadStart : Action.Reload);
	}

	private void Secondary()
	{
		if (DeployRemaining > 0 || Busy || _fireRemaining > 0) return;
		if (HasSilencer)
			Play(Silenced ? "detach_silencer" : "add_silencer", Action.Silencer);
		else if (HasZoom && _zoomRemaining <= .000001)
		{
			SetZoom((ZoomLevel + 1) % (IsSniper ? 3 : 2));
			if (IsSniper) Sounds.PlayScript("weapon_awp.zoom");
			_zoomRemaining = 0.3;
		}
		else if (Kind is FirearmKind.Glock or FirearmKind.Famas && _zoomRemaining <= .000001 && _burstRemaining == 0)
		{
			BurstMode = !BurstMode;
			_zoomRemaining = .3;
		}
	}

	// CSS CWeaponCSBase::Deploy drops the pending scope resume.
	private void ClearResumeZoom() { _resumeZoom = 0; _resumeZoomPending = false; }

	private void SetZoom(int level)
	{
		if (!HasZoom) return;
		ZoomLevel = level;
		_camera.Fov = level == 0 ? _defaultFov
			: Mathf.RadToDeg(2 * Mathf.Atan(Mathf.Tan(Mathf.DegToRad(SourceZoomFov) / 2) * 3 / 4));
		// CSS hides its viewmodel when scoped; CssScope draws the local HUD overlay.
		Model.Visible = !IsSniper || level == 0;
	}
	public void ApplyNetworkZoom(int level)
	{
		if (Wielder?.LocalInput == false) SetZoom(Math.Clamp(level, 0, IsSniper ? 2 : 1));
	}

	private void OnAnimationFinished(StringName clip)
		=> FinishAnimation();
	private void FinishAnimation(bool scheduled = false)
	{
		if (!Equipped) return;
		if (_action == Action.Fire && MagazineAmmo == 0 && ReserveAmmo == 0)
		{
			_action = Action.Idle;
			Animations.Pause(); // Preserve the authored empty slide/bolt pose.
			return;
		}
		if (_action is Action.ReloadStart or Action.ReloadShell)
		{
			if (!scheduled) return;
			if (_action == Action.ReloadShell) LoadShell();
			if (MagazineAmmo >= MagazineCapacity || ReserveAmmo == 0)
				Play("after_reload", Action.ReloadEnd);
			else Play("insert", Action.ReloadShell);
			return;
		}
		if (_action == Action.Silencer) Silenced = !Silenced;
		if (_action == Action.Reload) CompleteReload();
		Play(Clip(_idle), Action.Idle);
	}

	public override bool HandleCommand(string command, bool down)
	{
		if (!Equipped) return false;
		switch (command)
		{
			case "attack":
				var wasHeld = _triggerHeld;
				_triggerHeld = down;
				if (down && !wasHeld) TryFire();
				return true;
			case "attack2":
				var secondaryWasHeld = _secondaryHeld;
				_secondaryHeld = down;
				if (down && !secondaryWasHeld) Secondary();
				return true;
			case "reload": if (down) Reload(); return true;
			default: return false;
		}
	}

	public override void _Notification(int what)
	{
		if (what == NotificationApplicationFocusOut) _triggerHeld = _secondaryHeld = false;
	}
}
