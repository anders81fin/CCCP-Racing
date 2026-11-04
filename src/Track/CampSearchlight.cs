using System;
using Godot;

namespace CCCPRacing.Track;

/// <summary>A tower-mounted lamp; DayNight controls the light, and its head follows a slow local sweep.</summary>
public partial class CampSearchlight : Node3D
{
    readonly Node3D _head = new() { Name = "Head" };
    SpotLight3D _light;
    MeshInstance3D _beam;
    StandardMaterial3D _haze;
    Vector3 _a, _b;
    float _time, _step;

    public static void Attach(Node3D tower, Vector3 origin, float yaw, float legs,
        Vector3 targetA, Vector3 targetB, float phase)
    {
        var basis = new Basis(Vector3.Up, yaw);
        Vector3 localA = basis.Inverse() * (targetA - origin);
        Vector3 localB = basis.Inverse() * (targetB - origin);
        Vector3 face = (localA + localB) * .5f;
        face.Y = 0;
        // Mount outside the cabin wall so the housing and beam clear the tower itself.
        var lamp = new CampSearchlight { Name = "Searchlight", Position = face.Normalized() * 1.65f + Vector3.Up * (legs + 1.35f), _time = phase };
        lamp._a = localA - lamp.Position;
        lamp._b = localB - lamp.Position;
        tower.AddChild(lamp);
        lamp.Build();
        lamp.UpdateHead();
    }

    void Build()
    {
        AddChild(Props.Part(Props.Box(.18f, .5f, .18f), new Color(.23f, .25f, .24f), new Vector3(0, -.2f, 0)));
        AddChild(_head);
        _head.AddChild(Props.Part(Props.Cyl(.36f, .6f, 12), new Color(.26f, .29f, .27f), Vector3.Zero, new Vector3(90, 0, 0)));
        _head.AddChild(NightGlow.Part(Props.Cyl(.29f, .025f, 12), new Color(.64f, .70f, .72f), new Color(.85f, .93f, 1f), 1.8f, new Vector3(0, 0, -.315f)));
        _head.GetChild<Node3D>(1).RotationDegrees = new Vector3(90, 0, 0);
        _light = new SpotLight3D
        {
            Name = "BeamLight", Position = new Vector3(0, 0, -.36f),
            LightColor = new Color(.82f, .91f, 1f), LightEnergy = 0, Visible = false,
            SpotRange = 38f, SpotAngle = 15f, SpotAttenuation = .65f,
            ShadowEnabled = true, LightSpecular = 0,
            DistanceFadeEnabled = true, DistanceFadeBegin = 85f, DistanceFadeLength = 25f,
        };
        _light.SetMeta("night_energy", 12f);
        _head.AddChild(_light);
        _haze = NightGlow.Keep(new StandardMaterial3D
        {
            AlbedoColor = new Color(.67f, .79f, 1f, .025f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        });
        _beam = new MeshInstance3D
        {
            Name = "SnowHaze", MaterialOverride = _haze, Visible = false,
            Mesh = new CylinderMesh { TopRadius = 1, BottomRadius = .025f, Height = 1, RadialSegments = 12, Rings = 1, CapTop = false, CapBottom = false },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _head.AddChild(_beam);
    }

    public override void _Process(double delta)
    {
        _step += (float)delta;
        if (_step < 1f / 12f) return;
        _time += _step;
        _step = 0;
        _beam.Visible = _light.Visible;
        _haze.AlbedoColor = new Color(.67f, .79f, 1f, .025f * Math.Clamp(_light.LightEnergy / 12f, 0, 1));
        if (_light.Visible && _a != _b) UpdateHead();
    }

    void UpdateHead()
    {
        Vector3 target = _a.Lerp(_b, .5f + .5f * MathF.Sin(_time * MathF.Tau / 38f));
        _head.Basis = Basis.LookingAt(target.Normalized(), Vector3.Up);
        // Stop the visible haze at the ground, rather than letting a cone cut through the snow.
        float length = target.Length();
        float radius = length * MathF.Tan(Mathf.DegToRad(15));
        _beam.Transform = new Transform3D(new Basis(Vector3.Right, -MathF.PI / 2).Scaled(new Vector3(radius, radius, length)),
            new Vector3(0, 0, -.36f - length / 2));
    }
}
