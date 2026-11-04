using System;
using Godot;

namespace CCCPRacing.Car;

/// <summary>
/// The Pyatyorka's engine: 1 = fresh from the factory, 0 = seized. Crashes and explosions eat into
/// it, faster than linearly with how hard the hit was; power drops with it, but much less than in
/// proportion. At <see cref="SmokeAt"/> it starts smoking, at 0 it dies. A direct hit (shell,
/// Balalaika, missile) destroys the car outright. Plain logic, no nodes: RaceScene drives the effects.
/// </summary>
public class EngineHealth
{
    /// <summary>Collisions below this speed (m/s) are scrapes and cost nothing.</summary>
    public const float ImpactFloor = 4f;
    public const float SmokeAt = 0.25f;
    /// <summary>Tank shell within this distance (m) of the car: a direct hit.</summary>
    public const float ShellLethal = 2.5f;
    /// <summary>Shell blasts further than this do no engine damage.</summary>
    public const float ShellReach = 12f;
    /// <summary>A Balalaika crashing within this distance (m) lands on the car.</summary>
    public const float JetLethal = 6f;
    public const float JetReach = 30f;
    /// <summary>A missile bursting within this distance (m) of the car: a direct hit.</summary>
    public const float MissileLethal = 1.5f;
    /// <summary>Missile bursts inside this (m) are heavy hits, further out shrapnel.</summary>
    public const float MissileHeavy = 4f;
    /// <summary>Missile bursts further than this (m) do no harm; also the missile's proximity fuse radius.</summary>
    public const float MissileReach = 7f;
    /// <summary>Health lost at the inner and outer edge of the heavy band, then of the shrapnel band.</summary>
    const float HeavyNear = 0.7f, HeavyFar = 0.4f, ShrapnelNear = 0.3f, ShrapnelFar = 0.1f;
    /// <summary>One crash usually registers over several ticks; the charge covers the peak once.</summary>
    const float HitWindow = 0.35f;

    public enum Change { None, Damaged, Smoking, Died, Destroyed }

    /// <summary>False in training laps and the title demo: nothing is charged.</summary>
    public bool Enabled;
    public float Health { get; private set; } = 1f;
    public bool Dead => Health <= 0f;
    /// <summary>Blown up by a direct hit (implies Dead).</summary>
    public bool Destroyed { get; private set; }
    public string LastCause { get; private set; }
    public bool Smoking => !Dead && Health <= SmokeAt;
    /// <summary>Drive force multiplier: 1 when healthy, still half power just before it dies, 0 dead.</summary>
    public float PowerFactor => Dead ? 0f : PowerFor(Health);

    float _peak, _peakAge = 99f;

    public static float PowerFor(float health) => 0.5f + 0.5f * MathF.Sqrt(Math.Clamp(health, 0f, 1f));

    /// <summary>
    /// Health lost by one collision at this closing speed (m/s): nothing below 4, a 12 m/s wall
    /// about a tenth, 20 m/s under a quarter, 30 m/s two fifths.
    /// </summary>
    public static float ImpactDamage(float speed)
    {
        if (speed <= ImpactFloor) return 0f;
        return 0.0088f * MathF.Pow(speed - ImpactFloor, 1.17f);
    }

    /// <summary>Health lost by a shell landing this far away (m); lethal inside ShellLethal.</summary>
    public static float ShellDamage(float dist)
    {
        if (dist <= ShellLethal) return 1f;
        if (dist >= ShellReach) return 0f;
        float k = 1f - (dist - ShellLethal) / (ShellReach - ShellLethal);
        return 0.175f * k * k;
    }

    /// <summary>MI-24 cannon: a body contact is fatal; nearby impacts cause cumulative fragment damage.</summary>
    public static float CannonDamage(float bodyDistance)
    {
        if(bodyDistance<=.15f) return 1f;
        if(bodyDistance>=4f) return 0f;
        float k=1-bodyDistance/4f;
        return .08f*k*k;
    }

    /// <summary>Health lost by a missile bursting this far away (m): a direct hit inside MissileLethal,
    /// 70-40 % out to MissileHeavy, 30-10 % shrapnel out to MissileReach (linear in each band).</summary>
    public static float MissileDamage(float dist)
    {
        if (dist < MissileLethal) return 1f;
        if (dist < MissileHeavy) return Mathf.Lerp(HeavyNear, HeavyFar, (dist - MissileLethal) / (MissileHeavy - MissileLethal));
        if (dist < MissileReach) return Mathf.Lerp(ShrapnelNear, ShrapnelFar, (dist - MissileHeavy) / (MissileReach - MissileHeavy));
        return 0f;
    }

    /// <summary>Health lost by a Balalaika crashing this far away (m); lethal inside JetLethal.</summary>
    public static float JetDamage(float dist)
    {
        if (dist <= JetLethal) return 1f;
        if (dist >= JetReach) return 0f;
        float k = 1f - (dist - JetLethal) / (JetReach - JetLethal);
        return 0.25f * k * k;
    }

    public void Reset(bool enabled)
    {
        Enabled = enabled;
        Health = 1f;
        Destroyed = false;
        LastCause = null;
        _peak = 0;
        _peakAge = 99f;
    }

    /// <summary>
    /// Per physics tick: the strongest collision this tick (m/s). A crash that keeps registering
    /// for a few ticks is charged once for its peak.
    /// </summary>
    public Change Impact(float speed, float dt, string cause = "crash")
    {
        _peakAge += dt;
        if (_peakAge > HitWindow) _peak = 0;
        if (speed <= ImpactFloor || speed <= _peak) return Change.None;
        float loss = ImpactDamage(speed) - ImpactDamage(_peak);
        _peak = speed;
        _peakAge = 0;
        return Damage(loss, cause, speed);
    }

    /// <summary>Takes this much health off (0..1).</summary>
    public Change Damage(float loss, string cause, float severity = 0)
    {
        if (!Enabled || Dead || loss <= 0) return Change.None;
        bool wasSmoking = Health <= SmokeAt;
        Health = Math.Max(0f, Health - loss);
        LastCause = cause;
        Log(cause, severity);
        if (Dead) return Change.Died;
        return !wasSmoking && Health <= SmokeAt ? Change.Smoking : Change.Damaged;
    }

    /// <summary>The mechanic's work: as new again. Does nothing for a seized or wrecked engine.</summary>
    public void Repair()
    {
        if (Dead) return;
        Health = 1f;
        _peak = 0;
        _peakAge = 99f;
        if (OS.HasEnvironment("ENGINEDBG")) GD.Print("engine: repaired by the mechanic -> health 100%");
    }

    /// <summary>The mechanic's work in progress: health climbs to this (never down). Does nothing for a seized or wrecked engine.</summary>
    public void RepairTo(float health)
    {
        if (Dead || health <= Health) return;
        Health = Math.Min(1f, health);
    }

    /// <summary>A direct hit: the car is wrecked whatever the engine's health.</summary>
    public Change Destroy(string cause)
    {
        if (!Enabled || Destroyed) return Change.None;
        Health = 0f;
        Destroyed = true;
        LastCause = cause;
        Log(cause, 99f);
        return Change.Destroyed;
    }

    /// <summary>Test (--health=N): start the run with this much health.</summary>
    public void NetworkStatus(float health,bool destroyed) {Health=Math.Clamp(health,0,1);Destroyed=destroyed && Health<=0;}
    public void Set(float health) => Health = Math.Clamp(health, 0f, 1f);

    void Log(string cause, float severity)
    {
        if (OS.HasEnvironment("ENGINEDBG"))
            GD.Print($"engine: {cause} severity {severity:F2} -> health {Health * 100:F1}% power {PowerFactor:F3}{(Destroyed ? " DESTROYED" : Dead ? " DEAD" : "")}");
    }
}
