using Godot;
using System.Collections.Generic;
using Contrapoint.Players;

namespace Contrapoint.Weapons;

public enum WeaponSlot { Primary, Secondary, Melee, Grenade, Objective }

// Every view has WeaponSway/Model; the imported model owns its skeleton/player.
public abstract partial class WeaponView : Node3D
{
	private readonly Dictionary<string, (StringName Name, double Length)> _networkClips = new(System.StringComparer.Ordinal);
	private Player? _wielder;
	private bool _sceneLocalAudio = true;
	private WeaponSway? _sway;
	public Player? Wielder
	{
		get => _wielder;
		internal set
		{
			_wielder = value;
			if (Sounds != null) Sounds.Enabled = value != null ? value.LocalInput || value.IsServerBot : _sceneLocalAudio;
			SetProcessUnhandledInput(Equipped && (value?.LocalInput ?? true));
			_sway?.SetProcess(value?.LocalInput ?? true);
			_sway?.SetProcessInput(value?.LocalInput ?? true);
		}
	}
	public virtual WeaponSlot Slot => WeaponSlot.Melee;
	public virtual float MovementSpeed => 250;
	public virtual bool CanEquip => true;
	public virtual bool CanHolster => true;
	public bool Equipped { get; private set; }
	public SourceSoundPlayer Sounds { get; private set; } = null!;
	protected Node3D Model { get; private set; } = null!;
	protected AnimationPlayer Animations { get; private set; } = null!;
	internal AnimationPlayer NetworkAnimation => Animations;
	protected double DeployRemaining { get; private set; }
	protected abstract double DeploySeconds { get; }

	protected void ReplaceViewModel(PackedScene scene)
	{
		Sounds.CancelAnimation();
		var transform = Model.Transform;
		var parent = Model.GetParent();
		parent.RemoveChild(Model);
		Model.QueueFree();
		Model = scene.Instantiate<Node3D>();
		Model.Name = "Model";
		Model.Transform = transform;
		parent.AddChild(Model);
		Animations = Model.FindChild("AnimationPlayer", true, false) as AnimationPlayer
			?? throw new System.InvalidOperationException("Weapon AnimationPlayer is missing.");
		foreach (MeshInstance3D mesh in Model.FindChildren("*", "MeshInstance3D", true, false)) mesh.Layers = 2;
		foreach (var clip in _networkClips.Values) clip.Name.Dispose();
		_networkClips.Clear();
	}

	public override void _Ready()
	{
		// Inventory equips before Player._Ready binds ownership. Read the ancestor
		// now so a remote player's initial knife deploy cannot leak local audio.
		var localAudio = Wielder?.LocalInput ?? true;
		for (Node? ancestor = GetParent(); ancestor != null; ancestor = ancestor.GetParent())
			if (ancestor is Player player) { localAudio = player.LocalInput || player.IsServerBot; break; }
		_sceneLocalAudio = localAudio;
		Sounds = new SourceSoundPlayer { Name = "Sounds", Enabled = localAudio };
		AddChild(Sounds);
		Model = GetNode<Node3D>("WeaponSway/Model");
		_sway = GetNode<WeaponSway>("WeaponSway");
		_sway.SetProcess(Wielder?.LocalInput ?? true);
		_sway.SetProcessInput(Wielder?.LocalInput ?? true);
		Animations = Model.FindChild("AnimationPlayer", true, false) as AnimationPlayer
			?? throw new System.InvalidOperationException("Weapon AnimationPlayer is missing.");
		foreach (MeshInstance3D mesh in Model.FindChildren("*", "MeshInstance3D", true, false))
		{
			for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
			{
				if (mesh.GetActiveMaterial(surface) is not StandardMaterial3D source || source.ResourceName != "v_hands") continue;
				var hands = (StandardMaterial3D)source.Duplicate();
				hands.Metallic = 0;
				hands.Roughness = 0.9f;
				hands.MetallicSpecular = 0.1f;
				mesh.SetSurfaceOverrideMaterial(surface, hands);
			}
		}
		Visible = false;
		SetProcessUnhandledInput(false);
	}

	public void Equip()
	{
		Wielder?.Recoil.ResetBurst();
		Equipped = Visible = true;
		DeployRemaining = DeploySeconds;
		SetProcessUnhandledInput(Wielder?.LocalInput ?? true);
		OnEquip();
	}

	public void Holster()
	{
		Sounds.CancelAnimation();
		Equipped = Visible = false;
		SetProcessUnhandledInput(false);
		OnHolster();
	}

	public void SetInputEnabled(bool enabled)
	{
		SetProcessUnhandledInput(enabled && Equipped && (Wielder?.LocalInput ?? true));
		if (!enabled) OnInputDisabled();
	}

	public virtual bool HandleCommand(string command, bool down) => false;

	public void ApplyNetworkAnimation(string clip, double time)
	{
		if (Wielder?.LocalInput != false || !double.IsFinite(time) || clip.Length == 0) return;
		if (!_networkClips.TryGetValue(clip, out var cached))
		{
			var name = new StringName(clip);
			// Ignore unknown packet names without retaining unbounded cache entries.
			if (!Animations.HasAnimation(name)) { name.Dispose(); return; }
			cached = (name, Animations.GetAnimation(name).Length);
			_networkClips[clip] = cached;
		}
		Animations.Play(cached.Name);
		// Remote players render their separate world rigs. The viewmodel's clip
		// and time drive those poses, but its hidden bones need no evaluation.
		Animations.Seek(System.Math.Clamp(time, 0, cached.Length), false);
		// The replica samples presentation only; it must never run weapon actions.
		Animations.Pause();
	}

	public override void _ExitTree()
	{
		foreach (var clip in _networkClips.Values) clip.Name.Dispose();
		_networkClips.Clear();
	}

	// Standalone weapon previews use the same bind catalog as gameplay.
	public override void _UnhandledInput(InputEvent input)
	{
		if (input is InputEventKey { Echo: true }) return;
		var key = ConsoleBindings.EventKey(input);
		if (key != null && ConsoleBindings.Keys.TryGetValue(key, out var command) && command.StartsWith('+') &&
			HandleCommand(command[1..], input.IsPressed())) GetViewport().SetInputAsHandled();
	}

	protected void AdvanceDeploy(double delta) => DeployRemaining = System.Math.Max(0, DeployRemaining - delta);
	protected abstract void OnEquip();
	protected virtual void OnHolster() { }
	protected virtual void OnInputDisabled() { }
}
