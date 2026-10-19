using Godot;
using System.Collections.Generic;
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
		var players = GetTree().GetNodesInGroup("source_players").OfType<Player>()
			.Where(p => p != this && p.IsAlive && DeathmatchRules.Playing(p)).ToArray();
		var enemies = players.Where(p => !DeathmatchRules.Teammates(this, p)).ToArray();
		var space = GetWorld3D().DirectSpaceState;
		var excluded = new Godot.Collections.Array<Rid> { GetRid() };
		var sightExcluded = new Godot.Collections.Array<Rid>(players.Select(p => p.GetRid()).Append(GetRid()));
		using var spawnHull = new BoxShape3D
		{
			Size = new(32 * SourceMovement.Unit, SourceMovement.StandingHeight, 32 * SourceMovement.Unit), Margin = .001f
		};
		using var hull = new PhysicsShapeQueryParameters3D
		{
			Shape = spawnHull, CollisionMask = 3, Exclude = excluded
		};
		// Shuffle before scoring so an empty server and equally safe positions
		// still use the whole map. Only the host/offline authority selects a point.
		var candidates = (Marker3D[])_deathmatchSpawns.Clone();
		for (var i = candidates.Length - 1; i > 0; i--)
		{
			var other = GD.RandRange(0, i);
			(candidates[i], candidates[other]) = (candidates[other], candidates[i]);
		}
		var choices = new List<(Marker3D Point, int Visible, float Distance, bool Repeat)>();
		foreach (var point in candidates)
		{
			var foot = point.GlobalPosition + Vector3.Up * .04f;
			if (players.Any(p => new Vector2(p.GlobalPosition.X - foot.X, p.GlobalPosition.Z - foot.Z).LengthSquared() < 4
				&& Mathf.Abs(p.GlobalPosition.Y - foot.Y) < SourceMovement.StandingHeight)) continue;
			hull.Transform = new(Basis.Identity, foot + Vector3.Up * SourceMovement.StandingHeight / 2);
			if (space.IntersectShape(hull, 1).Count > 0) continue;
			var floor = space.IntersectRay(PhysicsRayQueryParameters3D.Create(foot + Vector3.Up * .1f,
				foot - Vector3.Up * .2f, 3, excluded));
			if (floor.Count == 0 || floor["normal"].AsVector3().Y < .7f) continue;
			var visible = 0;
			var distance = 24f * 24;
			foreach (var enemy in enemies)
			{
				distance = Mathf.Min(distance, foot.DistanceSquaredTo(enemy.GlobalPosition));
				var eye = enemy.GlobalPosition + Vector3.Up * SourceMovement.StandingEye;
				// World geometry blocks visibility; players do not provide spawn cover.
				var seen = false;
				for (var sample = 0; sample < 2; sample++)
				{
					var height = sample == 0 ? SourceMovement.StandingEye : SourceMovement.StandingHeight / 2;
					var ray = PhysicsRayQueryParameters3D.Create(eye, foot + Vector3.Up * height, 1);
					ray.Exclude = sightExcluded;
					if (space.IntersectRay(ray).Count == 0) { seen = true; break; }
				}
				if (seen) visible++;
			}
			choices.Add((point, visible, distance, _lastDeathmatchSpawn != null
				&& point.GlobalPosition.DistanceSquaredTo(_lastDeathmatchSpawn.GlobalPosition) < 36));
			if (choices.Count >= 32) break;
		}
		var selected = choices.OrderBy(c => c.Visible).ThenBy(c => c.Distance < 100)
			.ThenBy(c => c.Repeat).ThenByDescending(c => c.Distance).FirstOrDefault().Point;
		if (selected != null) _lastDeathmatchSpawn = selected;
		return selected;
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
