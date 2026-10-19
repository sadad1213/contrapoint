using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using Contrapoint.Weapons;
using Kind = Contrapoint.Weapons.FirearmView.FirearmKind;

namespace Contrapoint.Players;

// Installed CSSO scripts, including explicit CSS -> CSSO replacement mappings.
// Behaviour: CSSO weapon_csbase.cpp / cs_weapon_parse.cpp / fx_cs_shared.cpp.
internal static class CssoRifleBallistics
{
	internal readonly record struct Profile(float Spread, float Stand, float Crouch,
		float Move, float Jump, float JumpInitial, float Land, float Fire,
		float RecoveryStand, float RecoveryCrouch, float RecoveryStandFinal, float RecoveryCrouchFinal)
	{
		public float Speed { get; init; }
		public float Cycle { get; init; }
		public float Ladder { get; init; }
		public float Reload { get; init; }
		public float TransitionStart { get; init; }
		public float TransitionEnd { get; init; }
	}
	private static readonly Dictionary<(Kind, bool), Profile> Profiles = new();
	private static readonly Dictionary<(Kind, bool), Vector2[]> Patterns = new();
	static CssoRifleBallistics()
	{
		using var document = JsonDocument.Parse(FileAccess.GetFileAsString("res://assets/physics/csso_accuracy.json"));
		foreach (var entry in document.RootElement.EnumerateObject())
		{
			var kind = Enum.Parse<Kind>(entry.Name, true);
			var values = entry.Value.GetProperty("values");
			float Value(string key, float fallback = 0) => values.TryGetProperty(key, out var v) ? v.GetSingle() : fallback;
			foreach (var alternate in new[] { false, true })
			{
				float Mode(string key, float fallback = 0) => alternate ? Value(key + "alt", Value(key, fallback)) : Value(key, fallback);
				var standRecovery = Value("recoverytimestand", 1);
				var crouchRecovery = Value("recoverytimecrouch", 1);
				Profiles[(kind, alternate)] = new(Mode("spread"), Mode("inaccuracystand"), Mode("inaccuracycrouch"),
					Mode("inaccuracymove"), Mode("inaccuracyjump"), Value("inaccuracyjumpinitial"), Mode("inaccuracyland"), Mode("inaccuracyfire"),
					standRecovery, crouchRecovery, Value("recoverytimestandfinal", standRecovery), Value("recoverytimecrouchfinal", crouchRecovery))
				{
					Speed = Mode("maxplayerspeed", 250), Cycle = Mode("cycletime", .1f),
					Ladder = Mode("inaccuracyladder"), Reload = Value("inaccuracyreload"),
					TransitionStart = Value("recoverytransitionstartbullet", 2), TransitionEnd = Value("recoverytransitionendbullet", 5)
				};
				Patterns[(kind, alternate)] = Pattern((int)Value("recoilseed"), Mode("recoilangle"),
					Mode("recoilanglevariance"), Mode("recoilmagnitude"), Mode("recoilmagnitudevariance"),
					Value("fullauto", FirearmData.For(kind).Automatic ? 1 : 0) > 0);
			}
		}
	}
	public static bool Supports(Kind kind) => Profiles.ContainsKey((kind, false));
	public static Profile Data(Kind kind, bool alternate) => Profiles[(kind, alternate)];
	public static Profile Data(FirearmView gun) => Data(gun.Kind, gun.AccuracyAlternate);
	private static Vector2[] Pattern(int seed, float baseAngle, float angleVariance, float magnitude, float magnitudeVariance, bool automatic)
	{
		var random = new SourceRecoilRandom.FloatStream(seed);
		var result = new Vector2[64];
		float angle = 0, strength = 0;
		for (var i = 0; i < result.Length; i++)
		{
			var nextAngle = baseAngle + random.Range(-angleVariance, angleVariance);
			var nextStrength = magnitude + random.Range(-magnitudeVariance, magnitudeVariance);
			angle = i == 0 || !automatic ? nextAngle : Mathf.Lerp(angle, nextAngle, .55f);
			strength = i == 0 || !automatic ? nextStrength : Mathf.Lerp(strength, nextStrength, .55f);
			if (automatic && i < 4) strength *= Mathf.Lerp(.75f, 1, i / 4f);
			var radians = Mathf.DegToRad(angle);
			result[i] = new(-Mathf.Cos(radians) * strength, -Mathf.Sin(radians) * strength);
		}
		return result;
	}
	public static Vector2 Impulse(Kind kind, bool alternate, float index)
		=> Patterns[(kind, alternate)][Math.Max(0, (int)index) % 64];
	public static float Baseline(Profile data, Player player) => player.Movement.IsOnLadder
		? data.Ladder : !player.IsOnFloor() ? data.Stand + data.Jump : player.Movement.IsDucked ? data.Crouch : data.Stand;
	public static float Inaccuracy(FirearmView gun, Player player)
	{
		var data = Data(gun);
		var amount = Mathf.Max(gun.RifleAccuracy.Penalty, Baseline(data, player));
		var movement = Mathf.Clamp((player.Movement.HorizontalSpeed / Mathf.Max(1, data.Speed) - .34f) / .61f, 0, 1);
		if (!player.MovementIntent.Walk) movement = Mathf.Pow(movement, .25f);
		amount += movement * data.Move;
		if (!player.IsOnFloor())
		{
			var speed = Mathf.Sqrt(Mathf.Abs(player.Velocity.Y) / SourceMovement.Unit);
			var jump = Mathf.Sqrt(player.Movement.JumpImpulse);
			amount += Mathf.Clamp((speed - jump * .25f) / (jump * .75f), 0, 2) * data.JumpInitial;
		}
		return Mathf.Min(amount, 1);
	}
}

internal sealed class CssoRifleAccuracy
{
	public float Penalty { get; private set; }
	public float Index { get; private set; }
	private double _sinceShot;
	private bool _wasAirborne;
	private float _fallSpeed;
	public void Reset()
	{
		Penalty = Index = _fallSpeed = 0;
		_sinceShot = 0;
		_wasAirborne = false;
	}
	public void Fired(FirearmView gun)
	{
		var data = CssoRifleBallistics.Data(gun);
		if (gun.Wielder is { } player) Penalty = Mathf.Max(Penalty, CssoRifleBallistics.Baseline(data, player));
		Penalty += data.Fire;
		Index++;
		_sinceShot = 0;
	}
	public void Advance(FirearmView gun, Player player, double delta)
	{
		if (delta <= 0) return;
		var data = CssoRifleBallistics.Data(gun);
		var baseline = CssoRifleBallistics.Baseline(data, player);
		if (player.Movement.IsOnLadder) baseline += CssoRifleBallistics.Data(gun.Kind, false).Ladder;
		if (gun.Busy) baseline += data.Reload;
		if (_wasAirborne && player.IsOnFloor()) Penalty += _fallSpeed * data.Land;
		_wasAirborne = !player.IsOnFloor();
		_fallSpeed = Mathf.Max(0, -player.Velocity.Y / SourceMovement.Unit);
		var duck = player.Movement.IsDucked;
		var transition = Mathf.Clamp(((int)Index - data.TransitionStart) / Mathf.Max(1, data.TransitionEnd - data.TransitionStart), 0, 1);
		var recovery = player.Movement.IsOnLadder ? data.RecoveryStand : !player.IsOnFloor() ? data.RecoveryCrouch * 4
			: Mathf.Lerp(duck ? data.RecoveryCrouch : data.RecoveryStand,
				duck ? data.RecoveryCrouchFinal : data.RecoveryStandFinal, transition);
		Penalty = baseline + Mathf.Max(0, Penalty - baseline) * Mathf.Exp(-Mathf.Log(10) * (float)delta / Mathf.Max(.001f, recovery));
		var threshold = data.Cycle * 1.1;
		var decayTime = Math.Max(0, _sinceShot + delta - Math.Max(threshold, _sinceShot));
		Index *= Mathf.Exp(-Mathf.Log(10) * 2 * (float)decayTime);
		if (Index < .00001f) Index = 0;
		_sinceShot += delta;
	}
}
