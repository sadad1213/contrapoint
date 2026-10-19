using Godot;
using System.Text.Json;

namespace Contrapoint.Weapons;

public static class SourceSurfaceDensity
{
	private static readonly JsonDocument Densities = JsonDocument.Parse(
		FileAccess.GetFileAsString("res://assets/physics/css_surface_density.json"));

	public static float For(RigidBody3D prop)
	{
		if (prop.HasMeta("source_density")) return prop.GetMeta("source_density").AsSingle();
		var surface = "default";
		if (prop.HasMeta("source_surface")) surface = prop.GetMeta("source_surface").AsString();
		else if (prop.GetScript().VariantType != Variant.Type.Nil && prop.Get("source_data").VariantType == Variant.Type.Dictionary
			&& prop.Get("source_data").AsGodotDictionary().TryGetValue("surface", out var value)) surface = value.AsString();
		return Densities.RootElement.TryGetProperty(surface.ToLowerInvariant(), out var density) ? density.GetSingle() : 2000;
	}
}
