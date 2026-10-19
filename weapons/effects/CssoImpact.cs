using Godot;

namespace Contrapoint.Weapons;

// Original CSSO impact_fx.pcf systems and material sheets, oriented away from the hit.
public partial class CssoImpact : CssoExplosion
{
	protected override string Root => "res://assets/effects/csso_impact/";
	protected override string DataFile => "impact.json";
	protected override bool IsImpact => true;
	protected override Basis EmissionBasis => _emissionBasis;
	private Basis _emissionBasis = Basis.Identity;
	private string _system = "impact_concrete";

	public void Initialize(Vector3 point, Vector3 normal, string surface)
	{
		normal = normal.Normalized();
		var reference = Mathf.Abs(normal.Dot(Vector3.Up)) > .99f ? Vector3.Right : Vector3.Up;
		var tangent = reference.Cross(normal).Normalized();
		_emissionBasis = new Basis(tangent, normal, tangent.Cross(normal));
		GlobalPosition = point + normal * .005f;
		_system = "impact_" + Family(surface.ToLowerInvariant());
	}

	private static string Family(string surface)
	{
		if (surface.Contains("metal") || surface.Contains("grate") || surface.Contains("chainlink") || surface.Contains("vent") || surface == "duct") return "metal";
		if (surface.Contains("wood")) return "wood";
		if (surface.Contains("computer") || surface.Contains("electronic")) return "computer";
		if (surface.Contains("cardboard") || surface.Contains("paper")) return "cardboard";
		if (surface.Contains("cloth") || surface.Contains("carpet") || surface.Contains("upholstery")) return "cloth";
		foreach (var family in new[] { "brick", "tile", "glass", "sand", "dirt", "grass", "plaster", "plastic", "rubber", "asphalt", "snow", "clay", "rock" })
			if (surface.Contains(family)) return family;
		return "concrete";
	}

	protected override void InitializeSystems() => EmitHierarchy(Data.RootElement.GetProperty("systems").GetProperty(_system));
}
