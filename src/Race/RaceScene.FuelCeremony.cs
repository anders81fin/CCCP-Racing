using System;
using CCCPRacing.Audio;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    FuelGrade? _fuelCeremony;
    /// <summary>The title has come up (or a ceremony has just closed): the ceremonies that are due start when the title is clear (CeremonyClear), never before.</summary>
    bool _ceremonyDue;

    /// <summary>
    /// The main menu is up with nothing over it and nothing still on its way: not booting, the launch cards and the KGB ARCHIVE done, no loading card, no screen or card open, the settled frame after the last one.
    /// A ceremony that began earlier (the Deck's launch, 0.916 diag: a licence over the launch cards that took no key) sat under the startup flow's own input.
    /// </summary>
    bool CeremonyClear => _state == State.Title && !_booting && _archiveDone && !_filesAtLaunch && TrackLoadingScreen.Active == null
        && !_hud.StartupArchivePending && !_hud.StartupVideoOnly && TitleMenuToggleAllowed && !TitleSettingsOpen && !_titleMenuHidden && _card == TitleCard.None
        && !FuelCeremonyActive && !SputnikCeremonyActive && _tourCapture == false;
    bool _fcSetup, _fcDamageWas;      // (the car on show, below)
    float _fcPowerWas, _fcTurboWas, _fcVolumeWas, _fcFov, _fcNear, _fcFar;

    /// <summary>A ceremony is up over the title: the menu waits under it.</summary>
    bool FuelCeremonyActive => _fuelCeremony != null && _state == State.Title;
    bool SputnikCeremonyActive => _spStage != SpStage.None && _state == State.Title;

    /// <summary>
    /// A fuel that has just been opened by the rally silvers (PETROL, JET FUEL, SPUTNIK) gets a ceremony of its own, once, when the title comes up after the run (the owner's order of 2026-11-22): bigger than a track opening and than a
    /// gold on one track. A profile that has never been asked treats the fuels already open as old news.
    /// </summary>
    void BeginFuelCeremony()
    {
        if (_fuelCeremony != null || ViewerMode || _previewMovie || InstructionTrack != null || _campaign == null || !CeremonyClear) return;
        var candidates = new[] { FuelGrade.Petrol, FuelGrade.Rocket, FuelGrade.Sputnik };
        if (!_campaign.FuelPresentationStarted)
        {
            foreach (var g in candidates) if (FuelOpenByMedals(g)) _campaign.PresentFuel(g);
            _campaign.PresentFuel(FuelGrade.Diesel);      // (marks the question as asked: the section exists from now on)
            return;
        }
        foreach (var g in candidates)
            if (FuelOpenByMedals(g) && !_campaign.FuelPresented(g))
            {
                _fuelCeremony = g;
                _hud.BeginFuelCeremony(g);
                _overlayExitFrame = Engine.GetProcessFrames();
                return;
            }
    }

    void FuelCeremonyFrame(float dt)
    {
        ShowCarFrame();
        if (_ceremonyDue && CeremonyClear) { _ceremonyDue = false; BeginFuelCeremony(); if (_fuelCeremony == null) BeginSputnikCeremony(); }
        if (_fuelCeremony == null) return;
        if (_state != State.Title) { _fuelCeremony = null; _hud.EndFuelCeremony(); return; }
        _hud.FuelCeremonyTick(Math.Min(dt, 0.1f));
    }

    /// <summary>Any key, mouse or pad button closes it (after it has been up long enough to be seen).</summary>
    bool FuelCeremonyInput(InputEvent e)
    {
        if (!FuelCeremonyActive) return false;
        bool press = e is InputEventKey { Pressed: true, Echo: false } or InputEventMouseButton { Pressed: true } or InputEventJoypadButton { Pressed: true };
        if (!press || !_hud.FuelCeremonyCanClose) return true;
        _campaign.PresentFuel(_fuelCeremony.Value);
        _fuelCeremony = null; _hud.EndFuelCeremony();
        _overlayExitFrame = Engine.GetProcessFrames();
        _ceremonyDue = true;      // (the next one, if two fuels opened together, then the test pilots' programme: when the title is clear again)
        return true;
    }

    // ------------------------------------------------------------------ the car on show (the fuel ceremony's and the Sputnik paint scene's: the world's own car, parked at the start line, a camera that shows it)

    FuelGrade ShowCarFuel => _fuelCeremony ?? FuelGrade.Sputnik;
    /// <summary>A ceremony has the car on the whole screen: the new fuel's, or the chief test pilot's being painted.</summary>
    bool CarOnShow => FuelCeremonyActive || SputnikPaintActive || GalleryCarActive;
    float ShowCarAge => FuelCeremonyActive ? _hud.FuelCeremonyAge : _hud.GalleryAge;

    /// <summary>Per frame: the car is put out on show when a ceremony wants it, and back as it was when none does (so that a ceremony following another needs no second set-up).</summary>
    void ShowCarFrame()
    {
        if (CarOnShow && !_fcSetup) ShowCarSetup();
        else if (!CarOnShow && _fcSetup) ShowCarEnd();
    }

    void ShowCarSetup()
    {
        _fcSetup = true;
        _fcPowerWas = _car.PowerScale; _fcTurboWas = _car.TurboGain;
        _fcFov = _cam.Fov; _fcNear = _cam.Near; _fcFar = _cam.Far;
        var damage = _carVis.Damage;
        _fcDamageWas = damage?.Enabled ?? false;
        if (damage != null) { damage.Reset(); damage.Enabled = false; }
        _radio.Stop();
        AudioMix.MuteSfx(false);
        _fcVolumeWas = _snd.Volume;
        _snd.Volume = 0.6f;
        _fx.Clear(); ClearCarFeedback();
        _car.Reset(_track.StartPos, _track.StartYaw);
        _cam.Snap(_car.Pos);
        _car.PowerScale = FuelGrades.PhysicsPercent(ShowCarFuel) / 100f;
        _car.TurboGain = FuelGrades.TurboGainFor(ShowCarFuel);
    }

    void ShowCarEnd()
    {
        _fcSetup = false;
        _car.PowerScale = _fcPowerWas; _car.TurboGain = _fcTurboWas;
        _car.Neutral = false; _car.Clutch = false; _car.FreeBoost = false;
        _car.SetControls(0f, 0f, 0f, false, false);
        _car.Brake = 0;
        if (_carVis.Damage != null) { _carVis.Damage.Reset(); _carVis.Damage.Enabled = _fcDamageWas; }
        _cam.Fov = _fcFov; _cam.Near = _fcNear; _cam.Far = _fcFar;
        _snd.Volume = _fcVolumeWas;
        _fx.Clear(); ClearCarFeedback();
        _radio.Play();
        TitleSoundApply();
    }

    /// <summary>The car idles; a blip of the throttle when the name arrives and every few seconds after it (the engine's voice is part of the fuel); in the paint scene one when the paint is dry.</summary>
    void ShowCarDrive()
    {
        float th = 0f;
        if (SputnikPaintActive) { float c = _hud.SpPaintAge - _hud.SpPaintDoneAt; th = c is >= 0f and < 0.8f ? 0.9f : c is >= 1.4f and < 1.9f ? 0.55f : 0f; }
        else { float a = ShowCarAge; if (a >= 2.7f) { float c = (a - 2.7f) % 4.5f; th = c < 0.7f ? 0.9f : c is >= 1.2f and < 1.7f ? 0.55f : 0f; } }
        _car.Neutral = true;
        _car.Clutch = true;
        _car.FreeBoost = true;
        _car.SetControls(th, 0f, 0f, true, false);
        _car.Brake = 1;
    }

    void ShowCarCamera(float dt)
    {
        if (SputnikPaintActive) { SputnikPaintCamera(dt); return; }
        float age = ShowCarAge;
        float ang = Mathf.DegToRad(30f + age * 26f), dist = 7.2f + 0.9f * MathF.Sin(age * 0.5f), height = 1.5f + 0.35f * MathF.Sin(age * 0.37f + 1f);
        ShowCarLens(new Vector3(MathF.Sin(ang) * dist, height, -MathF.Cos(ang) * dist), new Vector3(0, -0.1f, 0), 50f, dt);
    }

    /// <summary>The lens at an eye and a look point in the car's own frame.</summary>
    void ShowCarLens(Vector3 eyeLocal, Vector3 lookLocal, float fov, float dt)
    {
        Vector3 eye = _car.Pos + _car.Rot * eyeLocal, look = _car.Pos + _car.Rot * lookLocal;
        Vector3 dir = (look - eye).Normalized();
        _cam.Scenic(_car.Pos, eye - _car.Pos, dt);
        _cam.Fov = fov; _cam.Near = 0.15f; _cam.Far = 600f;
        _cam.GlobalTransform = new Transform3D(Basis.LookingAt(dir, Vector3.Up), eye);
        _cam.ListenAt(eye, dir);
    }
}
