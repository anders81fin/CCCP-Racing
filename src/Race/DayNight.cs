using System;
using System.Collections.Generic;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// Day and night in the SOCIALIST RALLY, STAKHANOV LAPS and LAN races, by the laps driven in the run
/// (void laps too; a restart request keeps the count): laps 1-2 day, 3-4 sunset (the sun sinks and
/// warms), 5-6 dusk (headlights on), 7-10 night, 11-12 dawn, and from lap 13 the next day the same way. Within a lap the light follows the lap's progress, so
/// nothing pops at the line. The terrain swaps its unshaded day material for a lit one after the
/// day (TerrainMesh.NightMaterial), unshaded decals and signs dim with the light, glowing parts
/// (NightGlow) and a few real lights come on: the car's lamps (CarLights), bus, Kozlik and gulag truck lamps,
/// the radar's searchlight, a floodlight over the KGB at the line, afterburners and muzzle flashes.
/// The climate eases the rally plan's lap target per phase (ClimateAllowance). Inactive (the title,
/// the driving school) everything is exactly the plain day.
/// </summary>
public partial class DayNight : Node3D
{
    public enum Phase { Day, Sunset, Dusk, Night, Dawn }

    /// <summary>Laps in one day: 2 day, 2 sunset, 2 dusk, 4 night, 2 dawn, then the next day.</summary>
    public const int CycleLaps = 12;

    /// <summary>The phase of a lap (1-based: the first lap of the run is 1); the day comes round every CycleLaps.</summary>
    public static Phase PhaseForLap(int lap)
    {
        int l = ((lap - 1) % CycleLaps + CycleLaps) % CycleLaps + 1;
        return l <= 2 ? Phase.Day : l <= 4 ? Phase.Sunset : l <= 6 ? Phase.Dusk : l <= 10 ? Phase.Night : Phase.Dawn;
    }

    /// <summary>First lap of a phase in the first day (1-based).</summary>
    public static int FirstLap(Phase p) => p switch { Phase.Sunset => 3, Phase.Dusk => 5, Phase.Night => 7, Phase.Dawn => 11, _ => 1 };

    /// <summary>Seconds the climate adds to the lap target for a lap driven in this phase on the first track (Kolkhoz) with diesel: night +2, dusk +1 (the user, 2026-11-11), sunset and dawn in the old ratio.
    /// The rally scales it by the track's lap time (tracks/ID.json norms "climate") and the fuel (RaceScene.ClimateScale).</summary>
    public static float AllowanceFor(Phase p) => p switch { Phase.Sunset => 0.5f, Phase.Dusk => 1f, Phase.Night => 2f, Phase.Dawn => 0.75f, _ => 0f };

    /// <summary>A lap started at dusk or at night is a night lap (the board marks it).</summary>
    public static bool IsNight(Phase p) => p is Phase.Dusk or Phase.Night;

    /// <summary>The phase-change alert's first line where no norm is eased (Stakhanov laps, LAN).</summary>
    public static string Title(Phase p) => p switch
    {
        Phase.Sunset => "SUNSET", Phase.Dusk => "DUSK", Phase.Night => "NIGHT", Phase.Dawn => "DAWN", _ => "A NEW DAY",
    };

    /// <summary>The phase-change alert's second line.</summary>
    public static string Motto(Phase p) => p switch
    {
        Phase.Sunset => "THE SUN IS SETTING ACCORDING TO PLAN",
        Phase.Dusk => "DUSK. HEADLIGHTS ON, TOVARISH",
        Phase.Night => "THE NIGHT IS TEMPORARY. SO ARE YOU",
        Phase.Dawn => "DAWN OF A NEW FIVE-YEAR PLAN",
        _ => "ANOTHER DAY, ANOTHER NORM",
    };

    public static Phase? Parse(string s) => s.ToLowerInvariant() switch
    {
        "day" => Phase.Day, "sunset" => Phase.Sunset, "dusk" => Phase.Dusk, "night" => Phase.Night, "dawn" => Phase.Dawn, _ => null,
    };

    // ------------------------------------------------------------ the look

    /// <summary>Everything the light does at one moment. Sun: elevation and azimuth (degrees, 0 = the
    /// day sun's side, TerrainMesh.SunDir); at night the same light is the moon.</summary>
    public struct Look
    {
        public float SunElev, SunAz, SunEnergy, AmbientEnergy, LitMix;
        public Color SunColor, Ambient, Sky, Baked, Unlit;

        public static Look Lerp(in Look a, in Look b, float t) => new()
        {
            SunElev = Mathf.Lerp(a.SunElev, b.SunElev, t),
            SunAz = Mathf.Lerp(a.SunAz, b.SunAz, t),
            SunEnergy = Mathf.Lerp(a.SunEnergy, b.SunEnergy, t),
            AmbientEnergy = Mathf.Lerp(a.AmbientEnergy, b.AmbientEnergy, t),
            LitMix = Mathf.Lerp(a.LitMix, b.LitMix, t),
            SunColor = a.SunColor.Lerp(b.SunColor, t),
            Ambient = a.Ambient.Lerp(b.Ambient, t),
            Sky = a.Sky.Lerp(b.Sky, t),
            Baked = a.Baked.Lerp(b.Baked, t),
            Unlit = a.Unlit.Lerp(b.Unlit, t),
        };
    }

    /// <summary>The plain day as the race sets it up (RaceScene._Ready).</summary>
    public static readonly float DayElev = Mathf.RadToDeg(MathF.Asin(TerrainMesh.SunDir.Y));
    static readonly Look DayLook = new()
    {
        SunElev = DayElev, SunAz = 0, SunEnergy = 0.9f, SunColor = Colors.White,
        Ambient = new Color(0.62f, 0.6f, 0.6f), AmbientEnergy = 0.9f, Sky = new Color(0.62f, 0.64f, 0.62f),
        Baked = Colors.White, LitMix = 0, Unlit = Colors.White,
    };

    static Look L(float elev, float az, float energy, Color sun, Color ambient, float ambientEnergy, Color sky, Color baked, float lit, Color unlit) => new()
    {
        SunElev = elev, SunAz = az, SunEnergy = energy, SunColor = sun, Ambient = ambient, AmbientEnergy = ambientEnergy,
        Sky = sky, Baked = baked, LitMix = lit, Unlit = unlit,
    };

    /// <summary>Key looks by laps driven in the day (continuous: 0 is the start of lap 1, 3.5 halfway round
    /// lap 4; LookAt wraps every CycleLaps). The sun sets and the moon rises through zero energy, so the
    /// light swaps sides unseen; through the dawn the lit terrain hands back to the baked day.</summary>
    static readonly (float at, Look look)[] Keys =
    {
        (0f, DayLook),
        (2f, DayLook),
        (3f, L(24f, 8f, 0.88f, new Color(1f, 0.86f, 0.66f), new Color(0.66f, 0.57f, 0.52f), 0.85f, new Color(0.8f, 0.64f, 0.5f),
            new Color(0.78f, 0.66f, 0.52f), 0.3f, new Color(0.98f, 0.86f, 0.72f))),
        (4f, L(6f, 16f, 0.75f, new Color(1f, 0.52f, 0.26f), new Color(0.5f, 0.36f, 0.4f), 0.7f, new Color(0.72f, 0.4f, 0.32f),
            new Color(0.2f, 0.1f, 0.06f), 0.9f, new Color(0.74f, 0.5f, 0.42f))),
        (4.8f, L(0f, 20f, 0f, new Color(1f, 0.4f, 0.3f), new Color(0.26f, 0.26f, 0.44f), 0.8f, new Color(0.18f, 0.2f, 0.4f),
            Colors.Black, 1f, new Color(0.36f, 0.37f, 0.55f))),
        (4.82f, L(55f, 150f, 0f, new Color(0.6f, 0.68f, 1f), new Color(0.26f, 0.26f, 0.44f), 0.8f, new Color(0.18f, 0.2f, 0.4f),
            Colors.Black, 1f, new Color(0.36f, 0.37f, 0.55f))),
        (6f, L(55f, 150f, 0.24f, new Color(0.52f, 0.62f, 1f), new Color(0.1f, 0.12f, 0.34f), 1f, new Color(0.03f, 0.04f, 0.1f),
            Colors.Black, 1f, new Color(0.2f, 0.22f, 0.34f))),
        (10f, L(55f, 150f, 0.24f, new Color(0.52f, 0.62f, 1f), new Color(0.1f, 0.12f, 0.34f), 1f, new Color(0.03f, 0.04f, 0.1f),
            Colors.Black, 1f, new Color(0.2f, 0.22f, 0.34f))),
        (10.5f, L(40f, 150f, 0f, new Color(0.62f, 0.7f, 1f), new Color(0.24f, 0.2f, 0.34f), 0.9f, new Color(0.16f, 0.12f, 0.24f),
            Colors.Black, 1f, new Color(0.34f, 0.3f, 0.44f))),
        (10.52f, L(0f, 60f, 0f, new Color(1f, 0.5f, 0.55f), new Color(0.24f, 0.2f, 0.34f), 0.9f, new Color(0.16f, 0.12f, 0.24f),
            Colors.Black, 1f, new Color(0.34f, 0.3f, 0.44f))),
        (11.1f, L(9f, 50f, 0.75f, new Color(1f, 0.68f, 0.72f), new Color(0.66f, 0.52f, 0.62f), 1f, new Color(0.88f, 0.62f, 0.7f),
            Colors.Black, 1f, new Color(0.9f, 0.7f, 0.76f))),
        (11.6f, L(24f, 25f, 0.85f, new Color(1f, 0.9f, 0.84f), new Color(0.64f, 0.6f, 0.62f), 0.9f, new Color(0.74f, 0.72f, 0.74f),
            new Color(0.6f, 0.6f, 0.6f), 0.5f, new Color(1f, 0.95f, 0.95f))),
        (12f, DayLook),
    };

    /// <summary>The look at this many laps driven (continuous); smooth between the keys.</summary>
    public static Look LookAt(float laps)
    {
        laps = Cycle(laps);
        if (laps <= Keys[0].at) return Keys[0].look;
        for (int i = 1; i < Keys.Length; i++)
            if (laps < Keys[i].at)
            {
                float t = (laps - Keys[i - 1].at) / (Keys[i].at - Keys[i - 1].at);
                return Look.Lerp(Keys[i - 1].look, Keys[i].look, t * t * (3f - 2f * t));
            }
        return Keys[^1].look;
    }

    static float Ramp(float x, float a, float b) => Math.Clamp((x - a) / (b - a), 0f, 1f);

    /// <summary>Laps driven as the time of day: 0..CycleLaps (a whole day ends exactly on CycleLaps, the plain day).</summary>
    static float Cycle(float laps) => laps <= 0 ? 0 : laps - CycleLaps * MathF.Floor((laps - 1e-4f) / CycleLaps);

    /// <summary>Headlights (0..1): on through the first fifth of the first dusk lap, off early in the second dawn lap.</summary>
    public static float BeamsAt(float laps)
    {
        float c = Cycle(laps);
        return Ramp(c, 4f, 4.2f) - Ramp(c, 11.1f, 11.4f);
    }

    /// <summary>Windows, beacons, searchlight and floodlight (0..1): on as the sun goes, off in the dawn.</summary>
    public static float GlowAt(float laps)
    {
        float c = Cycle(laps);
        return Ramp(c, 3.6f, 4.8f) - Ramp(c, 10.8f, 11.5f);
    }

    /// <summary>The plain day through the first two laps of every day: nothing is touched.</summary>
    public static bool IsDay(float laps)
    {
        float c = Cycle(laps);
        return c <= 2f || c >= CycleLaps;
    }

    // ------------------------------------------------------------ state

    /// <summary>A rally run is on: the light follows the laps. Off: the plain day, exactly.</summary>
    public bool Active { get; private set; }
    /// <summary>Laps completed in the run (void laps too), with the test offset (--phase, --laps-driven).</summary>
    public int LapsDriven { get; private set; }
    /// <summary>The time of day in laps driven, as shown (continuous, never runs backwards within a run).</summary>
    public float Clock { get; private set; }
    /// <summary>The night's glow 0 (day) .. 1 (night): lit windows, the car's radiation.</summary>
    public float Glow => _glow;
    /// <summary>Headlights 0..1 now (other cars' lamps in a LAN race follow it).</summary>
    public float Beams => Active ? _beams : 0f;
    /// <summary>The phase of the lap being driven (decided at its start).</summary>
    public Phase LapPhase => PhaseForLap(LapsDriven + 1);
    /// <summary>
    /// Seconds the climate adds to the plan's lap target for the lap being driven: its phase at the
    /// lap's start, so it never changes mid-lap. 0 when inactive.
    /// </summary>
    public float ClimateAllowance => Active ? AllowanceFor(LapPhase) : 0f;

    /// <summary>Tests: --laps-driven / --phase add this to the run's laps; --time-of-day pins the clock (laps).</summary>
    public int LapOffset;
    public float? PinnedClock;
    /// <summary>The weather (Race.Weather) by the time of day in laps: how overcast (0..1) and how much fog
    /// to add to the base density; null on a track without weather (the look exactly as before).</summary>
    public Func<float, float> Overcast, ExtraFog;
    /// <summary>The time of day the title shows when no run sets one (only with weather: the plain day otherwise).</summary>
    const float TitleClock = 0.5f;
    float _baseFog = -1;

    /// <summary>The look under cloud: a dull sun, a grey sky, the baked land and the unlit world dimmed.</summary>
    static Look Overcasted(in Look l, float o)
    {
        float sky = (l.Sky.R + l.Sky.G + l.Sky.B) / 3f;
        var grey = l;
        grey.SunEnergy = l.SunEnergy * 0.3f;
        grey.SunColor = l.SunColor.Lerp(new Color(0.85f, 0.88f, 0.92f), 0.6f);
        grey.Ambient = l.Ambient.Lerp(new Color(0.55f, 0.57f, 0.6f) * (sky / 0.62f), 0.6f);
        grey.Sky = l.Sky.Lerp(new Color(0.46f, 0.48f, 0.5f) * (sky / 0.62f), 0.8f);
        grey.Baked = l.Baked * 0.72f;
        grey.Unlit = l.Unlit * 0.8f;
        return Look.Lerp(l, grey, o);
    }

    /// <summary>Test: one combined headlight beam instead of two (--one-beam).</summary>
    public bool OneBeam;

    /// <summary>Test: the lap bookkeeping alone, as in a run with this many laps done (nothing is rendered).</summary>
    public void SetLapsForTest(int done, float frac)
    {
        Active = true;
        LapsDriven = done + LapOffset;
        Clock = done + frac;
    }

    Godot.Environment _env;
    RallySky _sky;
    DirectionalLight3D _sun;
    Transform3D _sunXf;
    MeshInstance3D _terrain;
    Material _terrainDay;
    ShaderMaterial _terrainNight;
    CarLights _carLights;
    CarVisual _carVis;
    SovietBus _bus;
    KozlikPatrol _kozlik;
    BalalaikaSorties _jets;
    TankBattle _tanks;
    KgbOfficers _kgb;
    TyreEffects _fx;
    TrackData _t;
    bool _dirty = true, _dayApplied;
    float _appliedClock = -1, _glow, _beams;
    readonly bool _debug = OS.HasEnvironment("DAYNIGHTDBG");

    // Unshaded materials that dim with the light: their day colour and how much they keep (NightGlow.Keep).
    readonly Dictionary<StandardMaterial3D, (Color day, float keep)> _unlit = new();
    Color _tint = Colors.White;

    // World lights.
    SpotLight3D _flood;
    readonly OmniLight3D[] _burners = new OmniLight3D[2];
    readonly OmniLight3D[] _flashes = new OmniLight3D[2];
    readonly float[] _flashLife = new float[2];
    Node3D _busModel, _kozlikBody;
    readonly List<Node3D> _busLamps = new(), _kozlikLamps = new(), _truckLamps = new();
    /// <summary>The gulag truck (its headlamps after dark; set after Init).</summary>
    public GulagTruck Truck;
    public DrunkTractor Tractor;
    public RescueTractor Rescue;
    public SlurryTractor Slurry;
    Node3D _slurryModel;
    readonly List<Node3D> _slurryLamps = new();
    Node3D _tractorModel, _rescueModel;
    readonly List<Node3D> _tractorLamps = new(), _rescueLamps = new();
    Node3D _truckModel;
    StandardMaterial3D _vehicleLampMat;

    public void Init(Godot.Environment env, DirectionalLight3D sun, MeshInstance3D terrain, TrackData t, CarVisual car,
        SovietBus bus, KozlikPatrol kozlik, BalalaikaSorties jets, TankBattle tanks, KgbOfficers kgb, TyreEffects fx)
    {
        Name = "DayNight";
        _env = env;
        _sun = sun;
        _sunXf = sun.GlobalTransform;
        _terrain = terrain;
        _t = t;
        _world = WorldScope.Of(terrain) ?? WorldScope.Of(sun);      // (not in the tree yet itself; _Ready asks again)
        _sky = new RallySky(env, t.Winter);
        _carVis = car;
        _bus = bus;
        _kozlik = kozlik;
        _jets = jets;
        _tanks = tanks;
        _kgb = kgb;
        _fx = fx;
        _terrainDay = ((ArrayMesh)terrain.Mesh).SurfaceGetMaterial(0);
        _terrainNight = TerrainMesh.NightMaterial(((ShaderMaterial)_terrainDay).GetShaderParameter("atlas").As<Texture2D>());

        _carLights = new CarLights(OneBeam);
        car.AddChild(_carLights);
        _vehicleLampMat = NightGlow.Keep(new StandardMaterial3D { AlbedoColor = new Color(1f, 0.95f, 0.78f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded });

        _flood = new SpotLight3D
        {
            LightColor = new Color(0.9f, 0.95f, 1f), SpotRange = 34f, SpotAngle = 34f, SpotAttenuation = 0.5f,
            ShadowEnabled = false, LightSpecular = 0f, Visible = false,
        };
        AddChild(_flood);
        for (int i = 0; i < 2; i++)
        {
            _burners[i] = new OmniLight3D { LightColor = new Color(1f, 0.6f, 0.25f), OmniRange = 22f, OmniAttenuation = 1f, ShadowEnabled = false, LightSpecular = 0f, Visible = false };
            _flashes[i] = new OmniLight3D { LightColor = new Color(1f, 0.75f, 0.4f), OmniRange = 26f, OmniAttenuation = 1f, ShadowEnabled = false, LightSpecular = 0f, Visible = false };
            AddChild(_burners[i]);
            AddChild(_flashes[i]);
        }
        if (_tanks != null) _tanks.Fired += MuzzleFlash;
    }

    /// <summary>After the world is built: every unshaded material there is (and, from now on, every one added).</summary>
    public bool DeferInitialCollect { get; set; }

    public override void _Ready()
    {
        _world = WorldScope.Of(this);
        if (!DeferInitialCollect)
            foreach (var _ in CollectInitialSteps()) { }
        GetTree().NodeAdded += OnNodeAdded;
    }

    public override void _ExitTree()
    {
        GetTree().NodeAdded -= OnNodeAdded;
    }

    /// <summary>The 3D world this night lights. A second world in the tree (the replay viewer's) has its own DayNight: the other world's nodes are not ours.</summary>
    World3D _world;

    void OnNodeAdded(Node n)
    {
        if (n is not (GeometryInstance3D or Light3D) || WorldScope.Of(n) != _world) return;
        Collect(n, false);
    }

    /// <summary>Visit the completed world in the same preorder as Collect, with loading checkpoints.</summary>
    public System.Collections.Generic.IEnumerable<int> CollectInitialSteps()
    {
        var pending = new System.Collections.Generic.Stack<Node>();
        pending.Push(GetParent());
        var slice = System.Diagnostics.Stopwatch.StartNew();
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            Collect(node, false);
            var children = node.GetChildren();
            for (int i = children.Count - 1; i >= 0; i--) pending.Push(children[i]);
            if (slice.ElapsedMilliseconds < 10) continue;
            yield return 0;
            slice.Restart();
        }
        Consider(_fx?.SkidMaterial);      // on surfaces that come and go with the marks
        if (_debug) GD.Print($"daynight: {_unlit.Count} unshaded materials dim at night, {NightGlow.Glows.Count} glow");
    }

    readonly List<(Light3D light, float energy)> _sceneryLights = new();

    void Collect(Node n, bool deep = true)
    {
        if (n is Light3D light && light.HasMeta("night_energy"))
        {
            float energy = light.GetMeta("night_energy").AsSingle();
            _sceneryLights.Add((light, energy));
            light.Visible = _glow > 0.01f;          // a light made at night (a jet's landing light) starts as the night is
            light.LightEnergy = energy * _glow;
        }
        if (n is GeometryInstance3D g)
        {
            Consider(g.MaterialOverride);
            if (n is MeshInstance3D mi && mi.Mesh != null)
                for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                {
                    Consider(mi.GetSurfaceOverrideMaterial(s));
                    Consider(mi.Mesh.SurfaceGetMaterial(s));
                }
            else if (n is MultiMeshInstance3D mm && mm.Multimesh?.Mesh is { } m)
                for (int s = 0; s < m.GetSurfaceCount(); s++) Consider(m.SurfaceGetMaterial(s));
        }
        if (deep) foreach (var c in n.GetChildren()) Collect(c);
    }

    void Consider(Material m)
    {
        if (m is not StandardMaterial3D s || s.ShadingMode != BaseMaterial3D.ShadingModeEnum.Unshaded || _unlit.ContainsKey(s)) return;
        float keep = s.HasMeta(NightGlow.KeepMeta) ? s.GetMeta(NightGlow.KeepMeta).AsSingle() : 0f;
        // The day colour is recorded on the material the first time any night sees it: shared materials (Props.Mat, in every world)
        // may already wear another DayNight's tint by the time a second world considers them.
        bool recorded = s.HasMeta(DayMeta);
        Color c = recorded ? s.GetMeta(DayMeta).AsColor() : s.AlbedoColor;
        // Black (shadow blobs, soot) stays black anyway, and the game fades their alpha itself.
        if (keep >= 1f || Math.Max(c.R, Math.Max(c.G, c.B)) < 0.06f && s.AlbedoTexture == null && !s.VertexColorUseAsAlbedo) return;
        if (!recorded) s.SetMeta(DayMeta, c);
        _unlit[s] = (c, keep);
        if (_tint != Colors.White || recorded) Tint(s, c, keep);      // (a recorded one may wear the other world's tint)
    }

    /// <summary>Material meta: the colour the material has by day, before any DayNight tinted it.</summary>
    const string DayMeta = "night_day_colour";

    void Tint(StandardMaterial3D s, Color day, float keep)
    {
        Color t = _tint.Lerp(Colors.White, keep);
        s.AlbedoColor = new Color(day.R * t.R, day.G * t.G, day.B * t.B, day.A);
    }

    /// <summary>A tank fired: a short orange flash on the ground around the muzzle (only after dark).</summary>
    void MuzzleFlash(Vector3 at)
    {
        if (!Active || _glow <= 0.01f) return;
        int i = _flashLife[0] <= _flashLife[1] ? 0 : 1;
        _flashLife[i] = 0.14f;
        _flashes[i].GlobalPosition = at + Vector3.Up * 1.5f;
    }

    /// <summary>Per frame from the race: active (a rally run), the laps done in it and how far round the current lap is (0..1).</summary>
    public void Update(float dt, bool active, int lapsDone, float lapFrac, bool newRun, float brake)
    {
        _sky.Tick(dt);
        if (active != Active) { Active = active; _dirty = true; }
        if (!Active)
        {
            if (_dirty) { if (Overcast != null) Apply(TitleClock); else ApplyDay(); }
            _carLights.Level = 0;
            _carLights.Tick(dt, 0, null);
            return;
        }
        LapsDriven = lapsDone + LapOffset;
        float target = PinnedClock ?? LapsDriven + Math.Clamp(lapFrac, 0f, 1f);
        // Time never runs backwards in a run (a restart request, a rescue) and never jumps (a test lap):
        // it waits for the car or catches up at five laps a minute.
        if (newRun || PinnedClock != null) Clock = target;
        else Clock = Mathf.MoveToward(Clock, Math.Max(Clock, target), 5f / 60f * dt);
        if (_dirty || Math.Abs(Clock - _appliedClock) > 0.002f) Apply(Clock);
        _carLights.Level = _beams;
        _carLights.Tick(dt, brake, _carVis.Damage);
        TickLights(dt);
    }

    /// <summary>The plain day, exactly as RaceScene built it.</summary>
    void ApplyDay()
    {
        _dirty = false;
        _appliedClock = -1;
        if (_dayApplied) return;
        _dayApplied = true;
        var d = DayLook;
        if (_baseFog >= 0) _env.FogDensity = _baseFog;
        _env.BackgroundColor = d.Sky;
        _env.FogLightColor = d.Sky;
        _sky.SetLook(d, TerrainMesh.SunDir, 0f);
        _env.AmbientLightColor = d.Ambient;
        _env.AmbientLightEnergy = d.AmbientEnergy;
        _sun.LightEnergy = d.SunEnergy;
        _sun.LightColor = d.SunColor;
        _sun.GlobalTransform = _sunXf;
        TerrainMesh.SetLightingMaterial(_terrain, null);
        _terrain.GetNodeOrNull<WorldBackdrop>("WorldBackdrop")?.SetLook(Colors.White, d.Sky);
        SetTint(Colors.White);
        SetGlow(0);
        _beams = 0;
        _flood.Visible = false;
        foreach (var o in _burners) o.Visible = false;
        for (int i = 0; i < 2; i++) { _flashes[i].Visible = false; _flashLife[i] = 0; }
        foreach (var n in _busLamps) n.Visible = false;
        foreach (var n in _kozlikLamps) n.Visible = false;
        foreach (var n in _truckLamps) n.Visible = false;
        if (_debug) GD.Print("daynight: plain day");
    }

    void Apply(float clock)
    {
        _dirty = false;
        _appliedClock = clock;
        if (_baseFog < 0) _baseFog = _env.FogDensity;
        if (IsDay(clock) && Overcast == null) { ApplyDay(); return; }
        _dayApplied = false;
        var l = LookAt(clock);
        float overcast = Overcast?.Invoke(clock) ?? 0f;
        if (Overcast != null) l = Overcasted(l, overcast);
        if (ExtraFog != null) _env.FogDensity = _baseFog + ExtraFog(clock);
        _env.BackgroundColor = l.Sky;
        _env.FogLightColor = l.Sky;
        _env.AmbientLightColor = l.Ambient;
        _env.AmbientLightEnergy = l.AmbientEnergy;
        _sun.LightEnergy = l.SunEnergy;
        _sun.LightColor = l.SunColor;
        // Direction: the day sun's, lowered to SunElev and turned by SunAz about the vertical.
        var flat = new Vector2(TerrainMesh.SunDir.X, TerrainMesh.SunDir.Z).Normalized().Rotated(Mathf.DegToRad(l.SunAz));
        float e = Mathf.DegToRad(Math.Max(l.SunElev, 1f));
        var dir = new Vector3(flat.X * MathF.Cos(e), MathF.Sin(e), flat.Y * MathF.Cos(e));
        _sun.GlobalTransform = new Transform3D(Basis.LookingAt(-dir, Vector3.Up), Vector3.Zero);
        _sky.SetLook(l, dir, overcast);
        TerrainMesh.SetLightingMaterial(_terrain, _terrainNight);
        _terrain.GetNodeOrNull<WorldBackdrop>("WorldBackdrop")?.SetLook(l.Baked, l.Sky, l.LitMix, l.Unlit);
        _terrainNight.SetShaderParameter("baked_tint", new Vector3(l.Baked.R, l.Baked.G, l.Baked.B));
        _terrainNight.SetShaderParameter("lit_mix", l.LitMix);
        SetTint(l.Unlit);
        SetGlow(GlowAt(clock));
        _beams = BeamsAt(clock);
        if (_debug && (int)(clock * 4) != (int)(_lastPrint * 4))
        {
            _lastPrint = clock;
            GD.Print($"daynight: clock {clock:F2} laps (lap {LapsDriven + 1}, {LapPhase}, +{ClimateAllowance:0.0} s), sun {l.SunElev:F0} deg x{l.SunEnergy:F2}, lit {l.LitMix:F2}, glow {_glow:F2}, beams {_beams:F2}");
        }
    }

    float _lastPrint = -1;
    bool _burning;

    void SetTint(Color t)
    {
        if (t == _tint) return;
        _tint = t;
        foreach (var (m, (day, keep)) in _unlit) Tint(m, day, keep);
    }

    /// <summary>
    /// Puts the whole look back as this DayNight says it is now, whatever other worlds did to the shared state meanwhile: the
    /// shared materials' tint and NightGlow.Level (statics, rewritten by a second world's night, e.g. the replay viewer's). The session owner
    /// calls it after the nested world is gone and NightGlow.Restore ran, before the live scene runs again. Applies at once.
    /// </summary>
    public void ForceReapply()
    {
        _dayApplied = false;          // the three guards that skip an unchanged look: ApplyDay's, SetTint's and SetGlow's
        _tint = new Color(-1f, -1f, -1f, -1f);
        _glow = -1f;
        _appliedClock = -1;
        _dirty = true;
        if (Active) Apply(Clock);
        else if (Overcast != null) Apply(TitleClock);
        else ApplyDay();
    }

    void SetGlow(float g)
    {
        if (g == _glow && g > 0) return;
        _glow = g;
        NightGlow.Level = g;
        foreach (var (m, energy) in NightGlow.Glows) m.EmissionEnergyMultiplier = energy * g;
        foreach (var lamp in NightGlow.Lamps) if (IsInstanceValid(lamp)) lamp.Scale = Vector3.One * (1f + 1.4f * g);
        bool on = g > 0.01f;
        foreach (var (light, energy) in _sceneryLights)
            if (IsInstanceValid(light)) { light.Visible = on; light.LightEnergy = energy * g; }
        _flood.Visible = on;
        _flood.LightEnergy = 7f * g;
    }

    /// <summary>The moving lights: floodlight placement, bus and Kozlik lamps, afterburners, flashes.</summary>
    void TickLights(float dt)
    {
        bool night = _glow > 0.01f;
        if (night)
        {
            Vector2 boss = _kgb.SupervisorPosition;
            float h = _t.MeshHeight(boss.X, boss.Y);
            _flood.GlobalTransform = new Transform3D(Basis.LookingAt(Vector3.Down, Vector3.Forward), new Vector3(boss.X, h + 14f, boss.Y));
        }
        VehicleLamps(night);
        // Afterburners: a warm light on the ground under the two jets burning hardest.
        int k = 0;
        if (night && _jets != null && _jets.Visible)
            foreach (var (nozzle, burn) in _jets.Burners())
            {
                if (k >= _burners.Length) break;
                _burners[k].Visible = true;
                _burners[k].GlobalPosition = nozzle + Vector3.Up * 0.5f;
                _burners[k].LightEnergy = 6f * burn * _glow * (0.85f + 0.3f * (float)GD.Randf());
                k++;
            }
        if (_debug && (k > 0) != _burning) GD.Print($"daynight: afterburner light {(k > 0 ? "on" : "off")} at frame {Engine.GetProcessFrames()}");
        _burning = k > 0;
        for (; k < _burners.Length; k++) _burners[k].Visible = false;
        for (int i = 0; i < 2; i++)
        {
            _flashLife[i] = Math.Max(0f, _flashLife[i] - dt);      // (clamped: an expired flash is 0, not an ever more negative count that a re-applied look would reset)
            _flashes[i].Visible = _flashLife[i] > 0;
            _flashes[i].LightEnergy = 10f * Math.Max(0, _flashLife[i] / 0.14f) * _glow;
        }
    }

    /// <summary>Headlamps (a glowing pair and one beam) on the bus in service and the Kozlik; a burnt-out bus keeps none.</summary>
    void VehicleLamps(bool on)
    {
        if (_bus?.Model is { } bm && bm != _busModel)
        {
            foreach (var n in _busLamps) n.QueueFree();
            _busLamps.Clear();
            _busModel = bm;
            AddLamps(bm, _busLamps, new[] { new Vector3(-0.85f, 0.85f, -4.07f), new Vector3(0.85f, 0.85f, -4.07f) }, new Vector3(0, 0.95f, -4.2f), 0.22f, 4f);
        }
        if (_kozlik?.Body is { } kb && kb != _kozlikBody)
        {
            _kozlikBody = kb;
            var parent = kb.GetParent<Node3D>();
            float s = kb.Scale.X;
            AddLamps(parent, _kozlikLamps, new[] { new Vector3(-0.66f, 1.02f, -2.02f) * s, new Vector3(0.66f, 1.02f, -2.02f) * s }, new Vector3(0, 1.05f, -2.2f) * s, 0.16f * s, 3.5f);
        }
        if (Truck?.Model is { } tm && tm != _truckModel)
        {
            _truckModel = tm;
            AddLamps(tm, _truckLamps, GulagTruck.Headlamps, GulagTruck.BeamAt, 0.2f, 4.5f);
        }
        TractorLamps(Tractor?.Model, ref _tractorModel, _tractorLamps, on && Tractor != null && Tractor.Visible);
        TractorLamps(Slurry?.Model, ref _slurryModel, _slurryLamps, on && Slurry != null && Slurry.Visible);
        TractorLamps(Rescue?.Model, ref _rescueModel, _rescueLamps, on && Rescue != null && Rescue.Visible);
        bool bus = on && _bus != null && _bus.Visible, kozlik = on && _kozlik != null && _kozlik.Visible;
        bool truck = on && _truckModel != null && _truckModel.Visible;
        foreach (var n in _busLamps) n.Visible = bus;
        foreach (var n in _kozlikLamps) n.Visible = kozlik;
        foreach (var n in _truckLamps) n.Visible = truck;
    }

    void TractorLamps(Node3D model, ref Node3D previous, List<Node3D> lamps, bool on)
    {
        if (model != null && model != previous)
        {
            foreach (var lamp in lamps) lamp.QueueFree();
            lamps.Clear();
            previous = model;
            AddLamps(model, lamps, new[] { new Vector3(-0.48f, 1.59f, -2.01f), new Vector3(0.48f, 1.59f, -2.01f) },
                new Vector3(0, 1.59f, -2.12f), 0.115f, 3.5f);
        }
        foreach (var lamp in lamps) lamp.Visible = on && model != null && model.Visible;
    }

    void AddLamps(Node3D parent, List<Node3D> list, Vector3[] lamps, Vector3 beamAt, float radius, float energy)
    {
        foreach (var p in lamps)
        {
            var m = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = radius, Height = radius * 1.6f, RadialSegments = 8, Rings = 4 },
                MaterialOverride = _vehicleLampMat, Position = p, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false,
            };
            parent.AddChild(m);
            list.Add(m);
        }
        var spot = new SpotLight3D
        {
            LightColor = new Color(1f, 0.9f, 0.7f), LightEnergy = energy, SpotRange = 32f, SpotAngle = 32f, SpotAttenuation = 0.7f,
            ShadowEnabled = false, LightSpecular = 0f, Position = beamAt, RotationDegrees = new Vector3(-6f, 0, 0), Visible = false,
        };
        parent.AddChild(spot);
        list.Add(spot);
    }
}
