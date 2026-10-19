using Godot;

namespace Contrapoint.Weapons;

// The shared Source zerogxplode sprite from the installed vanilla CSS/HL2 VPK.
public partial class CssBombExplosion : Node3D
{
	private double _age;
	private Sprite3D _sprite = null!;
	private OmniLight3D _light = null!;
	public override void _Ready()
	{
		_sprite = new Sprite3D { Texture = GD.Load<Texture2D>("res://assets/effects/grenades/zerogxplode.png"),
			Hframes = 4, Vframes = 4, PixelSize = .125f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			Shaded = false, NoDepthTest = false, Transparent = true };
		AddChild(_sprite); _sprite.Position = Vector3.Up * 2;
		_light = new OmniLight3D { LightColor = new Color(1, .6f, .2f), LightEnergy = 12, OmniRange = 20 };
		AddChild(_light);
	}
	public override void _Process(double delta)
	{
		_age += delta;
		_sprite.Frame = System.Math.Min(14, (int)(_age * 25));
		_light.LightEnergy = Mathf.Max(0, 12 * (1 - (float)_age / .2f));
		if (_age >= .6) QueueFree();
	}
}
