using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Contrapoint.Players;
using Contrapoint.Network;

namespace Contrapoint.Weapons;

// CSS CWeaponCSBase::Drop/DefaultTouch/Spawn: world PHY, 200 deg/s spin,
// 30-unit trigger bloat and a 0.8-second lockout for the previous owner only.
public partial class WeaponPickup : RigidBody3D
{
	[Export] public string WeaponClass { get; set; } = "weapon_ak47";
	[Export] public int Magazine { get; set; } = -1;
	// -1 requests the weapon's full default reserve for authored map pickups.
	[Export] public int ExtraAmmo { get; set; }
	private bool? _silenced;
	[Export] public bool Silenced
	{
		get => _silenced ?? WeaponClass is "weapon_usp" or "weapon_m4a1";
		set => _silenced = value;
	}
	[Export] public bool BurstMode { get; set; }
	[Export] public int GrenadeAmmo { get; set; } = 1;
	public Player? PreviousOwner { get; set; }
	public bool NetworkReplica { get; set; }
	public double Age { get; private set; }
	public bool Taken { get; private set; }
	public const double PreviousOwnerDelay = .8;
	private Aabb _bounds;
	private Node3D _model = null!;
	private readonly HashSet<ulong> _excluded = [];
	private double _nextImpactSound;
	private float _impactSpeed;
	private Transform3D? _networkTarget;
	private static readonly JsonElement Solids = JsonDocument.Parse(
		FileAccess.GetFileAsString("res://assets/physics/css_weapon_solids.json")).RootElement.Clone();
	public static IEnumerable<string> Classes => Solids.EnumerateObject().Select(p => p.Name);
	public static bool IsSupported(string name) => Solids.TryGetProperty(name, out _);
	public static string ClassOf(WeaponView weapon) => "weapon_" + (weapon is FirearmView gun
		? gun.Kind.ToString().ToLowerInvariant() : weapon is GrenadeView grenade ? grenade.AssetName : weapon is C4View ? "c4" : "knife");
	public static string ViewPath(string name) => "res://weapons/" + name[7..] + "_view.tscn";
	public WeaponSlot Slot => WeaponClass is "weapon_deagle" or "weapon_usp" or "weapon_elite"
		or "weapon_glock" or "weapon_p228" or "weapon_fiveseven" ? WeaponSlot.Secondary
		: WeaponClass is "weapon_hegrenade" or "weapon_flashbang" or "weapon_smokegrenade" ? WeaponSlot.Grenade : WeaponSlot.Primary;

	public override void _Ready()
	{
		if (!Solids.TryGetProperty(WeaponClass, out var data)) { GD.PushError("Unsupported weapon entity: " + WeaponClass); QueueFree(); return; }
		AddToGroup("source_weapon_pickups");
		Mass = data.GetProperty("mass").GetSingle();
		SetMeta("source_surface", data.GetProperty("surface").GetString()!);
		CollisionLayer = CollisionMask = 1;
		ContinuousCd = true;
		ContactMonitor = true; MaxContactsReported = 4;
		BodyEntered += OnBodyEntered;
		// Source surfaceproperties weapon/default. Jolt supplies the rigid-body solver.
		PhysicsMaterialOverride = new PhysicsMaterial { Friction = .8f, Bounce = .25f };
		LinearDamp = .1f; AngularDamp = .1f;
		var points = new List<Vector3>();
		foreach (var solid in data.GetProperty("physics_hulls").EnumerateArray())
		{
			var hull = solid.EnumerateArray().Select(p => new Vector3(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle())).ToArray();
			points.AddRange(hull);
			AddChild(new CollisionShape3D { Shape = new ConvexPolygonShape3D { Points = hull, Margin = .001f } });
		}
		_bounds = new(points[0], Vector3.Zero);
		foreach (var point in points) _bounds = _bounds.Expand(point);
		var id = WeaponClass[7..];
		var modelPath = id == "elite" ? "res://assets/weapons/elite/world/dropped/elite_world.glb"
			: $"res://assets/weapons/{id}/world/{id}_world.glb";
		_model = GD.Load<PackedScene>(modelPath).Instantiate<Node3D>();
		AddChild(_model);
		WorldWeaponMaterials.Apply(_model);
		foreach (MeshInstance3D mesh in _model.FindChildren("*", "MeshInstance3D", true, false)) mesh.Layers = 1;
		if (NetworkReplica) { FreezeMode = FreezeModeEnum.Kinematic; Freeze = true; }
		foreach (var player in GetTree().GetNodesInGroup("source_players").OfType<Player>()) Exclude(player);
		foreach (var weapon in GetTree().GetNodesInGroup("source_weapon_pickups").OfType<WeaponPickup>())
			if (weapon != this) { Exclude(weapon); weapon.Exclude(this); }
	}

	private void Exclude(PhysicsBody3D body)
	{
		if (_excluded.Add(body.GetInstanceId())) AddCollisionExceptionWith(body);
	}

	public override void _IntegrateForces(PhysicsDirectBodyState3D state)
	{
		if (NetworkReplica) return;
		_impactSpeed = state.LinearVelocity.Length();
		// sv_gravity uses inches/s²; project gravity otherwise defaults to 9.8 m/s².
		var gravity = GodotObject.IsInstanceValid(PreviousOwner) ? PreviousOwner!.Movement.Gravity : 800;
		state.LinearVelocity += Vector3.Down * (gravity * SourceMovement.Unit - state.TotalGravity.Length()) * state.Step;
	}

	private void OnBodyEntered(Node body)
	{
		if (NetworkReplica || body is Player || Age < _nextImpactSound || _impactSpeed < 40 * SourceMovement.Unit) return;
		_nextImpactSound = Age + .1;
		var strength = _impactSpeed > 100 * SourceMovement.Unit ? "hard" : "soft";
		var path = $"physics/metal/weapon_impact_{strength}{GD.RandRange(1, 3)}.wav";
		var gain = Mathf.Clamp(_impactSpeed / 6, .15f, 1);
		if (GetParent() is Node3D world) SourceSoundPlayer.PlayWorldWave(world, GlobalPosition, path, gain);
		LanSession.Instance?.BroadcastWeaponImpact(GlobalPosition, path, gain);
	}

	public void ApplyNetworkTransform(Transform3D transform)
	{
		if (_networkTarget == null || GlobalPosition.DistanceTo(transform.Origin) > 4) GlobalTransform = transform;
		_networkTarget = transform;
	}
	public override void _Process(double delta)
	{
		if (NetworkReplica && _networkTarget is { } target)
			GlobalTransform = GlobalTransform.InterpolateWith(target, 1 - Mathf.Exp(-30 * (float)delta));
	}

	public override void _PhysicsProcess(double delta)
	{
		if (NetworkReplica || LanSession.Instance?.IsClient == true || Taken) return;
		Age += delta;
		foreach (var player in GetTree().GetNodesInGroup("source_players").OfType<Player>())
		{
			Exclude(player); // Weapons never block, support, or push a player.
			if (!player.IsAlive || player.Team is not (PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist)) continue;
			var bounds = GlobalTransform * _bounds;
			bounds = bounds.Grow(30 * SourceMovement.Unit);
			var height = player.Movement.IsDucked ? SourceMovement.DuckHeight : SourceMovement.StandingHeight;
			var playerBounds = new Aabb(player.GlobalPosition - new Vector3(16 * SourceMovement.Unit, 0, 16 * SourceMovement.Unit),
				new Vector3(32 * SourceMovement.Unit, height, 32 * SourceMovement.Unit));
			if (bounds.Intersects(playerBounds) && TryPickup(player)) break;
		}
	}

	public bool TryPickup(Player player)
	{
		if (Taken || NetworkReplica || LanSession.Instance?.IsClient == true || !player.IsAlive
			|| player.Team is not (PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist)
			|| player == PreviousOwner && Age < PreviousOwnerDelay) return false;
		// Do not collect through a wall, even where the expanded trigger overlaps it.
		var excluded = new Godot.Collections.Array<Rid> { player.GetRid(), GetRid() };
		using var query = PhysicsRayQueryParameters3D.Create(player.GlobalPosition + Vector3.Up * (SourceMovement.DuckHeight / 2),
			GlobalTransform * _bounds.GetCenter(), 1, excluded);
		while (true)
		{
			var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
			if (hit.Count == 0) break;
			// Death drops can overlap: a grenade or another gun is not a wall.
			if (hit["collider"].AsGodotObject() is not WeaponPickup pickup) return false;
			excluded.Add(pickup.GetRid());
			query.Exclude = excluded;
		}
		if (!player.Inventory.TryPickup(this)) return false;
		Taken = true;
		Visible = false;
		LanSession.Instance?.RemoveWeaponPickup(this);
		QueueFree();
		return true;
	}

	// WeaponCombat uses the same hook as Source map props, including offset torque.
	public void source_bullet_damage(float amount, Vector3 point, Vector3 direction, float impulse)
	{
		if (NetworkReplica || LanSession.Instance?.IsClient == true || amount <= 0) return;
		Sleeping = false;
		ApplyImpulse(direction.Normalized() * impulse, point - GlobalPosition);
	}

	public void source_damage(float amount, Vector3 point, Vector3 direction, float impulse = -1) =>
		source_bullet_damage(amount, point, direction, impulse >= 0 ? impulse : amount * .15f);

	public static WeaponPickup Spawn(Node3D world, string name, Transform3D transform, Vector3 velocity = default,
		Vector3 spin = default, Player? owner = null, int magazine = -1, int reserve = 0, bool? silenced = null, int grenades = 1, bool burst = false)
	{
		if (!IsSupported(name)) throw new ArgumentException("Unsupported weapon entity: " + name);
		var pickup = new WeaponPickup { Name = name, WeaponClass = name, PreviousOwner = owner,
			Magazine = magazine, ExtraAmmo = reserve, _silenced = silenced, GrenadeAmmo = grenades, BurstMode = burst };
		world.AddChild(pickup, true);
		pickup.GlobalTransform = transform;
		pickup.LinearVelocity = velocity; pickup.AngularVelocity = spin;
		LanSession.Instance?.RegisterWeaponPickup(pickup);
		return pickup;
	}
}
