using Godot;
using Contrapoint.Players;
using Contrapoint.Weapons;

public partial class KnifeView : WeaponView
{
	private enum Action { Idle, Draw, Inspect, Slash, Stab }
	// The source model embeds its inspect animation in the long idle clip.
	private const double InspectStart = 5.1;
	private const double InspectEnd = 9.65;
	private const double IdleStart = 0;
	private const double IdleEnd = 5.0;
	private const double InputBufferSeconds = 0.18;

	[Export(PropertyHint.Range, "0,0.15,0.005")]
	public double AttackTransitionSeconds { get; set; } = 0.045;
	[Export(PropertyHint.Range, "0,0.2,0.005")]
	public double InspectTransitionSeconds { get; set; } = 0.085;
	[Export(PropertyHint.Range, "0,0.2,0.005")]
	public double ReturnTransitionSeconds { get; set; } = 0.055;
	[Export(PropertyHint.Range, "0,0.6,0.01")]
	public double IdleLoopTransitionSeconds { get; set; } = 0.35;

	[ExportGroup("Weapon timing")]
	[Export(PropertyHint.Range, "0,2,0.01")]
	public double DeployReadySeconds { get; set; } = 0.75;
	[Export(PropertyHint.Range, "0.01,2,0.01")]
	public double PrimaryAttackCooldownSeconds { get; set; } = 0.50;
	[Export(PropertyHint.Range, "0.01,2,0.01")]
	public double SecondaryAttackCooldownSeconds { get; set; } = 1.10;

	private static readonly StringName[] Slashes = { "midslash1", "midslash2" };
	private static readonly StringName[] Stabs = { "stab", "stab_miss", "stab_miss2" };
	private AnimationPlayer _animations => Animations;
	protected override double DeploySeconds => DeployReadySeconds;
	private KnifePoseTransition _pose = null!;
	private readonly WeaponActionTimers _weapon = new();
	private Action _action;
	private Action? _buffered;
	private double _bufferExpires;
	private double _elapsed;
	private double _attackTime;
	private double _duration;
	private int _slashIndex;
	private int _stabIndex;
	private int _drawIndex, _inspectIndex;
	public int ViewStyle { get; private set; }
	internal string NetworkClipName
	{
		get
		{
			using var clip = Animations.AssignedAnimation;
			if (ViewStyle == 0) return clip.ToString();
			return _action switch
			{
				Action.Draw => "draw", Action.Slash => (_slashIndex - 1) % 2 == 0 ? "midslash1" : "midslash2",
				Action.Stab => "stab", _ => "idle"
			};
		}
	}
	private bool _primaryHeld, _secondaryHeld;
	private Action? HeldAttack => _secondaryHeld ? Action.Stab : _primaryHeld ? Action.Slash : null;

	public override void _Ready()
	{
		base._Ready();
		InitializeVisual();
	}

	public void SetViewStyle(int style)
	{
		if (style is < 0 or > 1) throw new System.ArgumentOutOfRangeException(nameof(style));
		if (ViewStyle == style) return;
		ViewStyle = style;
		ReplaceViewModel(PlayerSceneCache.Load(style == 1 ? "res://assets/weapons/butterfly/butterfly.glb" : "res://assets/weapons/karambit/karambit.glb"));
		InitializeVisual();
		// A cosmetic change preserves attack/deploy deadlines and the inventory entity.
		_buffered = null;
		_slashIndex = _stabIndex = _drawIndex = _inspectIndex = 0;
		if (Equipped) PlayVisual(Action.Draw, initial: true);
	}

	private void InitializeVisual()
	{
		var model = Model;
		var skeleton = model.FindChild("Skeleton3D", true, false) as Skeleton3D
			?? throw new System.InvalidOperationException("Knife skeleton is missing.");
		_pose = new KnifePoseTransition(skeleton);
		_animations.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
		foreach (MeshInstance3D mesh in model.FindChildren("*", "MeshInstance3D", true, false))
			for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
			{
				if (mesh.GetActiveMaterial(surface) is not StandardMaterial3D material) continue;
				if (ViewStyle == 1 && material.ResourceName == "knife_butterfly")
				{
					mesh.SetSurfaceOverrideMaterial(surface, GD.Load<ShaderMaterial>("res://assets/weapons/butterfly/butterfly_steel.tres"));
					continue;
				}
				var copy = (StandardMaterial3D)material.Duplicate();
				if (copy.ResourceName == "v_hands")
				{ copy.Metallic = 0; copy.Roughness = .9f; copy.MetallicSpecular = .1f; }
				else if (ViewStyle == 1 && copy.ResourceName.StartsWith("bare_arm_", System.StringComparison.Ordinal))
				{ copy.Metallic = 0; copy.RoughnessTexture = null; copy.Roughness = .75f; copy.MetallicSpecular = .25f; }
				else if (ViewStyle == 1 && copy.ResourceName == "glove_fullfinger")
				{ copy.Metallic = 0; copy.RoughnessTexture = null; copy.Roughness = .7f; copy.MetallicSpecular = .35f; }
				mesh.SetSurfaceOverrideMaterial(surface, copy);
			}
	}

	protected override void OnEquip()
	{
		_primaryHeld = _secondaryHeld = false;
		if (ViewStyle == 0) Sounds.PlayScript("weapon_knife.deploy");
		_buffered = null;
		_weapon.TryDeploy(DeployReadySeconds);
		PlayVisual(Action.Draw, initial: true);
	}

	protected override void OnInputDisabled()
	{
		_primaryHeld = _secondaryHeld = false;
		_buffered = null;
	}

	public override void _Process(double delta)
	{
		if (!Equipped)
		{
			AdvanceDeploy(delta);
			_weapon.Advance(delta);
			return;
		}
		// Weapon cooldown boundaries and visual clip ends are independent clocks.
		for (var boundary = 0; delta > 0.000001 && boundary < 16; boundary++)
		{
			var step = System.Math.Min(delta, _duration - _elapsed);
			var requested = HeldAttack ?? _buffered;
			if (requested.HasValue)
				step = System.Math.Min(step, System.Math.Max(DeployRemaining, _weapon.AttackRemaining(requested == Action.Stab)));
			AdvanceDeploy(step);
			_weapon.Advance(step);
			_pose.RestoreAnimationPose();
			_animations.Advance(step);
			_pose.Update(step);
			_elapsed += step;
			if (ViewStyle == 1) Sounds.AdvanceAnimation(_elapsed);
			delta -= step;
			if (_buffered.HasValue && _weapon.Time > _bufferExpires + 0.000001)
				_buffered = null;
			requested = HeldAttack ?? _buffered;
			if (requested.HasValue && TryAttack(requested.Value))
			{
				continue;
			}
			if (_elapsed >= _duration - 0.000001)
				PlayVisual(Action.Idle);
		}
	}

	public override bool HandleCommand(string command, bool down)
	{
		if (!Equipped) return false;
		if (command == "lookatweapon") { if (down) Request(Action.Inspect); return true; }
		if (command == "attack") _primaryHeld = down;
		else if (command == "attack2") _secondaryHeld = down;
		else return false;
		if (down && HeldAttack is { } attack) Request(attack);
		return true;
	}

	private void Request(Action action)
	{
		if (action == Action.Inspect && ViewStyle == 1)
		{
			// F is a fresh cosmetic request, including during deploy and another
			// inspect. Never modify deploy/attack deadlines or a queued contact.
			if (HeldAttack.HasValue || (_action is Action.Slash or Action.Stab) && _weapon.AttackRemaining(_action == Action.Stab) > 0)
				return;
			PlayVisual(Action.Inspect);
			return;
		}
		// Ignore duplicate cosmetic requests; weapon attack timers remain independent.
		if (action == Action.Inspect && _action == Action.Inspect)
			return;
		if (action == Action.Slash || action == Action.Stab)
		{
			if (!TryAttack(action) && System.Math.Max(DeployRemaining, _weapon.AttackRemaining(action == Action.Stab)) <= InputBufferSeconds)
			{
				_buffered = action;
				_bufferExpires = _weapon.Time + InputBufferSeconds;
			}
		}
		else if (action == Action.Draw && _weapon.TryDeploy(DeployReadySeconds))
			PlayVisual(Action.Draw);
		else if (action == Action.Inspect && DeployRemaining <= 0 && _weapon.TryInspect(0.25))
			PlayVisual(Action.Inspect);
	}

	private bool TryAttack(Action action)
	{
		if (Wielder != null && Contrapoint.Gameplay.DefuseRules.Active && Contrapoint.Gameplay.DefuseState.Instance?.CanAttack(Wielder) == false) return false;
		var cooldown = action == Action.Stab ? SecondaryAttackCooldownSeconds : PrimaryAttackCooldownSeconds;
		if (DeployRemaining > 0 || !_weapon.TryAttack(action == Action.Stab, new AttackCooldowns(cooldown, cooldown)))
			return false;
		_buffered = null;
		_attackTime = _weapon.Time;
		Wielder?.Combat.QueueKnife(this, action == Action.Stab);
		PlayVisual(action);
		return true;
	}

	public void ResolveContact(bool hit, bool stab)
	{
		var cooldown = stab ? SecondaryAttackCooldownSeconds : PrimaryAttackCooldownSeconds;
		if (!hit) cooldown = System.Math.Max(0, cooldown - .1);
		_weapon.ResolveContact(_attackTime, new(cooldown, !stab && !hit ? .5 : cooldown));
		if (!hit) Sounds.PlayScript("weapon_knife.slash");
	}

	private void PlayVisual(Action action, bool initial = false)
	{
		var previous = _action;
		_action = action;
		_elapsed = 0;
		double start = 0, end = -1;
		double transition;
		StringName clip;
		switch (action)
		{
			case Action.Draw:
				clip = ViewStyle == 1 && _drawIndex++ % 2 != 0 ? "draw2" : "draw"; transition = 0.075;
				break;
			case Action.Inspect:
				if (ViewStyle == 1) clip = "lookat0" + (1 + _inspectIndex++ % 3);
				else { clip = "idle"; start = InspectStart; end = InspectEnd; }
				transition = InspectTransitionSeconds;
				if (ViewStyle == 1 && previous is Action.Inspect or Action.Draw) transition = .035;
				break;
			case Action.Slash:
				var slash = _slashIndex++ % Slashes.Length;
				clip = ViewStyle == 1 ? slash == 0 ? "light_miss1" : "light_miss2" : Slashes[slash];
				transition = AttackTransitionSeconds;
				break;
			case Action.Stab:
				var stab = _stabIndex++ % Stabs.Length;
				clip = ViewStyle == 1 ? "heavy_miss1" : Stabs[stab];
				transition = AttackTransitionSeconds;
				break;
			default:
				if (ViewStyle == 1) clip = "idle1";
				else { clip = "idle"; start = IdleStart; end = IdleEnd; }
				// The authored idle section has different endpoint poses. Give its
				// seam time to settle without slowing returns from attacks/inspect.
				transition = previous == Action.Idle ? IdleLoopTransitionSeconds : ReturnTransitionSeconds;
				break;
		}
		if (previous == Action.Inspect && (action == Action.Slash || action == Action.Stab))
			transition = System.Math.Max(transition, 0.07);
		_duration = (end >= 0 ? end : _animations.GetAnimation(clip).Length) - start;
		if (ViewStyle == 1) Sounds.StartAnimation("butterfly", clip, _animations.GetAnimation(clip).Length);
		else Sounds.CancelAnimation();
		_pose.CaptureOutgoingMotion();
		_pose.RestoreAnimationPose();
		_animations.Stop(keepState: true);
		_animations.PlaySection(clip, start, end, 0);
		// Idle and inspect share a source clip; explicitly seek when changing
		// sections before sampling the incoming pose for the transition.
		_animations.Seek(start, update: true);
		_pose.CaptureIncomingPose();
		// Measure incoming velocity without consuming the visible animation.
		const double sampleTime = 1.0 / 240;
		_animations.Advance(sampleTime);
		_pose.Start(initial ? 0 : transition, sampleTime);
		_animations.Seek(start, update: true);
		_pose.Update(0);
	}

	protected override void OnHolster()
	{
		_primaryHeld = _secondaryHeld = false;
		_buffered = null;
	}

	public override void _Notification(int what)
	{
		if (what == NotificationApplicationFocusOut) OnHolster();
	}
}
