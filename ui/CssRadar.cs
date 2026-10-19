using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using Contrapoint.Players;

namespace Contrapoint.UI;

public partial class CssRadar : Control
{
	public bool Enabled { get; set; } = true;
	public bool RotateMap { get; set; } = true;
	public float RangeUnits { get; set; } = 1500;
	public float MapAlpha { get; set; } = 200 / 255f;
	public bool HasOverview => _map != null;
	public string MapId { get; private set; } = "";
	private Player _owner = null!;
	private Player _player => _owner.HudPlayer;
	private Texture2D? _map;
	private Vector2 _origin;
	private float _mapScale;
	private readonly record struct DrawState(Vector2 WindowSize, Vector3 Position, float Angle, float Range, float Alpha, bool Enabled);
	private DrawState? _drawState;
	private readonly List<(ulong Id, Vector3 Position)> _contacts = new();
	private float HudScale => GetWindow().Size.Y / 480f;
	private Vector2 Center => new Vector2(72, 72) * HudScale;
	private float Radius => 64 * HudScale;
	private float Angle => RotateMap ? _player.GlobalRotation.Y : 0;

	public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;
	public void Bind(Player player)
	{
		_owner = player;
		var root = player.SpawnPointsRootPath.IsEmpty ? null : player.GetNodeOrNull<Node>(player.SpawnPointsRootPath);
		MapId = root == null ? "" : System.IO.Path.GetFileNameWithoutExtension(root.SceneFilePath);
		var texture = $"res://assets/ui/css/hud/{MapId}_radar.png";
		if (!ResourceLoader.Exists(texture)) return;
		using var document = JsonDocument.Parse(FileAccess.GetFileAsString("res://assets/ui/css/hud/overviews.json"));
		if (!document.RootElement.TryGetProperty(MapId, out var overview)) return;
		_origin = new(overview.GetProperty("pos_x").GetSingle(), overview.GetProperty("pos_y").GetSingle());
		_mapScale = overview.GetProperty("scale").GetSingle();
		_map = GD.Load<Texture2D>(texture);
	}
	public override void _Process(double delta)
	{
		if (!IsVisibleInTree() || _map == null || _player == null) return;
		var state = new DrawState(GetWindow().Size, _player.GlobalPosition, Angle, RangeUnits, MapAlpha, Enabled);
		var changed = state != _drawState;
		var count = 0;
		foreach (Player other in GetTree().GetNodesInGroup("source_players"))
		{
			if (!Shows(other)) continue;
			var contact = (other.GetInstanceId(), other.GlobalPosition);
			if (count >= _contacts.Count) { _contacts.Add(contact); changed = true; }
			else if (_contacts[count] != contact) { _contacts[count] = contact; changed = true; }
			count++;
		}
		if (count < _contacts.Count) { _contacts.RemoveRange(count, _contacts.Count - count); changed = true; }
		_drawState = state;
		if (changed || Contrapoint.Gameplay.DefuseRules.Active) QueueRedraw();
	}
	public Vector2 WorldToOverview(Vector3 world) => new((world.X / SourceMovement.Unit - _origin.X) / _mapScale,
		(_origin.Y + world.Z / SourceMovement.Unit) / _mapScale);
	public Vector2 WorldToRadar(Vector3 world)
	{
		var relative = new Vector2(world.X - _player.GlobalPosition.X, world.Z - _player.GlobalPosition.Z);
		return Center + relative.Rotated(Angle) * (Radius / (RangeUnits * SourceMovement.Unit));
	}
	public bool Shows(Player other) => other.IsAlive && Contrapoint.Gameplay.DeathmatchRules.Teammates(_player, other)
		&& other.Team is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist;

	public override void _Draw()
	{
		if (_map == null || !Enabled || _player == null) return;
		var center = Center;
		var radius = Radius;
		DrawCircle(center, radius, new Color(0, 0, 0, .78f));
		const int segments = 96;
		var vertices = new Vector2[segments];
		var uv = new Vector2[segments];
		var mapCenter = WorldToOverview(_player.GlobalPosition);
		var mapRadius = RangeUnits / _mapScale;
		for (var i = 0; i < segments; i++)
		{
			var unit = Vector2.FromAngle(i * Mathf.Tau / segments);
			vertices[i] = center + unit * (radius - HudScale);
			uv[i] = (mapCenter + unit.Rotated(-Angle) * mapRadius) / _map.GetSize();
		}
		DrawPolygon(vertices, new[] { new Color(1, 1, 1, MapAlpha) }, uv, _map);
		DrawArc(center, radius, 0, Mathf.Tau, segments, new Color(0, .35f, 0, .9f), Math.Max(1, HudScale), true);
		// Team state already comes from the player/network layer. Enemy detection is a separate future input.
		foreach (var other in GetTree().GetNodesInGroup("source_players").OfType<Player>().Where(Shows))
		{
			var delta = WorldToRadar(other.GlobalPosition) - center;
			var at = center + delta.LimitLength(radius - 5 * HudScale);
			DrawCircle(at, 2 * HudScale, new Color(1, .65f, 0));
			var height = other.GlobalPosition.Y - _player.GlobalPosition.Y;
			if (Math.Abs(height) > 128 * SourceMovement.Unit)
				DrawLine(at, at + new Vector2(0, height > 0 ? -5 : 5) * HudScale, new Color(1, .65f, 0), HudScale);
		}
		if (Contrapoint.Gameplay.DefuseRules.Active && Contrapoint.Gameplay.DefuseState.Instance is { } de && _player.Team == PlayerTeam.Terrorist)
		{
			var at = center + (WorldToRadar(de.BombPosition) - center).LimitLength(radius - 5 * HudScale);
			if (de.BombPlanted || de.Carrier == null && de.Phase != Contrapoint.Gameplay.DefusePhase.Waiting)
				DrawRect(new Rect2(at - Vector2.One * 3 * HudScale, Vector2.One * 6 * HudScale), new Color(1, 0, 0));
			else if (de.Carrier != null)
				DrawRect(new Rect2(at - Vector2.One * 3 * HudScale, Vector2.One * 6 * HudScale), new Color(1, .65f, 0), false, HudScale);
		}
		var rotation = RotateMap ? 0 : -_player.GlobalRotation.Y;
		var arrow = new[] { new Vector2(0, -5), new Vector2(-3, 3), new Vector2(0, 1), new Vector2(3, 3) }
			.Select(point => center + point.Rotated(rotation) * HudScale).ToArray();
		DrawColoredPolygon(arrow, Colors.White);
	}
}
