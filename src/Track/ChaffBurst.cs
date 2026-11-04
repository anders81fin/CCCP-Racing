using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// Straw and chaff: golden thin bits flying across the view. Either a one-shot burst (a hay bale
/// knocked over: Fire) or a light stream (driving fast through a field: Aim).
/// </summary>
public partial class ChaffBurst : CpuParticles3D
{
    public static ChaffBurst Create(bool burst)
    {
        var bit = new QuadMesh { Size = new Vector2(0.5f, 0.1f) };
        bit.Material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.93f, 0.77f, 0.32f),
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        };
        return new ChaffBurst
        {
            Name = burst ? "HayChaff" : "FieldChaff",
            Mesh = bit,
            Amount = burst ? 34 : 14,
            Lifetime = burst ? 1.7f : 1.2f,
            OneShot = burst,
            Emitting = false,
            Explosiveness = burst ? 0.95f : 0f,
            LocalCoords = false,
            EmissionShape = EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.6f,
            Direction = Vector3.Up,
            Spread = burst ? 80f : 40f,
            InitialVelocityMin = burst ? 2.5f : 3f,
            InitialVelocityMax = 8f,
            Gravity = new Vector3(0.6f, -5.5f, 0.2f),
            DampingMin = 0.8f, DampingMax = 0.8f,
            AngleMin = 0f,
            AngleMax = 360f,
            AngularVelocityMin = -300f,
            AngularVelocityMax = 300f,
            ScaleAmountMin = 0.6f,
            ScaleAmountMax = 1.3f,
            CastShadow = ShadowCastingSetting.Off,
        };
    }

    /// <summary>A one-shot burst at a world point.</summary>
    public void Fire(Vector3 at)
    {
        GlobalPosition = at + Vector3.Up * 0.5f;
        Restart();
        Emitting = true;
    }

    /// <summary>The stream follows a point, flung up and forwards of the motion.</summary>
    public void Aim(Vector3 at, Vector3 vel, bool on)
    {
        GlobalPosition = at + Vector3.Up * 0.4f;
        var flat = new Vector3(vel.X, 0, vel.Z);
        Direction = (Vector3.Up * 1.4f + (flat.LengthSquared() > 1f ? flat.Normalized() * 0.5f : Vector3.Zero)).Normalized();
        if (Emitting != on) Emitting = on;
    }
}
