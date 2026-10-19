using Godot;

namespace Contrapoint.Weapons;

public partial class GrenadeBurst : Node3D
{
	private double _age;
	private OmniLight3D _light = null!;
	private bool _flash;

	public static void Spawn(Node3D parent, Vector3 position, bool flash)
	{
		var burst = new GrenadeBurst { _flash = flash };
		parent.AddChild(burst);
		burst.GlobalPosition = position;
	}

	public override void _Ready()
	{
		_light = new OmniLight3D
		{
			LightColor = _flash ? Colors.White : new Color(1, 0.65f, 0.25f),
			LightEnergy = _flash ? 5 : 8, OmniRange = _flash ? 10 : 8
		};
		AddChild(_light);
		if (_flash) return;
		AddChild(new CssoExplosion());
	}

	public override void _Process(double delta)
	{
		_age += delta;
		_light.LightEnergy = Mathf.Max(0, (_flash ? 5 : 8) * (1 - (float)_age / 0.15f));
		if (_flash ? _age >= 0.2 : _age > 0.2 && GetChildCount() == 1) QueueFree();
	}
}
