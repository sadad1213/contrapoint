using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using Contrapoint.Players;
using Contrapoint.Weapons;

// Compare sampled bones with Godot's original AnimationPlayer evaluation and
// bot weapon clocks with fully animated weapons; no reduced-frequency oracle.
public partial class BodyPoseChecks : Node3D
{
	private int _checks;
	private void Check(bool condition, string message)
	{
		_checks++;
		if (!condition) throw new Exception(message);
	}
	public override void _Ready()
	{
		try
		{
			foreach (var agent in PlayerBody.AgentIds) CheckAgent(agent);
			CheckWeaponClocks();
			GD.Print($"BODY_POSE_CHECKS checks={_checks} failures=0"); GetTree().Quit();
		}
		catch (Exception error) { GD.PushError($"BODY_POSE_CHECKS after {_checks}: {error}"); GetTree().Quit(1); }
	}
	private void CheckAgent(string agent)
	{
		var model = PlayerSceneCache.Load($"res://assets/players/{agent}/{agent}.glb").Instantiate<Node3D>();
		AddChild(model);
		var skeleton = (Skeleton3D)model.FindChild("Skeleton3D", true, false);
		var animations = (AnimationPlayer)model.FindChild("AnimationPlayer", true, false);
		animations.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
		var count = skeleton.GetBoneCount();
		var lower = new bool[count];
		for (var i = 0; i < count; i++)
		{
			lower[i] = skeleton.GetBoneName(i).ToString().Contains("Pelvis");
			for (var parent = i; parent >= 0; parent = skeleton.GetBoneParent(parent))
				if (skeleton.GetBoneName(parent).ToString().Contains("Thigh")) lower[i] = true;
		}
		var sampler = new PlayerBodyPose(skeleton, lower);
		using var hitboxData = JsonDocument.Parse(FileAccess.GetFileAsString($"res://assets/physics/css_damage/{agent}_hitboxes.json"));
		var boxes = new List<(int Bone, Vector3 Min, Vector3 Max)>();
		foreach (var box in hitboxData.RootElement.EnumerateArray())
		{
			var min = box.GetProperty("min"); var max = box.GetProperty("max");
			boxes.Add((skeleton.FindBone(box.GetProperty("bone").GetString()!),
				new Vector3(min[0].GetSingle(), min[1].GetSingle(), min[2].GetSingle()) * SourceMovement.Unit,
				new Vector3(max[0].GetSingle(), max[1].GetSingle(), max[2].GetSingle()) * SourceMovement.Unit));
		}
		var positions = new Vector3[count]; var rotations = new Quaternion[count]; var scales = new Vector3[count];
		foreach (var clip in animations.GetAnimationList())
		{
			var animation = animations.GetAnimation(clip);
			foreach (var fraction in new[] { 0.0, .17, .63, 1.0 })
			{
				var time = animation.Length * fraction;
				animations.Play(clip); animations.Seek(time, true);
				for (var i = 0; i < count; i++)
				{
					positions[i] = skeleton.GetBonePosePosition(i);
					rotations[i] = skeleton.GetBonePoseRotation(i);
					scales[i] = skeleton.GetBonePoseScale(i);
				}
				sampler.Sample(animation, time, true, positions, rotations, scales);
				sampler.Sample(animation, time, false, positions, rotations, scales);
				for (var i = 0; i < count; i++)
				{
					Check(positions[i].DistanceSquaredTo(skeleton.GetBonePosePosition(i)) < 1e-8f, $"position {agent}/{clip}/{fraction}/{i}");
					Check(Mathf.Abs(rotations[i].Normalized().Dot(skeleton.GetBonePoseRotation(i).Normalized())) > .999999f, $"rotation {agent}/{clip}/{fraction}/{i}");
					Check(scales[i].DistanceSquaredTo(skeleton.GetBonePoseScale(i)) < 1e-8f, $"scale {agent}/{clip}/{fraction}/{i}");
				}
				foreach (var box in boxes)
				{
					var transform = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(box.Bone);
					for (var corner = 0; corner < 8; corner++)
					{
						var point = transform * new Vector3((corner & 1) == 0 ? box.Min.X : box.Max.X, (corner & 2) == 0 ? box.Min.Y : box.Max.Y, (corner & 4) == 0 ? box.Min.Z : box.Max.Z);
						Check(point.DistanceSquaredTo(Vector3.Up) <= SourceHitboxes.BoundsRadius * SourceHitboxes.BoundsRadius, $"bounds {agent}/{clip}/{fraction}/{corner}");
					}
				}
			}
		}
		GD.Print("BODY_POSE_AGENT ", agent);
		RemoveChild(model); model.Free();
	}
	private void CheckWeaponClocks()
	{
		var reference = PlayerSceneCache.Load("res://scenes/player/player.tscn").Instantiate<Player>();
		var bot = PlayerSceneCache.Load("res://scenes/player/player.tscn").Instantiate<Player>();
		reference.LocalInput = bot.LocalInput = false; reference.DebugControls = bot.DebugControls = false; bot.IsBot = true;
		AddChild(reference); AddChild(bot);
		reference.SetPhysicsProcess(false); bot.SetPhysicsProcess(false);
		Check(!bot.GetNode<CanvasLayer>("HUD").Visible && !bot.GetNode("HUD").CanProcess(), "bot HUD does not update");
		Check(!bot.GetNode("WeaponLayer/Container/Viewport/Camera3D").CanProcess() && !bot.ViewmodelBob.IsProcessing(), "bot first-person camera and bob do not update");
		bot.GrenadeVision.Flash(1, 1);
		var flash = bot.GrenadeVision.FlashRemaining; bot.GrenadeVision._Process(.25);
		Check(Math.Abs(bot.GrenadeVision.FlashRemaining - flash + .25) < .00001, "bot flash timer continues without screen effects");
		foreach (var kind in Enum.GetValues<FirearmView.FirearmKind>())
		{
			var scene = PlayerSceneCache.Load(WeaponPickup.ViewPath("weapon_" + kind.ToString().ToLowerInvariant()));
			var normal = (FirearmView)reference.Inventory.AddWeapon(scene);
			var cheap = (FirearmView)bot.Inventory.AddWeapon(scene);
			Check(!cheap.GetNode("WeaponSway").IsProcessing() && !cheap.GetNode("WeaponSway").IsProcessingInput(), $"bot viewmodel sway disabled {kind}");
			normal.SetProcess(false); cheap.SetProcess(false);
			for (var tick = 0; tick < 1800; tick++)
			{
				if (tick is > 180 and < 400 && tick % 13 == 0)
				{
					normal.HandleCommand("attack", true); normal.HandleCommand("attack", false);
					cheap.HandleCommand("attack", true); cheap.HandleCommand("attack", false);
				}
				if (tick == 410) { normal.HandleCommand("reload", true); cheap.HandleCommand("reload", true); }
				if (tick == 1020) { normal.HandleCommand("attack2", true); cheap.HandleCommand("attack2", true); }
				normal._Process(1.0 / 120); cheap._Process(1.0 / 120);
				Check(normal.MagazineAmmo == cheap.MagazineAmmo && normal.ReserveAmmo == cheap.ReserveAmmo && normal.Busy == cheap.Busy && normal.Silenced == cheap.Silenced && normal.ZoomLevel == cheap.ZoomLevel, $"weapon state {kind}/{tick}");
				using var normalClip = normal.NetworkAnimation.AssignedAnimation;
				using var cheapClip = cheap.NetworkAnimation.AssignedAnimation;
				Check(normalClip == cheapClip, $"weapon clip {kind}/{tick}: {normalClip}/{cheapClip}");
				var difference = Math.Abs(normal.NetworkAnimation.CurrentAnimationPosition - cheap.NetworkAnimation.CurrentAnimationPosition);
				var animation = normal.NetworkAnimation.GetAnimation(normalClip);
				Check(difference < .00001 || animation.LoopMode != Animation.LoopModeEnum.None && Math.Abs(difference - animation.Length) < .00001,
					$"weapon time {kind}/{tick}: {normal.NetworkAnimation.CurrentAnimationPosition:R}/{cheap.NetworkAnimation.CurrentAnimationPosition:R}");
			}
			GD.Print("BODY_POSE_WEAPON ", kind);
		}
	}
}
