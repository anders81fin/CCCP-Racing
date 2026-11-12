using System;
using System.Collections.Generic;
using CCCPRacing.Car;
using CCCPRacing.Track;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>Atomgrad's school demonstrations use its real hotspot, wash bay and mechanic (the track has no fuel).</summary>
public partial class RaceScene
{
    Vector2 AtomgradHotSpot(int i)
    {
        var h = _rad.HotSpots[Math.Clamp(i, 0, _rad.HotSpots.Count - 1)];
        return new Vector2(h.X, h.Z);
    }

    List<Beat> AtomgradRadiation(string id)
    {
        Vector2 hot = AtomgradHotSpot(id == "atom_rad_dose" ? 1 : 0);
        int at = _track.NearestCenter(hot, out _);
        return id switch
        {
            "atom_rad_sources" => new()
            {
                B("THE RED FOREST: THE DOSIMETER CLICKS FASTER.", () =>
                {
                    ScFresh();
                    _doseSchoolMock = true;
                    ScRoll(at, 0, 18f);
                    _scLook = () => Ground(hot, 1.5f);
                }, (t, _) =>
                {
                    ScCruise(18f, SlideSteerNow);
                    MockDose(Math.Min(10f, 0.8f + t * 1.7f), false);
                    if (t > 3f) _sc.Caption = "BLOCK 4 IS HOT TOO. THE CLICKS SPEED UP.";
                    return t > 6f;
                }, SchoolBox.Dose, () => Ground(hot, 2f), 1f, 8f),
            },
            "atom_rad_dose" => new()
            {
                B("OFF THE ROAD: THREE TIMES THE DOSE.", () =>
                {
                    ScFresh();
                    _doseSchoolMock = true;
                    ScRoll(at, Math.Max(7f, _track.HalfWidth[at] + 1f), 0);
                    _scLook = () => Ground(hot, 1.5f);
                }, (t, _) =>
                {
                    ScStop();
                    MockDose(Math.Min(18f, 2f + t * 3.4f), false);
                    if (t > 2f) _sc.Caption = "MSV ACCUMULATES. STAY ON THE HARD ROAD.";
                    return t > 4.5f;
                }, SchoolBox.Dose, () => Ground(hot, 2f), 1f, 7f),
            },
            "atom_rad_power" => new()
            {
                B("THE DOSE ADDS POWER, UP TO 15 PERCENT.", () =>
                {
                    ScFresh();
                    _doseSchoolMock = true;
                    ScRoll(at, 0, 22f);
                    _scLook = () => Ground(hot, 1.5f);
                }, (t, _) =>
                {
                    ScCruise(22f, SlideSteerNow);
                    float msv = Math.Min(18f, 2f + t * 2.6f);
                    MockDose(msv, false);
                    _hud.RadBoost = EngineHealth.RadiationBoost(msv / 20f);
                    if (t > 3f) _sc.Caption = "IT EATS THE ENGINE. WASH, OR THE GULAG.";
                    return t > 6.5f;
                }, SchoolBox.Dose, () => Ground(hot, 2f), 1f, 9f),
            },
            "atom_rad_limit" => new()
            {
                B("OVER 20 MSV: THE DOSIMETRIST STOPS YOU.", () =>
                {
                    ScFresh();
                    _doseSchoolMock = true;
                    ScRoll(0, 0, 0);
                }, (t, _) =>
                {
                    ScStop();
                    MockDose(t < 2f ? 18f : 22f, t >= 2f);
                    if (t > 2f) _sc.Caption = "HE HOLDS YOU AT THE CHECKPOINT. TIME IS LOST.";
                    return t > 5f;
                }, SchoolBox.Dose, null, 1f, 7f),
            },
            _ => new()
            {
                B("THE WASH STATION: DRIVE THROUGH SLOWLY.", () =>
                {
                    ScFresh();
                    _doseSchoolMock = true;
                    Vector2 start = new(_rad.WashX, _rad.WashZ - 13f);
                    Vector2 dir = new(0, 1);
                    _car.Reset(new Vector3(start.X, _track.MeshHeight(start.X, start.Y) + .55f, start.Y), TrackData.YawFor(dir));
                    _cam.Snap(_car.Pos);
                    _scRoad = _track.NearestCenter(start, out _);
                    MockDose(16f, false);
                    _scLook = () => Ground(new Vector2(_rad.WashX, _rad.WashZ), 2f);
                }, (t, _) =>
                {
                    float dose = Math.Max(0f, 16f - Math.Max(0f, t - .6f) * 4.5f);
                    _car.SetControls(t < 1f ? .35f : 0f, 0f, 0f, false, false);
                    MockDose(dose, t > .6f && dose > 0f);
                    if (t > 1f) _sc.Caption = dose > 0 ? "THE HOSE WASHES THE DOSE AWAY." : "CLEAN AS A NEW KOPECK.";
                    return t > 4.5f;
                }, SchoolBox.Dose, () => Ground(new Vector2(_rad.WashX, _rad.WashZ), 2f), 1f, 7f),
            },
        };
    }
}
