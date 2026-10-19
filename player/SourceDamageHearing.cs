using Godot;

namespace Contrapoint.Players;

// CCSPlayer::OnDamagedByExplosion / ApplyDeafnessEffect, DSP presets 137-139.
// The DSP network is represented by Godot's low-pass filter and a 3 kHz tone.
public partial class SourceDamageHearing : Node
{
	public int Preset { get; private set; }
	public bool Active => _elapsed < 4.5;
	private double _elapsed = 5, _phase;
	private AudioEffectLowPassFilter? _filter;
	private AudioStreamPlayer? _tone;
	private AudioStreamGeneratorPlayback? _playback;
	private int FilterIndex()
	{
		for (var i = 0; i < AudioServer.GetBusEffectCount(0); i++) if (AudioServer.GetBusEffect(0, i) == _filter) return i;
		return -1;
	}
	public void Shock() { _elapsed = -.3; }
	public static int PresetAt(double elapsed) => elapsed < 0 || elapsed >= 4.5 ? 0
		: elapsed < .1 ? 137 + (int)(elapsed / .05)
		: elapsed < 3 ? 139 : 138 - (int)((elapsed - 3) / .75);
	public void Reset()
	{
		_elapsed = 5; Preset = 0;
		var index = FilterIndex(); if (index >= 0) AudioServer.SetBusEffectEnabled(0, index, false);
		StopTone();
	}
	private void StopTone()
	{
		_tone?.Stop(); _playback?.Dispose(); _playback = null;
	}
	public override void _Process(double delta)
	{
		_elapsed += delta;
		var preset = PresetAt(_elapsed);
		if (preset != Preset)
		{
			Preset = preset;
			if (GetParent<Player>().LocalInput)
			{
				if (_filter == null && preset > 0)
				{
					_filter = new AudioEffectLowPassFilter { Resonance = .5f };
					AudioServer.AddBusEffect(0, _filter);
					_tone = new AudioStreamPlayer { Stream = new AudioStreamGenerator { MixRate = 22050, BufferLength = .1f }, VolumeDb = -8 };
					AddChild(_tone);
				}
				if (_filter != null)
				{
					AudioServer.SetBusEffectEnabled(0, FilterIndex(), preset > 0);
					_filter.CutoffHz = preset == 139 ? 1000 : preset == 138 ? 2000 : 4000;
					if (preset > 0 && !_tone!.Playing) { _tone.Play(); _playback = (AudioStreamGeneratorPlayback)_tone.GetStreamPlayback(); }
					if (preset == 0) StopTone();
				}
			}
		}
		if (_playback == null || Preset == 0) return;
		var frames = _playback.GetFramesAvailable();
		var amplitude = (Preset - 136) * .01f;
		for (var i = 0; i < frames; i++)
		{
			var sample = (float)System.Math.Sin(_phase) * amplitude;
			_playback.PushFrame(new(sample, sample)); _phase = (_phase + System.Math.Tau * 3000 / 22050) % System.Math.Tau;
		}
	}
	public override void _ExitTree()
	{
		StopTone();
		if (_tone != null) { var stream = _tone.Stream; _tone.Stream = null; stream?.Dispose(); }
		var index = FilterIndex(); if (index >= 0) AudioServer.RemoveBusEffect(0, index);
		_filter?.Dispose(); _filter = null;
	}
}
