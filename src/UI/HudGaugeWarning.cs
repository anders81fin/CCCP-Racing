using System;
using Godot;

namespace CCCPRacing.UI;

public partial class Hud
{
    float _fuelWarningPhase = MathF.PI / 2, _engineWarningPhase = MathF.PI / 2;

    void TickGaugeWarnings(float dt)
    {
        if (Frozen) return;
        Tick(ref _fuelWarningPhase, Fuel);
        Tick(ref _engineWarningPhase, EngineHealth);
        void Tick(ref float phase, float fraction)
        {
            if (fraction < 0 || fraction > 0.5f) { phase = MathF.PI / 2; return; }
            // Integrate frequency: consuming fuel or taking damage speeds the pulse up
            // without jumping its phase. From one pulse / 1.5 s to almost three / s.
            float urgency = 1 - Math.Clamp(fraction / 0.5f, 0, 1);
            float hz = Mathf.Lerp(0.65f, 2.8f, urgency);
            phase = Mathf.PosMod(phase + MathF.Tau * hz * dt, MathF.Tau);
        }
    }
}

public partial class Hud
{
    /// <summary>The car instruments are hidden (VISUALS): what must still reach the driver stays, as plain text lines above where the cluster was, right-aligned: engine, fuel and dose danger, refuelling and washing.</summary>
    void DrawHiddenGaugeWarnings(Rect2 r)
    {
        bool blink = (int)(_clock * 3) % 2 == 0;
        float y = r.Position.Y - 18;      // (the CLUTCH label owns the line right above the cluster)
        void Line(string t, Color c)
        {
            PixelFont.Draw(this, t, new Vector2(r.End.X - PixelFont.Measure(t) - 1, y), c);
            y -= 9;
        }
        if (EngineHealth >= 0 && EngineHealth <= 0.25f) Line(EngineHealth <= 0f ? "ENGINE DEAD" : $"ENGINE {MathF.Ceiling(EngineHealth * 100):0}%", blink ? Red : Gold);
        if (Fuel >= 0 && Fuel < 0.2f) Line(Fuel <= 0f ? "FUEL EMPTY" : $"FUEL {MathF.Ceiling(FuelLitres):0}L", blink ? Red : Gold);
        if (Dose >= 1f) Line($"DOSE {DoseMsv:0}MSV", blink ? Red : Gold);
        if (Refuelling) Line("REFUELLING", blink ? Gold : Paper);
        else if (FuelBoost > 0) Line($"OCTANE BOOSTER  {MathF.Ceiling(FuelBoost):0}", Gold);
        if (Washing) Line("WASHING", blink ? Gold : Paper);
    }
}
