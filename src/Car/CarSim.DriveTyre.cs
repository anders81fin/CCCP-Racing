using System;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Car;

public partial class CarSim
{
    // A tyre can transmit the requested force while close to rolling. Once overloaded, its
    // angular velocity persists and the contact loses bite as overspeed grows past the peak.
    float DriveTyre(Wheel w, float demand, float direction, float peak, float roadSpeed, SurfaceInfo surface, float dt)
    {
        float slipSpeed = direction * (w.SpinSpeed * P.WheelRadius - roadSpeed);
        // A rolling tyre cannot start a drive event with an artificial braking impulse.
        if (slipSpeed < 0) { w.SpinSpeed = roadSpeed / P.WheelRadius; slipSpeed = 0; }
        float reference = Math.Max(4, Math.Abs(roadSpeed));
        float slip = slipSpeed / reference;
        float wanted = direction * demand;
        if (wanted <= peak && slip <= surface.PeakDriveSlip)
        {
            w.SpinSpeed = roadSpeed / P.WheelRadius;
            w.SlipLong = 0;
            w.Spinning = false;
            return demand;
        }

        float excess = Math.Max(0, slip / surface.PeakDriveSlip - 1);
        float grip = surface.SpinGrip + (1 - surface.SpinGrip) * MathF.Exp(-excess * 1.5f);
        float contactForce = peak * grip;
        // As overspeed decays the contact can transmit stored rotational energy to the car.
        float totalRatio = (Gear > 0 ? P.Gears[Gear - 1] : P.ReverseGear) * FinalDrive;
        float inertia = P.DriveWheelInertia + (Disengaged ? 0 : P.EngineInertia * totalRatio * totalRatio * 0.5f);
        contactForce = Math.Min(contactForce, Math.Max(0, wanted + slipSpeed * inertia / (P.WheelRadius * P.WheelRadius * dt)));
        float delta = (wanted - contactForce) * P.WheelRadius / inertia * dt;
        float angular = w.SpinSpeed * direction + delta;
        float rolling = direction * roadSpeed / P.WheelRadius;
        angular = Math.Max(rolling, angular);
        float limit = (P.Redline + 100) * MathF.Tau / (60 * totalRatio);
        w.SpinSpeed = direction * Math.Min(angular, Math.Max(rolling, limit));
        w.SlipLong = Math.Max(0, direction * (w.SpinSpeed * P.WheelRadius - roadSpeed) / reference);
        w.Spinning = w.SlipLong > surface.PeakDriveSlip;
        return direction * contactForce;
    }
}
