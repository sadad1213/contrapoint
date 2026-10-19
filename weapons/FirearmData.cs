using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Contrapoint.Weapons;

// Gameplay values are decrypted from the installed vanilla CSS weapon scripts.
internal sealed class FirearmData
{
	private static readonly Dictionary<FirearmView.FirearmKind, FirearmData> Definitions = Load();
	public int Magazine { get; init; }
	public int Reserve { get; init; }
	public int Pellets { get; init; }
	public AmmunitionType Ammo { get; init; }
	public float Speed { get; init; }
	public double Cycle { get; init; }
	public bool Automatic { get; init; }
	public string Sound { get; init; } = "";
	public string Extension { get; init; } = "";
	public static FirearmData For(FirearmView.FirearmKind kind) => Definitions[kind];
	private static Dictionary<FirearmView.FirearmKind, FirearmData> Load()
	{
		using var document = JsonDocument.Parse(FileAccess.GetFileAsString("res://assets/physics/css_firearms.json"));
		var result = new Dictionary<FirearmView.FirearmKind, FirearmData>();
		foreach (var entry in document.RootElement.EnumerateObject())
		{
			var data = entry.Value;
			var ammo = data.GetProperty("ammo").GetString() switch
			{
				"BULLET_PLAYER_762MM" => AmmunitionType.Nato762,
				"BULLET_PLAYER_556MM" => AmmunitionType.Nato556,
				"BULLET_PLAYER_556MM_BOX" => AmmunitionType.Nato556Box,
				"BULLET_PLAYER_45ACP" => AmmunitionType.Acp45,
				"BULLET_PLAYER_50AE" => AmmunitionType.Ae50,
				"BULLET_PLAYER_338MAG" => AmmunitionType.Magnum338,
				"BULLET_PLAYER_9MM" => AmmunitionType.Mm9,
				"BULLET_PLAYER_357SIG" => AmmunitionType.Sig357,
				"BULLET_PLAYER_57MM" => AmmunitionType.Mm57,
				"BULLET_PLAYER_BUCKSHOT" => AmmunitionType.Buckshot,
				_ => throw new InvalidOperationException("Unknown CSS ammunition: " + entry.Name)
			};
			result.Add(Enum.Parse<FirearmView.FirearmKind>(entry.Name, true), new()
			{
				Magazine = data.GetProperty("magazine").GetInt32(), Reserve = data.GetProperty("reserve").GetInt32(),
				Pellets = data.GetProperty("pellets").GetInt32(), Ammo = ammo,
				Speed = data.GetProperty("speed").GetSingle(), Cycle = data.GetProperty("cycle").GetDouble(),
				Automatic = data.GetProperty("automatic").GetBoolean(), Sound = data.GetProperty("sound").GetString()!,
				Extension = data.GetProperty("animation_extension").GetString()!
			});
		}
		return result;
	}
}
