using Godot;
using System.Linq;
using Contrapoint.Weapons;
using Contrapoint.UI;
using Contrapoint.Network;
using Contrapoint.Gameplay;

namespace Contrapoint.Players;

public enum PlayerTeam { Unassigned, Terrorist, CounterTerrorist, Spectator }

public partial class Player : CharacterBody3D
{
	[Signal] public delegate void HealthChangedEventHandler(int health);
	[Signal] public delegate void DiedEventHandler();
	[Signal] public delegate void RespawnedEventHandler();
	private PlayerTeam _team = PlayerTeam.Terrorist;
	[Export] public PlayerTeam Team
	{
		get => _team;
		set
		{
			_team = value;
			if (Body == null) return;
			if (value is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist && PlayerBody.AgentTeam(AgentId) != value)
				SetAgent(PlayerBody.AgentsForTeam(value)[0]);
			Body.Visible = IsAlive && value is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist;
		}
	}
	[Export] public int MaxHealth { get; set; } = 100;
	[Export] public bool LocalInput { get; set; } = true;
	public bool IsBot { get; set; }
	public bool IsServerBot => IsBot && LanSession.Instance?.IsClient != true;
	public string BotDisplayName { get; set; } = "";
	public string DisplayName
	{
		get
		{
			if (IsBot || !LocalInput && BotDisplayName.Length > 0) return BotDisplayName;
			using var name = Name;
			return name.ToString();
		}
	}
	public bool HasSimulationControl => (HasLocalControl || IsServerBot && IsAlive) && (!DefuseRules.Active || DefuseState.Instance?.CanAttack(this) != false);
	[Export] public bool DebugControls { get; set; } = true;
	[Export] public float MouseSensitivity { get; set; } = Mathf.DegToRad(0.066f);
	public float ZoomSensitivityRatio { get; set; } = 1;
	// Source view.cpp scales mouse counts by (zoom FOV / default FOV) * ratio.
	public float MouseZoomScale => ActiveWeapon is FirearmView { ZoomLevel: > 0 } gun
		? gun.SourceZoomFov / 90f * ZoomSensitivityRatio : 1;
	[Export] public NodePath SpawnPointPath { get; set; } = new("");
	[Export] public NodePath SpawnPointsRootPath { get; set; } = new("");
	public int Health { get; private set; }
	[Export] public int Armor { get; set; }
	[Export] public bool HasHelmet { get; set; }
	[Export] public int Money { get; set; } = 16000;
	public bool IsAlive => Health > 0;
	public bool MouseCaptured { get; private set; }
	public bool HasLocalControl => LocalInput && (IsAlive || Team == PlayerTeam.Spectator) && MouseCaptured && Observer?.Following != true;
	public Camera3D Camera { get; private set; } = null!;
	public PlayerInventory Inventory { get; private set; } = null!;
	public SourceMovement Movement { get; private set; } = null!;
	public SourceViewmodelBob ViewmodelBob { get; private set; } = null!;
	public PlayerMovementAudio MovementAudio { get; private set; } = null!;
	public WeaponCombat Combat { get; private set; } = null!;
	public SourceRecoil Recoil { get; private set; } = null!;
	public GrenadeVision GrenadeVision { get; private set; } = null!;
	public SourceDamageHearing DamageHearing { get; private set; } = null!;
	public DeveloperConsole? Console { get; private set; }
	public MainMenu? Menu { get; private set; }
	public ConsoleBindings Bindings { get; private set; } = null!;
	public ConsoleStats Stats { get; private set; } = null!;
	public float MouseYaw { get; set; } = .022f;
	public float MousePitch { get; set; } = .022f;
	public bool MouseFilter { get; set; }
	public bool RawMouseInput { get; set; } = true;
	public bool MouseLookEnabled { get; set; } = true;
	public float KeyboardYawSpeed { get; set; } = 210;
	public float KeyboardPitchSpeed { get; set; } = 225;
	public float KeyboardAngleSpeedKey { get; set; } = .67f;
	private Vector2 _previousMouse;
	public float AimPitch => _pitch;
	public WeaponView? ActiveWeapon => Inventory.ActiveWeapon;
	public float MaxMoveSpeed => ActiveWeapon?.MovementSpeed ?? 250;
	private Transform3D _spawn;
	private Marker3D[] _terroristSpawns = [];
	private Marker3D[] _counterTerroristSpawns = [];
	private float _pitch;
	private bool _wheelJumpQueued;

	public override void _Ready()
	{
		Bindings = new ConsoleBindings(this);
		Camera = GetNode<Camera3D>("View/Camera3D");
		Inventory = GetNode<PlayerInventory>("View/Camera3D/Weapons");
		Movement = GetNode<SourceMovement>("Movement");
		Movement.Landed += OnLanded;
		ViewmodelBob = GetNode<SourceViewmodelBob>("ViewmodelBob");
		MovementAudio = GetNode<PlayerMovementAudio>("MovementAudio");
		Combat = GetNode<WeaponCombat>("Combat");
		Recoil = GetNode<SourceRecoil>("Recoil");
		GrenadeVision = new GrenadeVision { Name = "GrenadeVision" };
		AddChild(GrenadeVision);
		DamageHearing = new SourceDamageHearing { Name = "DamageHearing" };
		AddChild(DamageHearing);
		AddToGroup("source_players");
		Inventory.Bind(this);
		if (!SpawnPointsRootPath.IsEmpty) Inventory.ConfigureSourceLoadout();
		Inventory.WeaponChanged += _ => Bindings.SyncWeapon();
		Camera.Current = LocalInput;
		GetNode<Node3D>("View").Visible = LocalInput;
		GetNode<CanvasLayer>("WeaponLayer").Visible = LocalInput;
		if (!LocalInput)
		{
			// Non-local players have gameplay weapons and a world rig, but no
			// personal HUD, viewmodel camera or first-person movement effects.
			var hud = GetNode<CanvasLayer>("HUD");
			hud.Visible = false; hud.ProcessMode = ProcessModeEnum.Disabled;
			GetNode<Control>("HUD/Status").Visible = false;
			GetNode<CanvasLayer>("WeaponLayer").ProcessMode = ProcessModeEnum.Disabled;
			ViewmodelBob.SetProcess(false);
		}
		GetNode<SubViewport>("WeaponLayer/Container/Viewport").RenderTargetUpdateMode =
			LocalInput ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
		_spawn = !SpawnPointPath.IsEmpty && GetNodeOrNull<Node3D>(SpawnPointPath) is { } spawn
			? new(spawn.GlobalBasis, spawn.GlobalPosition + Vector3.Up * 0.04f) : GlobalTransform;
		if (!SpawnPointsRootPath.IsEmpty && GetNodeOrNull<Node3D>(SpawnPointsRootPath) is { } spawnRoot)
		{
			_terroristSpawns = spawnRoot.FindChildren("Spawn_terrorist_*", "Marker3D", true, false)
				.OfType<Marker3D>().ToArray();
			_counterTerroristSpawns = spawnRoot.FindChildren("Spawn_counterterrorist_*", "Marker3D", true, false)
				.OfType<Marker3D>().ToArray();
			_deathmatchSpawns = spawnRoot.FindChildren("Spawn_deathmatch_*", "Marker3D", true, false)
				.OfType<Marker3D>().ToArray();
		}
		foreach (var child in GetNode("View").FindChildren("*", "MeshInstance3D", true, false))
			((MeshInstance3D)child).Layers = 2;
		if (LocalInput)
		{
			GetViewport().Msaa3D = (Viewport.Msaa)(int)GamePreferences.Value("cp_antialias", 2);
			SetMouseCaptured(true);
		}
		if (Team == PlayerTeam.Unassigned || LocalInput && !SpawnPointsRootPath.IsEmpty)
		{
			// The Source intro view is an observer, not a living T spawn.
			if (_terroristSpawns.Length > 0)
				GlobalTransform = new(_terroristSpawns[0].GlobalBasis, _terroristSpawns[0].GlobalPosition + Vector3.Up * .04f);
			BeginInitialTeamSelection();
		}
		else Respawn();
		InitializePresentation();
		if (LocalInput)
		{
			Stats = new ConsoleStats { Name = "ConsoleStats" };
			AddChild(Stats);
			Console = new DeveloperConsole { Name = "DeveloperConsole" };
			AddChild(Console);
			Menu = new MainMenu { Name = "MainMenu" };
			AddChild(Menu);
			ConsoleCommands.Initialize(this);
			Contrapoint.Gameplay.MatchSession.ApplyPendingRules();
			// Older saved configs predate the team menu and contain unbindall.
			ConsoleBindings.Keys.TryAdd("M", "chooseteam");
			ConsoleBindings.Keys.TryAdd("TAB", "+showscores");
			ConsoleBindings.Keys.TryAdd("G", "drop");
			ConsoleBindings.Keys.TryAdd("B", "buymenu");
			ConsoleBindings.Keys.TryAdd("F1", "autobuy");
			ConsoleBindings.Keys.TryAdd("F2", "rebuy");
			ConsoleBindings.Keys.TryAdd("LEFT", "+left");
			ConsoleBindings.Keys.TryAdd("RIGHT", "+right");
			ConsoleBindings.Keys.TryAdd("UP", "+lookup");
			ConsoleBindings.Keys.TryAdd("DOWN", "+lookdown");
			CallDeferred(MethodName.AttachNetwork);
		}
	}

	private void AttachNetwork()
	{
		LanSession.Instance?.Attach(this);
		DefuseState.Instance?.InitializePlayer(this);
		Contrapoint.Gameplay.Bots.BotManager.Attach(this);
		if (!SpawnPointsRootPath.IsEmpty && AwaitingInitialClass && LanSession.Instance?.IsClient != true) TeamMenu?.OpenInitial();
	}

	public void SetSpawn(Transform3D transform)
	{
		_terroristSpawns = [];
		_counterTerroristSpawns = [];
		_deathmatchSpawns = [];
		_lastDeathmatchSpawn = null;
		_spawn = transform;
	}

	public void SourceTeleport(Vector3 position, float yaw, float pitch)
	{
		if (LanSession.Instance?.ForwardTeleport(this, position, yaw, pitch) == true) return;
		GlobalPosition = position;
		Rotation = new(0, yaw, 0);
		_pitch = Mathf.Clamp(pitch, Mathf.DegToRad(-89), Mathf.DegToRad(89));
		Camera.Rotation = new(_pitch, 0, 0);
		Velocity = Vector3.Zero;
		Movement.Reset();
		ViewmodelBob.Reset();
		ResetNetworkInterpolation();
		ResetPhysicsInterpolation();
		FirstPerson?.ResetHistory();
		ThirdPerson?.ResetHistory();
	}

	public void Respawn(bool restoreLoadout = true)
	{
		if (AwaitingInitialClass || Team == PlayerTeam.Unassigned) return;
		if (Team == PlayerTeam.Spectator && Body != null) return;
		if (LanSession.Instance?.RequestRespawn(this) == true) return;
		if (DefuseRules.Active && LanSession.Instance?.IsClient != true && DefuseState.Instance?.CanSpawn(this) == false) return;
		MovementIntent = default;
		_wheelJumpQueued = false;
		var spawns = Team == PlayerTeam.CounterTerrorist ? _counterTerroristSpawns : _terroristSpawns;
		if (DeathmatchRules.Active && DeathmatchRules.FreeForAll) spawns = _terroristSpawns.Concat(_counterTerroristSpawns).ToArray();
		if (DeathmatchRules.Active && spawns.Length == 0) spawns = _terroristSpawns.Concat(_counterTerroristSpawns).ToArray();
		var deathmatchSpawn = DeathmatchRules.Active ? ChooseMapDeathmatchSpawn() : null;
		if (deathmatchSpawn != null)
			_spawn = new(deathmatchSpawn.GlobalBasis, deathmatchSpawn.GlobalPosition + Vector3.Up * .04f);
		else if (spawns.Length > 0)
		{
			var available = DefuseRules.Active ? spawns.Where(s => !GetTree().GetNodesInGroup("source_players").OfType<Player>()
				.Any(p => p != this && p.IsAlive && p.GlobalPosition.DistanceTo(s.GlobalPosition) < 40 * SourceMovement.Unit)).ToArray() : spawns;
			if (available.Length == 0) available = spawns;
			var spawn = DeathmatchRules.Active ? ChooseDeathmatchSpawn(spawns) : available[GD.RandRange(0, available.Length - 1)];
			_spawn = new(spawn.GlobalBasis, spawn.GlobalPosition + Vector3.Up * 0.04f);
		}
		GlobalTransform = DefuseRules.Active ? GroundRoundSpawn(_spawn) : _spawn;
		_pitch = 0;
		Camera.Rotation = Vector3.Zero;
		Movement.Reset();
		if (DefuseRules.Active)
		{
			GetNode<CollisionShape3D>("CollisionShape3D").GlobalBasis = Basis.Identity;
			ApplyFloorSnap();
		}
		ViewmodelBob.Reset();
		MovementAudio.Reset();
		Combat.Reset();
		Recoil.Reset();
		GrenadeVision.Reset();
		DamageHearing.Reset();
		ResetNetworkInterpolation();
		ResetPhysicsInterpolation();
		FirstPerson?.ResetHistory();
		ThirdPerson?.ResetHistory();
		if (restoreLoadout)
		{
			if (!SpawnPointsRootPath.IsEmpty) Inventory.ConfigureBuyLoadout();
			Inventory.RestoreSpawnLoadout();
		}
		Health = Mathf.Max(1, MaxHealth);
		Death?.Reset();
		LastDamageKind = PlayerDamageKind.Administrative;
		Armor = 0;
		HasHelmet = false;
		if (DeathmatchRules.Active) { Armor = DeathmatchRules.SpawnArmor; HasHelmet = DeathmatchRules.SpawnHelmet && Armor > 0; }
		_spawnProtection = DeathmatchRules.Active ? DeathmatchRules.ProtectionSeconds : 0;
		if (!DefuseRules.Active || restoreLoadout) { Inventory.ResetAmmunition(); Inventory.ResetGrenades(); }
		if (DefuseRules.Active) { ObjectiveUseHeld = ObjectiveAttackHeld = false; DefuseState.Instance?.DidSpawn(this); }
		var playing = Team is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist;
		Inventory.SetEnabled((!LocalInput || MouseCaptured) && playing, keepVisible: LocalInput && playing);
		if (Body != null) Body.Visible = playing;
		EmitSignal(SignalName.HealthChanged, Health);
		EmitSignal(SignalName.Respawned);
		LanSession.Instance?.BroadcastVitals(this);
		LanSession.Instance?.BroadcastRespawn(this);
	}

	// Gameplay input state is explicit; a headless server has no OS cursor to capture.
	public void SetMouseCaptured(bool captured)
	{
		if (!LocalInput) return;
		if (captured && Console?.IsOpen == true) return;
		if (captured && Menu?.IsOpen == true) return;
		if (captured && TeamMenu?.IsOpen == true) return;
		if (captured && BuyMenu?.IsOpen == true) return;
		if (!captured) { Bindings?.ReleaseAll(); _previousMouse = Vector2.Zero; }
		MouseCaptured = captured;
		_wheelJumpQueued = false;
		Input.MouseMode = captured ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
		Inventory.SetEnabled(captured && IsAlive && Team is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist,
			keepVisible: IsAlive && Team is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist);
	}

	// Administrative damage (kill/debug); weapons use TakeTraceDamage.
	public void TakeDamage(int amount)
	{
		if (LanSession.Instance?.RequestKill(this, amount) == true) return;
		ApplyDamage(amount, PlayerDamageKind.Administrative);
	}

	private void Die()
	{
		_rebuy = CaptureBuyPackage();
		BuyMenu?.Close();
		if (!DeathmatchRules.Active && !DefuseRules.Active && LanSession.Instance?.IsClient != true) Money = 16000;
		DefuseState.Instance?.PlayerDied(this);
		Death?.Begin();
		if (ActiveWeapon is GrenadeView grenade) grenade.OwnerDied();
		DropDeathWeapons();
		Inventory.SetEnabled(false);
		_wheelJumpQueued = false;
		Bindings?.ReleaseAll();
		Velocity = Vector3.Zero;
		EmitSignal(SignalName.Died);
		LanSession.Instance?.BroadcastVitals(this);
	}

	public override void _PhysicsProcess(double delta)
	{
		TickSpawnProtection(delta);
		if (!LocalInput) return;
		TickObjectiveInput(delta);
		TickKeyboardLook((float)delta);
		var command = HasLocalControl
			? new PlayerCommand(Input.GetVector("move_left", "move_right", "move_forward", "move_back"),
				Input.IsActionPressed("jump") || _wheelJumpQueued, Input.IsActionPressed("duck"), Input.IsActionPressed("walk"))
				{ JumpPressed = _wheelJumpQueued }
			: default;
		_wheelJumpQueued = false;
		Simulate(command, (float)delta);
	}

	// Explicit simulation entry point for a future bot/server controller.
	public void Simulate(PlayerCommand command, float delta)
	{
		if (Team == PlayerTeam.Unassigned) { MovementIntent = default; Velocity = Vector3.Zero; return; }
		if (Team == PlayerTeam.Spectator)
		{
			if (Observer?.Following == true) { Velocity = Vector3.Zero; return; }
			if (!Movement.Noclip) Movement.ToggleNoclip();
			Movement.Simulate(command, MaxMoveSpeed, delta);
			return;
		}
		MovementIntent = IsAlive ? command : default;
		if (!IsAlive) return;
		if (DefuseRules.Active && DefuseState.Instance?.MovementLocked(this) == true)
		{
			MovementIntent = default;
			// Keep floor contact/gravity current while horizontal movement is frozen.
			Velocity = new Vector3(0, Mathf.Min(0, Velocity.Y), 0);
			Movement.Simulate(default, 0, delta); return;
		}
		Movement.Simulate(IsAlive ? command : default, MaxMoveSpeed, delta);
		MovementAudio.Tick(delta, IsAlive ? command : default);
		if (DebugControls && GlobalPosition.Y < -80) { if (DefuseRules.Active) TakeDamage(Health); else Respawn(); }
	}

	public override void _Input(InputEvent @event)
	{
		if (!LocalInput) return;
		if (Menu?.IsOpen == true) return;
		if (Console?.HandleInput(@event) == true) return;
		if (TeamMenu?.HandleInput(@event) == true) return;
		if (BuyMenu?.HandleInput(@event) == true) return;
		if (Observer?.Available == true && @event.IsPressed() && @event is not InputEventKey { Echo: true })
		{
			var observerKey = ConsoleBindings.EventKey(@event);
			if (observerKey is "MOUSE1" or "MOUSE2" or "SPACE")
			{
				if (!MouseCaptured && observerKey is "MOUSE1" or "MOUSE2") SetMouseCaptured(true);
				else if (observerKey == "SPACE") Observer.ToggleMode();
				else Observer.Cycle(observerKey == "MOUSE2" ? -1 : 1);
				GetViewport().SetInputAsHandled(); return;
			}
		}
		if (!IsAlive && Team != PlayerTeam.Spectator)
		{
			if (@event is InputEventKey { Pressed: true, Echo: false } deathKey && deathKey.PhysicalKeycode is Key.Space or Key.Enter
				|| @event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
			{
				if (Death.RequestRespawn()) GetViewport().SetInputAsHandled();
				return;
			}
			if (@event is InputEventKey { Pressed: true, Echo: false } home && home.PhysicalKeycode == Key.Home)
			{ if (Death.RequestRespawn()) GetViewport().SetInputAsHandled(); return; }
			Bindings.Handle(@event); return;
		}
		if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }
			&& !MouseCaptured)
		{
			SetMouseCaptured(true);
			GetViewport().SetInputAsHandled();
			return;
		}
		if (HasLocalControl && MouseLookEnabled && @event is InputEventMouseMotion motion)
		{
			var raw = RawMouseInput ? motion.ScreenRelative : motion.Relative;
			var mouse = MouseFilter ? (raw + _previousMouse) * .5f : raw;
			_previousMouse = raw;
			var sensitivity = MouseSensitivity * MouseZoomScale;
			RotateY(-mouse.X * sensitivity * MouseYaw / .022f);
			_pitch = Mathf.Clamp(_pitch - mouse.Y * sensitivity * MousePitch / .022f, Mathf.DegToRad(-89), Mathf.DegToRad(89));
			Recoil.ApplyView();
		}
		if (Bindings.Handle(@event)) GetViewport().SetInputAsHandled();
	}

	public void QueueJump() => _wheelJumpQueued = true;

	private void TickKeyboardLook(float delta)
	{
		if (!HasLocalControl || delta <= 0) return;
		var yaw = Bindings.SampleLookButton("left") - Bindings.SampleLookButton("right");
		var pitch = Bindings.SampleLookButton("lookup") - Bindings.SampleLookButton("lookdown");
		if (yaw == 0 && pitch == 0) return;
		var speed = delta * (Input.IsActionPressed("walk") ? KeyboardAngleSpeedKey : 1);
		RotateY(Mathf.DegToRad(KeyboardYawSpeed * speed * yaw));
		_pitch = Mathf.Clamp(_pitch + Mathf.DegToRad(KeyboardPitchSpeed * speed * pitch), Mathf.DegToRad(-89), Mathf.DegToRad(89));
		Recoil.ApplyView();
	}

	public void SetBotAim(float yaw, float pitch)
	{
		if (!IsBot) return;
		Rotation = new(0, yaw, 0);
		_pitch = Mathf.Clamp(pitch, Mathf.DegToRad(-89), Mathf.DegToRad(89));
		Recoil.ApplyView();
	}

	public void Use()
	{
		if (!HasLocalControl) return;
		if (LanSession.Instance?.RequestUse() == true) return;
		UseWorld();
	}

	public void UseWorld()
	{
		var ray = PhysicsRayQueryParameters3D.Create(Camera.GlobalPosition,
			Camera.GlobalPosition - Camera.GlobalBasis.Z * (80 * SourceMovement.Unit), 1);
		ray.Exclude = new() { GetRid() };
		var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
		if (hit.TryGetValue("collider", out var objectHit) && objectHit.AsGodotObject() is Node target && target.HasMethod("source_use"))
			target.Call("source_use", this);
	}

	public override void _Notification(int what)
	{
		if (what != NotificationApplicationFocusOut || !LocalInput || Inventory == null) return;
		SetMouseCaptured(false);
	}

	public override void _ExitTree()
	{
		LanSession.Instance?.Detach(this);
		if (LocalInput)
		{
			ConsoleCommands.Save(this);
			Bindings?.ReleaseAll();
			Input.MouseMode = Input.MouseModeEnum.Visible;
		}
	}
}
