using System;
using System.Collections.Generic;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// The road service's snowploughs (TrackDef.Ploughs): six-wheeled orange trucks with a huge steel
/// V-plough on the front, an amber beacon turning on the cab, a sand spreader behind. Each runs
/// round the lap in its own lane, one with the race and one against it, hugging the centre line
/// so the blade reaches it (when two meet, both keep right into the bank until they have passed);
/// snow piles on the blade and flies off both wings. Blade and body are
/// moving obstacles that shove the car. A plough slows for bends and stops, honking, for a car
/// stuck in its lane ahead (it means to frighten, not to flatten).
/// </summary>
public partial class SnowPloughs : Node3D
{
    const float Cruise = 10f, BendSpeed = 6f, LaneShare = 0.42f, PassShare = 0.68f;
    const float BladeHalf = 2.6f, BladeTip = -7.4f, WingBack = -5.0f;
    /// <summary>The whole truck is built a size up: it should look massive.</summary>
    const float Size = 1.15f;
    static readonly Color Orange = new(0.9f, 0.42f, 0.08f), Steel = new(0.24f, 0.26f, 0.29f), Edge = new(0.7f, 0.72f, 0.74f);
    static readonly Color Black = new(0.06f, 0.06f, 0.06f), Glass = new(0.16f, 0.22f, 0.27f), Sand = new(0.45f, 0.38f, 0.3f);
    static readonly Color Snow = new(0.95f, 0.96f, 0.99f), Amber = new(1f, 0.6f, 0.1f);

    // Collision circles in the truck's frame (front at -Z): the blade's tip and wings, then the body.
    static readonly (float x, float z, float r)[] Circles =
        { (0f, -6.4f, 1.0f), (-1.3f, -5.8f, 0.9f), (1.3f, -5.8f, 0.9f), (-2.2f, -5.2f, 0.7f), (2.2f, -5.2f, 0.7f),
          (0f, -3.0f, 1.45f), (0f, -0.5f, 1.45f), (0f, 2.0f, 1.45f), (0f, 3.8f, 1.45f) };

    class Plough
    {
        public float S, Speed, StartS, HonkCd, SprayL, SprayR, Clock, Lane = LaneShare;
        public bool Against, Mixer;
        public Node3D Drum;
        public int Obs;
        public Node3D Model, Beacon, PileL, PileR;
        public float Soot;
        public DieselSound Sound;
        public Vector2 Pos, Fwd;
    }

    TrackData _t;
    TyreEffects _fx;
    readonly List<Plough> _ploughs = new();
    bool _tapeDriven;

    /// <summary>The replay supplies the recorded speed and snow load. Release
    /// restores the untouched live simulation's load and engine speed.</summary>
    public bool TapeDriven
    {
        get => _tapeDriven;
        set
        {
            if (_tapeDriven == value) return;
            _tapeDriven = value;
            if (!value)
                foreach (var p in _ploughs) { p.Sound.Speed = p.Speed; SnowLoad(p, p.Speed); }
        }
    }

    public void Init(TrackData t, TyreEffects fx, List<PloughDef> defs)
    {
        _t = t;
        _fx = fx;
        Tape.Version(this, 1); Tape.Part(this, "root");
        foreach (var d in defs)
        {
            var p = new Plough { StartS = d.Start, Against = d.Against, Mixer = d.Kind == "mixer", Obs = t.Obstacles.Count };
            foreach (var _ in p.Mixer ? MixerCircles : Circles) t.Obstacles.Add(new Obstacle(new Vector2(-1e4f, -1e4f), 0.1f));
            p.Model = p.Mixer ? BuildMixer(out p.Beacon, out p.Drum) : BuildModel(out p.Beacon, out p.PileL, out p.PileR);
            AddChild(p.Model);
            // The registered beacon part stays (old tapes fit it) but is empty: the lamp is the rotating beacon, which is not taped.
            ((MeshInstance3D)p.Beacon).Mesh = null;
            RotatingBeacon.Add(p.Model, p.Beacon.Position, _ploughs.Count * 0.37f * Mathf.Tau, () => p.Sound.Speed > 1f);
            p.Sound = new DieselSound { BaseRate = 7f, RatePerSpeed = 1.8f, Body = 0.08f, Loudness = 4f, Reach = p.Mixer ? 140f : 240f, AirHorn = true, Plough = !p.Mixer };      // the blade rumbles; a mixer truck keeps the plain voice
            AddChild(p.Sound);
            MarkTape(p, _ploughs.Count + 1);
            _ploughs.Add(p);
        }
        Reset();
    }

    /// <summary>The world tape parts of one plough or mixer: model, beacon, mixer
    /// drum, engine sound position and speed. Snow heap scale is reconstructed
    /// from the same DieselSpeed channel, including old tapes, seeks and loops.</summary>
    static void MarkTape(Plough p, int no)
    {
        string n = $"{no}/";
        Tape.Part(p.Model, n + "model"); Tape.Part(p.Beacon, n + "beacon"); Tape.Part(p.Sound, n + "sound");
        if (p.Drum != null) { Tape.Part(p.Drum, n + "drum"); Tape.Spin(p.Drum, Vector3.Back, () => p.Clock * 1.6f); }
        Tape.Sound(p.Model, TapeSoundChannel.DieselSpeed, () => p.Sound.Speed,
            v => { p.Sound.Speed = v; SnowLoad(p, v); });
    }

    static void SnowLoad(Plough p, float speed)
    {
        if (!p.Mixer)
            p.PileL.Scale = p.PileR.Scale = new Vector3(1f, Math.Clamp(speed / Cruise, 0.05f, 1.2f), 1f);
    }

    /// <summary>Where the ploughs are (their centres).</summary>
    public IEnumerable<Vector2> Positions
    {
        get { foreach (var p in _ploughs) yield return p.Pos; }
    }

    /// <summary>Where the ploughs are along the road (road metres) and which way each one drives.</summary>
    public IEnumerable<(float s, bool against)> Lanes
    {
        get { foreach (var p in _ploughs) yield return (p.S, p.Against); }
    }

    public void Reset()
    {
        foreach (var p in _ploughs)
        {
            p.S = p.StartS;
            p.Speed = Cruise;
            p.Lane = LaneShare;
            Place(p, 0f);
        }
    }

    /// <summary>Stage a real runway pass for the winter scenic tour; normal driving takes over immediately.</summary>
    public void CueSchoolRunwayPass()
    {
        foreach (var p in _ploughs)
        {
            p.S = p.Against ? 1100f : _t.Runway.From + 100f;
            p.Speed = Cruise;
            p.Lane = LaneShare;
            Place(p, 0f);
        }
    }

    /// <summary>Drive on round the lap; car may be null (it then ignores it).</summary>
    public void Tick(float dt, CarSim car)
    {
        if (_tapeDriven) return;
        int n = _t.Center.Length;
        foreach (var p in _ploughs)
        {
            p.Clock += dt;
            float sign = p.Against ? -1f : 1f;
            int i = ((int)p.S % n + n) % n;
            // Slow for the bends ahead in its own direction.
            float turn = 0;
            Vector2 d0 = _t.CenterDir(i) * sign;
            for (int k = 5; k < 40; k += 5)
                turn = Math.Max(turn, MathF.Abs(d0.AngleTo(_t.CenterDir(((i + (int)sign * k) % n + n) % n) * sign)));
            float target = Mathf.Lerp(Cruise, BendSpeed, Math.Clamp(turn / 1.0f, 0f, 1f));
            // A car stuck in its lane ahead: stop and lean on the horn.
            p.HonkCd -= dt;
            if (car != null)
            {
                Vector2 rel = new Vector2(car.Pos.X, car.Pos.Z) - p.Pos;
                float ahead = rel.Dot(p.Fwd), lat = rel.Dot(new Vector2(-p.Fwd.Y, p.Fwd.X));
                if (ahead > 5f && ahead < 16f && MathF.Abs(lat) < BladeHalf + 0.6f)
                {
                    target = 0;
                    if (p.HonkCd <= 0)
                    {
                        p.Sound.Honk(1f);
                        p.HonkCd = 2.2f;
                        if (OS.HasEnvironment("PLOUGHDBG")) GD.Print($"plough {(p.Against ? "against" : "with the race")} honks at {p.Clock:F1} s");
                    }
                }
            }
            // The gulag truck in its lane ahead: stop for it too.
            foreach (var c in GulagTruck.Body)
            {
                Vector2 rel = c - p.Pos;
                float ahead = rel.Dot(p.Fwd), lat = rel.Dot(new Vector2(-p.Fwd.Y, p.Fwd.X));
                if (ahead > 0f && ahead < 14f && MathF.Abs(lat) < BladeHalf + GulagTruck.BodyRadius + 0.3f) target = 0;
            }
            // Any other vehicle moving in its lane ahead (the Kozlik, the bus, a tractor): stop short of it.
            target = Math.Min(target, TrafficCap.Cap(_t, p.Pos, p.Fwd, p.Speed, BladeHalf + 0.3f, IsPloughObstacle));
            // A convoy: keep a truck's length or two behind the one ahead going the same way.
            foreach (var o in _ploughs)
            {
                if (o == p || o.Against != p.Against) continue;
                float gap = (o.Pos - p.Pos).Dot(p.Fwd);
                if (gap > 0f && gap < 30f && (o.Pos - p.Pos).Length() < 32f) target = Math.Min(target, Math.Max(0f, o.Speed + (gap - ConvoyGap) * 0.5f));
            }
            // Another plough coming the other way: both keep right (into the bank) until they have passed.
            float lane = p.Mixer ? MixerLane : LaneShare;
            foreach (var o in _ploughs)
            {
                if (o == p || o.Against == p.Against) continue;
                float gap = (o.Pos - p.Pos).Dot(p.Fwd);
                if (gap > -12f && gap < 70f && (o.Pos - p.Pos).Length() < 75f) lane = PassShare;
            }
            p.Lane = Mathf.MoveToward(p.Lane, lane, 0.35f * dt);
            p.Speed = Mathf.MoveToward(p.Speed, target, (target < p.Speed ? 4f : 1.2f) * dt);
            p.S = ((p.S + sign * p.Speed * dt) % n + n) % n;
            Place(p, dt);
        }
    }

    /// <summary>One of the ploughs' own obstacle circles (they keep clear of each other by their convoy and lane rules).</summary>
    bool IsPloughObstacle(int index)
    {
        foreach (var p in _ploughs)
            if (index >= p.Obs && index < p.Obs + (p.Mixer ? MixerCircles : Circles).Length) return true;
        return false;
    }

    // Interpolate heading and lane width as well as position. Integer metre headings made the
    // long blade visibly snap sideways at each sample, especially in the tight snowy bends.
    internal static (Vector2 position, Vector2 forward) RoadPose(TrackData track, float s, bool against, float lane)
    {
        int n = track.Center.Length;
        int i = ((int)MathF.Floor(s) % n + n) % n, next = (i + 1) % n;
        float f = s - MathF.Floor(s);
        Vector2 c = track.Center[i].Lerp(track.Center[next], f);
        Vector2 fwd = track.CenterDir(i).Lerp(track.CenterDir(next), f).Normalized() * (against ? -1f : 1f);
        Vector2 right = new(-fwd.Y, fwd.X);
        float width = Mathf.Lerp(track.HalfWidth[i], track.HalfWidth[next], f);
        return (c + right * (width * lane), fwd);
    }

    void Place(Plough p, float dt)
    {
        var (pos, fwd) = RoadPose(_t, p.S, p.Against, p.Lane);
        Vector2 right = new(-fwd.Y, fwd.X);
        // Its lane is the right of its own direction, but close to the middle: the blade reaches the centre line.
        p.Pos = pos;
        p.Fwd = fwd;
        float hF = _t.MeshHeight(pos.X + fwd.X * 3f, pos.Y + fwd.Y * 3f), hB = _t.MeshHeight(pos.X - fwd.X * 3f, pos.Y - fwd.Y * 3f);
        float hR = _t.MeshHeight(pos.X + right.X * 1.2f, pos.Y + right.Y * 1.2f), hL = _t.MeshHeight(pos.X - right.X * 1.2f, pos.Y - right.Y * 1.2f);
        var b = new Basis(Vector3.Up, TrackData.YawFor(fwd))
              * new Basis(Vector3.Right, MathF.Atan2(hF - hB, 6f))
              * new Basis(Vector3.Back, MathF.Atan2(hR - hL, 2.4f) * 0.8f);
        p.Model.GlobalTransform = new Transform3D(b.Scaled(Vector3.One * Size), new Vector3(pos.X, (hF + hB) * 0.5f, pos.Y));
        p.Sound.Position = p.Model.Position;
        p.Sound.Speed = p.Speed;

        Vector2 vel = fwd * p.Speed;
        var circles = p.Mixer ? MixerCircles : Circles;
        for (int k = 0; k < circles.Length; k++)
        {
            var (x, z, r) = circles[k];
            Vector2 at = pos + (right * x - fwd * z) * Size;      // the model's front is -Z: -z is forward
            _t.Obstacles[p.Obs + k] = new Obstacle(at, r * Size, vel, Material: ImpactMaterial.Metal);
        }

        if (_fx != null && dt > 0)
        {
            // The player's diesel smoke from the stack behind the cab, a big truck engine.
            Vector3 pipe = p.Model.GlobalTransform * (p.Mixer ? MixerSootPipe : SootPipe);
            Vector3 f3 = new(fwd.X, 0, fwd.Y);
            _fx.EngineSoot(pipe, -f3, f3 * p.Speed, Mathf.Clamp(p.Speed / 8f, 0.25f, 1f), 2f, ref p.Soot, dt);
        }
        if (p.Mixer)
        {
            // The drum turns all the way, mixing.
            p.Drum.Rotation = new Vector3(0, 0, p.Clock * 1.6f);
            return;
        }
        // Snow heaps up on the blade with speed, and flies off both wings.
        SnowLoad(p, p.Speed);
        if (dt > 0 && p.Speed > 1f && _fx != null)
        {
            var xf = p.Model.GlobalTransform;
            foreach (float s in new[] { -1f, 1f })
            {
                SprayWing(xf, s, out Vector3 wing, out Vector3 outward);
                Vector3 v3 = new(vel.X, 0, vel.Y);
                if (s < 0) _fx.PloughSpray(wing, outward.Normalized(), v3, p.Speed, ref p.SprayL, dt);
                else _fx.PloughSpray(wing, outward.Normalized(), v3, p.Speed, ref p.SprayR, dt);
            }
        }
    }

    // ------------------------------------------------------------ the mixer

    // A concrete mixer: cab and the turning drum, a truck's width, in the right lane like any lorry.
    const float MixerLane = 0.5f, ConvoyGap = 16f;
    static readonly (float x, float z, float r)[] MixerCircles =
        { (0f, -3.2f, 1.25f), (0f, -1.1f, 1.3f), (0f, 1.1f, 1.3f), (0f, 3.0f, 1.3f) };
    static readonly Color MixerCab = new(0.2f, 0.38f, 0.55f), Drum = new(0.78f, 0.78f, 0.74f), DrumStripe = new(0.85f, 0.45f, 0.1f);

    /// <summary>
    /// The lights of a service truck: a pair of headlamp lenses (front is -Z; at +-front.X, front.Y, front.Z) that glow at
    /// night with one real beam ahead, and red tail lamps (at +-rearX, 1.2 m up, rearZ). All dark by day (NightGlow).
    /// </summary>
    static void Lamps(Node3D truck, Vector3 front, float rearZ, float rearX)
    {
        foreach (float s in new[] { -1f, 1f })
        {
            truck.AddChild(NightGlow.Part(Props.Box(0.34f, 0.21f, 0.06f), new Color(0.9f, 0.87f, 0.7f), new Color(1f, 0.93f, 0.72f), 2.6f, new Vector3(s * front.X, front.Y, front.Z)));
            truck.AddChild(NightGlow.Part(Props.Box(0.28f, 0.14f, 0.05f), new Color(0.55f, 0.08f, 0.06f), new Color(1f, 0.1f, 0.06f), 2.4f, new Vector3(s * rearX, 1.2f, rearZ)));
        }
        var beam = new SpotLight3D
        {
            Position = new Vector3(0, front.Y, front.Z - 0.15f), RotationDegrees = new Vector3(-6f, 0, 0), LightColor = new Color(1f, 0.9f, 0.7f),
            SpotRange = 34f, SpotAngle = 32f, SpotAttenuation = 0.7f, ShadowEnabled = false, LightSpecular = 0f, LightEnergy = 0f, Visible = false,
        };
        beam.SetMeta("night_energy", 3.5f);        // DayNight lights it after dark
        truck.AddChild(beam);
    }

    static Node3D BuildMixer(out Node3D beacon, out Node3D drum)
    {
        var m = new Node3D { Name = "Mixer" };
        m.AddChild(Props.Part(Props.Box(2.4f, 0.5f, 8.2f), Black, new Vector3(0, 0.95f, 0)));
        foreach (float z in new[] { -2.6f, 1.6f, 3.1f })
        foreach (float x in new[] { -1.25f, 1.25f })
            m.AddChild(Props.Part(Props.Cyl(0.58f, 0.48f, 10), Black, new Vector3(x, 0.58f, z), new Vector3(0, 0, 90)));
        BuildMixerCab(m);
        Lamps(m, new Vector3(0.8f, 1.5f, -4.08f), 4.14f, 1.1f);
        // Bearings and cradle stay still while the drum rotates about its inclined axis.
        foreach (float z in new[] { -0.65f, 2.65f })
        {
            m.AddChild(Props.Part(Props.Box(1.65f, 0.45f, 0.35f), Steel, new Vector3(0, 1.5f, z)));
            foreach (float x in new[] { -0.75f, 0.75f })
                m.AddChild(Props.Part(Props.Cyl(0.22f, 0.32f, 12), Black, new Vector3(x, 1.85f, z), new Vector3(90, 0, 0)));
        }
        var tilt = new Node3D { Position = new Vector3(0, 2.6f, 1.0f), RotationDegrees = new Vector3(-12f, 0, 0) };
        m.AddChild(tilt);
        drum = new Node3D { Name = "RotatingDrum" };
        tilt.AddChild(drum);
        drum.AddChild(MixerDrum());
        // Rear mouth, filling hopper and an open discharge trough make the machine readable.
        tilt.AddChild(Props.Part(Props.Cyl(0.36f, 0.025f, 24), Black, new Vector3(0, 0, 2.32f), new Vector3(90, 0, 0)));
        m.AddChild(Props.Part(Props.Cyl(0.55f, 0.55f, 12, 0.28f), Steel, new Vector3(0, 3.36f, 3.35f)));
        var chute = new Node3D { Position = new Vector3(0, 2.35f, 3.65f), RotationDegrees = new Vector3(22, -18, 0) };
        m.AddChild(chute);
        chute.AddChild(Props.Part(Props.Box(0.58f, 0.08f, 1.15f), Steel, new Vector3(0, 0, 0)));
        foreach (float x in new[] { -0.29f, 0.29f })
            chute.AddChild(Props.Part(Props.Box(0.06f, 0.24f, 1.15f), Steel, new Vector3(x, 0.1f, 0)));
        beacon = new MeshInstance3D
        {
            Mesh = Props.Box(0.35f, 0.25f, 0.35f),
            Position = new Vector3(0, 3.43f, -2.85f),
            MaterialOverride = NightGlow.Keep(new StandardMaterial3D { AlbedoColor = Amber, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded }),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        m.AddChild(beacon);
        return m;
    }

    // ------------------------------------------------------------ the truck

    static Node3D BuildModel(out Node3D beacon, out Node3D pileL, out Node3D pileR)
    {
        var m = new Node3D { Name = "Plough" };
        // Frame, six big wheels.
        m.AddChild(Props.Part(Props.Box(2.5f, 0.5f, 9.2f), Black, new Vector3(0, 0.95f, 0)));
        foreach (float z in new[] { -2.9f, 1.6f, 3.3f })
        foreach (float x in new[] { -1.3f, 1.3f })
            m.AddChild(Props.Part(Props.Cyl(0.62f, 0.5f, 10), Black, new Vector3(x, 0.62f, z), new Vector3(0, 0, 90)));
        // Bonnet and cab, road-service orange; a dark windscreen; the beacon on the roof.
        m.AddChild(Props.Part(Props.Box(2.3f, 0.95f, 1.3f), Orange, new Vector3(0, 1.75f, -3.85f)));
        m.AddChild(Props.Part(Props.Box(2.1f, 0.25f, 0.06f), Black, new Vector3(0, 1.55f, -4.52f)));      // grille
        m.AddChild(Props.Part(Props.Box(2.7f, 1.9f, 2.0f), Orange, new Vector3(0, 2.35f, -2.25f)));
        m.AddChild(Props.Part(Props.Box(2.4f, 0.8f, 0.05f), Glass, new Vector3(0, 2.75f, -3.27f)));
        foreach (float s in new[] { -1f, 1f })
            m.AddChild(Props.Part(Props.Box(0.05f, 0.7f, 1.2f), Glass, new Vector3(s * 1.36f, 2.75f, -2.3f)));
        m.AddChild(Props.Part(Props.Box(1.6f, 0.12f, 0.6f), Black, new Vector3(0, 3.36f, -2.4f)));
        m.AddChild(Props.Part(Props.Cyl(0.09f, 1.9f, 8), Black, new Vector3(1.1f, 3.0f, -1.2f)));      // the exhaust stack behind the cab
        beacon = new MeshInstance3D
        {
            Mesh = Props.Box(0.4f, 0.3f, 0.4f),
            Position = new Vector3(0, 3.57f, -2.4f),
            MaterialOverride = NightGlow.Keep(new StandardMaterial3D { AlbedoColor = Amber, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded }),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        m.AddChild(beacon);
        m.AddChild(Props.Part(Props.Box(0.42f, 0.12f, 0.42f), Amber * 0.6f, new Vector3(0, 3.44f, -2.4f)));
        Lamps(m, new Vector3(0.8f, 1.9f, -4.54f), 4.64f, 1.25f);
        // Two work lamps on the front edge of the cab roof, over the blade.
        foreach (float x in new[] { -0.55f, 0.55f })
            m.AddChild(NightGlow.Part(Props.Box(0.3f, 0.12f, 0.1f), new Color(0.95f, 0.93f, 0.8f), new Color(1f, 0.95f, 0.75f), 2.2f, new Vector3(x, 3.36f, -3.32f)));
        // The sand spreader body behind, heaped with grit.
        m.AddChild(Props.Part(Props.Box(2.7f, 1.4f, 5.2f), new Color(0.32f, 0.33f, 0.3f), new Vector3(0, 2.0f, 1.6f)));
        m.AddChild(Props.Part(new PrismMesh { Size = new Vector3(2.5f, 0.5f, 5f) }, Sand, new Vector3(0, 2.95f, 1.6f)));
        m.AddChild(Props.Part(Props.Box(0.5f, 0.4f, 0.4f), Black, new Vector3(0, 0.9f, 4.4f)));          // the spinner

        // The V-plough: two steel wings meeting in a point well ahead of the bumper, a pale cutting
        // edge along the bottom, orange and black chevrons along the top, a push frame to the chassis.
        float wingLen = new Vector2(BladeHalf, WingBack - BladeTip).Length();
        float ang = Mathf.RadToDeg(MathF.Atan2(BladeHalf, WingBack - BladeTip));
        pileL = pileR = null;
        foreach (float s in new[] { -1f, 1f })
        {
            var wing = new Node3D { Position = new Vector3(s * BladeHalf * 0.5f, 0, (BladeTip + WingBack) * 0.5f), RotationDegrees = new Vector3(0, s * ang, 0) };   // from the tip back to the wing's end
            m.AddChild(wing);
            wing.AddChild(Props.Part(Props.Box(0.18f, 1.8f, wingLen), Steel, new Vector3(0, 1.1f, 0), new Vector3(0, 0, s * 12f)));   // its top leaning back (the front face is +s x)
            // Ribs on the back of the blade.
            for (int k = 1; k < 5; k++)
                wing.AddChild(Props.Part(Props.Box(0.35f, 1.5f, 0.12f), Black, new Vector3(-s * 0.2f, 1.0f, -wingLen / 2f + k * wingLen / 5f)));
            wing.AddChild(Props.Part(Props.Box(0.2f, 0.16f, wingLen), Edge, new Vector3(s * 0.05f, 0.26f, 0)));
            for (int k = 0; k < 5; k++)
                wing.AddChild(Props.Part(Props.Box(0.2f, 0.2f, wingLen / 5f - 0.02f), k % 2 == 0 ? Orange : Black,
                    new Vector3(-s * 0.1f, 2.0f, -wingLen / 2f + (k + 0.5f) * wingLen / 5f)));
            // Miter both snow heaps on the truck's centre plane. Separate shortened,
            // offset prisms left a hole at the V's tip. The shared end profile also
            // stays joined as both heaps scale vertically with the same speed.
            var pile = new Node3D { Name = "SnowHeap", Position = new Vector3(0, 0.05f, 0) };
            pile.AddChild(Props.Part(SnowHeap(wing.Transform, s), Snow, Vector3.Zero));
            wing.AddChild(pile);
            if (s < 0) pileL = pile; else pileR = pile;
        }
        foreach (float x in new[] { -0.7f, 0.7f })
            m.AddChild(Props.Part(Props.Box(0.18f, 0.18f, 1.9f), Black, new Vector3(x, 0.85f, -5.1f)));
        SiberiaModelDetails.Plough(m);
        return m;
    }

    static Mesh SnowHeap(Transform3D wing, float side)
    {
        // Three vertices at the shared centre seam, three at the outer wing.
        // Define them in truck space so left and right meet exactly, then bake
        // them into the existing wing's local frame without changing tape parts.
        var points = new[]
        {
            new Vector3(0, 0.12f, BladeTip - 1.0f),
            new Vector3(0, 1.35f, BladeTip - 0.35f),
            new Vector3(0, 0.12f, BladeTip + 0.12f),
            new Vector3(side * (BladeHalf + 0.15f), 0.12f, WingBack - 1.0f),
            new Vector3(side * (BladeHalf + 0.15f), 1.35f, WingBack - 0.35f),
            new Vector3(side * (BladeHalf + 0.15f), 0.12f, WingBack + 0.12f),
        };
        var inverse = wing.AffineInverse();
        Vector3 centre = Vector3.Zero;
        for (int i = 0; i < points.Length; i++) { points[i] = inverse * points[i]; centre += points[i]; }
        centre /= points.Length;
        var mesh = new SurfaceTool();
        mesh.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var (ia, ib, ic) in new[]
        {
            (0, 1, 2), (3, 5, 4),
            (0, 3, 4), (0, 4, 1),
            (1, 4, 5), (1, 5, 2),
            (2, 5, 3), (2, 3, 0),
        })
        {
            Vector3 a = points[ia], b = points[ib], c = points[ic];
            Vector3 normal = (b - a).Cross(c - a).Normalized();
            if (normal.Dot((a + b + c) / 3f - centre) < 0) { (b, c) = (c, b); normal = -normal; }
            mesh.SetNormal(normal);
            // Godot's front faces wind clockwise when viewed from outside.
            mesh.AddVertex(c); mesh.AddVertex(b); mesh.AddVertex(a);
        }
        return mesh.Commit();
    }

    /// <summary>Where a wing of the blade throws snow from and which way (live play and the title replay's emitters); <paramref name="s"/> is -1 (left) or +1.</summary>
    internal static void SprayWing(Transform3D xf, float s, out Vector3 wing, out Vector3 outward)
    {
        wing = xf * new Vector3(s * (BladeHalf + 0.2f), 1.0f, WingBack + 0.3f);
        outward = (xf.Basis.Orthonormalized() * new Vector3(s, 0, -0.35f)).Normalized();
    }

    /// <summary>The models of the ploughs that throw snow (not the mixers), for the replay's emitters.</summary>
    internal IEnumerable<Node3D> TapePloughs() { foreach (var p in _ploughs) if (!p.Mixer) yield return p.Model; }
    /// <summary>The concrete mixers' models (they throw no snow but smoke like the ploughs), for the replay's emitters.</summary>
    internal IEnumerable<Node3D> TapeMixers() { foreach (var p in _ploughs) if (p.Mixer) yield return p.Model; }
    /// <summary>The exhaust stack behind the cab (the model's own space) and the engine's size against the player's car.</summary>
    internal static readonly Vector3 SootPipe = new(1.1f, 3.95f, -1.2f), MixerSootPipe = new(1.0f, 3.95f, -1.9f);
    internal const float SootPower = 1.2f;      // 60 % of the first guess (2): the ploughs and mixers smoked too much
}
