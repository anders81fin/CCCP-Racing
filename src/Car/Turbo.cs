using System;

namespace CCCPRacing.Car;

/// <summary>
/// The rocket fuel's turbo: the boost (0..1) follows the throttle and the revs, rising in about half a second and dropping in about a fifth of one. CarSim adds
/// TurboGain times it to the engine's power; EngineSound makes the whistle of it (it keeps its own copy of the same curve, per sample, so that a replay whistles too).
/// </summary>
public static class Turbo
{
    public const float SpoolUp = 0.5f, SpoolDown = 0.18f, FromRpm = 2500f, SpanRpm = 3500f;

    /// <summary>The boost the throttle and the revs ask for: none at idle revs, all at the top with the throttle down.</summary>
    public static float Target(float throttle, float rpm) => Math.Clamp(throttle, 0f, 1f) * Math.Clamp((rpm - FromRpm) / SpanRpm, 0f, 1f);

    /// <summary>One step of dt seconds towards the target.</summary>
    public static float Step(float boost, float target, float dt, float spoolUp = SpoolUp)
    {
        float time = target > boost ? spoolUp : SpoolDown;
        return boost + (target - boost) * (1f - MathF.Exp(-dt / time));
    }

    /// <summary>The share of TurboGain the engine gets at this boost: the boost itself, or with kick 1 a smoothstep of it (little through the lag, then the surge).</summary>
    public static float Power(float boost, float kick)
    {
        float b = Math.Clamp(boost, 0f, 1f);
        return kick <= 0f ? boost : boost + (b * b * (3f - 2f * b) - b) * Math.Clamp(kick, 0f, 1f);
    }
}
