using Godot;

namespace Contrapoint.Players;

public partial class SourceRagdollBone : PhysicalBone3D
{
	public SourceRagdoll Ragdoll { get; set; } = null!;
	private Vector3 _previousVelocity;
	public override void _IntegrateForces(PhysicsDirectBodyState3D state)
	{
		for (var i = 0; i < state.GetContactCount(); i++)
		{
			var normal = (state.Transform.Basis * state.GetContactLocalNormal(i)).Normalized();
			var speed = Mathf.Max(0, -_previousVelocity.Dot(normal));
			if (speed > .8f)
				Ragdoll.Contact(state.Transform * state.GetContactLocalPosition(i), speed);
		}
		_previousVelocity = state.LinearVelocity;
	}
}
