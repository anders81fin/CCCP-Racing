using System;
using System.Collections.Generic;
using CCCPRacing.Track;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>Winter lessons driven with the real car, snowbanks, traffic and camp mechanic.</summary>
public partial class RaceScene
{
    // One continuous lap. Cards pause it; only the first part resets the car.
    void SiberiaStart(int at, float kmh, float lateral = 0)
    {
        ScFresh();
        _scRoad = at;
        ScRoll(at, lateral, kmh);
    }

    int SiberiaMetre() => SchoolScript.Follow(_track, CarXZ, ref _scRoad);

    void SiberiaMarks(params (int from, int to, string text, Color col, bool striped, int side)[] marks)
    {
        _scMarkEntry = 0;
        _scMarks.AddRange(marks);
        PaintMarks();
    }

    Beat SiberiaRoad(string caption, int from, int to, float kmh) => B(caption,
        () => SiberiaStart(from, kmh), (t, _) =>
        {
            int i = SiberiaMetre();
            ScCruise(kmh, SlideSteerNow);
            return i >= to && i < to + 100;
        }, SchoolBox.None, null, 1.3f, 30f);

    List<Beat> SiberiaBank() => new()
    {
        B("BRUSH THE BANK, DO NOT RAM IT.", () =>
        {
            SiberiaStart(355, 35f, 3.5f);
            SiberiaMarks((362, 375, "A LIGHT BRUSH", MarkYellow, false, 1));
        }, (t, _) =>
        {
            int i = SiberiaMetre();
            float edge = _track.HalfWidth[i] - 0.15f;
            ScCruise(35f, SchoolScript.SlideSteer(_car, _track, ref _scRoad, i < 374 ? edge : 0f));
            if (i >= 374) _sc.Caption = "STEER BACK IN. KEEP MOVING.";
            return i >= 389 && i < 420;
        }, SchoolBox.None, null, 1f, 18f),
    };

    List<Beat> SiberiaHairpin() => new()
    {
        B("THE SWITCHBACK: SLOW BEFORE IT.", () =>
        {
            SiberiaStart(295, 42f);
            string handbrake = _padMode ? "A" : "SPACE";
            SiberiaMarks((298, 311, "SLOW DOWN", MarkYellow, true, 0),
                (311, 321, $"STEER + HANDBRAKE ({handbrake})", MarkRed, false, 0),
                (321, 344, "RELEASE, GAS ON", MarkGreen, false, 0));
        }, (t, _) =>
        {
            int i = SiberiaMetre();
            if (i < 311 && _sc.T0 < 0) ScCruise(38f, SlideSteerNow);
            else
            {
                if (_sc.T0 < 0) { _sc.T0 = t; SlowMo(1.5f); }
                float u = t - _sc.T0;
                _sc.Caption = u < 0.25f ? "STEER AND PULL THE HANDBRAKE." : "RELEASE. CATCH IT WITH THE GAS.";
                _car.SetControls(u > 0.45f && Kmh < 35f ? 1f : 0f, Kmh > 40f ? 0.3f : 0f,
                    SlideSteerNow, u < 0.25f, false);
            }
            return i >= 348 && i < 400;
        }, SchoolBox.None, null, 1f, 25f),
    };

    List<Beat> SiberiaPlough() => new()
    {
        B("AN ONCOMING PLOUGH: KEEP RIGHT.", () =>
        {
            SiberiaStart(838, 25f, 3f);
            ApplyWorld(Actors.School | Actors.Ploughs);
            _ploughs.Reset();
            _scLook = () => SiberiaNearestPlough() is Vector2 p ? Ground(p) : _car.Pos;
        }, (t, _) =>
        {
            SiberiaMetre();
            ScCruise(22f, SchoolScript.SlideSteer(_car, _track, ref _scRoad, 3.3f));
            foreach (var p in _ploughs.Positions)
                if (!_sc.Once && p.DistanceTo(CarXZ) < 22f)
                {
                    _sc.Once = true;
                    SlowMo(1.5f);
                }
            return t > 8f;
        }, SchoolBox.None, () => SiberiaNearestPlough() is Vector2 p ? Ground(p, 4f) : null, 1f, 12f),
    };

    Vector2? SiberiaNearestPlough()
    {
        Vector2? nearest = null;
        if (_ploughs != null)
            foreach (var p in _ploughs.Positions)
                if (nearest == null || p.DistanceSquaredTo(CarXZ) < nearest.Value.DistanceSquaredTo(CarXZ)) nearest = p;
        return nearest;
    }

    // Called with the real contact feedback before it is cleared: show the brush itself.
    void SiberiaBankFeedback(float plough)
    {
        if (!InSchool || _sc.Current.step.Id != "snow_bank" || _sc.Once || plough <= 0.3f) return;
        _sc.Once = true;
        SlowMo(1.5f);
        if (SchoolDbg) GD.Print($"school: bank brushed at road metre {CarIndex}, {Kmh:F0} km/h, powder {plough:F1}");
    }

    List<Beat> SiberiaIceExit() => new()
    {
        B("ICE EXIT: THE BEND HIDES ITS SHAPE.", () =>
        {
            SiberiaStart(1035, 65f);
            SiberiaMarks((1048, 1090, "BRAKE EARLY ON THE ICE", MarkRed, true, 0),
                (1090, 1146, "DO NOT RUN WIDE", MarkYellow, false, -1));
        }, (t, _) =>
        {
            int i = SiberiaMetre();
            if (i >= 1060 && _sc.T0 < 0) { _sc.T0 = t; ShotSoon("ice_brake", 0.2f); }
            if (i >= 1106 && _sc.T1 < 0)
            {
                _sc.T1 = t;
                _scLook = () => Ground(_track.Center[1122]);
                SlowMo(1.5f);
                ShotSoon("ice_exit", 0.3f);
            }
            if (i >= 1048 && i < 1090 && Kmh > 30f)
            {
                _sc.Caption = "BRAKE NOW. THE ICE TAKES LONGER.";
                _car.SetControls(0f, (t % 0.32f) < 0.20f ? 0.65f : 0f, SlideSteerNow, false, false);
            }
            else ScCruise(i < 1048 ? 65f : 28f, SlideSteerNow);
            if (i >= 1090) _sc.Caption = "DO NOT RUN WIDE INTO THE WRECK.";
            return i >= 1154 && i < 1200;
        }, SchoolBox.None, () => Ground(_track.Center[1119], 2f), 1f, 30f),
    };

    Beat SiberiaFuelIntro(string caption, bool hard) => B(caption, () =>
    {
        ScFresh();
        _scRoad = 650;
        ScRoll(_scRoad, 0, hard ? 35f : 50f);
        ShotSoon("consumption", 2f);
    }, (t, _) =>
    {
        SiberiaMetre();
        if (hard)
        {
            if (t > 3f) _sc.Caption = "HARD GAS AND HIGH REVS USE MORE.";
            _car.SetControls(1f, 0, SlideSteerNow, false, false);
        }
        else ScCruise(50f, SlideSteerNow);
        return t > 6f;
    }, SchoolBox.Fuel, null, 1f, 8f);

    // Use this run's actual grandmother without changing who she is in the next race.
    List<Beat> SiberiaGrandma() => new()
    {
        B("HONK AND SLOW DOWN. KEEP HER SAFE.", () =>
        {
            ScFresh();
            int granny = _babushkas.PilotMother;
            _babushkas.PutOnRoad(granny, 710, 1.2f);
            _babushkas.KeepAway(SchoolScript.RoadPoint(_track, 710, 0), 90f, granny);
            _scRoad = 670;
            ScRoll(_scRoad, 1.2f, HornKmh);
            _scLook = () => Ground(_babushkas.WalkerPosition(granny));
        }, (t, _) =>
        {
            SiberiaMetre();
            int granny = _babushkas.PilotMother;
            float d = _babushkas.WalkerPosition(granny).DistanceTo(CarXZ);
            _sc.Horn = !_scStartled && d < 25f;
            if (_scStartled)
            {
                if (_sc.T0 < 0) { _sc.T0 = t; ShotSoon("grandma", 0.3f); }
                ScStop();
                return t - _sc.T0 > 3f;
            }
            ScCruise(HornKmh, 0f);
            return false;
        }, SchoolBox.None, () => Babushka(_babushkas.PilotMother), 1f, 12f),
    };

    List<Beat> SiberiaLowFuel() => new()
    {
        B("FUEL LOW: WATCH THE BLINKING GAUGE.", () =>
        {
            ScFresh(fuel: 0.15f);
            _lowWarned = false;
            _scRoad = 650;
            ScRoll(_scRoad, 0, 30f);
            ShotSoon("low", 1f);
        }, (t, _) =>
        {
            SiberiaMetre();
            ScCruise(30f, SlideSteerNow);
            if (t > 4f && !_sc.Once)
            {
                _sc.Once = true;
                _tank.Refill(0.025f);
                _sc.Caption = "NEAR EMPTY: THE ENGINE SPUTTERS.";
                ShotSoon("sputter", 1f);
            }
            return t > 8f;
        }, SchoolBox.Fuel, null, 1f, 10f),
    };

    List<Beat> SiberiaEmptyFuel() => new()
    {
        B("EMPTY TANK: THE ENGINE DIES.", () =>
        {
            ScFresh(fuel: 0.0001f);
            _scRoad = 660;
            ScRoll(_scRoad, 0, 50f);
        }, (t, _) =>
        {
            SiberiaMetre();
            _car.SetControls(0f, 0f, SlideSteerNow, false, false);
            if (_dry && _sc.T0 < 0) { _sc.T0 = t; ShotSoon("empty", 0.3f); }
            if (_dry && t > 2f) _sc.Caption = "COAST TO A CAN OR THE DEPOT.";
            return _dry && t > 5f;
        }, SchoolBox.Fuel, null, 1f, 12f),
    };

    List<Beat> SiberiaFuelTow() => new()
    {
        B("STOPPED: THE TRACTOR COMES.", () =>
        {
            ScFresh(fuel: 0);
            ScRoll(710, 0, 0);
        }, (t, _) =>
        {
            ScStop();
            if (_rescue.Towing && !_rescue.Arriving && !_sc.Once)
            {
                _sc.Once = true;
                _sc.Caption = "TOWED: THE LAP AND ONE TRY LOST.";
                ShotSoon("tow", 0.3f);
            }
            if (_sc.Once && !_dry && _tank.Fraction > 0.99f)
            {
                if (_sc.T0 < 0) { _sc.T0 = t; ShotSoon("full_tank", 0.3f); }
                _sc.Caption = "AT THE DEPOT, WITH A FULL TANK.";
                return t - _sc.T0 > 2f;
            }
            return false;
        }, SchoolBox.Fuel, () => _rescue.Towing ? _rescue.Model.GlobalPosition + Vector3.Up * 3f : null, 1.5f, 45f),
    };

    List<Beat> SiberiaMechanic() => new()
    {
        B("CAMP: FIRST BARRACK, RIGHT SIDE.", () =>
        {
            ScFresh(health: MechanicHealth);
            _scRoad = MechanicStop - 24;
            ScRoll(_scRoad, 1f, 18f);
            _carVis.Damage?.Hit(10f, new Vector3(0, -0.05f, -2.05f), Vector3.Back, _car, _fx);
            _scLook = () => Ground(_drunks.MechanicPosition);
            if (SchoolDbg) GD.Print($"school: camp mechanic at road metre {MechanicStop}, position {_drunks.MechanicPosition}");
        }, (t, _) =>
        {
            SiberiaMetre();
            Vector2 park = SchoolScript.RoadPoint(_track, MechanicStop - 3, 2.5f);
            float d = park.DistanceTo(CarXZ);
            if (_drunks.MechanicUp || t > 3f) _sc.Caption = _drunks.MechanicUp ? "PARK NEAR HIM. HE FIXES IT." : "CALL THE SLEEPING MAN: HONK.";
            _sc.Horn = !_drunks.MechanicUp && MechanicHonk(_drunks.MechanicPosition.DistanceTo(CarXZ));
            if (_sc.Horn && _sc.T1 < 0) { _sc.T1 = t; HonkLog("camp mechanic", Ground(_drunks.MechanicPosition, 1f), _drunks.MechanicPosition.DistanceTo(CarXZ)); }
            if (d < 2f || _drunks.HoldsCar) ScStop();
            else
            {
                float want = Math.Clamp(d * 1.3f, 0, 18f);
                _car.SetControls(Kmh < want ? 0.7f : 0f, Kmh > want + 2 ? 0.6f : 0f,
                    SchoolScript.SteerTo(_car, park), false, false);
            }
            if (_scRepairing && _sc.T0 < 0) { _sc.T0 = t; ShotSoon("repair", 0.5f); }
            _sc.Speed = _scRepairing && !_scRepaired && t - _sc.T0 > 2f ? 2.5f : 1f;
            if (_scRepaired)
            {
                if (!_sc.Once) { _sc.Once = true; _sc.T0 = t; }
                _sc.Caption = "ENGINE AND BODY: GOOD AS NEW.";
                return t - _sc.T0 > 2f;
            }
            return false;
        }, SchoolBox.Engine, () => Ground(_drunks.MechanicPosition, 2.6f), 1f, 45f),
    };
}
