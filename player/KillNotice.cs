using Godot;
using System;
using Contrapoint.Network;
using Contrapoint.UI;
using Contrapoint.Weapons;
using GDictionary = Godot.Collections.Dictionary;

namespace Contrapoint.Players;

[Flags]
public enum KillFlags { None = 0, Headshot = 1, Penetrated = 2, NoScope = 4, Blind = 8, ThroughSmoke = 16, InAir = 32 }

// A host-authored event, separate from persistent corpse state: late joins and
// duplicate health/pose messages must never generate another kill-feed entry.
public sealed record KillNotice(long KillerId, string KillerName, PlayerTeam KillerTeam,
	long VictimId, string VictimName, PlayerTeam VictimTeam, string Weapon, KillFlags Flags)
{
	public static long Identity(Player player) => player.NetworkPeerId != 0 ? player.NetworkPeerId : (long)player.GetInstanceId();
	public static string WeaponIcon(WeaponView? weapon) => weapon switch
	{
		FirearmView gun => gun.Kind switch
		{
			FirearmView.FirearmKind.M4A1 => gun.Silenced ? "m4a1_silencer" : "m4a1_silencer_off",
			FirearmView.FirearmKind.USP => gun.Silenced ? "usp_silencer" : "usp_silencer_off",
			FirearmView.FirearmKind.Scout => "ssg08", FirearmView.FirearmKind.Galil => "galilar",
			FirearmView.FirearmKind.Glock => "glock18", FirearmView.FirearmKind.M3 => "nova",
			FirearmView.FirearmKind.MP5Navy => "mp5sd", FirearmView.FirearmKind.P228 => "p250",
			FirearmView.FirearmKind.SG550 => "scar20", FirearmView.FirearmKind.SG552 => "sg553",
			FirearmView.FirearmKind.TMP => "mp9", _ => gun.Kind.ToString().ToLowerInvariant()
		},
		KnifeView => "knife", GrenadeView => "hegrenade", _ => "suicide"
	};
	public static KillFlags AttackFlags(Player player, WeaponView weapon, bool? scoped = null)
	{
		var flags = KillFlags.None;
		if (weapon is FirearmView { IsSniper: true } gun
			&& !(scoped ?? gun.ZoomLevel > 0)) flags |= KillFlags.NoScope;
		if (player.GrenadeVision.FlashRemaining > 0) flags |= KillFlags.Blind;
		var grounded = !player.LocalInput && !player.IsBot && player.NetworkPeerId != 0 ? player.NetworkGrounded : player.IsOnFloor();
		if (!grounded && !player.Movement.Noclip && player.Movement.WaterLevel == 0) flags |= KillFlags.InAir;
		return flags;
	}
	public static bool ThroughSmoke(Node source, Vector3 from, Vector3 to)
	{
		var segment = to - from; var length = segment.LengthSquared();
		if (length < .0001f) return false;
		foreach (GrenadeSmoke smoke in source.GetTree().GetNodesInGroup("source_smoke"))
		{
			if (smoke.Density < .2f || smoke.Age >= GrenadeSmoke.FadeEnd) continue;
			var radius = GrenadeSmoke.Radius * Mathf.Sin(Mathf.Clamp((float)smoke.Age / GrenadeSmoke.ExpandSeconds, 0, 1) * Mathf.Pi * .5f);
			var nearest = from + segment * Mathf.Clamp((smoke.GlobalPosition - from).Dot(segment) / length, 0, 1);
			if (nearest.DistanceSquaredTo(smoke.GlobalPosition) < radius * radius) return true;
		}
		return false;
	}
	public static void Publish(Player victim, Player? killer, PlayerDamageKind kind, SourceHitGroup group, string? weapon, KillFlags flags)
	{
		if (LanSession.Instance?.IsClient == true) return;
		if (kind == PlayerDamageKind.Bullet && group == SourceHitGroup.Head) flags |= KillFlags.Headshot;
		var icon = kind == PlayerDamageKind.HE ? "hegrenade" : kind == PlayerDamageKind.Knife ? "knife"
			: kind == PlayerDamageKind.Bullet ? weapon ?? WeaponIcon(killer?.ActiveWeapon) : "suicide";
		var other = GodotObject.IsInstanceValid(killer) && killer != victim ? killer : null;
		var notice = new KillNotice(other != null ? Identity(other) : 0, other?.DisplayName ?? "", other?.Team ?? PlayerTeam.Unassigned,
			Identity(victim), victim.DisplayName, victim.Team, icon, flags);
		Deliver(victim, notice);
		LanSession.Instance?.BroadcastKillNotice(notice);
	}
	public static void Deliver(Node source, KillNotice notice)
	{
		foreach (Player player in source.GetTree().GetNodesInGroup("source_players"))
			if (player.LocalInput) player.KillFeed?.Add(notice);
	}
	public GDictionary Pack() => new() { ["killer"] = KillerId, ["killer_name"] = KillerName, ["killer_team"] = (int)KillerTeam,
		["victim"] = VictimId, ["victim_name"] = VictimName, ["victim_team"] = (int)VictimTeam, ["weapon"] = Weapon, ["flags"] = (int)Flags };
	public static KillNotice Unpack(GDictionary data) => new(data["killer"].AsInt64(), data["killer_name"].AsString(), (PlayerTeam)data["killer_team"].AsInt32(),
		data["victim"].AsInt64(), data["victim_name"].AsString(), (PlayerTeam)data["victim_team"].AsInt32(), data["weapon"].AsString(), (KillFlags)data["flags"].AsInt32());
}
