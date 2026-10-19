using Godot;
using System;
using System.Threading.Tasks;
using Contrapoint.Players;

// Run with --headless --audio-driver Dummy for a single captured stereo pair.
public partial class MovementAudioChecks : Node3D
{
	private int _failures;
	private void Check(bool condition, string message)
	{
		if (!condition) { _failures++; GD.PushError(message); }
	}
	private async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
	public override async void _Ready()
	{
		try { await Run(); }
		catch (Exception error) { Check(false, error.ToString()); }
		GD.Print($"MOVEMENT_AUDIO_CHECKS failures={_failures}");
		GetTree().Quit(_failures == 0 ? 0 : 1);
	}
	private async Task Run()
	{
		AudioServer.SetBusMute(0, true);
		GetViewport().AudioListenerEnable3D = true;
		var camera = new Camera3D { Current = true };
		AddChild(camera);
		var listener = new AudioListener3D();
		camera.AddChild(listener);
		listener.MakeCurrent();
		var bus = AudioServer.BusCount;
		AudioServer.AddBus();
		AudioServer.SetBusName(bus, "MovementAudioChecks");
		var capture = new AudioEffectCapture { BufferLength = 1 };
		AudioServer.AddBusEffect(bus, capture);
		// Keep the shared stream cache alive between one-shot voices.
		AddChild(new SourceSoundPlayer());
		async Task<Vector2> Measure(Vector3 position, float yaw = 0)
		{
			camera.Rotation = new(0, yaw, 0);
			await ToSignal(GetTree().CreateTimer(.15), SceneTreeTimer.SignalName.Timeout);
			capture.ClearBuffer();
			var voice = SourceSoundPlayer.PlayWorldWave(this, position, "player/footsteps/concrete1.wav", .5f);
			voice.Bus = "MovementAudioChecks";
			Check(voice.Stream is AudioStreamWav { Stereo: false }, "Footsteps must be mono for positional playback.");
			await ToSignal(GetTree().CreateTimer(.65), SceneTreeTimer.SignalName.Timeout);
			var samples = capture.GetBuffer(capture.GetFramesAvailable());
			var energy = Vector2.Zero;
			foreach (var sample in samples) energy += sample * sample;
			energy /= Math.Max(1, samples.Length);
			GD.Print($"STEP_AUDIO position={position} yaw={yaw} energy={energy}");
			return energy;
		}
		var left = await Measure(new(-6, 0, -6));
		var right = await Measure(new(6, 0, -6));
		var rotated = await Measure(new(6, 0, -6), Mathf.Pi);
		var near = await Measure(new(0, 0, -2));
		var middle = await Measure(new(0, 0, -10));
		var far = await Measure(new(0, 0, -20));
		var outside = await Measure(new(0, 0, -30));
		Check(left.X > left.Y * 1.25f && right.Y > right.X * 1.25f, "Footsteps must pan toward the player.");
		Check(rotated.X > rotated.Y * 1.25f, "Turning must reverse the audible direction.");
		Check(middle.X > near.X * .25f, "Footsteps ten metres away must remain audible.");
		Check(far.X > 0 && far.X < middle.X * .5f, "Distant footsteps must fade with distance.");
		Check(outside.LengthSquared() < 1e-12f, "Footsteps outside the audible radius must be silent.");
		AudioServer.RemoveBus(bus);

		var floor = new StaticBody3D { Position = new(0, -.5f, 0) };
		floor.SetMeta("source_surface", "wood");
		floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(200, 1, 200) } });
		AddChild(floor);
		var bot = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
		bot.LocalInput = false; bot.IsBot = true; bot.DebugControls = false;
		AddChild(bot);
		async Task Move(PlayerCommand command, int ticks = 66)
		{
			for (var i = 0; i < ticks; i++)
			{
				await Frame();
				bot.Simulate(command, 1f / Engine.PhysicsTicksPerSecond);
			}
		}
		await Move(default, 10);
		var sounds = bot.MovementAudio.Sounds;
		var count = sounds.SoundsPlayed;
		await Move(new(new(0, -1), false, false, false));
		Check(sounds.SoundsPlayed > count && sounds.LastSound.Contains("wood"), "Running bots must emit surface footsteps.");
		await Move(default, 30);
		count = sounds.SoundsPlayed;
		await Move(new(new(0, -1), false, false, true));
		await Move(default, 30);
		Check(sounds.SoundsPlayed == count, "Walking bots must be silent.");
		await Move(new(new(0, -1), false, true, false));
		await Move(default, 30);
		Check(sounds.SoundsPlayed == count, "Crouching bots must be silent.");
		bot.QueueFree();
		await Frame();
	}
}
