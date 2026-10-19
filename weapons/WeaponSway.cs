using Godot;
using Contrapoint.Players;

namespace Contrapoint.Weapons;

// A camera-space parent keeps CSSO motion separate from animated bones.
public partial class WeaponSway : Node3D
{
	private Transform3D _rest;
	private WeaponView _weapon = null!;
	private readonly SourceViewmodelLag _previewLag = new();

	public override void _Ready()
	{
		_rest = Transform;
		_weapon = GetParent<WeaponView>();
	}

	public override void _Process(double delta)
	{
		if (delta <= 0) return;
		if (!_weapon.Equipped)
		{
			_previewLag.Reset();
			Transform = _rest;
			return;
		}
		if (_weapon.Wielder is { } player)
			Transform = player.ViewmodelBob.MotionOffset * _rest;
		else
		{
			// Standalone weapon previews use the same angle-based sway.
			var camera = GetViewport().GetCamera3D();
			var offset = camera == null ? Vector3.Zero : _previewLag.Sample(camera.GlobalBasis, delta, .1f, 1.6f);
			Transform = new Transform3D(Basis.Identity, offset) * _rest;
		}
	}
}
