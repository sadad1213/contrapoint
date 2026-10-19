using Godot;
using System.Linq;
using Contrapoint.Gameplay;
using Contrapoint.Players;

namespace Contrapoint.Weapons;

public partial class ObjectivePickup : RigidBody3D
{
	public int ObjectiveId { get; set; }
	public bool Kit { get; set; }
	public bool Replica { get; set; }
	public Player? PreviousOwner { get; set; }
	private double _age;
	public override void _Ready()
	{
		CollisionLayer = 0; CollisionMask = 1; Mass = Kit ? .5f : 3;
		GravityScale = 20.32f / (float)ProjectSettings.GetSetting("physics/3d/default_gravity", 9.8);
		ContinuousCd = true; Freeze = Replica;
		AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(.25f, .12f, .18f) } });
		var path = Kit ? "res://assets/weapons/defuser/world/defuser_world.glb" : "res://assets/weapons/c4/world/c4_world.glb";
		var model = GD.Load<PackedScene>(path).Instantiate<Node3D>();
		AddChild(model); WorldWeaponMaterials.Apply(model);
		foreach (var p in GetTree().GetNodesInGroup("source_players").OfType<Player>()) AddCollisionExceptionWith(p);
	}
	public override void _PhysicsProcess(double delta)
	{
		if (Replica) return;
		_age += delta;
		foreach (var p in GetTree().GetNodesInGroup("source_players").OfType<Player>())
		{
			if (p == PreviousOwner && _age < .8) continue;
			var offset = GlobalPosition - (p.GlobalPosition + Vector3.Up * .7f);
			if (Mathf.Abs(offset.Y) < 1 && new Vector2(offset.X, offset.Z).Length() < .95f
				&& DefuseState.Instance?.Pickup(this, p) == true) return;
		}
	}
}
