using System;
using System.Globalization;

namespace CCCPRacing.Lab;

public enum LabStep { Fine, Normal, Coarse }

/// <summary>One slider of the LAB panel: a number with a default, a range and a way to read and write it.</summary>
public sealed class LabParam
{
    public string Group { get; init; }
    public string Name { get; init; }
    /// <summary>What the panel prints (Inertia has three: INERTIA X, INERTIA Y, INERTIA Z).</summary>
    public string Label { get; init; }
    public string Doc { get; init; } = "";
    public float Default { get; init; }
    public float Min { get; init; }
    public float Max { get; init; }
    public Func<float> Get { get; init; }
    public Action<float> Set { get; init; }
    /// <summary>Called after the value changed (the wheels' places are worked out once, in the car's constructor).</summary>
    public Action Changed { get; init; }

    public float Value => Get();
    public bool IsChanged => MathF.Abs(Value - Default) > 1e-6f * MathF.Max(1f, MathF.Abs(Default));
    public float Fraction => Max > Min ? Math.Clamp((Value - Min) / (Max - Min), 0f, 1f) : 0f;
    public float StepSize(LabStep step) => (Max - Min) / (step switch { LabStep.Fine => 1000f, LabStep.Coarse => 20f, _ => 100f });

    public void Assign(float v)
    {
        if (!float.IsFinite(v)) return;
        Set(Math.Clamp(v, Min, Max));
        Changed?.Invoke();
    }

    /// <summary>One step up (+1) or down (-1); the result is rounded so that 0.42 + 0.0126 reads 0.43, not 0.4326000...</summary>
    public void Nudge(int direction, LabStep step)
    {
        float v = Value + direction * StepSize(step);
        Assign(Round(v, StepSize(step)));
    }

    public void Reset() => Assign(Default);

    static float Round(float v, float step)
    {
        if (step <= 0) return v;
        float unit = MathF.Pow(10, MathF.Floor(MathF.Log10(step)));
        return MathF.Round(v / unit) * unit;
    }

    public static string Format(float v)
    {
        float a = MathF.Abs(v);
        string f = a >= 1000 ? "0" : a >= 100 ? "0.#" : a >= 10 ? "0.##" : a >= 1 ? "0.###" : "0.####";
        return v.ToString(f, CultureInfo.InvariantCulture);
    }

    public string Text => Format(Value);
    public string DefaultText => Format(Default);
}
