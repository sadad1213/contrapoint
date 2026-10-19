using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.Gameplay;
using Contrapoint.Players;
using Contrapoint.UI;
using Contrapoint.Weapons;

public partial class HudChecks : Node3D
{
	private int _checks, _failures;
	private Player _player = null!;
	private void Check(bool condition, string message)
	{
		_checks++;
		if (condition) return;
		_failures++;
		GD.PushError(message);
	}
	private async Task Frames(int count = 3)
	{
		for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}
	private static void Advance(FirearmView gun, double seconds)
	{
		while (seconds > .000001)
		{
			var step = Math.Min(seconds, 1.0 / 144);
			gun._Process(step);
			seconds -= step;
		}
	}
	private static void Fire(FirearmView gun)
	{
		gun.HandleCommand("attack", true);
		gun.HandleCommand("attack", false);
		Advance(gun, .24);
	}
	public override async void _Ready()
	{
		try { await Test(); }
		catch (Exception error) { _failures++; GD.PushError(error.ToString()); }
		GD.Print($"HUD_CHECKS checks={_checks} failures={_failures}");
		GetTree().Quit(_failures == 0 ? 0 : 1);
	}
	private async Task CheckRetainedDrawing(CssHud hud)
	{
		// These checks need a real renderer: snapshots alone cannot catch a
		// cached HUD that stops repainting when health, geometry or settings change.
		var foreground = hud.GetNode<Control>("Foreground");
		var crosshair = _player.GetNode<Crosshair>("HUD/Crosshair");
		var panels = 0; var text = 0; var radar = 0; var sight = 0;
		void PanelDraw() => panels++;
		void TextDraw() => text++;
		void RadarDraw() => radar++;
		void SightDraw() => sight++;
		hud.Draw += PanelDraw; foreground.Draw += TextDraw;
		hud.Radar.Draw += RadarDraw; crosshair.Draw += SightDraw;
		try
		{
			// Initial team selection briefly has zero HP and starts the existing
			// damage-colour pulse. Wait for that animation before testing idle.
			await ToSignal(GetTree().CreateTimer(1.2), SceneTreeTimer.SignalName.Timeout);
			await Frames(6);
			panels = text = radar = sight = 0;
			await Frames(12);
			Check(panels == 0 && text == 0 && radar == 0 && sight == 0,
				$"Unchanged HUD, radar and crosshair must reuse their retained draw commands (panels={panels}, text={text}, radar={radar}, crosshair={sight}).");
			_player.Armor = 17; await Frames();
			Check(text > 0 && hud.Snapshot.Armor == 17, "Armour changes must repaint cached text.");
			_player.Armor = 0; await Frames();
			var oldRadar = radar;
			_player.Position += Vector3.Right; await Frames();
			Check(radar > oldRadar, "Movement must invalidate the cached radar.");
			_player.Position -= Vector3.Right; await Frames();
			oldRadar = radar;
			hud.Radar.RangeUnits += 1; await Frames();
			Check(radar > oldRadar, "Radar settings must invalidate its cached geometry.");
			hud.Radar.RangeUnits -= 1; await Frames();
			var oldSight = sight;
			crosshair.Gap += 1; await Frames();
			Check(sight > oldSight, "Crosshair settings must repaint the cached sight.");
			crosshair.Gap -= 1; await Frames();
			var oldSize = GetWindow().Size;
			var oldPanels = panels; oldRadar = radar; oldSight = sight;
			GetWindow().Size = new(oldSize.X - 64, oldSize.Y - 64); await Frames(6);
			Check(panels > oldPanels && radar > oldRadar && sight > oldSight,
				"Window resizing must repaint panels, radar and crosshair at the new scale.");
			GetWindow().Size = oldSize; await Frames(6);
			_player.SetVitals(24, 0, false); await Frames();
			var oldText = text; await Frames(4);
			Check(text > oldText, "Low-health colour pulses must keep animating between state changes.");
			_player.SetVitals(100, 0, false);
		}
		finally
		{
			hud.Draw -= PanelDraw; foreground.Draw -= TextDraw;
			hud.Radar.Draw -= RadarDraw; crosshair.Draw -= SightDraw;
		}
	}
	private async Task Test()
	{
		var root = new Node3D { Name = "Dust2", SceneFilePath = "res://assets/maps/de_dust2/de_dust2.scn" };
		AddChild(root);
		_player = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		_player.SpawnPointsRootPath = new("../Dust2");
		AddChild(_player);
		await Frames();
		_player.JoinTeam(2);
		_player.JoinClass(1);
		_player.SetPhysicsProcess(false);
		// Playing maps now spawn with the team pistol. Supply the two rifles
		// explicitly for the shared-ammunition comparison.
		_player.Inventory.AddWeapon(GD.Load<PackedScene>("res://weapons/ak47_view.tscn"), equip: false);
		var scout = (FirearmView)_player.Inventory.AddWeapon(GD.Load<PackedScene>("res://weapons/scout_view.tscn"), equip: false);
		foreach (var weapon in _player.Inventory.Weapons)
		{
			weapon.SetProcess(false);
			weapon.Sounds.StopAll();
			weapon.Sounds.Enabled = false; // Hundreds of simulated shots must not queue real-time audio voices.
		}
		await Frames();
		var hud = _player.GetNode<CssHud>("HUD/Status");
		Check(hud.Visible && hud.Snapshot.Health == 100 && hud.Snapshot.Armor == 0 && hud.Snapshot.Clock == "0:00",
			"A local player starts with real HP and zero armour/round time.");
		Check(hud.Snapshot.Money == _player.Money && !hud.Snapshot.HasAmmo, "HUD reads the actual balance, and knives hide ammo.");
		Check(hud.Radar.HasOverview && hud.Radar.MapId == "de_dust2", "Radar resolves the map from its scene.");
		Check(hud.Radar.WorldToOverview(new(-2440 * SourceMovement.Unit, 0, -3383 * SourceMovement.Unit)).Length() < .001,
			"Radar uses the stock overview origin and Source-to-Godot coordinate conversion.");
		if (DisplayServer.GetName() != "headless") await CheckRetainedDrawing(hud);
		var center = hud.Radar.WorldToRadar(_player.GlobalPosition);
		_player.Rotation = new(0, Mathf.Pi / 2, 0);
		var ahead = hud.Radar.WorldToRadar(_player.GlobalPosition - Vector3.Right * 10) - center;
		Check(Math.Abs(ahead.X) < .001 && ahead.Y < 0, "A world point ahead stays above the rotating radar center.");
		_player.Rotation = Vector3.Zero;
		var remote = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		remote.LocalInput = false;
		AddChild(remote);
		await Frames();
		Check(!remote.GetNode<CssHud>("HUD/Status").Visible, "Remote players never draw a local HUD.");
		Check(hud.Radar.Shows(remote), "Living teammates appear on radar.");
		remote.Team = PlayerTeam.CounterTerrorist;
		Check(!hud.Radar.Shows(remote), "Enemies are not revealed from network positions.");
		Check(!remote.GetNode<CssHud>("HUD/Status").Radar.HasOverview, "Maps without an overview have no radar.");
		remote.QueueFree();
		var guns = _player.Inventory.Weapons.OfType<FirearmView>().ToArray();
		var ak = guns.Single(gun => gun.Kind == FirearmView.FirearmKind.AK47);
		_player.SetMouseCaptured(true);
		_player.Inventory.Equip(ak);
		Advance(ak, 2);
		await Frames();
		Check(hud.Snapshot.Magazine == 30 && hud.Snapshot.Reserve == 90 && hud.Snapshot.UsesMagazine,
			"Equipping a rifle reads its actual magazine and reserve.");
		Fire(ak);
		Check(ak.MagazineAmmo == 29 && ak.ReserveAmmo == 90, "A shot consumes exactly one round from the magazine.");
		ak.HandleCommand("reload", true);
		Check(ak.Busy && ak.MagazineAmmo == 29, "Reload keeps its ammunition until it completes.");
		var shots = ak.ShotsFired;
		ak.HandleCommand("attack", true); ak.HandleCommand("attack", false);
		Check(ak.ShotsFired == shots, "Reloading blocks firing.");
		Advance(ak, 4);
		Check(ak.MagazineAmmo == 30 && ak.ReserveAmmo == 89 && scout.ReserveAmmo == 89,
			"Reload commits once and shares the 7.62 mm reserve with the Scout.");
		Fire(ak);
		ak.HandleCommand("reload", true);
		_player.Inventory.EquipSlot(WeaponSlot.Melee);
		Advance(ak, 4);
		Check(ak.MagazineAmmo == 29 && ak.ReserveAmmo == 89, "Switching weapons cancels an unfinished reload without adding ammunition.");
		_player.Inventory.Equip(ak); Advance(ak, 2);
		_player.Inventory.TakeReserveAmmo(ak.AmmoType, 86);
		for (var i = 0; i < 4; i++) Fire(ak);
		ak.HandleCommand("reload", true); Advance(ak, 4);
		Check(ak.MagazineAmmo == 28 && ak.ReserveAmmo == 0, "A partial reserve only transfers the available rounds.");
		for (var i = 0; i < 28; i++) Fire(ak);
		shots = ak.ShotsFired;
		Fire(ak);
		Check(ak.MagazineAmmo == 0 && ak.ShotsFired == shots, "An exhausted gun cannot create more bullets.");
		_player.Inventory.ResetAmmunition(); Advance(ak, 2);
		for (var i = 0; i < 30; i++) Fire(ak);
		Advance(ak, .8); // Let the final firing animation return to idle.
		Check(ak.Busy && ak.MagazineAmmo == 0 && ak.ReserveAmmo == 90, "An empty magazine automatically starts reloading after the last shot.");
		Advance(ak, 4);
		Check(ak.MagazineAmmo == 30 && ak.ReserveAmmo == 60, "Automatic reload transfers exactly one full magazine.");
		Fire(ak); ak.HandleCommand("reload", true);
		_player.Respawn(restoreLoadout: false); // Test replenishment with the explicit rifle fixture.
		ak = _player.Inventory.Weapons.OfType<FirearmView>().Single(gun => gun.Kind == FirearmView.FirearmKind.AK47);
		_player.Inventory.Equip(ak); Advance(ak, 2);
		Check(ak.MagazineAmmo == 30 && ak.ReserveAmmo == 90 && !ak.Busy, "Respawn replenishes ammunition and cancels the previous weapon action.");
		_player.Armor = 75; _player.HasHelmet = true;
		RoundState.Instance!.RemainingSeconds = 61.1;
		await Frames();
		Check(hud.Snapshot.Armor == 75 && hud.Snapshot.Helmet && hud.Snapshot.Clock == "1:02",
			"Future armour and round rules can supply values without changing the HUD.");
		RoundState.Instance.RemainingSeconds = 0;
		ConsoleCommands.Execute("sv_cheats 1; cl_drawhud 0", _player.Movement, _player.Console!.Print);
		await Frames();
		Check(!hud.Visible && !_player.GetNode<Control>("HUD/Crosshair").Visible, "cl_drawhud hides status and crosshair.");
		ConsoleCommands.Execute("cl_drawhud 1; hideradar", _player.Movement, _player.Console.Print);
		await Frames();
		Check(hud.Visible && !hud.Radar.Visible, "hideradar only hides the radar.");
		ConsoleCommands.Execute("drawradar; sv_cheats 0", _player.Movement, _player.Console.Print);
		await Frames();
		Check(hud.Radar.Visible, "drawradar restores an available overview.");
		_player.TakeDamage(100);
		await Frames();
		Check(!hud.Visible, "Death hides the first-person HUD.");
		_player.QueueFree();
		root.QueueFree();
		await Frames();
	}
}
