using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Contrapoint.Players;
using Contrapoint.Weapons;

// Run headless with --audio-driver Dummy: a multichannel device interleaves
// additional speaker pairs into AudioEffectCapture's shared ring buffer.
public partial class ExplosionChecks : Node3D
{
	private int _failures;
	private Camera3D _camera = null!;
	private void Check(bool pass, string message)
	{
		if (!pass) { _failures++; GD.PushError(message); }
	}
	public override async void _Ready()
	{
		AudioServer.SetBusMute(0, true);
		_camera = new Camera3D { Current = true, Position = new(0, 2, 10) };
		AddChild(_camera);
		GetViewport().AudioListenerEnable3D = true;
		try
		{
			await CheckHaze(false);
			await CheckHaze(true);
			await CheckAudio();
		}
		catch (Exception error) { Check(false, error.ToString()); }
		GD.Print($"EXPLOSION_CHECKS failures={_failures}");
		GetTree().Quit(_failures == 0 ? 0 : 1);
	}
	private async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
	private async Task CheckHaze(bool interior)
	{
		var ceiling = new StaticBody3D { Position = Vector3.Up * 4 };
		if (interior)
		{
			ceiling.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(4, 0.2f, 4) } });
			AddChild(ceiling);
		}
		await Frame(); await Frame();
		var effect = new CssoExplosion();
		AddChild(effect);
		effect.SetPhysicsProcess(false); effect.SetProcess(false);
		effect._PhysicsProcess(1.0 / 66);
		var previous = new Dictionary<MeshInstance3D, float>();
		var pops = 0;
		var worst = 0f;
		for (var frame = 0; frame < 2400 && !effect.IsQueuedForDeletion(); frame++)
		{
			effect._Process(1.0 / 240);
			foreach (var child in effect.GetChildren())
			{
				if (child is not MeshInstance3D mesh) continue;
				var alpha = mesh.Visible ? mesh.GetInstanceShaderParameter("tint").AsColor().A : 0;
				if (previous.TryGetValue(mesh, out var old) && !mesh.Visible && old > 0.02f) { pops++; worst = Math.Max(worst, old); }
				previous[mesh] = alpha;
			}
		}
		Check(pops == 0, $"Visible layers must fade before removal ({interior}): pops={pops}, worst={worst}");
		Check(effect.IsQueuedForDeletion(), "Explosion must finish after every particle fades.");
		GD.Print($"HAZE_CHECK interior={interior} particles={previous.Count} pops={pops}");
		if (!effect.IsQueuedForDeletion()) effect.QueueFree();
		if (interior) ceiling.QueueFree(); else ceiling.Free();
		await Frame();
	}
	private async Task CheckAudio()
	{
		GD.Print($"AUDIO_DEVICE mode={AudioServer.GetSpeakerMode()} rate={AudioServer.GetMixRate()} device={AudioServer.OutputDevice}");
		if (AudioServer.GetSpeakerMode() != AudioServer.SpeakerMode.ModeStereo)
			throw new InvalidOperationException("Run the audio checks with --headless --audio-driver Dummy to capture a single stereo pair.");
		var bus = AudioServer.BusCount;
		AudioServer.AddBus();
		AudioServer.SetBusName(bus, "ExplosionChecks");
		var capture = new AudioEffectCapture { BufferLength = 1 };
		AudioServer.AddBusEffect(bus, capture);
		_camera.Position = Vector3.Zero;
		_camera.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
		var listener = new AudioListener3D();
		_camera.AddChild(listener);
		listener.MakeCurrent();
		async Task<Vector2> Measure(Vector3 position, float yaw = 0)
		{
			_camera.Rotation = new(0, yaw, 0);
			await Frame();
			await ToSignal(GetTree().CreateTimer(0.2), SceneTreeTimer.SignalName.Timeout);
			capture.ClearBuffer();
			GD.Seed(12345); // Same wave and pitch for each direction/distance.
			var voice = SourceSoundPlayer.PlayWorld(this, position, "basegrenade.explode");
			voice.Bus = "ExplosionChecks";
			Check(voice.Stream is AudioStreamWav { Stereo: false }, "A point explosion must import as mono.");
			await ToSignal(GetTree().CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
			var samples = capture.GetBuffer(capture.GetFramesAvailable());
			var energy = Vector2.Zero;
			foreach (var sample in samples) energy += sample * sample;
			voice.QueueFree();
			await Frame();
			await ToSignal(GetTree().CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
			GD.Print($"AUDIO_CHECK position={position} yaw={yaw} frames={samples.Length} energy={energy}");
			return energy / Math.Max(1, samples.Length);
		}
		var left = await Measure(new(-8, 0, -8));
		var right = await Measure(new(8, 0, -8));
		var rotated = await Measure(new(8, 0, -8), Mathf.Pi);
		var front = await Measure(new(0, 0, -8));
		var far = await Measure(new(0, 0, -64));
		Check(left.X > left.Y * 1.25f && right.Y > right.X * 1.25f, "Explosions must pan to their actual side.");
		Check(rotated.X > rotated.Y * 1.25f, "Panning must follow camera rotation.");
		Check(front.X > 0 && Math.Abs(front.X - front.Y) < front.X * 0.05f, "A centered explosion must balance both ears.");
		Check(far.X > 0 && far.X < front.X * 0.5f, "A distant explosion must attenuate without a hard range cutoff.");
		AudioServer.RemoveBus(bus);
	}
}
