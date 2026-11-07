using System;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// CAR TOUR: T (a pad: Y) on the FUEL card plays the highlighted fuel's car as a short show: a black card that waits for a key, then close-ups with a caption each, the engine revved
/// and lifted off so that its voice, whistle, pops and smoke are heard and seen, a gauge, and a card of its data. It is a title presentation only: the car stands at the start line of the
/// loaded track (nothing is raced, recorded or written; the fuel of the rally, the saved one and the lock are untouched, and a locked fuel's tour cannot start a run), the world is not
/// rebuilt, and Esc goes back to the card. The script of each car is CarTourScript.cs.
/// </summary>
public partial class RaceScene
{
    bool _carTourOpen, _ctWait, _ctDamageWas;
    FuelGrade _carTourFuel;
    CarTourScript _ct;
    float _ctVolumeWas, _ctT, _ctWaitT, _ctPowerWas, _ctTurboWas, _ctFov, _ctNear, _ctFar;
    ulong _ctOpenFrame, _ctReleaseFrame;
    string[] _ctSpecs = Array.Empty<string>();
    string _ctCover;

    /// <summary>The tour is up (the title, the FUEL card, the tour open): the screen is the tour's.</summary>
    public bool CarTourActive => _carTourOpen && _state == State.Title && _card == TitleCard.RallyFuel;

    /// <summary>Something presents on the whole screen: a track's tour or a car's.</summary>
    public bool PresentationActive => TrackPreviewActive || CarTourActive;
    public bool PresentationFullscreen => TrackPreviewFullscreen || CarTourActive;

    void OpenCarTour()
    {
        if (_carTourOpen || _state != State.Title) return;
        _carTourFuel = (FuelGrade)Math.Clamp(_cardSel, 0, FuelGrades.Count - 1);
        _ct = CarTourScript.For(_carTourFuel);
        _carTourOpen = true;
        _ctWait = true; _ctWaitT = 0; _ctT = 0;
        _ctOpenFrame = Engine.GetProcessFrames();
        _ctPowerWas = _car.PowerScale; _ctTurboWas = _car.TurboGain;
        _ctFov = _cam.Fov; _ctNear = _cam.Near; _ctFar = _cam.Far;
        var damage = _carVis.Damage;
        _ctDamageWas = damage?.Enabled ?? false;
        if (damage != null) { damage.Reset(); damage.Enabled = false; }      // the car stays as it left the factory
        _radio.Stop();
        AudioMix.MuteSfx(false);
        _ctVolumeWas = _snd.Volume;
        _snd.Volume = 0.8f;      // the title's demo car stays at 0.1 under the radio; the car on show is heard like the car in a race
        _fx.Clear(); ClearCarFeedback();
        _car.Reset(_track.StartPos, _track.StartYaw);
        _cam.Snap(_car.Pos);
        // The engine of this fuel needs its power and its turbo too (the title has the factory's).
        _car.PowerScale = FuelGrades.PhysicsPercent(_carTourFuel) / 100f;
        _car.TurboGain = FuelGrades.TurboGainFor(_carTourFuel);
        _ctSpecs = CarSpecRows(_carTourFuel);
        _hud.PreviewCardBig = _ct.CardBig;
        _hud.PreviewCardSmall = _ct.CardSmall;
        if (OS.HasEnvironment("CARTOURDBG")) GD.Print($"car tour: {FuelGrades.Names[(int)_carTourFuel]} opens, {_ct.Duration:F0} s");
    }

    void CloseCarTour()
    {
        if (!_carTourOpen) return;
        _carTourOpen = false;
        _ctWait = false;
        _car.PowerScale = _ctPowerWas; _car.TurboGain = _ctTurboWas;
        _car.Neutral = false; _car.Clutch = false; _car.FreeBoost = false;
        _car.SetControls(0f, 0f, 0f, false, false);
        _car.Brake = 0;
        if (_carVis.Damage != null) { _carVis.Damage.Reset(); _carVis.Damage.Enabled = _ctDamageWas; }
        _cam.Fov = _ctFov; _cam.Near = _ctNear; _cam.Far = _ctFar;
        _snd.Volume = _ctVolumeWas;
        _fx.Clear(); ClearCarFeedback();
        _radio.Play();
        AudioMix.MuteSfx(true);
        _hud.CarTour = false; _hud.TrackPreview = false;
        _hud.PreviewCardAlpha = 0; _hud.PreviewCardWaiting = false;
        _hud.PreviewCardBig = _hud.PreviewCardSmall = Array.Empty<string>();
        _hud.CarSpecAlpha = 0; _hud.CarTourCaption = null; _hud.CarRedline = 0;
        _overlayExitFrame = Engine.GetProcessFrames();
        if (OS.HasEnvironment("CARTOURDBG")) GD.Print("car tour: closed");
    }

    /// <summary>The card's keys while the tour is up. Esc closes it; Enter races on this fuel if it is open (a locked fuel stays locked). True: the tour has the key.</summary>
    bool CarTourInput()
    {
        if (!_carTourOpen) return false;
        if (Engine.GetProcessFrames() <= _ctOpenFrame + 1) return true;
        if (Input.IsActionJustPressed("pause")) { CloseCarTour(); return true; }
        if (_ctWait) return true;      // (any other key starts it: CarTourRawInput)
        if (Engine.GetProcessFrames() <= _ctReleaseFrame + 2) return true;
        if (Input.IsActionJustPressed("start") && !Input.IsKeyPressed(Key.Alt)) CarTourEnter();
        return true;
    }

    /// <summary>Enter in the tour: race on this fuel (true), unless it is locked (the same refusal as the card's: false, the tour goes on).</summary>
    bool CarTourEnter()
    {
        if (FuelLocked(_carTourFuel)) return false;
        CloseCarTour();
        ChooseTitleCard();
        return true;
    }

    /// <summary>Any key, mouse or pad button releases the black card (Esc still goes back).</summary>
    bool CarTourRawInput(InputEvent e)
    {
        if (!_carTourOpen || !_ctWait) return false;
        if (e is InputEventKey { Pressed: true, Keycode: Key.Escape }) return false;
        bool press = e is InputEventKey { Pressed: true, Echo: false } or InputEventMouseButton { Pressed: true } or InputEventJoypadButton { Pressed: true };
        if (press && _ctWaitT > 0.25f) ReleaseCarTourCard();
        return press;
    }

    void ReleaseCarTourCard()
    {
        if (!_ctWait) return;
        _ctWait = false;
        _ctT = 0;
        _ctReleaseFrame = Engine.GetProcessFrames();      // (the key that released the card may be Enter: it must not also race)
    }

    int CarShotAt(float t, out float u)
    {
        for (int i = 0; i < _ct.Shots.Length; i++)
        {
            if (t < _ct.Shots[i].Dur) { u = t; return i; }
            t -= _ct.Shots[i].Dur;
        }
        u = _ct.Shots[^1].Dur;
        return _ct.Shots.Length - 1;
    }

    /// <summary>Per frame (after the track tour's): the film's clock, the HUD's captions, card and gauges.</summary>
    void CarTourFrame(float dt)
    {
        if (_carTourOpen && !CarTourActive) CloseCarTour();      // (the card went away under it)
        if (!_carTourOpen) { _hud.CarTour = false; return; }
        if (_ctWait && _ctWaitT > 0.5f && Array.Exists(CmdArgs(), x => x == "--car-tour-enter-test"))
        {
            // The key that releases the black card is Enter: the card goes, the tour must not also race.
            Input.ActionPress("start");
            CarTourRawInput(new InputEventKey { Pressed = true, Keycode = Key.Enter, PhysicalKeycode = Key.Enter });
            bool open = CarTourInput();
            GD.Print($"CARTOURENTER: waitCleared={!_ctWait} tourOpen={_carTourOpen} card={_card} rallyFuel={_rallyFuel}");
            Input.ActionRelease("start");
            CloseCarTour();
            CCCPRacing.Audio.AudioMix.Quit(this);
            return;
        }
        if (_ctWait)
        {
            _ctWaitT += dt;
            if (_ctWaitT >= 60f) ReleaseCarTourCard();
        }
        else
        {
            _ctT += dt;
            if (_ctT >= _ct.Duration) { CarTourSelfCheck(); CloseCarTour(); return; }
        }
        int idx = CarShotAt(_ctWait ? 0 : _ctT, out float u);
        var shot = _ct.Shots[idx];
        _hud.TrackPreview = true;
        _hud.PreviewFullscreen = true;
        _hud.PreviewFocused = true;
        _hud.PreviewMovie = false;
        _hud.PreviewTitle = null; _hud.PreviewCaption = null; _hud.PreviewTell = null; _hud.PreviewArrow = null; _hud.PreviewLoading = null;
        _hud.CarTour = true;
        _hud.CarTourLocked = FuelLocked(_carTourFuel);
        _hud.PreviewCardWaiting = _ctWait;
        // The black card: held while it waits, a slow fade at the start, a quick dip to black at each cut.
        float alpha = _ctWait ? 1f : idx == 0 ? Math.Clamp(1f - u / 1.2f, 0f, 1f) : Math.Clamp(1f - u / 0.3f, 0f, 1f);
        bool dipOnly = !_ctWait;
        _hud.PreviewCardAlpha = alpha;
        _hud.PreviewCardBig = dipOnly ? Array.Empty<string>() : _ct.CardBig;
        _hud.PreviewCardSmall = dipOnly ? Array.Empty<string>() : _ct.CardSmall;
        _hud.CarTourCaption = _ctWait ? null : shot.Caption;
        _hud.CarSpecRows = _ctSpecs;
        _hud.CarSpecTitle = "TECHNICAL DATA";
        _hud.CarSpecAlpha = !_ctWait && shot.Specs ? Math.Clamp(u / 1.0f, 0f, 1f) * Math.Clamp((shot.Dur - u) / 0.5f, 0f, 1f) : 0f;
        _hud.CarRpm = _car.Rpm; _hud.CarRedline = _params.Redline; _hud.CarBoost = _car.Boost;
        if (_ctCover != null)
        {
            // --car-tour-cover="TITLE|SUBTITLE": a still for a cover picture: the tour's own heading band over the car, nothing else (no captions, gauges, hints or card).
            var parts = _ctCover.Split('|');
            _hud.PreviewTitle = parts[0]; _hud.PreviewSubtitle = parts.Length > 1 ? parts[1] : null;
            _hud.PreviewMovie = true; _hud.CarTourCaption = null; _hud.CarRedline = 0; _hud.CarSpecAlpha = 0; _hud.PreviewCardAlpha = 0;
        }
        else _hud.PreviewSubtitle = null;
    }

    /// <summary>The camera of the shot: the lens and the point it looks at move along their lines in the car's frame (eased); the ears go to the lens.</summary>
    void CarTourCamera(float dt)
    {
        int idx = CarShotAt(_ctWait ? 0 : _ctT, out float u);
        var s = _ct.Shots[idx];
        float k = Mathf.SmoothStep(0, 1, Math.Clamp(u / s.Dur, 0f, 1f));
        Vector3 eye = _car.Pos + _car.Rot * s.Eye0.Lerp(s.Eye1, k);
        Vector3 look = _car.Pos + _car.Rot * s.Look0.Lerp(s.Look1, k);
        Vector3 dir = (look - eye).Normalized();
        _cam.Scenic(_car.Pos, eye - _car.Pos, dt);
        _cam.Fov = s.Fov;
        _cam.Near = 0.15f;
        _cam.Far = 600f;
        _cam.GlobalTransform = new Transform3D(Basis.LookingAt(dir, Vector3.Up), eye);
        _cam.ListenAt(eye, dir);
    }

    /// <summary>The throttle of the film's moment: what the shot asks (blips, a sweep to the floor and a lift, flat out and a lift).</summary>
    float CarTourThrottle()
    {
        if (_ctWait) return 0f;
        int idx = CarShotAt(_ctT, out float u);
        var s = _ct.Shots[idx];
        switch (s.Rev)
        {
            case TourRev.Blip:
                return u is >= 1.0f and < 1.5f ? 0.6f : u is >= 2.6f and < 3.2f ? 0.85f : 0f;
            case TourRev.Sweep:
                if (u < 0.6f || u > s.Dur - 1.3f) return 0f;
                return Math.Clamp((u - 0.6f) / 1.8f, 0f, 1f);
            case TourRev.Lift:
                return u is >= 0.3f and < 2.2f ? 1f : u is >= 3.4f and < 4.2f ? 1f : 0f;
            default:
                return 0f;
        }
    }

    /// <summary>Physics: the car stands (neutral, clutch down, handbrake on) while the engine is revved as the shot asks.</summary>
    void CarTourDrive()
    {
        _car.Neutral = true;
        _car.Clutch = true;
        _car.FreeBoost = true;
        _car.SetControls(CarTourThrottle(), 0f, 0f, true, false);
        _car.Brake = 1;
    }

    /// <summary>The data card: what is told about the car, from the code and the loaded track's own norms (nothing measured by hand).</summary>
    string[] CarSpecRows(FuelGrade g)
    {
        string power = g switch
        {
            FuelGrade.Diesel => "400 NM, REDLINE 5000",
            FuelGrade.Petrol => "100% - THE FACTORY ENGINE",
            _ => "175%, 250% ON TURBO",
        };
        string drive = g == FuelGrade.Sputnik ? "4WD 40/60 + CENTRE LOCK" : "REAR-WHEEL";
        string voice = g switch
        {
            FuelGrade.Diesel => "KNOCK + SOOT",
            FuelGrade.Petrol => "FOUR-POT + POPS",
            FuelGrade.Rocket => "TURBO WHISTLE + BANGS",
            _ => "FIVE-POT WARBLE + ANTI-LAG",
        };
        var ladder = NormPlan.LadderFor(TrackDef.Load($"res://tracks/{_trackId}.json").Norms, g);
        var medal = _campaign.BestOn(_trackId, g);
        return new[]
        {
            "POWER|" + power,
            "DRIVE|" + drive,
            "VOICE|" + voice,
            "FUEL USE|" + (FuelGrades.Burn[(int)g] / FuelGrades.Burn[(int)FuelGrade.Petrol]).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " X PETROL",
            "NORMS|" + LapTimer.Format(ladder[0]) + " - " + LapTimer.Format(ladder[^1]),
            "MEDAL|" + (medal > NormPlan.Medal.None ? Campaign.Name(medal) : "NONE YET") + " ON " + TrackName(_trackId),
            "STATUS|" + (FuelLocked(g) ? "LOCKED" : "OPEN"),
        };
    }

    /// <summary>--car-tour-check: at the film's end print what the tour must not have touched, try Enter on a locked fuel, and quit (the tests read the line).</summary>
    void CarTourSelfCheck()
    {
        if (!Array.Exists(CmdArgs(), a => a == "--car-tour-check")) return;
        FuelGrade savedFuel = FuelGrades.Load(_records.Dir);
        bool locked = FuelLocked(_carTourFuel);
        bool entered = locked && CarTourEnter();      // a locked fuel must refuse (an open one would start a run: not tried)
        bool refused = locked && !entered && _carTourOpen && _card == TitleCard.RallyFuel;
        int archive = _lapArchiveUsed?.List().Count ?? -1;
        GD.Print($"CARTOURCHECK: fuel={_carTourFuel} locked={locked} enterRefused={refused} rallyFuel={_rallyFuel} savedFuel={savedFuel} card={_card} state={_state} archive={archive} "
            + $"damaged={_carVis.Damage?.IsDamaged} powerScale={_car.PowerScale:F2} turboGain={_car.TurboGain:F2} boost={_car.Boost:F2} volume={_snd.Volume:F2}");
        if (!locked || refused) { CloseCarTour(); GD.Print($"CARTOURCHECK: closed, replaying={Replaying} turboGain={_car.TurboGain:F2} powerScale={_car.PowerScale:F2} damageEnabled={_carVis.Damage?.Enabled} engineFuel={EngineFuel} volume={_snd.Volume:F2}"); }
        CCCPRacing.Audio.AudioMix.Quit(this);
    }

    /// <summary>Test hook: --car-tour=diesel|petrol|rocket|sputnik opens that car's tour at once on the title (a still or a check, no card).</summary>
    void StartCarTourFromArgs()
    {
        string id = null;
        foreach (var a in CmdArgs())
        {
            if (a.StartsWith("--car-tour=")) id = a["--car-tour=".Length..];
            if (a.StartsWith("--car-tour-cover=")) _ctCover = a["--car-tour-cover=".Length..];
        }
        if (id == null) return;
        EnterTitle();
        OpenMenu(MenuItem.Single, MenuItem.Race);
        OpenCard(TitleCard.RallyFuel);
        _cardSel = (int)FuelGrades.Parse(id);
        _cardFrame = 0;
        OpenCarTour();
        _ctOpenFrame = 0;
        if (Array.Exists(CmdArgs(), a => a == "--car-tour-nocard")) ReleaseCarTourCard();
        var skip = Array.Find(CmdArgs(), a => a.StartsWith("--car-tour-at="));
        if (skip != null) { ReleaseCarTourCard(); _ctT = float.Parse(skip["--car-tour-at=".Length..], System.Globalization.CultureInfo.InvariantCulture); }
    }
}
