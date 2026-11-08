using System;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// Fingerprint of a generated track's shape: the road centre line (position and height,
/// quantized to 5 cm) hashed with 64-bit FNV-1a. Decorative props do not count. Opt-in
/// physical off-road meadow changes also invalidate comparisons. The bonus layout includes
/// road width and the hall, whose covered concrete and oil change grip.
/// </summary>
public static class TrackSignature
{
    const float Quantum = 0.05f;

    /// <summary>The v0.2 bonus map (seed 1917, hillAmp 11.52, no hill shapes) that saved records without a signature.</summary>
    public const string LegacyBonus = "42ef2c90d964bb36";

    /// <summary>What <see cref="Compute"/> writes: sixteen lower-case hex digits.</summary>
    public static bool LooksValid(string s)
    {
        if (s == null || s.Length != 16) return false;
        foreach (char c in s) if (!(c is >= '0' and <= '9' or >= 'a' and <= 'f')) return false;
        return true;
    }

    public static string Compute(TrackData t)
    {
        ulong h = 14695981039346656037UL;
        void Mix(int v)
        {
            for (int b = 0; b < 4; b++)
            {
                h ^= (byte)(v >> (8 * b));
                h *= 1099511628211UL;
            }
        }
        int Q(float v) => (int)MathF.Round(v / Quantum);

        Mix(t.Center.Length);
        for (int i = 0; i < t.Center.Length; i++)
        {
            Mix(Q(t.Center[i].X));
            Mix(Q(t.Center[i].Y));
            Mix(Q(t.CenterH[i]));
        }
        // Physical off-road meadow changes shortcut/escape conditions even when the racing road stays fixed.
        if(t.Meadow is {} meadow)
        {Mix(0x4d454144);Mix(2);Mix(meadow.Seed);Mix(Q(meadow.BaseHeight));Mix(Q(meadow.Amplitude));Mix(Q(meadow.Scale));}
        if(t.RoadLeftInset != null)
        {
            Mix(0x524f4144); Mix(1);
            for(int i=0;i<t.Center.Length;i++)
            { Mix(Q(t.HalfWidth[i])); Mix(Q(t.RoadLeftInset[i])); Mix(Q(t.RoadRightInset[i])); }
        }
        if(t.RoadLandforms.Count>0)
        {
            Mix(0x52494447);Mix(1);
            foreach(var form in t.RoadLandforms)
            {Mix(Q(form.Width));foreach(float value in form.Lift)Mix(Q(value));}
        }
        // Poligon's new passing places change competitive lines without moving the centreline.
        if(t.Id=="bonus") foreach(float width in t.HalfWidth) Mix(Q(width));
        if(t.MaintenanceHall is {} hall) {Mix(0x48414c4c);Mix(Q(hall.Center.X));Mix(Q(hall.Center.Y));Mix(Q(hall.HalfWidth));Mix(Q(hall.HalfLength));}
        // The cars' generation: 8 November 2026 (0.86) the engines, masses and gearboxes of every fuel changed a lot, so laps made on the earlier cars no longer compare.
        // Bump the second number when the cars change that much again.
        Mix(0x43415253); Mix(2);
        return h.ToString("x16");
    }
}
