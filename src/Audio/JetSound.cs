using System;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>
/// Positional turbojet, loud enough to carry across most of the map: a deep rumbling roar, the
/// jet blast and a turbine whine; the afterburner lights with a whump and adds a ragged, popping
/// crackle. Distance is shaped here rather than by Godot's filter: far away only a muffled rumble
/// is left, close by the full roar opens up with a tearing swoosh as it passes, and the whine and
/// roar shift with a cheap doppler from the closing speed.
/// </summary>
public partial class JetSound : AudioStreamPlayer3D
{
    const float MixRate = 22050f;
    const float SpeedOfSound = 343f;
    public float Level;        // 0 idle .. 1 full thrust
    public float Afterburner;  // 0..1
    AudioStreamGeneratorPlayback _pb;
    double _whine;
    float _rumble, _roar1, _roar2, _blast, _hissLp, _crack, _crackLp, _whump, _whumpLp, _out1, _out2;
    float _wobble, _wobbleT;
    float _dist = -1f, _closing, _prevAb;
    // Per-block parameters, eased per sample so nothing zips.
    float _near, _cut = 0.2f, _doppler = 1f, _level, _ab;
    readonly Random _rng = new(1959);
    internal static readonly bool Debug = OS.GetEnvironment("CCCP_JET_DEBUG") != "";
    float _dbgT;

    /// <summary>Metres from the listener (debug / tests).</summary>
    public float Distance => _dist;

    public override void _ExitTree()
    {
        using var driverLock = AudioMix.LockDriver();
        using var generator = Stream as AudioStreamGenerator;
        StreamPaused = false; Stop();
        Stream = null;
        if (GodotObject.IsInstanceValid(_pb)) _pb.Stop(); _pb?.Dispose();
        _pb = null;
    }

    public override void _Ready()
    {
        Stream = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 0.2f };
        // Inverse-distance: 0 dB (capped) within 40 m, -11 dB at 150 m, -20 dB at 400 m; heard to ~1 km.
        UnitSize = 40f;
        MaxDistance = 1000f;
        MaxDb = 0f;
        VolumeDb = 0f;
        AttenuationFilterDb = 0f;   // distance filtering is done in the synth below
        Bus = Debug ? "Jet" : AudioMix.BusFor(AudioMix.Jet);   // test: record the jets alone with --record-bus=Jet
        Play();
        _pb = GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    Vector3 Ear()
    {
        var vp = GetViewport();
        var l = vp?.GetAudioListener3D();
        if (l != null) return l.GlobalPosition;
        return vp?.GetCamera3D()?.GlobalPosition ?? GlobalPosition;
    }

    public override void _Process(double delta)
    {
        if (_pb == null) return;
        float dt = Math.Max(1e-4f, (float)delta);
        float d = GlobalPosition.DistanceTo(Ear());
        if (_dist >= 0) _closing += ((_dist - d) / dt - _closing) * Math.Min(1f, dt * 6f);
        _dist = d;
        if (Afterburner > 0.5f && _prevAb <= 0.5f) _whump = 1f;   // afterburner lights
        _prevAb = Afterburner;
        if (Debug && (_dbgT += dt) >= 0.25f)
        {
            _dbgT = 0;
            GD.Print($"JET t={Time.GetTicksMsec() / 1000f:F2} d={d:F0} closing={_closing:F0} level={Level:F2} ab={Afterburner:F2}");
        }

        // Closeness 0..1 opens the filters; beyond ~300 m it is a low rumble only.
        float near = Math.Clamp(1f - (d - 20f) / 280f, 0f, 1f);
        near *= near;
        float closing = Math.Clamp(_closing, -120f, 120f);
        float doppler = SpeedOfSound / (SpeedOfSound - closing);
        // Output low-pass: ~9 kHz at 20 m down to ~550 Hz far out (kept above what laptop
        // speakers drop, so the distant rumble is still heard on them).
        float fc = Math.Clamp(9000f * MathF.Pow(20f / Math.Max(20f, d), 1.1f), 550f, 9000f);
        float cut = 1f - MathF.Exp(-MathF.Tau * fc / MixRate);

        int frames = _pb.GetFramesAvailable();
        float k = frames > 0 ? 1f / frames : 0;
        for (int i = 0; i < frames; i++)
        {
            _near += (near - _near) * k;
            _cut += (cut - _cut) * k;
            _doppler += (doppler - _doppler) * k;
            _level += (Level - _level) * 0.0005f;
            _ab += (Afterburner - _ab) * 0.0008f;

            float n = (float)_rng.NextDouble() * 2f - 1f;
            // Slow random wobble: a big engine never drones evenly.
            if ((_wobbleT -= 1f / MixRate) <= 0) { _wobbleT = 0.05f; _wobble = (float)_rng.NextDouble(); }
            float wob = 0.85f + 0.3f * _wobble;

            // Deep rumble (~120 Hz) and the roar body (~250 Hz, doppler-shifted).
            _rumble += (n - _rumble) * 0.035f;
            float rc = 0.07f * _doppler;
            _roar1 += (n - _roar1) * rc;
            _roar2 += (_roar1 - _roar2) * rc;
            // Jet blast: broad noise, strongest at full thrust.
            _blast += (n - _blast) * (0.3f * _doppler);
            // Swoosh: bright tearing hiss, only close by.
            _hissLp += (n - _hissLp) * 0.6f;
            float hiss = (n - _hissLp) * _near * _near;

            _whine += (1700 + 1900 * _level) * _doppler / MixRate;
            float whine = ((float)Math.Sin(_whine * Math.Tau) + 0.4f * (float)Math.Sin(_whine * 2.02 * Math.Tau))
                          * 0.05f * (0.3f + _level) * (0.2f + 0.8f * _near);

            // Afterburner: popping crackle plus a lower, heavier roar.
            if (_ab > 0.1f && _rng.Next(160) == 0) _crack = 0.6f + 0.4f * (float)_rng.NextDouble();
            _crackLp += (n * _crack - _crackLp) * 0.5f;
            float crack = _crackLp * 1.3f * _ab;
            _crack *= 0.994f;
            float whump = 0;
            if (_whump > 0.001f)
            {
                _whumpLp += (n - _whumpLp) * 0.04f;
                whump = _whumpLp * 9f * _whump;
                _whump *= 0.9997f;
            }

            float s = _rumble * 4f * (0.4f + 0.6f * _level)
                    + _roar2 * 5f * (0.2f + _level) * wob
                    + _blast * (0.2f + 0.45f * _level * _level + 0.5f * _ab) * wob
                    + hiss * (0.25f + 0.4f * _level)
                    + whine + crack + whump
                    + _roar2 * 3f * _ab;
            // Distance: two poles so far away is properly muffled.
            _out1 += (s - _out1) * _cut;
            _out2 += (_out1 - _out2) * _cut;
            // Idle (taxiing) is far quieter than full thrust.
            float o = MathF.Tanh(_out2 * 0.9f * (0.35f + 0.65f * _level * MathF.Sqrt(_level)));
            _pb.PushFrame(new Vector2(o, o));
        }
    }
}

/// <summary>One-shot positional explosion: a deep boom rolling off, crackle of burning debris.
/// The sound arrives late from afar (speed of sound) and muffled with distance; a big one
/// (crashing jet) rolls on like thunder off the hills and carries across the whole map. A small
/// one (tank cannon, shell) falls off steeply: a bang at 20 m, a dull thud at 150 m, a faint
/// rumble at 400 m. Frees itself when done.</summary>
public partial class ExplosionSound : AudioStreamPlayer3D
{
    const float MixRate = 22050f;
    /// <summary>Volume in dB: at 180 m for a big one (crashing jet 2), at 10 m for a small one
    /// (tank cannon 3, shell 1: about -3 / -5 dBFS peak at 20 m).</summary>
    public float Loudness = 4f;
    /// <summary>Heard up to this far (m); a big boom also gets a long rolling tail.</summary>
    public float Reach = 600f;
    /// <summary>Optional reference distance for heavy weapons, independent of explosion size.</summary>
    public float ReferenceDistance;
    public bool Cannon;
    AudioStreamGeneratorPlayback _pb;
    float _t, _lp, _lp2, _roll, _roll2, _out1, _out2, _cut = 1f, _delay, _air = 1f;
    double _boom;
    bool Big => Reach > 800f;
    readonly Random _rng = new();

    public override void _ExitTree()
    {
        using var driverLock = AudioMix.LockDriver();
        using var generator = Stream as AudioStreamGenerator;
        StreamPaused = false; Stop();
        Stream = null;
        if (GodotObject.IsInstanceValid(_pb)) _pb.Stop(); _pb?.Dispose();
        _pb = null;
    }

    public override void _Ready()
    {
        Stream = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 0.2f };
        // A big one keeps its level far out but is capped close by, so it cannot clip the mix.
        // A small one: inverse distance from 10 m, never above 0 dB, plus air loss past 100 m.
        UnitSize = ReferenceDistance > 0 ? ReferenceDistance : Big ? 180f : 10f;
        MaxDistance = Reach;
        // A tank cannon next to the car must out-shout the idling engine voices (+3 dB cap), so it may
        // rise above 0 dB inside its reference distance; the master limiter catches the peak.
        MaxDb = Cannon ? 16f : 0f;
        VolumeDb = Loudness;
        AttenuationFilterDb = 0f;
        if (JetSound.Debug && Big) Bus = "Jet";
        else Bus = AudioMix.BusFor(Big ? AudioMix.Jet : AudioMix.Boom);
        var vp = GetViewport();
        Vector3? ear = vp?.GetAudioListener3D()?.GlobalPosition ?? vp?.GetCamera3D()?.GlobalPosition;
        if (ear is Vector3 e)
        {
            float d = GlobalPosition.DistanceTo(e);
            _delay = Math.Max(0f, d - 30f) / 343f;
            if (JetSound.Debug && Big) GD.Print($"BOOM t={Time.GetTicksMsec() / 1000f:F2} d={d:F0} delay={_delay:F2}");
            float fc = Math.Clamp(8000f * MathF.Pow(30f / Math.Max(30f, d), 1.2f), 450f, 8000f);
            if (!Big) _air = Mathf.DbToLinear(-6f * Math.Clamp((d - 100f) / 200f, 0f, 2f));
            if (AudioMix.Debug) GD.Print($"BOOM t={Time.GetTicksMsec() / 1000f:F2} d={d:F0} loud={Loudness:F0} big={Big} db={(Big ? 0 : Loudness + 20f * MathF.Log10(10f / Math.Max(d, 1f)) + Mathf.LinearToDb(_air)):F1}");
            _cut = 1f - MathF.Exp(-MathF.Tau * fc / MixRate);
        }
        Play();
        _pb = GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    public override void _Process(double delta)
    {
        if (_pb == null) return;
        int frames = _pb.GetFramesAvailable();
        for (int i = 0; i < frames; i++)
        {
            if (_delay > 0) { _delay -= 1f / MixRate; _pb.PushFrame(Vector2.Zero); continue; }
            _t += 1f / MixRate;
            float n = (float)_rng.NextDouble() * 2f - 1f;
            _lp += (n - _lp) * 0.03f;
            _lp2 += (n - _lp2) * 0.3f;
            _boom += (55.0 - 25.0 * Math.Min(1.0, _t)) / MixRate;
            float env = MathF.Exp(-_t * 2.2f);
            float crackle = _rng.Next(700) == 0 ? 1f : 0f;
            float s = _lp * 6f * env + (float)Math.Sin(_boom * Math.Tau) * 0.8f * MathF.Exp(-_t * 3f)
                    + _lp2 * 0.5f * MathF.Exp(-_t * 8f) + crackle * n * 0.6f * MathF.Exp(-_t * 0.6f);
            if (Cannon)
            {
                // Heavy gun: a blinding crack, a chest-hitting 40-110 Hz thump and a long ragged boom with the field echo.
                float attack = n * 2.6f * MathF.Exp(-_t * 190f);
                float thump = MathF.Sin(MathF.Tau * (110f * _t - 180f * _t * _t * (_t < .3f ? 1f : 0f))) * 2.2f * MathF.Exp(-_t * 9f);
                float sub = MathF.Sin(MathF.Tau * (42f * _t - 12f * _t * _t)) * 2.6f * MathF.Exp(-_t * 3.2f);
                float body = _lp2 * 1.6f * MathF.Exp(-_t * 14f) + _lp * 4.5f * MathF.Exp(-_t * 4.2f);
                float echoAge = Math.Max(0, _t - .13f);
                float echo = _t > .13f ? _lp * 4f * MathF.Exp(-echoAge * 2.4f) : 0;
                s = attack + thump + sub + body + echo;
            }
            if (Big)
            {
                // Rolling thunder: a slow, swelling and ebbing low rumble after the blast.
                _roll += (n - _roll) * 0.04f;
                _roll2 += (_roll - _roll2) * 0.04f;
                float swell = MathF.Min(1f, _t * 3f) * MathF.Exp(-_t * 0.7f) * (0.7f + 0.3f * MathF.Sin(_t * 5.3f) * MathF.Sin(_t * 2.1f));
                s += _roll2 * 9f * swell;
            }
            _out1 += (s - _out1) * _cut;
            _out2 += (_out1 - _out2) * _cut;
            s = (Cannon ? MathF.Tanh(_out2 * 1.5f) * .96f : Math.Clamp(_out2, -1f, 1f)) * _air;
            _pb.PushFrame(new Vector2(s, s));
        }
        if (_t > (Big ? 6f : Cannon ? 6f : 4f)) QueueFree();
    }
}
