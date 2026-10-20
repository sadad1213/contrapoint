using Godot;

namespace Contrapoint.Players;

// Render the weapon layer over the world with an independent projection.
public partial class ViewmodelCamera : Camera3D
{
	[Export] public NodePath WorldCameraPath { get; set; } = new("../../../../View/Camera3D");
	private Camera3D _worldCamera = null!;
	private Viewport _worldViewport = null!;
	private SubViewportContainer _container = null!;
	private CanvasLayer _layer = null!;

	public override void _Ready()
	{
		_worldCamera = GetNode<Camera3D>(WorldCameraPath);
		_worldViewport = _worldCamera.GetViewport();
		_container = GetParent().GetParent<SubViewportContainer>();
		_layer = _container.GetParent<CanvasLayer>();
		GetViewport().World3D = _worldCamera.GetWorld3D();
		// The weapon shares the world but uses a different projection. Render it
		// without world occlusion, and avoid a second CPU occlusion pass.
		GetViewport().UseOcclusionCulling = false;
		SyncResolution();
		GlobalTransform = _worldCamera.GlobalTransform;
	}

	public override void _Process(double delta)
	{
		GlobalTransform = _worldCamera.GlobalTransform;
		SyncResolution();
	}

	private void SyncResolution()
	{
		// Deliberately selected lower resolutions must affect both 3D passes.
		// Otherwise a low-resolution world appears blurred behind native-resolution hands.
		GetViewport().Scaling3DScale = _worldViewport.Scaling3DScale;
		// canvas_items scales UI from the project's base resolution. Cancel that
		// scaling for this layer so its texture is rendered and composited 1:1.
		_layer.Transform = _worldViewport.GetStretchTransform().AffineInverse();
		var size = (Vector2)_worldCamera.GetWindow().Size;
		if (size.X > 0 && size.Y > 0 && _container.Size != size) _container.Size = size;
	}
}
