using Godot;
using Contrapoint.Players;

public partial class WeaponPreview : Node3D
{
	private PlayerInventory _inventory = null!;

	public override void _Ready()
	{
		_inventory = GetNode<PlayerInventory>("Camera3D/Weapons");
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Escape })
			GetTree().Quit();
		else if (!_inventory.HandleInput(@event)) return;
		GetViewport().SetInputAsHandled();
	}

	public override void _ExitTree() => Input.MouseMode = Input.MouseModeEnum.Visible;
}
