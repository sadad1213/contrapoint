using Godot;

namespace Contrapoint.Weapons;

public static class WorldWeaponMaterials
{
	public static void Apply(Node3D model)
	{
		foreach (MeshInstance3D mesh in model.FindChildren("*", "MeshInstance3D", true, false))
			for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
				if (mesh.GetActiveMaterial(surface) is StandardMaterial3D source)
				{
					// Match the held world models in PlayerBody. Godot imports these
					// GLBs with specular 0.5 despite KHR_materials_specular = 0,
					// producing a blue sky reflection over the original CSS textures.
					var material = (StandardMaterial3D)source.Duplicate();
					material.Metallic = 0;
					material.Roughness = .85f;
					material.MetallicSpecular = .15f;
					mesh.SetSurfaceOverrideMaterial(surface, material);
				}
	}
}
