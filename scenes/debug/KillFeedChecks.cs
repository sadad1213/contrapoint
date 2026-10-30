using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.Players;
using Contrapoint.UI;
using Contrapoint.Weapons;

public partial class KillFeedChecks : Node3D
{
	private int _checks, _failures;
	private void Check(bool value, string message) { _checks++; if (!value) { _failures++; GD.PushError(message); } }
	private async Task Frames(int count = 2)
	{
		for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
	}
	private Player Spawn(string name, Vector3 position, bool local)
	{
		var player = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		player.Name = name; player.LocalInput = local; player.DebugControls = false; player.Position = position;
		AddChild(player); player.SetPhysicsProcess(false); return player;
	}
	public override async void _Ready()
	{
		try { await Run(); } catch (Exception error) { _failures++; GD.PushError(error.ToString()); }
		foreach (var child in GetChildren()) child.QueueFree();
		await Frames(); await Task.Delay(150);
		GD.Print($"KILLFEED_CHECKS checks={_checks} failures={_failures}"); GetTree().Quit(_failures == 0 ? 0 : 1);
	}
	private async Task Run()
	{
		GetWindow().Size = new(1280, 720);
		AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new(.22f, .24f, .27f) } });
		var floor = new StaticBody3D { Position = new(0, -.5f, 0) };
		floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(100, 1, 100) } }); AddChild(floor);
		var local = Spawn("Player", Vector3.Zero, true);
		var enemy = Spawn("Opponent", Vector3.Forward * 2, false); enemy.Team = PlayerTeam.CounterTerrorist;
		await Frames();
		for (var i = 0; i < 4; i++) { local.Simulate(default, 1f / 66); enemy.Simulate(default, 1f / 66); await Frames(1); }
		SourceDeathRules.AutoRespawn = false;
		var feed = local.KillFeed!;
		var awp = local.Inventory.Weapons.OfType<FirearmView>().First(g => g.Kind == FirearmView.FirearmKind.AWP);
		local.Inventory.Equip(awp);
		var aim = new Transform3D(Basis.Identity, local.Camera.GlobalPosition).LookingAt(enemy.Body.Hitboxes.GroupCenter(SourceHitGroup.Head));
		local.Combat.ResolveNetworkAttack(awp, aim, false, false); await Frames(3);
		Check(!enemy.IsAlive && feed.Entries.Count == 1, "One real lethal trace creates exactly one notice.");
		var first = feed.Entries.Last().Notice;
		Check(first.Weapon == "awp" && first.Flags.HasFlag(KillFlags.Headshot) && first.Flags.HasFlag(KillFlags.NoScope), "The lethal AWP trace retains headshot and no-scope metadata.");
		Check(first.KillerId == KillNotice.Identity(local) && first.VictimTeam == PlayerTeam.CounterTerrorist, "Names, teams and local highlight identities are captured by the authority.");
		enemy.SetVitals(0, 0, false); enemy.TakeDamage(500); await Frames();
		Check(feed.Entries.Count == 1, "Repeated dead vitals and damage do not duplicate a notice.");
		enemy.Respawn(); Check(feed.Entries.Count == 1, "Respawning preserves the feed.");
		local.Combat.ResolveNetworkAttack(awp, aim, false, false, scoped: true); await Frames(3);
		Check(!feed.Entries.Last().Notice.Flags.HasFlag(KillFlags.NoScope), "A scoped network attack does not gain the no-scope icon.");
		enemy.Respawn();
		local.TakeTraceDamage(195, enemy, SourceHitGroup.Generic, local.GlobalPosition + Vector3.Up, Vector3.Back, 1.7f, knife: true);
		Check(feed.Entries.Last().Notice.Weapon == "knife" && feed.Entries.Last().Notice.VictimId == KillNotice.Identity(local), "An enemy knife death reaches the local player's feed while dead.");
		local.Respawn();
		enemy.TakeHeDamage(400, local);
		Check(feed.Entries.Last().Notice.Weapon == "hegrenade", "HE uses its own icon even when the thrower holds an AWP.");
		local.ApplyFallDamage(1300);
		Check(feed.Entries.Last().Notice.KillerName == "" && feed.Entries.Last().Notice.Weapon == "suicide", "World/fall deaths have no fictional attacker.");
		local.Respawn(); enemy.Respawn();
		var cloud = new GrenadeSmoke { Position = new(0, 1, -1) }; AddChild(cloud); cloud.SetPhysicsProcess(false); cloud.SetNetworkAge(3); cloud._Process(.1);
		Check(KillNotice.ThroughSmoke(this, local.Camera.GlobalPosition, enemy.Camera.GlobalPosition)
			&& !KillNotice.ThroughSmoke(this, new(100, 1, 0), new(100, 1, -2)), "Smoke flags test the actual shot segment against active smoke volumes.");
		local.GrenadeVision.Flash(1, 1);
		Check(KillNotice.AttackFlags(local, awp).HasFlag(KillFlags.Blind), "A flashed shooter retains the blind-kill condition.");
		local.GrenadeVision.Reset();
		var packed = first.Pack(); Check(KillNotice.Unpack(packed) == first, "Reliable notice serialization preserves every field.");
		if (OS.GetCmdlineUserArgs().Contains("--killfeed-preview"))
		{
			local.Combat.ResolveNetworkAttack(awp, aim, false, false); await Frames(3);
			await Capture("killfeed_720");
			GetWindow().Size = new(1920, 1080); await Frames(3); await Capture("killfeed_1080");
		}
		for (var i = 0; i < 20; i++) feed.Add(first);
		Check(feed.Entries.Count == 16, "The imported CSSO entry_limit bounds the feed.");
		feed.Duration = 0; feed._Process(.01);
		Check(feed.Entries.Count == 0, "hud_deathnotice_time 0 immediately hides and clears existing notices.");
		feed.Add(first); Check(feed.Entries.Count == 0, "Disabled feed does not queue new notices.");
		feed.Duration = .05f; feed.Add(first); feed._Process(.06);
		Check(feed.Entries.Count == 0, "Notices expire during normal HUD processing.");
	}
	private async Task Capture(string name)
	{
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		using var image = GetViewport().GetTexture().GetImage(); image.SavePng("res://.godot/" + name + ".png");
		GD.Print("KILLFEED_PREVIEW " + name);
	}
}
