using System;
using System.Collections.Generic;
using Godot;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Track;

namespace CCCPRacing.Race;

/// <summary>
/// The main menu's background lap. A take is driven on the kolkhoz in a rally run (its moving world, its car damage, no norms) with the Stakhanov laps' fuel keys: the diesel first,
/// then the petrol, the jet fuel and the Sputnik on the keys 1-4 as the driver likes. Every change of the car brings the next hour of the day (the light and the weather follow the
/// time of day, the car's own fuel lights it), so the lap goes from noon to night in four cars. The take is recorded as a pair (ghost, world tape) with the fuel list beside it
/// (tools/title/promote_title_take.sh puts a good one into assets/replays), and the title plays it with the moving world, the day and the car of each moment.
/// Record with: godot-mono --path . -- --title-rec=DIR   (DIR/lap-001 has the take; the game quits when the lap is done).
/// </summary>
public partial class RaceScene
{
    /// <summary>The time of day (in laps of the day, DayNight.Clock) each fuel's stretch heads for: diesel noon, petrol the golden sunset, jet fuel dusk, Sputnik the dead of night.</summary>
    internal static readonly float[] TitleRecLights = { 1.0f, 3.5f, 5.0f, 8.0f };
    const float TitleRecLightRate = 0.6f;      // (the day moves this many clock units a second after a change of car: a few seconds from one hour to the next)

    /// <summary>The recording's clock of the day moves towards the hour of the car in use.</summary>
    internal static float TitleRecLightStep(float clock, FuelGrade fuel, float dt)
    {
        float target = TitleRecLights[Math.Clamp((int)fuel, 0, TitleRecLights.Length - 1)];
        return Mathf.MoveToward(clock, target, TitleRecLightRate * dt);
    }

    void TitleRecLightTick(float dt) => _titleRecClock = TitleRecLightStep(_titleRecClock, _rallyFuel, dt);

    // ------------------------------------------------------------ the title's bundled lap
    /// <summary>The fuel of each moment of the bundled lap (seconds:grade), null when the lap was driven on one car.</summary>
    (float T, int Grade)[] _bundledTitleFuels;
    /// <summary>The bundled lap is the recorded take (title background), with the tape of its own: it does not use a record set's slot.</summary>
    bool _bundledRallyTake;
    TapeSlot _bundledTapeSlot;

    /// <summary>The bundled lap's car at the replay clock (its fuel list), or null (the record set tells, as before).</summary>
    FuelGrade? BundledFuelNow()
    {
        if (!BundledTitleReplay || _bundledTitleFuels == null) return null;
        int at = 0;
        for (int i = 0; i < _bundledTitleFuels.Length; i++) if (_bundledTitleFuels[i].T <= _replayClock) at = i;
        return (FuelGrade)_bundledTitleFuels[at].Grade;
    }

    const string BundledRallyTake = "res://assets/replays/ilyich-title-rally";

    /// <summary>The recorded background lap (ghost, world tape and fuel list in assets/replays), if it was made on this very track: it plays on the title before the older demonstration.</summary>
    bool LoadBundledRallyTake()
    {
        if (_track.Id != "ilyich" || !FileAccess.FileExists(BundledRallyTake + ".sig") || !FileAccess.FileExists(BundledRallyTake + ".bin")) return false;
        if (FileAccess.GetFileAsString(BundledRallyTake + ".sig").StripEdges() != _records.Signature) return false;      // (the terrain changed: the take no longer fits)
        var ghost = Ghost.Load(BundledRallyTake + ".bin", _params, _track);
        if (ghost == null) return false;
        _bundledTitleGhost = ghost;
        _bundledTitleFuels = FileAccess.FileExists(BundledRallyTake + ".fuels") ? ParseLapFuels(FileAccess.GetFileAsString(BundledRallyTake + ".fuels").StripEdges()) : null;
        var slot = new TapeSlot { Ghost = ghost };
        if (!TapeOff && FileAccess.FileExists(BundledRallyTake + ".tape"))
        {
            var tape = TapeStore.LoadBytes(BundledRallyTake + ".tape", FileAccess.GetFileAsBytes(BundledRallyTake + ".tape"), ghost);
            var body = tape == null ? null : WorldTapePlayer.Validate(tape);
            if (body != null) { slot.Tape = tape; slot.Body = body; }
            else if (tape != null) GD.Print("World tape: the bundled title take has a corrupt or inconsistent body, ignored");
        }
        _bundledTapeSlot = slot;
        _bundledRallyTake = true;
        return true;
    }

    /// <summary>The recorded background lap plays with its sounds at half the normal volume under the menu music.</summary>
    const float TitleBackdropDb = -6f;

    bool TitleBackdropSounds => _state == State.Title && !_tapeShoot && BundledTitleReplay && _bundledRallyTake && !_watching;

    /// <summary>The title's sound: the background lap's engine and world at half volume (the sound-effect bus at -6 dB), else the music alone as before (a tape shoot keeps its full sound).</summary>
    void TitleSoundApply()
    {
        if (TitleBackdropSounds) { AudioMix.Backdrop(TitleBackdropDb); _snd.Volume = 1f; return; }
        AudioMix.MuteSfx(!_tapeShoot);      // (a tape shoot needs the replay's engine and world sound)
        _snd.Volume = _tapeShoot ? 1f : 0.1f;      // demo car stays under the radio unless shooting
    }

    /// <summary>The car's paint is the shared materials': this scene's car claims them (after a nested viewer has used them).</summary>
    internal void RepaintCar() => _carVis?.Repaint();

    /// <summary>A recorded take's fuel list goes beside its pair (RaceScene.Tape.cs, TapeSaveSessionLap).</summary>
    void TitleRecWriteFuels(string folder)
    {
        if (!_titleRec) return;
        System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "fuels.txt"), LapFuelsText() + "\n");
        GD.Print($"title take: fuels {LapFuelsText()}, light {_titleRecClock:0.00}");
    }
}
