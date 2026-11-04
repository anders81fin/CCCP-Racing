using System;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Car;

/// <summary>
/// Visual feedback for tyres that spin, slide or lock: rubber marks and smoke on concrete,
/// flying gravel, wildly exaggerated mud (it sticks where it lands), water spray and grass clumps.
/// Particles are billboards in one MultiMesh; skid marks are one rolling ArrayMesh.
/// </summary>
public partial class TyreEffects : Node3D
{
    const int MaxParticles = 3000;
    const int MaxSegments = 2400;

    struct Particle
    {
        public Vector3 Pos, Vel;
        public float Age, Life, Size0, Size1, Gravity, Drag, Alpha;
        public Color Color;
        public bool Solid;      // hits the ground and stays there (mud), else fades out
        public bool Landed;
        public bool DamageReplay;
        public bool Replay;     // made for the world tape's replay (cleared with it, counted by tests)
    }

    TrackData _track;
    readonly Particle[] _p = new Particle[MaxParticles];
    int _count;
    public int ParticleCount => _count;
    MultiMesh _mm;
    float[] _buf;
    readonly RandomNumberGenerator _rng = new() { Seed = 1917 };
    /// <summary>Cars that can emit at once (LAN races: each car has its own wheel slots, 0 is the player's).</summary>
    public const int MaxCars = CCCPRacing.Net.LanSession.MaxPlayers;
    readonly float[] _emitAcc = new float[4 * MaxCars];

    // Skid marks ring buffer: 2 triangles per segment.
    readonly Vector3[] _skidV = new Vector3[MaxSegments * 6];
    readonly Color[] _skidC = new Color[MaxSegments * 6];
    int _segHead, _segCount;
    bool _skidDirty;
    readonly Vector3?[] _lastMark = new Vector3?[4 * MaxCars];
    ArrayMesh _skidMesh;
    StandardMaterial3D _skidMat;
    /// <summary>The skid marks' material (set on the mesh's surfaces as they are rebuilt; the night dims it).</summary>
    public Material SkidMaterial => _skidMat;

    static readonly Color Rubber = new(0.05f, 0.05f, 0.05f);
    static readonly Color Rut = new(0.22f, 0.15f, 0.09f);
    static readonly Color TyreSmoke = new(0.86f, 0.86f, 0.84f);
    // Stones must contrast with the gravel texture: dark chips and pale ones, no mid tones.
    static readonly Color[] GravelCols = { new(0.3f, 0.26f, 0.2f), new(0.4f, 0.35f, 0.28f), new(0.95f, 0.9f, 0.8f) };
    static readonly Color Dust = new(0.85f, 0.78f, 0.64f);
    static readonly Color[] MudCols = { new(0.28f, 0.18f, 0.1f), new(0.38f, 0.26f, 0.15f), new(0.22f, 0.14f, 0.08f) };
    static readonly Color[] GrassCols = { new(0.3f, 0.5f, 0.2f), new(0.4f, 0.3f, 0.18f) };
    static readonly Color[] SnowCols = { new(0.96f, 0.97f, 1f), new(0.88f, 0.91f, 0.96f), new(1f, 1f, 1f) };
    static readonly Color SnowRut = new(0.55f, 0.6f, 0.7f);
    static readonly Color IceScratch = new(0.92f, 0.96f, 1f);
    static readonly Color Water = new(0.75f, 0.82f, 0.9f);

    public void Init(TrackData track)
    {
        _track = track;

        _mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            InstanceCount = MaxParticles,
            VisibleInstanceCount = 0,
            Mesh = new QuadMesh { Size = Vector2.One },
        };
        _buf = new float[MaxParticles * 16];
        AddChild(new MultiMeshInstance3D
        {
            Multimesh = _mm,
            // Dust, smoke and flames alike: at night they dim only part way (headlights catch the dust).
            MaterialOverride = NightGlow.Keep(new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoTexture = BlobTexture(),
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            }, 0.25f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = 10000f,
        });

        _skidMesh = new ArrayMesh();
        _skidMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        AddChild(new MeshInstance3D { Mesh = _skidMesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    /// <summary>8×8 pixel disc so particles read as chunky blobs, not squares.</summary>
    static ImageTexture BlobTexture()
    {
        var img = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8);
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
        {
            float d = new Vector2(x - 3.5f, y - 3.5f).Length();
            img.SetPixel(x, y, d < 3.8f ? Colors.White : new Color(1, 1, 1, 0));
        }
        return ImageTexture.CreateFromImage(img);
    }

    public void Clear()
    {
        _count = 0;
        _segCount = 0;
        _segHead = 0;
        _skidDirty = true;
        Array.Fill(_lastMark, null);
    }

    /// <summary>A private packet viewer rewinds cosmetic emission as well as the visible particles.</summary>
    public void ResetPlaybackEmission()
    {
        Clear();_rng.Seed=1917;Array.Clear(_emitAcc);Array.Clear(_wetAcc);
        _replay=_damageReplay=false;_replayAge=0;
        if(_mm!=null) _mm.VisibleInstanceCount=0;
    }

    // ------------------------------------------------------------ emission (physics tick)

    public void Emit(CarSim car, float dt, int slot = 0)
    {
        float speed = car.Speed;
        Vector3 fwd = car.Forward, right = car.Right;
        float driveDir = car.Gear < 0 ? -1f : 1f;

        for (int wi = 0; wi < 4; wi++)
        {
            int i = slot * 4 + wi;     // this car's wheel slot: skid marks and emission carry on per wheel
            Wheel w = car.Wheels[wi];
            if (!w.Contact)
            {
                _lastMark[i] = null;
                continue;
            }

            float spin = w.Spinning ? 1f : 0f;
            float slide = Math.Clamp((Math.Abs(w.SlipLat) - 1.5f) / 4f, 0f, 1f);
            float lockA = w.Locked ? 0.8f : 0f;
            float intensity = Math.Max(spin, Math.Max(slide, lockA));
            SurfaceType s = w.Surface;

            // Marks.
            bool hard = s is SurfaceType.Concrete or SurfaceType.Pothole or SurfaceType.Runway;
            bool soft = s is SurfaceType.Mud or SurfaceType.Field or SurfaceType.Puddle or SurfaceType.Grass or SurfaceType.Dirt or SurfaceType.Water or SurfaceType.Shallow;
            if (intensity > 0.25f && hard)
                AddMark(i, w.ContactPoint, new Color(Rubber, 0.3f + 0.4f * intensity));
            else if (s == SurfaceType.Ice && intensity > 0.25f)
                AddMark(i, w.ContactPoint, new Color(IceScratch, 0.25f + 0.3f * intensity));     // studs scratch the ice white
            else if (s is SurfaceType.Snow or SurfaceType.DeepSnow && (intensity > 0.2f || s == SurfaceType.DeepSnow && speed > 2f))
                AddMark(i, w.ContactPoint, new Color(SnowRut, s == SurfaceType.DeepSnow ? 0.5f : 0.3f));
            else if (soft && (intensity > 0.25f || (s is SurfaceType.Mud or SurfaceType.Field && speed > 2f)))
                AddMark(i, w.ContactPoint, new Color(Rut, s == SurfaceType.Mud ? 0.55f : 0.35f));
            else
                _lastMark[i] = null;

            // Spray direction: spinning wheels throw backwards, sliding wheels plough sideways.
            Vector3 throwDir = -fwd * driveDir * spin;
            float latSign = Math.Sign(w.SlipLat);
            Vector3 plough = right * latSign * slide;
            Vector3 carry = car.Vel * 0.35f;

            float rate;
            switch (s)
            {
                case SurfaceType.Concrete:
                case SurfaceType.Pothole:
                case SurfaceType.Runway:
                    rate = intensity > 0.2f ? 45f * intensity : 0f;
                    Spawn(i, rate, dt, () => Add(w.ContactPoint + Vector3.Up * 0.3f,
                        carry * 0.5f + throwDir * 2f + plough * 1.5f + Rand(1.2f, 1.2f, 0.8f),
                        _rng.RandfRange(1.1f, 1.8f), 0.5f, _rng.RandfRange(2.5f, 3.8f), -1.2f, 1.6f,
                        TyreSmoke, 0.55f, false));
                    break;

                case SurfaceType.Gravel:
                case SurfaceType.Dirt:
                    rate = intensity > 0.1f ? 90f * intensity : 0f;
                    Spawn(i, rate, dt, () =>
                    {
                        if (_rng.Randf() < 0.3f)
                            Add(w.ContactPoint + Vector3.Up * 0.3f, carry * 0.4f + throwDir * 1.5f + Rand(1f, 0.8f, 0.6f),
                                _rng.RandfRange(0.9f, 1.5f), 0.5f, 2.8f, -0.5f, 1.8f, Dust, 0.5f, false);
                        else
                            Add(w.ContactPoint + Vector3.Up * 0.15f,
                                carry + throwDir * _rng.RandfRange(4f, 8f) + plough * _rng.RandfRange(3f, 6f) + Rand(1.5f, 0, 1.5f) + Vector3.Up * _rng.RandfRange(2f, 5f),
                                _rng.RandfRange(0.5f, 0.8f), 0.38f, 0.32f, 20f, 0.3f, Pick(GravelCols), 1f, false);
                    });
                    break;

                case SurfaceType.Mud:
                case SurfaceType.Field:
                {
                    // Exaggerated on purpose: the Soviet mud goes everywhere, even when just rolling.
                    float roll = s == SurfaceType.Mud ? Math.Clamp(speed / 12f, 0f, 1f) * 0.35f : 0f;
                    rate = 170f * Math.Max(intensity, roll);
                    Spawn(i, rate, dt, () =>
                        Add(w.ContactPoint + Vector3.Up * 0.2f,
                            carry * 0.6f + throwDir * _rng.RandfRange(6f, 13f) + plough * _rng.RandfRange(5f, 10f) + Rand(3f, 0, 3f) + Vector3.Up * _rng.RandfRange(6f, 13f),
                            _rng.RandfRange(1.6f, 2.8f), _rng.RandfRange(0.6f, 1.1f), _rng.RandfRange(0.45f, 0.8f), 16f, 0.15f, Pick(MudCols), 1f, true));
                    break;
                }

                case SurfaceType.Water:
                    // Wading: a bow wave off every wheel, higher and wider the faster it goes.
                    rate = 110f * Math.Clamp(speed / 8f, 0.15f, 1.2f);
                    Spawn(i, rate, dt, () =>
                        Add(w.ContactPoint + Vector3.Up * 0.45f,
                            carry * 0.5f + throwDir * 5f + plough * 6f + Rand(3.5f, 0, 3.5f) + Vector3.Up * _rng.RandfRange(3f, 8f + speed * 0.4f),
                            _rng.RandfRange(0.5f, 0.9f), 0.4f, 0.25f, 14f, 0.4f, Water, 0.85f, false));
                    break;

                case SurfaceType.Puddle:
                case SurfaceType.Shallow:
                    rate = 60f * Math.Max(intensity, Math.Clamp(speed / 10f, 0f, 1f) * 0.6f);
                    Spawn(i, rate, dt, () =>
                        Add(w.ContactPoint + Vector3.Up * 0.15f,
                            carry * 0.6f + throwDir * 4f + plough * 4f + Rand(2.5f, 0, 2.5f) + Vector3.Up * _rng.RandfRange(2f, 6f),
                            _rng.RandfRange(0.4f, 0.7f), 0.3f, 0.2f, 14f, 0.4f, Water, 0.8f, false));
                    break;

                case SurfaceType.Ice:
                    // A fine white powder off the studs, only when the wheel slides or spins.
                    rate = intensity > 0.3f ? 30f * intensity : 0f;
                    Spawn(i, rate, dt, () => Add(w.ContactPoint + Vector3.Up * 0.2f,
                        carry * 0.5f + throwDir * 2f + plough * 1.5f + Rand(0.8f, 0.6f, 0.8f),
                        _rng.RandfRange(0.6f, 1.0f), 0.3f, _rng.RandfRange(1.2f, 2f), -0.3f, 1.2f, IceScratch, 0.5f, false));
                    break;

                case SurfaceType.Snow:
                case SurfaceType.DeepSnow:
                {
                    // Powder: a rooster tail off the spinning wheels, a cloud when sliding; deep snow
                    // sprays even when just rolling (the bumper ploughs it).
                    float roll = s == SurfaceType.DeepSnow ? Math.Clamp(speed / 10f, 0f, 1f) * 0.5f : Math.Clamp(speed / 30f, 0f, 1f) * 0.12f;
                    rate = 130f * Math.Max(intensity, roll);
                    Spawn(i, rate, dt, () =>
                    {
                        if (_rng.Randf() < 0.35f)
                            Add(w.ContactPoint + Vector3.Up * 0.3f, carry * 0.5f + throwDir * 2f + plough * 2f + Rand(1.2f, 0.8f, 1.2f),
                                _rng.RandfRange(1.0f, 1.8f), 0.6f, 3.2f, -0.4f, 1.6f, SnowCols[0], 0.7f, false);   // the powder cloud
                        else
                            Add(w.ContactPoint + Vector3.Up * 0.15f,
                                carry * 0.6f + throwDir * _rng.RandfRange(4f, 10f) + plough * _rng.RandfRange(3f, 7f) + Rand(2f, 0, 2f) + Vector3.Up * _rng.RandfRange(3f, 8f),
                                _rng.RandfRange(0.6f, 1.1f), 0.3f, 0.25f, 14f, 0.4f, Pick(SnowCols), 1f, false);
                    });
                    break;
                }

                case SurfaceType.Grass:
                    rate = intensity > 0.2f ? 40f * intensity : 0f;
                    Spawn(i, rate, dt, () =>
                        Add(w.ContactPoint + Vector3.Up * 0.15f,
                            carry * 0.6f + throwDir * _rng.RandfRange(3f, 6f) + plough * 3f + Rand(1.5f, 0, 1.5f) + Vector3.Up * _rng.RandfRange(2f, 4f),
                            _rng.RandfRange(0.6f, 1f), 0.22f, 0.18f, 14f, 0.8f, Pick(GrassCols), 1f, true));
                    break;
            }

            // A wet road (the rain, Surfaces.Wetness): a fine grey spray off every tyre, thicker the
            // faster and the wetter, hanging behind the car.
            if (Surfaces.Wetness > 0.2f && speed > 5f && s is SurfaceType.Concrete or SurfaceType.Pothole or SurfaceType.Runway or SurfaceType.Dirt or SurfaceType.Gravel)
            {
                _wetAcc[i] += 32f * Surfaces.Wetness * Math.Clamp((speed - 5f) / 20f, 0f, 1f) * dt;
                while (_wetAcc[i] >= 1f)
                {
                    _wetAcc[i] -= 1f;
                    Add(w.ContactPoint + Vector3.Up * 0.25f, carry * 0.55f - fwd * speed * 0.12f + Rand(0.8f, 0.3f, 0.8f) + Vector3.Up * _rng.RandfRange(0.6f, 1.4f),
                        _rng.RandfRange(0.5f, 0.85f), 0.3f, _rng.RandfRange(1.2f, 1.8f), -0.3f, 1.6f, Water, 0.3f, false);
                }
            }
            else _wetAcc[i] = 0;
        }
    }

    readonly float[] _wetAcc = new float[4 * MaxCars];

    float _sprayAcc;

    /// <summary>
    /// A car ploughing through a snow bank (plough: speed m/s x depth m): a thick spray of powder
    /// thrown up and away off the bank side, chunks and a cloud, as long as it ploughs.
    /// </summary>
    public void SnowSpray(Vector3 at, float plough, Vector3 carVel, Vector3 bankDir, float dt)
    {
        _sprayAcc += Math.Min(plough, 14f) * 45f * dt;
        while (_sprayAcc >= 1f)
        {
            _sprayAcc -= 1f;
            Vector3 v = carVel * 0.45f + bankDir * _rng.RandfRange(1f, 3f + plough * 0.4f) + Rand(1.6f, 0, 1.6f)
                + Vector3.Up * _rng.RandfRange(2.5f, 4.5f + plough * 0.5f);
            if (_rng.Randf() < 0.35f)
                Add(at + Vector3.Up * 0.7f, v * 0.35f, _rng.RandfRange(1.2f, 2.2f), 0.9f, 3.8f, -0.3f, 1.3f, SnowCols[0], 0.8f, false);
            else
                Add(at + Vector3.Up * 0.5f, v, _rng.RandfRange(0.6f, 1.2f), 0.4f, 0.3f, 13f, 0.4f, Pick(SnowCols), 1f, false);
        }
    }

    /// <summary>A snowplough's wing throwing snow out sideways at speed (m/s): a thick arc of chunks and a cloud. acc: the caller's emission accumulator.</summary>
    public void PloughSpray(Vector3 at, Vector3 outward, Vector3 vel, float speed, ref float acc, float dt)
    {
        acc += speed * 14f * dt;
        while (acc >= 1f)
        {
            acc -= 1f;
            Vector3 v = vel * 0.6f + outward * _rng.RandfRange(3f, 4f + speed * 0.5f) + Rand(1.2f, 0, 1.2f) + Vector3.Up * _rng.RandfRange(3f, 5f + speed * 0.3f);
            if (_rng.Randf() < 0.3f)
                Add(at, v * 0.4f, _rng.RandfRange(1.4f, 2.4f), 1.0f, 4.2f, -0.3f, 1.2f, SnowCols[0], 0.75f, false);
            else
                Add(at, v, _rng.RandfRange(0.7f, 1.3f), 0.45f, 0.35f, 13f, 0.35f, Pick(SnowCols), 1f, false);
        }
    }

    /// <summary>A car shoves into a snow bank at this many m/s: powder bursts up and over it.</summary>
    public void SnowPuff(Vector3 at, float hit, Vector3 carVel)
    {
        int count = (int)Math.Clamp(hit * 6f, 6f, 70f);
        for (int k = 0; k < count; k++)
        {
            bool cloud = _rng.Randf() < 0.4f;
            Vector3 v = carVel * 0.25f + Rand(1.5f + hit * 0.25f, 0, 1.5f + hit * 0.25f) + Vector3.Up * _rng.RandfRange(1.5f, 2.5f + hit * 0.5f);
            if (cloud)
                Add(at + Vector3.Up * 0.8f, v * 0.4f, _rng.RandfRange(1.0f, 2.0f), 0.8f, 3.5f, -0.3f, 1.4f, SnowCols[0], 0.75f, false);
            else
                Add(at + Vector3.Up * 0.6f, v, _rng.RandfRange(0.6f, 1.2f), 0.35f, 0.3f, 14f, 0.4f, Pick(SnowCols), 1f, false);
        }
    }

    /// <summary>The car comes down after a jump: every wheel that has met the ground throws a ring of dust or snow outwards and a few clods up.</summary>
    public void Landing(CarSim car, float k)
    {
        for (int wi = 0; wi < 4; wi++)
        {
            Wheel w = car.Wheels[wi];
            if (!w.Contact) continue;
            SurfaceType s = w.Surface;
            bool snow = s is SurfaceType.Snow or SurfaceType.DeepSnow or SurfaceType.Ice;
            bool hard = s is SurfaceType.Concrete or SurfaceType.Pothole or SurfaceType.Runway;
            int count = (int)(6 + 18 * k);
            for (int n = 0; n < count; n++)
            {
                float a = _rng.RandfRange(0f, Mathf.Tau), speed = _rng.RandfRange(2f, 5f + 5f * k);
                Vector3 v = new(MathF.Cos(a) * speed, _rng.RandfRange(0.6f, 2.2f + 2f * k), MathF.Sin(a) * speed);
                Color c = snow ? SnowCols[0] : hard ? TyreSmoke : Dust;
                Add(w.ContactPoint + Vector3.Up * 0.2f, v, _rng.RandfRange(0.9f, 1.8f), 0.5f, _rng.RandfRange(2.5f, 4.5f + 2f * k), -0.4f, 1.6f, c, 0.55f, false);
            }
            if (!hard && !snow)
                for (int n = 0; n < (int)(2 + 4 * k); n++)
                    Clod(w.ContactPoint + Vector3.Up * 0.2f, new Vector3(_rng.RandfRange(-2f, 2f), _rng.RandfRange(3f, 6f + 3f * k), _rng.RandfRange(-2f, 2f)), _rng.RandfRange(0.15f, 0.3f));
        }
    }

    static readonly Color[] FlameCols = { new(1f, 0.92f, 0.5f), new(1f, 0.6f, 0.15f), new(1f, 0.35f, 0.05f) };
    static readonly Color ExhaustSmoke = new(0.3f, 0.3f, 0.3f);

    float _sootAcc, _sootSpread;

    /// <summary>
    /// A diesel's black smoke: an exaggerated thick dark cloud from the pipes whenever the pedal is down (more when it is floored and the revs are still low, an old
    /// engine pumping in more fuel than it can burn), nothing on a lift. Driven by the car's state, so the replay and the tape draw it again by themselves.
    /// </summary>
    public void DieselSoot(CarSim car, float dt)
    {
        // Always sooty, even with the pedal up (an old diesel never burns clean); the throttle multiplies it.
        float th = Math.Clamp(car.Throttle, 0f, 1f);
        float lowRev = 1f - Math.Clamp((car.Rpm - 1500f) / 3000f, 0f, 1f);
        float rate = 9f + 75f * MathF.Pow(th, 1.2f) * (0.6f + 0.4f * lowRev);        // puffs per second, both pipes together
        _sootAcc += rate * dt;
        Vector3 back = -car.Forward;
        // A car that stands still is wrapped in its own smoke: the puffs hang about and rise over it instead of streaming away.
        float still = 1f - Math.Clamp(car.Vel.Length() / 8f, 0f, 1f);
        // The cloud starts at the pipes and spreads while the car stands (the faster the more throttle, 2-6 s to wrap the car); it thins out again once the car moves.
        _sootSpread = Math.Clamp(_sootSpread + (still > 0.5f ? dt / (6f - 4f * th) : -dt / 2f), 0f, 1f);
        while (_sootAcc >= 1f)
        {
            _sootAcc -= 1f;
            float px = _rng.Randf() < 0.5f ? 0.33f : 0.47f;
            Vector3 pipe = car.Pos + car.Rot * new Vector3(px, -0.40f, 2.02f);
            float k = 0.5f + 0.5f * th;
            Vector3 at = pipe + back * 0.15f;
            if (_rng.Randf() < 0.45f * _sootSpread * still)
            {
                float w = 0.3f + 1.4f * _sootSpread;      // the reach of the cloud from the pipes: first the tail, then the whole car and a little beyond
                at = car.Pos + car.Rot * new Vector3(_rng.RandfRange(-w, w), _rng.RandfRange(-0.3f, 0.3f + 0.8f * _sootSpread), Mathf.Lerp(2.1f, _rng.RandfRange(-1.4f, 2.2f), _sootSpread));
            }
            Add(at, car.Vel * 0.6f + back * _rng.RandfRange(Mathf.Lerp(2f, 0.3f, still), Mathf.Lerp(5f, 1.2f, still)) + Rand(0.5f, 0.4f, 0.5f) + Vector3.Up * (0.8f + 1.4f * still),
                _rng.RandfRange(2.2f, 3.4f) * (1f + 0.5f * still), 0.7f * k, 5.5f * k * (1f + 0.4f * still), -0.4f, 0.9f, SootBlack, 0.85f * k, false);      // exaggerated on purpose: a rolling-coal wall
        }
    }
    static readonly Color SootBlack = new(0.04f, 0.04f, 0.04f);

    /// <summary>Backfire: a short tongue of flame and a puff of dark smoke from the twin pipes.</summary>
    public void ExhaustFlame(CarSim car, float strength)
    {
        Vector3 back = -car.Forward;
        foreach (float px in new[] { 0.33f, 0.47f })
        {
            Vector3 pipe = car.Pos + car.Rot * new Vector3(px, -0.40f, 2.02f);
            // Exaggerated on purpose so it reads at 480×270: a fat fireball, bright core first.
            int n = 3 + (int)(strength * 4f);
            for (int k = 0; k < n; k++)
                Add(pipe + back * _rng.RandfRange(0.1f, 0.6f), car.Vel + back * _rng.RandfRange(4f, 9f) + Rand(1f, 0.6f, 1f),
                    _rng.RandfRange(0.12f, 0.22f), 0.5f + 0.5f * strength, 0.2f, -1f, 3f, FlameCols[Math.Min(k, 2)], 1f, false);
            Add(pipe + back * 0.3f, car.Vel * 0.5f + back * 2f + Rand(0.4f, 0.3f, 0.4f),
                _rng.RandfRange(0.4f, 0.7f), 0.25f, 1.1f, -0.8f, 2f, ExhaustSmoke, 0.5f, false);
        }
    }

    /// <summary>Any particle: fire, debris, sparks. Solid ones land and stick; others fade.</summary>
    public void Burst(Vector3 pos, Vector3 vel, Color color, float alpha, float size0, float size1, float life,
        float gravity, float drag, bool solid) =>
        Add(pos, vel, life, size0, size1, gravity, drag, color, alpha, solid);

    /// <summary>A single smoke/dust puff that fades and grows (exhaust, dust clouds).</summary>
    public void Smoke(Vector3 pos, Vector3 vel, Color color, float alpha, float size0, float size1, float life) =>
        Add(pos, vel, life, size0, size1, -0.6f, 1.2f, color, alpha, false);

    /// <summary>Clods thrown up by a plough share or similar: solid, they land and stick.</summary>
    public void Clod(Vector3 pos, Vector3 vel, float size)
    {
        Add(pos, vel, _rng.RandfRange(1.2f, 2f), size, size * 0.8f, 14f, 0.2f, Pick(MudCols), 1f, true);
    }

    void Spawn(int wheel, float rate, float dt, Action spawn)
    {
        if (rate <= 0f) { _emitAcc[wheel] = 0; return; }
        _emitAcc[wheel] += rate * dt;
        while (_emitAcc[wheel] >= 1f)
        {
            _emitAcc[wheel] -= 1f;
            spawn();
        }
    }

    Vector3 Rand(float x, float y, float z) =>
        new(_rng.RandfRange(-x, x), _rng.RandfRange(-y, y), _rng.RandfRange(-z, z));

    Color Pick(Color[] cols) => cols[_rng.RandiRange(0, cols.Length - 1)];

    void Add(Vector3 pos, Vector3 vel, float life, float size0, float size1, float gravity, float drag,
        Color color, float alpha, bool solid)
    {
        if (_count >= MaxParticles)
        {
            // Recycle the oldest-looking slot.
            int k = _rng.RandiRange(0, MaxParticles - 1);
            _p[k] = _p[--_count];
        }
        var q = new Particle
        {
            Pos = pos, Vel = vel, Life = life, Size0 = size0, Size1 = size1,
            Gravity = gravity, Drag = drag, Color = color, Alpha = alpha, Solid = solid, Replay = _replay, DamageReplay = _damageReplay,
        };
        if (_replay && _replayAge > 0f)
        {
            // The world tape restores an effect by its age: the particle has already been flying for that long (the same steps as Tick).
            for (float t = 0; t < _replayAge; t += 1f / 60f) if (!Step(ref q, Math.Min(1f / 60f, _replayAge - t))) return;
        }
        _p[_count++] = q;
    }

    bool _replay, _damageReplay;
    float _replayAge;
    /// <summary>Particles added until <see cref="EndReplay"/> belong to the world tape's replay: they are tagged and start <paramref name="age"/> seconds into their life.</summary>
    public void BeginReplay(float age) { _replay = true; _replayAge = age; }
    public void EndReplay() { _replay = _damageReplay = false; _replayAge = 0f; }
    public void BeginDamageReplay(float age) { BeginReplay(age); _damageReplay = true; }
    public void ClearDamageReplay()
    {
        int n = 0;
        for (int i = 0; i < _count; i++) if (!_p[i].DamageReplay) _p[n++] = _p[i];
        _count = n;
    }
    /// <summary>Particles made for the replay that are alive now.</summary>
    public int ReplayParticles { get { int n = 0; for (int i = 0; i < _count; i++) if (_p[i].Replay) n++; return n; } }
    /// <summary>The replay's effects go (a seek, the end of the replay): its particles, and only its.</summary>
    public void ClearReplay()
    {
        int n = 0;
        for (int i = 0; i < _count; i++) if (!_p[i].Replay) _p[n++] = _p[i];
        _count = n;
    }

    void AddMark(int wheel, Vector3 p, Color c)
    {
        if (_lastMark[wheel] is not Vector3 last || last.DistanceTo(p) > 3f)
        {
            _lastMark[wheel] = p;
            return;
        }
        Vector3 d = p - last;
        d.Y = 0;
        if (d.LengthSquared() < 0.3f * 0.3f) return;
        Vector3 side = new Vector3(-d.Z, 0, d.X).Normalized() * 0.12f;

        Vector3 a0 = Ground(last - side), a1 = Ground(last + side);
        Vector3 b0 = Ground(p - side), b1 = Ground(p + side);
        int o = _segHead * 6;
        _skidV[o] = a0; _skidV[o + 1] = a1; _skidV[o + 2] = b1;
        _skidV[o + 3] = a0; _skidV[o + 4] = b1; _skidV[o + 5] = b0;
        for (int k = 0; k < 6; k++) _skidC[o + k] = c;
        _segHead = (_segHead + 1) % MaxSegments;
        _segCount = Math.Min(_segCount + 1, MaxSegments);
        _skidDirty = true;
        _lastMark[wheel] = p;
    }

    Vector3 Ground(Vector3 p) => new(p.X, _track.MeshHeight(p.X, p.Z) + 0.05f, p.Z);

    // ------------------------------------------------------------ update + render (frame)

    /// <summary>One step of one particle; false when it is over. (Tick and the replay's age restore share it.)</summary>
    bool Step(ref Particle q, float dt)
    {
        q.Age += dt;
        if (q.Age >= q.Life) return false;
        if (!q.Landed)
        {
            q.Vel.Y -= q.Gravity * dt;
            q.Vel *= 1f - Math.Min(1f, q.Drag * dt);
            q.Pos += q.Vel * dt;
            if (q.Gravity > 0f)
            {
                float g = _track.MeshHeight(q.Pos.X, q.Pos.Z);
                if (q.Pos.Y < g + 0.05f)
                {
                    if (q.Solid)
                    {
                        // Splat: stick to the ground and linger a while.
                        q.Pos.Y = g + 0.06f;
                        q.Landed = true;
                        q.Life = q.Age + 2.5f;
                    }
                    else q.Age = q.Life;
                }
            }
        }
        return true;
    }

    /// <summary>Advance hidden replay history without uploading/rebuilding a frame nobody sees.</summary>
    public void AdvancePlayback(float dt)
    {
        int n = 0;
        for (int i = 0; i < _count; i++)
        {
            ref Particle q = ref _p[i];
            if (!Step(ref q, dt)) continue;
            _p[n++] = q;
        }
        _count = n;
    }

    public void Tick(float dt, Basis cam)
    {
        AdvancePlayback(dt);
        for (int i = 0; i < _count; i++)
        {
            ref Particle q = ref _p[i];
            float t = q.Age / q.Life;
            float size = Mathf.Lerp(q.Size0, q.Size1, t);
            if (q.Landed) size = q.Size1 * 1.4f;
            float alpha = q.Solid || q.Gravity > 5f ? q.Alpha * Math.Clamp((1f - t) * 5f, 0f, 1f) : q.Alpha * (1f - t);
            Basis b = cam.Scaled(Vector3.One * size);
            int o = i * 16;
            _buf[o + 0] = b.X.X; _buf[o + 1] = b.Y.X; _buf[o + 2] = b.Z.X; _buf[o + 3] = q.Pos.X;
            _buf[o + 4] = b.X.Y; _buf[o + 5] = b.Y.Y; _buf[o + 6] = b.Z.Y; _buf[o + 7] = q.Pos.Y;
            _buf[o + 8] = b.X.Z; _buf[o + 9] = b.Y.Z; _buf[o + 10] = b.Z.Z; _buf[o + 11] = q.Pos.Z;
            _buf[o + 12] = q.Color.R; _buf[o + 13] = q.Color.G; _buf[o + 14] = q.Color.B; _buf[o + 15] = alpha;
        }
        _mm.Buffer = _buf;
        _mm.VisibleInstanceCount = _count;

        if (_skidDirty)
        {
            _skidDirty = false;
            _skidMesh.ClearSurfaces();
            if (_segCount > 0)
            {
                int verts = _segCount * 6;
                var v = new Vector3[verts];
                var c = new Color[verts];
                Array.Copy(_skidV, v, verts);
                Array.Copy(_skidC, c, verts);
                var arrays = new Godot.Collections.Array();
                arrays.Resize((int)Mesh.ArrayType.Max);
                arrays[(int)Mesh.ArrayType.Vertex] = v;
                arrays[(int)Mesh.ArrayType.Color] = c;
                _skidMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                _skidMesh.SurfaceSetMaterial(0, _skidMat);
            }
        }
    }
}
