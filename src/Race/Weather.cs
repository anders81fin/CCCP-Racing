using System;
using CCCPRacing.Audio;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The rainy autumn of a track with weather (TrackDef.Weather "rain"): the weather is a function of
/// the time of day in laps (DayNight.Clock), so it changes lap by lap round the day and the record
/// lap's replay has the weather it was driven in. Rain (streaks round the car, a hiss), a wet road
/// that lags the rain and never quite dries (Surfaces.Wetness: grip), an overcast and fog the
/// DayNight look takes in (OvercastAt, FogAt), and at night the storm: lightning and thunder.
/// Tests: env WEATHERDBG.
/// </summary>
public partial class Weather : Node3D
{
    /// <summary>Per lap of the day (DayNight.CycleLaps): rain 0..1, fog 0..1, a thunderstorm.
    /// Day: drizzle, rain, a downpour at sunset, easing; dusk dry, then drizzle; night: the storm, then
    /// rain and drizzle; dawn: fog.</summary>
    static readonly (float rain, float fog, bool storm)[] Laps =
    {
        (0.35f, 0.10f, false), (0.75f, 0.15f, false), (1.00f, 0.20f, false), (0.30f, 0.10f, false),
        (0.00f, 0.15f, false), (0.40f, 0.20f, false), (1.00f, 0.25f, true),  (0.90f, 0.20f, true),
        (0.50f, 0.15f, false), (0.20f, 0.20f, false), (0.00f, 0.80f, false), (0.15f, 0.50f, false),
    };

    static float Cycle(float laps) => ((laps % DayNight.CycleLaps) + DayNight.CycleLaps) % DayNight.CycleLaps;

    /// <summary>A lap's value at its middle, smoothly between the middles.</summary>
    static float Blend(float clock, Func<int, float> of)
    {
        float c = Cycle(clock) - 0.5f;
        int i = (int)MathF.Floor(c);
        float t = c - i;
        t = t * t * (3f - 2f * t);
        int n = Laps.Length;
        return Mathf.Lerp(of(((i % n) + n) % n), of(((i + 1) % n + n) % n), t);
    }

    public static float RainAt(float clock) => Blend(clock, i => Laps[i].rain);
    public static float FogAt(float clock) => Blend(clock, i => Laps[i].fog);
    public static bool StormAt(float clock) => Blend(clock, i => Laps[i].storm ? 1f : 0f) > 0.5f;
    /// <summary>The road: it lags the rain (half a lap, a lap) and an autumn road never quite dries.</summary>
    public static float WetAt(float clock) => Math.Max(0.25f, Math.Max(RainAt(clock), Math.Max(RainAt(clock - 0.5f) * 0.85f, RainAt(clock - 1f) * 0.6f)));
    /// <summary>The cloud: always grey in the zone's autumn, darker in the rain.</summary>
    public static float OvercastAt(float clock) => Math.Clamp(0.45f + 0.55f * RainAt(clock), 0f, 1f);

    /// <summary>What the rain falls round (the car's visual).</summary>
    public Node3D Target;

    // Three layers of rain, on as it gets heavier: a drizzle, the rain, the downpour.
    readonly CpuParticles3D[] _rain = new CpuParticles3D[3];
    static readonly float[] LayerFrom = { 0.03f, 0.4f, 0.75f };
    AudioStreamPlayer _hiss;
    AudioStreamGeneratorPlayback _pb;
    DirectionalLight3D _flash;
    readonly RandomNumberGenerator _rng = new() { Seed = 1986 };
    float _level, _rainNow, _flashT = -1, _thunderIn = -1, _thunder, _nextStrike = 5f, _lp, _lp2, _rumble, _dbg;
    const float MixRate = 22050f;

    public static Weather Create(Node3D target)
    {
        var w = new Weather { Name = "Weather", Target = target };
        var streak = new QuadMesh { Size = new Vector2(0.03f, 0.75f) };
        streak.Material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.78f, 0.82f, 0.88f, 0.55f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY,
        };
        for (int k = 0; k < 3; k++)
        {
            w._rain[k] = new CpuParticles3D
            {
                Name = "Rain" + k,
                Mesh = streak,
                Amount = 900,
                Lifetime = 1.1f,
                Preprocess = 1.1f,
                LocalCoords = false,
                EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
                EmissionBoxExtents = new Vector3(30f, 1f, 24f),
                Direction = new Vector3(0.12f, -1f, 0.05f),
                Spread = 4f,
                InitialVelocityMin = 15f,
                InitialVelocityMax = 18f,
                Gravity = new Vector3(0, -4f, 0),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Emitting = false,
            };
            w.AddChild(w._rain[k]);
        }
        w._hiss = new AudioStreamPlayer { Stream = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 0.1f }, VolumeDb = -4f, Bus = AudioMix.BusFor(AudioMix.Diesel) };
        w.AddChild(w._hiss);
        // The lightning: a cold white light from straight above, off but for a flash.
        w._flash = new DirectionalLight3D { LightColor = new Color(0.82f, 0.88f, 1f), LightEnergy = 0f, ShadowEnabled = false, Visible = false };
        w._flash.RotationDegrees = new Vector3(-80f, 20f, 0);
        w.AddChild(w._flash);
        return w;
    }

    public override void _Ready()
    {
        _hiss.Play();
        _pb = _hiss.GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }

    /// <summary>Per frame: the weather at this time of day (laps). The road's wetness goes to the tyres.</summary>
    public void Tick(float clock, float dt)
    {
        _rainNow = RainAt(clock);
        Surfaces.Wetness = _wet = WetAt(clock);
        for (int k = 0; k < 3; k++)
        {
            if (Target != null) _rain[k].GlobalPosition = Target.GlobalPosition + Vector3.Up * 14f;
            _rain[k].Emitting = _rainNow > LayerFrom[k];
        }

        // The storm: a strike now and then, the thunder a moment after it.
        if (StormAt(clock))
        {
            _nextStrike -= dt;
            if (_nextStrike <= 0)
            {
                _nextStrike = _rng.RandfRange(6f, 14f);
                _flashT = 0;
                _thunderIn = _rng.RandfRange(0.4f, 2.2f);
                if (OS.HasEnvironment("WEATHERDBG")) GD.Print($"weather: lightning at clock {clock:F2}");
            }
        }
        if (_flashT >= 0)
        {
            // Two or three flickers over a third of a second.
            _flashT += dt;
            float f = _flashT < 0.06f ? 1f : _flashT < 0.11f ? 0.15f : _flashT < 0.2f ? 0.8f : Math.Max(0f, 0.5f - (_flashT - 0.2f) * 3f);
            _flash.Visible = f > 0.01f;
            _flash.LightEnergy = 2.4f * f;
            if (_flashT > 0.4f) { _flashT = -1; _flash.Visible = false; }
        }
        if (_thunderIn > 0 && (_thunderIn -= dt) <= 0) _thunder = 1f;
        _dbg -= dt;
        if (_dbg <= 0 && OS.HasEnvironment("WEATHERDBG"))
        {
            _dbg = 5f;
            GD.Print($"weather: clock {clock:F2}, rain {_rainNow:F2}, fog {FogAt(clock):F2}, wet {Surfaces.Wetness:F2}, overcast {OvercastAt(clock):F2}{(StormAt(clock) ? ", storm" : "")}");
        }
    }

    public override void _Process(double delta)
    {
        if (_pb == null) return;
        int frames = _pb.GetFramesAvailable();
        for (int i = 0; i < frames; i++)
        {
            _level += (_rainNow - _level) * 0.00005f;
            // The rain: white noise, a bright and a darker band, louder the harder it falls.
            float n = _rng.Randf() * 2f - 1f;
            _lp += (n - _lp) * 0.35f;
            _lp2 += (n - _lp2) * 0.06f;
            float hiss = (_lp - _lp2) * 0.6f + _lp2 * 0.5f;
            // The thunder: a deep rumble that rolls away over a few seconds.
            float thunder = 0;
            if (_thunder > 0.001f)
            {
                _rumble += (n - _rumble) * 0.01f;
                thunder = _rumble * 6f * _thunder * (0.7f + 0.3f * MathF.Sin(_thunder * 40f));
                _thunder -= 1f / (3.2f * MixRate);
            }
            float s = Math.Clamp(hiss * 0.22f * _level + thunder, -1f, 1f);
            _pb.PushFrame(new Vector2(s, s));
        }
    }

    /// <summary>The weathers in the tree (the replay viewer's second world has its own beside the live one) and the wetness this one last set.</summary>
    static readonly System.Collections.Generic.List<Weather> InTree = new();
    float _wet;

    public override void _EnterTree() => InTree.Add(this);

    public override void _ExitTree()
    {
        using var driverLock = AudioMix.LockDriver();
        using var generator = GodotObject.IsInstanceValid(_hiss) ? _hiss.Stream as AudioStreamGenerator : null;
        InTree.Remove(this);
        // The next track is dry; a weather that is still there (the live one, while a viewer's ends) keeps the road as it left it.
        Surfaces.Wetness = InTree.Count > 0 ? InTree[^1]._wet : 0;
        if (GodotObject.IsInstanceValid(_hiss)) { _hiss.StreamPaused = false; _hiss.Stop(); _hiss.Stream = null; }
        if (GodotObject.IsInstanceValid(_pb)) _pb.Stop(); _pb?.Dispose();
        _pb = null;
    }
}
