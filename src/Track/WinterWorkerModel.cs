using System;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

public enum WinterJob { Saw, CarryWood, Chop, Shovel, Inspect, Mechanic, CarryBox, Signal }

/// <summary>Winter clothing and work gestures, batched by moving part; carriers add one removable load mesh.</summary>
public partial class WinterWorkerModel : Node3D
{
    static readonly Color Mitt = new(0.23f, 0.20f, 0.17f);
    static readonly Color Wood = new(0.46f, 0.29f, 0.15f);
    static readonly Color Steel = new(0.48f, 0.53f, 0.55f);
    readonly Node3D _body = new() { Name = "Body" };
    readonly Node3D _load = new() { Name = "Load" };
    readonly Node3D _leftArm = new() { Name = "LeftArm", Position = new(-0.29f, 1.32f, 0) };
    readonly Node3D _rightArm = new() { Name = "RightArm", Position = new(0.29f, 1.32f, 0) };
    readonly Node3D _leftLeg = new() { Name = "LeftLeg", Position = new(-0.13f, 0.76f, 0) };
    readonly Node3D _rightLeg = new() { Name = "RightLeg", Position = new(0.13f, 0.76f, 0) };
    WinterJob _job;
    float _phase;

    /// <summary>Set false during pauses along carrying routes to stop the walking gait.</summary>
    public bool Walking { get; set; }

    /// <summary>The route controller drops the load at its destination and returns empty-handed.</summary>
    public bool Carrying { get; set; } = true;

    /// <summary>Opposed sawyers use opposite signs to follow the single shared saw.</summary>
    public float SawStrokeDirection { get; set; } = 1f;

    public static WinterWorkerModel Build(WinterJob job, int variant = 0)
    {
        var worker = new WinterWorkerModel
        {
            _job = job, _phase = Math.Abs(variant % 7) * 0.71f,
            Walking = job == WinterJob.CarryWood || job == WinterJob.CarryBox,
        };
        worker.BuildFigure(Math.Abs(variant % 4));
        worker.Animate(0);
        return worker;
    }

    void BuildFigure(int variant)
    {
        Color coat = variant switch
        {
            0 => new(0.32f, 0.36f, 0.25f), // quilted forestry jacket
            1 => new(0.40f, 0.34f, 0.28f), // camp work clothes
            2 => new(0.23f, 0.29f, 0.35f), // aircrew blue-grey
            _ => new(0.32f, 0.34f, 0.37f),
        };
        Color fur = new(0.32f, 0.28f, 0.23f);
        var b = new Batch();
        b.Box(new(0, 1.08f, 0), new(0.56f, 0.65f, 0.37f), coat);
        b.Box(new(0, 1.35f, 0), new(0.59f, 0.16f, 0.38f), coat.Lightened(0.09f));
        b.Box(new(0, 0.79f, 0), new(0.56f, 0.10f, 0.38f), coat.Darkened(0.15f));
        b.Box(new(0, 1.06f, -0.192f), new(0.035f, 0.48f, 0.022f), coat.Lightened(0.21f));
        for (int i = 0; i < 4; i++)
        {
            float y = 0.88f + i * 0.12f;
            b.Box(new(0, y, -0.21f), new(0.035f, 0.035f, 0.02f), Mitt);
            b.Box(new(0, y, 0.193f), new(0.49f, 0.012f, 0.012f), coat.Darkened(0.09f));
        }
        for (int side = -1; side <= 1; side += 2)
        {
            b.Box(new(side * 0.18f, 0.95f, -0.20f), new(0.16f, 0.17f, 0.045f), coat.Darkened(0.1f));
            b.Box(new(side * 0.18f, 1.025f, -0.23f), new(0.17f, 0.035f, 0.025f), coat.Lightened(0.12f));
        }
        b.Box(new(0, 1.40f, -0.045f), new(0.38f, 0.16f, 0.33f), fur);
        b.Box(new(0, 1.58f, -0.005f), new(0.33f, 0.32f, 0.29f), new(0.79f, 0.58f, 0.46f));
        b.Box(new(0, 1.55f, -0.18f), new(0.075f, 0.075f, 0.085f), new(0.69f, 0.42f, 0.34f));
        for (int side = -1; side <= 1; side += 2)
            b.Box(new(side * 0.085f, 1.625f, -0.157f), new(0.055f, 0.028f, 0.022f), Mitt);
        // The broad crown, forehead fur and lowered ear flaps read as an ushanka from the race camera.
        b.Box(new(0, 1.77f, 0.02f), new(0.43f, 0.15f, 0.37f), coat.Darkened(0.12f));
        b.Box(new(0, 1.715f, -0.17f), new(0.43f, 0.10f, 0.09f), fur.Lightened(0.15f));
        b.Box(new(-0.19f, 1.61f, 0.015f), new(0.09f, 0.23f, 0.29f), fur);
        b.Box(new(0.19f, 1.61f, 0.015f), new(0.09f, 0.23f, 0.29f), fur);
        b.Box(new(0, 1.43f, -0.17f), new(0.29f, 0.08f, 0.065f), new(0.51f, 0.46f, 0.36f));
        var load = new Batch();
        if (_job == WinterJob.CarryWood)
        {
            for (int i = 0; i < 3; i++)
            {
                Vector3 p = new(0, 1.02f + i * 0.095f, -0.42f + (i % 2) * 0.09f);
                load.Box(p, new(0.75f, 0.13f, 0.13f), Wood, new(0, i * 7 - 7, 0));
                load.Box(p + new Vector3(-0.38f, 0, 0), new(0.018f, 0.12f, 0.12f), Wood.Lightened(0.3f));
            }
        }
        if (_job == WinterJob.CarryBox)
        {
            load.Box(new(0, 1.02f, -0.40f), new(0.56f, 0.34f, 0.33f), new(0.38f, 0.41f, 0.31f));
            load.Box(new(0, 1.19f, -0.40f), new(0.59f, 0.035f, 0.35f), new(0.27f, 0.30f, 0.23f));
            for (int side = -1; side <= 1; side += 2)
                load.Box(new(side * 0.19f, 1.02f, -0.57f), new(0.04f, 0.33f, 0.018f), Steel.Darkened(0.3f));
        }
        AddBatch(_body, b);
        AddChild(_body);
        _body.AddChild(_load);
        if (_job is WinterJob.CarryWood or WinterJob.CarryBox) AddBatch(_load, load);
        // Arms follow the torso so carried loads and hands stay together while the worker breathes.
        _body.AddChild(_leftArm); _body.AddChild(_rightArm);
        BuildArm(_leftArm, -1, coat); BuildArm(_rightArm, 1, coat);
        AddChild(_leftLeg); AddChild(_rightLeg);
        BuildLeg(_leftLeg, coat); BuildLeg(_rightLeg, coat);
    }

    void BuildArm(Node3D arm, int side, Color coat)
    {
        var b = new Batch();
        Vector3 elbow = new(-side * 0.015f, -0.28f, -0.08f);
        Vector3 hand = new(_job is WinterJob.Chop or WinterJob.Saw ? -side * 0.29f : -side * 0.055f,
            -0.35f + (_job == WinterJob.Saw ? side * 0.045f : 0), _job == WinterJob.Saw ? -0.30f : -0.38f);
        b.Segment(Vector3.Zero, elbow, 0.21f, coat);
        b.Segment(elbow, hand, 0.18f, coat.Lightened(0.07f));
        b.Box(hand + new Vector3(0, 0, 0.055f), new(0.19f, 0.17f, 0.08f), coat.Darkened(0.2f));
        b.Box(hand, new(0.17f, 0.15f, 0.17f), Mitt);
        if (side == 1 && _job == WinterJob.Chop)
        {
            // At the bottom of the swing the head reaches the block 1.25m ahead, at about 1.1m high.
            b.Segment(hand + new Vector3(0, 0.20f, 0.32f), hand + new Vector3(0, -0.25f, -0.40f), 0.055f, Wood);
            b.Box(hand + new Vector3(0, -0.25f, -0.40f), new(0.32f, 0.16f, 0.08f), Steel);
            b.Box(hand + new Vector3(-0.15f, -0.26f, -0.40f), new(0.035f, 0.20f, 0.095f), Steel.Lightened(0.2f));
        }
        if (side == 1 && _job == WinterJob.Shovel)
        {
            b.Segment(hand + new Vector3(0, 0.20f, 0), hand + new Vector3(0, -0.67f, -0.24f), 0.055f, Wood);
            b.Box(hand + new Vector3(0, -0.74f, -0.27f), new(0.43f, 0.24f, 0.07f), Steel);
            b.Box(hand + new Vector3(0, -0.83f, -0.29f), new(0.45f, 0.035f, 0.09f), Steel.Lightened(0.2f));
        }
        if (side == -1 && _job == WinterJob.Inspect)
        {
            b.Box(hand + new Vector3(0, 0.08f, -0.07f), new(0.19f, 0.23f, 0.09f), new(0.26f, 0.32f, 0.28f));
            b.Box(hand + new Vector3(0, 0.12f, -0.12f), new(0.14f, 0.10f, 0.018f), new(0.76f, 0.78f, 0.63f));
            b.Box(hand + new Vector3(0.025f, 0.12f, -0.135f), new(0.012f, 0.07f, 0.012f), Mitt, new(0, 0, -20));
        }
        if (side == 1 && _job == WinterJob.Mechanic)
        {
            b.Box(hand + new Vector3(0, 0.08f, -0.07f), new(0.035f, 0.25f, 0.035f), Steel);
            b.Box(hand + new Vector3(0, 0.20f, -0.07f), new(0.10f, 0.05f, 0.035f), Steel);
        }
        if (_job == WinterJob.Signal)
        {
            b.Segment(hand, hand + new Vector3(0, 0.34f, 0), 0.035f, Wood);
            b.Box(hand + new Vector3(side * 0.13f, 0.32f, 0), new(0.26f, 0.22f, 0.018f),
                side == -1 ? new Color(0.83f, 0.23f, 0.12f) : new Color(0.90f, 0.77f, 0.25f));
        }
        AddBatch(arm, b);
    }

    static void BuildLeg(Node3D leg, Color coat)
    {
        var b = new Batch();
        b.Box(new(0, -0.20f, 0), new(0.23f, 0.42f, 0.27f), coat.Darkened(0.15f));
        b.Box(new(0, -0.51f, 0), new(0.25f, 0.31f, 0.28f), new(0.31f, 0.29f, 0.25f));
        b.Box(new(0, -0.65f, -0.065f), new(0.27f, 0.17f, 0.41f), new(0.25f, 0.23f, 0.20f));
        b.Box(new(0, -0.72f, -0.075f), new(0.28f, 0.035f, 0.43f), new(0.16f, 0.16f, 0.15f));
        AddBatch(leg, b);
    }

    /// <summary>The world tape parts of this worker (names carry <paramref name="prefix"/>, e.g. "saw1/"): the figure, body, load (it shows and hides), arms and legs.</summary>
    public void MarkTape(string prefix)
    {
        Tape.Part(this, prefix + "figure"); Tape.Part(_body, prefix + "body"); Tape.Part(_load, prefix + "load");
        Tape.Part(_leftArm, prefix + "armL"); Tape.Part(_rightArm, prefix + "armR"); Tape.Part(_leftLeg, prefix + "legL"); Tape.Part(_rightLeg, prefix + "legR");
    }

    public void Animate(float time, bool signalMoving = false, bool signalWaiting = false)
    {
        float t = time + _phase;
        float wave = MathF.Sin(t * 2.5f);
        _body.Position = new(0, Walking ? MathF.Abs(MathF.Sin(t * 4f)) * 0.025f : MathF.Sin(t * 1.1f) * 0.006f, 0);
        _body.Rotation = new(0, 0, Walking ? MathF.Sin(t * 4f) * 0.025f : 0);
        _leftLeg.Rotation = new(Walking ? MathF.Sin(t * 4f) * 0.30f : 0, 0, 0);
        _rightLeg.Rotation = -_leftLeg.Rotation;
        _leftArm.Rotation = _rightArm.Rotation = Vector3.Zero;
        _load.Visible = Carrying;
        if ((_job is WinterJob.CarryWood or WinterJob.CarryBox) && !Carrying)
        {
            // Lower the bent arms when empty, then swing them opposite the walking legs.
            float swing = Walking ? MathF.Sin(t * 4f) * 0.28f : 0;
            _leftArm.Rotation = new(-0.65f - swing, 0, 0);
            _rightArm.Rotation = new(-0.65f + swing, 0, 0);
        }
        switch (_job)
        {
            case WinterJob.Saw:
                wave = SiberiaDetails.SawStroke(time) * SawStrokeDirection;
                _leftArm.Position = new(-0.29f, 1.32f, wave * 0.125f);
                _rightArm.Position = new(0.29f, 1.32f, wave * 0.125f);
                _body.Rotation = Vector3.Zero;
                break;
            case WinterJob.Chop:
                // A measured lift, short downward stroke and pause with the head near the block.
                float stroke = (t % 6.2f) / 6.2f;
                float lift = stroke < 0.48f ? stroke / 0.48f : stroke < 0.62f ? 1f - (stroke - 0.48f) / 0.14f : 0;
                _leftArm.Rotation = _rightArm.Rotation = new(lift * 2.0f, 0, 0);
                _body.Rotation = new(-0.09f * (1f - lift), 0, 0);
                break;
            case WinterJob.Shovel:
                _rightArm.Rotation = new(0.10f + wave * 0.28f, 0, 0);
                _leftArm.Rotation = new(0.10f + wave * 0.20f, 0, 0.15f);
                _body.Rotation = new(-0.05f - wave * 0.06f, 0, 0);
                break;
            case WinterJob.Inspect:
                float cycle = t % 17f;
                _leftArm.Rotation = new(cycle < 10 ? 0.18f : 0.40f, 0, 0);
                _rightArm.Rotation = cycle < 5 ? new(0, 0, -0.08f)
                    : cycle < 10 ? new(0.18f + MathF.Sin(t * 7f) * 0.045f, 0.35f, -0.18f)
                    : cycle < 14 ? new(0.40f, 0.35f, -0.18f + MathF.Sin(t * 5f) * 0.035f)
                    : new(0, 0, -0.08f);
                break;
            case WinterJob.Mechanic:
                _body.Rotation = new(-0.10f, 0, 0);
                _rightArm.Rotation = new(0.15f, MathF.Sin(t * 2f) * 0.12f, 0);
                _leftArm.Rotation = new(0.10f, 0, 0);
                break;
            case WinterJob.Signal:
                if (signalWaiting)
                {
                    _leftArm.Rotation = new(1.10f, 0, -0.30f);
                    _rightArm.Rotation = new(1.10f, 0, 0.30f);
                }
                else if (signalMoving)
                {
                    _leftArm.Rotation = new(0.55f + wave * 0.30f, 0, -0.35f);
                    _rightArm.Rotation = new(0.55f + wave * 0.30f, 0, 0.35f);
                }
                else
                {
                    _leftArm.Rotation = new(-0.45f, 0, 0);
                    _rightArm.Rotation = new(-0.45f, 0, 0);
                }
                break;
        }
    }

    static readonly StandardMaterial3D Material = new()
    {
        VertexColorUseAsAlbedo = true, ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
        Roughness = 1f, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    static void AddBatch(Node3D parent, Batch batch) => parent.AddChild(new MeshInstance3D
    {
        Mesh = batch.Finish(), MaterialOverride = Material,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    });

    sealed class Batch
    {
        readonly SurfaceTool _surface = new();
        bool _started;
        public void Segment(Vector3 from, Vector3 to, float width, Color color)
        {
            Vector3 d = to - from;
            var rotation = new Quaternion(Vector3.Up, d.Normalized()).GetEuler();
            Box((from + to) * 0.5f, new(width, d.Length(), width), color, rotation * (180f / MathF.PI));
        }
        public void Box(Vector3 center, Vector3 size, Color color, Vector3? degrees = null)
        {
            Vector3 r = degrees ?? Vector3.Zero;
            var basis = Basis.FromEuler(r * (MathF.PI / 180f));
            Vector3 h = size * 0.5f;
            Vector3[] p = {
                new(-h.X,-h.Y,-h.Z), new(h.X,-h.Y,-h.Z), new(h.X,h.Y,-h.Z), new(-h.X,h.Y,-h.Z),
                new(-h.X,-h.Y,h.Z), new(h.X,-h.Y,h.Z), new(h.X,h.Y,h.Z), new(-h.X,h.Y,h.Z),
            };
            int[] idx = { 0,2,1, 0,3,2, 4,5,6, 4,6,7, 0,4,7, 0,7,3, 1,2,6, 1,6,5, 0,1,5, 0,5,4, 3,7,6, 3,6,2 };
            if (!_started) { _surface.Begin(Mesh.PrimitiveType.Triangles); _started = true; }
            for (int i = 0; i < idx.Length; i += 3)
            {
                Vector3 a = center + basis * p[idx[i]], b = center + basis * p[idx[i + 1]], c = center + basis * p[idx[i + 2]];
                Vector3 normal = (b - a).Cross(c - a).Normalized();
                foreach (Vector3 v in new[] { a, b, c })
                {
                    _surface.SetNormal(normal); _surface.SetColor(color.SrgbToLinear()); _surface.AddVertex(v);
                }
            }
        }
        public ArrayMesh Finish() => (ArrayMesh)_surface.Commit();
    }
}
