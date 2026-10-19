using Godot;
using System;
using System.Threading.Tasks;
using Contrapoint.Players;

// Run with a real renderer: checks the actual culling signal and lazy hitboxes.
public partial class BodyVisibilityChecks : Node3D
{
	private int _checks;
	private void Check(bool condition, string message)
	{
		_checks++; if (!condition) throw new Exception(message); GD.Print("BODY_VISIBILITY ", message);
	}
	private async Task Frames(int count) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
	private static Transform3D[] Bones(Player actor)
	{
		var result = new Transform3D[actor.Body.Skeleton.GetBoneCount()];
		for (var i = 0; i < result.Length; i++) result[i] = actor.Body.Skeleton.GetBonePose(i);
		return result;
	}
	private static bool Changed(Transform3D[] before, Player actor)
	{
		for (var i = 0; i < before.Length; i++) if (!before[i].IsEqualApprox(actor.Body.Skeleton.GetBonePose(i))) return true;
		return false;
	}
	public override async void _Ready()
	{
		try
		{
			if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("Use a real renderer.");
			Engine.MaxFps = 120; DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
			var camera = new Camera3D { Position = new(0, 1.4f, 7), Current = true }; AddChild(camera); camera.LookAt(new(0, 1, 0));
			var actor = PlayerSceneCache.Load("res://scenes/player/player.tscn").Instantiate<Player>();
			actor.LocalInput = false; actor.DebugControls = false; actor.IsBot = true;
			AddChild(actor); actor.SetPhysicsProcess(false); actor.Movement.ToggleNoclip();
			var visibility = actor.Body.GetNode<VisibleOnScreenNotifier3D>("PoseVisibility");
			actor.Velocity = Vector3.Forward * 5;
			await Frames(20);
			Check(visibility.IsOnScreen(), "visible body enters render view");
			var pose = Bones(actor); await Frames(25);
			Check(Changed(pose, actor), "visible locomotion continues animating");
			actor.SourceTeleport(new(1000, 0, 0), 0, 0); actor.Velocity = Vector3.Forward * 5;
			await Frames(20);
			Check(!visibility.IsOnScreen(), "distant body leaves render view");
			pose = Bones(actor); await Frames(30);
			Check(!Changed(pose, actor), "hidden animation clocks avoid skeleton writes");
			Check(!actor.Body.Hitboxes.Trace(Vector3.Zero, Vector3.Forward * 10, out _, out _, out _), "distant ray misses the player's bounds");
			Check(!Changed(pose, actor), "distant misses avoid evaluating hidden bones");
			var center = actor.Body.Hitboxes.GroupCenter(SourceHitGroup.Head);
			Check(Changed(pose, actor), "hitbox center requests current hidden pose");
			Check(center.IsFinite() && center.DistanceTo(actor.GlobalPosition) < 3, "hidden head remains at the player's position");
			pose = Bones(actor);
			actor.SetBotAim(0, .6f);
			await Frames(20);
			Check(!Changed(pose, actor), "hidden aim waits for a pose consumer");
			var from = actor.GlobalPosition + new Vector3(0, 1, 5);
			Check(actor.Body.Hitboxes.Trace(from, from + Vector3.Forward * 10, out _, out _, out _), "trace hits a hidden moving player");
			Check(Changed(pose, actor), "trace refreshes hidden aim before testing hitboxes");
			actor.SourceTeleport(Vector3.Zero, 0, 0); actor.Velocity = Vector3.Forward * 5;
			await Frames(20);
			Check(visibility.IsOnScreen(), "body re-enters render view");
			pose = Bones(actor); await Frames(25);
			Check(Changed(pose, actor), "animation resumes after re-entry");
			actor.SourceTeleport(new(1000, 0, 0), 0, 0); actor.Velocity = Vector3.Forward * 5;
			await Frames(35);
			pose = Bones(actor); actor.TakeDamage(1000);
			Check(actor.Death.Active && actor.Death.Ragdoll != null, "hidden death creates a ragdoll");
			Check(Changed(pose, actor), "hidden death captures a fresh pose");
			var local = PlayerSceneCache.Load("res://scenes/player/player.tscn").Instantiate<Player>();
			local.DebugControls = false;
			AddChild(local); local.SetMouseCaptured(false); local.SetPhysicsProcess(false);
			local.Movement.ToggleNoclip(); local.Velocity = Vector3.Forward * 5;
			var localVisibility = local.Body.GetNode<VisibleOnScreenNotifier3D>("PoseVisibility");
			await Frames(20);
			Check(!localVisibility.IsOnScreen(), "first-person layer mask hides the local body");
			pose = Bones(local); await Frames(30);
			Check(!Changed(pose, local), "first person advances clocks without writing hidden body bones");
			center = local.Body.Hitboxes.GroupCenter(SourceHitGroup.Head);
			Check(Changed(pose, local) && center.IsFinite(), "local hitboxes request the current first-person pose");
			pose = Bones(local); await Frames(20);
			Check(!Changed(pose, local), "local body returns to lazy evaluation after a hitbox query");
			local.SetThirdPerson(true);
			Check(Changed(pose, local), "third person refreshes the local pose immediately on switching");
			await Frames(40);
			Check(localVisibility.IsOnScreen(), "third-person camera sees the local body");
			pose = Bones(local); await Frames(25);
			Check(Changed(pose, local), "visible local third-person locomotion stays animated");
			local.SetThirdPerson(false); await Frames(20);
			pose = Bones(local); await Frames(25);
			Check(!Changed(pose, local), "returning to first person stops hidden body writes");
			local.TakeDamage(1000);
			Check(local.Death.Active && local.Death.Ragdoll != null && Changed(pose, local), "first-person death captures a fresh local ragdoll pose");
			GD.Print($"BODY_VISIBILITY_CHECKS checks={_checks} failures=0"); GetTree().Quit();
		}
		catch (Exception error) { GD.PushError($"BODY_VISIBILITY_CHECKS after {_checks}: {error}"); GetTree().Quit(1); }
	}
}
