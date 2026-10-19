using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.Gameplay;
using Contrapoint.Network;
using Contrapoint.Players;
using Contrapoint.UI;
using Contrapoint.Weapons;

public partial class BuyMenuChecks : Node
{
	private int _checks;
	private Player Player => LanSession.Instance!.LocalPlayer!;
	private void Check(bool condition, string message) { _checks++; if (!condition) throw new InvalidOperationException(message); }
	private async Task Until(Func<bool> condition, string message)
	{
		var end = Time.GetTicksMsec() + 60000;
		while (!condition()) { if (Time.GetTicksMsec() > end) throw new TimeoutException(message); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
	}
	private async Task Frames(int count = 3) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
	private void Key(Key code) => Player._Input(new InputEventKey { PhysicalKeycode = code, Pressed = true });
	private async Task Capture(string name, Vector2I size)
	{
		GetWindow().Mode = Window.ModeEnum.Windowed; GetWindow().Size = size;
		await Frames(6); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		Check(GetViewport().GetTexture().GetImage().SavePng("res://.godot/" + name + ".png") == Error.Ok, "saved " + name);
	}
	public override async void _Ready()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		Reparent(GetTree().Root); GetTree().CurrentScene = null;
		try
		{
			if (!OS.GetCmdlineUserArgs().Contains("--menu-test-profile")) throw new InvalidOperationException("Use --menu-test-profile to isolate saved presets.");
			if (FileAccess.FileExists("user://buy_presets_test.cfg")) DirAccess.RemoveAbsolute("user://buy_presets_test.cfg");
			MatchSession.Begin(new MatchSetup { ModeId = "sandbox", FallDamage = false });
			GetTree().ChangeSceneToFile("res://scenes/maps/dust2.tscn");
			await Until(() => LanSession.Instance!.LocalPlayer?.TeamMenu?.IsOpen == true, "map load");
			Player.JoinTeam(2); Player.JoinClass(1); Player.SetPhysicsProcess(false);
			Check(Player.Money == 16000 && !Player.Inventory.Weapons.Any(w => w.Slot == WeaponSlot.Primary)
				&& Player.Inventory.Weapons.OfType<FirearmView>().Single().Kind == FirearmView.FirearmKind.Glock
				&& !Player.Inventory.Weapons.OfType<GrenadeView>().Any(), "T starts with knife, Glock and $16000");
			Check(HudSnapshot.Capture(Player).CanBuy, "HUD shopping cart is available while alive");
			var menu = Player.BuyMenu!;
			menu.Open();
			await Frames();
			Check(ConsoleBindings.Keys["F1"] == "autobuy" && ConsoleBindings.Keys["F2"] == "rebuy", "CSS F1/F2 default bindings");
			foreach (var (suffix, name) in new[] { ("a", "RIFLE"), ("b", "SNIPER"), ("c", "SHOTGUN"), ("d", "SMG") })
				Check(menu.FindChild("buypresetbutton" + suffix, true, false) is Button { Disabled: false } preset && preset.Text.Contains(name), "stock default package " + name);
			Check(menu.FindChildren("*", "Button", true, false).OfType<Button>().All(button => button.Size.Y == 20), "button heights match original .res geometry");
			Check(menu.IsOpen && !Player.MouseCaptured && !Player.Inventory.Enabled && !GetTree().Paused, "buy menu releases input without pausing");
			Check(menu.FindChildren("*", "Button", true, false).OfType<Button>().Where(b => b.Name.ToString() is "submachineguns" or "shotguns" or "machineguns").All(b => !b.Disabled), "complete arsenal categories are enabled");
			foreach (var category in new[] { "submachineguns", "shotguns", "machineguns" })
			{
				menu.Open(category);
				Check(menu.Category == category && menu.PreviewItem.Length > 0, category + " loads original layout/card");
			}
			menu.Open();
			if (OS.GetCmdlineUserArgs().Contains("--buy-preview"))
			{
				await Capture("buy_main_720", new(1280, 720));
				menu.Open("pistols");
				(menu.FindChild("deserteagle", true, false) as Button)?.GrabFocus();
				await Capture("buy_pistols_720", new(1280, 720));
				menu.Open();
			}
			Key(Godot.Key.Key4);
			Check(menu.Category == "rifles" && menu.PreviewItem.Length > 0, "stock category hotkey opens rifle cards");
			if (OS.GetCmdlineUserArgs().Contains("--buy-preview"))
			{
				await Capture("buy_rifles_720", new(1280, 720));
				await Capture("buy_rifles_1080", new(1920, 1080));
				await Capture("buy_rifles_768", new(1024, 768));
				GetWindow().Size = new(1280, 720);
			}
			Key(Godot.Key.Key2);
			Check(!menu.IsOpen && Player.MouseCaptured && Player.Money == 13500 && Player.ActiveWeapon is FirearmView { Kind: FirearmView.FirearmKind.AK47 }, "keyboard buys AK, spends CSS price, closes and equips");
			Check(Player.Buy("m4a1") == "" && Player.Money == 10400 && Player.Inventory.Weapons.Count(w => w.Slot == WeaponSlot.Primary) == 1, "replacement uses a single primary slot");
			Check(GetTree().GetNodesInGroup("source_weapon_pickups").OfType<WeaponPickup>().Any(p => p.WeaponClass == "weapon_ak47"), "replaced gun drops in world");
			Player.Money = 0;
			Check(Player.Buy("awp").Length > 0 && Player.Money == 0 && Player.Inventory.Weapons.OfType<FirearmView>().Any(g => g.Kind == FirearmView.FirearmKind.M4A1), "insufficient funds leave money and inventory intact");
			Player.Money = 16000;
			Check(Player.Buy("mp5navy") == "" && Player.Money == 14500 && Player.ActiveWeapon is FirearmView { Kind: FirearmView.FirearmKind.MP5Navy }, "original MP5 is purchasable for $1500");
			Player.Money = 16000;
			Check(Player.Buy("awp") == "" && Player.Money == 11250, "AWP costs $4750");
			Check(Player.Buy("awp").Length > 0 && Player.Money == 11250, "duplicate firearm does not charge");
			Check(Player.Buy("hegrenade") == "" && Player.Buy("hegrenade").Length > 0 && Player.Money == 10950, "one HE maximum");
			Check(Player.Buy("flashbang") == "" && Player.Buy("flashbang") == "" && Player.Buy("flashbang").Length > 0 && Player.Money == 10550, "two flashes maximum");
			Check(Player.Buy("vest") == "" && Player.Money == 9900 && Player.Armor == 100 && !Player.HasHelmet, "kevlar costs $650");
			Check(Player.Buy("vesthelm") == "" && Player.Money == 9550 && Player.HasHelmet, "helmet upgrade costs $350");
			menu.Open("equipment");
			if (OS.GetCmdlineUserArgs().Contains("--buy-preview")) await Capture("buy_equipment_720", new(1280, 720));
			Player.TakeDamage(100);
			Check(!menu.IsOpen && Player.Money == 16000 && !HudSnapshot.Capture(Player).CanBuy, "death closes buy menu, hides cart and resets money to $16000");
			Player.Respawn();
			Check(Player.Money == 16000 && !Player.Inventory.Weapons.Any(w => w.Slot == WeaponSlot.Primary), "custom respawn preserves budget without free rifle");
			Player.Rebuy();
			Check(Player.Money < 16000 && Player.Inventory.Weapons.OfType<FirearmView>().Any(w => w.Kind == FirearmView.FirearmKind.AWP), "rebuy restores previous equipment");
			menu.Open(); Key(Godot.Key.Escape);
			Check(!menu.IsOpen && Player.Menu?.IsOpen == false && Player.MouseCaptured, "Escape closes buying without opening pause");
			menu.Open(); Player.Console!.SetOpen(true); Player.Console.SetOpen(false);
			Check(menu.IsOpen && !Player.MouseCaptured, "console cannot recapture mouse over buy menu");
			menu.Close();
			// Save the current AWP package, cancel once, then confirm and reload it.
			menu.Open();
			((Button)menu.FindChild("buypresetbuttonseta", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
			Check(menu.FindChild("SavePresetConfirmation", true, false) != null, "SAVE asks before replacing a favorite");
			if (OS.GetCmdlineUserArgs().Contains("--buy-preview")) await Capture("buy_save_confirmation_720", new(1280, 720));
			Key(Godot.Key.Escape);
			Check(menu.IsOpen && menu.FindChild("SavePresetConfirmation", true, false) == null, "cancel returns to the buy menu");
			((Button)menu.FindChild("buypresetbuttonseta", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
			Key(Godot.Key.Enter);
			var presets = new ConfigFile();
			Check(presets.Load("user://buy_presets_test.cfg") == Error.Ok && presets.GetValue("favorites_ter", "1", "").AsString().Contains("awp"), "confirmed preset persists under T profile");
			menu.Close();
			Player.TakeDamage(100); Player.Respawn();
			Key(Godot.Key.F2); Player._Input(new InputEventKey { PhysicalKeycode = Godot.Key.F2, Pressed = false });
			Check(Player.Inventory.Weapons.OfType<FirearmView>().Any(w => w.Kind == FirearmView.FirearmKind.AWP), "F2 restores previous equipment without opening shop");
			var cash = Player.Money;
			Player.Rebuy();
			Check(Player.Money == cash, "repeated package does not add or charge for extra grenades");
			Player.TakeDamage(100); Player.Respawn();
			menu.Open(); Key(Godot.Key.F1);
			Check(!menu.IsOpen && Player.Inventory.Weapons.OfType<FirearmView>().Any(w => w.Kind == FirearmView.FirearmKind.AK47) && Player.HasHelmet, "F1 auto-buys T rifle and armor from open shop");
			Player.TakeDamage(100); Player.Respawn();
			Player.Money = 1700;
			Key(Godot.Key.F1); Player._Input(new InputEventKey { PhysicalKeycode = Godot.Key.F1, Pressed = false });
			Check(Player.Inventory.Weapons.OfType<FirearmView>().Any(w => w.Kind == FirearmView.FirearmKind.MP5Navy) && Player.Money == 200, "F1 falls back to affordable CSS primary");
			menu.Open(); Key(Godot.Key.S);
			Check(!menu.IsOpen, "S purchases the first favorite");
			Player.JoinTeam(3); Player.JoinClass(5);
			Check(Player.Inventory.Weapons.OfType<FirearmView>().Single().Kind == FirearmView.FirearmKind.USP, "switching to CT grants USP");
			menu.Open(); Key(Godot.Key.S);
			Check(Player.Inventory.Weapons.OfType<FirearmView>().Any(w => w.Kind == FirearmView.FirearmKind.M4A1), "CT retains its default RIFLE preset after saving T favorite");
			Player.JoinTeam(2); Player.JoinClass(1);
			Check(Player.Inventory.Weapons.OfType<FirearmView>().Single().Kind == FirearmView.FirearmKind.Glock, "switching back to T grants Glock");
			var reloaded = new CssBuyMenu(); AddChild(reloaded); reloaded.Bind(Player); reloaded.Open();
			((Button)reloaded.FindChild("buypresetbuttona", true, false)).EmitSignal(BaseButton.SignalName.Pressed);
			Check(Player.Inventory.Weapons.OfType<FirearmView>().Any(w => w.Kind == FirearmView.FirearmKind.AWP), "new menu reloads saved T package from disk");
			reloaded.QueueFree();
			if (OS.GetCmdlineUserArgs().Contains("--buy-preview")) await Capture("buy_hud_cart_720", new(1280, 720));
			RoundState.Instance!.BeginMatch("deathmatch");
			Player.Money = 0;
			Check(Player.Buy("ak47") == "" && Player.Money == 0, "DM firearm is free with zero cash");
			Check(Player.Buy("hegrenade").Length > 0, "DM keeps its grenade restriction");
			Player.TakeDamage(100); Player.Respawn();
			Check(Player.Money == 0 && Player.Inventory.Weapons.OfType<FirearmView>().Any(w => w.Kind == FirearmView.FirearmKind.AK47), "DM respawn retains selected free rifle");
			RoundState.Instance.BeginMatch("teamdeathmatch");
			Check(Player.Buy("m4a1") == "" && Player.Money == 0, "TDM buying is also free");
			GD.Print($"BUY_CHECKS checks={_checks} failures=0"); GetTree().Quit();
		}
		catch (Exception error) { GD.PushError(error.ToString()); GD.Print($"BUY_CHECKS checks={_checks} failures=1"); GetTree().Quit(1); }
	}
}
