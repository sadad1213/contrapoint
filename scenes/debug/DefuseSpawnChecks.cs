using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.Gameplay;
using Contrapoint.Network;
using Contrapoint.Players;

public partial class DefuseSpawnChecks : Node
{
	public override async void _Ready()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		Reparent(GetTree().Root); GetTree().CurrentScene = null;
		var checks = 0;
		try
		{
			MatchSession.Begin(new MatchSetup { ModeId = "defuse", FallDamage = false });
			GetTree().ChangeSceneToFile("res://scenes/maps/dust2.tscn");
			while (LanSession.Instance!.LocalPlayer?.AwaitingInitialClass != true) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
			var p = LanSession.Instance.LocalPlayer!; p.JoinTeam(2); p.JoinClass(1);
			var root = p.GetNode<Node3D>(p.SpawnPointsRootPath);
			var spawns = root.FindChildren("Spawn_*", "Marker3D", true, false).OfType<Marker3D>()
				.Where(s => s.Name.ToString().StartsWith("Spawn_terrorist_") || s.Name.ToString().StartsWith("Spawn_counterterrorist_")).ToArray();
			foreach (var spawn in spawns)
			{
				p.Team = spawn.Name.ToString().Contains("counterterrorist") ? PlayerTeam.CounterTerrorist : PlayerTeam.Terrorist;
				p.SetSpawn(new(spawn.GlobalBasis, spawn.GlobalPosition + Vector3.Up * .04f)); p.Respawn();
				var start = p.GlobalPosition;
				await ToSignal(GetTree().CreateTimer(.12), SceneTreeTimer.SignalName.Timeout);
				checks++;
				if (!p.IsOnFloor() || Math.Abs(p.GlobalPosition.Y - start.Y) > .015f)
					throw new Exception($"{spawn.Name}: airborne or fell after spawning: marker={spawn.GlobalPosition}, start={start}, settled={p.GlobalPosition}, floor={p.IsOnFloor()}");
			}
			GD.Print($"DE_SPAWN_CHECKS checks={checks} failures=0"); GetTree().Quit();
		}
		catch (Exception e) { GD.PushError(e.ToString()); GD.Print($"DE_SPAWN_CHECKS checks={checks} failures=1"); GetTree().Quit(1); }
	}
}
