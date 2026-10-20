using Godot;
using System.Collections.Generic;
using Contrapoint.Weapons;

namespace Contrapoint.Players;

// Source-style instant bullet traces; the visible tracer is only presentation.
// Knife ranges/delays follow CS:S weapon_knife.cpp (48/32 units, .1/.2 seconds).
public partial class WeaponCombat : Node
{
	[Export] public int MaxMarks { get; set; } = 256;
	[Export] public double MarkLifetime { get; set; } = 90;
	public int BulletsResolved { get; private set; }
	public int MeleeResolved { get; private set; }
	public int MarksCreated { get; private set; }
	public int TracersCreated { get; private set; }
	public Vector3 LastHitPosition { get; private set; }
	public Vector3 LastHitNormal { get; private set; }
	public Vector3 LastShotDirection { get; private set; }
	private Player _player = null!;
	private Node3D _world = null!;
	private readonly List<Attack> _pending = new();
	private readonly List<Smack> _smacks = new();
	private readonly Queue<ImpactMark> _marks = new();
	private readonly Queue<CssoImpact> _impacts = new();
	private readonly Dictionary<string, Texture2D> _textures = new();
	private readonly RandomNumberGenerator _random = new();
	private readonly RandomNumberGenerator _spreadRandom = new();
	private double _combatTime, _nextKnifePrimary = -1;
	private readonly record struct Attack(WeaponView Weapon, Transform3D Aim, float Range, float Damage, bool Knife, bool Stab, double Delay)
	{
		public string Icon { get; init; } = "suicide";
		public KillFlags Flags { get; init; }
		public Vector3 ShotOrigin { get; init; }
	}
	private readonly record struct Smack(Attack Attack, Node3D Collider, Vector3 Point, Vector3 Normal, string Surface, double Delay);

	public override void _Ready()
	{
		_player = GetParent<Player>();
		_world = new Node3D { Name = "WorldEffects", TopLevel = true };
		AddChild(_world);
		_random.Randomize();
		_spreadRandom.Randomize();
	}

	public void QueueBullet(WeaponView weapon, int damage)
	{
		if (!_player.IsAlive || !weapon.Equipped || weapon.Wielder != _player) return;
		_player.EndSpawnProtection();
		var aim = _player.Recoil.GetBulletAim();
		if (weapon is FirearmView { PelletCount: > 1 } shotgun)
		{
			var pellets = SourceWeaponSpread.SamplePellets(aim, shotgun, _player, _spreadRandom);
			Contrapoint.Network.LanSession.Instance?.RequestPellets(_player, shotgun, pellets);
			foreach (var pellet in pellets) QueueProjectile(weapon, pellet, damage, relay: false);
			return;
		}
		if (weapon is FirearmView gun) aim = SourceWeaponSpread.Sample(aim, gun, _player, _spreadRandom);
		QueueProjectile(weapon, aim, damage);
	}

	private void QueueProjectile(WeaponView weapon, Transform3D aim, int damage, bool relay = true)
	{
		LastShotDirection = -aim.Basis.Z;
		if (relay) Contrapoint.Network.LanSession.Instance?.RequestAttack(_player, weapon, aim, false, false);
		_pending.Add(new(weapon, aim, weapon is FirearmView firearm ? SourceDamageRules.Range(firearm) : 300,
			weapon is FirearmView firearmDamage ? firearmDamage.EffectiveBulletDamage : damage, false, false, 0)
			{ Icon = KillNotice.WeaponIcon(weapon), Flags = KillNotice.AttackFlags(_player, weapon), ShotOrigin = aim.Origin });
	}

	public void Reset() { _pending.Clear(); _smacks.Clear(); _nextKnifePrimary = -1; }

	public void QueueKnife(WeaponView weapon, bool stab)
	{
		if (!_player.IsAlive || !weapon.Equipped || weapon.Wielder != _player) return;
		_player.EndSpawnProtection();
		Contrapoint.Network.LanSession.Instance?.RequestAttack(_player, weapon, _player.Camera.GlobalTransform, true, stab);
		QueueKnifeAttack(weapon, _player.Camera.GlobalTransform, stab);
	}

	private void QueueKnifeAttack(WeaponView weapon, Transform3D aim, bool stab)
	{
		var first = _nextKnifePrimary + .4 < _combatTime;
		var attack = new Attack(weapon, aim, (stab ? 32 : 48) * SourceMovement.Unit,
			(int)SourceDamageRules.KnifeDamage(stab, first, false), true, stab, 0)
			{ Icon = "knife", Flags = KillNotice.AttackFlags(_player, weapon), ShotOrigin = aim.Origin };
		// CSS applies damage immediately; only the smack sound/decal waits .1/.2s.
		_pending.Add(attack);
		_nextKnifePrimary = _combatTime + (stab ? 1.1 : .5);
	}

	public override void _PhysicsProcess(double delta)
	{
		_combatTime += delta;
		for (var i = 0; i < _pending.Count;)
		{
			var attack = _pending[i] with { Delay = _pending[i].Delay - delta };
			if (attack.Delay > 0) { _pending[i++] = attack; continue; }
			_pending.RemoveAt(i);
			if (_player.IsAlive && GodotObject.IsInstanceValid(attack.Weapon)) Resolve(attack);
		}
		for (var i = 0; i < _smacks.Count;)
		{
			var smack = _smacks[i] with { Delay = _smacks[i].Delay - delta };
			if (smack.Delay > 0) { _smacks[i++] = smack; continue; }
			_smacks.RemoveAt(i);
			if (GodotObject.IsInstanceValid(smack.Collider)) PresentSmack(smack);
		}
	}

	private void Resolve(Attack attack, int depth = 0, float penetrationPower = -1, float travelled = 0)
	{
		var origin = attack.Aim.Origin;
		var direction = -attack.Aim.Basis.Z;
		var end = origin + direction * attack.Range;
		var ray = PhysicsRayQueryParameters3D.Create(origin, end);
		ray.CollisionMask = 1; // Source playerclip and trigger brushes do not stop shots.
		ray.Exclude = new() { _player.GetRid() };
		if (!attack.Knife)
			foreach (Player body in GetTree().GetNodesInGroup("source_players")) ray.Exclude = AppendRid(ray.Exclude, body.GetRid());
		var hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(ray);
		var group = SourceHitGroup.Generic;
		if (!attack.Knife)
		{
			var closest = hit.Count > 0 ? origin.DistanceTo(hit["position"].AsVector3()) / attack.Range : 1f;
			foreach (Player candidate in GetTree().GetNodesInGroup("source_players"))
			{
				if (candidate == _player || !candidate.IsAlive || candidate.Movement.Noclip || candidate.Team is PlayerTeam.Spectator or PlayerTeam.Unassigned) continue;
				if (!candidate.Body.Hitboxes.Trace(origin, end, out var fraction, out var hitGroup, out var normal) || fraction >= closest) continue;
				closest = fraction; group = hitGroup;
				hit = new() { ["position"] = origin.Lerp(end, fraction), ["normal"] = normal, ["collider"] = candidate };
			}
		}
		else if (hit.Count == 0) hit = TraceKnifeHull(origin, end);
		if (attack.Knife)
		{
			var didHit = hit.Count > 0;
			_nextKnifePrimary = _combatTime + (attack.Stab ? didHit ? 1.1 : 1 : didHit ? .5 : .4);
			if (attack.Weapon is KnifeView knife) knife.ResolveContact(didHit, attack.Stab);
		}
		if (depth == 0) { if (attack.Knife) MeleeResolved++; else BulletsResolved++; }
		if (hit.Count > 0) end = hit["position"].AsVector3();
		var network = Contrapoint.Network.LanSession.Instance;
		if (!attack.Knife) network?.BroadcastImpact(_player, attack.Aim, end, hit.Count > 0 ? hit["normal"].AsVector3() : Vector3.Zero,
			hit.Count > 0 ? hit["collider"].AsGodotObject() as Node3D : null, attack.Knife, attack.Stab);
		if (!attack.Knife)
		{
			SourceWaterRipple.Spawn(_world, origin, end);
			CreateTracer(attack.Aim, end);
		}
		if (hit.Count == 0) return;
		LastHitPosition = end;
		LastHitNormal = hit["normal"].AsVector3().Normalized();
		var collider = hit["collider"].AsGodotObject() as Node3D;
		var surface = collider == null ? "concrete" : SourceBulletPenetration.Surface(collider, hit);
		var currentDamage = attack.Damage;
		var currentDistance = travelled + origin.DistanceTo(end);
		if (!attack.Knife && attack.Weapon is FirearmView rangeGun)
			currentDamage = SourceDamageRules.DistanceDamage(currentDamage, currentDistance, SourceDamageRules.RangeModifier(rangeGun));
		if (collider is Player target)
		{
			if (attack.Knife) _smacks.Add(new(attack, target, end, LastHitNormal, "flesh", attack.Stab ? .2 : .1));
			if (network?.IsClient != true)
			{
				var damage = currentDamage;
				var ratio = SourceDamageRules.WeaponValue("knife", "armor_ratio");
				var large = false;
				if (attack.Knife && attack.Stab && SourceDamageRules.IsBackstab(_player.GlobalPosition, target.GlobalPosition, -target.GlobalBasis.Z)) damage *= 3;
				if (!attack.Knife && attack.Weapon is FirearmView gun)
				{
					ratio = SourceDamageRules.ArmorRatio(gun);
					large = !gun.IsPistol;
				}
				target.TakeTraceDamage(damage, _player, group, end, direction, ratio, attack.Knife, large,
					attack.Weapon is FirearmView forceGun ? forceGun.BulletPhysicsImpulse : 300 * SourceMovement.Unit,
					attack.Icon, attack.Flags | (depth > 0 ? KillFlags.Penetrated : KillFlags.None)
					| (!attack.Knife && KillNotice.ThroughSmoke(this, attack.ShotOrigin, end) ? KillFlags.ThroughSmoke : KillFlags.None));
			}
			ContinueBullet(attack, target, surface, end, direction, currentDamage, currentDistance, depth, penetrationPower);
			return;
		}
		(Vector3 Point, Vector3 Normal, string Surface)? knownExit = null;
		if (!attack.Knife && collider != null && SourceBulletPenetration.FindExit(_player, collider, end, direction,
			out var cachedExit, out var cachedNormal, out var cachedSurface)) knownExit = (cachedExit, cachedNormal, cachedSurface);
		if (network?.IsClient != true && collider != null)
		{
			if (!attack.Knife && attack.Weapon is FirearmView firearm && collider.HasMethod("source_bullet_damage"))
				collider.Call("source_bullet_damage", currentDamage, end, direction, firearm.BulletPhysicsImpulse);
			else if (collider.HasMethod("source_damage"))
				collider.Call("source_damage", currentDamage, end, direction);
		}
		if (collider == null) return;
		if (attack.Knife) _smacks.Add(new(attack, collider, end, LastHitNormal, surface, attack.Stab ? .2 : .1));
		else if (!collider.HasMeta("no_impact_marks")) CreateMark(collider, end, LastHitNormal, surface, false, false);
		ContinueBullet(attack, collider, surface, end, direction, currentDamage, currentDistance, depth, penetrationPower, knownExit);
	}

	private void ContinueBullet(Attack attack, Node3D collider, string surface, Vector3 point, Vector3 direction,
		float damage, float distance, int depth, float power, (Vector3 Point, Vector3 Normal, string Surface)? knownExit = null)
	{
		if (attack.Knife || attack.Weapon is not FirearmView gun) return;
		var count = (int)SourceDamageRules.WeaponValue(SourceDamageRules.WeaponName(gun), "penetration");
		var grate = surface.Contains("grate") || surface.Contains("chainlink");
		if (depth > count || depth == count && !grate) return;
		var ammo = SourceBulletPenetration.Ammo(gun);
		if (distance / SourceMovement.Unit > ammo.Distance) return;
		if (power < 0) power = ammo.Power;
		Vector3 exit, normal; string exitSurface;
		if (knownExit is { } cached) { exit = cached.Point; normal = cached.Normal; exitSurface = cached.Surface; }
		else if (!SourceBulletPenetration.FindExit(_player, collider, point, direction, out exit, out normal, out exitSurface)) return;
		var material = SourceBulletPenetration.Material(surface);
		if (surface == exitSurface && (surface.Contains("wood") || surface.Contains("metal"))) material.Power *= 2;
		var thickness = point.DistanceTo(exit) / SourceMovement.Unit;
		if (thickness > power * material.Power) return;
		var remaining = (attack.Range - distance - thickness * SourceMovement.Unit) * .5f;
		if (remaining <= 0 || damage * material.Damage < 1) return;
		if (collider is not Player && !collider.HasMeta("no_impact_marks"))
		{
			CreateMark(collider, exit, normal, exitSurface, false, false);
			Contrapoint.Network.LanSession.Instance?.BroadcastImpact(_player, attack.Aim, exit, normal, collider, false, false);
		}
		Resolve(attack with { Aim = new(attack.Aim.Basis, exit + direction * .001f), Range = remaining, Damage = damage * material.Damage },
			depth + 1, power - thickness / material.Power, distance + thickness * SourceMovement.Unit);
	}

	private void PresentSmack(Smack smack)
	{
		var network = Contrapoint.Network.LanSession.Instance;
		var attack = smack.Attack;
		var sound = smack.Collider is Player ? attack.Stab ? "weapon_knife.stab" : "weapon_knife.hit" : "weapon_knife.hitwall";
		if (network?.IsClient != true)
		{
			if (_player.LocalInput || network?.IsHosting != true) attack.Weapon.Sounds.PlayScript(sound, relay: false);
			else SourceSoundPlayer.PlayWorld(_world, smack.Point, sound);
			network?.BroadcastWorldScript(smack.Point, sound);
		}
		network?.BroadcastImpact(_player, attack.Aim, smack.Point, smack.Normal, smack.Collider, true, attack.Stab);
		if (smack.Collider is not Player && !smack.Collider.HasMeta("no_impact_marks"))
			CreateMark(smack.Collider, smack.Point, smack.Normal, smack.Surface, true, attack.Stab);
	}

	public void ResolveNetworkAttack(WeaponView weapon, Transform3D aim, bool knife, bool stab, bool scoped = false)
	{
		if (knife) { QueueKnifeAttack(weapon, aim, stab); return; }
		if (weapon is FirearmView gun) _pending.Add(new(weapon, aim, SourceDamageRules.Range(gun), gun.EffectiveBulletDamage, false, false, 0)
			{ Icon = KillNotice.WeaponIcon(weapon), Flags = KillNotice.AttackFlags(_player, weapon, scoped), ShotOrigin = aim.Origin });
	}

	private static Godot.Collections.Array<Rid> AppendRid(Godot.Collections.Array<Rid> source, Rid rid)
	{
		var copy = source.Duplicate(); copy.Add(rid); return copy;
	}

	private Godot.Collections.Dictionary TraceKnifeHull(Vector3 from, Vector3 to)
	{
		var query = new PhysicsShapeQueryParameters3D
		{
			Shape = new BoxShape3D { Size = new Vector3(32, 36, 32) * SourceMovement.Unit },
			Transform = new(Basis.Identity, from), Motion = to - from,
			CollisionMask = 1, Exclude = new() { _player.GetRid() }, Margin = 0
		};
		var space = _player.GetWorld3D().DirectSpaceState;
		var fractions = space.CastMotion(query);
		if (fractions.Length < 2 || fractions[1] >= 1) return new();
		query.Transform = new(Basis.Identity, from.Lerp(to, fractions[1])); query.Motion = Vector3.Zero;
		var rest = space.GetRestInfo(query);
		if (rest.Count == 0) return new();
		var collider = GodotObject.InstanceFromId(rest["collider_id"].AsUInt64());
		if (collider == null) return new();
		return new() { ["position"] = rest["point"], ["normal"] = rest["normal"], ["collider"] = collider, ["shape"] = rest["shape"] };
	}

	public void PresentNetworkImpact(Transform3D aim, Vector3 end, Vector3 normal, Node3D? collider, bool knife, bool stab)
	{
		if (!knife)
		{
			CreateTracer(aim, end);
		}
		if (collider == null || collider is Player || collider.HasMeta("no_impact_marks") || normal.IsZeroApprox()) return;
		var surface = collider.HasMeta("source_surface") ? collider.GetMeta("source_surface").AsString() : "concrete";
		CreateMark(collider, end, normal, surface, knife, stab);
	}

	private void CreateTracer(Transform3D aim, Vector3 end)
	{
		if (_player.LocalInput && !_player.IsThirdPerson && Contrapoint.UI.GamePreferences.Value("r_drawtracers_firstperson", 1) == 0) return;
		var start = TracerStart(aim, end);
		// Source FX_PlayerTracer / FX_Tracer suppress paths shorter than 256 units.
		const float minimumDistance = 256 * SourceMovement.Unit;
		if (start.DistanceSquaredTo(end) < minimumDistance * minimumDistance) return;
		var tracer = new ShotTracer();
		_world.AddChild(tracer);
		tracer.Initialize(start, end, Contrapoint.Network.LanSession.Instance?.LocalPlayer?.RenderCamera ?? _player.RenderCamera);
		TracersCreated++;
	}

	private Vector3 TracerStart(Transform3D aim, Vector3 end)
	{
		if ((!_player.LocalInput || _player.IsThirdPerson) && _player.Body.TryGetMuzzlePosition(out var muzzle)) return muzzle;
		// First-person viewmodels use a separate viewport and projection.
		return aim.Origin.DistanceTo(end) < .5f ? aim.Origin
			: aim.Origin + aim.Basis * new Vector3(.18f, -.12f, -.35f);
	}

	private void CreateMark(Node3D collider, Vector3 point, Vector3 normal, string surface, bool knife, bool stab)
	{
		if (!knife && !normal.IsZeroApprox() && Contrapoint.UI.GamePreferences.Value("cl_new_impact_effects", 1) != 0)
		{
			while (_impacts.Count > 0 && !GodotObject.IsInstanceValid(_impacts.Peek())) _impacts.Dequeue();
			while (_impacts.Count >= 48)
			{
				var oldest = _impacts.Dequeue();
				if (GodotObject.IsInstanceValid(oldest)) oldest.QueueFree();
			}
			var effect = new CssoImpact { Name = "CssoBulletImpact" };
			_world.AddChild(effect);
			effect.Initialize(point, normal, surface);
			_impacts.Enqueue(effect);
		}
		if (MaxMarks <= 0 || MarkLifetime <= 0) return;
		while (_marks.Count > 0 && !GodotObject.IsInstanceValid(_marks.Peek())) _marks.Dequeue();
		while (_marks.Count >= MaxMarks)
		{
			var oldest = _marks.Dequeue();
			if (GodotObject.IsInstanceValid(oldest)) oldest.QueueFree();
		}
		var family = surface.Contains("wood") ? "wood" : surface.Contains("metal") ? "metal" : "concrete";
		var number = family == "metal" ? _random.RandiRange(6, 7) : _random.RandiRange(1, 5);
		var cut = _random.RandiRange(1, 3);
		var name = knife ? "decals_manhackcut" + (cut == 1 ? "" : cut.ToString()) : $"decals_{family}_shot{number}";
		if (!_textures.TryGetValue(name, out var texture))
		{
			texture = GD.Load<Texture2D>($"res://assets/effects/css/{name}.png");
			_textures.Add(name, texture);
		}
		var size = knife ? new Vector2(stab ? 0.12f : 0.28f, stab ? 0.03f : 0.07f) : new Vector2(0.1225f, 0.1225f);
		var mark = new ImpactMark { Name = knife ? "KnifeScratch" : "BulletHole" };
		_world.AddChild(mark);
		mark.Initialize(texture, size, point, normal, _random.RandfRange(-Mathf.Pi, Mathf.Pi), MarkLifetime);
		mark.Reparent(collider, true); // Moving targets carry the mark with them.
		_marks.Enqueue(mark);
		MarksCreated++;
	}

	public override void _ExitTree()
	{
		_pending.Clear();
		foreach (var mark in _marks)
			if (GodotObject.IsInstanceValid(mark)) mark.QueueFree();
		_marks.Clear();
		_impacts.Clear();
		_textures.Clear();
	}
}
