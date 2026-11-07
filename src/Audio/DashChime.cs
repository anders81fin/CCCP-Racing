using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>
/// The sound of the dashboard's warning lights (HudWarningLights.cs): a soft two-note chime when a light comes on yellow (a warning), a burst of
/// three sharp beeps when one turns red (an alarm), and the burst again every 1.5 s while any light stays red (every 4 s once the engine is dead or
/// the tank empty: nothing more to be done). Not positional, on the Warning bus (the ALERTS level). No samples.
/// </summary>
public partial class DashChime : Node
{
    const float MixRate = 22050f;
    const float AlarmEvery = 1.5f, DeadEvery = 4f;

    /// <summary>Linear gain of the whole chime.</summary>
    public float Volume = 0.4f;

    /// <summary>False when the drive is not live (a wreck, a pause, a replay, the school, a menu): nothing sounds and nothing repeats; the lights keep their levels.</summary>
    public bool Live = true;

    AudioStreamPlayer _player;
    AudioStreamGeneratorPlayback _pb;
    readonly List<Note> _notes = new();
    int[] _levels = Array.Empty<int>();
    float _repeat;
    double _clock;

    struct Note { public double Start, Len; public float Hz, Amp; public bool Sharp; public double Phase; }

    public override void _ExitTree()
    {
        using var driverLock = AudioMix.LockDriver();
        using var generator = GodotObject.IsInstanceValid(_player) ? _player.Stream as AudioStreamGenerator : null;
        if (GodotObject.IsInstanceValid(_player))
        {
            _player.StreamPaused = false; _player.Stop();
            _player.Stream = null;
        }
        if (GodotObject.IsInstanceValid(_pb)) _pb.Stop(); _pb?.Dispose(); _pb = null;
    }

    public override void _Ready()
    {
        _player = new AudioStreamPlayer { Stream = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 0.1f }, VolumeDb = EngineSound.GainDb, Bus = AudioMix.BusFor(AudioMix.Warning) };
        AddChild(_player);
        _player.Play();
        _pb = _player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    /// <summary>The drive goes on or stops being live. Stopping silences it at once (the notes and the repeat); it picks up where it was when the drive is live again.</summary>
    public void SetLive(bool live)
    {
        if (Live == live) return;
        Live = live;
        if (!live) _notes.Clear();
    }

    /// <summary>A new run (or the title): the lights are out, so that a warning on the new drive sounds as the first of its kind.</summary>
    public void Reset()
    {
        _levels = Array.Empty<int>();
        _repeat = 0;
        _notes.Clear();
    }

    /// <summary>The levels of the lights now (0 off, 1 warning, 2 alarm, 3 dead or empty), the same order each time: a light that rises sounds once.</summary>
    public void Lights(params int[] levels)
    {
        if (!Live) return;
        if (_levels.Length != levels.Length) _levels = new int[levels.Length];
        int top = 0; bool rose = false, roseRed = false;
        for (int i = 0; i < levels.Length; i++)
        {
            if (levels[i] > _levels[i]) { rose = true; if (levels[i] >= 2) roseRed = true; }
            _levels[i] = levels[i];
            top = Math.Max(top, levels[i]);
        }
        if (roseRed) { Alarm(); _repeat = top >= 3 && !AnyRed(levels) ? DeadEvery : AlarmEvery; }
        else if (rose) Chime();
        if (top < 2) { _repeat = 0; return; }
    }

    static bool AnyRed(int[] levels) { foreach (int l in levels) if (l == 2) return true; return false; }

    /// <summary>The repeat of an alarm that stays: called every frame with the frame's time.</summary>
    void Repeat(float dt)
    {
        int top = 0; foreach (int l in _levels) top = Math.Max(top, l);
        if (top < 2) { _repeat = 0; return; }
        _repeat -= dt;
        if (_repeat <= 0) { Alarm(); _repeat = AnyRed(_levels) ? AlarmEvery : DeadEvery; }
    }

    void Chime()
    {
        double t = _clock + 0.02;
        _notes.Add(new Note { Start = t, Len = 0.32, Hz = 784f, Amp = 0.55f });
        _notes.Add(new Note { Start = t + 0.17, Len = 0.5, Hz = 1046f, Amp = 0.55f });
    }

    void Alarm()
    {
        double t = _clock + 0.02;
        for (int i = 0; i < 3; i++) _notes.Add(new Note { Start = t + i * 0.16, Len = 0.09, Hz = 1250f, Amp = 0.35f, Sharp = true });
    }

    public override void _Process(double delta)
    {
        if (_pb == null) return;
        if (Live) Repeat((float)delta);
        int frames = _pb.GetFramesAvailable();
        for (int i = 0; i < frames; i++)
        {
            _clock += 1.0 / MixRate;
            float s = 0;
            for (int n = _notes.Count - 1; n >= 0; n--)
            {
                var note = _notes[n];
                double age = _clock - note.Start;
                if (age < 0) continue;
                if (age >= note.Len) { _notes.RemoveAt(n); continue; }
                note.Phase += note.Hz / MixRate;
                _notes[n] = note;
                float env = note.Sharp ? (float)Math.Min(1.0, age / 0.004) * (float)Math.Min(1.0, (note.Len - age) / 0.01)
                    : (float)Math.Min(1.0, age / 0.006) * MathF.Exp((float)(-age * 7.5));      // a bell: struck, dying away
                float w = note.Sharp ? ((note.Phase % 1.0) < 0.5 ? 1f : -1f) * 0.55f + MathF.Sin((float)(note.Phase * Math.Tau)) * 0.45f
                    : MathF.Sin((float)(note.Phase * Math.Tau)) + 0.28f * MathF.Sin((float)(note.Phase * 2 * Math.Tau)) * MathF.Exp((float)(-age * 18));
                s += w * env * note.Amp;
            }
            s = Math.Clamp(s * Volume, -1f, 1f);
            _pb.PushFrame(new Vector2(s, s));
        }
    }
}
