using System;
using Godot;

namespace CCCPRacing.Race;

/// <summary>What one driver asks of the car this tick. Pad: a pad drive input (a trigger or the
/// stick past its dead zone, the d-pad's left/right, A or LB) is in use; PadSteer: the stick steers.</summary>
public struct DriveControls
{
    public float Throttle, Brake, Steer;
    public bool Handbrake, Clutch, Horn, Pad, PadSteer;
    public float PadThrottle;
}

/// <summary>
/// One driver's controls: the keyboard (the InputMap actions) and a game pad (standard SDL layout,
/// JoyAxis/JoyButton) read for one device. RT throttle, LT brake (0..1 with a small dead zone),
/// left stick X steers through a radial dead zone and a response curve, the d-pad's left/right is
/// full lock, A handbrake, LB clutch, Y or the stick click the horn. Keys and pad never fight: the
/// pedals take the larger, the stick wins over the keys when it is out of its dead zone.
/// A later split screen gives each driver his own DriverInput and pad device.
/// </summary>
public class DriverInput
{
    public const float TriggerDead = 0.05f, StickDead = 0.12f, SteerCurve = 1.6f;

    /// <summary>The pad device read (-1: keys only).</summary>
    public int Device;
    /// <summary>The keyboard drives too (player one).</summary>
    public bool Keys = true;
    /// <summary>The pad was unplugged: read as released until it sends something again.</summary>
    public bool Gone;

    /// <summary>A trigger's raw 0..1 to a pedal: nothing under the dead zone, then the full range.</summary>
    public static float Pedal(float raw) => raw <= TriggerDead ? 0f : Math.Min(1f, (raw - TriggerDead) / (1f - TriggerDead));

    /// <summary>Stick to steering: a radial dead zone on the stick's travel (a little up or down does
    /// not steer), rescaled to full range, then sign·|x|^SteerCurve so small corrections at speed stay small.</summary>
    public static float Steering(float x, float y)
    {
        float m = MathF.Sqrt(x * x + y * y);
        if (m <= StickDead || Math.Abs(x) < 1e-4f) return 0f;
        float scaled = Math.Min(1f, (m - StickDead) / (1f - StickDead));
        float sx = Math.Clamp(x / m * scaled, -1f, 1f);
        return MathF.Sign(sx) * MathF.Pow(Math.Abs(sx), SteerCurve);
    }

    /// <summary>The raw trigger value that gives this pedal (tests, --pad-autodrive).</summary>
    public static float PedalRaw(float pedal) => pedal <= 0 ? 0f : TriggerDead + Math.Min(1f, pedal) * (1f - TriggerDead);

    /// <summary>The raw stick X (stick level) that gives this steering (tests, --pad-autodrive).</summary>
    public static float SteeringRaw(float steer)
    {
        if (steer == 0) return 0f;
        float a = MathF.Pow(Math.Min(1f, Math.Abs(steer)), 1f / SteerCurve);
        return MathF.Sign(steer) * (StickDead + a * (1f - StickDead));
    }

    public DriveControls Read()
    {
        var c = new DriveControls();
        if (Device >= 0 && !Gone)
        {
            int d = Device;
            float rt = Pedal(Input.GetJoyAxis(d, JoyAxis.TriggerRight)), lt = Pedal(Input.GetJoyAxis(d, JoyAxis.TriggerLeft));
            float stick = Steering(Input.GetJoyAxis(d, JoyAxis.LeftX), Input.GetJoyAxis(d, JoyAxis.LeftY));
            bool left = Input.IsJoyButtonPressed(d, JoyButton.DpadLeft), right = Input.IsJoyButtonPressed(d, JoyButton.DpadRight);
            bool hb = Input.IsJoyButtonPressed(d, JoyButton.A), clutch = Input.IsJoyButtonPressed(d, JoyButton.LeftShoulder);
            c.Throttle = c.PadThrottle = rt;
            c.Brake = lt;
            c.PadSteer = stick != 0;
            c.Steer = c.PadSteer ? stick : (right ? 1f : 0f) - (left ? 1f : 0f);
            c.Handbrake = hb;
            c.Clutch = clutch;
            c.Horn = Input.IsJoyButtonPressed(d, JoyButton.Y) || Input.IsJoyButtonPressed(d, JoyButton.LeftStick);
            c.Pad = rt > 0 || lt > 0 || stick != 0 || left || right || hb || clutch;
        }
        if (Keys)
        {
            if (Input.IsActionPressed("accelerate")) c.Throttle = 1f;
            if (Input.IsActionPressed("brake")) c.Brake = 1f;
            if (!c.PadSteer)
                c.Steer = Math.Clamp(c.Steer + Input.GetActionStrength("steer_right") - Input.GetActionStrength("steer_left"), -1f, 1f);
            c.Handbrake |= Input.IsActionPressed("handbrake");
            c.Clutch |= Input.IsActionPressed("clutch");
            c.Horn |= Input.IsActionPressed("horn");
        }
        return c;
    }
}
