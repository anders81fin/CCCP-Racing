using System;

namespace CCCPRacing.Car;

/// <summary>
/// The rally Pyatyorka's tank (CarParams.TankLitres). The engine drinks a little at idle and more
/// the harder the throttle and the higher the revs (FlowFor); Rate scales it all (the --fuel-rate
/// test flag). Near empty the engine sputters, at empty it dies. Disabled (Stakhanov laps, LAN with consumption off,
/// the title) it neither drinks nor runs dry.
/// </summary>
public class FuelTank
{
    /// <summary>Below this fraction the gauge blinks and the driver hears about it.</summary>
    public const float LowAt = 0.2f;
    /// <summary>Below this fraction the engine sputters (throttle cuts, misfires).</summary>
    public const float SputterAt = 0.03f;
    /// <summary>
    /// Keyboard throttle drinks this share: a key is all or nothing, a pad's trigger can hold the
    /// revs under the knee, so the keys are compensated (the physics and the lap times are not touched).
    /// </summary>
    public const float KeysRate = 0.8f;
    /// <summary>OCTANE BOOSTER (the golden can's 350 %): the engine drinks this many times as much.</summary>
    public const float BoostRate = 2f;

    readonly CarParams _p;
    public bool Enabled;
    public float Litres;
    /// <summary>Consumption multiplier (1: as tuned).</summary>
    public float Rate = 1f;

    public FuelTank(CarParams p)
    {
        _p = p;
        Litres = p.TankLitres;
    }

    public float Capacity => _p.TankLitres;
    public float Fraction => Litres / Capacity;
    public bool Empty => Enabled && Litres <= 0f;
    public bool Low => Enabled && Fraction < LowAt;
    public bool Sputtering => Enabled && Litres > 0f && Fraction < SputterAt;

    /// <summary>
    /// Litres per second at this throttle (0..1) and rpm: the idle drain plus the throttle's share,
    /// small up to the knee (4500 rpm) and then growing with the square of the way to the redline:
    /// a screaming engine drinks most, short shifts and a light foot save a lot.
    /// </summary>
    public static float FlowFor(CarParams p, float throttle, float rpm)
    {
        float revs = Math.Clamp((rpm - p.FuelRevKnee) / (p.Redline - p.FuelRevKnee), 0f, 1.05f);
        return p.FuelIdle + p.FuelFull * Math.Clamp(throttle, 0f, 1f) * (p.FuelRevBase + (1f - p.FuelRevBase) * revs * revs);
    }

    /// <summary>One step of running: the litres it took (0 when disabled or dry); scale: KeysRate, BoostRate.</summary>
    public float Burn(float dt, float throttle, float rpm, float scale = 1f)
    {
        if (!Enabled || Litres <= 0f) return 0f;
        float used = Math.Min(Litres, FlowFor(_p, throttle, rpm) * Rate * scale * dt);
        Litres -= used;
        return used;
    }

    /// <summary>Pours litres in (never past full): the litres that went in.</summary>
    public float Fill(float litres)
    {
        float room = Math.Max(0f, Capacity - Litres), added = Math.Min(room, Math.Max(0f, litres));
        Litres += added;
        return added;
    }

    /// <summary>A full tank at this fraction (1: brim full).</summary>
    public void Refill(float fraction = 1f) => Litres = Capacity * Math.Clamp(fraction, 0f, 1f);
}
