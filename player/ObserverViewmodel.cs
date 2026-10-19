using Godot;
using System;
using Contrapoint.Weapons;

namespace Contrapoint.Players;

// A private render world contains only a passive copy of the selected model.
// No weapon controller, input, damage, sound relay or animation method track runs here.
public partial class ObserverViewmodel : CanvasLayer
{
	public Node3D? Model { get; private set; }
	public WeaponView? Source { get; private set; }
	public SubViewport Viewport { get; private set; } = null!;
	private SubViewportContainer _container = null!;
	private AnimationPlayer? _animation;
	private Node3D? _sourceModel;
	private string _clip = "";
	private double _sourceTime, _sampleTime;
	public override void _Ready()
	{
		Layer = 1; Visible = false;
		_container = new SubViewportContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Stretch = true };
		AddChild(_container);
		Viewport = new SubViewport { TransparentBg = true, OwnWorld3D = true, GuiDisableInput = true,
			HandleInputLocally = false, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled, Msaa3D = GetViewport().Msaa3D };
		_container.AddChild(Viewport);
		Viewport.AddChild(new Camera3D { Current = true, Fov = 58, Near = .005f, Far = 10 });
		Viewport.AddChild(new DirectionalLight3D { RotationDegrees = new(-35, -30, 0), LightEnergy = 1.3f, LightColor = new(1, .96f, .9f) });
		Viewport.AddChild(new DirectionalLight3D { RotationDegrees = new(-20, 135, 0), LightEnergy = .6f, LightColor = new(.78f, .87f, 1) });
	}
	public void Clear()
	{
		if (Model != null) { Model.GetParent()?.RemoveChild(Model); Model.QueueFree(); }
		Model = null; Source = null; _sourceModel = null; _animation = null; _clip = "";
		Visible = false;
		if (Viewport != null) Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
	}
	public void Present(WeaponView? weapon, double delta, Vector2I size)
	{
		if (!GodotObject.IsInstanceValid(weapon) || weapon!.IsQueuedForDeletion()) { if (Model != null) Clear(); return; }
		var sourceModel = weapon.GetNode<Node3D>("WeaponSway/Model");
		if (Source != weapon || _sourceModel != sourceModel)
		{
			Clear(); Source = weapon; _sourceModel = sourceModel;
			Model = (Node3D)sourceModel.Duplicate(0); // Do not copy scripts, groups or signal connections.
			Viewport.AddChild(Model);
			_animation = Model.FindChild("AnimationPlayer", true, false) as AnimationPlayer;
			if (_animation != null)
			{
				_animation.CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual;
				// Imported models normally contain only bone tracks. Strip side effects
				// from a private animation library if a future model adds event tracks.
				foreach (var libraryName in _animation.GetAnimationLibraryList())
				{
					var library = (AnimationLibrary)_animation.GetAnimationLibrary(libraryName).Duplicate(true);
					_animation.RemoveAnimationLibrary(libraryName); _animation.AddAnimationLibrary(libraryName, library);
					foreach (var name in library.GetAnimationList())
					{
						var animation = library.GetAnimation(name);
						for (var i = animation.GetTrackCount() - 1; i >= 0; i--)
							if (animation.TrackGetType(i) is Animation.TrackType.Method or Animation.TrackType.Audio or Animation.TrackType.Animation) animation.RemoveTrack(i);
					}
				}
			}
		}
		Visible = sourceModel.Visible;
		Model!.Visible = sourceModel.Visible;
		Viewport.RenderTargetUpdateMode = Visible ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
		Transform = GetViewport().GetStretchTransform().AffineInverse(); _container.Size = size;
		Model!.Transform = weapon.Wielder!.Camera.GlobalTransform.AffineInverse() * sourceModel.GlobalTransform;
		if (!weapon.Wielder.LocalInput) Model.Transform = weapon.Wielder.ViewmodelBob.MotionOffset * Model.Transform;
		if (_animation == null) return;
		using var assigned = weapon.NetworkAnimation.AssignedAnimation;
		var clip = assigned.ToString(); var time = weapon.NetworkAnimation.CurrentAnimationPosition;
		if (clip.Length == 0 || !_animation.HasAnimation(assigned)) return;
		var animationClip = _animation.GetAnimation(assigned);
		if (clip != _clip || Math.Abs(time - _sourceTime) > .00001) { _sampleTime = time; _clip = clip; _sourceTime = time; }
		else _sampleTime = Math.Min(time + .1, _sampleTime + delta * weapon.NetworkAnimation.SpeedScale);
		var length = Math.Max(.001, animationClip.Length);
		var sample = animationClip.LoopMode == Animation.LoopModeEnum.None ? Math.Min(_sampleTime, length) : _sampleTime % length;
		_animation.Play(assigned); _animation.Seek(sample, true); _animation.Pause();
	}
}
