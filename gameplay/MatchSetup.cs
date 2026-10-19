using Godot;
using System;
using System.Linq;
using System.Text.Json;
using Contrapoint.Players;

namespace Contrapoint.Gameplay;

public sealed record BotSetup
{
	public string Difficulty { get; init; } = "none";
	public string QuotaMode { get; init; } = "normal";
	public int Quota { get; init; }
	public string Prefix { get; init; } = "";
	public string Team { get; init; } = "any";
	public string Chatter { get; init; } = "normal";
	public bool NewVoices { get; init; } = true;
	public bool Controllable { get; init; } = true;
	public bool JoinAfterPlayer { get; init; } = true;
	public bool DeferGoals { get; init; } = true;
	public bool DeferItems { get; init; } = true;
	public bool AllowRogues { get; init; } = true;
	public bool VoicePitch { get; init; } = true;
	public bool Pistols { get; init; } = true;
	public bool Shotguns { get; init; } = true;
	public bool SubmachineGuns { get; init; } = true;
	public bool Rifles { get; init; } = true;
	public bool MachineGuns { get; init; } = true;
	public bool Grenades { get; init; } = true;
	public bool Snipers { get; init; } = true;
	public bool Stop { get; init; }
	public bool DontShoot { get; init; }
}

// Immutable launch snapshot; UI drafts cannot alter a running match.
public sealed record MatchSetup
{
	public string ModeId { get; init; } = "sandbox";
	public string MapId { get; init; } = "de_dust2";
	public bool AllMaps { get; init; } = true;
	public bool FriendlyFire { get; init; } = true;
	public bool FallDamage { get; init; } = true;
	public bool Cheats { get; init; }
	public BotSetup Bots { get; init; } = new();
}

public static class MatchSession
{
	private static string ProfilePath => OS.GetCmdlineUserArgs().Contains("--menu-test-profile")
		? "user://menu_test_setup.json" : "user://match_setup.json";
	public static MatchSetup Draft { get; set; } = Load();
	public static MatchSetup? Current { get; private set; }
	private static bool _applyOnSpawn;

	private static MatchSetup Load()
	{
		try
		{
			if (!FileAccess.FileExists(ProfilePath)) return new();
			var setup = JsonSerializer.Deserialize<MatchSetup>(FileAccess.GetFileAsString(ProfilePath)) ?? new();
			return setup with { ModeId = GameModeCatalog.Find(setup.ModeId).Id, Bots = (setup.Bots ?? new()) with
				{ Quota = Math.Clamp(setup.Bots?.Quota ?? 0, 0, 15), Prefix = setup.Bots?.Prefix ?? "" } };
		}
		catch (Exception error) when (error is JsonException or NotSupportedException)
		{
			GD.PushWarning("Could not read match setup: " + error.Message);
			return new();
		}
	}

	public static bool Save()
	{
		using var file = FileAccess.Open(ProfilePath, FileAccess.ModeFlags.Write);
		if (file == null) return false;
		file.StoreString(JsonSerializer.Serialize(Draft, new JsonSerializerOptions { WriteIndented = true }));
		return file.GetError() == Error.Ok;
	}

	public static void Begin(MatchSetup setup)
	{
		var mode = GameModeCatalog.Find(setup.ModeId);
		if (mode.Id != setup.ModeId || !mode.Available) throw new InvalidOperationException("Mode is not implemented.");
		Current = setup;
		_applyOnSpawn = true;
	}

	// Apply after console initialization, which restores its own archived variables.
	public static void ApplyPendingRules()
	{
		// Console and LAN scene loads have no menu launch snapshot.
		if (!_applyOnSpawn) { Current = null; RoundState.Instance?.BeginMatch("sandbox"); return; }
		if (Current == null) return;
		_applyOnSpawn = false;
		SourceDamageRules.FriendlyFire = Current.FriendlyFire;
		SourceDamageRules.FallDamageEnabled = Current.FallDamage;
		SourceDamageRules.Cheats = Current.Cheats;
		RoundState.Instance?.BeginMatch(Current.ModeId);
	}
}
