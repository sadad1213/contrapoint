using Godot;
using Contrapoint.Weapons;

namespace Contrapoint.Players;

// CSS presentation/ammunition use CSSO's accuracy and two-polar-sample spread.
public static class SourceWeaponSpread
{
	public static float Radius(FirearmView gun, Player player)
		=> (CssoRifleBallistics.Inaccuracy(gun, player) + CssoRifleBallistics.Data(gun).Spread) * gun.SpreadScale;

	public static Transform3D Sample(Transform3D aim, FirearmView gun, Player player, RandomNumberGenerator random)
	{
		if (gun.SpreadScale <= 0) return aim;
		// CSSO adds two independent polar samples: accuracy penalty + base spread.
		var a = random.RandfRange(0, Mathf.Tau);
		var r = random.Randf() * CssoRifleBallistics.Inaccuracy(gun, player);
		var b = random.RandfRange(0, Mathf.Tau);
		var s = random.Randf() * CssoRifleBallistics.Data(gun).Spread;
		var offset = new Vector2(Mathf.Cos(a) * r + Mathf.Cos(b) * s, Mathf.Sin(a) * r + Mathf.Sin(b) * s) * gun.SpreadScale;
		var direction = (-aim.Basis.Z + aim.Basis.X * offset.X + aim.Basis.Y * offset.Y).Normalized();
		return new(Basis.LookingAt(direction, aim.Basis.Y), aim.Origin);
	}

	public static Transform3D[] SamplePellets(Transform3D aim, FirearmView gun, Player player, RandomNumberGenerator random)
	{
		var result = new Transform3D[gun.PelletCount];
		// CSSO shares accuracy displacement, then samples spread per pellet.
		var angle = random.RandfRange(0, Mathf.Tau);
		var radius = random.Randf() * CssoRifleBallistics.Inaccuracy(gun, player);
		var accuracy = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
		for (var i = 0; i < result.Length; i++)
		{
			angle = random.RandfRange(0, Mathf.Tau);
			radius = random.Randf() * CssoRifleBallistics.Data(gun).Spread;
			var offset = (accuracy + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius) * gun.SpreadScale;
			var direction = (-aim.Basis.Z + aim.Basis.X * offset.X + aim.Basis.Y * offset.Y).Normalized();
			result[i] = new(Basis.LookingAt(direction, aim.Basis.Y), aim.Origin);
		}
		return result;
	}

	public static Transform3D Sample(Transform3D aim, float radius, RandomNumberGenerator random)
	{
		if (radius <= 0) return aim;
		// Center-weighted random cone, independent of cosmetic camera movement.
		var angle = random.RandfRange(0, Mathf.Tau);
		var distance = random.Randf() * radius;
		var direction = (-aim.Basis.Z + aim.Basis.X * (Mathf.Cos(angle) * distance)
			+ aim.Basis.Y * (Mathf.Sin(angle) * distance)).Normalized();
		return new(Basis.LookingAt(direction, aim.Basis.Y), aim.Origin);
	}
}
