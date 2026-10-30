using Godot;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Contrapoint.Gameplay;
using Contrapoint.Maps;
using Contrapoint.Players;

namespace Contrapoint.UI;

// Geometry, artwork, colors and font heights are imported from the installed
// CSS resource/ui/scoreboard.res and resource/clientscheme.res (480-unit canvas).
public partial class CssScoreboard : Control
{
	private const string Assets = "res://assets/ui/css/scoreboard/";
	private JsonElement _layout, _strings;
	private Player _player = null!;
	private FontFile _regular = null!, _bold = null!, _icons = null!;
	private Texture2D _background = null!, _selection = null!, _dead = null!, _ctAvatar = null!, _tAvatar = null!;
	private float _scale;
	private Vector2 _center;
	private static readonly Color White = new(216 / 255f, 220 / 255f, 223 / 255f);
	private static readonly Color Blue = new(150 / 255f, 200 / 255f, 1);
	private static readonly Color Red = new(240 / 255f, 90 / 255f, 90 / 255f);
	public void Bind(Player player)
	{
		_player = player; MouseFilter = MouseFilterEnum.Ignore;
		using var document = JsonDocument.Parse(FileAccess.GetFileAsString(Assets + "scoreboard.json"));
		var data = document.RootElement;
		_layout = data.GetProperty("layout").Clone();
		_strings = data.GetProperty("strings").GetProperty("english").Clone();
		_regular = GD.Load<FontFile>(Assets + "verdana.ttf"); _bold = GD.Load<FontFile>(Assets + "verdanab.ttf");
		_icons = GD.Load<FontFile>("res://assets/ui/css/hud/cstrike.ttf");
		_background = GD.Load<Texture2D>(Assets + "panel-scoreboard.png");
		_selection = GD.Load<Texture2D>(Assets + "scoreboard-select.png");
		_dead = GD.Load<Texture2D>(Assets + "scoreboard_dead.png");
		_ctAvatar = GD.Load<Texture2D>(Assets + "avatar_default_64.png");
		_tAvatar = GD.Load<Texture2D>(Assets + "avatar_default-t_64.png");
	}
	public override void _Process(double delta) { if (Visible) QueueRedraw(); }
	private static float Value(JsonElement field, string key) => float.Parse(field.GetProperty(key).GetString()!.TrimStart('c'), CultureInfo.InvariantCulture);
	private Rect2 Field(string key)
	{
		var field = _layout.GetProperty(key);
		var x = field.GetProperty("xpos").GetString()!; var y = field.GetProperty("ypos").GetString()!;
		return new(new Vector2((x.StartsWith('c') ? _center.X : 0) + Value(field, "xpos") * _scale,
			(y.StartsWith('c') ? _center.Y : 0) + Value(field, "ypos") * _scale), new Vector2(Value(field, "wide"), Value(field, "tall")) * _scale);
	}
	private string Localize(string token) => _strings.TryGetProperty(token.TrimStart('#').ToLowerInvariant(), out var text) ? text.GetString()! : token;
	private void Text(Rect2 rect, string text, float height, Color color, bool bold = false, string align = "west", bool icon = false)
	{
		var font = icon ? _icons : bold ? _bold : _regular;
		var size = Math.Max(1, Mathf.RoundToInt(height * _scale / 1.129f));
		while (text.Length > 0 && font.GetStringSize(text, fontSize: size).X > rect.Size.X) text = text[..^1];
		var width = font.GetStringSize(text, fontSize: size).X;
		var x = align.EndsWith("east") ? rect.End.X - width : align == "center" ? rect.Position.X + (rect.Size.X - width) / 2 : rect.Position.X;
		var cell = font.GetHeight(size);
		var y = align.StartsWith("south") ? rect.End.Y - cell : rect.Position.Y + (rect.Size.Y - cell) / 2;
		DrawString(font, new Vector2(Mathf.Round(x), Mathf.Round(y + font.GetAscent(size))), text, fontSize: size, modulate: color);
	}
	private void Label(string name, string? text = null)
	{
		var field = _layout.GetProperty(name);
		text ??= Localize(field.GetProperty("labeltext").GetString()!);
		var font = field.GetProperty("font").GetString();
		var height = font switch { "ScoreboardHeader" or "ScoreboardColumns" => 8, "ScoreboardTeamName" => 14,
			"ScoreboardScore" => 45, "ScoreboardPlayersAlive" => 12, "IconsSmall" => 20, _ => 10 };
		var parts = field.GetProperty("fgcolor_override").GetString()!.Split(' ').Select(v => float.Parse(v, CultureInfo.InvariantCulture) / 255).ToArray();
		Text(Field(name), text, height, new(parts[0], parts[1], parts[2], parts[3]),
			font is not ("ScoreboardBody_1" or "IconsSmall"), field.TryGetProperty("textalignment", out var alignment) ? alignment.GetString()! : "west", font == "IconsSmall");
	}
	public override void _Draw()
	{
		if (_player == null) return;
		var size = GetViewportRect().Size;
		_scale = size.Y / 480f; _center = size / 2;
		DrawTextureRect(_background, Field("scoreboardbackground"), false);
		Label("mapname", MapCatalog.Find(GetTree().CurrentScene?.SceneFilePath ?? "")?.Id ?? "");
		Label("servernamelabel", "Contrapoint");
		Label("icon_clock");
		var round = RoundState.Instance!;
		Text(new Rect2(_center + new Vector2(227, -179) * _scale, new Vector2(45, 15) * _scale),
			round.TimerRunning ? $"{round.DisplaySeconds / 60}:{round.DisplaySeconds % 60:00}" : "", 8, new Color("ffb100"), true, "east");
		var players = GetTree().GetNodesInGroup("source_players").OfType<Player>().ToArray();
		DrawTeam("ct", PlayerTeam.CounterTerrorist, players, round.CounterTerroristKills);
		DrawTeam("t", PlayerTeam.Terrorist, players, round.TerroristKills);
		var observers = players.Where(p => !DeathmatchRules.Playing(p)).ToArray();
		Label("spectators", observers.Length == 0 ? Localize("Cstrike_Scoreboard_NoSpectators") :
			Localize(observers.Length == 1 ? "Cstrike_Scoreboard_Spectator" : "Cstrike_Scoreboard_Spectators")
				.Replace("%s1", observers.Length.ToString()).Replace("%s2", string.Join(", ", observers.Select(p => p.Name.ToString()))));
	}
	private void DrawTeam(string prefix, PlayerTeam team, Player[] players, int score)
	{
		Label(prefix + "_label", Localize(team == PlayerTeam.Terrorist ? "Cstrike_Team_T" : "Cstrike_Team_CT"));
		var roster = players.Where(p => p.Team == team).OrderByDescending(p => p.MatchKills).ThenBy(p => p.MatchDeaths).ThenBy(p => p.NetworkPeerId).ToArray();
		Label(prefix + "playersalive", roster.Count(p => p.IsAlive).ToString());
		var alive = Field(prefix + "playersalive");
		var suffix = new Rect2(new Vector2(team == PlayerTeam.Terrorist ? alive.Position.X - 84 * _scale : alive.End.X + 4 * _scale, alive.Position.Y), new Vector2(80, 12) * _scale);
		Text(suffix, Localize("Cstrike_Scoreboard_PlayersAlive"), 10, White, true, team == PlayerTeam.Terrorist ? "south-east" : "south-west");
		Label(prefix + "teamscore", score.ToString());
		foreach (var column in new[] { "clanlabel", "playerlabel", "playerscorelabel", "playerdeathslabel", "playerlatencylabel" }) Label(prefix + column);
		var rowHeight = roster.Length > 16 ? 12 : 16;
		for (var i = 0; i < roster.Length; i++)
		{
			var player = roster[i]; var shift = new Vector2(0, i * rowHeight * _scale);
			Rect2 Cell(string name) { var rect = Field(prefix + "player" + name + "0"); rect.Position += shift; return rect; }
			var tint = team == PlayerTeam.Terrorist ? Red : Blue;
			if (player == _player)
			{
				var area = Field(prefix + "playerarea");
				DrawTextureRect(_selection, new Rect2(new Vector2(area.Position.X, Cell("name").Position.Y), new Vector2(area.Size.X, 14 * _scale)), false);
			}
			DrawTextureRect(team == PlayerTeam.Terrorist ? _tAvatar : _ctAvatar, Cell("avatar"), false);
			Text(Cell("name"), player.DisplayName, 10, tint);
			Text(Cell("score"), player.MatchKills.ToString(), 10, tint, align: "center");
			Text(Cell("deaths"), player.MatchDeaths.ToString(), 10, tint, align: "center");
			Text(Cell("latency"), player.IsBot ? "BOT" : player.MatchPingMilliseconds.ToString(), 10, tint, align: "center");
			if (!player.IsAlive) DrawTextureRect(_dead, Cell("status"), false, tint);
		}
	}
}
