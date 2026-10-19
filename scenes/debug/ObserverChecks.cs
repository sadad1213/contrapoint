using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.Gameplay;
using Contrapoint.Players;
using Contrapoint.UI;
using Contrapoint.Weapons;

public partial class ObserverChecks : Node3D
{
	private int _checks;
	private Player _viewer = null!;
	private void Check(bool value, string message) { _checks++; if (!value) throw new Exception(message); GD.Print("OBSERVER_OK " + message); }
	private async Task Frames(int count = 3)
	{
		for (var i = 0; i < count; i++) { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
	}
	private Player Spawn(string name, Vector3 at, bool local, PlayerTeam team, int id)
	{
		var p = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		p.Name = name; p.Position = at; p.LocalInput = local; p.Team = team; p.NetworkPeerId = id; p.DebugControls = false;
		AddChild(p); p.SetPhysicsProcess(false);
		foreach (var weapon in p.Inventory.Weapons) { weapon.SetProcess(false); weapon.Sounds.Enabled = false; }
		return p;
	}
	public override async void _Ready()
	{
		try { await Run(); GD.Print($"OBSERVER_CHECKS checks={_checks} failures=0"); GetTree().Quit(); }
		catch (Exception e) { GD.PushError(e.ToString()); GD.Print($"OBSERVER_CHECKS checks={_checks} failures=1"); GetTree().Quit(1); }
	}
	private async Task Screenshot(string name)
	{
		if (DisplayServer.GetName() == "headless") return;
		await Frames(); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		using var image = GetViewport().GetTexture().GetImage();
		image.SavePng("res://.godot/" + name + ".png");
	}
	private async Task Run()
	{
		SourceDeathRules.AutoRespawn = false; SourceDeathRules.DisableFreezeCam = true;
		RoundState.Instance!.BeginMatch("defuse"); DefuseState.Instance!.SetPhysicsProcess(false);
		var floor = new StaticBody3D { Position = new(0, -.5f, 0) };
		floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(100, 1, 100) } });
		floor.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new(100, 1, 100) } }); AddChild(floor);
		AddChild(new DirectionalLight3D { RotationDegrees = new(-60, -30, 0) });
		AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color,
			BackgroundColor = new Color("607080"), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = .7f } });
		_viewer = Spawn("Viewer", new(0, 0, 4), true, PlayerTeam.Terrorist, 1);
		var first = Spawn("First ally", Vector3.Zero, false, PlayerTeam.Terrorist, 2);
		var second = Spawn("Second ally", new(3, 0, 0), false, PlayerTeam.Terrorist, 3);
		var enemy = Spawn("Enemy", new(-3, 0, -4), false, PlayerTeam.CounterTerrorist, 4);
		await Frames();
		RoundState.Instance!.BeginMatch("defuse"); DefuseState.Instance!.SetPhysicsProcess(false);
		_viewer.SetVitals(0, 0, false); _viewer.Death._PhysicsProcess(.81);
		await Frames(); var observer = _viewer.Observer!;
		Check(observer.FirstPerson && observer.Target == first, "dead DE player automatically follows a living teammate");
		Check(_viewer.RenderCamera == observer.Camera && observer.Camera.Current, "observer owns the rendered camera");
		Check(observer.Camera.GlobalTransform.IsEqualApprox(first.Camera.GlobalTransform), "eye position and aim match target");
		Check(first.Body.Visible && first.Body.ObservedFirstPerson && (observer.Camera.CullMask & PlayerBody.ObservedLayer) == 0,
			"camera excludes target body without globally hiding it");
		Check(!_viewer.HasLocalControl && !_viewer.Inventory.Enabled, "observation cannot enable the dead player's combat");
		Check(observer.Viewmodel.Model != null && observer.Viewmodel.Source == first.ActiveWeapon && observer.Viewmodel.Viewport.OwnWorld3D,
			"first person renders a passive weapon in its own world");
		Check(observer.Cycle(-1) && observer.Target == second && !first.Body.ObservedFirstPerson, "previous wraps and immediately releases old body mask");
		Check(observer.Cycle() && observer.Target == first, "next wraps among allies only");
		var gun = first.Inventory.Weapons.OfType<FirearmView>().Single(w => w.Kind == FirearmView.FirearmKind.AK47);
		first.Inventory.EquipNetwork(gun); gun.ApplyNetworkAnimation("ak47_reload", .7);
		var packet = first.CaptureNetworkState(); packet["magazine"] = 7; packet["reserve"] = 41; packet["money"] = 4200;
		packet["health"] = 67; packet["armor"] = 45; packet["helmet"] = true;
		packet["display_name"] = "Remote Rifleman";
		packet["view_angles"] = new Vector3(.12f, -.02f, .01f); first.ApplyNetworkState(packet, true);
		await Frames();
		var hud = _viewer.GetNode<CssHud>("HUD/Status");
		Check(hud.Visible && hud.Snapshot.Health == 67 && hud.Snapshot.Armor == 45 && hud.Snapshot.Helmet
			&& hud.Snapshot.Money == 4200 && hud.Snapshot.Magazine == 7 && hud.Snapshot.Reserve == 41 && !hud.Snapshot.CanBuy,
			"observer HUD uses replicated target health, armor, money and ammunition");
		Check(observer.Camera.GlobalBasis.IsEqualApprox(first.Camera.GlobalBasis), "replicated recoil/view angles reach observer");
		Check(first.DisplayName == "Remote Rifleman" && observer.FindChild("ObservedPlayer", true, false) is Label nameLabel
			&& nameLabel.Text == "Remote Rifleman", "observer name uses the replicated player name");
		Check(observer.Viewmodel.Source == gun && observer.Viewmodel.Model!.FindChild("AnimationPlayer", true, false) is AnimationPlayer anim
			&& anim.AssignedAnimation == "ak47_reload", "weapon replacement selects and samples replicated reload animation");
		var ammo = gun.MagazineAmmo; var shots = gun.ShotsFired;
		await Screenshot("observer_rifle"); await Frames(8);
		Check(gun.MagazineAmmo == ammo && gun.ShotsFired == shots && !gun.IsProcessing(), "rendering does not simulate or fire the observed weapon");
		var awp = first.Inventory.Weapons.OfType<FirearmView>().Single(w => w.Kind == FirearmView.FirearmKind.AWP);
		first.Inventory.EquipNetwork(awp); packet = first.CaptureNetworkState(); packet["zoom"] = 2; first.ApplyNetworkState(packet, true);
		await Frames();
		Check(awp.ZoomLevel == 2 && observer.Camera.Fov == first.Camera.Fov && observer.Camera.Fov < 15,
			"network zoom updates both camera FOV and weapon state");
		Check(_viewer.GetNode<CssScope>("HUD/Scope").Visible && !observer.Viewmodel.Visible && !_viewer.GetNode<Crosshair>("HUD/Crosshair").Visible,
			"sniper zoom shows scope and suppresses viewmodel and crosshair");
		await Screenshot("observer_scope");
		packet = first.CaptureNetworkState(); packet["zoom"] = 0; first.ApplyNetworkState(packet, true); await Frames();
		Check(observer.Viewmodel.Visible && observer.Viewmodel.Model!.Visible && !_viewer.GetNode<CssScope>("HUD/Scope").Visible,
			"unscoping restores the same weapon model");
		var motion = new Transform3D(Basis.Identity, new Vector3(.02f, -.01f, 0));
		packet = first.CaptureNetworkState(); packet["viewmodel_offset"] = motion; first.ApplyNetworkState(packet, true); await Frames();
		Check(first.ViewmodelBob.MotionOffset.IsEqualApprox(motion), "viewmodel movement survives the replicated snapshot");
		first.GrenadeVision.Flash(1, 1); await Frames();
		Check(_viewer.GrenadeVision.Visible && first.GrenadeVision.CaptureFlash().Z > .9f, "observer inherits target flash blindness");
		var flash = first.GrenadeVision.CaptureFlash(); first.GrenadeVision.ReceiveFlash(flash);
		Check(first.GrenadeVision.CaptureFlash().IsEqualApprox(flash), "replicated flash preserves current opacity and timing");
		first.GrenadeVision.Reset();
		var wall = new StaticBody3D { Position = new(0, 1.5f, 1) };
		wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(4, 3, .2f) } }); AddChild(wall);
		observer.ToggleMode(); await Frames();
		Check(observer.Mode == ObserverMode.Chase && !first.Body.ObservedFirstPerson && !observer.Viewmodel.Visible && !hud.Visible,
			"chase releases first-person body, weapon and HUD");
		Check(observer.Camera.GlobalPosition.DistanceTo(first.Camera.GlobalPosition) < .9f,
			"chase sphere cast retracts the camera before a wall");
		wall.QueueFree();
		observer.ToggleMode(); await Frames();
		Check(observer.FirstPerson && observer.Target == first, "mode switch preserves selected player");
		first.SetVitals(0, 0, false); await Frames();
		Check(observer.FirstPerson && observer.Target == second && !first.Body.ObservedFirstPerson, "death automatically selects another teammate");
		second.QueueFree(); await Frames();
		Check(!observer.Following && observer.Target == null && !observer.Viewmodel.Visible, "disconnecting final teammate clears observation without revealing enemies");
		Check(_viewer.JoinTeam(1), "dead player can join spectator team"); _viewer.SetMouseCaptured(true);
		Check(observer.Mode == ObserverMode.Free && _viewer.HasLocalControl, "spectator begins with free movement");
		Check(observer.Cycle(-1) && observer.Target == enemy, "spectator may select either team, including previous from no target");
		await Frames();
		var at = _viewer.GlobalPosition; var yaw = _viewer.Rotation.Y;
		_viewer._Input(new InputEventMouseMotion { ScreenRelative = new(30, 15), Relative = new(30, 15) });
		_viewer.Simulate(new(new(1, 0), false, false, false), .1f);
		Check(_viewer.GlobalPosition == at && _viewer.Rotation.Y == yaw, "following spectator cannot move or rotate a hidden free camera");
		observer.ToggleMode(); observer.ToggleMode(); await Frames();
		Check(observer.Mode == ObserverMode.Free && !observer.Following && !enemy.Body.ObservedFirstPerson && _viewer.HasLocalControl,
			"spectator cycles through chase back to free camera and restores bodies");
		_viewer.Simulate(new(new(1, 0), false, false, false), .1f);
		Check(_viewer.GlobalPosition != at, "free camera can move again");
		observer.Cycle(); await Frames();
		RoundState.Instance.BeginMatch("sandbox"); _viewer.JoinTeam(2); _viewer.JoinClass(1); await Frames();
		Check(_viewer.IsAlive && !observer.Following && observer.Target == null && !enemy.Body.ObservedFirstPerson && !observer.Viewmodel.Visible,
			"return to play releases all observer state");
		Check(_viewer.RenderCamera == _viewer.FirstPerson && _viewer.GetNode<CanvasLayer>("WeaponLayer").Visible && _viewer.Inventory.Enabled,
			"respawn restores own first-person camera and weapon input");
		// Release corpses and physical pickups as well as players before the audio
		// mixer exits; their collision sounds may still be playing in rendered runs.
		foreach (var child in GetChildren()) child.QueueFree();
		await Frames(); await Task.Delay(150);
	}
}
