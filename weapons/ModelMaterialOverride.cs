using Godot;

namespace Contrapoint.Weapons;

// Applied on an imported model root, before the weapon controller becomes ready.
public partial class ModelMaterialOverride : Node3D
{
	[Export] public string SourceMaterialName { get; set; } = "";
	[Export] public Material Replacement { get; set; } = null!;

	public override void _Ready()
	{
		foreach (MeshInstance3D mesh in FindChildren("*", "MeshInstance3D", true, false))
			for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
				if (mesh.GetActiveMaterial(surface)?.ResourceName == SourceMaterialName)
					mesh.SetSurfaceOverrideMaterial(surface, Replacement);
	}
}
