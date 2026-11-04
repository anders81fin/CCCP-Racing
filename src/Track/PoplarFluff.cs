using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// Poplar fluff of high summer (Ilyich's Lamp): a few dozen soft white tufts drift slowly through the
/// view around the car, in world space (like Snowfall), tumbling a little and fading in and out.
/// Very sparse on purpose; the owner keeps it to a hint of the season.
/// </summary>
public partial class PoplarFluff : CpuParticles3D
{
    public Node3D Target;
    const float Above = 7f;

    public static PoplarFluff Create(Node3D target)
    {
        var tuft = new QuadMesh { Size = new Vector2(0.24f, 0.24f) };
        tuft.Material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1f, 1f, 0.97f),
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        };
        var fade = new Gradient();
        fade.SetColor(0, new Color(1, 1, 1, 0));
        fade.SetColor(1, new Color(1, 1, 1, 0));
        fade.AddPoint(0.15f, new Color(1, 1, 1, 0.85f));
        fade.AddPoint(0.85f, new Color(1, 1, 1, 0.85f));
        return new PoplarFluff
        {
            Name = "PoplarFluff",
            Target = target,
            Mesh = tuft,
            Amount = 110,
            Lifetime = 16f,
            Preprocess = 16f,
            LocalCoords = false,
            EmissionShape = EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(28f, 4f, 22f),
            Direction = Vector3.Up,
            Spread = 180f,
            InitialVelocityMin = 0.1f,
            InitialVelocityMax = 0.5f,
            Gravity = new Vector3(0.4f, -0.05f, 0.12f),       // a breath of wind, hardly any sinking
            DampingMin = 0.15f, DampingMax = 0.15f,
            AngleMin = 0f,
            AngleMax = 360f,
            AngularVelocityMin = -90f,
            AngularVelocityMax = 90f,
            ScaleAmountMin = 0.6f,
            ScaleAmountMax = 1.3f,
            ColorRamp = fade,
            CastShadow = ShadowCastingSetting.Off,
        };
    }

    public override void _Process(double delta)
    {
        if (Target != null) GlobalPosition = Target.GlobalPosition + Vector3.Up * Above;
    }
}
