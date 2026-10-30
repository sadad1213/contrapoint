using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Contrapoint.Players;

// Shared local first-person audio. Sources and MDL event cycles are extracted by
// imported from CSS sound scripts, never guessed from animation names.
public partial class SourceSoundPlayer : Node
{
	public static event Action<Player, Vector3, string>? Emitted;
	public static void NotifySound(Player source, Vector3 position, string path) => Emitted?.Invoke(source, position, path);
	public bool Enabled { get; set; } = true;
	public int SoundsPlayed { get; private set; }
	public string LastSound { get; private set; } = "";
	private const string Root = "res://assets/audio/css/";
	private static readonly JsonElement Bank = JsonDocument.Parse(FileAccess.GetFileAsString(Root + "bank.json")).RootElement.Clone();
	private static readonly Dictionary<string, AudioStream> Streams = new();
	private static int _users;
	private readonly List<AudioStreamPlayer> _voices = new(8);
	private readonly RandomNumberGenerator _random = new();
	private JsonElement _events;
	private int _eventIndex;
	private double _clipLength;

	public override void _EnterTree() => _users++;
	internal static void RetainWorldVoice() => _users++;
	internal static void ReleaseWorldVoice()
	{
		if (--_users != 0) return;
		foreach (var stream in Streams.Values) stream.Dispose();
		Streams.Clear();
	}
	private static AudioStream LoadStream(string path)
	{
		if (!Streams.TryGetValue(path, out var stream))
		{
			stream = GD.Load<AudioStream>(Root + path) ?? throw new InvalidOperationException($"Missing CSS audio: {path}");
			Streams.Add(path, stream);
		}
		return stream;
	}

	public override void _Ready()
	{
		_random.Randomize();
		for (var i = 0; i < 8; i++) AddVoice();
	}

	private AudioStreamPlayer AddVoice()
	{
		var voice = new AudioStreamPlayer();
		AddChild(voice);
		_voices.Add(voice);
		return voice;
	}

	public void PlayScript(string name, float gain = 1, bool relay = true)
	{
		if (!Enabled) return;
		name = name.ToLowerInvariant();
		if (!Bank.GetProperty("sounds").TryGetProperty(name, out var definition))
			throw new InvalidOperationException($"Unknown CSS sound: {name}");
		var paths = definition.GetProperty("paths");
		var path = paths[(int)_random.RandiRange(0, paths.GetArrayLength() - 1)].GetString()!;
		PlayWave(path, gain * definition.GetProperty("volume").GetSingle(), name, relay);
	}

	public void PlayWave(string path, float gain = 1, string? label = null, bool relay = true)
	{
		if (!Enabled || gain <= 0) return;
		Player? owner = null;
		for (Node? ancestor = GetParent(); ancestor != null; ancestor = ancestor.GetParent())
			if (ancestor is Player player) { owner = player; break; }
		if (relay && owner != null) NotifySound(owner, owner.GlobalPosition + Vector3.Up, path);
		var stream = LoadStream(path);
		if (owner?.IsBot == true)
		{
			PlayWorldWave(owner.GetParent<Node3D>(), owner.GlobalPosition + Vector3.Up, path, gain);
			SoundsPlayed++; LastSound = label ?? path;
			if (relay) Contrapoint.Network.LanSession.Instance?.SendSound(owner, path, gain);
			return;
		}
		// Reuse finished voices; an overlapping shot/deploy must keep its tail.
		var voice = _voices.Find(candidate => !candidate.Playing) ?? AddVoice();
		voice.Stream = stream;
		voice.VolumeDb = Mathf.LinearToDb(Mathf.Max(0.001f, gain)) - 8;
		voice.Play();
		SoundsPlayed++;
		LastSound = label ?? path;
		for (Node? ancestor = relay ? GetParent() : null; ancestor != null; ancestor = ancestor.GetParent())
			if (ancestor is Player player) { if (player.LocalInput) Contrapoint.Network.LanSession.Instance?.SendSound(player, path, gain); break; }
	}

	public static AudioStreamPlayer3D PlayWorldWave(Node3D parent, Vector3 position, string path, float gain)
	{
		var voice = new SourceWorldSound { Stream = LoadStream(path), VolumeDb = Mathf.LinearToDb(Mathf.Max(.001f, gain)) - 8,
			UnitSize = 1, MaxDistance = 50, MaxDb = -6 };
		if (path.StartsWith("player/footsteps/", StringComparison.OrdinalIgnoreCase))
		{
			// Source steps use a linear distance rolloff. MaxDistance already
			// supplies that in Godot; inverse-distance attenuation on top made
			// other players' steps almost inaudible just a few metres away.
			voice.AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.Disabled;
			voice.MaxDistance = 1000 * SourceMovement.Unit;
			voice.AttenuationFilterDb = -6;
		}
		parent.AddChild(voice); voice.GlobalPosition = position; voice.Finished += voice.QueueFree; voice.Play();
		return voice;
	}

	public static AudioStreamPlayer3D PlayWorld(Node3D parent, Vector3 position, string name)
	{
		var definition = Bank.GetProperty("sounds").GetProperty(name.ToLowerInvariant());
		var paths = definition.GetProperty("paths");
		var path = paths[GD.RandRange(0, paths.GetArrayLength() - 1)].GetString()!;
		var level = definition.TryGetProperty("soundlevel", out var db) ? db.GetSingle() : 75;
		var attenuation = level > 50 ? 20 / (level - 50) : 1;
		var pitch = definition.TryGetProperty("pitch", out var range)
			? (float)GD.RandRange(range[0].GetDouble(), range[1].GetDouble()) / 100 : 1;
		var voice = new SourceWorldSound
		{
			Stream = LoadStream(path),
			VolumeDb = Mathf.LinearToDb(definition.GetProperty("volume").GetSingle()) + level - 75 - 8,
			UnitSize = 36 * SourceMovement.Unit, MaxDistance = 1000 * SourceMovement.Unit / attenuation,
			MaxDb = -6,
			PitchScale = pitch,
			Bus = "Master"
		};
		if (name.Equals("basegrenade.explode", StringComparison.OrdinalIgnoreCase))
		{
			// Broad blast up close, then a continuous distance falloff. The old
			// +17 dB source hit MaxDb for ~13 m and cut out completely at 63.5 m.
			voice.VolumeDb = Mathf.LinearToDb(definition.GetProperty("volume").GetSingle()) - 6;
			voice.UnitSize = 6;
			voice.MaxDistance = 0;
		}
		parent.AddChild(voice);
		voice.SetMeta("source_sound", name);
		voice.GlobalPosition = position;
		voice.Finished += voice.QueueFree;
		voice.Play();
		return voice;
	}

	public void StartAnimation(string weapon, string clip, double length)
	{
		CancelAnimation();
		if (Bank.GetProperty("animations").TryGetProperty(weapon, out var clips) && clips.TryGetProperty(clip, out _events))
		{
			_clipLength = length;
			AdvanceAnimation(0);
		}
	}

	public void AdvanceAnimation(double position)
	{
		if (_events.ValueKind != JsonValueKind.Array) return;
		while (_eventIndex < _events.GetArrayLength())
		{
			var item = _events[_eventIndex];
			if (item.GetProperty("cycle").GetDouble() * _clipLength > position + 0.000001) break;
			PlayScript(item.GetProperty("sound").GetString()!);
			_eventIndex++;
		}
	}

	public void CancelAnimation()
	{
		_events = default;
		_eventIndex = 0;
	}

	public void StopAll()
	{
		CancelAnimation();
		foreach (var voice in _voices) voice.Stop();
	}

	public override void _ExitTree()
	{
		StopAll();
		foreach (var voice in _voices) voice.Stream = null;
		ReleaseWorldVoice();
	}

	public static string SurfaceForMaterial(string name)
	{
		foreach (var suffix in new[] { "_lightmap", "_vertex", "_decal" })
			if (name.EndsWith(suffix, StringComparison.Ordinal)) name = name[..^suffix.Length];
		return Bank.GetProperty("surfaces").TryGetProperty(name, out var surface) ? surface.GetString()! : "concrete";
	}
}
