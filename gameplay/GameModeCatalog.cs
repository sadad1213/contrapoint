using System;
using System.Collections.Generic;
using System.Linq;
using Contrapoint.Maps;

namespace Contrapoint.Gameplay;

// CS:SO gamemodes.txt separates types from modes. Availability belongs here,
// so a future rules implementation does not need a second menu or map list.
public static class GameModeCatalog
{
	public sealed record GameType(string Id, string Title);
	public sealed record Mode(string Id, string TypeId, string Title, string Description, bool Available = false,
		bool IncludesMovementMaps = false)
	{
		public bool Supports(MapCatalog.Entry map) => Id == "defuse" ? map.Id.StartsWith("de_", StringComparison.Ordinal) : IncludesMovementMaps || map.IsCombat;
	}
	public static IReadOnlyList<GameType> Types { get; } = new GameType[]
	{
		new("classic", "Classic"), new("gungame", "Arsenal"),
		new("training", "Training"), new("custom", "Custom")
	};
	public static IReadOnlyList<Mode> Modes { get; } = new Mode[]
	{
		new("defuse", "classic", "Bomb Defusal", "Counter-Strike: Source. Plant or defuse the C4; rounds, buying and team economy.", true),
		new("casual", "classic", "Casual", "Classic team play with rounds and map objectives."),
		new("competitive", "classic", "Competitive", "A 5v5 match with an economy and side switching."),
		new("competitive2v2", "classic", "Wingman", "A 2v2 team match."),
		new("armsrace", "gungame", "Arms Race", "Earn new weapons with kills and respawn instantly."),
		new("demolition", "gungame", "Demolition", "Short rounds with a fixed weapon progression."),
		new("deathmatch", "gungame", "Deathmatch", "Free-for-all. Fast respawns, armor, +15 HP and magazine ammo per kill.", true),
		new("teamdeathmatch", "gungame", "Team Deathmatch", "T vs CT. Fast respawns, armor, +15 HP and magazine ammo per enemy kill.", true),
		new("training", "training", "Training", "Practice shooting and map objectives.", IncludesMovementMaps: true),
		new("sandbox", "custom", "Free Play", "Explore any installed map, including KZ.", true, true)
	};
	public static Mode Find(string? id) => Modes.FirstOrDefault(mode => mode.Id == id) ?? Modes.Single(mode => mode.Id == "sandbox");
}
