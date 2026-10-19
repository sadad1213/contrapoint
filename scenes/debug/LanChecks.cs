using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.Network;
using Contrapoint.Players;
using Contrapoint.UI;
using Contrapoint.Weapons;
using GDictionary = Godot.Collections.Dictionary;

// Run separate real ENet processes with --lan-role=host/client/late.
public partial class LanChecks : Node
{
	private string _role = "";
	private int _checks;
	private LanSession Net => LanSession.Instance!;
	private Player Player => Net.LocalPlayer!;
	private static string Root => "user://lan_checks/" + (OS.GetCmdlineUserArgs().FirstOrDefault(s => s.StartsWith("--lan-run="))?[10..] ?? "default") + "/";
	private static string Map => OS.GetCmdlineUserArgs().FirstOrDefault(s => s.StartsWith("--lan-map="))?[10..] ?? "dust2";
	private static bool MovementOnly => OS.GetCmdlineUserArgs().Contains("--lan-movement-only");
	private static bool CheatsOnly => OS.GetCmdlineUserArgs().Contains("--lan-cheats-only");
	private static bool WorldOnly => OS.GetCmdlineUserArgs().Contains("--lan-world-only");
	private void Check(bool value, string message)
	{
		_checks++; if (!value) throw new InvalidOperationException(message);
	}
	private async Task Until(Func<bool> condition, string message, double timeout = 40)
	{
		var end = Time.GetTicksMsec() + (ulong)(timeout * 1000);
		while (!condition())
		{
			if (Time.GetTicksMsec() > end) throw new TimeoutException(_role + ": " + message);
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
	}
	private async Task Delay(double seconds) => await ToSignal(GetTree().CreateTimer(seconds, true), SceneTreeTimer.SignalName.Timeout);
	private static void Write(string name, Variant value) { using var file = FileAccess.Open(Root + name, FileAccess.ModeFlags.Write); file.StoreVar(value); }
	private static Variant Read(string name) { using var file = FileAccess.Open(Root + name, FileAccess.ModeFlags.Read); return file.GetVar(); }
	private static bool Exists(string name) => FileAccess.FileExists(Root + name);
	private void Command(string text)
	{
		ConsoleCommands.Execute(text, Player.Movement, (line, error) => { GD.Print(line); if (error) throw new InvalidOperationException(line); });
	}
	public override async void _Ready()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		Reparent(GetTree().Root);
		GetTree().CurrentScene = null;
		try
		{
			_role = OS.GetCmdlineUserArgs().FirstOrDefault(s => s.StartsWith("--lan-role="))?[11..] ?? "host";
			DirAccess.MakeDirRecursiveAbsolute(Root);
			if (_role == "host") await Host(); else if (_role == "failure") await Failure(); else await Client();
			GD.Print($"LAN_CHECKS role={_role} checks={_checks} failures=0");
			Write(_role + "_passed", true); GetTree().Quit();
		}
		catch (Exception error) { GD.PushError(error.ToString()); GD.Print($"LAN_CHECKS role={_role} checks={_checks} failures=1"); Write(_role + "_failed", error.ToString()); Net.Stop(); GetTree().Quit(1); }
	}
	private async Task Failure()
	{
		GetTree().ChangeSceneToFile("res://scenes/main_menu.tscn"); await Delay(.1);
		var console = (DeveloperConsole)GetTree().CurrentScene.FindChild("DeveloperConsole", true, false);
		var errors = 0;
		ConsoleCommands.ExecuteInMenu("connect 127.0.0.1:99999", console, (_, error) => { if (error) errors++; });
		Check(errors == 1 && !Net.IsActive, "invalid connect must leave networking offline");
		ConsoleCommands.ExecuteInMenu("connect 127.0.0.1:27999", console, (line, _) => GD.Print(line));
		Check(Net.IsClient, "connection attempt starts");
		await Until(() => !Net.IsActive, "unreachable host timeout", 16);
		await Until(() => GetTree().CurrentScene?.FindChild("DeveloperConsole", true, false) is DeveloperConsole { IsOpen: true }, "timeout returns to visible console");
		Check(!GetTree().Paused, "failed connect cannot leave menu paused");
	}
	private void CheckHostMovementSmoothing(Player remote)
	{
		var original = remote.CaptureNetworkState();
		var state = remote.CaptureNetworkState();
		state["yaw"] = 3.1f;
		remote.ApplyNetworkState(state, true);
		var start = remote.GlobalTransform;
		var localHead = start.AffineInverse() * remote.Body.Hitboxes.GroupCenter(SourceHitGroup.Head);
		var localEyes = start.AffineInverse() * remote.Camera.GlobalPosition;
		// Feed 30 Hz poses and render four frames per packet, including a yaw wrap.
		for (var packet = 1; packet <= 10; packet++)
		{
			var position = start.Origin + Vector3.Right * (.15f * packet);
			var yaw = Mathf.Wrap(3.1f + .04f * packet, -Mathf.Pi, Mathf.Pi);
			state["position"] = position; state["yaw"] = yaw;
			var beforePacket = remote.Body.GlobalTransform;
			remote.ApplyNetworkState(state, false);
			Check(remote.Body.GlobalTransform.IsEqualApprox(beforePacket), "host model must not jump when a movement packet arrives");
			Check(remote.GlobalPosition.IsEqualApprox(position), "host collision must immediately use the received position");
			Check(remote.Camera.GlobalPosition.IsEqualApprox(remote.GlobalTransform * localEyes), "host gameplay eyes must use the received pose");
			Check(remote.CaptureNetworkState()["position"].AsVector3().IsEqualApprox(position), "host must relay the received position without rendering delay");
			var head = remote.Body.Hitboxes.GroupCenter(SourceHitGroup.Head);
			Check(head.DistanceTo(remote.GlobalTransform * localHead) < .0002f, "host hitboxes must follow the received pose while the model is smoothed");
			Check(remote.Body.Hitboxes.Trace(head + Vector3.Up, head, out _, out _, out _), "host bullet traces must still hit the authoritative head");
			for (var frame = 0; frame < 4; frame++)
			{
				var beforeFrame = remote.Body.GlobalPosition;
				var distance = beforeFrame.DistanceTo(position);
				remote._Process(1.0 / 120);
				Check(remote.Body.GlobalPosition.DistanceTo(position) < distance
					&& remote.Body.GlobalPosition.DistanceTo(beforeFrame) < .15f,
					"host must advance the model smoothly on every render frame between packets");
				Check(Mathf.Abs(Mathf.AngleDifference(beforePacket.Basis.GetEuler().Y, remote.Body.GlobalRotation.Y)) < .5f,
					"host model must turn smoothly across the yaw wrap");
				Check(remote.Body.Hitboxes.GroupCenter(SourceHitGroup.Head).DistanceTo(head) < .0002f,
					"render smoothing must not move host hitboxes");
			}
		}
		state["position"] = remote.GlobalPosition + Vector3.Up * 8;
		remote.ApplyNetworkState(state, false);
		Check(remote.Body.Transform.IsEqualApprox(Transform3D.Identity), "large corrections must discard host smoothing history");
		remote.ApplyNetworkState(original, true);
		Check(remote.Body.Transform.IsEqualApprox(Transform3D.Identity), "explicit snaps must discard host smoothing history");
	}
	private async Task Host()
	{
		SourceDamageRules.FriendlyFire = false; // Initial shared-team fixture isolates action replication.
		GetTree().ChangeSceneToFile($"res://scenes/maps/{Map}.tscn");
		await Until(() => Net.LocalPlayer is { } p && GodotObject.IsInstanceValid(p) && p.GetTree().CurrentScene?.SceneFilePath.EndsWith(Map + ".tscn") == true, "host map");
		await Delay(.25);
		Player.JoinTeam(2); Player.JoinClass(1);
		Check(LanSession.TryEndpoint("26.1.2.3:27016", out var address, out var port) && address == "26.1.2.3" && port == 27016, "IPv4 endpoint");
		Check(!LanSession.TryEndpoint("127.0.0.1:99999", out _, out _), "reject invalid port");
		Check(ConsoleCommands.GetCompletions("LAN").Contains("lan"), "LAN completion");
		Command("LAN 1");
		Check(Net.IsHosting, "LAN opened through console");
		Player.Menu!.SetOpen(true); Check(!GetTree().Paused, "multiplayer menu cannot pause host"); Player.Menu.SetOpen(false);
		Player.SetPhysicsProcess(false);
		var world = GetTree().CurrentScene;
		var prop = world.FindChildren("*", "RigidBody3D", true, false).OfType<RigidBody3D>().FirstOrDefault();
		var baseline = new GDictionary { ["spawn"] = Player.GlobalPosition };
		if (prop != null)
		{
			prop.Freeze = true; prop.GlobalPosition += Vector3.Up * .4f;
			baseline["path"] = world.GetPathTo(prop).ToString(); baseline["position"] = prop.GlobalPosition;
		}
		if (Map == "inferno")
		{
			var door = world.GetNode<Node3D>("Inferno/brush_2"); door.Call("source_input", "Open");
			door.Get("source_data").AsGodotDictionary()["wait"] = "-1";
			var glass = world.FindChildren("*", "AnimatableBody3D", true, false).OfType<Node3D>().First(n => n.Get("source_data").AsGodotDictionary()["classname"].AsString() == "func_breakable_surf");
			glass.Call("source_damage", 10000, glass.GlobalPosition, Vector3.Zero);
			baseline["glass"] = world.GetPathTo(glass).ToString(); baseline["door"] = world.GetPathTo(door).ToString();
			baseline["door_open"] = true;
			baseline["removed"] = "Inferno/brush_14"; world.GetNode("Inferno/brush_14").QueueFree();
			await Delay(.7);
		}
		if (Map == "de_cpl_strike")
		{
			var boards = world.GetNode<Node3D>("Strike/brush_15");
			boards.Call("source_damage", 20, boards.GlobalPosition, Vector3.Zero);
			baseline["breakable"] = world.GetPathTo(boards).ToString();
		}
		Write("baseline", baseline);
		Write("host_ready", true);
		await Until(() => Net.Players.Count >= 2 && Net.Players.Values.Any(p => p.AgentId == "leet"), "first player pose");
		var remote = Net.Players.Values.First(p => p != Player);
		if (CheatsOnly)
		{
			Write("pose_verified", true);
			await HostCheats(remote);
			Net.Stop();
			return;
		}
		Check(remote.Movement.IsDucked && Mathf.Abs(remote.AimPitch - .35f) < .01f, "crouch and aim pitch");
		Check(remote.ActiveWeapon is FirearmView, "equipped firearm");
		Check(Player.Health == 100 && remote.Health == 100, "players initially alive");
		CheckHostMovementSmoothing(remote);
		if (MovementOnly)
		{
			Write("pose_verified", true);
			var previousPosition = remote.GlobalPosition;
			var previousBody = remote.Body.GlobalPosition;
			var advancedBetweenPackets = false;
			await Until(() =>
			{
				advancedBetweenPackets |= remote.GlobalPosition.IsEqualApprox(previousPosition)
					&& !remote.Body.GlobalPosition.IsEqualApprox(previousBody);
				previousPosition = remote.GlobalPosition;
				previousBody = remote.Body.GlobalPosition;
				return Exists("movement_client_verified");
			}, "client movement stream");
			Check(advancedBetweenPackets, "host must render movement between actual ENet pose packets");
			Net.Stop();
			return;
		}
		if (prop != null) { prop.GlobalPosition += Vector3.Right; baseline["position"] = prop.GlobalPosition; }
		if (baseline.ContainsKey("door")) { world.GetNode(baseline["door"].AsString()).Call("source_input", "Close"); baseline["door_open"] = false; }
		Write("baseline", baseline);
		Write("pose_verified", true);
		if (WorldOnly)
		{
			await Until(() => Exists("world_client_verified"), "client receives live world changes");
			Write("late_ready", true);
			await Until(() => Exists("late_verified"), "late join world state");
			Check(Net.Players.Count == 3, "world state reaches both clients");
			await Until(() => Exists("late_disconnected"), "late world client disconnects");
			Net.Stop();
			return;
		}
		await Until(() => remote.Body.UpperClip.Contains("Reload"), "replicated reload animation");
		Write("reload_verified", true);
		await Until(() => Exists("client_effects"), "client effects");
		Check(remote.Combat.BulletsResolved > 0 && Player.Health == 100, "host resolves client shots with default friendly fire disabled");
		Check(world.FindChildren("*", "", true, false).OfType<GrenadeSmoke>().Any(), "client smoke simulated on host");
		remote.Armor = 100; remote.HasHelmet = true; remote.TakeHeDamage(80, null);
		await Until(() => Exists("vitals_received"), "client receives HE health and armor");
		await Delay(.2);
		Check(remote.Health == 60 && remote.Armor == 80, "client pose snapshots cannot overwrite host health or armor");
		Net.BroadcastVitals(remote); Write("fall_ready", true);
		await Until(() => remote.Health == 10, "client landing damage resolved by host");
		remote.TakeHeDamage(40, null);
		await Until(() => Exists("death_received"), "client receives lethal damage");
		await Until(() => Exists("respawn_received"), "client respawn");
		Check(remote.Health == 100 && remote.Armor == 0, "host owns respawn vitals");
		// Open air above the map isolates actual peer shots from map cover.
		Player.GlobalPosition += Vector3.Up * 40;
		Player.Team = PlayerTeam.Terrorist;
		remote.Team = PlayerTeam.CounterTerrorist;
		remote.SourceTeleport(Player.GlobalPosition + Vector3.Forward * 2, Mathf.Pi, 0);
		remote.SetVitals(100, 100, true); Net.BroadcastVitals(remote);
		await Delay(.3);
		Write("pvp_ready", new GDictionary { ["target"] = Player.Body.Hitboxes.GroupCenter(SourceHitGroup.Chest) });
		await Until(() => Player.Health < 100, "client bullet damages opposing host player");
		Check(Player.LastDamageKind == PlayerDamageKind.Bullet && Player.Recoil.DamagePunchDegrees.X < 0,
			"host receives authoritative enemy bullet damage and unarmored aim punch");
		await Until(() => Exists("pvp_fired"), "client shot request completed");
		var pvpGun = Player.Inventory.Weapons.OfType<FirearmView>().First(g => g.Kind == FirearmView.FirearmKind.AK47);
		var pvpAim = new Transform3D(Basis.Identity, Player.Camera.GlobalPosition).LookingAt(remote.Body.Hitboxes.GroupCenter(SourceHitGroup.Chest));
		Player.Combat.ResolveNetworkAttack(pvpGun, pvpAim, false, false);
		await Until(() => Exists("pvp_received"), "client receives host bullet and damage feedback");
		Check(remote.Health == 73 && remote.Armor == 95, "host owns enemy AK armor and health calculation at two metres");
		Player.SetVitals(100, 100, true); Net.BroadcastVitals(Player); Player.Rotation = Vector3.Zero;
		remote.SourceTeleport(Player.GlobalPosition + Vector3.Back * .75f, 0, 0);
		await Delay(.3); Write("knife_ready", true);
		await Until(() => !Player.IsAlive, "client rear knife attack kills armored host");
		Check(Player.LastDamageKind == PlayerDamageKind.Knife && Player.LastHealthDamage == 165,
			"host knife backstab uses 195 raw damage and stock armor ratio");
		await Until(() => Exists("knife_received"), "client receives knife death feedback");
		Player.Respawn();
		Command("sv_falldamage 0; mp_friendlyfire 1"); Write("damage_rules_ready", true);
		await Until(() => Exists("damage_rules_received"), "server damage rules replicated");
		await HostCheats(remote);
		Command("sv_falldamage 1; mp_friendlyfire 0");
		Write("late_ready", true);
		await Until(() => Exists("late_verified"), "late join state");
		Check(Net.Players.Count == 3, "three concurrent peers");
		// A server teleport must survive subsequently queued pose packets.
		remote.SourceTeleport(Player.GlobalPosition + Vector3.Up * 3, .7f, .2f);
		Check(remote.Body.Transform.IsEqualApprox(Transform3D.Identity), "server teleports must immediately reset the rendered model");
		await Until(() => Exists("teleport_verified"), "server teleport");
		await Until(() => Exists("late_disconnected"), "peer disconnect");
		await Until(() => Net.Players.Count == 2, "disconnect roster cleanup");
		Command("lan 0"); Check(!Net.IsActive && Net.Players.Count == 0, "LAN close cleanup");
		Write("closed", true);
		await Until(() => Exists("client_closed"), "client handles server close");
		Command("lan 1"); Check(Net.IsHosting, "reopen same UDP port"); Command("lan 0");
	}
	private async Task Client()
	{
		await Until(() => Exists(_role == "late" ? "late_ready" : "host_ready"), "host ready", 80);
		// Exercise the menu console command path, including automatic map loading.
		GetTree().ChangeSceneToFile("res://scenes/main_menu.tscn"); await Delay(.1);
		var console = GetTree().CurrentScene.FindChild("DeveloperConsole", true, false) as DeveloperConsole;
		Check(console != null, "menu console exists");
		ConsoleCommands.ExecuteInMenu("connect 127.0.0.1:" + LanSession.Port, console!, (line, error) => { GD.Print(line); if (error) throw new Exception(line); });
		await Until(() => Net.WorldReady, "network ready", 80);
		Player.JoinTeam(2);
		await Until(() => Player.PickingClass, "team selection");
		Player.JoinClass(1);
		await Until(() => Player.IsAlive && !Player.AwaitingInitialClass, "class selection and spawn");
		Player.SetPhysicsProcess(false);
		Check(GetTree().CurrentScene.SceneFilePath == $"res://scenes/maps/{Map}.tscn", "server map loaded");
		var baseline = Read("baseline").AsGodotDictionary();
		var prop = baseline.ContainsKey("path") ? GetTree().CurrentScene.GetNode<RigidBody3D>(baseline["path"].AsString()) : null;
		if (prop != null) Check(prop.GlobalPosition.DistanceTo(baseline["position"].AsVector3()) < .01f && prop.Freeze, "initial prop transform and client authority");
		if (baseline.ContainsKey("breakable"))
		{
			var boards = GetTree().CurrentScene.GetNode<Node3D>(baseline["breakable"].AsString());
			Check(boards.Get("broken").AsBool() && !boards.Visible && ((CollisionObject3D)boards).CollisionLayer == 0,
				"broken boards and their collision must replicate to joining players");
		}
		if (baseline.ContainsKey("glass"))
		{
			var glass = GetTree().CurrentScene.GetNode<Node3D>(baseline["glass"].AsString());
			Check(glass.Get("broken").AsBool() && !glass.Visible && ((CollisionObject3D)glass).CollisionLayer == 0, "broken glass is replicated on join");
			var door = GetTree().CurrentScene.GetNode<Node3D>(baseline["door"].AsString());
			Check(Mathf.Abs(door.Get("progress").AsSingle() - (baseline["door_open"].AsBool() ? 1 : 0)) < .01f, "door state is replicated on join");
			await Until(() => GetTree().CurrentScene.GetNodeOrNull(baseline["removed"].AsString()) == null, "deleted entity tombstone");
		}
		if (_role == "late")
		{
			await Until(() => Net.Players.Count == 3 && Net.Players.Values.Any(p => p != Player && p.NetworkPeerId != 1 && p.ActiveWeapon is FirearmView), "late roster");
			if (!WorldOnly) await Until(() => GetTree().CurrentScene.FindChildren("*", "", true, false).OfType<GrenadeSmoke>().Any(), "late smoke");
			Check(Net.Players.Values.First(p => p != Player && p.NetworkPeerId != 1).ActiveWeapon is FirearmView, "late weapon state");
			Write("late_verified", true); await Delay(.5);
			Command("disconnect"); await Delay(.25); Write("late_disconnected", true); return;
		}
		Check(Net.Players.Count == 2, "host and local player roster");
		Player.SourceTeleport(baseline["spawn"].AsVector3() + Vector3.Up * 1, .5f, .35f);
		Player.Movement.ApplyNetworkDuck(true);
		Player.SetAgent("leet");
		Player.Inventory.Equip(Player.Inventory.Weapons.OfType<FirearmView>().First());
		await Until(() => Exists("pose_verified"), "host sees player state");
		if (CheatsOnly)
		{
			await ClientCheats();
			await Until(() => !Net.IsActive, "host finishes client cheat checks");
			return;
		}
		if (MovementOnly)
		{
			for (var packet = 0; packet < 20; packet++)
			{
				Player.GlobalPosition += Vector3.Right * .08f;
				await Delay(1.0 / 30);
			}
			Write("movement_client_verified", true);
			await Until(() => !Net.IsActive, "host finishes movement checks");
			Check(!Net.IsActive, "movement check connection closes cleanly");
			return;
		}
		var updated = Read("baseline").AsGodotDictionary();
		if (prop != null) await Until(() => prop.GlobalPosition.DistanceTo(updated["position"].AsVector3()) < .01f, "live prop movement");
		if (updated.ContainsKey("door")) await Until(() => GetTree().CurrentScene.GetNode(updated["door"].AsString()).Get("progress").AsSingle() < .01f, "live door movement");
		Check(true, "world changes after joining");
		if (WorldOnly)
		{
			Write("world_client_verified", true);
			await Until(() => !Net.IsActive, "host finishes world checks");
			return;
		}
		Player.ActiveWeapon!._Process(2);
		Player.ActiveWeapon.HandleCommand("attack", true);
		Player.ActiveWeapon.HandleCommand("attack", false);
		await Delay(.2); // A full magazine cannot reload; consume a real round first.
		Player.ActiveWeapon.HandleCommand("reload", true);
		await Until(() => Exists("reload_verified"), "host sees weapon animation");
		Player.Combat.QueueBullet(Player.ActiveWeapon!, 30);
		await Delay(.1);
		var grenade = Player.Inventory.Weapons.OfType<GrenadeView>().First(g => g.Kind == GrenadeKind.Smoke);
		Net.RequestGrenade(grenade, true);
		await Until(() => GetTree().CurrentScene.FindChildren("*", "", true, false).OfType<GrenadeSmoke>().Any(), "replicated smoke", 15);
		Check(Player.Health == 100, "Smoke does not damage LAN players");
		Write("client_effects", true);
		await Until(() => Player.Health == 60 && Player.Armor == 80 && Player.HasHelmet, "host HE vitals reach owning client");
		Player.SetVitals(100, 100, true); Write("vitals_received", true);
		await Until(() => Exists("fall_ready") && Player.Health == 60, "host restores authoritative vitals");
		Net.RequestFallDamage(Player, 788);
		await Until(() => !Player.IsAlive, "host death reaches owner");
		Check(!Player.Inventory.Enabled, "network death disables owning player's weapons");
		await Until(() => Player.KillFeed!.Entries.Any(e => e.Notice.VictimId == Player.NetworkPeerId), "reliable kill-feed notice reaches the owning client");
		Check(Player.Death.Active && Player.Death.Ragdoll?.Bones.Count == 16 && Player.Body.WorldWeapon == null,
			"reliable death creates a physical corpse without a floating weapon on the owning client");
		Write("death_received", true);
		await Until(() => Player.Death.CanRespawn, "death-camera minimum respawn delay"); Player.Respawn();
		await Until(() => Player.IsAlive && Player.Armor == 0 && Player.Inventory.Enabled, "network respawn restores gameplay");
		Write("respawn_received", true);
		await Until(() => Exists("pvp_ready"), "enemy damage fixture");
		await Delay(.3);
		Player.Team = PlayerTeam.CounterTerrorist;
		var pvpGun = Player.Inventory.Weapons.OfType<FirearmView>().First(g => g.Kind == FirearmView.FirearmKind.AK47);
		Player.Inventory.Equip(pvpGun);
		var pvpAim = new Transform3D(Basis.Identity, Player.Camera.GlobalPosition).LookingAt(Read("pvp_ready").AsGodotDictionary()["target"].AsVector3());
		Net.RequestAttack(Player, pvpGun, pvpAim, false, false); Write("pvp_fired", true);
		await Until(() => Player.Health == 73 && Player.LastDamageKind == PlayerDamageKind.Bullet, "owning client receives enemy bullet damage");
		Check(Player.LastDamageSound == "player.damagekevlar" && Mathf.IsEqualApprox(Player.Movement.DamageSpeedModifier, .65f)
			&& Player.Recoil.DamagePunchDegrees == Vector3.Zero, "damage sound, tagging and armor protection arrive together");
		Write("pvp_received", true);
		await Until(() => Exists("knife_ready"), "knife fixture"); await Delay(.3);
		var pvpKnife = Player.Inventory.Weapons.First(w => w is KnifeView); Player.Inventory.Equip(pvpKnife);
		Net.RequestAttack(Player, pvpKnife, new Transform3D(Basis.Identity, Player.Camera.GlobalPosition), true, true);
		await Until(() => !Net.Players[1].IsAlive && Net.Players[1].LastDamageKind == PlayerDamageKind.Knife, "knife backstab feedback reaches attacker");
		Check(Net.Players[1].LastHealthDamage == 165, "replicated knife damage is not applied twice");
		await Until(() => Player.KillFeed!.Entries.Any(e => e.Notice.VictimId == 1 && e.Notice.Weapon == "knife"), "authoritative knife kill appears in the attacker's feed");
		Check(Player.KillFeed!.Entries.Count(e => e.Notice.VictimId == 1 && e.Notice.Weapon == "knife") == 1, "death and vitals RPCs do not duplicate kill-feed entries");
		Player.Inventory.Equip(pvpGun); Write("knife_received", true);
		await Until(() => Exists("damage_rules_ready") && !SourceDamageRules.FallDamageEnabled && SourceDamageRules.FriendlyFire, "host damage cvars reach client");
		var damageRuleErrors = 0;
		ConsoleCommands.Execute("mp_friendlyfire 0", Player.Movement, (_, error) => { if (error) damageRuleErrors++; });
		Check(damageRuleErrors == 1 && SourceDamageRules.FriendlyFire, "client console cannot override server damage rules");
		Write("damage_rules_received", true);
		await ClientCheats();
		var revision = Player.NetworkTeleportRevision;
		await Until(() => Player.NetworkTeleportRevision > revision, "server teleports local player");
		await Delay(.3);
		Check(Mathf.Abs(Player.AimPitch - .2f) < .01f, "server teleport pitch");
		Write("teleport_verified", true);
		await Until(() => Exists("closed") && !Net.IsActive, "server closes LAN");
		await Until(() => GetTree().CurrentScene.SceneFilePath == "res://scenes/main_menu.tscn", "server close returns to menu");
		Write("client_closed", true);
	}
	private async Task HostCheats(Player remote)
	{
		var existingPickups = GetTree().GetNodesInGroup("source_weapon_pickups").OfType<WeaponPickup>().Select(p => p.GetInstanceId()).ToHashSet();
		Command("sv_cheats 1"); Write("client_cheats_ready", true);
		await Until(() => remote.GodMode, "client god command enables server-side invulnerability");
		var protectedHealth = remote.Health;
		remote.EndSpawnProtection(); remote.TakeHeDamage(20, null);
		Check(remote.Health == protectedHealth && !Player.GodMode, "client god protects the requester without enabling god for the host");
		Write("client_god_verified", true);
		await Until(() => !remote.GodMode, "client god command toggles off on the server");
		Write("client_god_off_verified", true);
		await Until(() => remote.GodMode, "client god 1 enables server-side invulnerability");
		await Until(() => GetTree().GetNodesInGroup("source_weapon_pickups").OfType<WeaponPickup>()
			.Any(p => p.WeaponClass == "weapon_awp" && !existingPickups.Contains(p.GetInstanceId())), "client ent_create spawns a server weapon");
		var createdPickup = GetTree().GetNodesInGroup("source_weapon_pickups").OfType<WeaponPickup>()
			.First(p => p.WeaponClass == "weapon_awp" && !existingPickups.Contains(p.GetInstanceId()));
		Check(createdPickup.GlobalPosition.DistanceTo(remote.Camera.GlobalPosition) < 3, "client ent_create uses the requesting player's aim");
		createdPickup.Freeze = true;
		await Until(() => Exists("client_entity_verified"), "created weapon replicates to the client");
		Command("sv_cheats 0");
		Check(!remote.GodMode, "disabling cheats clears client god mode on the server");
		await Until(() => Exists("client_cheats_verified"), "client observes cheat reset and command restrictions");
	}
	private async Task ClientCheats()
	{
		await Until(() => Exists("client_cheats_ready") && SourceDamageRules.Cheats, "host enables client cheats");
		Command("GoD");
		await Until(() => Player.GodMode && Exists("client_god_verified"), "server confirms client god mode");
		Command("god");
		await Until(() => !Player.GodMode && Exists("client_god_off_verified"), "server confirms client god toggle off");
		var awpCount = GetTree().GetNodesInGroup("source_weapon_pickups").OfType<WeaponPickup>().Count(p => p.WeaponClass == "weapon_awp");
		Command("god 1; ENT_CREATE weapon_awp");
		await Until(() => Player.GodMode && GetTree().GetNodesInGroup("source_weapon_pickups").OfType<WeaponPickup>()
			.Count(p => p.WeaponClass == "weapon_awp") > awpCount, "server god confirmation and created weapon replica");
		Check(GetTree().GetNodesInGroup("source_weapon_pickups").OfType<WeaponPickup>().Where(p => p.WeaponClass == "weapon_awp")
			.All(p => p.NetworkReplica), "client ent_create receives server-owned weapon entities");
		Write("client_entity_verified", true);
		await Until(() => !SourceDamageRules.Cheats && !Player.GodMode, "host disables client cheats and clears god mode");
		var cheatErrors = 0;
		ConsoleCommands.Execute("god; ent_create weapon_awp", Player.Movement, (_, error) => { if (error) cheatErrors++; });
		Check(cheatErrors == 2, "client god and ent_create still require sv_cheats 1");
		Write("client_cheats_verified", true);
	}

}
