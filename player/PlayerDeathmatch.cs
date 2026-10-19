using Godot;
using System.Linq;
using Contrapoint.Gameplay;
using Contrapoint.Network;
using Contrapoint.Weapons;

namespace Contrapoint.Players;

public partial class Player
{
	public int MatchKills { get; set; }
	public int MatchDeaths { get; set; }
	public int MatchPingMilliseconds { get; set; }
	public bool ShowMatchScores { get; set; }
	private double _spawnProtection;
	public bool SpawnProtected => DeathmatchRules.Active && _spawnProtection > 0;
	public void EndSpawnProtection() => _spawnProtection = 0;
	public void TickSpawnProtection(double delta) => _spawnProtection = System.Math.Max(0, _spawnProtection - delta);
	private Marker3D[] _deathmatchSpawns = [];
	private Marker3D? _lastDeathmatchSpawn;

	private Marker3D? ChooseMapDeathmatchSpawn()
	{
		if (_deathmatchSpawns.Length == 0) return null;
		var nodes = GetTree().GetNodesInGroup("source_players");
		using var ownedNodes = (Godot.Collections.Array)nodes;
		var players = nodes.OfType<Player>()
			.Where(p => p != this && p.IsAlive && DeathmatchRules.Playing(p)).ToArray();
		// Snapshot once: scoring can visit hundreds of player/point pairs.
		var positions = players.Select(p => p.GlobalPosition).ToArray();
		var enemies = players.Where(p => !DeathmatchRules.Teammates(this, p)).Select(p => p.GlobalPosition).ToArray();
		var space = GetWorld3D().DirectSpaceState;
		var excluded = new Godot.Collections.Array<Rid> { GetRid() };
		using var ownedExcluded = (Godot.Collections.Array)excluded;
		var sightExcluded = new Godot.Collections.Array<Rid>(players.Select(p => p.GetRid()).Append(GetRid()));
		using var ownedSightExcluded = (Godot.Collections.Array)sightExcluded;
		using var spawnHull = new BoxShape3D
		{
			Size = new(32 * SourceMovement.Unit, SourceMovement.StandingHeight, 32 * SourceMovement.Unit), Margin = .001f
		};
		using var hull = new PhysicsShapeQueryParameters3D
		{
			Shape = spawnHull, CollisionMask = 3, Exclude = excluded
		};
		using var floorRay = new PhysicsRayQueryParameters3D { CollisionMask = 3, Exclude = excluded };
		using var sightRay = new PhysicsRayQueryParameters3D { CollisionMask = 1, Exclude = sightExcluded };
		// Shuffle before scoring so an empty server and equally safe positions
		// still use the whole map. Only the host/offline authority selects a point.
		var candidates = (Marker3D[])_deathmatchSpawns.Clone();
		for (var i = candidates.Length - 1; i > 0; i--)
		{
			var other = GD.RandRange(0, i);
			(candidates[i], candidates[other]) = (candidates[other], candidates[i]);
		}
		Marker3D? selected = null;
		var bestVisible = int.MaxValue;
		var bestDistance = 0f;
		var bestRepeat = false;
		var checkedPoints = 0;
		var lastPosition = _lastDeathmatchSpawn?.GlobalPosition;
		foreach (var point in candidates)
		{
			var position = point.GlobalPosition;
			var foot = position + Vector3.Up * .04f;
			var occupied = false;
			foreach (var other in positions)
				if (new Vector2(other.X - foot.X, other.Z - foot.Z).LengthSquared() < 4
					&& Mathf.Abs(other.Y - foot.Y) < SourceMovement.StandingHeight) { occupied = true; break; }
			if (occupied) continue;
			hull.Transform = new(Basis.Identity, foot + Vector3.Up * SourceMovement.StandingHeight / 2);
			var overlaps = space.IntersectShape(hull, 1);
			using var ownedOverlaps = (Godot.Collections.Array)overlaps;
			if (overlaps.Count > 0) continue;
			floorRay.From = foot + Vector3.Up * .1f;
			floorRay.To = foot - Vector3.Up * .2f;
			using var floor = space.IntersectRay(floorRay);
			if (floor.Count == 0 || floor["normal"].AsVector3().Y < .7f) continue;
			checkedPoints++;
			var visible = 0;
			var distance = 24f * 24;
			foreach (var enemy in enemies)
				distance = Mathf.Min(distance, foot.DistanceSquaredTo(enemy));
			var repeat = lastPosition.HasValue && position.DistanceSquaredTo(lastPosition.Value) < 36;
			var betterDistance = selected == null || BetterSpawnDistance(distance, repeat, bestDistance, bestRepeat);
			// Visibility only increases. Once a candidate cannot beat the best
			// complete score, the remaining rays cannot change the winner.
			foreach (var enemy in enemies)
			{
				if (visible > bestVisible || visible == bestVisible && !betterDistance) break;
				var eye = enemy + Vector3.Up * SourceMovement.StandingEye;
				// World geometry blocks visibility; players do not provide spawn cover.
				var seen = false;
				for (var sample = 0; sample < 2; sample++)
				{
					var height = sample == 0 ? SourceMovement.StandingEye : SourceMovement.StandingHeight / 2;
					sightRay.From = eye;
					sightRay.To = foot + Vector3.Up * height;
					using var hit = space.IntersectRay(sightRay);
					if (hit.Count == 0) { seen = true; break; }
				}
				if (seen) visible++;
			}
			if (visible < bestVisible || visible == bestVisible && betterDistance)
			{
				selected = point; bestVisible = visible; bestDistance = distance; bestRepeat = repeat;
			}
			if (checkedPoints >= 32) break;
		}
		if (selected != null) _lastDeathmatchSpawn = selected;
		return selected;
	}
	private static bool BetterSpawnDistance(float distance, bool repeat, float bestDistance, bool bestRepeat)
	{
		if ((distance < 100) != (bestDistance < 100)) return distance >= 100;
		if (repeat != bestRepeat) return !repeat;
		return distance > bestDistance;
	}

	private Marker3D ChooseDeathmatchSpawn(Marker3D[] candidates)
	{
		var enemies = GetTree().GetNodesInGroup("source_players").OfType<Player>()
			.Where(p => p != this && p.IsAlive && DeathmatchRules.Playing(p) && !DeathmatchRules.Teammates(this, p)).ToArray();
		return candidates.OrderByDescending(point => (enemies.Length == 0 ? 0 :
			enemies.Min(enemy => point.GlobalPosition.DistanceSquaredTo(enemy.GlobalPosition))) + GD.Randf() * 4).First();
	}

	public void RestoreMatchIdentity(PlayerTeam team, string agent, string weapon)
	{
		ApplyTeamSelection(team, agent, false, false);
		if (DeathmatchRules.Playing(this))
		{
			AwaitingInitialClass = false;
			if (Movement.Noclip) Movement.ToggleNoclip();
			Respawn();
			var selected = Inventory.Weapons.FirstOrDefault(w => w.Name.ToString() == weapon);
			if (selected != null) { if (LocalInput) Inventory.Equip(selected); else Inventory.EquipNetwork(selected); }
		}
	}

	public void RecordDeathmatchDeath(Player? killer, PlayerDamageKind kind)
	{
		if (!DeathmatchRules.Active || LanSession.Instance?.IsClient == true) return;
		MatchDeaths++;
		if (killer == null || killer == this || !DeathmatchRules.Playing(killer) || DeathmatchRules.Teammates(killer, this)
			|| kind is not (PlayerDamageKind.Bullet or PlayerDamageKind.Knife or PlayerDamageKind.HE)) return;
		killer.MatchKills++;
		RoundState.Instance?.RecordKill(killer);
		if (!killer.IsAlive) return;
		killer.SetVitals(System.Math.Min(killer.MaxHealth, killer.Health + DeathmatchRules.KillHealth), killer.Armor, killer.HasHelmet);
		LanSession.Instance?.BroadcastVitals(killer);
		if (killer.ActiveWeapon is FirearmView gun)
		{
			gun.AddMagazineAmmo(DeathmatchRules.KillAmmo);
			LanSession.Instance?.SendKillAmmunition(killer, gun, DeathmatchRules.KillAmmo);
		}
	}
}
