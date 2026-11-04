using System;
using CCCPRacing.Audio;
using Godot;

namespace CCCPRacing.Track;

/// <summary>Two nude adult visitors on a drunken duck hunt. The Finnish guest always misses.</summary>
public partial class PartyRowboat : Node3D
{
    public static readonly Vector2 PondCentre = new(220, 205);
    Node3D _boat, _leftOar, _rightOar, _body, _passenger;
    MultiMeshInstance3D _ripples;
    TrackData _track;
    float _clock;
    Node3D _gun, _barrels, _flash, _smoke, _splash;
    readonly MeshInstance3D[] _droplets = new MeshInstance3D[8];
    MeshInstance3D _splashRing;
    StandardMaterial3D _smokeMat;
    Vector3 _impact;
    float _nextShot = 6.5f, _lastShot = -100f, _recoil, _splashAge = 10f, _impactDelay = -1f;
    int _shots;
    // The guest's aim in the boat (0: at the rower's back), how far the gun is up (0 resting, 1 aimed),
    // the boat's head start on its loop (a school scene's shot) and whether the rower is in the way.
    float _aimYaw = MathF.PI, _ready, _phase, _animTime;
    bool _blocked;

    /// <summary>The hull and both seated figures share one scale; the 4.8 m authored hull becomes a 4.3 m rowboat.</summary>
    public const float FigureScale = 0.9f;
    const float RowRate = 0.05f, RowX = 10f, RowZ = 2.4f, GuestZ = 1.43f;
    /// <summary>Half the angle round the rower's back (seen from the guest) the gun never points into.</summary>
    const float RowerShadow = 1.15f;
    static readonly Vector2 RowCentre = new(220, 205.5f);

    public Vector3 BoatPosition => _boat?.GlobalPosition ?? Vector3.Zero;
    public Vector3 DuckPosition => new(PartyPondDuck.PositionAt(_clock).X, _track.WaterLevel + PartyPondDuck.Height * 0.5f, PartyPondDuck.PositionAt(_clock).Y);
    public float NextShotIn => _nextShot - _clock;
    public float SinceShot => _clock - _lastShot;
    public int ShotsFired => _shots;

    /// <summary>Give a scenic camera time to arrive before the visitor's next inevitable miss. The boat
    /// starts further round its loop if need be, so the duck is clear of the rower then (the scene cuts
    /// to it: the jump is not seen; the boat's loop never comes near the duck's).</summary>
    public void CueSchoolShot(float delay)
    {
        _nextShot = _clock + Math.Max(1.5f, delay);
        for (int i = 0; i < 64 && !(Clear(_nextShot - 3f) && Clear(_nextShot) && Clear(_nextShot + 1f)); i++) _phase += 2f;
        bool Clear(float t) => ClearShot(t + _phase, t, _shots);
    }

    /// <summary>The pellets land four metres to either side of the duck, always in the pond.</summary>
    public static Vector2 MissAt(float time, int shot) => PartyPondDuck.PositionAt(time)
        + new Vector2(shot % 2 == 0 ? 4f : -4f, 0);

    /// <summary>A wide loop over the pond's north half: the duck's waters stay to the south, the pier's landing north.</summary>
    public static Vector2 PositionAt(float time) => RowCentre + new Vector2(MathF.Cos(time * RowRate) * RowX, MathF.Sin(time * RowRate) * RowZ);
    static Vector2 Heading(float time) => new(-RowX * MathF.Sin(time * RowRate), RowZ * MathF.Cos(time * RowRate));

    /// <summary>Where a point on the water lies as seen from the guest's seat (boat at boatTime): 0 straight
    /// at the rower's back, ±pi straight astern.</summary>
    static float GuestBearing(float boatTime, Vector2 at)
    {
        Vector2 d = at - PositionAt(boatTime);
        float yaw = TrackData.YawFor(Heading(boatTime)), c = MathF.Cos(yaw), s = MathF.Sin(yaw);
        var local = new Vector2(d.X * c - d.Y * s, d.X * s + d.Y * c) - new Vector2(0, GuestZ * FigureScale);
        return TrackData.YawFor(local);
    }

    /// <summary>Both the duck and where this shot lands lie clear of the rower: the guest may fire.</summary>
    public static bool ClearShot(float boatTime, float time, int shot) =>
        MathF.Abs(GuestBearing(boatTime, PartyPondDuck.PositionAt(time))) > RowerShadow
        && MathF.Abs(GuestBearing(boatTime, MissAt(time, shot))) > RowerShadow;
    public static PartyRowboat Build(TrackData t)
    {
        var root = new PartyRowboat { Name = "PartyOfficialRowing", _track = t };
        if (t.Id != "ilyich" || float.IsNaN(t.WaterLevel)) { root.SetProcess(false); return root; }
        root.BuildModel(); root.BuildHuntEffects(); root.Animate(0);
        return root;
    }

    void BuildModel()
    {
        _boat = new Node3D { Scale = Vector3.One * FigureScale }; AddChild(_boat);
        void Box(Node3D parent, Vector3 size, Vector3 at, Color color, Vector3 rotation = default) => parent.AddChild(Props.Part(Props.Box(size.X, size.Y, size.Z), color, at, rotation));
        Color wood = new(0.33f, 0.19f, 0.085f), plank = new(0.58f, 0.39f, 0.18f), cream = new(0.75f, 0.69f, 0.49f);
        // A pointed, open hull. The broad sides conceal the seated figure's lap naturally.
        Box(_boat, new Vector3(1.65f, 0.17f, 3.5f), new Vector3(0, 0.08f, 0), wood);
        // The bottom goes on into the pointed bow and stern, narrowing with their sides: no water shows through.
        foreach (float end in new[] { -1f, 1f })
            for (int i = 0; i < 4; i++)
                Box(_boat, new Vector3(2f * (0.86f - i * 0.22f), 0.17f, 0.22f), new Vector3(0, 0.08f, end * (1.85f + i * 0.2f)), wood);
        foreach (float side in new[] { -1f, 1f })
        {
            for (int row = 0; row < 3; row++)
                Box(_boat, new Vector3(0.15f, 0.17f, 3.5f), new Vector3(side * (0.80f + row * 0.06f), 0.20f + row * 0.17f, 0), plank);
            Box(_boat, new Vector3(0.20f, 0.10f, 3.6f), new Vector3(side * 0.94f, 0.72f, 0), cream);
            foreach (float end in new[] { -1f, 1f })
                Box(_boat, new Vector3(0.15f, 0.60f, 1.22f), new Vector3(side * 0.47f, 0.40f, end * 2.15f), plank, new Vector3(0, -side * end * 48, 0));
        }
        foreach (float z in new[] { -0.8f, 0.20f, 1.40f }) Box(_boat, new Vector3(1.70f, 0.12f, 0.43f), new Vector3(0, 0.48f, z), cream);
        DrunkTractor.BatchStatic(_boat, null);
        _body = new Node3D { Position = new Vector3(0, 0, -0.35f) }; _boat.AddChild(_body);
        Color skin = new(0.86f, 0.62f, 0.44f), shade = new(0.72f, 0.46f, 0.32f), cap = new(0.26f, 0.29f, 0.16f);
        void Ball(Vector3 at, Vector3 scale, Color color)
        {
            var part = Props.Part(Props.Ball(1, 12), color, at); part.Scale = scale; _body.AddChild(part);
        }
        Ball(new Vector3(0, 1.09f, 0.40f), new Vector3(0.57f, 0.67f, 0.46f), skin); // broad bare chest and belly
        Ball(new Vector3(0, 1.83f, 0.42f), new Vector3(0.30f, 0.34f, 0.28f), skin);
        Ball(new Vector3(0, 1.78f, 0.12f), new Vector3(0.095f, 0.09f, 0.13f), shade);
        foreach (float side in new[] { -1f, 1f })
        {
            Ball(new Vector3(side * 0.35f, 0.41f, -0.20f), new Vector3(0.19f, 0.18f, 0.55f), skin);
            Box(_body, new Vector3(0.25f, 0.48f, 0.25f), new Vector3(side * 0.48f, 1.13f, 0.05f), skin, new Vector3(40, 0, side * -15));
            Box(_body, new Vector3(0.18f, 0.20f, 0.44f), new Vector3(side * 0.48f, 0.93f, -0.13f), shade);
            Box(_body, new Vector3(0.16f, 0.10f, 0.06f), new Vector3(side * 0.13f, 1.86f, 0.16f), new Color(0.10f, 0.12f, 0.10f)); // sunglasses
        }
        Box(_body, new Vector3(0.66f, 0.13f, 0.58f), new Vector3(0, 2.12f, 0.42f), cap);
        Box(_body, new Vector3(0.60f, 0.07f, 0.26f), new Vector3(0, 2.04f, 0.10f), new Color(0.12f, 0.13f, 0.09f));
        Box(_body, new Vector3(0.14f, 0.14f, 0.04f), new Vector3(0, 2.10f, 0.10f), new Color(0.77f, 0.59f, 0.20f));
        DrunkTractor.BatchStatic(_body, null);
        _passenger = BuildPassenger(); _boat.AddChild(_passenger);
        Node3D Oar(float side)
        {
            var pivot = new Node3D { Position = new Vector3(side * 0.94f, 0.70f, -0.10f) }; _boat.AddChild(pivot);
            Box(pivot, new Vector3(2.5f, 0.07f, 0.09f), new Vector3(side * 0.60f, 0, 0), plank);
            Box(pivot, new Vector3(0.66f, 0.08f, 0.30f), new Vector3(side * 1.65f, 0, 0), cream);
            DrunkTractor.BatchStatic(pivot, null); return pivot;
        }
        _leftOar = Oar(-1); _rightOar = Oar(1);
        _ripples = new MultiMeshInstance3D
        {
            Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new TorusMesh { InnerRadius = 0.88f, OuterRadius = 1f, Rings = 16, RingSegments = 3 }, InstanceCount = 6 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.59f, 0.70f, 0.61f, 0.25f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_ripples);
    }

    Node3D BuildPassenger()
    {
        var guest = new Node3D { Name = "FinnishSaunaGuest", Position = new Vector3(0, 0, GuestZ) };
        var body = new Node3D { Position = new Vector3(0, 0, -1.43f) }; guest.AddChild(body);
        Color skin = new(0.91f, 0.70f, 0.56f), shade = new(0.78f, 0.54f, 0.40f);
        Color felt = new(0.92f, 0.91f, 0.83f), blue = new(0.025f, 0.18f, 0.49f);
        void Ball(Vector3 at, Vector3 scale, Color color)
        {
            var part = Props.Part(Props.Ball(1, 12), color, at); part.Scale = scale; body.AddChild(part);
        }
        void Box(Vector3 size, Vector3 at, Color color, Vector3 rotation = default) => body.AddChild(Props.Part(Props.Box(size.X, size.Y, size.Z), color, at, rotation));
        // The guest sits on the stern thwart, with bent legs inside the hull.
        Ball(new Vector3(0, 1.04f, 1.43f), new Vector3(0.46f, 0.58f, 0.36f), skin);
        Ball(new Vector3(0, 1.73f, 1.43f), new Vector3(0.28f, 0.32f, 0.27f), skin);
        Ball(new Vector3(0, 1.69f, 1.16f), new Vector3(0.08f, 0.085f, 0.11f), shade);
        foreach (float side in new[] { -1f, 1f })
        {
            Ball(new Vector3(side * 0.25f, 0.46f, 0.94f), new Vector3(0.16f, 0.17f, 0.40f), skin);
        }
        // White felt bell-shaped sauna hat, blue band, cross on its front and top loop.
        body.AddChild(Props.Part(Props.Cyl(0.39f, 0.50f, 12, 0.14f), felt, new Vector3(0, 2.08f, 1.43f)));
        body.AddChild(Props.Part(Props.Cyl(0.41f, 0.10f, 12), felt, new Vector3(0, 1.84f, 1.43f)));
        body.AddChild(Props.Part(Props.Cyl(0.325f, 0.09f, 12, 0.28f), blue, new Vector3(0, 2.01f, 1.43f)));
        Box(new Vector3(0.09f, 0.49f, 0.025f), new Vector3(0, 2.08f, 1.16f), blue, new Vector3(27, 0, 0));
        Box(new Vector3(0.09f, 0.49f, 0.025f), new Vector3(0, 2.08f, 1.70f), blue, new Vector3(-27, 0, 0));
        Box(new Vector3(0.08f, 0.16f, 0.05f), new Vector3(0, 2.38f, 1.43f), blue);
        DrunkTractor.BatchStatic(guest, null);
        BuildShotgun(guest, skin, shade);
        return guest;
    }

    public override void _Process(double delta)
    {
        if (_boat == null || delta == 0) return;
        _clock += (float)delta;
        _recoil = Math.Max(0, _recoil - (float)delta * 5f);
        Animate(_clock);
        if (_clock >= _nextShot)
        {
            // Never through the rower: with him in the way (or the gun not yet up) the guest waits.
            if (!_blocked && _ready > 0.9f) Fire();
            else _nextShot = _clock + 2.5f;
        }
        HuntEffects((float)delta);
    }
    void Animate(float time)
    {
        float dt = Math.Clamp(time - _animTime, 0f, 0.1f);
        _animTime = time;
        float boatTime = time + _phase;
        Vector2 p = PositionAt(boatTime);
        float stroke = time % 8f < 5.6f ? MathF.Sin(time * 2.2f) : 0;
        _boat.Position = new Vector3(p.X, _track.WaterLevel + 0.07f + MathF.Sin(time * 1.2f) * 0.035f, p.Y);
        _boat.Rotation = new Vector3(0, TrackData.YawFor(Heading(boatTime)), MathF.Sin(time * 1.2f) * 0.025f + _recoil * 0.035f);
        _body.Rotation = new Vector3(stroke * 0.07f, 0, 0);
        // The guest aims at the duck; just before the shot the drunken barrels wander off to where the
        // pellets land and stay there a moment after. With the rower in the way the gun goes up and waits.
        Vector2 duck = PartyPondDuck.PositionAt(time);
        float before = _nextShot - time, since = time - _lastShot;
        float drift = 1f - Mathf.SmoothStep(0f, 0.35f, before), after = 1f - Mathf.SmoothStep(0.35f, 0.7f, since);
        Vector2 target = after > 0 ? duck.Lerp(MissAt(time, _shots - 1), after) : duck.Lerp(MissAt(time, _shots), drift);
        _blocked = !ClearShot(boatTime, time, _shots);
        float raise = _blocked ? 0f : 1f - Mathf.SmoothStep(1f, 3.5f, before);
        _ready = Mathf.MoveToward(_ready, raise, dt * 1.5f);
        Vector3 aim = _boat.Transform.AffineInverse() * new Vector3(target.X, _track.WaterLevel + PartyPondDuck.Height * 0.5f, target.Y) - _passenger.Position;
        // Turned within the free side only (never across the rower's back), swaying less once aimed.
        float want = Mathf.PosMod(TrackData.YawFor(new Vector2(aim.X, aim.Z)) + MathF.Sin(time * 1.7f) * Mathf.Lerp(0.17f, 0.03f, _ready), MathF.Tau);
        want = Math.Clamp(want, RowerShadow, MathF.Tau - RowerShadow);
        _aimYaw = Mathf.Lerp(_aimYaw, want, Math.Min(1f, dt * 5f));
        _passenger.Rotation = new Vector3(MathF.Sin(time * 0.83f) * 0.09f * (1f - 0.6f * _ready) + _recoil * 0.22f, _aimYaw,
            MathF.Sin(time * 1.23f) * 0.12f * (1f - 0.6f * _ready) - _recoil * 0.14f);
        // Resting, the barrels point up and clear of the boat; aimed, down at the duck on the water.
        float pitch = MathF.Atan2(aim.Y - 1.23f, new Vector2(aim.X, aim.Z).Length());
        float reload = MathF.Sin(MathF.PI * Mathf.Clamp((time - _lastShot - 0.3f) / 1.6f, 0, 1));
        _gun.Position = new Vector3(0.12f, 1.23f, _recoil * 0.14f);
        _gun.Rotation = new Vector3(Mathf.Lerp(0.5f, pitch, _ready) + MathF.Sin(time * 2.1f) * 0.07f * (1f - 0.7f * _ready) + _recoil * 0.3f, 0, 0);
        _barrels.Rotation = new Vector3(-reload * 0.65f, 0, 0);
        _leftOar.Rotation = new Vector3(0, stroke * 0.40f, -0.19f - stroke * 0.07f);
        _rightOar.Rotation = new Vector3(0, -stroke * 0.40f, 0.19f + stroke * 0.07f);
        for (int i = 0; i < 6; i++)
        {
            float age = (time * 0.45f + (i / 2) / 3f) % 1f;
            Vector3 local = new(i % 2 == 0 ? -2.3f : 2.3f, -0.04f, 0);
            Vector3 world = _boat.Transform * local; world.Y = _track.WaterLevel + 0.02f;
            float size = (0.12f + age * 0.8f) * FigureScale;
            _ripples.Multimesh.SetInstanceTransform(i, new Transform3D(Basis.FromScale(new Vector3(size, 0.10f, size)), world));
        }
    }
}
