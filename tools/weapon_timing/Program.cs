using System;
using Contrapoint.Weapons;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var weapon = new WeaponActionTimers();
Check(weapon.TryDeploy(0.75), "Initial deploy failed");
Check(!weapon.TryAttack(false, new(0.7, 0.7)), "Shot before deploy readiness");
weapon.Advance(0.75);
Check(weapon.TryInspect(0.25), "Inspect rejected when ready");
Check(weapon.TryAttack(false, new(0.4, 0.9)), "Inspect blocked primary fire");
Check(!weapon.TryInspect(0.25), "Inspect bypassed attack cooldown");
weapon.Advance(0.4);
Check(weapon.AttackRemaining(false) < 0.000001, "Primary cooldown did not expire");
Check(weapon.AttackRemaining(true) > 0.49, "Secondary cooldown not independent");
var primary = weapon.NextPrimaryAttackAt;
var secondary = weapon.NextSecondaryAttackAt;
Check(weapon.TryInspect(0.25), "Inspect rejected after cosmetic cooldown");
Check(weapon.NextPrimaryAttackAt == primary && weapon.NextSecondaryAttackAt == secondary,
    "Inspect changed attack deadlines");
Check(!weapon.TryAttack(true, new(0.9, 0.9)), "Inspect cleared secondary cooldown");
Check(weapon.TryDeploy(0.1), "Ready weapon could not redeploy");
weapon.Advance(0.1);
Check(!weapon.TryAttack(true, new(0.9, 0.9)), "Redeploy cleared existing cooldown");
weapon.Advance(0.4);
Check(weapon.TryAttack(true, new(0.9, 0.9)), "Secondary did not unlock on its deadline");

foreach (var fps in new[] { 30, 60, 144 })
{
    var timed = new WeaponActionTimers();
    Check(timed.TryAttack(false, new(0.7, 0.92)), "Initial attack rejected");
    for (var frame = 0; frame < fps; frame++) timed.Advance(1.0 / fps);
    Check(timed.AttackRemaining(false) == 0 && timed.AttackRemaining(true) == 0,
        "Cooldown depends on frame rate");
}

Console.WriteLine("WEAPON_TIMERS_OK: deploy, independent attack deadlines, inspect cancellation, redeploy persistence, 30/60/144 FPS; no engine or animation dependency");
