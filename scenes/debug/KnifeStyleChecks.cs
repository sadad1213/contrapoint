using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.Players;
using Contrapoint.UI;
using Contrapoint.Weapons;

public partial class KnifeStyleChecks : Node3D
{
	private int _checks, _failures;
	private void Check(bool condition, string message)
	{
		_checks++;
		if (condition) return;
		_failures++;
		GD.PushError(message);
	}
	public override async void _Ready()
	{
		if (!OS.GetCmdlineUserArgs().Contains("--console-test-profile"))
		{
			GD.PushError("Knife style checks require --console-test-profile.");
			GetTree().Quit(1); return;
		}
		try { await Run(); }
		catch (Exception exception) { _failures++; GD.PushError(exception.ToString()); }
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		GD.Print($"KNIFE_STYLE checks={_checks} failures={_failures}");
		GetTree().Quit(_failures == 0 ? 0 : 1);
	}
	private static AnimationPlayer Animation(KnifeView knife) => (AnimationPlayer)knife.GetNode("WeaponSway/Model").FindChild("AnimationPlayer", true, false)!;
	private void CheckInspectInterruptions()
	{
		foreach (var fps in new[] { 30, 60, 144 })
		{
			var knife = GD.Load<PackedScene>("res://weapons/knife_view.tscn").Instantiate<KnifeView>();
			AddChild(knife);
			knife.SetProcess(false);
			knife.Sounds.Enabled = false;
			knife.SetViewStyle(1);
			knife.Equip();
			var animation = Animation(knife);
			var elapsed = 0.0;
			void Advance(double seconds)
			{
				while (seconds > .000001)
				{
					var dt = Math.Min(seconds, 1.0 / fps);
					knife._Process(dt);
					seconds -= dt;
					elapsed += dt;
				}
			}
			void Inspect()
			{
				knife.HandleCommand("lookatweapon", true);
				knife.HandleCommand("lookatweapon", false);
			}
			for (var press = 0; press < 9; press++)
			{
				Advance(1.0 / fps);
				Inspect();
				Check(animation.AssignedAnimation == "lookat0" + (press % 3 + 1) && animation.CurrentAnimationPosition < .001,
					$"Every F press restarts inspect, including during draw, at {fps} FPS.");
			}
			knife.HandleCommand("attack", true);
			knife.HandleCommand("attack", false);
			Check(animation.AssignedAnimation.ToString().StartsWith("lookat", StringComparison.Ordinal), "Inspect spam cannot unlock attack before deploy readiness.");
			Advance(.74 - elapsed);
			Inspect();
			knife.HandleCommand("attack", true);
			Advance(.02);
			knife.HandleCommand("attack", false);
			Check(animation.AssignedAnimation == "light_miss1", "A held attack interrupts inspect at the original deploy deadline.");
			var attackClip = animation.AssignedAnimation.ToString();
			for (var press = 0; press < 6; press++)
			{
				Inspect();
				Advance(1.0 / fps);
				Check(animation.AssignedAnimation == attackClip, "F cannot replace an attack during its contact/cooldown window.");
			}
			Advance(.51);
			Inspect();
			Check(animation.AssignedAnimation.ToString().StartsWith("lookat", StringComparison.Ordinal), "Inspect can interrupt the remaining attack animation once the gameplay cooldown is over.");
			knife.HandleCommand("attack", true);
			knife.HandleCommand("attack", false);
			Check(animation.AssignedAnimation == "light_miss2", "Inspect never adds a delay to a ready attack.");
			knife.Free();
		}
	}
	private async Task Run()
	{
		var player = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		AddChild(player);
		player.SetPhysicsProcess(false);
		player.ViewmodelBob.SetProcess(false);
		var knife = player.Inventory.Weapons.OfType<KnifeView>().Single();
		knife.SetProcess(false);
		var errors = 0;
		void Command(string text) => ConsoleCommands.Execute(text, player.Movement, (_, error) => { if (error) errors++; });
		Command("sv_cheats 0; cl_knife_style 0");
		player.Inventory.Equip(knife);
		knife._Process(2);
		Command("cl_knife_style 1");
		Check(errors == 0 && knife.ViewStyle == 1 && player.Inventory.ActiveWeapon == knife, "Console changes the cosmetic model with cheats disabled and preserves the inventory entity.");
		Check(knife.GetNode("WeaponSway/Model").SceneFilePath.Contains("butterfly"), "CSSO butterfly is the active model.");
		Check(knife.NetworkClipName == "draw", "Butterfly deploy uses a compatible network animation name.");
		Command("+lookatweapon; -lookatweapon");
		Check(Animation(knife).AssignedAnimation == "lookat01", "The console/bind path can inspect during draw.");
		Command("+lookatweapon; -lookatweapon");
		Check(Animation(knife).AssignedAnimation == "lookat02", "The console/bind path can restart an ongoing inspect.");
		var skeleton = (Skeleton3D)knife.FindChild("Skeleton3D", true, false)!;
		var animation = Animation(knife);
		Check(skeleton.GetBoneCount() == 95 && animation.GetAnimationList().Length == 16, "Knife and current T Phoenix arms retain their bind bones and authored clips.");
		foreach (var clip in animation.GetAnimationList())
		{
			animation.Play(clip);
			foreach (var fraction in new[] { 0f, .25f, .6f, .95f })
			{
				animation.Seek(animation.GetAnimation(clip).Length * fraction, true);
				for (var i = 0; i < skeleton.GetBoneCount(); i++)
				{
					var name = skeleton.GetBoneName(i);
					if (!name.StartsWith("arms__", StringComparison.Ordinal)) continue;
					var target = skeleton.FindBone(name[6..]);
					var actual = skeleton.GetBoneGlobalPose(i);
					if (target < 0) { Check(actual.IsFinite(), "Unmerged torso ancestor remains finite."); continue; }
					var expected = skeleton.GetBoneGlobalPose(target);
					// glTF/Godot independently optimize the two bone tracks. Allow
					// sub-millimetre / quarter-degree interpolation differences.
					Check(actual.IsFinite() && actual.Origin.DistanceTo(expected.Origin) < .00025f &&
						actual.Basis.GetRotationQuaternion().AngleTo(expected.Basis.GetRotationQuaternion()) < Mathf.DegToRad(.25f),
						"T hand bone follows the knife animation with its original inverse bind: " + clip + "/" + name +
						$" position={actual.Origin.DistanceTo(expected.Origin)} angle={actual.Basis.GetRotationQuaternion().AngleTo(expected.Basis.GetRotationQuaternion())}");
				}
			}
		}
		animation.Play("idle1");
		animation.Seek(.5, true);
		var hand = skeleton.FindBone("arms__v_weapon.Bip01_L_Hand");
		var handInCamera = player.Camera.GlobalTransform.AffineInverse() * (skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(hand).Origin);
		var halfHeight = MathF.Tan(Mathf.DegToRad(58) * .5f) * -handInCamera.Z;
		Check(handInCamera.Z < -.02f && handInCamera.X < 0 && MathF.Abs(handInCamera.X) < halfHeight * 1280 / 720 &&
			MathF.Abs(handInCamera.Y) < halfHeight * 1.2f, "The left hand must remain in front of the viewmodel camera and visible at idle: " + handInCamera);
		var knifeMesh = knife.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>()
			.First(mesh => mesh.GetActiveMaterial(0).ResourceName == "knife_butterfly_steel");
		Check(knifeMesh.GetActiveMaterial(0) is ShaderMaterial, "Steel uses Source masks and a view-dependent reflection material.");
		foreach (MeshInstance3D mesh in knife.FindChildren("*", "MeshInstance3D", true, false)) Check(mesh.Layers == 2, "New geometry stays on the viewmodel layer.");
		knife.Equip();
		var sounds = knife.Sounds.SoundsPlayed;
		knife._Process(.2);
		Check(knife.Sounds.SoundsPlayed > sounds && knife.Sounds.LastSound.Contains("butterfly"), "Original draw sound follows the MDL event.");
		knife._Process(2);
		for (var inspect = 1; inspect <= 3; inspect++)
		{
			knife.HandleCommand("lookatweapon", true);
			Check(Animation(knife).AssignedAnimation == "lookat0" + (1 + (inspect + 1) % 3), "Inspect cycles through each original butterfly animation.");
			knife._Process(6);
		}
		knife.HandleCommand("attack", true);
		knife.HandleCommand("attack", false);
		Check(Animation(knife).AssignedAnimation == "light_miss1" && knife.NetworkClipName == "midslash1", "Butterfly attack uses its authored clip and a compatible network action.");
		Command("cl_knife_style 0");
		knife.HandleCommand("attack", true);
		knife.HandleCommand("attack", false);
		Check(Animation(knife).AssignedAnimation == "draw", "Changing cosmetics cannot bypass an active attack cooldown.");
		knife._Process(.6);
		knife.HandleCommand("attack2", true);
		knife.HandleCommand("attack2", false);
		Check(Animation(knife).AssignedAnimation == "stab", "Original karambit still attacks after switching back.");
		Command("cl_knife_style 1; host_writeconfig knife_style_check; cl_knife_style 0; exec knife_style_check");
		Check(knife.ViewStyle == 1, "The style survives a config round trip.");
		Command("cl_knife_style -1; cl_knife_style 2; cl_knife_style NaN");
		Check(errors == 3 && knife.ViewStyle == 1, "Invalid style values preserve the selected model.");
		player.Body.EnsurePose();
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		player.Body.EnsurePose();
		Check(GodotObject.IsInstanceValid(player.Body), "Body animation remains valid after replacing the viewmodel rig.");
		player.Inventory.RememberSpawnLoadout();
		player.Respawn();
		var respawnKnife = player.Inventory.Weapons.OfType<KnifeView>().Single();
		Check(respawnKnife.ViewStyle == 1, "Respawn/recreated inventory inherits the chosen cosmetic.");
		Command("cl_knife_style 0");
		player.Free();
		CheckInspectInterruptions();
	}
}
