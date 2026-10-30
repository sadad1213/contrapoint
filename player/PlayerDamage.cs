using Godot;
using Contrapoint.Network;
using Contrapoint.Weapons;
using Contrapoint.Gameplay;

namespace Contrapoint.Players;

public enum PlayerDamageKind { Administrative, Fall, HE, Bullet, Knife, World, Bomb }

public partial class Player
{
	public PlayerDamageKind LastDamageKind { get; private set; }
	public SourceHitGroup LastHitGroup { get; private set; }
	public int LastHealthDamage { get; private set; }
	public int LastArmorDamage { get; private set; }
	public string LastDamageSound { get; private set; } = "";
	public bool GodMode { get; set; }
	public bool BuddhaMode { get; set; }
	public void TakeDebugDamage(float amount) => ApplyDamage(amount, PlayerDamageKind.World);
	public static float FallDamage(float speed) => Mathf.Max(0, speed - 580) * (100f / (1100 - 580)) * 1.25f;
	public static float HeDamage(float distance, float exposure = 1) =>
		100 * Mathf.Exp(-distance * distance / (2 * (350f / 3) * (350f / 3))) * Mathf.Clamp(exposure, 0, 1);

	private void OnLanded(float speed)
	{
		if (!IsAlive || Movement.Noclip || Movement.WaterLevel > 0) return;
		if (speed >= 350) Recoil.AddLandingPunch(speed);
		if (LanSession.Instance?.RequestFallDamage(this, speed) == true) return;
		ApplyFallDamage(speed);
	}

	public void ApplyFallDamage(float speed)
	{
		if (SourceDamageRules.FallDamageEnabled) ApplyDamage(FallDamage(speed), PlayerDamageKind.Fall);
	}

	public void TakeHeDamage(float amount, Player? attacker, Vector3? explosion = null)
	{
		var ratio = 1f;
		if (attacker != null)
		{
			if (!SourceDamageRules.LegacyGrenadeDamage) ratio = SourceDamageRules.WeaponValue("hegrenade", "armor_ratio");
			else if (attacker.ActiveWeapon is FirearmView gun) ratio = SourceDamageRules.ArmorRatio(gun);
			else if (attacker.ActiveWeapon is KnifeView) ratio = SourceDamageRules.WeaponValue("knife", "armor_ratio");
			else if (attacker.ActiveWeapon is GrenadeView grenade)
				ratio = SourceDamageRules.WeaponValue(grenade.Kind == GrenadeKind.HE ? "hegrenade"
					: grenade.Kind == GrenadeKind.Flashbang ? "flashbang" : "smokegrenade", "armor_ratio");
		}
		var direction = explosion.HasValue ? (GlobalPosition + Vector3.Up - explosion.Value).Normalized() : Vector3.Zero;
		ApplyDamage(amount, PlayerDamageKind.HE, attacker, armorRatio: ratio, position: explosion ?? GlobalPosition,
			direction: direction, force: Mathf.Min(15000, amount * 300) * SourceMovement.Unit * 1.5f);
	}

	public void TakeTraceDamage(float amount, Player attacker, SourceHitGroup group, Vector3 position,
		Vector3 direction, float armorRatio, bool knife = false, bool largeFlinch = false, float impulse = 0,
		string? weaponIcon = null, KillFlags killFlags = KillFlags.None) =>
		ApplyDamage(amount * SourceDamageRules.HitMultiplier(group), knife ? PlayerDamageKind.Knife : PlayerDamageKind.Bullet,
			attacker, group, armorRatio, position, direction, largeFlinch,
			impulse > 0 ? impulse : knife ? 300 * SourceMovement.Unit : (attacker.ActiveWeapon as FirearmView)?.BulletPhysicsImpulse ?? 0,
			weaponIcon, killFlags);

	private void ApplyDamage(float amount, PlayerDamageKind kind, Player? attacker = null,
		SourceHitGroup group = SourceHitGroup.Generic, float armorRatio = 1,
		Vector3 position = default, Vector3 direction = default, bool largeFlinch = false, float force = 0,
		string? weaponIcon = null, KillFlags killFlags = KillFlags.None)
	{
		if (!IsAlive || !float.IsFinite(amount) || amount <= 0 || LanSession.Instance?.IsClient == true) return;
		var gameplay = kind != PlayerDamageKind.Administrative;
		if (gameplay && (Movement.Noclip || Team == PlayerTeam.Spectator || GodMode && SourceDamageRules.Cheats)) return;
		if (gameplay && SpawnProtected) return;
		var teammate = attacker != null && DeathmatchRules.Teammates(attacker, this);
		if (gameplay && teammate)
		{
			if (!SourceDamageRules.FriendlyFire) return;
			if (kind != PlayerDamageKind.HE) amount *= .35f;
		}
		LastDamageKind = kind;
		LastHitGroup = group;
		var punch = Vector3.Zero;
		var helmetHit = group == SourceHitGroup.Head && HasHelmet;
		var bloodMagnitude = kind is PlayerDamageKind.Bullet or PlayerDamageKind.Knife
			? amount * (Armor > 0 ? .5f : 1) * (helmetHit ? .5f : 1) : 0;
		var oldArmor = Armor;
		if (kind is PlayerDamageKind.Bullet or PlayerDamageKind.Knife)
		{
			if (group == SourceHitGroup.Head && !HasHelmet)
				punch = new(Mathf.Max(-12, amount * -.5f), 0, Mathf.Clamp(amount * (float)GD.RandRange(-1.0, 1.0), -9, 9));
			else if (Armor <= 0 && group is SourceHitGroup.Chest or SourceHitGroup.Stomach)
				punch.X = Mathf.Max(-4, amount * -.1f);
		}
		// CS deliberately does not apply damage force to a living player's velocity.
		var modifier = Movement.DamageSpeedModifier;
		if (gameplay)
		{
			if (largeFlinch && !Movement.IsDucked && group is not (SourceHitGroup.LeftLeg or SourceHitGroup.RightLeg))
			{ if (Velocity.Length() / SourceMovement.Unit < 300) modifier = .65f; }
			else modifier = .5f;
		}
		var armored = gameplay && kind != PlayerDamageKind.Fall && SourceDamageRules.IsArmored(group, Armor, HasHelmet);
		if (armored)
		{
			var healthDamage = amount * armorRatio * .5f;
			var armorDamage = (amount - healthDamage) * .5f;
			if (armorDamage > Armor) { healthDamage = amount - Armor / .5f; Armor = 0; }
			else Armor = (int)(Armor - Mathf.Max(0, armorDamage));
			if (Armor == 0) HasHelmet = false;
			amount = healthDamage;
		}
		// CCSPlayer truncates health damage BEFORE CBaseCombatCharacter's accumulator.
		var whole = Mathf.FloorToInt(amount);
		var health = Mathf.Max(BuddhaMode && SourceDamageRules.Cheats && gameplay ? 1 : 0, Health - whole);
		LastHealthDamage = whole;
		LastArmorDamage = oldArmor - Armor;
		var sound = !gameplay ? "" : kind == PlayerDamageKind.Fall ? "player.falldamage"
			: group == SourceHitGroup.Head ? HasHelmet ? "player.damagehelmet" : "player.damageheadshot"
			: armored ? "player.damagekevlar" : "flesh.bulletimpact";
		var deathSound = health == 0 ? group == SourceHitGroup.Head ? "player.deathheadshot" : "player.death" : "";
		if (health == 0 && Death != null)
		{
			var cause = kind == PlayerDamageKind.Fall ? "Fall" : kind == PlayerDamageKind.HE ? "HE"
				: kind == PlayerDamageKind.Knife ? "Knife" : kind == PlayerDamageKind.Bullet
					? (attacker?.ActiveWeapon as FirearmView)?.Kind.ToString() ?? "Bullet" : "";
			Death.Prepare(attacker, position, direction.Normalized() * force * 2, group, cause);
			LanSession.Instance?.BroadcastDeath(this);
		}
		SetVitals(health, Armor, HasHelmet);
		if (health == 0)
		{
			RecordDeathmatchDeath(attacker, kind);
			DefuseState.Instance?.RecordDeath(this, attacker, kind);
			KillNotice.Publish(this, attacker, kind, group, weaponIcon, killFlags);
		}
		PresentDamageFeedback(kind, group, LastHealthDamage, LastArmorDamage, punch, modifier, sound, deathSound, position, direction, bloodMagnitude, helmetHit);
		LanSession.Instance?.BroadcastDamageFeedback(this, kind, group, LastHealthDamage, LastArmorDamage,
			punch, modifier, sound, deathSound, position, direction, bloodMagnitude, helmetHit);
		LanSession.Instance?.BroadcastVitals(this);
		if (SourceDamageRules.DebugDamage)
		{
			var line = $"{Name}: {kind} {group}, damage {LastHealthDamage} health / {LastArmorDamage} armor; remaining {Health}/{Armor}";
			GD.Print(line); Console?.Print(line, false);
		}
	}

	public void PresentDamageFeedback(PlayerDamageKind kind, SourceHitGroup group, int healthDamage, int armorDamage,
		Vector3 punch, float speedModifier, string sound, string deathSound, Vector3 position, Vector3 direction, float bloodMagnitude, bool helmetHit)
	{
		LastDamageKind = kind; LastHitGroup = group;
		LastHealthDamage = healthDamage; LastArmorDamage = armorDamage; LastDamageSound = sound;
		Movement.DamageSpeedModifier = speedModifier;
		if (punch != Vector3.Zero) Recoil.SetDamagePunch(punch);
		if (kind == PlayerDamageKind.HE && healthDamage >= 30) DamageHearing.Shock();
		if (!direction.IsZeroApprox()) SourceDamageEffects.Spawn(GetParent<Node3D>(), position, direction, bloodMagnitude, helmetHit);
		foreach (var name in new[] { sound, deathSound })
		{
			if (name.Length == 0) continue;
			if (LocalInput) MovementAudio.Sounds.PlayScript(name, relay: false);
			else SourceSoundPlayer.PlayWorld(GetParent<Node3D>(), Camera.GlobalPosition, name);
		}
	}

	public void SetVitals(int health, int armor, bool helmet)
	{
		var wasAlive = IsAlive;
		Armor = Mathf.Clamp(armor, 0, 100);
		HasHelmet = helmet && Armor > 0;
		health = Mathf.Clamp(health, 0, MaxHealth);
		if (Health != health) { Health = health; EmitSignal(SignalName.HealthChanged, Health); }
		if (wasAlive && !IsAlive) Die();
		else if (!wasAlive && IsAlive)
		{
			Death?.Reset();
			Movement.DamageSpeedModifier = 1;
			Recoil.Reset();
			Inventory.SetEnabled(!LocalInput || MouseCaptured);
			EmitSignal(SignalName.Respawned);
		}
	}
}
