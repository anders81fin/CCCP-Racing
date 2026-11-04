using System;
using CCCPRacing.Audio;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// Summer lens effects of the Ilyich's Lamp (tuned restrained; nothing touches the driving):
///  - poplar fluff drifting in the 3D view by day (PoplarFluff); every 20-40 s one tuft sticks to the lens for 2-4 s;
///  - a dust film (HudLens) building over tens of seconds on loose dry ground or behind a tractor, cleared by
///    fast smooth driving or water;
///  - insects: on average once per 60-120 s of driving, never twice within 30 s: a fly that walks about
///    (2-3 s) or, over 70 km/h, a bug that splats (fades over 3 s, with a tiny tick);
///  - straw and chaff: a burst when a hay bale is knocked over (a couple of bits stick), a light stream
///    when driving fast through a field;
///  - the low sun's flare at sunset and sunrise (at most about 12% opacity).
/// Test: env LENSDBG=fluff|dust|fly|splat|straw|flare forces that effect to show (~3 s into the race).
/// </summary>
public partial class RaceScene
{
    PoplarFluff _fluff;
    ChaffBurst _hayChaff, _fieldChaff;
    InsectTick _insectTick;
    string _lensDbg;
    bool _lensWasOn, _lensDbgDone;
    float _lensRaceT, _lensDust, _fluffNext, _insectNext, _insectCool, _fieldT, _fieldCool;
    int _lensHayKnocks;
    readonly RandomNumberGenerator _lensRng = new();

    void InitLensFx()
    {
        if (_track.Id != "ilyich") return;
        _lensRng.Seed = (ulong)Time.GetTicksUsec();
        _lensDbg = OS.GetEnvironment("LENSDBG");
        if (string.IsNullOrEmpty(_lensDbg)) _lensDbg = null;
        AddChild(_fluff = PoplarFluff.Create(_carVis));
        AddChild(_hayChaff = ChaffBurst.Create(true));
        AddChild(_fieldChaff = ChaffBurst.Create(false));
        AddChild(_insectTick = new InsectTick { Name = "InsectTick" });
        ResetLensFx();
    }

    void ResetLensFx()
    {
        _lensRaceT = 0; _lensDust = 0; _lensDbgDone = false;
        _fluffNext = _lensRng.RandfRange(15f, 30f);
        _insectNext = _lensRng.RandfRange(60f, 120f);
        _insectCool = 30f; _fieldT = 0; _fieldCool = 0;
        _lensHayKnocks = _scenery?.HayKnocks ?? 0;
        _hud?.LensReset();
    }

    /// <summary>How much of the day's high sun there is (0 at night and in the low sun, 1 by day).</summary>
    float LensDaylight(out DayNight.Look look)
    {
        look = DayNight.LookAt(_dayNight != null && _dayNight.Active ? _dayNight.Clock : 0f);
        return Mathf.SmoothStep(14f, 28f, look.SunElev) * Math.Clamp((look.SunEnergy - 0.5f) / 0.3f, 0f, 1f);
    }

    static bool LooseDry(SurfaceType s) => s is SurfaceType.Gravel or SurfaceType.Dirt or SurfaceType.Field or SurfaceType.Grass or SurfaceType.DeadGrass;

    void LensFx(float dt)
    {
        if (_fluff == null || _hud == null) return;
        bool inCar = _state is State.Countdown or State.Racing or State.Paused && !InSchool;
        bool live = _state == State.Racing && !InSchool && dt > 0f;
        float day = LensDaylight(out var look);
        _fluff.Visible = inCar && !_hud.Clean && day > 0.05f;
        _fluff.Emitting = _fluff.Visible;
        if (inCar != _lensWasOn) { _lensWasOn = inCar; if (inCar && _state == State.Countdown) ResetLensFx(); }

        // The low sun's flare: stronger the lower the sun, gone when it has set.
        float flare = 0;
        var flat = new Vector2(TerrainMesh.SunDir.X, TerrainMesh.SunDir.Z).Normalized().Rotated(Mathf.DegToRad(look.SunAz));
        if (inCar && look.SunEnergy > 0.2f)
            flare = Mathf.SmoothStep(26f, 9f, look.SunElev) * Math.Clamp(look.SunEnergy / 0.6f, 0f, 1f);
        if (_lensDbg == "flare") flare = 1f;
        _hud.LensFlare = flare;
        _hud.LensFlareDir = flat;               // the camera looks along -Z: world x is screen x, world z is screen down

        if (!inCar) return;
        _hud.LensAdvance(dt, _car.Speed * 3.6f);
        if (!live) return;

        float kmh = _car.Speed * 3.6f;
        _lensRaceT += dt;
        SurfaceType? ground = _car.GroundSurface;

        // --- 1. a tuft of fluff sticks to the glass
        if (day > 0.5f && (_fluffNext -= dt) <= 0)
        {
            _fluffNext = _lensRng.RandfRange(20f, 40f);
            _hud.LensFluff(_lensRng.RandfRange(2f, 4f));
        }

        // --- 2. dust
        float rate = 0;
        if (ground is SurfaceType g && LooseDry(g)) rate += Mathf.SmoothStep(25f, 90f, kmh) / 45f;
        float near = float.MaxValue;
        var cp = new Vector2(_car.Pos.X, _car.Pos.Z);
        if (_tractorAll != null) near = Math.Min(near, _tractorAll.Position2D.DistanceTo(cp));
        if (_slurryAll != null) near = Math.Min(near, _slurryAll.Position2D.DistanceTo(cp));
        if (near < 14f && kmh > 8f) rate += 1f / 18f;
        if (ground is SurfaceType w && w is SurfaceType.Puddle or SurfaceType.Water or SurfaceType.Shallow && kmh > 8f) rate = -1f / 3f;
        else if (rate <= 0 && ground is SurfaceType h && h is SurfaceType.Concrete or SurfaceType.Runway && kmh > 110f) rate = -1f / 25f;
        else if (rate <= 0) rate = -1f / 400f;                    // the wind takes it very slowly
        _lensDust = Math.Clamp(_lensDust + rate * dt, 0f, 1f);
        _hud.LensDust = _lensDbg == "dust" ? 1f : _lensDust;

        // --- 3. insects
        _insectCool -= dt;
        if (kmh > 10f) _insectNext -= dt;
        if (_insectNext <= 0 && _insectCool <= 0)
        {
            _insectNext = _lensRng.RandfRange(60f, 120f);
            _insectCool = 30f;
            if (kmh > 70f && _lensRng.Randf() < 0.65f) { _hud.LensBug(); _insectTick?.Tick(); }
            else _hud.LensFly(_lensRng.RandfRange(2f, 3f));
        }

        // --- 4. straw and chaff
        if (_scenery != null && _scenery.HayKnocks != _lensHayKnocks)
        {
            _lensHayKnocks = _scenery.HayKnocks;
            _hayChaff.Fire(_scenery.HayAt);
            _hud.LensStraw(_lensRng.RandiRange(1, 3), _lensRng.RandfRange(1.2f, 2.2f));
        }
        bool field = ground is SurfaceType.Field && kmh > 55f;
        _fieldT = field ? _fieldT + dt : 0f;
        _fieldCool -= dt;
        _fieldChaff.Aim(_car.Pos, _car.Vel, field);
        if (field && _fieldT > 2f && _fieldCool <= 0 && _lensRng.Randf() < dt * 0.25f)
        {
            _fieldCool = 14f;
            _hud.LensStraw(1, _lensRng.RandfRange(1.2f, 2f));
        }

        // --- test forcing
        if (_lensDbg != null && !_lensDbgDone && _lensRaceT > 3f)
        {
            _lensDbgDone = true;
            switch (_lensDbg)
            {
                case "fluff": _hud.LensFluff(40f); break;
                case "fly": _hud.LensFly(40f); break;
                case "splat": _hud.LensBug(); _insectTick?.Tick(); break;
                case "straw": _hayChaff.Fire(_car.Pos + new Vector3(0, 0, -3)); _hud.LensStraw(3, 40f); break;
            }
        }
    }
}
