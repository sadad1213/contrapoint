using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.Players;
using Contrapoint.UI;
using Contrapoint.Weapons;

public partial class PlayerPresentationChecks : Node3D
{
	private Player _player = null!;
	private int _checks;
	private const float Dt = 1f / 66;
	private void Check(bool result, string message)
	{
		_checks++;
		if (!result) throw new Exception(message);
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
			_player.Body._Process(Dt);
			_player.ThirdPerson._PhysicsProcess(Dt);
			_player.ThirdPerson._Process(Dt);
		}
	}
	private async Task CheckCameraInterpolation()
	{
		var camera = _player.FirstPerson!;
		camera.SetPhysicsProcess(false);
		var start = _player.GlobalPosition;
		camera.ResetHistory();
		var previousEye = _player.Camera.GlobalPosition;
		_player.Position += Vector3.Right;
		camera._PhysicsProcess(Dt);
		var currentEye = _player.Camera.GlobalPosition;
		var sawIntermediatePosition = false;
		for (var frame = 0; frame < 12; frame++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			camera._Process(0);
			var x = camera.GlobalPosition.X;
			sawIntermediatePosition |= x > previousEye.X + 0.001f && x < currentEye.X - 0.001f;
			Check(x >= previousEye.X && x <= currentEye.X, "Rendered eyes must stay between physics samples without overshoot.");
			Check(_player.Recoil.GetBulletAim().Origin.IsEqualApprox(currentEye), "Rendering must not move the current-tick bullet origin.");
		}
		Check(sawIntermediatePosition, "Rendering must show positions between physics ticks.");
		_player._Input(new InputEventMouseMotion { Relative = new(30, -10), ScreenRelative = new(30, -10) });
		camera._Process(0);
		Check(camera.GlobalBasis.IsEqualApprox(_player.Camera.GlobalBasis), "Mouse look must reach the rendered camera before another physics tick.");
		Check(!_player.Camera.GlobalBasis.IsEqualApprox(Basis.Identity), "The mouse sample must actually turn the view.");
		_player.Camera.Fov = 40;
		camera._Process(0);
		Check(camera.Fov == 40, "Rendered camera must follow weapon zoom.");
		_player.Camera.Fov = 75;
		_player.SourceTeleport(start + new Vector3(8, 2, 0), 0.5f, 0.2f);
		camera._Process(0);
		Check(camera.GlobalPosition.IsEqualApprox(_player.Camera.GlobalPosition), "Teleport must discard the old rendered eye position.");
		_player.SetSpawn(new(Basis.Identity, start));
		_player.Respawn();
		camera._Process(0);
		Check(camera.GlobalPosition.IsEqualApprox(_player.Camera.GlobalPosition), "Respawn must discard the old rendered eye position.");
		camera.SetPhysicsProcess(true);
	}
	private async Task CheckThirdPersonInterpolation()
	{
		var follow = _player.ThirdPerson;
		var camera = follow.Camera;
		var start = _player.GlobalPosition;
		follow.ResetHistory();
		follow._PhysicsProcess(2); // Settle the boom before checking translation.
		follow._PhysicsProcess(Dt);
		follow._Process(0);
		var cameraStart = camera.GlobalPosition;
		_player.Position += Vector3.Right;
		follow._PhysicsProcess(Dt);
		var currentEye = _player.Camera.GlobalPosition;
		var sawIntermediatePosition = false;
		for (var frame = 0; frame < 12; frame++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			follow._Process(0);
			var travel = camera.GlobalPosition.X - cameraStart.X;
			sawIntermediatePosition |= travel > 0.001f && travel < 0.999f;
			Check(travel >= -0.0001f && travel <= 1.0001f, "Third-person camera must stay between movement samples.");
			Check(Mathf.Abs(_player.Body.GlobalPosition.X - start.X - travel) < 0.0001f,
				"The visible body must move with the same interpolation as the follow camera.");
			Check(_player.Recoil.GetBulletAim().Origin.IsEqualApprox(currentEye), "Third-person smoothing must preserve the physics eye used by shots.");
		}
		Check(sawIntermediatePosition, "Third person must render intermediate movement positions.");
		var beforeTurn = camera.GlobalBasis;
		_player._Input(new InputEventMouseMotion { Relative = new(100, -30), ScreenRelative = new(100, -30) });
		follow._Process(0);
		Check(!camera.GlobalBasis.IsEqualApprox(beforeTurn), "Third-person mouse look must update without waiting for a physics tick.");
		var renderedEyes = _player.Body.GlobalPosition + (_player.Camera.GlobalPosition - _player.GlobalPosition);
		var boomDirection = (_player.Camera.GlobalBasis * new Vector3(follow.Shoulder, 0.12f, follow.Distance)).Normalized();
		Check((camera.GlobalPosition - renderedEyes).Normalized().Dot(boomDirection) > 0.9999f,
			"The rendered boom must immediately follow the latest mouse angles.");
		_player.Camera.Fov = 40;
		follow._Process(0);
		Check(camera.Fov == 40, "Third-person projection must follow weapon zoom on the render frame.");
		_player.Camera.Fov = 75;
		_player.SourceTeleport(start + new Vector3(8, 2, 0), 0.5f, 0.2f);
		Check(camera.GlobalPosition.IsEqualApprox(_player.Camera.GlobalPosition), "Teleport must reset the follow camera immediately.");
		Check(_player.Body.Position.IsZeroApprox(), "Teleport must discard the body's interpolation offset.");
		_player.SetSpawn(new(Basis.Identity, start));
		_player.Respawn();
		Check(camera.GlobalPosition.IsEqualApprox(_player.Camera.GlobalPosition), "Respawn must reset the follow camera immediately.");
		Check(_player.Body.Position.IsZeroApprox(), "Respawn must discard the body's interpolation offset.");
	}
	public override async void _Ready()
	{
		try
		{
			Box(new(0, -0.5f, 0), new(100, 1, 100));
			_player = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
			_player.DebugControls = false;
			AddChild(_player);
			_player.SetPhysicsProcess(false);
			_player.Body.SetProcess(false);
			_player.ThirdPerson.SetPhysicsProcess(false);
			_player.SetMouseCaptured(true);
			await Tick(count: 10);
			Check(_player.IsOnFloor(), "Player must settle on the floor.");
			Check(_player.Body.LowerClip == "Idle_lower", "Standing must select the lower idle clip.");
			Check(_player.Body.UpperClip == "Idle_Upper_KNIFE", "Knife needs its own upper pose.");
			Check(!_player.IsThirdPerson && (_player.Camera.CullMask & 4) == 0, "First person hides the local body.");
			Check(_player.Body.WorldWeapon != null, "Knife must have a world model.");
			Check(_player.RenderCamera.Current && _player.RenderCamera != _player.Camera, "First person must render independently of the gameplay eye transform.");
			await CheckCameraInterpolation();
			var messages = new List<string>();
			void Execute(string text) => ConsoleCommands.Execute(text, _player.Movement, (line, error) => messages.Add((error ? "ERROR " : "") + line));
			Execute("sv_cheats 1; thirdperson");
			await Tick(count: 30);
			Check(_player.IsThirdPerson && _player.ThirdPerson.Camera.Current, "thirdperson must select the follow camera.");
			Check(!_player.GetNode<CanvasLayer>("WeaponLayer").Visible, "Third person hides the weapon overlay.");
			Check(_player.ThirdPerson.ActualDistance > 2, "Follow camera must move behind the player.");
			Check(_player.Camera.Position == Vector3.Zero, "The eye camera must remain at its original position.");
			var aim = _player.Recoil.GetBulletAim();
			Check(aim.Origin.IsEqualApprox(_player.Camera.GlobalPosition), "Bullets must originate at the eyes in third person.");
			Check(aim.Origin.DistanceTo(_player.ThirdPerson.Camera.GlobalPosition) > 2, "Combat aim must be independent of follow camera position.");
			Check(ConsoleCommands.GetCompletions("thirdp").Contains("thirdperson"), "thirdperson must participate in console completion.");
			var wall = Box(new(0, 1.5f, 0.9f), new(6, 4, 0.2f));
			await Tick(count: 3);
			Check(_player.ThirdPerson.ActualDistance < 0.85f, "The camera must retract before a wall behind the player.");
			Check(_player.ThirdPerson.Camera.GlobalPosition.DistanceTo(_player.Camera.GlobalPosition) <= _player.ThirdPerson.ActualDistance + 0.0001f,
				"Rendered retraction must not interpolate from the old camera position through the wall.");
			wall.QueueFree();
			await Tick(count: 35);
			Check(_player.ThirdPerson.ActualDistance > 2, "The camera must recover after the wall is removed.");
			await CheckThirdPersonInterpolation();
			Execute("firstperson");
			Check(!_player.IsThirdPerson && _player.RenderCamera.Current && _player.GetNode<CanvasLayer>("WeaponLayer").Visible, "firstperson restores the rendered eye camera and weapon overlay.");
			Check(_player.Body.Position.IsZeroApprox(), "First person must clear the third-person body's render offset.");
			foreach (var agent in PlayerBody.AgentIds)
			{
				Execute("cl_playermodel " + agent);
				Check(_player.AgentId == agent && _player.Body.Skeleton.GetBoneCount() >= 49, "Every agent must load its skinned skeleton: " + agent);
				var animations = (AnimationPlayer)_player.Body.FindChild("AnimationPlayer", true, false);
				Check(animations.HasAnimation("a_RunN") && animations.HasAnimation("Idle_Shoot_KNIFE") && animations.HasAnimation("Idle_Reload_AK"), "Every agent must carry locomotion and composed weapon action clips.");
				Check(_player.Body.WorldWeapon != null, "Switching agent must retain the equipped weapon.");
			}
			Execute("cl_playermodel leet");
			Execute("cl_playermodel invalid");
			Check(_player.AgentId == "leet" && messages[^1].StartsWith("ERROR"), "Invalid agent IDs must preserve the current agent.");
			await Tick(count: 50);
			var skeleton = _player.Body.Skeleton;
			var spine = skeleton.FindBone("ValveBiped.Bip01_Spine4");
			var reference = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
			reference.LocalInput = false;
			reference.DebugControls = false;
			reference.Position = new(10, 0, 0);
			AddChild(reference);
			reference.SetAgent("leet");
			reference.Body.SetProcess(false);
			for (var frame = 0; frame < 10; frame++)
			{
				await Tick();
				reference.Simulate(default, Dt);
			}
			_player.Body.ResetPose();
			reference.Body.ResetPose();
			var referenceSkeleton = reference.Body.Skeleton;
			var referenceSpine = referenceSkeleton.FindBone("ValveBiped.Bip01_Spine4");
			var maxTurn = 0f;
			foreach (var side in new[] { -1, 1, -1 })
			{
				for (var frame = 0; frame < 45; frame++)
				{
					await Tick(new(new(side, 0), false, false, false));
					reference.Simulate(new(new(0, -1), false, false, false), Dt);
					// Compare the same speed profile while the reference keeps running forward.
					reference.Velocity = new(0, _player.Velocity.Y, -new Vector2(_player.Velocity.X, _player.Velocity.Z).Length());
					reference.Body._Process(Dt);
					var referenceBasis = referenceSkeleton.GlobalBasis * referenceSkeleton.GetBoneGlobalPose(referenceSpine).Basis;
					var forward = skeleton.GlobalBasis * skeleton.GetBoneGlobalPose(spine).Basis * referenceBasis.Inverse() * Vector3.Forward;
					maxTurn = Mathf.Max(maxTurn, Mathf.Abs(Mathf.RadToDeg(Mathf.Atan2(forward.X, -forward.Z))));
					Check(!_player.Body.LowerClip.StartsWith("a_Walk"), "Starting or reversing a running strafe must not briefly select the walking clip.");
				}
			}
			GD.Print($"STRAFE_TORSO_MAX_TURN degrees={maxTurn:F2}");
			Check(maxTurn < 5, "Strafing pelvis yaw must not swing the torso away from the equivalent forward-running aim pose.");
			reference.QueueFree();
			await Tick(new(new(0, -1), false, false, false), 70);
			Check(_player.Body.LowerClip == "a_RunN", "Forward running needs the forward gait.");
			await Tick(new(new(1, 0), false, false, false), 90);
			Check(_player.Body.LowerClip == "a_RunE", "Strafing must select its own directional gait.");
			await Tick(new(new(0, -1), false, false, true), 100);
			Check(_player.Body.LowerClip == "a_WalkN", "Shift walking must select the walking gait.");
			await Tick(new(new(0, -1), false, true, false), 100);
			Check(_player.Body.LowerClip == "a_Crouch_walkN" && _player.Body.UpperClip.StartsWith("Crouch_"), "Crouch walking must affect both animation layers.");
			await Tick(count: 50);
			await Tick(new(Vector2.Zero, true, false, false));
			Check(_player.Body.LowerClip == "Jump", "Jumping must select the authored jump clip.");
			await Tick(count: 100);
			_player.Inventory.EquipSlot(WeaponSlot.Primary);
			var gun = (FirearmView)_player.ActiveWeapon!;
			gun._Process(2);
			_player.Body._Process(0.1);
			Check(_player.Body.UpperClip.EndsWith("_AK"), "Rifle selection must change the upper pose.");
			gun._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
			gun._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
			_player.Body._Process(0.02);
			Check(_player.Body.UpperClip == "Idle_Shoot_AK", "A real shot must select the authored third-person attack clip.");
			gun._Process(0.4);
			gun._UnhandledInput(new InputEventKey { PhysicalKeycode = Key.R, Pressed = true });
			gun._Process(0.2);
			_player.Body._Process(0.1);
			Check(_player.Body.UpperClip == "Idle_Reload_AK", "Third person must follow actual weapon reload state.");
			_player.TakeDamage(1000);
			_player.Body._Process(0.5);
			Check(_player.Body.WorldWeapon == null, "Death must delete the held world weapon.");
			_player.Respawn();
			Check(_player.Body.LowerClip == "Idle_lower" && _player.Body.WorldWeapon!.Visible, "Respawn must restore the living pose and held weapon.");
			GD.Print($"PLAYER_PRESENTATION_CHECKS checks={_checks} failures=0");
			GetTree().Quit();
		}
		catch (Exception error)
		{
			GD.PushError($"PLAYER_PRESENTATION_CHECKS after {_checks}: {error}");
			GetTree().Quit(1);
		}
	}
}
