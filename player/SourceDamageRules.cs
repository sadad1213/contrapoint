using Godot;
using System.Text.Json;
using Contrapoint.Weapons;

namespace Contrapoint.Players;

public enum SourceHitGroup { Generic, Head, Chest, Stomach, LeftArm, RightArm, LeftLeg, RightLeg }

// CCSPlayer::TraceAttack / OnTakeDamage and installed stock weapon_*.ctx.
public static class SourceDamageRules
{
	public static bool FriendlyFire { get; set; } = true;
	public static bool FallDamageEnabled { get; set; } = true;
	public static bool Cheats { get; set; }
	public static bool LegacyGrenadeDamage { get; set; }
	public static bool DebugDamage { get; set; }
	private static readonly JsonElement Weapons = JsonDocument.Parse(FileAccess.GetFileAsString(
		"res://assets/physics/css_damage/weapons.json")).RootElement.Clone();
	public static float WeaponValue(string weapon, string field) => Weapons.GetProperty(weapon).GetProperty(field).GetSingle();
	public static string WeaponAmmo(string weapon) => Weapons.GetProperty(weapon).GetProperty("ammo").GetString()!;
	public static string WeaponName(FirearmView gun) => gun.Kind.ToString().ToLowerInvariant();
	public static float ArmorRatio(FirearmView gun) => WeaponValue(WeaponName(gun), "armor_ratio");
	public static float Range(FirearmView gun) => WeaponValue(WeaponName(gun), "range") * SourceMovement.Unit;
	public static float RangeModifier(FirearmView gun) => gun.Kind == FirearmView.FirearmKind.M4A1 && gun.Silenced
		? .95f : WeaponValue(WeaponName(gun), "range_modifier");
	public static int BulletDamage(FirearmView gun) => gun.Kind == FirearmView.FirearmKind.USP && gun.Silenced
		? 30 : (int)WeaponValue(WeaponName(gun), "damage");
	public static float DistanceDamage(float damage, float distance, float rangeModifier) =>
		damage * Mathf.Pow(rangeModifier, distance / SourceMovement.Unit / 500);
	public static float HitMultiplier(SourceHitGroup group) => group switch
	{
		SourceHitGroup.Head => 4, SourceHitGroup.Stomach => 1.25f,
		SourceHitGroup.LeftLeg or SourceHitGroup.RightLeg => .75f, _ => 1
	};
	public static bool IsArmored(SourceHitGroup group, int armor, bool helmet) => armor > 0 && group switch
	{
		SourceHitGroup.Head => helmet, SourceHitGroup.LeftLeg or SourceHitGroup.RightLeg => false, _ => true
	};
	public static float KnifeDamage(bool stab, bool firstSwing, bool backstab) => stab ? backstab ? 195 : 65 : firstSwing ? 20 : 15;
	public static bool IsBackstab(Vector3 attacker, Vector3 victim, Vector3 victimForward)
	{
		var sight = new Vector2(victim.X - attacker.X, victim.Z - attacker.Z).Normalized();
		return sight.Dot(new(victimForward.X, victimForward.Z)) > .8f;
	}
}
