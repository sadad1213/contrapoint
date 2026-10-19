using Godot;
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Contrapoint.Network;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

public partial class LanWorldChecks : Node3D
{
	private int _checks;
	private LanSession Net => LanSession.Instance!;
	private object? Invoke(string method, params object[] args) => typeof(LanSession)
		.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Net, args);
	private void Check(bool value, string message)
	{
		_checks++;
		if (!value) throw new InvalidOperationException(message);
	}
	private (GDictionary State, byte[] Bytes) Capture()
	{
		var entries = ((IEnumerable)Invoke("CaptureWorld")!).Cast<object>().ToArray();
		Check(entries.Length == 1, "fixture must capture one world entity");
		var entry = entries[0];
		return ((GDictionary)entry.GetType().GetProperty("State")!.GetValue(entry)!,
			(byte[])entry.GetType().GetProperty("Bytes")!.GetValue(entry)!);
	}
	private AnimationPlayer AddAnimation(Node parent, string name)
	{
		var player = new AnimationPlayer { Name = name };
		parent.AddChild(player);
		var library = new AnimationLibrary();
		library.AddAnimation("idle", new Animation { Length = 1 });
		player.AddAnimationLibrary("", library);
		player.Play("idle"); player.Seek(.25, true); player.Pause();
		return player;
	}
	private void Apply(GDictionary state, int sequence) => Invoke("ApplyWorld", sequence, new GArray { state });
	public override void _Ready()
	{
		try
		{
			var body = new RigidBody3D { Name = "Fixture", Freeze = true };
			AddChild(body);
			var animation = AddAnimation(body, "Original");
			typeof(LanSession).GetField("_scene", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Net, this);
			Invoke("CollectWorld");
			var first = Capture();
			Check(first.State["animations"].AsGodotArray().Count == 1, "authored animation must be included");
			Check(first.Bytes.AsSpan().SequenceEqual(GD.VarToBytes(first.State)), "shared snapshot bytes must match the RPC state");
			Check(first.Bytes.AsSpan().SequenceEqual(Capture().Bytes), "unchanged snapshots must remain byte-identical for per-peer filtering");
			var nested = new Node3D { Name = "Nested" }; body.AddChild(nested);
			AddAnimation(nested, "Added");
			Check(Capture().State["animations"].AsGodotArray().Count == 2, "adding a nested animation must refresh the lookup cache");
			body.RemoveChild(animation); animation.Free();
			Check(Capture().State["animations"].AsGodotArray().Count == 1, "removing an animation must refresh the lookup cache");
			var voice = new AudioStreamPlayer3D(); body.AddChild(voice);
			Check(Capture().State.ContainsKey("sound"), "adding a voice must refresh the lookup cache");
			body.RemoveChild(voice); voice.Free();
			Check(!Capture().State.ContainsKey("sound"), "removing a voice must discard the cached reference");
			var state = Capture().State;
			state["transform"] = new Transform3D(Basis.Identity, Vector3.Right);
			Apply(state, 1);
			Check(body.Position.IsEqualApprox(Vector3.Right), "initial world state must snap immediately");
			typeof(LanSession).GetProperty(nameof(LanSession.WorldReady))!.SetValue(Net, true);
			state["transform"] = new Transform3D(Basis.Identity, Vector3.Right * 2);
			Apply(state, 2);
			Invoke("InterpolateWorld", 1.0 / 120);
			Check(body.Position.X > 1 && body.Position.X < 2, "live movement must interpolate between received poses");
			for (var frame = 0; frame < 240; frame++) Invoke("InterpolateWorld", 1.0 / 120);
			Check(body.Position == Vector3.Right * 2, "settled movement must end at the exact received transform");
			var active = (IList)typeof(LanSession).GetField("_interpolatingEntities", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Net)!;
			Check(active.Count == 0, "settled entities must stop doing render-frame work");
			state["transform"] = new Transform3D(Basis.Identity, Vector3.Right * 3);
			Apply(state, 3);
			Apply(first.State, 2);
			Check(body.Position == Vector3.Right * 2, "stale packets must not replace a newer movement target");
			Invoke("InterpolateWorld", .02);
			Check(body.Position.X > 2 && body.Position.X < 3, "a stopped entity must resume smoothing on its next movement");
			state["transform"] = new Transform3D(Basis.Identity, Vector3.Right * 20);
			Apply(state, 4); Invoke("InterpolateWorld", .02);
			Check(body.Position == Vector3.Right * 20 && active.Count == 0, "large corrections must snap and leave the active list");
			state["transform"] = new Transform3D(Basis.Identity, Vector3.Right * 21);
			Apply(state, 5); body.QueueFree();
			Check(Capture().State.ContainsKey("gone"), "deleted entities must retain a late-join tombstone");
			Invoke("InterpolateWorld", .02);
			Check(active.Count == 0, "deleted entities must stop interpolating");
			Invoke("ClearMapReplication");
			Check(active.Count == 0, "map reset must discard interpolation state");
			GD.Print($"LAN_WORLD_CHECKS checks={_checks} failures=0"); GetTree().Quit();
		}
		catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
	}
}
