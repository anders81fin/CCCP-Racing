using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// Heavy snowfall over a winter track: flakes drift down in a box that follows the car (Target),
/// in world space so the car drives through them. Unshaded white, a little dimmer at night with
/// the rest of the unshaded world.
/// </summary>
public partial class Snowfall : CpuParticles3D
{
    /// <summary>What the snow falls around (the car's visual).</summary>
    public Node3D Target;

    const float Above = 16f;

    public static Snowfall Create(Node3D target)
    {
        var flake = new QuadMesh { Size = new Vector2(0.16f, 0.16f) };
        flake.Material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.97f, 0.98f, 1f),
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        };
        return new Snowfall
        {
            Name = "Snowfall",
            Target = target,
            Mesh = flake,
            Amount = 2700,          // thick: it snows hard in Siberia
            Lifetime = 7.5f,
            Preprocess = 7.5f,
            LocalCoords = false,
            EmissionShape = EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(34f, 1.5f, 26f),
            Direction = Vector3.Down,
            Spread = 18f,
            InitialVelocityMin = 1.8f,
            InitialVelocityMax = 2.8f,
            Gravity = new Vector3(0.6f, -0.4f, 0.2f),     // a breath of wind
            ScaleAmountMin = 0.6f,
            ScaleAmountMax = 1.4f,
            CastShadow = ShadowCastingSetting.Off,
        };
    }

    public override void _Process(double delta)
    {
        if (Target != null) GlobalPosition = Target.GlobalPosition + Vector3.Up * Above;
    }
}
