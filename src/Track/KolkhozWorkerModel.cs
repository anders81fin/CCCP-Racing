using System;
using Godot;

namespace CCCPRacing.Track;

/// <summary>A deliberately chunky, low-poly kolkhoz worker. Geometry is batched by moving part.</summary>
public partial class KolkhozWorkerModel : Node3D
{
    static readonly Color Skin = new(0.78f, 0.56f, 0.43f);
    static readonly Color SkinLight = new(0.91f, 0.69f, 0.54f);
    static readonly Color Trouser = new(0.25f, 0.29f, 0.24f);
    static readonly Color Boot = new(0.17f, 0.13f, 0.10f);
    static readonly Color Metal = new(0.58f, 0.58f, 0.49f);
    static readonly Color Wood = new(0.40f, 0.25f, 0.13f);
    static readonly Color Straw = new(0.67f, 0.52f, 0.22f);

    readonly Node3D _body = new() { Name = "Body" };
    readonly Node3D _leftArm = new() { Name = "LeftArm", Position = new Vector3(-0.27f, 1.31f, 0) };
    readonly Node3D _rightArm = new() { Name = "RightArm", Position = new Vector3(0.27f, 1.31f, 0) };
    readonly Node3D _work = new() { Name = "HeldWork" };
    Node3D _rod;
    Node3D _line;
    readonly Node3D _head = new() { Name = "Head", Position = Neck };
    readonly Node3D[] _hip = { new() { Name = "HipL" }, new() { Name = "HipR" } };
    readonly Node3D[] _knee = { new() { Name = "KneeL", Position = new Vector3(0, -0.37f, 0) }, new() { Name = "KneeR", Position = new Vector3(0, -0.37f, 0) } };
    readonly Node3D[] _foot = { new() { Name = "FootL", Position = new Vector3(0, -0.22f, 0) }, new() { Name = "FootR", Position = new Vector3(0, -0.22f, 0) } };
    readonly float[] _legA = new float[2], _legK = new float[2];
    static readonly Vector3 Neck = new(0, 1.40f, 0);
    Vector3 _fishingBobber;
    KolkhozJob _job;
    float _variantPhase;

    // Individual rhythm, set at build time from the worker's seed.
    int _seedI;
    float _seedF, _period, _workDur, _breakDur, _speedOut, _speedBack;
    // Animation state (the model keeps its own clock: Animate only gets the world time).
    float _last = float.NaN, _clock, _sched, _workT;
    float _b, _backW, _browW, _lookW, _carW, _carYaw, _headYaw, _headPitch;
    float _react;
    bool _armed = true;
    // Walkers (CarrySack, Load): a short path between a pile and the cart, along the model's own X axis.
    int _ws, _loop;
    float _wt, _walkX, _wHeading, _wSquat, _wBend, _wLoadY, _v, _gait, _gaitW, _carryW;
    bool _wShow = true, _wCarry, _wBreak;

    /// <summary>Where the walker is on his path (model space): the pivot for a knock-down.</summary>
    public Vector3 Offset => new(_walkX, 0, 0);

    public static KolkhozWorkerModel Build(KolkhozJob job, int variant, float fishingWaterHeight = 0, int seed = 0)
    {
        var worker = new KolkhozWorkerModel { _job = job, _variantPhase = (variant % 5) * 0.83f };
        worker.InitRhythm(seed * 7 + variant + (int)job * 13);
        worker.BuildFigure(job, variant);
        if (job == KolkhozJob.Fish) worker.BuildFishingGear(fishingWaterHeight);
        return worker;
    }

    void BuildFigure(KolkhozJob job, int variant)
    {
        Color coat = (variant % 4) switch
        {
            0 => new Color(0.39f, 0.31f, 0.21f),
            1 => new Color(0.25f, 0.34f, 0.29f),
            2 => new Color(0.39f, 0.25f, 0.20f),
            _ => new Color(0.31f, 0.34f, 0.36f),
        };
        Color coatLight = coat.Lightened(0.17f);
        Color cap = (variant % 3) switch
        {
            0 => new Color(0.27f, 0.30f, 0.22f),
            1 => new Color(0.48f, 0.26f, 0.16f),
            _ => new Color(0.28f, 0.30f, 0.34f),
        };
        var core = new Batch();
        var work = new Batch();

        // Broad shoulders, tucked waist and a square collar give a sturdy work-clothes silhouette.
        core.Box(new Vector3(0, 1.30f, 0), new Vector3(0.51f, 0.19f, 0.29f), coatLight);
        core.Box(new Vector3(0, 1.04f, 0), new Vector3(0.48f, 0.55f, 0.30f), coat);
        core.Box(new Vector3(0, 0.77f, 0), new Vector3(0.45f, 0.10f, 0.31f), new Color(0.23f, 0.19f, 0.13f)); // belt
        core.Box(new Vector3(0, 0.77f, -0.165f), new Vector3(0.10f, 0.10f, 0.035f), new Color(0.72f, 0.56f, 0.22f));
        core.Box(new Vector3(0, 1.27f, -0.055f), new Vector3(0.22f, 0.14f, 0.23f), coatLight); // collar
        core.Box(new Vector3(0, 1.03f, -0.16f), new Vector3(0.045f, 0.30f, 0.025f), new Color(0.68f, 0.59f, 0.41f)); // placket
        for (int i = 0; i < 3; i++)
            core.Box(new Vector3(0, 1.12f - i * 0.095f, -0.18f), new Vector3(0.025f, 0.025f, 0.018f), new Color(0.13f, 0.12f, 0.10f));
        var head = new Batch();
        // Head and face point towards -Z; nose, brows and eyes remain readable at distance.
        head.Ellipsoid(new Vector3(0, 1.55f, 0), new Vector3(0.205f, 0.235f, 0.19f), SkinLight);
        head.Box(new Vector3(0, 1.47f, -0.18f), new Vector3(0.075f, 0.075f, 0.09f), Skin);
        head.Box(new Vector3(-0.105f, 1.57f, -0.177f), new Vector3(0.052f, 0.033f, 0.026f), new Color(0.17f, 0.13f, 0.11f));
        head.Box(new Vector3(0.105f, 1.57f, -0.177f), new Vector3(0.052f, 0.033f, 0.026f), new Color(0.17f, 0.13f, 0.11f));
        head.Box(new Vector3(-0.105f, 1.615f, -0.178f), new Vector3(0.075f, 0.023f, 0.03f), new Color(0.32f, 0.22f, 0.16f));
        head.Box(new Vector3(0.105f, 1.615f, -0.178f), new Vector3(0.075f, 0.023f, 0.03f), new Color(0.32f, 0.22f, 0.16f));
        head.Box(new Vector3(0, 1.405f, -0.18f), new Vector3(0.09f, 0.025f, 0.035f), new Color(0.31f, 0.18f, 0.13f)); // mouth
        head.Box(new Vector3(0, 1.77f, 0.015f), new Vector3(0.40f, 0.095f, 0.36f), cap);
        head.Box(new Vector3(0, 1.73f, -0.18f), new Vector3(0.37f, 0.055f, 0.22f), cap);
        if (variant % 3 == 1)
        {
            // A tied kerchief with two small tails, in place of the flat cap.
            head.Box(new Vector3(0, 1.745f, 0.015f), new Vector3(0.41f, 0.09f, 0.35f), new Color(0.58f, 0.30f, 0.20f));
            head.Box(new Vector3(0.17f, 1.67f, 0.06f), new Vector3(0.11f, 0.16f, 0.09f), new Color(0.58f, 0.30f, 0.20f));
        }
        else
        {
            head.Box(new Vector3(0, 1.70f, -0.205f), new Vector3(0.16f, 0.085f, 0.028f),
                variant % 2 == 0 ? new Color(0.70f, 0.17f, 0.10f) : new Color(0.78f, 0.67f, 0.39f)); // cap badge
        }
        // The carrying worker supports a bulky grain sack against his chest.
        if (job == KolkhozJob.CarrySack)
        {
            work.Ellipsoid(new Vector3(0, 1.02f, -0.36f), new Vector3(0.36f, 0.33f, 0.22f),
                variant % 2 == 0 ? new Color(0.61f, 0.49f, 0.31f) : new Color(0.54f, 0.54f, 0.40f));
            work.Box(new Vector3(0, 1.35f, -0.35f), new Vector3(0.16f, 0.14f, 0.16f), new Color(0.43f, 0.35f, 0.23f));
            work.Box(new Vector3(0, 1.10f, -0.58f), new Vector3(0.28f, 0.06f, 0.035f), new Color(0.62f, 0.48f, 0.28f)); // tied seam
        }
        if (job == KolkhozJob.Load)
        {
            // Grain sheaf held at waist height, supported from below by both hands.
            work.Ellipsoid(new Vector3(0, 1.00f, -0.42f), new Vector3(0.43f, 0.22f, 0.20f), Straw);
            for (int i = -2; i <= 2; i++)
                work.Box(new Vector3(i * 0.13f, 1.01f + (i % 2) * 0.04f, -0.49f), new Vector3(0.025f, 0.23f, 0.035f),
                    new Color(0.83f, 0.68f, 0.33f), new Vector3(0, 0, i * 5));
        }
        if (job == KolkhozJob.Pitchfork)
        {
            // A single continuous shaft crosses both hands and ends just above the ground.
            work.Box(new Vector3(0, 0.78f, -0.34f), new Vector3(0.075f, 1.08f, 0.075f), Wood, new Vector3(0, 0, -5));
            work.Box(new Vector3(0.045f, 0.235f, -0.35f), new Vector3(0.37f, 0.07f, 0.075f), Metal);
            for (int i = -1; i <= 1; i++)
                work.Box(new Vector3(0.045f + i * 0.13f, 0.115f, -0.35f), new Vector3(0.055f, 0.23f, 0.055f), Metal,
                    new Vector3(0, 0, i * 3));
        }
        AddBatch(_body, core);
        _body.AddChild(_head);
        AddBatch(_head, head, -Neck);
        AddChild(_body);

        // Hands and tools share the torso's waist bend, but each has its own work motion.
        _body.AddChild(_work);
        AddBatch(_work, work);
        _body.AddChild(_leftArm);
        _body.AddChild(_rightArm);
        BuildArm(_leftArm, false, job, coat, coatLight);
        BuildArm(_rightArm, true, job, coat, coatLight);
        for (int i = 0; i < 2; i++)
        {
            float x = i == 0 ? -0.115f : 0.115f;
            AddChild(_hip[i]);
            _hip[i].AddChild(_knee[i]);
            _knee[i].AddChild(_foot[i]);
            var thigh = new Batch(); var shin = new Batch(); var foot = new Batch();
            thigh.Box(new Vector3(0, -0.18f, 0), new Vector3(0.21f, 0.39f, 0.25f), coat);
            shin.Box(new Vector3(0, -0.09f, -0.01f), new Vector3(0.19f, 0.25f, 0.22f), Trouser,
                new Vector3(0, 0, (variant % 2 == 0 ? 1 : -1) * 2));
            foot.Box(new Vector3(0, -0.07f, -0.055f), new Vector3(0.25f, 0.15f, 0.37f), Boot);
            foot.Box(new Vector3(0, -0.14f, -0.08f), new Vector3(0.26f, 0.035f, 0.40f), new Color(0.10f, 0.09f, 0.075f));
            AddBatch(_hip[i], thigh);
            AddBatch(_knee[i], shin);
            AddBatch(_foot[i], foot);
            _hip[i].Position = new Vector3(x, 0.77f, 0);
        }
        if (job is KolkhozJob.CarrySack or KolkhozJob.Load) BuildWalkProps(job, variant);
    }

    /// <summary>A pile where the walker picks up and a heap or cart where he puts the load down (both in front of him).</summary>
    void BuildWalkProps(KolkhozJob job, int variant)
    {
        float a = WalkHalf(job);
        var props = new Batch();
        if (job == KolkhozJob.CarrySack)
        {
            Color sack = variant % 2 == 0 ? new Color(0.61f, 0.49f, 0.31f) : new Color(0.54f, 0.54f, 0.40f);
            props.Ellipsoid(new Vector3(-a, 0.14f, -0.62f), new Vector3(0.30f, 0.14f, 0.20f), sack);
            props.Ellipsoid(new Vector3(-a + 0.12f, 0.30f, -0.60f), new Vector3(0.20f, 0.12f, 0.16f), sack.Darkened(0.1f));
            props.Ellipsoid(new Vector3(a, 0.14f, -0.62f), new Vector3(0.32f, 0.14f, 0.21f), sack.Lightened(0.08f));
            props.Ellipsoid(new Vector3(a - 0.1f, 0.29f, -0.60f), new Vector3(0.22f, 0.12f, 0.16f), sack);
        }
        else
        {
            props.Ellipsoid(new Vector3(-a, 0.20f, -0.62f), new Vector3(0.40f, 0.20f, 0.24f), Straw);
            props.Ellipsoid(new Vector3(-a + 0.1f, 0.38f, -0.62f), new Vector3(0.26f, 0.12f, 0.18f), Straw.Lightened(0.1f));
            // A little hand cart, its bed at loading height.
            props.Box(new Vector3(a, 0.93f, -0.78f), new Vector3(0.78f, 0.07f, 0.62f), Wood);
            props.Box(new Vector3(a, 1.00f, -1.07f), new Vector3(0.78f, 0.12f, 0.04f), Wood.Darkened(0.1f));
            props.Box(new Vector3(a, 0.45f, -0.78f), new Vector3(0.06f, 0.06f, 0.06f), Wood.Darkened(0.2f));
            props.Ellipsoid(new Vector3(a - 0.42f, 0.30f, -0.78f), new Vector3(0.04f, 0.30f, 0.30f), Wood.Darkened(0.25f));
            props.Ellipsoid(new Vector3(a + 0.42f, 0.30f, -0.78f), new Vector3(0.04f, 0.30f, 0.30f), Wood.Darkened(0.25f));
        }
        AddBatch(this, props);
    }

    static float WalkHalf(KolkhozJob job) => job == KolkhozJob.Load ? 0.55f : 0.6f;

    static float Hash(int seed, int k)
    {
        float x = MathF.Sin(seed * 12.9898f + k * 78.233f) * 43758.5453f;
        return x - MathF.Floor(x);
    }

    void InitRhythm(int seed)
    {
        _seedI = seed;
        _seedF = Hash(seed, 5) * MathF.Tau;
        _period = 2.7f + 1.2f * Hash(seed, 1);
        _workDur = 4f + 4f * Hash(seed, 2);
        _breakDur = 2.4f + 1.4f * Hash(seed, 3);
        _sched = Hash(seed, 4) * (_workDur + _breakDur);
        _speedOut = 0.72f + 0.2f * Hash(seed, 6);
        _speedBack = 0.9f + 0.2f * Hash(seed, 7);
        _workT = Hash(seed, 8) * _period;
        int st = (int)(Hash(seed, 9) * 3f);
        _ws = st == 0 ? 0 : st == 1 ? 1 : 4;
        _wt = Hash(seed, 10) * 0.8f;
        _loop = seed & 1;
    }

    void BuildFishingGear(float waterHeight)
    {
        _rod = new Node3D { Name = "FishingRod" };
        _line = new Node3D { Name = "FishingLine" };
        var gear = new Batch();
        // Long cane pole, fine hanging line and a red-and-cream float on the water.
        var rod = new Batch();
        rod.Segment(Vector3.Zero, Vector3.Up, 0.035f, Wood.Lightened(0.25f));
        AddBatch(_rod, rod);
        AddChild(_rod);
        Vector3 bobber = new(0.05f, waterHeight + 0.045f, -3.5f);
        _fishingBobber = bobber;
        var line = new Batch();
        line.Segment(Vector3.Zero, Vector3.Up, 0.009f, new Color(0.65f, 0.64f, 0.51f));
        AddBatch(_line, line);
        AddChild(_line);
        gear.Ellipsoid(bobber, new Vector3(0.055f, 0.085f, 0.055f), new Color(0.8f, 0.16f, 0.08f));
        gear.Box(bobber + Vector3.Up * 0.07f, new Vector3(0.045f, 0.10f, 0.045f), new Color(0.92f, 0.85f, 0.65f));
        // A battered metal bucket beside the boots, with an open dark rim and wire handle.
        gear.Ellipsoid(new Vector3(-0.6f, 0.20f, 0.06f), new Vector3(0.20f, 0.20f, 0.20f), Metal.Darkened(0.2f));
        gear.Ellipsoid(new Vector3(-0.6f, 0.39f, 0.06f), new Vector3(0.18f, 0.025f, 0.18f), new Color(0.14f, 0.17f, 0.15f));
        gear.Segment(new Vector3(-0.79f, 0.32f, 0.06f), new Vector3(-0.6f, 0.58f, 0.06f), 0.02f, Metal);
        gear.Segment(new Vector3(-0.6f, 0.58f, 0.06f), new Vector3(-0.41f, 0.32f, 0.06f), 0.02f, Metal);
        AddBatch(this, gear);
    }

    static void BuildArm(Node3D arm, bool right, KolkhozJob job, Color coat, Color light)
    {
        float side = right ? 1f : -1f;
        var b = new Batch();
        bool raised = job == KolkhozJob.Load;
        float elbowZ = raised || job == KolkhozJob.Fish ? -0.16f : -0.025f;
        // Shirt sleeve, rolled cuff, forearm and fist are one softly bent silhouette.
        b.Box(new Vector3(side * -0.015f, -0.16f, elbowZ * 0.35f), new Vector3(0.19f, 0.30f, 0.20f), coat,
            new Vector3(0, 0, side * 7));
        b.Box(new Vector3(side * -0.025f, -0.32f, elbowZ), new Vector3(0.15f, 0.065f, 0.17f), light);
        b.Box(new Vector3(side * -0.035f, -0.48f, elbowZ - 0.035f), new Vector3(0.14f, 0.27f, 0.15f), SkinLight,
            new Vector3(-8, 0, side * 3)); // rolled-up bare forearm
        b.Box(new Vector3(side * -0.035f, -0.63f, elbowZ - 0.07f), new Vector3(0.15f, 0.12f, 0.16f), Skin);

        if (job == KolkhozJob.Reap && right)
        {
            b.Box(new Vector3(0.00f, -0.85f, -0.10f), new Vector3(0.065f, 0.48f, 0.065f), Wood, new Vector3(0, 0, -8));
            // Short handle socket and an open, angular sickle blade, lifted clear of the body.
            b.Box(new Vector3(0.10f, -1.06f, -0.11f), new Vector3(0.27f, 0.075f, 0.06f), Metal, new Vector3(0, 0, -10));
            b.Box(new Vector3(0.22f, -1.22f, -0.11f), new Vector3(0.20f, 0.055f, 0.045f), Metal, new Vector3(0, 0, -25));
            b.Box(new Vector3(0.30f, -1.35f, -0.11f), new Vector3(0.13f, 0.045f, 0.04f), Metal, new Vector3(0, 0, -35));
        }
        else if (job == KolkhozJob.Pitchfork)
        {
            // The right hand carries the long fork; the left steadies it at chest height.
            // Both hands follow the independently animated shared shaft.
        }
        else if (job == KolkhozJob.Foreman && right)
        {
            b.Box(new Vector3(0, -0.78f, -0.09f), new Vector3(0.14f, 0.18f, 0.13f), SkinLight, new Vector3(0, 0, -10));
            b.Box(new Vector3(0, -0.88f, -0.14f), new Vector3(0.06f, 0.16f, 0.07f), SkinLight);
        }
        else if (job == KolkhozJob.Rest && !right)
        {
            b.Box(new Vector3(-0.02f, -0.61f, 0.08f), new Vector3(0.15f, 0.22f, 0.15f), SkinLight, new Vector3(12, 0, 12));
        }
        else if (job == KolkhozJob.CarrySack || job == KolkhozJob.Load)
        {
            // Both hands angle inward beneath the load, keeping a continuous contact silhouette.
            b.Box(new Vector3(-side * 0.13f, -0.60f, -0.25f), new Vector3(0.16f, 0.14f, 0.14f), SkinLight,
                new Vector3(0, 0, side * 20));
        }
        AddBatch(arm, b);
    }

    // ---- Animation ----------------------------------------------------------------------------

    struct ArmPose
    {
        public Quaternion Q;
        public float S;
        public ArmPose(Quaternion q, float s) { Q = q; S = s; }
        public static ArmPose Lerp(ArmPose a, ArmPose b, float t) =>
            t <= 0f ? a : t >= 1f ? b : new ArmPose(a.Q.Slerp(b.Q, t), Mathf.Lerp(a.S, b.S, t));
    }

    static ArmPose Eul(float x, float y, float z) => new(Quaternion.FromEuler(new Vector3(x, y, z)), 1f);

    static ArmPose AimOf(Vector3 shoulder, Vector3 hand)
    {
        Vector3 reach = hand - shoulder;
        return new ArmPose(new Quaternion(Vector3.Down, reach.Normalized()), Math.Clamp(reach.Length() / 0.65f, 0.7f, 1.4f));
    }

    static void Apply(Node3D arm, ArmPose p)
    {
        arm.Quaternion = p.Q;
        arm.Scale = new Vector3(1, p.S, 1);
    }

    static float Smooth(float x) { x = Math.Clamp(x, 0f, 1f); return x * x * (3f - 2f * x); }
    static float Toward(float cur, float target, float rate, float dt) => cur + (target - cur) * (1f - MathF.Exp(-rate * dt));
    /// <summary>Slow up, quick down: a heavy thing lifted and let go (1 at u = 0.7).</summary>
    static float Lift(float u) => u < 0.7f ? Smooth(u / 0.7f) : 1f - Smooth((u - 0.7f) / 0.3f);

    public void Animate(float time) => Animate(time, float.MaxValue, 0, 0);

    /// <summary>
    /// One animation step. carDist: metres to the player's car; carBearing: the angle to it from the
    /// model's forward (positive towards the left, -Z forward); carKmh: its speed.
    /// Only the model's children move (the owner keeps the root transform: the knock-down uses it).
    /// </summary>
    public void Animate(float time, float carDist, float carBearing, float carKmh)
    {
        float dt = float.IsNaN(_last) ? 0f : Math.Clamp(time - _last, 0f, 0.2f);
        _last = time;
        _clock += dt;
        bool walker = _job is KolkhozJob.CarrySack or KolkhozJob.Load;

        // Work and break schedule: each worker has his own rhythm and takes his own breaks.
        bool onBreak = false;
        float backT = 0, browT = 0, lookT = 0;
        if (_job == KolkhozJob.Rest)
        {
            _sched += dt;
            float c = _sched % 9f;
            onBreak = true;
            if (c < 3f) backT = 1f; else if (c < 6f) browT = 1f; else lookT = 1f;
        }
        else if (walker)
        {
            WalkerStep(dt);
            onBreak = _wBreak;
            if (onBreak)
            {
                if ((_loop & 1) == 0) backT = 1f;
                else { browT = _wt < _breakDur * 0.5f ? 1f : 0f; lookT = 1f - browT; }
            }
        }
        else
        {
            _sched += dt;
            float cyc = _workDur + _breakDur;
            float c = _sched % cyc;
            int idx = (int)(_sched / cyc);
            onBreak = c > _workDur;
            if (onBreak)
            {
                float f = (c - _workDur) / _breakDur;
                if (((idx + _seedI) & 1) == 0) { backT = 1f; lookT = f > 0.6f ? 0.6f : 0f; }
                else if (f < 0.5f) browT = 1f;
                else lookT = 1f;
            }
        }
        _b = Toward(_b, onBreak ? 1f : 0f, 3.5f, dt);
        _backW = Toward(_backW, backT, 4f, dt);
        _browW = Toward(_browW, browT, 4f, dt);
        _lookW = Toward(_lookW, lookT, 3f, dt);
        float b = _b;
        _workT += dt * (1f - b);
        float u = (_workT / _period) % 1f;
        float L = Lift(u);
        float sweep = MathF.Sin(u * MathF.Tau);

        // The car: look after it, and shake a fist if it tears past close by.
        bool near = carDist < 25f;
        if (carDist > 28f) _armed = true;
        if (_armed && carDist < 10f && carKmh > 50f)
        {
            _armed = false;
            if (_job != KolkhozJob.Fish) _react = 1.5f;
        }
        if (_react > 0f) _react -= dt;
        float r = _react > 0f ? Smooth((1.5f - _react) / 0.25f) * Smooth(_react / 0.3f) : 0f;
        float heading = walker ? _wHeading : 0f;
        if (near) _carYaw = Math.Clamp(Mathf.Wrap(carBearing - heading, -MathF.PI, MathF.PI), -1.25f, 1.25f);
        _carW = Toward(_carW, near ? 1f : 0f, 4f, dt);

        // Body pose per job.
        float bend = 0, twist = 0, squat = 0.04f, shift = 0;
        Vector3 wp = Vector3.Zero, wr = Vector3.Zero;
        float gaitW = 0;
        switch (_job)
        {
            case KolkhozJob.Reap:
                // Bend into the crop, straighten slowly with the gathered sheaf, sweep the sickle across.
                bend = -0.30f - 0.18f * L; twist = sweep * 0.20f; squat = 0.10f + 0.24f * L; shift = sweep;
                break;
            case KolkhozJob.Pitchfork:
                // Drive the fork down in a quick thrust, then straighten slowly and heave it up.
                bend = -0.48f * (1 - L); twist = 0.16f * sweep; squat = 0.06f + 0.30f * (1 - L); shift = sweep;
                wp = new Vector3(0, 0.65f * L, -0.15f * (1 - L));
                wr = new Vector3(-0.40f * L, 0, 0);
                wp = wp.Lerp(new Vector3(0, 0.12f, -0.05f), b);
                wr = wr.Lerp(new Vector3(-0.05f, 0, 0), b);
                break;
            case KolkhozJob.CarrySack:
            case KolkhozJob.Load:
                gaitW = _gaitW;
                bend = _wBend + (-0.03f + 0.13f * _carryW);
                squat = _wSquat;
                twist = -0.14f * MathF.Sin(_gait) * gaitW;
                wp = new Vector3(0, _wLoadY + 0.025f * MathF.Sin(_gait * 2f) * gaitW, 0);
                wr = new Vector3(0, 0, 0.05f * MathF.Sin(_gait) * gaitW);
                break;
            case KolkhozJob.Foreman:
                bend = -0.02f; twist = sweep * 0.28f; shift = 0.4f * sweep;
                break;
            case KolkhozJob.Fish:
                bend = 0.03f * L; squat = 0.05f;
                break;
        }
        // The break pose: straighten up and stretch, or just stand easy and glance about.
        float bendB = 0.14f * _backW - 0.02f;
        float twistB = 0.22f * _lookW * MathF.Sin(_clock * 0.9f + _seedF);
        float shiftB = MathF.Sin(_clock * 0.5f + _seedF);
        if (!walker || onBreak)
        {
            bend = Mathf.Lerp(bend, bendB, b);
            twist = Mathf.Lerp(twist, twistB, b);
            squat = Mathf.Lerp(squat, 0.04f + 0.02f * (1f - _backW), b);
            shift = Mathf.Lerp(shift, shiftB * 0.6f, b);
        }
        if (_job == KolkhozJob.Fish) shift = 0f;      // the grip and the rod are fixed in the model's frame
        twist += 0.4f * _carYaw * r;

        // Legs: thigh and shin pivots; squat bends hips and knees together, the gait swings them.
        const float GaitAmp = 0.42f;
        float pelvisY = 0f;
        for (int i = 0; i < 2; i++)
        {
            float sgn = i == 0 ? 1f : -1f;
            float si = Math.Clamp(squat * (1f + 0.35f * shift * sgn), 0f, 1.2f);
            float a = 0.7f * si, k = -1.4f * si;
            if (gaitW > 0f)
            {
                float ph = _gait + i * MathF.PI;
                a += GaitAmp * gaitW * MathF.Sin(ph);
                k -= 1.0f * gaitW * MathF.Max(0f, MathF.Cos(ph));
            }
            _legA[i] = a; _legK[i] = k;
            pelvisY = MathF.Max(pelvisY, 0.37f * MathF.Cos(a) + 0.22f * MathF.Cos(a + k));
        }
        pelvisY += 0.18f;
        Vector3 pelvis = new(_walkX + 0.03f * shift, pelvisY, 0);
        float ch = MathF.Cos(heading), sh = MathF.Sin(heading);
        for (int i = 0; i < 2; i++)
        {
            float x = i == 0 ? -0.115f : 0.115f;
            _hip[i].Position = pelvis + new Vector3(x * ch, 0, -x * sh);
            _hip[i].Rotation = new Vector3(_legA[i], heading, 0);
            _knee[i].Rotation = new Vector3(_legK[i], 0, 0);
            _foot[i].Rotation = new Vector3(Math.Clamp(-(_legA[i] + _legK[i]), -0.8f, 0.8f), 0, 0);
        }

        // Torso: rotate about the (lowered) belt.
        _body.Rotation = new Vector3(bend, heading + twist, 0.05f * shift);
        _body.Position = pelvis - _body.Basis * new Vector3(0, 0.77f, 0);
        _work.Position = wp;
        _work.Rotation = wr;
        _work.Visible = !walker || _wShow;
        var wt = new Transform3D(Basis.FromEuler(wr), wp);

        // Head: nods with the work, follows the car, looks around during a break.
        float idleYaw = 0.10f * MathF.Sin(_clock * 0.7f + _seedF) + 0.8f * _lookW * MathF.Sin(_clock * 1.2f + _seedF);
        float yawT = Mathf.Lerp(idleYaw, _carYaw, _carW);
        float pitchT = -0.3f * bend + 0.07f * MathF.Sin(u * MathF.Tau * 2f) * (1f - b) + 0.3f * _backW - 0.1f * _carW * r;
        if (_job == KolkhozJob.Fish) pitchT = -0.12f;
        _headYaw = Toward(_headYaw, yawT, 9f, dt);
        _headPitch = Toward(_headPitch, pitchT, 9f, dt);
        _head.Rotation = new Vector3(_headPitch, _headYaw, 0);

        // Arms.
        Vector3 lsh = _leftArm.Position, rsh = _rightArm.Position;
        ArmPose lw = Eul(0.05f, 0, -0.10f), rw = Eul(0.05f, 0, 0.10f);
        bool rFree = true;
        switch (_job)
        {
            case KolkhozJob.Reap:
                rw = ArmPose.Lerp(Eul(0.55f + L * 0.45f, sweep * 0.65f, 0.35f + sweep * 0.55f), Eul(0.25f, 0, 0.30f), b);
                lw = Eul(0.85f + L * 0.25f, 0, -0.28f);
                rFree = false;
                break;
            case KolkhozJob.Pitchfork:
                lw = AimOf(lsh, wt * new Vector3(0.03f, 1.02f, -0.34f));
                rw = AimOf(rsh, wt * new Vector3(0.03f, 0.69f, -0.34f));
                rFree = false;
                break;
            case KolkhozJob.CarrySack:
            case KolkhozJob.Load:
            {
                float sw = 0.9f * GaitAmp * gaitW * MathF.Sin(_gait);
                bool sack = _job == KolkhozJob.CarrySack;
                Vector3 lh = sack ? new Vector3(-0.24f, 0.82f, -0.40f) : new Vector3(-0.28f, 0.88f, -0.42f);
                Vector3 rh = new(-lh.X, lh.Y, lh.Z);
                lw = ArmPose.Lerp(Eul(-sw, 0, -0.08f), AimOf(lsh, wt * lh), _carryW);
                rw = ArmPose.Lerp(Eul(sw, 0, 0.08f), AimOf(rsh, wt * rh), _carryW);
                break;
            }
            case KolkhozJob.Foreman:
                // Point across the field, lower the arm and give another broad instruction.
                rw = Eul(0.35f + L * 1.35f, sweep * 0.4f, -0.15f - L * 0.45f);
                lw = Eul(0.15f, 0, -0.25f);
                break;
            case KolkhozJob.Fish:
            {
                // Test the line with a lift of the rod; the line remains attached to the float.
                float le = L * (1f - b);
                Vector3 grip = new(0.14f, 0.96f + 0.38f * le, -0.38f);
                Vector3 tip = new(0.05f + sweep * 0.14f * (1f - b), 2.30f + 0.65f * le, -3.0f + le * 0.25f);
                SetSegment(_rod, grip, tip);
                SetSegment(_line, tip, _fishingBobber);
                rw = AimOf(rsh, grip);
                lw = AimOf(lsh, grip + new Vector3(-0.10f, le * 0.18f, 0.13f));
                rFree = false;
                break;
            }
        }
        // Free hands (during a break): on the lower back, or wiping the brow.
        float sway = 0.04f * MathF.Sin(_clock * 1.3f + _seedF);
        ArmPose freeL = ArmPose.Lerp(Eul(0.05f + sway, 0, -0.10f), AimOf(lsh, new Vector3(-0.17f, 0.86f, 0.19f)), _backW);
        freeL = ArmPose.Lerp(freeL, AimOf(lsh, new Vector3(-0.07f + 0.09f * MathF.Sin(_clock * 6f), 1.64f, -0.23f)), _browW);
        ArmPose freeR = ArmPose.Lerp(Eul(0.05f - sway, 0, 0.10f), AimOf(rsh, new Vector3(0.17f, 0.86f, 0.19f)), _backW);
        if (_job != KolkhozJob.Fish || b > 0f)
        {
            lw = ArmPose.Lerp(lw, freeL, b);
            if (rFree) rw = ArmPose.Lerp(rw, freeR, b);
        }
        if (r > 0f)
            rw = ArmPose.Lerp(rw, Eul(2.2f + 0.35f * MathF.Sin(_clock * 15f), 0, 0.18f), r);
        Apply(_leftArm, lw);
        Apply(_rightArm, rw);
    }

    /// <summary>The walkers' loop: pick up at the pile, carry to the cart, put down, (rest), walk back empty-handed.</summary>
    void WalkerStep(float dt)
    {
        float a = WalkHalf(_job);
        bool sack = _job == KolkhozJob.CarrySack;
        float prevX = _walkX, prevH = _wHeading;
        _wt += dt;
        _wBreak = false;
        _wBend = 0; _wSquat = 0.04f; _wLoadY = 0;
        const float Turn = 0.5f;
        switch (_ws)
        {
            case 0:     // pick the load up from the pile: quick down, slow heave up
            {
                const float D = 1.5f;
                float f = Math.Min(_wt / D, 1f);
                float down = f < 0.3f ? Smooth(f / 0.3f) : 1f - Smooth((f - 0.3f) / 0.7f);
                _walkX = -a; _wHeading = 0;
                _wSquat = 0.04f + 0.75f * down; _wBend = -0.5f * down; _wLoadY = -0.55f * down;
                _wShow = f >= 0.28f; _wCarry = f >= 0.22f;
                if (_wt >= D) Next(1);
                break;
            }
            case 1:     // carry it to the cart
            {
                float d = 2f * a / _speedOut + 1f;
                float mv = Smooth((_wt - 0.4f) / (d - 0.8f));
                _walkX = -a + 2f * a * mv;
                _wHeading = -MathF.PI / 2f * Smooth(_wt / Turn) * (1f - Smooth((_wt - (d - Turn)) / Turn));
                _wShow = _wCarry = true;
                if (_wt >= d) Next(2);
                break;
            }
            case 2:     // put it down (a sack to the ground, a sheaf up on the cart)
            {
                const float D = 1.5f;
                float f = Math.Min(_wt / D, 1f);
                float down = f < 0.3f ? Smooth(f / 0.3f) : 1f - Smooth((f - 0.3f) / 0.7f);
                _walkX = a; _wHeading = 0;
                bool holding = f < 0.3f;
                if (sack) { _wSquat = 0.04f + 0.7f * down; _wBend = -0.45f * down; _wLoadY = -0.55f * Smooth(f / 0.3f); }
                else { _wSquat = 0.04f + 0.12f * down; _wBend = -0.15f * down; _wLoadY = 0.18f * Smooth(f / 0.3f); }
                _wShow = _wCarry = holding;
                if (_wt >= D) { _loop++; Next((((_loop + _seedI) & 1) == 0) ? 3 : 4); }
                break;
            }
            case 3:     // a breather beside the cart
                _walkX = a; _wHeading = 0; _wShow = _wCarry = false; _wBreak = true;
                if (_wt >= _breakDur) Next(4);
                break;
            default:    // back to the pile, empty-handed
            {
                float d = 2f * a / _speedBack + 1f;
                float mv = Smooth((_wt - 0.4f) / (d - 0.8f));
                _walkX = a - 2f * a * mv;
                _wHeading = MathF.PI / 2f * Smooth(_wt / Turn) * (1f - Smooth((_wt - (d - Turn)) / Turn));
                _wShow = _wCarry = false;
                if (_wt >= d) Next(0);
                break;
            }
        }
        // Gait speed follows the actual movement (walking and turning on the spot).
        float vx = dt > 0f ? MathF.Abs(_walkX - prevX) / dt : 0f;
        float vt = dt > 0f ? MathF.Abs(Mathf.Wrap(_wHeading - prevH, -MathF.PI, MathF.PI)) / dt * 0.16f : 0f;
        _v = Toward(_v, MathF.Max(vx, vt), 14f, dt);
        _gaitW = Math.Clamp(_v / 0.25f, 0f, 1f);
        _gait += 5.0f * _v * dt;
        _carryW = Toward(_carryW, _wCarry ? 1f : 0f, 8f, dt);
    }

    void Next(int state) { _ws = state; _wt = 0; }

    static void SetSegment(Node3D segment, Vector3 from, Vector3 to)
    {
        Vector3 length = to - from;
        segment.Position = from;
        segment.Quaternion = new Quaternion(Vector3.Up, length.Normalized());
        segment.Scale = new(1, length.Length(), 1);
    }

    static readonly StandardMaterial3D WorkerMaterial = new()
    {
        VertexColorUseAsAlbedo = true,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
        Roughness = 1f,
        SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    static void AddBatch(Node3D parent, Batch batch, Vector3 offset = default)
    {
        var mesh = batch.Finish();
        if (mesh == null) return;
        parent.AddChild(new MeshInstance3D
        {
            Mesh = mesh,
            Position = offset,
            MaterialOverride = WorkerMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    /// <summary>Collects colored primitives into one triangle mesh, preserving the independently moving upper body and arms.</summary>
    sealed class Batch
    {
        readonly SurfaceTool _surface = new();
        bool _started;

        public void Segment(Vector3 from, Vector3 to, float width, Color color)
        {
            Vector3 direction = to - from;
            var rotation = new Quaternion(Vector3.Up, direction.Normalized()).GetEuler();
            Box((from + to) * 0.5f, new Vector3(width, direction.Length(), width), color,
                rotation * (180f / MathF.PI));
        }

        public void Box(Vector3 center, Vector3 size, Color color, Vector3? rotation = null)
        {
            Vector3 degrees = rotation ?? Vector3.Zero;
            var basis = Basis.FromEuler(new Vector3(Mathf.DegToRad(degrees.X), Mathf.DegToRad(degrees.Y), Mathf.DegToRad(degrees.Z)));
            Vector3 h = size * 0.5f;
            Vector3[] p =
            {
                new(-h.X,-h.Y,-h.Z), new(h.X,-h.Y,-h.Z), new(h.X,h.Y,-h.Z), new(-h.X,h.Y,-h.Z),
                new(-h.X,-h.Y,h.Z), new(h.X,-h.Y,h.Z), new(h.X,h.Y,h.Z), new(-h.X,h.Y,h.Z),
            };
            int[] idx = { 0,2,1, 0,3,2, 4,5,6, 4,6,7, 0,4,7, 0,7,3, 1,2,6, 1,6,5, 0,1,5, 0,5,4, 3,7,6, 3,6,2 };
            for (int i = 0; i < idx.Length; i += 3)
            {
                Vector3 a = center + basis * p[idx[i]], b = center + basis * p[idx[i + 1]], c = center + basis * p[idx[i + 2]];
                Triangle(a, b, c, color);
            }
        }

        public void Ellipsoid(Vector3 center, Vector3 scale, Color color)
        {
            const int rings = 5, segments = 8;
            Vector3[,] pts = new Vector3[rings + 1, segments];
            for (int y = 0; y <= rings; y++)
            {
                float lat = -MathF.PI / 2f + MathF.PI * y / rings;
                for (int x = 0; x < segments; x++)
                {
                    float lon = 2f * MathF.PI * x / segments;
                    pts[y, x] = center + new Vector3(MathF.Cos(lat) * MathF.Cos(lon) * scale.X,
                        MathF.Sin(lat) * scale.Y, MathF.Cos(lat) * MathF.Sin(lon) * scale.Z);
                }
            }
            for (int y = 0; y < rings; y++)
            for (int x = 0; x < segments; x++)
            {
                int next = (x + 1) % segments;
                Triangle(pts[y, x], pts[y + 1, x], pts[y + 1, next], color);
                Triangle(pts[y, x], pts[y + 1, next], pts[y, next], color);
            }
        }

        void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color)
        {
            if (!_started) { _surface.Begin(Mesh.PrimitiveType.Triangles); _started = true; }
            Vector3 normal = (b - a).Cross(c - a).Normalized();
            Color linearColor = color.SrgbToLinear();
            _surface.SetColor(linearColor); _surface.SetNormal(normal); _surface.AddVertex(a);
            _surface.SetColor(linearColor); _surface.SetNormal(normal); _surface.AddVertex(b);
            _surface.SetColor(linearColor); _surface.SetNormal(normal); _surface.AddVertex(c);
        }

        public ArrayMesh Finish() => _started ? _surface.Commit() as ArrayMesh : null;
    }
}
