using Godot;
using System;
using System.Text.Json;
using Contrapoint.Players;
using Contrapoint.Gameplay;

namespace Contrapoint.Weapons;

public enum GrenadeKind { HE, Flashbang, Smoke }

public partial class GrenadeView : WeaponView
{
	[Export] public GrenadeKind Kind { get; set; }
	public string AssetName => Kind switch { GrenadeKind.HE => "hegrenade", GrenadeKind.Flashbang => "flashbang", _ => "smokegrenade" };
	public override WeaponSlot Slot => WeaponSlot.Grenade;
	public override bool CanEquip => Wielder == null || !DeathmatchRules.Active && Ammo > 0;
	public override bool CanHolster => _state != State.Throw || _emitted;
	public int Ammo { get; private set; }
	public int Throws { get; private set; }
	public void RestoreAmmo(int amount) => Ammo = Math.Clamp(amount, 0, Kind == GrenadeKind.Flashbang ? 2 : 1);
	public bool PinPulled => _state is State.PullPin or State.Hold;
	public GrenadeProjectile? LastProjectile { get; private set; }
	protected override double DeploySeconds => Duration("deploy");
	private enum State { Deploy, Idle, PullPin, Hold, Throw, Spent }
	private State _state;
	private bool _held, _launchPending, _emitted, _drop;
	private double _elapsed;
	private string _clip = "idle";
	private readonly System.Collections.Generic.Dictionary<string, double> _durations = new();

	public override void _Ready()
	{
		base._Ready();
		var report = JsonDocument.Parse(FileAccess.GetFileAsString($"res://assets/weapons/{AssetName}/import_report.json"));
		foreach (var clip in report.RootElement.GetProperty("animations").EnumerateArray())
			_durations[clip.GetProperty("name").GetString()!] = clip.GetProperty("duration").GetDouble();
		report.Dispose();
		foreach (MeshInstance3D mesh in Model.FindChildren("*", "MeshInstance3D", true, false))
			for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
				if (mesh.GetActiveMaterial(surface) is StandardMaterial3D source)
				{
					var material = (StandardMaterial3D)source.Duplicate();
					material.CullMode = BaseMaterial3D.CullModeEnum.Back;
					material.Metallic = 0;
					material.Roughness = source.ResourceName == "v_hands" ? 0.9f : 0.8f;
					material.MetallicSpecular = 0.15f;
					mesh.SetSurfaceOverrideMaterial(surface, material);
				}
		Animations.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
		Animations.GetAnimation("idle").LoopMode = Animation.LoopModeEnum.Linear;
		ResetAmmo();
	}

	public void ResetAmmo()
	{
		Ammo = DeathmatchRules.Active ? 0 : Kind == GrenadeKind.Flashbang ? 2 : 1;
		_held = _launchPending = _emitted = _drop = false;
		if (Equipped) OnEquip();
	}

	private double Duration(string clip) => _durations[clip];

	private void Play(string clip, State state)
	{
		_state = state;
		_clip = clip;
		_elapsed = 0;
		Animations.Stop(true);
		// Keep the exact MDL time even when the glTF export rounded a final frame.
		Animations.SpeedScale = (float)(Animations.GetAnimation(clip).Length / Duration(clip));
		Animations.Play(clip);
		Animations.Advance(0);
		Sounds.StartAnimation(AssetName, clip, Duration(clip));
	}

	protected override void OnEquip()
	{
		_held = _emitted = _drop = false;
		Play("deploy", State.Deploy);
	}

	protected override void OnHolster()
	{
		_held = false;
		_state = State.Idle;
		Animations.Stop();
	}

	protected override void OnInputDisabled()
	{
		_held = false;
		if (PinPulled) Play("idle", State.Idle);
	}

	public override bool HandleCommand(string command, bool down)
	{
		if (!Equipped || command != "attack") return false;
		if (down && Wielder != null && DefuseRules.Active && DefuseState.Instance?.CanAttack(Wielder) == false) return true;
		_held = down;
		if (_held && _state == State.Idle && DeployRemaining <= 0 && CanEquip)
			Play("pullpin", State.PullPin);
		return true;
	}

	public override void _Process(double delta)
	{
		AdvanceDeploy(delta);
		if (!Equipped) return;
		// Split at action deadlines so an early release and low FPS cannot shorten
		// pulling the pin, cook the fuse, or skip the launch.
		for (var remaining = delta; remaining > 0.0000001;)
		{
			if (_state == State.Hold)
			{
				if (_held) return;
				Play("throw", State.Throw);
				_emitted = false;
				continue;
			}
			if (_state == State.Spent) return;
			if (_state == State.Idle)
			{
				if (_held && DeployRemaining <= 0 && CanEquip)
				{
					Play("pullpin", State.PullPin);
					continue;
				}
				Animations.Advance(remaining);
				return;
			}
			var step = Math.Min(remaining, Math.Max(0, Duration(_clip) - _elapsed));
			_elapsed += step;
			remaining -= step;
			Sounds.AdvanceAnimation(_elapsed);
			Animations.Advance(step);
			// CSS starts the projectile 100 ms after release; the MDL 3005 event
			// describes the view animation and is not the server throw deadline.
			if (_state == State.Throw && _elapsed + 0.0000001 >= 0.1 && !_emitted) _launchPending = true;
			if (_elapsed + 0.0000001 < Duration(_clip)) continue;
			switch (_state)
			{
				case State.Deploy: Play("idle", State.Idle); break;
				case State.PullPin: _state = State.Hold; break; // Freeze the authored ready pose.
				case State.Throw: _state = State.Spent; FinishThrow(); break;
			}
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!_launchPending) return;
		_launchPending = false;
		if (Wielder is { IsAlive: false } && !_drop) return;
		var camera = Wielder?.Camera ?? GetParent().GetParent<Camera3D>();
		if (Contrapoint.Network.LanSession.Instance?.RequestGrenade(this, _drop) != true)
			LastProjectile = GrenadeProjectile.Throw(this, camera, _drop);
		_emitted = true;
		Throws++;
		if (_state == State.Spent) FinishThrow();
	}

	private void FinishThrow()
	{
		if (!_emitted) return;
		if (Wielder != null) Wielder.Inventory.EquipBest();
		else OnEquip(); // Standalone model preview has an unlimited supply.
	}

	public void OwnerDied()
	{
		if (!PinPulled && _state != State.Throw) return;
		if (_emitted) return;
		_drop = _launchPending = true;
		_held = false;
	}
}
