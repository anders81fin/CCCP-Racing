using System;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>
/// Title music as heard from a cheap Soviet transistor radio: band-limited, mono, a bit of
/// speaker distortion, constant hiss, crackle and every now and then a spell of bad reception
/// (music fades and flutters, static and a tuning whistle rise).
/// All real-time on the "Radio" bus, so the original recording stays untouched.
/// Also the car radio: Tune() changes station with a sweep of static and whistle, PowerOff()
/// clicks it off, SongStarted/SongFinished let the owner show titles and move to the next song.
/// Interference/Sweep (set by the car radio near the radar station) add their own static, fading,
/// a warbling heterodyne whistle and a radar buzz.
/// </summary>
public partial class RadioMusic : Node
{
    public const string BusName = "Radio";
    /// <summary>The title music's bus: only a hint of the old radio (see <see cref="Subtle"/>).</summary>
    public const string SubtleBusName = "RadioTitle";
    const float MixRate = 22050f;

    [Export] public string TrackPath = "res://assets/music/he_katsoivat_lanteen.mp3";
    [Export] public float VolumeDb = -1f;
    /// <summary>Loop the song (title music) or finish it and raise SongFinished (car radio).</summary>
    public bool LoopSongs = true;
    /// <summary>Title music: a minimal radio effect — a gentle band limit, a whisper of hiss and
    /// a rare crackle, no distortion and no reception drop-outs. Set before the node enters the tree.</summary>
    public bool Subtle;
    string Bus => Subtle ? SubtleBusName : BusName;

    public event Action<string> SongStarted;
    public event Action SongFinished;

    // Tuning (station change) state: 0..1 through the sweep, stream swaps at the middle.
    const float TuneTime = 0.9f;
    float _tune = -1f, _offBurst;
    AudioStream _pending;
    string _pendingName;
    bool _suspended;

    AudioStreamPlayer _music, _static;
    AudioStreamGeneratorPlayback _pb;
    readonly Random _rng = new();

    float _fade, _fadeTarget;

    // Reception state.
    float _nextEpisode = 5f, _episodeLeft, _episodeLen, _episodeDepth;
    float _flutter = 1f, _flutterTarget = 1f, _flutterTimer;

    // Static synthesis.
    float _hissLp, _crackle, _crackleLp;
    double _whistlePhase;
    float _whistleFreq = 1800f;

    /// <summary>Radar interference 0..1 (0 for the title music).</summary>
    public float Interference;
    /// <summary>0..1 while the radar beam sweeps across the receiver: a harsh buzz burst.</summary>
    public float Sweep;
    float _intFade = 1f, _intFadeTarget = 1f, _intFadeTimer, _dropLeft, _nextDrop = 1f;
    double _warblePhase, _hetPhase, _buzzPhase;
    float _buzz;

    public bool Playing => _fadeTarget > 0;

    public override void _ExitTree()
    {
        // Release the managed reference before Godot tears down the audio server.
        _pb?.Dispose();
        _pb = null;
    }

    public override void _Ready()
    {
        if (Subtle) EnsureSubtleBus(); else EnsureBus();

        AudioStream stream = string.IsNullOrEmpty(TrackPath) ? null : ResourceLoader.Load<AudioStream>(TrackPath);
        if (stream is AudioStreamMP3 mp3) mp3.Loop = LoopSongs;
        _music = new AudioStreamPlayer { Stream = stream, Bus = Bus, VolumeDb = -80f };
        _music.Finished += () => { if (!LoopSongs && _tune < 0) SongFinished?.Invoke(); };
        AddChild(_music);

        var gen = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 0.1f };
        _static = new AudioStreamPlayer { Stream = gen, Bus = Bus, VolumeDb = -80f };
        AddChild(_static);
    }

    /// <summary>Creates the radio bus: a band-pass, mostly mono, a lightly overdriven little speaker
    /// (2026-10-05: half the old effect: an octave wider each way, half the drive, half mono).</summary>
    static void EnsureBus()
    {
        if (AudioServer.GetBusIndex(BusName) >= 0) return;
        AudioServer.AddBus();
        int i = AudioServer.BusCount - 1;
        AudioServer.SetBusName(i, BusName);
        AudioServer.SetBusSend(i, "Master");
        AudioServer.AddBusEffect(i, new AudioEffectStereoEnhance { PanPullout = 0.5f });         // half way to mono
        AudioServer.AddBusEffect(i, new AudioEffectHighPassFilter { CutoffHz = 190f, Resonance = 0.6f });
        AudioServer.AddBusEffect(i, new AudioEffectLowPassFilter { CutoffHz = 6000f, Resonance = 0.9f });
        AudioServer.AddBusEffect(i, new AudioEffectDistortion
        {
            Mode = AudioEffectDistortion.ModeEnum.Overdrive,
            PreGain = 3f,
            Drive = 0.2f,
            KeepHfHz = 7000f,
            PostGain = -2.5f,
        });
        // A second, gentler low-pass after the distortion takes the fizz off, like a paper cone.
        AudioServer.AddBusEffect(i, new AudioEffectLowPassFilter { CutoffHz = 6800f, Resonance = 0.5f });
    }

    /// <summary>The title's bus: just the top and bottom trimmed a little, like an old record player.</summary>
    static void EnsureSubtleBus()
    {
        if (AudioServer.GetBusIndex(SubtleBusName) >= 0) return;
        AudioServer.AddBus();
        int i = AudioServer.BusCount - 1;
        AudioServer.SetBusName(i, SubtleBusName);
        AudioServer.SetBusSend(i, "Master");
        AudioServer.AddBusEffect(i, new AudioEffectHighPassFilter { CutoffHz = 70f, Resonance = 0.5f });
        AudioServer.AddBusEffect(i, new AudioEffectLowPassFilter { CutoffHz = 11000f, Resonance = 0.5f });
    }

    public void Play(float from = 0f, bool immediate = false)
    {
        _fadeTarget = 1f;
        if (immediate)
        {
            _fade = 1f;
            // Playback runs on the audio thread even while world construction blocks _Process.
            _music.VolumeDb = VolumeDb;
            _static.VolumeDb = VolumeDb;
        }
        if (!_music.Playing && _music.Stream != null) _music.Play(from);
        StartStatic();
    }

    void StartStatic()
    {
        if (!_static.Playing)
        {
            _static.Play();
            _pb = _static.GetStreamPlayback() as AudioStreamGeneratorPlayback;
        }
    }

    public void Stop() => _fadeTarget = 0f;

    /// <summary>
    /// A second player of the same song from the same second, under a node that outlives the scene (the track
    /// loading card), so the music carries on while the world is rebuilt. Use CutNow on this one afterwards.
    /// </summary>
    public RadioMusic Continuation(Node parent)
    {
        var copy = new RadioMusic { TrackPath = "", Subtle = Subtle, VolumeDb = VolumeDb, LoopSongs = LoopSongs };
        parent.AddChild(copy);
        float at = _music != null && _music.Playing ? (float)_music.GetPlaybackPosition() : 0f;
        copy._music.Stream = _music?.Stream;
        copy.Play(at, immediate: true);
        return copy;
    }

    /// <summary>Silent at once (the music was handed over to a continuation).</summary>
    public void CutNow()
    {
        _fade = _fadeTarget = 0f;
        _tune = -1f;
        _music?.Stop();
        _static?.Stop();
        _pb = null;
    }

    /// <summary>Move the current track to a precise point without restarting the radio fade.</summary>
    public void Seek(float seconds)
    {
        if (_music?.Stream != null && _music.Playing) _music.Seek(Math.Max(0, seconds));
    }

    /// <summary>Change station: the current song sweeps away into static and a falling whistle,
    /// the new one comes up out of it from its start. From silence it just fades up from static.</summary>
    public void Tune(AudioStream stream, string name)
    {
        if (stream is AudioStreamMP3 mp3) mp3.Loop = LoopSongs;
        _pending = stream;
        _pendingName = name;
        bool silent = _fade <= 0.01f || !_music.Playing;
        _tune = silent ? 0.45f : 0f;      // from silence: straight into the static, song comes up at once
        _fadeTarget = 1f;
        if (silent) _fade = 1f;
        StartStatic();
    }

    /// <summary>Click off: a short burst of static, then silence.</summary>
    public void PowerOff()
    {
        _offBurst = 0.25f;
        _tune = -1f;
        _fadeTarget = 0f;
    }

    /// <summary>Pause everything (e.g. while the title screen has its own music) and resume later.</summary>
    public void Suspend(bool on)
    {
        if (_suspended == on) return;
        _suspended = on;
        _music.StreamPaused = on;
        _static.StreamPaused = on;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (_suspended) return;
        _offBurst -= dt;
        _fade = Mathf.MoveToward(_fade, _fadeTarget, dt / (_fadeTarget > 0 ? 1.2f : (_offBurst > -1f ? 0.15f : 0.8f)));
        if (_fade <= 0f && _fadeTarget <= 0f && _offBurst <= 0f)
        {
            if (_music.Playing) _music.Stop();
            if (_static.Playing) _static.Stop();
            _pb = null;
            return;
        }

        UpdateReception(dt);

        // "Bad" 0..1: how deep in a reception drop-out we are.
        float bad = _episodeLeft > 0 ? _episodeDepth * Envelope() : 0f;
        float tuneGain = 1f;
        if (_tune >= 0f)
        {
            float before = _tune;
            _tune += dt / TuneTime;
            // Static peaks in the middle of the sweep, the whistle falls through the band.
            bad = Math.Max(bad, 1f - MathF.Abs(_tune - 0.5f) * 2f);
            _whistleFreq = Mathf.Lerp(3200f, 450f, Math.Clamp(_tune, 0f, 1f));
            tuneGain = _tune < 0.5f ? 1f - _tune * 2f : Math.Min(1f, (_tune - 0.5f) * 2f);
            if (before < 0.5f && _tune >= 0.5f && _pending != null)
            {
                _music.Stream = _pending;
                _music.Play();
                SongStarted?.Invoke(_pendingName);
                _pending = null;
            }
            if (_tune >= 1f) _tune = -1f;
        }
        if (_offBurst > 0f) bad = 1f;
        float radar = UpdateInterference(dt);
        float musicLevel = _fade * (1f - 0.85f * bad) * _flutter * tuneGain * radar;
        _music.VolumeDb = Mathf.LinearToDb(Math.Max(musicLevel, 0.0001f)) + VolumeDb;
        _static.VolumeDb = Mathf.LinearToDb(Math.Max(Math.Max(_fade, _offBurst > 0 ? 1f : 0f), 0.0001f)) + VolumeDb;

        FillStatic(bad);
    }

    /// <summary>Radar interference on the station: slow fading that deepens with Interference,
    /// short drop-outs when strong and the buzz sweep pushing the music under. Returns the music gain.</summary>
    float UpdateInterference(float dt)
    {
        float I = Interference;
        _buzz += (Sweep - _buzz) * Math.Min(1f, dt * 30f);
        if (I <= 0.001f) { _intFade = 1f; _dropLeft = 0f; return 1f; }
        _intFadeTimer -= dt;
        if (_intFadeTimer <= 0)
        {
            _intFadeTimer = 0.15f + (float)_rng.NextDouble() * 0.5f;
            _intFadeTarget = 1f - I * (0.2f + 0.6f * (float)_rng.NextDouble());
        }
        _intFade += (_intFadeTarget - _intFade) * Math.Min(1f, dt * 6f);
        // Drop-outs: the station vanishes into the noise for a moment, more often when close.
        _dropLeft -= dt;
        _nextDrop -= dt * I;
        if (_nextDrop <= 0 && I > 0.35f)
        {
            _dropLeft = 0.1f + (float)_rng.NextDouble() * 0.4f * I;
            _nextDrop = 0.6f + (float)_rng.NextDouble() * 1.8f;
        }
        float gain = _intFade * (_dropLeft > 0 ? 0.08f : 1f);
        return gain * (1f - 0.9f * _buzz);
    }

    /// <summary>0→1→0 over an episode, quick fade in and out.</summary>
    float Envelope()
    {
        float t = 1f - _episodeLeft / _episodeLen;
        return Math.Clamp(Math.Min(t / 0.2f, (1f - t) / 0.25f), 0f, 1f);
    }

    void UpdateReception(float dt)
    {
        if (_episodeLeft > 0)
        {
            _episodeLeft -= dt;
            // Signal flutters while fading: random level jumps several times a second.
            _flutterTimer -= dt;
            if (_flutterTimer <= 0)
            {
                _flutterTimer = 0.05f + (float)_rng.NextDouble() * 0.12f;
                _flutterTarget = 0.35f + (float)_rng.NextDouble() * 0.65f;
            }
        }
        else
        {
            _flutterTarget = 1f;
            if (!Subtle) _nextEpisode -= dt;      // the title music never drops out
            if (_nextEpisode <= 0)
            {
                _episodeLen = _episodeLeft = 0.5f + (float)_rng.NextDouble() * 2f;
                _episodeDepth = (0.65f + (float)_rng.NextDouble() * 0.35f) * 0.5f;   // half as deep as it was
                _nextEpisode = 6f + (float)_rng.NextDouble() * 9f;
                _whistleFreq = 900f + (float)_rng.NextDouble() * 2200f;
                if (OS.HasEnvironment("RADIODBG"))
                    GD.Print($"radio drop-out at {Time.GetTicksMsec() / 1000f:F1} s: {_episodeLen:F1} s, depth {_episodeDepth:F2}");
            }
        }
        _flutter += (_flutterTarget - _flutter) * Math.Min(1f, dt * 25f);
    }

    void FillStatic(float bad)
    {
        if (_pb == null) return;
        int frames = _pb.GetFramesAvailable();
        float I = Interference, buzzAmp = _buzz * (0.3f + 0.7f * I);
        float crackleChance = (0.00006f + 0.004f * bad + 0.003f * I) * (Subtle ? 0.25f : 1f);
        float staticGain = Subtle ? 0.2f : 1f;
        for (int i = 0; i < frames; i++)
        {
            float n = (float)_rng.NextDouble() * 2f - 1f;

            // Hiss: always a little, a roar of static when reception drops.
            _hissLp += (n - _hissLp) * 0.5f;
            float hiss = _hissLp * (0.0175f + 0.35f * bad + 0.3f * I * (_dropLeft > 0 ? 1.6f : 1f));

            // Crackle: sparse sharp pops and short bursts.
            if (_rng.NextDouble() < crackleChance) _crackle = 0.4f + (float)_rng.NextDouble() * 0.6f;
            float crackle = 0f;
            if (_crackle > 0.002f)
            {
                _crackleLp += (n - _crackleLp) * 0.7f;
                crackle = _crackleLp * _crackle;
                _crackle *= 0.985f;
            }

            // Heterodyne whistle drifting while the signal is bad.
            float whistle = 0f;
            if (bad > 0.01f)
            {
                _whistleFreq += ((float)_rng.NextDouble() - 0.5f) * 0.8f;
                _whistlePhase += _whistleFreq / MixRate;
                whistle = (float)Math.Sin(_whistlePhase * Math.Tau) * 0.06f * bad;
            }

            // Radar: a heterodyne whistle warbling ~6 times a second, and the buzz burst when the
            // beam passes: a rasping ~220 Hz pulse train through a burst of noise.
            float radar = 0f;
            if (I > 0.001f)
            {
                _warblePhase += 6.3 / MixRate;
                double het = 1350.0 + 260.0 * Math.Sin(_warblePhase * Math.Tau) + 90.0 * Math.Sin(_warblePhase * 0.37 * Math.Tau);
                _hetPhase += het / MixRate;
                radar += (float)Math.Sin(_hetPhase * Math.Tau) * 0.07f * I;
            }
            if (buzzAmp > 0.001f)
            {
                _buzzPhase += 220.0 / MixRate;
                float p = (float)(_buzzPhase % 1.0);
                // Gated ~28 times a second: a tearing "brrrt" rather than a steady tone.
                float gate = (int)(_buzzPhase / 8.0) % 2 == 0 ? 1f : 0.45f;
                radar += ((p < 0.18f ? 1f : -0.22f) * 0.8f + n * 0.4f) * buzzAmp * gate;
            }

            float s = Math.Clamp((hiss + crackle) * staticGain + whistle + radar, -1f, 1f);
            _pb.PushFrame(new Vector2(s, s));
        }
    }
}
