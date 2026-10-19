using Godot;
using System;
using System.Threading.Tasks;
using Contrapoint.Players;

public partial class StairChecks : Node3D
{
	private Player _player = null!;
	private static Vector3 Units(float x, float y, float z) => new Vector3(x, y, z) * SourceMovement.Unit;
	private async Task Tick(PlayerCommand command = default)
	{
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
		_player.Simulate(command, 1f / 66);
	}
	public override async void _Ready()
	{
		try
		{
			var map = GD.Load<PackedScene>("res://assets/maps/de_cpl_strike/de_cpl_strike.scn").Instantiate<Node3D>();
			AddChild(map);
			_player = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
			_player.LocalInput = false;
			AddChild(_player);
			foreach (var stance in new[] { (Walk: false, Duck: false), (Walk: true, Duck: false), (Walk: false, Duck: true) })
			foreach (var route in new[]
			{
				(Units(-1624, -296, -1196), Units(-1416, -192, -1196)),
				(Units(-1784, -296, -728), Units(-2080, -168, -728)),
				(Units(-1284, -208, -1088), Units(-1284, -80, -832)),
				(Units(-880, -368, -416), Units(-360, -80, -416)),
			})
			{
				if ((stance.Walk || stance.Duck) && route.Item1.X != -1624 * SourceMovement.Unit) continue;
				var ray = PhysicsRayQueryParameters3D.Create(route.Item1 + Vector3.Up, route.Item1 + Vector3.Down * 3, 3);
				ray.Exclude = new() { _player.GetRid() };
				await Tick();
				var floor = GetWorld3D().DirectSpaceState.IntersectRay(ray);
				if (floor.Count == 0) throw new InvalidOperationException("No route start floor at " + route.Item1);
				var start = floor["position"].AsVector3() + Vector3.Up * .003f;
				_player.SourceTeleport(start, 0, 0);
				for (var i = 0; i < 10; i++) await Tick();
				var direction = route.Item2 - route.Item1;
				direction.Y = 0;
				var distance = direction.Length();
				direction = direction.Normalized();
				var command = new PlayerCommand(new(direction.X, direction.Z), false, stance.Duck, stance.Walk);
				var stalled = 0;
				var worst = 0;
				var speed = 250 * SourceMovement.Unit * (stance.Duck ? .34f : stance.Walk ? .52f : 1);
				var maxFrames = (int)((distance / speed + 1) * 66);
				for (var frame = 0; frame < maxFrames && (_player.GlobalPosition - start).Dot(direction) < distance; frame++)
				{
					var before = _player.GlobalPosition;
					await Tick(command);
					if ((_player.GlobalPosition - before).Dot(direction) < .002f) worst = Math.Max(worst, ++stalled); else stalled = 0;
				}
				var progress = (_player.GlobalPosition - start).Dot(direction);
				GD.Print($"STAIR_ROUTE walk={stance.Walk} duck={stance.Duck} start={start / SourceMovement.Unit} end={_player.GlobalPosition / SourceMovement.Unit} progress={progress:F3}/{distance:F3} stalled={worst}");
				if (progress < distance - .1f || worst > 3) throw new InvalidOperationException("Stair climb stalled without strafing");
			}
			GD.Print("STAIR_CHECKS_OK");
			GetTree().Quit();
		}
		catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
	}
}
