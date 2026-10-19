using Godot;
using System;
using System.Threading.Tasks;
using Contrapoint.Players;
using Contrapoint.Weapons;

// Run player_checks.tscn with the Mono Godot executable; exits nonzero on failure.
public partial class PlayerChecks : Node3D
{
	private Player _player = null!;
	private int _checks;
	private int _failures;
	private float Dt => 1f / Engine.PhysicsTicksPerSecond;
	private static PlayerCommand Forward => new(new(0, -1), false, false, false);

	public override async void _Ready()
	{
		try { await Run(); }
		catch (Exception error) { _failures++; GD.PushError(error.ToString()); }
		await Task.Delay(100); // Drain stopped audio playbacks on the audio thread.
		GD.Print($"PLAYER_CHECKS checks={_checks} failures={_failures}");
		GetTree().Quit(_failures == 0 ? 0 : 1);
	}

	private void Check(bool condition, string message)
	{
		_checks++;
		if (condition) return;
		_failures++;
		GD.PushError(message);
	}

	private StaticBody3D Box(Vector3 position, Vector3 size)
	{
		var body = new StaticBody3D { Position = position };
		body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
		AddChild(body);
		return body;
	}

	private async Task Tick(PlayerCommand command = default, int count = 1)
	{
		for (var i = 0; i < count; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			_player.Simulate(command, Dt);
		}
	}

	private async Task Reset(Vector3? at = null)
	{
		_player.SetSpawn(new(Basis.Identity, at ?? new Vector3(0, 0.003f, 0)));
		_player.Respawn();
		await Tick(count: 5);
	}

	private async Task Run()
	{
		var floor = Box(new(0, -0.5f, 0), new(200, 1, 200));
		_player = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		_player.LocalInput = false;
		_player.DebugControls = false;
		AddChild(_player);
		await Reset();
		Check(_player.IsOnFloor(), "Player must settle on a flat floor.");
		var movementHull = (BoxShape3D)_player.GetNode<CollisionShape3D>("CollisionShape3D").Shape;
		Check(Mathf.Abs(movementHull.Size.Y / SourceMovement.Unit - 62) < .001f,
			"CSS standing movement hull must be 62 units, independent of the model and eye height.");
		Check(!_player.GetNode<Control>("HUD/Crosshair").Visible, "Remote players must not display a crosshair.");
		Check(_player.ActiveWeapon!.Sounds.SoundsPlayed == 0, "A remote player's initial deploy must not play local audio.");
		var loadoutCount = _player.Inventory.Weapons.Count;
		Check(loadoutCount == 10, "Shared loadout must contain seven weapons and three grenades.");
		Check(_player.ActiveWeapon?.Slot == WeaponSlot.Melee, "Player must initially equip the knife.");
		Check(_player.Inventory.Weapons.TrueForAllOwned(_player), "Inventory must assign the owning player to every weapon.");
		Check(ConsoleBindings.Keys.TryGetValue("CTRL", out var duckBind) && duckBind == "+duck", "Duck must be bound to Ctrl in the command catalog.");
		Check(ConsoleBindings.Keys.TryGetValue("SHIFT", out var walkBind) && walkBind == "+speed", "Walk must be bound to Shift in the command catalog.");

		await Tick(Forward);
		Check(_player.Movement.HorizontalSpeed > 0 && _player.Movement.HorizontalSpeed < 30, "Ground movement must accelerate rather than instantly snap to max speed.");
		await Tick(Forward, 100);
		Check(Mathf.Abs(_player.Movement.HorizontalSpeed - 250) < 0.1f, "Knife running speed must reach 250 u/s.");
		var runVelocity = _player.Velocity;
		await Reset();
		await Tick(new(new(1, -1), false, false, false), 100);
		Check(Mathf.Abs(_player.Movement.HorizontalSpeed - 250) < 0.1f, "Diagonal input must not exceed the straight-line input speed.");
		await Reset();
		await Tick(Forward with { Walk = true }, 100);
		Check(Mathf.Abs(_player.Movement.HorizontalSpeed - 130) < 0.1f, "Shift must walk at 52 percent speed.");
		await Reset();
		await Tick(Forward with { Duck = true }, 100);
		Check(Mathf.Abs(_player.Movement.HorizontalSpeed - 85) < 0.1f, "Crouch must use 34 percent speed.");
		Check(_player.Movement.IsDucked, "Crouch must change player stance.");
		Check(Mathf.Abs(((BoxShape3D)_player.GetNode<CollisionShape3D>("CollisionShape3D").Shape).Size.Y - SourceMovement.DuckHeight) < 0.001f, "Crouch must shrink the collision hull.");
		Check(Mathf.Abs(movementHull.Size.Y / SourceMovement.Unit - 45) < .001f
			&& Mathf.Abs(_player.GetNode<Node3D>("View").Position.Y / SourceMovement.Unit - 47) < .001f,
			"CSS crouch must use a 45-unit hull and a separate 47-unit eye height.");

		await Reset();
		_player.Velocity = runVelocity;
		var coastTicks = 0;
		while (_player.Movement.HorizontalSpeed > 1 && coastTicks++ < 100) await Tick();
		await Reset();
		_player.Velocity = runVelocity;
		var counterTicks = 0;
		while (_player.Velocity.Z < -0.0254f && counterTicks++ < 100)
			await Tick(new(new(0, 1), false, false, false));
		Check(counterTicks < coastTicks, "Counter-strafing must stop the player sooner than releasing movement.");

		await Reset();
		_player.Velocity = runVelocity;
		var jump = Forward with { Jump = true };
		await Tick(jump);
		Check(_player.Velocity.Y > 7, "Source jump must use roughly 302 u/s impulse.");
		var apex = _player.Position.Y;
		for (var i = 0; i < 85; i++)
		{
			await Tick(jump with { Move = Vector2.Zero });
			apex = Mathf.Max(apex, _player.Position.Y);
			if (i == 6)
				Check(Mathf.Abs(_player.Movement.HorizontalSpeed - 250) < 0.2f, "Airborne momentum must survive with no movement input.");
		}
		Check(apex > 1.39f && apex < 1.46f, $"Jump apex must be close to 57 Source units, got {apex} m.");
		Check(_player.IsOnFloor(), "Holding jump must not automatically jump again in the default mode.");
		await Tick(jump with { JumpPressed = true });
		Check(_player.Velocity.Y > 0, "A fresh wheel pulse must jump even while Space remains held.");
		await Tick(jump, 100);
		await Tick();
		await Tick(jump);
		Check(!_player.IsOnFloor() && _player.Velocity.Y > 0, "Releasing and pressing jump must allow the next jump.");
		await Reset();
		_player.Movement.AutoBunnyHop = true;
		await Tick(jump, 55);
		Check(!_player.IsOnFloor(), "Optional auto-bhop must jump again when holding Space.");
		_player.Movement.AutoBunnyHop = false;

		await Reset();
		await Tick(jump);
		var before = _player.Velocity.Z;
		await Tick(new(new(1, 0), false, false, false), 5);
		Check(_player.Velocity.X > 0 && Mathf.Abs(_player.Velocity.Z - before) < 0.001f, "Air strafe must add lateral speed while preserving forward momentum.");
		Check(_player.Velocity.X <= 30 * SourceMovement.Unit + 0.001f, "Air speed cap must limit the wish direction, not total momentum.");

		await Reset();
		await Tick(new(Vector2.Zero, false, true, false), 20);
		var ceiling = Box(new(0, 1.8f, 0), new(4, 0.6f, 4));
		await Tick(count: 4);
		Check(_player.Movement.IsDucked, "Releasing Ctrl must not stand the player up into a ceiling.");
		ceiling.QueueFree();
		await Tick(count: 20);
		Check(!_player.Movement.IsDucked, "The player must stand up when there is headroom again.");

		await Reset();
		await Tick(new(Vector2.Zero, false, true, false), 3);
		var droppedGun = new WeaponPickup { WeaponClass = "weapon_ak47", Position = new(0, .15f, 0), Freeze = true };
		AddChild(droppedGun);
		await Tick(new(Vector2.Zero, false, true, false), 3);
		await Tick(count: 3);
		Check(!_player.Movement.IsDucked, "A dropped weapon inside the hull must not prevent standing up.");
		droppedGun.QueueFree();

		await Reset();
		await Tick(new(Vector2.Zero, false, true, false), 3);
		var otherPlayer = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		otherPlayer.LocalInput = false;
		otherPlayer.DebugControls = false;
		otherPlayer.Position = _player.Position + new Vector3(.1f, 0, 0);
		AddChild(otherPlayer);
		await Tick();
		Check(!_player.Movement.IsDucked, "An existing overlap with another player must not trap the crouched stance.");

		otherPlayer.Position = new(10, SourceMovement.DuckHeight + .03f, 0);
		await Reset();
		await Tick(new(Vector2.Zero, false, true, false), 3);
		otherPlayer.Position = _player.Position + Vector3.Up * (SourceMovement.DuckHeight + .03f);
		await Tick(count: 3);
		Check(_player.Movement.IsDucked, "A player above the crouched hull must still block standing up.");
		otherPlayer.QueueFree();
		await Tick(count: 3);
		Check(!_player.Movement.IsDucked, "Standing must resume after the player above moves away.");

		// A real 48-unit passage rejects the former 54-unit crouch hull. Check
		// traversal and blocked unducking, rather than just reading constants.
		await Reset(new(20, .003f, 0));
		var lowPassage = Box(new(20, 48 * SourceMovement.Unit + .15f, -2), new(4, .3f, 2));
		await Tick(Forward with { Duck = true }, 80);
		Check(_player.Position.Z < -1.5f && _player.Movement.IsDucked, "Crouching must enter a 48-unit passage.");
		await Tick(count: 20);
		Check(_player.Movement.IsDucked, "Releasing duck inside a low passage must retain the crouched hull.");
		await Tick(Forward with { Duck = true }, 80);
		Check(_player.Position.Z < -3.5f, "Crouching must traverse the full low passage without getting stuck.");
		lowPassage.QueueFree();

		await Reset(new(10, 0.003f, 0));
		var stair = Box(new(10, 0.2f, -2), new(4, 0.4f, 2));
		await Tick(Forward, 32);
		Check(_player.Position.Z < -1.5f && _player.Position.Y > 0.39f, $"Player must climb a 0.4 m step, got {_player.Position}.");
		await Tick(new(new(0, 1), false, false, false), 70);
		Check(_player.Position.Z > -0.5f && _player.Position.Y < 0.01f && _player.IsOnFloor(), "Player must descend a step and remain grounded.");
		stair.QueueFree();
		var wall = Box(new(10, 0.3f, -2), new(4, 0.6f, 2));
		await Reset(new(10, 0.003f, 0));
		await Tick(Forward, 65);
		Check(_player.Position.Z > -0.61f && _player.Position.Y < 0.01f, "A step taller than 18 units must block movement.");
		wall.QueueFree();

		await Reset();
		_player.Inventory.EquipSlot(WeaponSlot.Primary);
		Check(_player.MaxMoveSpeed == 221 && _player.ActiveWeapon?.Name == "AK47", "Primary slot must select AK47 and its speed.");
		await Tick(Forward, 100);
		Check(Mathf.Abs(_player.Movement.HorizontalSpeed - 221) < 0.1f, "Equipped weapon speed must drive ground movement.");
		_player.Inventory.EquipPrevious();
		Check(_player.ActiveWeapon?.Slot == WeaponSlot.Melee, "Q must restore the previous weapon.");
		_player.Inventory.EquipSlot(WeaponSlot.Primary);
		_player.Inventory.EquipSlot(WeaponSlot.Primary);
		Check(_player.ActiveWeapon?.Name == "M4A1" && _player.MaxMoveSpeed == 230, "Repeated slot selection must cycle primary weapons.");
		Check(_player.Inventory.Weapons.CountEquipped() == 1, "Only one weapon may be equipped.");
		var added = _player.Inventory.AddWeapon(GD.Load<PackedScene>("res://weapons/deagle_view.tscn"));
		Check(_player.ActiveWeapon == added && added.Wielder == _player, "Picking up a weapon must bind and equip it.");
		_player.Inventory.RemoveWeapon(added);
		Check(_player.ActiveWeapon != added && _player.Inventory.Weapons.Count == loadoutCount, "Removing the active weapon must select a valid replacement.");

		_player.TakeDamage(35);
		Check(_player.Health == 65 && _player.IsAlive, "Damage must reduce health.");
		_player.TakeDamage(100);
		Check(!_player.IsAlive && !_player.Inventory.Enabled && _player.Inventory.Weapons.CountEquipped() == 0, "Death must disable and holster weapons.");
		Check(!_player.Inventory.EquipSlot(WeaponSlot.Primary), "Dead players cannot switch weapons.");
		await Reset();
		Check(_player.Health == 100 && _player.Inventory.Enabled && _player.Inventory.Weapons.CountEquipped() == 1, "Respawn must restore health and weapon control.");
		_player.Movement.ToggleNoclip();
		var start = _player.Position;
		await Tick(new(new(0, -1), true, false, false), 10);
		Check(_player.Position.Y > start.Y && _player.Position.Z < start.Z, "Noclip must allow flight.");
		await Reset();
		Check(!_player.Movement.Noclip && _player.IsOnFloor(), "Respawn must restore collision after noclip.");

		GD.Print($"SOURCE_MOVEMENT coast_ticks={coastTicks} counter_ticks={counterTicks} jump_apex_m={apex:F4}");
		_player.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		var map = GD.Load<PackedScene>("res://scenes/maps/dust2.tscn").Instantiate<Node3D>();
		AddChild(map);
		_player = map.GetNode<Player>("Player");
		// Maps enter CSS's team/class selection before a living player can move.
		_player.JoinTeam(2); _player.JoinClass(1);
		for (var i = 0; i < 100; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		Check(_player.IsOnFloor() && _player.Inventory.Enabled && _player.ActiveWeapon?.Equipped == true,
			"Main Dust2 scene must spawn a grounded player with an equipped weapon and enabled input.");
		Check(_player.GetNode<Node3D>("View").Visible, "Local player view must be visible.");
		Check(_player.GetNode<Control>("HUD/Crosshair").Visible, "Local player must display the crosshair.");
		GetViewport().PushInput(new InputEventKey { PhysicalKeycode = Key.Key1, Pressed = true });
		Check(_player.ActiveWeapon?.Name == "AK47", "Local player input must select its own inventory slot.");
		GetViewport().PushInput(new InputEventKey { PhysicalKeycode = Key.Escape, Pressed = true });
		Check(!_player.Inventory.Enabled && Input.MouseMode == Input.MouseModeEnum.Visible, "Escape must release mouse and disable weapon input.");
		_player.Menu!.SetOpen(false);
		_player.SetMouseCaptured(false);
		GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
		Check(_player.Inventory.Enabled && _player.HasLocalControl, "Click must recapture mouse and restore weapon input.");
		Check(_player.ActiveWeapon is FirearmView { ShotsFired: 0 }, "The click used to recapture mouse must not fire a shot.");
		Input.ActionPress("move_forward");
		for (var i = 0; i < 6; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		Input.ActionRelease("move_forward");
		Check(_player.Movement.HorizontalSpeed > 1, "Local input actions must drive the player physics loop.");
		_player.DebugControls = false;
		// Use the same isolated floor as the movement checks, away from map geometry.
		_player.Reparent(this);
		_player.SetMouseCaptured(true);
		map.QueueFree();
		_player.SetSpawn(new(Basis.Identity, new(0, 0.003f, 0)));
		_player.Respawn();
		for (var i = 0; i < 10; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		floor.SetMeta("source_surface", "wood");
		var movementSounds = _player.MovementAudio.Sounds;
		var soundCount = movementSounds.SoundsPlayed;
		Input.ActionPress("move_forward");
		for (var i = 0; i < 80; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		Input.ActionRelease("move_forward");
		for (var i = 0; i < 30; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		Check(movementSounds.SoundsPlayed > soundCount && movementSounds.LastSound.Contains("wood"),
			"Running must play footsteps for the contacted surface.");
		soundCount = movementSounds.SoundsPlayed;
		Input.ActionPress("walk");
		Input.ActionPress("move_forward");
		for (var i = 0; i < 80; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		Input.ActionRelease("move_forward");
		Input.ActionRelease("walk");
		for (var i = 0; i < 30; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		Check(movementSounds.SoundsPlayed == soundCount, "Shift walking must be silent like CS:S.");
		Input.ActionPress("duck");
		Input.ActionPress("move_forward");
		for (var i = 0; i < 80; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		Input.ActionRelease("move_forward");
		Input.ActionRelease("duck");
		for (var i = 0; i < 30; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		Check(movementSounds.SoundsPlayed == soundCount, "Crouching must be silent like CS:S.");
		foreach (var direction in new[] { MouseButton.WheelUp, MouseButton.WheelDown })
		{
			soundCount = movementSounds.SoundsPlayed;
			GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = direction, Pressed = true });
			GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = direction, Pressed = false });
			// Frame signal precedes the player's physics callback; wait through that tick.
			for (var i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			Check(_player.Velocity.Y > 0, $"{direction} pulse between physics ticks must produce a jump.");
			Check(movementSounds.SoundsPlayed == soundCount + 1, "Accepted jump must emit one surface sound.");
			for (var i = 0; i < 100; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			Check(_player.IsOnFloor(), "A wheel pulse must not remain held or automatically jump on landing.");
			Check(movementSounds.SoundsPlayed == soundCount + 2, "Landing must emit one additional surface sound.");
		}
		GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true });
		_player.SetMouseCaptured(false);
		for (var i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		Check(_player.GetNode<Control>("HUD/Crosshair").Visible && _player.ActiveWeapon!.Visible,
			"Releasing the mouse must preserve the HUD and weapon view.");
		_player.SetMouseCaptured(true);
		for (var i = 0; i < 2; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		Check(_player.IsOnFloor(), "Releasing the mouse must discard a pending wheel jump.");
		_player.QueueFree();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}
}

internal static class PlayerCheckExtensions
{
	public static bool TrueForAllOwned(this System.Collections.Generic.IReadOnlyList<WeaponView> weapons, Player player)
	{
		foreach (var weapon in weapons) if (weapon.Wielder != player) return false;
		return true;
	}
	public static int CountEquipped(this System.Collections.Generic.IReadOnlyList<WeaponView> weapons)
	{
		var count = 0;
		foreach (var weapon in weapons) if (weapon.Equipped) count++;
		return count;
	}
}
