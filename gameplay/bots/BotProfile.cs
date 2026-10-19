using Godot;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Contrapoint.Gameplay.Bots;

// Values and inheritance are extracted from the installed CS:S botprofile.db.
public sealed record BotProfile
{
	[JsonPropertyName("name")] public string Name { get; init; } = "Bot";
	[JsonPropertyName("skill")] public float Skill { get; init; }
	[JsonPropertyName("aggression")] public float Aggression { get; init; }
	[JsonPropertyName("reaction")] public float Reaction { get; init; }
	[JsonPropertyName("attack_delay")] public float AttackDelay { get; init; }
	[JsonPropertyName("difficulty")] public string[] Difficulty { get; init; } = [];
	[JsonPropertyName("weapons")] public string[] Weapons { get; init; } = [];
	[JsonPropertyName("skin")] public int Skin { get; init; }
	private static BotProfile[]? _profiles;
	public static BotProfile[] ForDifficulty(string difficulty) =>
		(_profiles ??= JsonSerializer.Deserialize<BotProfile[]>(FileAccess.GetFileAsString("res://assets/bots/profiles.json"))
			?? throw new InvalidOperationException("Missing CSS bot profiles.")).Where(p => p.Difficulty.Contains(difficulty)).ToArray();
}
