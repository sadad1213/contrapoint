using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.Players;
using Contrapoint.UI;
using Contrapoint.Weapons;
using Contrapoint.Maps;

public partial class ConsoleChecks : Node3D
{
	private int _checks;
	private int _failures;
	private void Check(bool condition, string message)
	{
		_checks++;
		if (condition) return;
		_failures++;
		GD.PushError(message);
	}

	private void KeyEvent(Key key, bool pressed = true) =>
		GetViewport().PushInput(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = pressed });
	private async Task Frames(int count = 2)
	{
		for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	public override async void _Ready()
	{
		if (!OS.GetCmdlineUserArgs().Contains("--console-test-profile"))
		{
			GD.PushError("Console checks require --console-test-profile to protect saved settings.");
			GetTree().Quit(1);
			return;
		}
		try { await Run(); }
		catch (Exception exception) { _failures++; GD.PushError(exception.ToString()); }
		await Task.Delay(100);
		GD.Print($"CONSOLE_CHECKS checks={_checks} failures={_failures}");
		GetTree().Quit(_failures == 0 ? 0 : 1);
	}

	private async Task CheckScoreboardControls(Player player)
	{
		ConsoleBindings.Keys.TryGetValue("TAB", out var previousTab);
		ConsoleBindings.Keys.TryGetValue("F6", out var previousF6);
		ConsoleBindings.Keys["TAB"] = ConsoleBindings.Keys["F6"] = "+showscores";
		var round = Contrapoint.Gameplay.RoundState.Instance!;
		round.BeginMatch("deathmatch");
		var board = (CssScoreboard)player.GetNode("DeathmatchHud").FindChild("Scoreboard", true, false)!;
		for (var i = 0; i < 2; i++)
		{
			KeyEvent(Key.Tab); await Frames();
			Check(player.ShowMatchScores && board.IsVisibleInTree(), "Holding TAB opens the scoreboard.");
			KeyEvent(Key.Tab, false); await Frames();
			Check(!player.ShowMatchScores && !board.IsVisibleInTree(), "Releasing TAB closes the scoreboard on every press.");
		}
		KeyEvent(Key.Tab); KeyEvent(Key.F6); KeyEvent(Key.Tab, false);
		Check(player.ShowMatchScores, "Another held scoreboard binding keeps it open.");
		KeyEvent(Key.F6, false);
		Check(!player.ShowMatchScores, "Releasing the final scoreboard binding closes it.");
		KeyEvent(Key.Tab); player.Menu!.SetOpen(true);
		Check(!player.ShowMatchScores, "Opening the menu releases the scoreboard.");
		player.Menu.SetOpen(false); KeyEvent(Key.Tab, false);
		KeyEvent(Key.Tab); player.TakeDamage(player.Health);
		Check(!player.ShowMatchScores, "Death releases the held scoreboard.");
		KeyEvent(Key.Tab, false); KeyEvent(Key.Tab); await Frames();
		Check(player.ShowMatchScores && board.IsVisibleInTree(), "The scoreboard can be held while dead.");
		KeyEvent(Key.Tab, false); await Frames();
		Check(!player.ShowMatchScores && !board.IsVisibleInTree(), "Releasing TAB closes the scoreboard while dead.");
		round.BeginMatch("sandbox"); player.Respawn();
		if (previousTab == null) ConsoleBindings.Keys.Remove("TAB"); else ConsoleBindings.Keys["TAB"] = previousTab;
		if (previousF6 == null) ConsoleBindings.Keys.Remove("F6"); else ConsoleBindings.Keys["F6"] = previousF6;
	}

	private async Task Run()
	{
		var floor = new StaticBody3D { Position = new(0, -0.5f, 0) };
		floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(200, 1, 200) } });
		AddChild(floor);
		var player = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		AddChild(player);
		await Frames();
		var console = player.Console!;
		var entry = console.GetNode<LineEdit>("Modal/ConsoleWindow/Command");
		var output = console.GetNode<RichTextLabel>("Modal/ConsoleWindow/Output");
		var suggestions = console.GetNode<ItemList>("Modal/Completions");
		void SetEntry(string text)
		{
			entry.Text = text;
			entry.EmitSignal(LineEdit.SignalName.TextChanged, text);
		}
		Check(!console.IsOpen && player.HasLocalControl, "Console starts closed with gameplay enabled.");
		CheckKeyboardLook(player);
		await CheckScoreboardControls(player);
		ConsoleCommands.Execute("cl_bob_style 0; cl_wpn_sway_scale 1.6; cl_wpn_sway_interp .1; sensitivity 3; m_yaw .022; m_pitch .022; m_filter 0; m_rawinput 1; cl_mouseenable 1", player.Movement, console.Print);
		player.ViewmodelBob.Reset();
		var sway = player.ActiveWeapon!.GetNode<WeaponSway>("WeaponSway");
		var rest = sway.Transform;
		for (var i = 0; i < 16; i++)
		{
			player._Input(new InputEventMouseMotion { ScreenRelative = new(40, 16) });
			player.ViewmodelBob._Process(1.0 / 60);
			sway._Process(1.0 / 60);
		}
		var swayed = sway.Transform;
		var swayDistance = swayed.Origin.DistanceTo(rest.Origin);
		Check(swayDistance > 0.001f, "Mouse motion produces a visible sway before opening the console.");
		KeyEvent(Key.Tab);
		KeyEvent(Key.Quoteleft);
		KeyEvent(Key.Quoteleft, false);
		Check(!player.ShowMatchScores, "Opening the console releases a held scoreboard.");
		KeyEvent(Key.Tab, false);
		player.ViewmodelBob._Process(1.0 / 60);
		sway._Process(1.0 / 60);
		Check(sway.Transform.Origin.DistanceTo(swayed.Origin) < swayDistance * 0.25f,
			"Opening the console must not snap the hands back to the rest pose.");
		for (var i = 0; i < 240; i++)
		{
			player._Input(new InputEventMouseMotion { ScreenRelative = new(100, 100) });
			player.ViewmodelBob._Process(1.0 / 60);
			sway._Process(1.0 / 60);
		}
		Check(sway.Transform.Origin.DistanceTo(rest.Origin) < 0.00001f &&
			sway.Basis.GetRotationQuaternion().AngleTo(rest.Basis.GetRotationQuaternion()) < 0.001f,
			"Sway settles smoothly while console cursor motion does not drive the hands.");
		Check(console.IsOpen && !player.HasLocalControl && !player.Inventory.Enabled, "Tilde opens a modal console and disables gameplay.");
		Check(entry.HasFocus(), "Opening the console focuses command entry.");
		await Frames();
		Check(player.ActiveWeapon!.Visible && player.ActiveWeapon.Equipped &&
			player.GetNode<Control>("HUD/Crosshair").Visible,
			"Opening the console preserves equipped hands and HUD.");
		Check(!player.ActiveWeapon.IsProcessingUnhandledInput(), "Visible weapons must not receive console input.");
		GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = new(1000, 600) });
		GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = new(1000, 600) });
		Check(!player.MouseCaptured, "Clicking outside the console must not capture the mouse.");
		var rotation = player.Rotation;
		GetViewport().PushInput(new InputEventMouseMotion { ScreenRelative = new(100, 100) });
		KeyEvent(Key.V);
		Check(player.Rotation == rotation && !player.Movement.Noclip, "Console input must not rotate the player or toggle noclip.");
		entry.GrabFocus();
		foreach (var character in "sv_air")
			GetViewport().PushInput(new InputEventKey { Unicode = character, Pressed = true });
		Check(entry.Text == "sv_air", "Ordinary keyboard text reaches the focused command entry.");
		await Frames(); // LineEdit coalesces TextChanged until the next process frame.
		Check(suggestions.Visible && suggestions.ItemCount == 1 && suggestions.GetItemText(0) == "sv_airaccelerate 100",
			"Autocomplete shows the movement cvar with its current value.");
		Check(suggestions.GlobalPosition.Y >= console.GetNode<Panel>("Modal/ConsoleWindow").GetGlobalRect().End.Y,
			"Autocomplete appears below the console window.");
		SetEntry("m");
		await Frames();
		Check(suggestions.ItemCount > 0 && suggestions.GetItemText(0) == "map", "Map appears in command autocomplete.");
		KeyEvent(Key.Tab);
		var maps = MapCatalog.Discover().Select(map => "map " + map.Id).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
		Check(entry.Text == "map " && suggestions.ItemCount == maps.Length &&
			Enumerable.Range(0, maps.Length).All(index => suggestions.GetItemText(index) == maps[index]),
			"Completing map immediately lists all available maps.");
		KeyEvent(Key.Down);
		KeyEvent(Key.Tab);
		Check(entry.Text == maps[1] + " ", "Arrows select a map and Tab inserts the selection.");
		SetEntry("map de_d");
		await Frames();
		Check(suggestions.ItemCount == 1 && suggestions.GetItemText(0) == "map de_dust2", "Map arguments filter by prefix.");
		suggestions.EmitSignal(ItemList.SignalName.ItemClicked, 0L, Vector2.Zero, (long)MouseButton.Left);
		Check(entry.Text == "map de_dust2 " && entry.HasFocus(), "Clicking a suggestion inserts it and keeps typing focused.");
		SetEntry("late_");
		await Frames();
		Check(!suggestions.Visible, "Unknown prefixes have no suggestions.");
		var registeredCommandRan = false;
		ConsoleCommands.Register("late_registered", (_, _, _) => registeredCommandRan = true);
		Check(suggestions.Visible && suggestions.GetItemText(0) == "late_registered",
			"Registering a command refreshes an already open dropdown immediately.");
		console.Execute("late_registered");
		Check(registeredCommandRan, "New commands execute through the same registry used by autocomplete.");
		var batched = ConsoleCommands.GetCompletions("sv_airaccelerate 10; map \"de_i");
		Check(batched.Count == 1 && batched[0] == "sv_airaccelerate 10; map de_inferno",
			"Completion preserves earlier commands and handles quoted map names.");
		SetEntry("sv_air");
		await Frames();
		KeyEvent(Key.Tab);
		Check(entry.Text == "sv_airaccelerate ", "Tab completes the movement cvar without inserting its displayed value.");
		SetEntry("sv_airaccelerate 100");
		KeyEvent(Key.Enter);
		Check(player.Movement.AirAcceleration == 100 && entry.Text == "", "Enter submits the cvar and clears command entry.");
		Check(!suggestions.Visible, "Submission dismisses autocomplete.");
		console.Execute("sensitivity 0.1; volume 0.1");
		SetEntry("sensitiv");
		await Frames();
		Check(suggestions.GetItemText(0) == "sensitivity 0.1", "Sensitivity suggestions hide float conversion noise.");
		KeyEvent(Key.Tab);
		Check(entry.Text == "sensitivity ", "Tab leaves the cvar value for the user to enter.");
		Check(output.GetParsedText().Contains("\"sensitivity\" changed to \"0.1\""), "Cvar output uses the same clean numeric formatting.");
		SetEntry("net_graph");
		await Frames();
		console.Execute("net_graph 3");
		Check(suggestions.GetItemText(0) == "net_graph 3", "Changing a cvar refreshes its value in an already open suggestion list.");
		suggestions.EmitSignal(ItemList.SignalName.ItemClicked, 0L, Vector2.Zero, (long)MouseButton.Left);
		Check(entry.Text == "net_graph ", "Clicking a suggestion inserts its name without its displayed value.");
		Check(ConsoleCommands.GetCompletionText("echo \";\"; sensitivity", player.Movement) == "echo \";\"; sensitivity 0.1",
			"Decorating a batched cvar completion preserves earlier quoted commands.");
		Check(ConsoleCommands.GetCompletionText("volume", player.Movement) == "volume 0.1" &&
			ConsoleCommands.GetCompletionText("sensitivity", null) == "sensitivity 0.1", "Clean values work for audio and menu completions.");
		console.Execute("net_graph 0");
		SetEntry("");
		console.Execute("sv_airaccelerate");
		Check(output.GetParsedText().Contains("\"100\" ( def. \"100\" )"), "Query prints current and default values.");
		console.Execute("SV_AIRACCELERATE \"150\"; sv_airaccelerate");
		Check(player.Movement.AirAcceleration == 150 && output.GetParsedText().Contains("changed to \"150\""), "Quoted values and command batches execute in order, ignoring name case.");
		foreach (var value in new[] { "NaN", "Infinity", "-1", "1,5", "1e1000", "abc", "10 20" })
			console.Execute("sv_airaccelerate " + value);
		Check(player.Movement.AirAcceleration == 150, "Invalid, negative and non-finite values must not change movement.");
		console.Execute("sv_airaccelerate 12; sv_airaccelerate \"20");
		Check(player.Movement.AirAcceleration == 150, "Malformed quotes must not execute a partial batch.");
		console.Execute("[color=red]unknown[/color]");
		Check(output.GetParsedText().Contains("Unknown command: [color=red]unknown[/color]"), "Unknown commands remain literal text and report an error.");
		SetEntry("unfinished draft");
		KeyEvent(Key.Up);
		Check(entry.Text == "[color=red]unknown[/color]", "Up recalls the last command.");
		KeyEvent(Key.Down);
		Check(entry.Text == "unfinished draft", "Down restores the draft after history browsing.");
		console.Execute("sv_airaccelerate 0");
		player.GlobalPosition = new(0, 10, 0);
		player.Movement.Reset();
		player.Simulate(new(new(1, 0), false, false, false), 1f / 66);
		Check(player.Movement.HorizontalSpeed == 0, "sv_airaccelerate 0 disables acceleration in actual airborne simulation.");
		console.Execute("sv_airaccelerate 100");
		player.Movement.Reset();
		player.Simulate(new(new(1, 0), false, false, false), 1f / 66);
		Check(player.Movement.HorizontalSpeed > 29.9f, "sv_airaccelerate 100 immediately changes actual airborne acceleration.");
		player.Respawn();
		Check(player.Movement.AirAcceleration == 100 && !player.Inventory.Enabled, "Respawn preserves tuning and cannot re-enable weapons through an open console.");
		KeyEvent(Key.Escape);
		Check(!console.IsOpen && player.HasLocalControl, "Escape closes the console and restores prior gameplay control.");
		player.SetMouseCaptured(false);
		console.SetOpen(true);
		console.SetOpen(false);
		Check(!player.MouseCaptured, "Closing a console opened with a free cursor preserves that state.");
		player.SetMouseCaptured(true);
		console.SetOpen(true);
		for (var i = 0; i < 600; i++) console.Print("scrollback " + i);
		Check(!output.GetParsedText().Contains("scrollback 0\n") && output.GetParsedText().Contains("scrollback 599"), "Scrollback is bounded while retaining newest lines.");
		console.Execute("sv_airaccelerate 100");
		SetEntry("sv_air");
		await Frames();
		if (Array.Exists(OS.GetCmdlineUserArgs(), argument => argument == "--console-screenshot"))
		{
			await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
			GetViewport().GetTexture().GetImage().SavePng("user://console_preview.png");
			GD.Print("CONSOLE_PREVIEW " + ProjectSettings.GlobalizePath("user://console_preview.png"));
		}
		await CheckBasics(player);
		player.QueueFree();
		await Frames();
	}

	private void CheckKeyboardLook(Player player)
	{
		void Run(string line) => ConsoleCommands.Execute(line, player.Movement, player.Console!.Print);
		Run("bind LEFTARROW +left; bind RIGHTARROW +right; bind UPARROW +lookup; bind DOWNARROW +lookdown; cl_yawspeed 210; cl_pitchspeed 225; cl_anglespeedkey 0.67");
		var yaw = player.Rotation.Y;
		KeyEvent(Key.Left); player._PhysicsProcess(.01);
		Check(Mathf.Abs(player.Rotation.Y - yaw - Mathf.DegToRad(1.05f)) < .00001f, "Turning starts with Source's half-tick key sample.");
		yaw = player.Rotation.Y; player._PhysicsProcess(.01);
		Check(Mathf.Abs(player.Rotation.Y - yaw - Mathf.DegToRad(2.1f)) < .00001f, "Holding left turns at cl_yawspeed.");
		KeyEvent(Key.Left, false); yaw = player.Rotation.Y; player._PhysicsProcess(.01);
		Check(player.Rotation.Y == yaw, "Releasing left stops turning.");
		KeyEvent(Key.Right); KeyEvent(Key.Shift); player._PhysicsProcess(.01);
		Check(Mathf.Abs(player.Rotation.Y - yaw + Mathf.DegToRad(1.05f * .67f)) < .00001f, "Right turns the other way and +speed scales angular speed.");
		KeyEvent(Key.Right, false); KeyEvent(Key.Shift, false);
		var pitch = player.AimPitch;
		KeyEvent(Key.Up); player._PhysicsProcess(.01);
		Check(Mathf.Abs(player.AimPitch - pitch - Mathf.DegToRad(1.125f)) < .00001f, "Up arrow looks up at cl_pitchspeed.");
		player._PhysicsProcess(1);
		Check(Mathf.Abs(player.AimPitch - Mathf.DegToRad(89)) < .00001f, "Keyboard pitch stops at 89 degrees.");
		KeyEvent(Key.Up, false); KeyEvent(Key.Down); player._PhysicsProcess(.01);
		Check(player.AimPitch < Mathf.DegToRad(89), "Down arrow looks down.");
		KeyEvent(Key.Down, false);
		KeyEvent(Key.Left); player.Console!.SetOpen(true);
		yaw = player.Rotation.Y; player._PhysicsProcess(.01);
		player.Console.SetOpen(false); player.SetMouseCaptured(true); player._PhysicsProcess(.01);
		Check(player.Rotation.Y == yaw, "Opening the console clears held turn keys and pending samples.");
		KeyEvent(Key.Left, false);
		Run("bind F6 +left; cl_yawspeed 100");
		KeyEvent(Key.F6); player._PhysicsProcess(.01);
		Check(Mathf.Abs(player.Rotation.Y - yaw - Mathf.DegToRad(.5f)) < .00001f, "Turn commands can be rebound and use the configured speed.");
		KeyEvent(Key.F6, false); Run("unbind F6; cl_yawspeed 210");
		player.Respawn();
	}

	private async Task CheckBasics(Player player)
	{
		var console = player.Console!;
		console.SetOpen(false);
		player.SetMouseCaptured(true);
		void Run(string line) => ConsoleCommands.Execute(line, player.Movement, console.Print);
		Run("sensitivity 2; m_yaw 0.022; m_pitch -0.022; m_filter 0; m_rawinput 1; cl_mouseenable 1");
		var yaw = player.Rotation.Y;
		var pitch = player.AimPitch;
		player._Input(new InputEventMouseMotion { ScreenRelative = new(10, 10) });
		Check(Mathf.Abs(player.Rotation.Y-yaw+Mathf.DegToRad(.44f))<.00001f && player.AimPitch>pitch,
			"Source sensitivity and negative m_pitch affect real mouse look.");
		Run("volume 0.25; volume NaN; volume -1; sensitivity Infinity");
		Check(Mathf.Abs(AudioServer.GetBusVolumeLinear(0)-.25f)<.00001f && Mathf.Abs(player.MouseSensitivity-Mathf.DegToRad(.044f))<.00001f,
			"Volume changes the audio bus; invalid settings leave real values intact.");
		Run("unbind w; bind UPARROW +forward; bind k +forward");
		KeyEvent(Key.W);
		Check(!Input.IsActionPressed("move_forward"), "Unbinding W removes movement without fallback hardcoded keys.");
		KeyEvent(Key.W,false);
		KeyEvent(Key.Up);
		KeyEvent(Key.K);
		KeyEvent(Key.Up,false);
		Check(Input.IsActionPressed("move_forward"), "Two keys may hold the same action; releasing one keeps the other held.");
		Run("bind k +back");
		KeyEvent(Key.K,false);
		Check(!Input.IsActionPressed("move_forward") && !Input.IsActionPressed("move_back"), "Rebinding a held key releases its original action.");
		Run("bind k \"+forward; +duck\"");
		KeyEvent(Key.K);
		Check(Input.IsActionPressed("move_forward") && Input.IsActionPressed("duck"), "A bind executes multiple button commands.");
		KeyEvent(Key.K,false);
		Check(!Input.IsActionPressed("move_forward") && !Input.IsActionPressed("duck"), "Key-up releases every button from a compound bind.");
		Run("alias +testhold \"+forward; +duck\"; alias -testhold \"-forward; -duck; echo release-marker\"; bind k +testhold");
		KeyEvent(Key.K);
		Check(Input.IsActionPressed("move_forward") && Input.IsActionPressed("duck"), "A plus alias expands through the command registry.");
		KeyEvent(Key.K,false);
		Check(!Input.IsActionPressed("move_forward") && console.GetNode<RichTextLabel>("Modal/ConsoleWindow/Output").GetParsedText().Contains("release-marker"),
			"Releasing a plus alias executes the matching minus alias.");
		KeyEvent(Key.K);
		console.SetOpen(true);
		Check(!Input.IsActionPressed("move_forward") && !Input.IsActionPressed("duck"), "Opening console clears all held gameplay buttons.");
		KeyEvent(Key.K,false);
		console.SetOpen(false);
		player.Inventory.EquipSlot(WeaponSlot.Primary);
		var firearm = (FirearmView)player.ActiveWeapon!;
		Run("unbind MOUSE1; bind j +attack");
		GetViewport().PushInput(new InputEventMouseButton { ButtonIndex=MouseButton.Left, Pressed=true });
		Check(!firearm.TriggerHeld, "Unbinding MOUSE1 prevents weapon input from bypassing binds.");
		GetViewport().PushInput(new InputEventMouseButton { ButtonIndex=MouseButton.Left, Pressed=false });
		KeyEvent(Key.J);
		Check(firearm.TriggerHeld, "Attack may be rebound to a keyboard key.");
		Run("use weapon_m4a1");
		Check(player.ActiveWeapon is FirearmView { Kind: FirearmView.FirearmKind.M4A1, TriggerHeld: true }, "Held attack carries over to a newly selected weapon.");
		KeyEvent(Key.J,false);
		Check(player.ActiveWeapon is FirearmView { TriggerHeld: false }, "Releasing rebound attack clears the active weapon trigger.");
		Run("bind j +jump");
		KeyEvent(Key.J);
		player.SetMouseCaptured(false);
		Check(!Input.IsActionPressed("jump"), "Losing gameplay capture releases held actions.");
		KeyEvent(Key.J,false);
		player.SetMouseCaptured(true);
		Run("unbind `; bind F8 toggleconsole");
		KeyEvent(Key.Quoteleft);
		Check(!console.IsOpen, "The console key itself is configurable.");
		KeyEvent(Key.Quoteleft,false);
		KeyEvent(Key.F8);
		KeyEvent(Key.F8,false);
		Check(console.IsOpen, "A rebound toggleconsole opens the console.");
		KeyEvent(Key.F8);
		KeyEvent(Key.F8,false);
		Check(!console.IsOpen, "The same rebound key closes the console.");
		console.SetOpen(true);
		Run("slot3");
		Check(player.ActiveWeapon?.Slot == WeaponSlot.Melee && !player.Inventory.Enabled, "Console weapon commands select a weapon while keeping gameplay input disabled.");
		console.SetOpen(false);
		Run("crosshair 0; cl_crosshairsize 6; cl_crosshaircolor_r 127; cl_crosshairdot 1; cl_showfps 1; cl_showpos 1; sv_cheats 1; viewmodel_fov 70");
		await Frames();
		var sight=player.GetNode<Crosshair>("HUD/Crosshair");
		Check(!sight.Visible && sight.ArmLength==6 && sight.Dot && player.Stats.ShowFps && player.Stats.ShowPosition,
			"Crosshair and diagnostic commands update actual presentation.");
		Check(player.GetNode<Camera3D>("WeaponLayer/Container/Viewport/Camera3D").Fov==70,"Viewmodel FOV updates its independent camera.");
		Run("sv_gravity 400; sv_accelerate 7; sv_friction 3; sv_stopspeed 90; sv_stepsize 24; sv_maxspeed 200; sv_wateraccelerate 12; sv_waterfriction 2");
		Check(player.Movement.Gravity==400 && player.Movement.GroundAcceleration==7 && player.Movement.Friction==3 && player.Movement.StopSpeed==90 &&
			player.Movement.StepHeight==24 && player.Movement.MaxSpeed==200 && player.Movement.WaterAcceleration==12 && player.Movement.WaterFriction==2,
			"Movement cvars update simulation properties.");
		Run("toggle crosshair; incrementvar volume 0 1 0.25");
		Check(sight.Enabled && Mathf.Abs(AudioServer.GetBusVolumeLinear(0)-.5f)<.00001f, "toggle and incrementvar use current cvar values.");
		Run("alias testcfg \"echo \\\"quoted; text\\\"\"; bind l testcfg; host_writeconfig roundtrip");
		Run("unbindall; sensitivity 9; volume 1; alias testcfg \"echo overwritten\"; exec roundtrip");
		Check(ConsoleBindings.Keys.TryGetValue("l",out var binding) && binding=="testcfg" && Mathf.Abs(player.MouseSensitivity-Mathf.DegToRad(.044f))<.00001f &&
			Mathf.Abs(AudioServer.GetBusVolumeLinear(0)-.5f)<.00001f, "Config roundtrip restores bindings and archived settings.");
		Run("testcfg");
		Check(console.GetNode<RichTextLabel>("Modal/ConsoleWindow/Output").GetParsedText().Contains("quoted; text"), "Config roundtrip preserves nested quotes and semicolons in aliases.");
		var messages=new System.Collections.Generic.List<string>();
		ConsoleCommands.Execute("alias recurse recurse; recurse",player.Movement,(text,_)=>messages.Add(text));
		Check(messages.Exists(x=>x.Contains("recursion limit")),"Recursive aliases stop at a finite limit.");
		Run("sensitivity 2 // comment\nvolume 0.3\necho \"// literal; text\"");
		Check(Mathf.Abs(AudioServer.GetBusVolumeLinear(0)-.3f)<.00001f,"CFG parsing supports newlines and comments.");
		var original=ConsoleBindings.Keys.Count;
		Run("bind not_a_real_key +attack; exec ../config");
		Check(ConsoleBindings.Keys.Count==original,"Invalid key names and config paths do not alter binds.");
		Check(ConsoleCommands.GetCompletions("testc").Contains("testcfg"), "User aliases participate in autocomplete.");
		Run("clear");
		Check(console.GetNode<RichTextLabel>("Modal/ConsoleWindow/Output").GetParsedText()=="","clear erases scrollback.");
		CheckCheats(player);
		// Keep the isolated profile deterministic for the next headless run.
		Run("exec default; sensitivity 3; m_pitch 0.022; volume 1; crosshair 1; cl_crosshairsize 2; cl_crosshairdot 0; cl_showfps 0; cl_showpos 0; viewmodel_fov 58");
	}
	private void CheckCheats(Player player)
	{
		var errors = new System.Collections.Generic.List<string>();
		void Run(string line) => ConsoleCommands.Execute(line, player.Movement, (text, error) => { if (error) errors.Add(text); });
		Run("sv_cheats 0");
		var protectedCommands = new[] { "noclip", "thirdperson", "stopsound", "respawn", "cl_playermodel leet",
			"god", "buddha", "hurtme", "cl_drawhud 0", "r_drawviewmodel 0",
			"cl_bob 0", "cl_bobup 0.7", "spec_freeze_time 2", "spec_freeze_traveltime 1", "spec_freeze_distance_min 20",
			"spec_freeze_distance_max 30", "player_debug_print_damage 1" };
		foreach (var command in protectedCommands)
		{
			errors.Clear(); Run(command);
			Check(errors.Count == 1 && errors[0].Contains("sv_cheats 1"), command + " requires cheats.");
		}
		errors.Clear();
		Run("viewmodel_fov 70; incrementvar viewmodel_fov 20 120 1; firstperson; hideradar; drawradar; sv_gravity 700; sv_noclipspeed 6");
		Check(errors.Count == 0 && player.Movement.Gravity == 700 && player.GetNode<Camera3D>("WeaponLayer/Container/Viewport/Camera3D").Fov == 71,
			"Viewmodel FOV, firstperson, radar and server movement tuning do not require cheats.");
		Run("alias cheat_alias noclip; cheat_alias; toggle cl_drawhud; incrementvar cl_bob 0 0.1 0.001");
		Check(errors.Count == 3 && !player.Movement.Noclip, "Aliases, toggle and incrementvar cannot bypass cheat protection.");
		Run("sv_cheats 1; noclip; thirdperson; god; buddha; cl_drawhud 0; viewmodel_fov 70; r_drawviewmodel 0; spec_freeze_time 2; player_debug_print_damage 1");
		Check(player.Movement.Noclip && player.IsThirdPerson && player.GodMode && player.BuddhaMode &&
			!player.GetNode<CssHud>("HUD/Status").Enabled && SourceDeathRules.FreezeSeconds == 2,
			"Enabling cheats allows gameplay and presentation cheats in the same batch.");
		Run("sv_cheats 0");
		Check(!player.Movement.Noclip && !player.IsThirdPerson && !player.GodMode && !player.BuddhaMode &&
			player.GetNode<CssHud>("HUD/Status").Enabled && player.GetNode<Camera3D>("WeaponLayer/Container/Viewport/Camera3D").Fov == 70 &&
			player.GetNode<SubViewportContainer>("WeaponLayer/Container").Visible && SourceDeathRules.FreezeSeconds == 5 && !SourceDamageRules.DebugDamage,
			"Disabling cheats clears active modes and restores cheat variables while preserving viewmodel FOV.");
		ConsoleCommands.Initialize(player);
		Check(player.GetNode<Camera3D>("WeaponLayer/Container/Viewport/Camera3D").Fov == 70,
			"Player initialization preserves the viewmodel FOV override with cheats disabled.");
		var team = player.Team;
		player.Team = PlayerTeam.Spectator;
		player.Movement.ToggleNoclip();
		Run("sv_cheats 0");
		Check(player.Movement.Noclip, "Disabling cheats preserves spectator flight.");
		player.Movement.ToggleNoclip();
		player.Team = team;
		Run("sv_gravity 800; sv_noclipspeed 5; viewmodel_fov 58");
	}

}
