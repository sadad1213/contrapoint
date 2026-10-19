using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.Maps;
using Contrapoint.Players;
using Contrapoint.Weapons;
using Contrapoint.Gameplay;

public partial class Arena2000Checks : Node3D
{
	private int _checks;
	private Player _player = null!;
	private void Check(bool condition, string message)
	{
		_checks++;
		if (!condition) throw new InvalidOperationException(message);
	}
	private async Task Frames(int count, bool simulate = false)
	{
		for (var i = 0; i < count; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
			if (simulate) _player.Simulate(default, 1f / 66);
		}
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}
	public override async void _Ready()
	{
		try
		{
			var entry = MapCatalog.Find("$2000$");
			Check(entry?.Id == "2000" && entry.IsCombat, "original name resolves to a combat map");
			Check(GameModeCatalog.Find("deathmatch").Supports(entry!) && GameModeCatalog.Find("teamdeathmatch").Supports(entry!), "both combat modes support the arena");
			Check(GD.Load<NavigationMesh>("res://assets/maps/2000/bot_navigation.tres").GetPolygonCount() > 0, "baked bot navigation");
			var game = GD.Load<PackedScene>(entry!.ScenePath).Instantiate<Node3D>();
			_player = game.GetNode<Player>("Player");
			_player.LocalInput = false;
			AddChild(game);
			var map = game.GetNode<Node3D>("Arena2000");
			Check(map.FindChildren("Spawn_terrorist_*", "Marker3D", true, false).OfType<Marker3D>()
				.Any(m => _player.GlobalPosition.DistanceTo(m.GlobalPosition + Vector3.Up * .04f) < .001f), "initial entry uses an authored T spawn");
			var pickups = map.GetChildren().OfType<WeaponPickup>().ToArray();
			Check(pickups.Length == 33, "33 supported authored weapons");
			Check(pickups.All(p => p.Magazine == -1), "authored map weapons request a full default magazine");
			Check(pickups.All(p => p.ExtraAmmo == -1), "authored map weapons request a full default reserve");
			// Keep the spawn traversal from consuming the pickup fixtures.
			foreach (var pickup in pickups) pickup.SetPhysicsProcess(false);
			await Frames(3);
			Check(map.GetNode("WorldBrushes").GetChildCount() == 66, "66 compiled world collision brushes");
			Check(map.FindChildren("Glow_*", "MeshInstance3D", true, false).Count == 16, "16 authored light glows");
			Check(GetTree().GetNodesInGroup("source_buyzone").Count == 2, "both authored buy zones");
			Check(map.FindChildren("Spawn_deathmatch_*", "Marker3D", true, false).Count == 69, "69 generated reachable deathmatch spawns");
			foreach (var team in new[] { PlayerTeam.Terrorist, PlayerTeam.CounterTerrorist })
			{
				_player.Team = team;
				var pattern = team == PlayerTeam.Terrorist ? "Spawn_terrorist_*" : "Spawn_counterterrorist_*";
				var markers = map.FindChildren(pattern, "Marker3D", true, false).OfType<Marker3D>().ToArray();
				Check(markers.Length == 16, "16 authored spawns for " + team);
				foreach (var marker in markers)
				{
					_player.SourceTeleport(marker.GlobalPosition + Vector3.Up * .04f, marker.GlobalRotation.Y, 0);
					await Frames(50, true);
					Check(_player.IsOnFloor(), "settles on floor at " + marker.Name);
				}
				for (var i = 0; i < 20; i++)
				{
					_player.Respawn();
					Check(markers.Any(m => _player.GlobalPosition.DistanceTo(m.GlobalPosition + Vector3.Up * .04f) < .001f
						&& _player.GlobalBasis.IsEqualApprox(m.GlobalBasis)), "respawn preserves team position and orientation");
					await Frames(45, true);
					Check(_player.IsOnFloor(), "respawn stays on arena floor");
				}
			}
			foreach (var group in pickups.GroupBy(p => p.WeaponClass))
			{
				var pickup = group.First();
				foreach (var weapon in _player.Inventory.Weapons.Where(w => w.Slot == pickup.Slot).ToArray()) _player.Inventory.RemoveWeapon(weapon);
				_player.SourceTeleport(pickup.GlobalPosition - Vector3.Up * .1f, 0, 0);
				pickup.SetPhysicsProcess(true);
				await Frames(4);
				Check(!GodotObject.IsInstanceValid(pickup) || pickup.Taken, "physical overlap picks up " + group.Key);
				if (_player.Inventory.Weapons.OfType<FirearmView>().FirstOrDefault(w => WeaponPickup.ClassOf(w) == group.Key) is { } gun)
				{
					Check(gun.MagazineAmmo == gun.MagazineCapacity, "map pickup has a full magazine: " + group.Key);
					Check(gun.ReserveAmmo == gun.ReserveCapacity, "map pickup has full reserve for reloading: " + group.Key);
				}
			}
			var breakables = map.GetChildren().OfType<AnimatableBody3D>().Where(n => n.GetMeta("source_class").AsString() == "func_breakable").ToArray();
			Check(breakables.Length == 9, "nine authored breakable skylights");
			_player.Inventory.EquipSlot(WeaponSlot.Melee);
			foreach (var brush in breakables)
			{
				var hull = (ConvexPolygonShape3D)brush.GetChildren().OfType<CollisionShape3D>().First().Shape;
				var min = hull.Points.Aggregate(Vector3.Inf, (a, p) => a.Min(p));
				var max = hull.Points.Aggregate(-Vector3.Inf, (a, p) => a.Max(p));
				var center = brush.ToGlobal((min + max) / 2);
				var start = center + Vector3.Down * ((max.Y - min.Y) / 2 + .5f);
				_player.SourceTeleport(start - Vector3.Up * _player.Camera.Position.Y, 0, 0);
				_player.Camera.GlobalPosition = start;
				_player.Camera.LookAt(center, Vector3.Right);
				_player.Combat.QueueKnife(_player.ActiveWeapon!, false);
				await Frames(15);
				Check(brush.Get("broken").AsBool() && !brush.Visible && brush.CollisionLayer == 0, "actual knife trace breaks " + brush.Name);
			}
			GD.Print($"ARENA_2000_CHECKS_OK checks={_checks}");
			GetTree().Quit();
		}
		catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
	}
}
