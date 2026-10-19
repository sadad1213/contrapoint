using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.Players;
using Contrapoint.UI;
using Contrapoint.Weapons;

public partial class ViewmodelMotionChecks : Node3D
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
			GD.PushError("Viewmodel checks require --console-test-profile.");
			GetTree().Quit(1);
			return;
		}
		try { CheckLag(); await CheckPlayer(); }
		catch (Exception exception) { _failures++; GD.PushError(exception.ToString()); }
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		GD.Print($"VIEWMODEL_MOTION checks={_checks} failures={_failures}");
		GetTree().Quit(_failures == 0 ? 0 : 1);
	}

	private void CheckLag()
	{
		foreach (var fps in new[] { 30, 60, 144, 300 })
		{
			var lag = new SourceViewmodelLag();
			Check(lag.Sample(Basis.Identity, 1.0 / fps, .1f, 1.6f).IsZeroApprox(), "First sample cannot kick the arms.");
			var offset = Vector3.Zero;
			for (var i = 1; i <= fps; i++)
				offset = lag.Sample(Basis.FromEuler(new(0, (float)i / fps, 0)), 1.0 / fps, .1f, 1.6f);
			var expected = new Vector3(Mathf.Sin(.1f), 0, Mathf.Cos(.1f) - 1) * (1.6f * .98f * SourceMovement.Unit);
			Check(offset.DistanceTo(expected) < .000001f, $"Source 100ms yaw lag at {fps} FPS, including direction and units.");
			for (var i = 0; i < fps; i++) offset = lag.Sample(Basis.FromEuler(new(0, 1, 0)), 1.0 / fps, .1f, 1.6f);
			Check(offset.IsZeroApprox(), "Sway returns exactly to rest after holding aim.");
			Check(lag.Sample(Basis.FromEuler(new(.4f, .2f, 0)), 1.0 / fps, 0, 1.6f).IsZeroApprox(), "Zero interpolation disables sway immediately.");
			lag.Reset();
			lag.Sample(Basis.Identity, 1.0 / fps, .1f, 1.6f);
			offset = lag.Sample(Basis.FromEuler(new(.1f, 0, 0)), 1.0 / fps, .1f, 1.6f);
			Check(offset.Y < 0 && Mathf.IsZeroApprox(offset.X), "Looking up leaves the gun below aim.");
			Check(lag.Sample(Basis.Identity, 1.0 / fps, .1f, 0).IsZeroApprox(), "Zero scale disables sway.");
			lag.Reset();
			lag.Sample(Basis.FromEuler(new(0, Mathf.DegToRad(179), 0)), 1.0 / fps, .1f, 1.6f);
			offset = lag.Sample(Basis.FromEuler(new(0, Mathf.DegToRad(-179), 0)), 1.0 / fps, .1f, 1.6f);
			Check(offset.IsFinite() && offset.Length() < .002f, "Crossing the yaw seam cannot fling the arms.");
		}
	}

	private async Task CheckPlayer()
	{
		var floor = new StaticBody3D();
		floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(100, .2f, 100) }, Position = new(0, -.1f, 0) });
		AddChild(floor);
		var player = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		AddChild(player);
		player.SetPhysicsProcess(false);
		var bob = player.ViewmodelBob;
		bob.SetProcess(false);
		foreach (var weapon in player.Inventory.Weapons) weapon.GetNode<WeaponSway>("WeaponSway").SetProcess(false);
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		for (var i = 0; i < 10; i++) player.Simulate(default, 1f / 66);
		Check(player.IsOnFloor(), "Test player stands on a real floor.");
		var errors = 0;
		void Command(string text) => ConsoleCommands.Execute(text, player.Movement, (_, error) => { if (error) errors++; });
		Command("sv_cheats 0; cl_bob_style 1; cl_bobcycle .98; cl_bobcycle_old .8; cl_bobamt_vert .25; cl_bobamt_lat .4; cl_bob_lower_amt 18; cl_wpn_sway_scale 1.6; cl_wpn_sway_interp .1");
		Check(errors == 0 && bob.UseNewBob, "CSSO motion settings work with cheats disabled.");
		Check(ConsoleCommands.GetCompletions("cl_bob_style").Contains("cl_bob_style"), "Style switch is discoverable in console completion.");
		player.Inventory.EquipSlot(WeaponSlot.Melee);
		foreach (var fps in new[] { 30, 60, 144 })
		{
			foreach (var modern in new[] { false, true })
			{
				Command($"cl_bob_style {(modern ? 1 : 0)}");
				bob.Reset();
				player.Velocity = Vector3.Zero;
				bob._Process(1.0 / fps);
				Check(bob.MotionOffset.IsEqualApprox(Transform3D.Identity), "Stationary grounded knife stays at rest.");
				var camera = player.Camera.Transform;
				player.Velocity = Vector3.Forward * (250 * SourceMovement.Unit);
				for (var frame = 0; frame < fps * 3; frame++)
				{
					bob._Process(1.0 / fps);
					Check(bob.MotionOffset.IsFinite() && bob.MotionOffset.Origin.Length() < (modern ? .09 : .02), "Both bob styles stay within Source motion bounds.");
				}
				Check(Mathf.IsEqualApprox(bob.SmoothedSpeed, 250), "Actual velocity drives bob.");
				Check(player.Camera.Transform.IsEqualApprox(camera), "Motion cannot change aim.");
				Check(modern ? bob.Offset.Origin.Z > .04f : bob.Offset.Origin.X == 0, "Modern running lowers the gun; legacy has no lateral translation.");
				var phase = bob.PhaseSeconds;
				Command("toggle cl_bob_style");
				Check(bob.UseNewBob != modern && bob.PhaseSeconds == phase, "Console toggle changes style without restarting gait.");
				Command($"cl_bob_style {(modern ? 1 : 0)}");
				player.Inventory.EquipSlot(WeaponSlot.Primary);
				var sway = player.ActiveWeapon!.GetNode<WeaponSway>("WeaponSway");
				sway._Process(1.0 / fps);
				Check(sway.Transform.IsEqualApprox(bob.MotionOffset) && bob.PhaseSeconds == phase, "Equipping another gun preserves the shared motion.");
				player.Inventory.EquipSlot(WeaponSlot.Melee);
				player.Velocity = Vector3.Zero;
				for (var frame = 0; frame < fps * 2; frame++) bob._Process(1.0 / fps);
				Check(bob.MotionOffset.IsEqualApprox(Transform3D.Identity), "Both styles settle after stopping.");
			}
		}
		Command("cl_bob_style 1; cl_bobamt_vert .6; cl_wpn_sway_scale 2; host_writeconfig viewmodel_motion_check");
		Command("cl_bob_style 0; cl_bobamt_vert .2; cl_wpn_sway_scale 0; exec viewmodel_motion_check");
		Check(bob.UseNewBob && Mathf.IsEqualApprox(bob.VerticalAmount, .6f) && bob.SwayScale == 2, "Style, bob amounts and sway survive a config round trip.");
		Command("cl_bob_style 2; cl_bobamt_vert nan; cl_wpn_sway_interp -1");
		Check(errors == 3 && bob.UseNewBob && bob.SwayInterpolation == .1f, "Invalid motion settings report errors and preserve the previous values.");
		player.Respawn();
		Check(bob.MotionOffset.IsEqualApprox(Transform3D.Identity) && bob.SwayOffset.IsZeroApprox() && bob.PhaseSeconds == 0, "Respawn resets bob and sway history.");
		Check(bob.UseNewBob && bob.SwayScale == 2, "Respawn retains console preferences.");
		player.Free();
		floor.Free();
	}
}
