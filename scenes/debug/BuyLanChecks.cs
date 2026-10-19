using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.Gameplay;
using Contrapoint.Network;
using Contrapoint.Players;
using Contrapoint.Weapons;

public partial class BuyLanChecks : Node
{
	private int _checks;
	private string _role = "";
	private LanSession Net => LanSession.Instance!;
	private Player Player => Net.LocalPlayer!;
	private static string Arg(string name, string fallback) => OS.GetCmdlineUserArgs().FirstOrDefault(s => s.StartsWith(name + "="))?[(name.Length + 1)..] ?? fallback;
	private static string Root => "user://buy_checks/" + Arg("--buy-run", "default") + "/";
	private static bool Exists(string name) => FileAccess.FileExists(Root + name);
	private static void Write(string name) { using var file = FileAccess.Open(Root + name, FileAccess.ModeFlags.Write); file.StoreString("ok"); }
	private void Check(bool condition, string message) { _checks++; if (!condition) throw new InvalidOperationException(message); }
	private async Task Until(Func<bool> condition, string message)
	{
		var end = Time.GetTicksMsec() + 60000;
		while (!condition()) { if (Time.GetTicksMsec() > end) throw new TimeoutException(_role + ": " + message); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
	}
	private async Task Delay(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
	private async Task Join()
	{
		Net.Connect("127.0.0.1:" + LanSession.Port, (line, _) => GD.Print(line));
		await Until(() => Net.WorldReady && Net.LocalPlayer?.TeamMenu?.IsOpen == true, "connected");
		Player.JoinTeam(2);
		await Until(() => Player.Team == PlayerTeam.Terrorist && Player.TeamMenu!.IsClassSelection, "team selection");
		Player.JoinClass(1);
		await Until(() => Player.IsAlive && Player.TeamMenu?.IsOpen != true, "spawn");
		if (!DeathmatchRules.Active)
			await Until(() => !Player.Inventory.Weapons.Any(w => w.Slot == WeaponSlot.Primary)
				&& Player.Inventory.Weapons.OfType<FirearmView>().SingleOrDefault()?.Kind == FirearmView.FirearmKind.Glock, "authoritative T starting inventory");
		Player.SetPhysicsProcess(false);
	}
	public override async void _Ready()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		Reparent(GetTree().Root); GetTree().CurrentScene = null;
		_role = Arg("--buy-role", "host"); DirAccess.MakeDirRecursiveAbsolute(Root);
		try
		{
			if (_role == "host") await Host(); else if (_role == "late") await Late(); else await Client();
			GD.Print($"BUY_LAN_CHECKS role={_role} checks={_checks} failures=0"); Write(_role + "_passed"); GetTree().Quit();
		}
		catch (Exception error) { GD.PushError(error.ToString()); GD.Print($"BUY_LAN_CHECKS role={_role} checks={_checks} failures=1"); GetTree().Quit(1); }
	}
	private async Task Host()
	{
		MatchSession.Begin(new MatchSetup { ModeId = "sandbox", FallDamage = false });
		GetTree().ChangeSceneToFile("res://scenes/maps/dust2.tscn");
		await Until(() => Net.LocalPlayer?.TeamMenu?.IsOpen == true, "host map");
		Player.JoinTeam(2); Player.JoinClass(1); Player.SetPhysicsProcess(false);
		Player.SourceTeleport(new(100, 100, 100), 0, 0);
		Net.Open((line, _) => GD.Print(line)); Write("ready");
		await Until(() => Net.Players.Count == 2 && Exists("bought"), "client purchase");
		var remote = Net.Players.Values.Single(p => !p.LocalInput);
		Check(remote.Money == 11250 && remote.Inventory.Weapons.OfType<FirearmView>().Any(w => w.Kind == FirearmView.FirearmKind.AWP), "server validates purchase against its own balance");
		await Until(() => Exists("rejections"), "client invalid requests");
		Check(remote.Money == 11250 && remote.Inventory.Weapons.Count(w => w.Slot == WeaponSlot.Primary) == 1, "unsupported and duplicate requests do not charge or duplicate");
		remote.TakeDamage(100); Write("killed");
		await Until(() => Exists("dead_checked"), "client death budget");
		Check(remote.Money == 16000 && !remote.Inventory.Weapons.Any(w => w.Slot == WeaponSlot.Primary), "dead player cannot buy and server resets balance");
		remote.Respawn();
		await Until(() => Exists("custom_done"), "client respawn purchase");
		Check(remote.Money == 12900 && remote.Inventory.Weapons.OfType<FirearmView>().Any(w => w.Kind == FirearmView.FirearmKind.M4A1), "custom respawn purchase spends server money");
		RoundState.Instance!.BeginMatch("deathmatch"); Net.BroadcastDamageRules();
		await Until(() => Exists("dm_bought"), "client free purchase");
		Check(remote.Money == 12900 && remote.Inventory.Weapons.OfType<FirearmView>().Any(w => w.Kind == FirearmView.FirearmKind.AK47), "server gives DM weapon without charging");
		remote.TakeDamage(100); remote.Respawn(); Write("dm_respawned");
		await Until(() => Exists("dm_retained"), "client DM inventory");
		Write("late_join");
		await Until(() => Exists("late_ready"), "late join economy snapshot");
		Check(Net.Players.Count == 3 && remote.Money == 12900, "late join keeps authoritative economy");
		Write("done");
		await Until(() => Exists("client_passed") && Exists("late_passed"), "clients finish");
	}
	private async Task Client()
	{
		await Until(() => Exists("ready"), "host ready"); await Join();
		Check(Player.Money == 16000 && !Player.Inventory.Weapons.Any(w => w.Slot == WeaponSlot.Primary), "initial owner balance and custom inventory");
		Player.Money = 99999; // A forged client balance must never reach the server ledger.
		Player.BuyMenu!.Open("rifles"); Player.Buy("awp");
		await Until(() => Player.Money == 11250 && !Player.BuyMenu.IsOpen && Player.ActiveWeapon is FirearmView { Kind: FirearmView.FirearmKind.AWP }, "reliable inventory, money and menu result");
		Check(Player.MouseCaptured, "successful remote purchase recaptures input"); Write("bought");
		Net.RequestBuy(Player, "awp"); Net.RequestBuy(Player, "unknown_weapon");
		await Delay(.3); Check(Player.Money == 11250, "rejected requests keep owner balance"); Write("rejections");
		await Until(() => Exists("killed") && !Player.IsAlive && Player.Money == 16000, "death refills owner money");
		Net.RequestBuy(Player, "ak47"); await Delay(.3); Write("dead_checked");
		await Until(() => Player.IsAlive, "custom respawn");
		Player.BuyMenu.Open("rifles"); Player.Buy("m4a1");
		await Until(() => Player.Money == 12900 && !Player.BuyMenu.IsOpen, "paid respawn purchase"); Write("custom_done");
		await Until(() => DeathmatchRules.Active, "DM rules replicated");
		Player.BuyMenu.Open("rifles"); Player.Buy("ak47");
		await Until(() => !Player.BuyMenu.IsOpen && Player.Inventory.Weapons.OfType<FirearmView>().Any(w => w.Kind == FirearmView.FirearmKind.AK47), "free DM purchase");
		Check(Player.Money == 12900, "DM owner balance unchanged"); Write("dm_bought");
		await Until(() => Exists("dm_respawned") && Player.IsAlive && Player.Inventory.Weapons.OfType<FirearmView>().Any(w => w.Kind == FirearmView.FirearmKind.AK47), "DM selected inventory survives authoritative respawn");
		Check(Player.Money == 12900, "DM death does not reset paid balance"); Write("dm_retained");
		await Until(() => Exists("done"), "host done");
	}
	private async Task Late()
	{
		await Until(() => Exists("late_join"), "late join signal"); await Join();
		Check(DeathmatchRules.Active && Player.Money == 16000, "late join gets current mode and initial money");
		Check(Net.Players.Values.Any(p => !p.LocalInput && p.Money == 12900 && p.Inventory.Weapons.OfType<FirearmView>().Any(w => w.Kind == FirearmView.FirearmKind.AK47)), "late join gets spent remote balance and purchased weapon");
		Write("late_ready"); await Until(() => Exists("done"), "host done");
	}
}
