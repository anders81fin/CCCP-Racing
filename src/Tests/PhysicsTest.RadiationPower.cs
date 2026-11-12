using System;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>ATOMGRAD-4: the radiation power from the dose collected (EngineHealth.RadiationBoost, +15 % at the limit, linear) against the engine damage (RadiationLoss while in a hot spot, ContaminationLoss with the dose), and the track's own data (flag --radiation-power-tests).</summary>
    void RadiationPowerTests()
    {
        GD.Print("Radiation power (ATOMGRAD-4)");
        Check(EngineHealth.RadiationBoost(0) == 0f && Math.Abs(EngineHealth.RadiationBoost(1f) - 0.15f) < 1e-6f && Math.Abs(EngineHealth.RadiationBoost(1.2f) - 0.15f) < 1e-6f && Math.Abs(EngineHealth.RadiationBoost(0.5f) - 0.075f) < 1e-6f,
            "radiation power: none for a clean car, half the bonus at half the limit, +15 % at the limit and no more over it");
        float prev = 0, maxStep = 0; bool monotone = true;
        for (float d = 0; d <= 1.3f; d += 0.01f) { float b = EngineHealth.RadiationBoost(d); monotone &= b >= prev; maxStep = Math.Max(maxStep, b - prev); prev = b; }
        Check(monotone && maxStep < 0.0016f, $"radiation power grows continuously with the dose (largest step {maxStep * 100:F2} % per 1 % of the limit)");
        Check(EngineHealth.ContaminationLoss(0, 1) == 0f && EngineHealth.ContaminationLoss(1, 0) == 0f && Math.Abs(EngineHealth.ContaminationLoss(1f, 1f) - EngineHealth.ContaminationPerSec) < 1e-7f
              && Math.Abs(EngineHealth.ContaminationLoss(0.5f, 1f) / EngineHealth.ContaminationLoss(1f, 1f) - 0.25f) < 1e-4f,
            "contamination: a clean car loses nothing, a car at the limit 0.6 % of its engine a second, half the dose a quarter of that");
        bool rising = true; float last = -1;
        for (float d = 0; d <= 1.2f; d += 0.05f) { float l = EngineHealth.ContaminationLoss(d, 1f); rising &= l >= last; last = l; }
        Check(rising, "contamination loss never falls as the dose rises (the wash is the cure)");

        var def = TrackDef.Load("res://tracks/atomgrad.json");
        Check(!def.Fuel && def.Radiation != null, "ATOMGRAD-4 has no fuel (no consumption, depot, cans) and keeps its radiation");
        // A car that stays in the strongest hot spot until the dosimetrist's limit: what the bonus costs.
        float strongest = 0;
        foreach (var h in def.Radiation.HotSpots) strongest = Math.Max(strongest, def.Radiation.Background + h.Rate);
        float dose = 0, health = 1, t = 0; const float Dt = 0.05f;
        while (dose < def.Radiation.Limit && t < 300)
        {
            dose += strongest * Dt; t += Dt;
            health -= EngineHealth.RadiationLoss(strongest, Dt) + EngineHealth.ContaminationLoss(dose / def.Radiation.Limit, Dt);
        }
        Check(t > 15 && t < 40 && health > 0.6f && health < 0.9f,
            $"the strongest hot spot fills the limit in {t:F0} s and costs {(1 - health) * 100:F0} % of the engine for +{EngineHealth.RadiationBoost(dose / def.Radiation.Limit) * 100:F0} % power: the trade-off is paid in health");
        // Then driving on contaminated, no hot spot: the engine still dies unless washed.
        float seconds = 0, h2 = health;
        while (h2 > 0 && seconds < 600) { h2 -= EngineHealth.ContaminationLoss(1f, Dt); seconds += Dt; }
        Check(seconds > 60 && seconds < 300, $"a car left at the limit (no wash) loses the rest of its engine in {seconds:F0} s: the wash or the GULAG");
        // LAN: a client reads the boost from the PowerScale the host sends, and the host's HUD uses the exact value.
        bool roundTrip = true;
        foreach (float basePower in new[] { 0.5f, 1f, 1.4f, 2f })
            for (float share = 0; share <= 1.5f; share += 0.05f)
            {
                float boost = EngineHealth.RadiationBoost(share);
                float got = EngineHealth.BoostFromPower(basePower * (1f + boost), basePower);
                roundTrip &= Math.Abs(got - (boost < 0.002f ? 0f : boost)) < 0.0006f;
            }
        Check(roundTrip && EngineHealth.BoostFromPower(0f, 1f) == 0f && EngineHealth.BoostFromPower(1f, 0f) == 0f && EngineHealth.BoostFromPower(1.4f, 1f) == EngineHealth.RadiationBoostMax,
            "LAN client: the boost read back from PowerScale equals the host's (every fuel's power, every dose), none without power to compare, never above +15 %");
        // The LAN radiation HUD life cycle: a round with the damage vote ON, then a new round with it OFF (the reset at StartLanRace), then the rally.
        var lan = new LanRadiationState();
        lan.Set(0.5f, 0.075f, 0.3f);
        var (onShare, onBoost) = lan.View(true, 0f, false, 0f);
        lan.Reset();
        bool cleared = !lan.On && lan.Share == 0f && lan.Boost == 0f && lan.Rate == 0f;
        var (offShare, offBoost) = lan.View(true, 0f, false, 0f);
        lan.Set(0.9f, 0.135f, 0f);
        var (rallyShare, rallyBoost) = lan.View(false, 0.4f, true, 0.06f);
        Check(onShare == 0.5f && onBoost == 0.075f && cleared && offShare < 0f && offBoost == 0f && rallyShare == 0.4f && rallyBoost == 0.06f,
            "LAN radiation HUD: damage-on round shows the dose and the hint; the next round (damage off) shows no gauge and no hint; a LAN state never leaks into the rally");
        // The fuel switch of the LAN lobby follows the track data.
        bool fuelSame = true;
        foreach (var id in new[] { "ilyich", "siberia", "atomgrad", "bonus" })
            fuelSame &= TrackIds.HasFuel(id) == TrackDef.Load($"res://tracks/{id}.json").Fuel;
        Check(fuelSame && !TrackIds.HasFuel("atomgrad") && !TrackIds.HasFuel("ilyich") && TrackIds.HasFuel("siberia"), "TrackIds.HasFuel (LAN lobby) equals each track's own \"fuel\" flag: Ilyich and Atomgrad dry");
        // A fresh wash gives the bonus back to zero and stops the loss.
        Check(EngineHealth.RadiationBoost(0) == 0f && EngineHealth.ContaminationLoss(0, 1) == 0f, "washed clean: no bonus, no contamination loss");
    }
}
