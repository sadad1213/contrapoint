using Godot;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Contrapoint.Gameplay;
using Contrapoint.Maps;
using Contrapoint.Players;
using Contrapoint.UI;

public partial class MenuChecks : Node
{
	private int _checks;
	private const string Dialog = "Interface/Modal/MapSelection";
	private void Check(bool condition, string message)
	{
		_checks++;
		if (!condition) throw new InvalidOperationException(message);
	}
	private async Task Frames(int count = 2)
	{
		for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}
	private void Key(Key code)
	{
		foreach (var pressed in new[] { true, false })
			GetViewport().PushInput(new InputEventKey { PhysicalKeycode = code, Keycode = code, Pressed = pressed });
	}
	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		CallDeferred(MethodName.RunChecks);
	}
	private async Task<Player> WaitForMap(string path)
	{
		var deadline = Time.GetTicksMsec() + 120000;
		while (Time.GetTicksMsec() < deadline)
		{
			await Frames();
			if (GetTree().CurrentScene?.SceneFilePath == path)
			{
				await Frames(3);
				return GetTree().CurrentScene.GetNode<Player>("Player");
			}
		}
		throw new TimeoutException("Map did not load: " + path);
	}
	private async void RunChecks()
	{
		try
		{
			if (!OS.GetCmdlineUserArgs().Contains("--menu-test-profile")) throw new InvalidOperationException("Use --menu-test-profile.");
			GetTree().CurrentScene = null;
			MatchSession.Draft = new();
			GetTree().ChangeSceneToFile("res://scenes/main_menu.tscn");
			await Frames(3);
			var menu = (MainMenu)GetTree().CurrentScene;
			Key(Godot.Key.Enter);
			Check(menu.IsSelectingMap, "Enter on Play opens the selector.");
			var dialog = menu.GetNode<Panel>(Dialog);
			Check(dialog.GetNode<Label>("Title").Text == "Create Server", "Original English caption.");
			Check(dialog.GetNode<Control>("Server/GameType").Position == new Vector2(8, 32), "Original CS:SO field coordinates.");
			Check(dialog.GetNode<Control>("Server/MapList").Size == new Vector2(252, 352), "Original CS:SO map list size.");
			Check(menu.VisibleMapCount == MapCatalog.Discover().Length, "All installed maps are listed.");
			foreach (var map in MapCatalog.Discover().Select((entry, index) => (entry, index)))
			{
				menu.SelectMap(map.index);
				Check(dialog.GetNode<TextureRect>("Preview/MapPreview").Texture != null, map.entry.Id + " has a preview.");
				Check(dialog.GetNode<Label>("Preview/PreviewId").Text == map.entry.Id, "Preview follows map selection.");
			}
			menu.SelectMap(0);
			dialog.GetNode<Button>("Start").GrabFocus(); Key(Godot.Key.Right);
			Check(menu.SelectedMap == 1, "Arrow navigation selects the next map.");
			var gameType = dialog.GetNode<OptionButton>("Server/GameType");
			Check(gameType.ItemCount == 2 && gameType.GetItemText(0) == "Arsenal" && gameType.GetItemText(1) == "Custom", "Types without available modes are hidden.");
			gameType.EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
			var gameMode = dialog.GetNode<OptionButton>("Server/GameMode");
			Check(gameMode.ItemCount == 2 && gameMode.GetItemText(0) == "Deathmatch" && gameMode.GetItemText(1) == "Team Deathmatch", "Unavailable modes are hidden from the dropdown.");
			gameMode.EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
			Check(menu.SelectedModeId == "deathmatch" && !dialog.GetNode<Button>("Start").Disabled, "Dropdowns select playable DM.");
			menu.SelectMode("teamdeathmatch");
			Check(!dialog.GetNode<Button>("Start").Disabled, "Team deathmatch is playable.");
			menu.SelectMode("armsrace");
			Check(menu.SelectedModeId == "sandbox" && GameModeCatalog.Modes.Any(mode => mode.Id == "armsrace" && !mode.Available), "Unavailable saved modes fall back to Free Play and remain in the catalog.");
			menu.SelectMode("deathmatch");
			menu.SelectMap(2);
			dialog.GetNode<CheckBox>("Server/AllMaps").ButtonPressed = false;
			Check(menu.SelectedMap != 2 && menu.VisibleMapCount == MapCatalog.Discover().Count(map => map.IsCombat), "Mode filtering excludes KZ and selects a valid map.");
			menu.SelectMap(-1);
			Check(dialog.GetNode<Button>("Start").Disabled && dialog.GetNode<Label>("Preview/PreviewFallback").Visible, "Empty selection has a fallback and cannot start.");
			menu.SelectMode("sandbox");
			Check(menu.VisibleMapCount == MapCatalog.Discover().Length && !dialog.GetNode<Button>("Start").Disabled, "Free Play supports every map.");
			menu.SelectSetupTab(1);
			dialog.GetNode<CheckBox>("Gameplay/FriendlyFire").ButtonPressed = false;
			dialog.GetNode<CheckBox>("Gameplay/FallDamage").ButtonPressed = false;
			dialog.GetNode<CheckBox>("Gameplay/Cheats").ButtonPressed = true;
			menu.SelectSetupTab(2);
			dialog.GetNode<OptionButton>("Bots/Difficulty").EmitSignal(OptionButton.SignalName.ItemSelected, 4L);
			dialog.GetNode<HSlider>("Bots/Quota").Value = 7;
			dialog.GetNode<CheckBox>("Bots/Snipers").ButtonPressed = false;
			Check(MatchSession.Draft.Bots.Difficulty == "hard" && MatchSession.Draft.Bots.Quota == 7 && !MatchSession.Draft.Bots.Snipers, "Bot settings update the future preset.");
			Key(Godot.Key.Escape);
			Check(!menu.IsSelectingMap, "Escape closes the selector.");
			var saved = JsonSerializer.Deserialize<MatchSetup>(FileAccess.GetFileAsString("user://menu_test_setup.json"))!;
			Check(saved.Bots.Quota == 7 && !saved.FriendlyFire && saved.Cheats, "Game and bot settings are persisted.");
			GetTree().ChangeSceneToFile("res://scenes/main_menu.tscn");
			await Frames(3);
			menu = (MainMenu)GetTree().CurrentScene; menu.OpenMapSelection();
			dialog = menu.GetNode<Panel>(Dialog);
			Check(dialog.GetNode<HSlider>("Bots/Quota").Value == 7 && !dialog.GetNode<CheckBox>("Gameplay/FallDamage").ButtonPressed, "Settings survive menu recreation.");
			dialog.GetNode<Button>("Cancel").GrabFocus(); Key(Godot.Key.Enter);
			Check(!menu.IsSelectingMap, "Enter activates focused Cancel.");
			menu.OpenMapSelection(); menu.SelectMap(0); menu.StartSelectedMap();
			Check(menu.GetNode<Panel>("Interface/Modal/Loading").Visible, "Starting shows load progress.");
			var player = await WaitForMap("res://scenes/maps/dust2.tscn");
			Check(MatchSession.Current?.MapId == "de_dust2" && MatchSession.Current.ModeId == "sandbox", "Launch snapshot reaches the match.");
			Check(!SourceDamageRules.FriendlyFire && !SourceDamageRules.FallDamageEnabled && SourceDamageRules.Cheats, "Gameplay rules apply after player initialization.");
			player.JoinTeam(2); player.JoinClass(1); Key(Godot.Key.Escape);
			var pause = player.Menu!;
			Check(pause.IsOpen && GetTree().Paused && !player.MouseCaptured, "Pause menu pauses offline play and releases input.");
			pause.OpenMapSelection(); pause.SelectSetupTab(1);
			pause.GetNode<CheckBox>(Dialog + "/Gameplay/FriendlyFire").ButtonPressed = true;
			Check(!SourceDamageRules.FriendlyFire && MatchSession.Current?.FriendlyFire == false, "Editing the next match leaves the current snapshot unchanged.");
			Key(Godot.Key.Escape);
			Check(pause.IsOpen && !pause.IsSelectingMap, "Escape from selection returns to pause menu.");
			Key(Godot.Key.Escape);
			Check(!GetTree().Paused && player.MouseCaptured, "Escape resumes gameplay.");
			Key(Godot.Key.Escape); pause.OpenMapSelection(); pause.SelectSetupTab(0); pause.SelectMap(1);
			pause.GetNode<ItemList>(Dialog + "/Server/MapList").EmitSignal(ItemList.SignalName.ItemActivated, 1L);
			await WaitForMap("res://scenes/maps/inferno.tscn");
			Check(!GetTree().Paused && MatchSession.Current?.MapId == "de_inferno" && SourceDamageRules.FriendlyFire, "Double click changes map from a paused match and applies the new snapshot.");
			GD.Print($"MENU_CHECKS_OK checks={_checks}"); GetTree().Quit();
		}
		catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
	}
}
