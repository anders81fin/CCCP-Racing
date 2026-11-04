using System;

namespace CCCPRacing.Car;

/// <summary>Presentation estimate from the contact-normal relative speed already
/// recorded by Ghost. This is not the total kinetic energy of either vehicle:
/// breakable/soft contacts have already reduced the recorded impact speed.</summary>
public readonly record struct CollisionResponse(float EnergyJoules, float Weight)
{
    public static CollisionResponse Of(float normalSpeed, float massKg)
    {
        if (!float.IsFinite(normalSpeed) || !float.IsFinite(massKg) || normalSpeed <= 0 || massKg <= 0)
            return default;
        float speed = Math.Min(normalSpeed, 200), mass = Math.Min(massKg, 100000);
        float energy = .5f * mass * speed * speed;
        return new(energy, energy / (energy + 80000f));
    }

    public float Duration => .16f + .62f * Weight;
    public float CameraTravel => .015f + .42f * Weight;
    public float CameraAngle => .12f + 3.1f * Weight; // degrees
    public float Duck => .2f + .65f * Weight;
}
