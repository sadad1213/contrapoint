namespace Contrapoint.Weapons;

public readonly record struct AttackCooldowns(double Primary, double Secondary);

// Gameplay time only. No dependency on Godot, animation names, clip length,
// playback speed or AnimationFinished. A future weapon can own one instance.
public sealed class WeaponActionTimers
{
    private const double Epsilon = 0.000001;

    public double Time { get; private set; }
    public double ReadyAt { get; private set; }
    public double NextPrimaryAttackAt { get; private set; }
    public double NextSecondaryAttackAt { get; private set; }
    public double NextDeployAt { get; private set; }
    public double NextInspectAt { get; private set; }

    public void Advance(double delta)
    {
        if (!double.IsFinite(delta) || delta < 0)
            throw new System.ArgumentOutOfRangeException(nameof(delta));
        Time += delta;
    }

    public double AttackRemaining(bool secondary) => System.Math.Max(0,
        System.Math.Max(ReadyAt, secondary ? NextSecondaryAttackAt : NextPrimaryAttackAt) - Time);

    public bool TryAttack(bool secondary, AttackCooldowns cooldowns)
    {
        ValidateDelay(cooldowns.Primary);
        ValidateDelay(cooldowns.Secondary);
        if (AttackRemaining(secondary) > Epsilon)
            return false;

        NextPrimaryAttackAt = Time + cooldowns.Primary;
        NextSecondaryAttackAt = Time + cooldowns.Secondary;
        var cosmeticReady = Time + System.Math.Min(cooldowns.Primary, cooldowns.Secondary);
        NextInspectAt = System.Math.Max(NextInspectAt, cosmeticReady);
        NextDeployAt = System.Math.Max(NextDeployAt, cosmeticReady);
        return true;
    }

    public bool TryDeploy(double readyDelay)
    {
        ValidateDelay(readyDelay);
        if (Time + Epsilon < System.Math.Max(ReadyAt, NextDeployAt))
            return false;

        ReadyAt = Time + readyDelay;
        NextDeployAt = ReadyAt;
        NextInspectAt = System.Math.Max(NextInspectAt, ReadyAt);
        // Keep existing shot cooldowns; re-equipping cannot bypass them.
        return true;
    }

    // A knife traces on the next safe physics tick, then chooses hit/miss delays.
    public void ResolveContact(double attackTime, AttackCooldowns cooldowns)
    {
        ValidateDelay(cooldowns.Primary);
        ValidateDelay(cooldowns.Secondary);
        NextPrimaryAttackAt = attackTime + cooldowns.Primary;
        NextSecondaryAttackAt = attackTime + cooldowns.Secondary;
        NextInspectAt = NextDeployAt = attackTime + System.Math.Min(cooldowns.Primary, cooldowns.Secondary);
    }

    public bool TryInspect(double repeatDelay)
    {
        ValidateDelay(repeatDelay);
        if (Time + Epsilon < System.Math.Max(ReadyAt, NextInspectAt))
            return false;
        NextInspectAt = Time + repeatDelay;
        // Inspect is cosmetic. It must never delay or unlock either attack.
        return true;
    }

    private static void ValidateDelay(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
            throw new System.ArgumentOutOfRangeException(nameof(seconds));
    }
}
