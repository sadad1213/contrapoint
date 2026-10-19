using Godot;
using Contrapoint.Weapons;

namespace Contrapoint.Players;

// CSS FireBullet: ammunition penetration budget, material loss, TraceToExit.
public static class SourceBulletPenetration
{
	public static (float Power, float Distance) Ammo(FirearmView gun) => SourceDamageRules.WeaponAmmo(SourceDamageRules.WeaponName(gun)) switch
	{
		"BULLET_PLAYER_50AE" => (30, 1000), "BULLET_PLAYER_762MM" => (39, 5000),
		"BULLET_PLAYER_556MM" or "BULLET_PLAYER_556MM_BOX" => (35, 4000),
		"BULLET_PLAYER_338MAG" => (45, 8000), "BULLET_PLAYER_45ACP" => (15, 500),
		"BULLET_PLAYER_9MM" => (21, 800), "BULLET_PLAYER_357SIG" => (25, 800),
		"BULLET_PLAYER_57MM" => (30, 2000), _ => (0, 0)
	};
	public static (float Power, float Damage) Material(string surface)
	{
		if (surface.Contains("grate") || surface.Contains("chainlink")) return (1, .99f);
		if (surface.Contains("vent") || surface == "duct") return (.5f, .45f);
		if (surface.Contains("metal")) return (.5f, .3f);
		if (surface.Contains("dirt")) return (.5f, .3f);
		if (surface.Contains("concrete")) return (.4f, .25f);
		if (surface.Contains("tile")) return (.65f, .3f);
		if (surface.Contains("computer")) return (.4f, .45f);
		if (surface.Contains("wood")) return (1, .6f);
		return (1, .5f);
	}
	public static string Surface(Node3D collider, Godot.Collections.Dictionary hit)
	{
		if (collider is Player) return "flesh";
		if (collider.HasMeta("source_surfaces") && hit.ContainsKey("shape"))
		{
			var surfaces = collider.GetMeta("source_surfaces").AsStringArray();
			var shape = hit["shape"].AsInt32();
			if (shape >= 0 && shape < surfaces.Length) return surfaces[shape];
		}
		if (collider.HasMeta("source_surface")) return collider.GetMeta("source_surface").AsString();
		return collider.GetParent() is MeshInstance3D mesh && mesh.GetActiveMaterial(0) is { } material
			? SourceSoundPlayer.SurfaceForMaterial(material.ResourceName) : "concrete";
	}
	public static bool FindExit(Player owner, Node3D collider, Vector3 point, Vector3 direction,
		out Vector3 exit, out Vector3 normal, out string surface)
	{
		exit = point; normal = -direction; surface = "flesh";
		if (collider is Player player)
		{
			var outside = point + direction * (128 * SourceMovement.Unit);
			if (!player.Body.Hitboxes.Trace(outside, point, out var fraction, out _, out normal)) return false;
			exit = outside.Lerp(point, fraction); return true;
		}
		var excluded = new Godot.Collections.Array<Rid>();
		foreach (Player playerBody in owner.GetTree().GetNodesInGroup("source_players")) excluded.Add(playerBody.GetRid());
		var space = owner.GetWorld3D().DirectSpaceState;
		for (var distance = 24; distance <= 128; distance += 24)
		{
			var sample = point + direction * (distance * SourceMovement.Unit);
			var query = new PhysicsPointQueryParameters3D { Position = sample, CollisionMask = 1, Exclude = excluded };
			if (space.IntersectPoint(query, 1).Count > 0) continue;
			var ray = PhysicsRayQueryParameters3D.Create(sample, point - direction * .001f, 1, excluded);
			var hit = space.IntersectRay(ray);
			if (hit.Count == 0) return false;
			if (hit["collider"].AsGodotObject() != collider)
			{
				var skip = excluded.Duplicate(); skip.Add(hit["rid"].AsRid()); ray.Exclude = skip;
				hit = space.IntersectRay(ray);
				if (hit.Count == 0) return false;
			}
			exit = hit["position"].AsVector3(); normal = hit["normal"].AsVector3();
			surface = Surface(collider, hit); return true;
		}
		return false;
	}
}
