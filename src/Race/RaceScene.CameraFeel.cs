using System;
using CCCPRacing.Camera;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The overhead camera's feel of the drive (ChaseCamera.Feel): close to a slow or standing car, it pulls back as the car gets fast,
/// rumbles finely with the ground under the wheels, and dips once when the car lands from a jump.
/// Only while racing; the car's handling is untouched.
/// </summary>
public partial class RaceScene
{
    float _feelAir;

    /// <summary>
    /// The view's rumble at full speed for each ground (m). The tracks' own driving surfaces (asphalt,
    /// gravel, dirt roads, packed snow) stay still: a rumble there would never stop. Only leaving the
    /// road for rough ground is felt, and lightly; potholes and hits keep their own jolts.
    /// </summary>
    static float RumbleOf(SurfaceType s) => s switch
    {
        SurfaceType.Grass or SurfaceType.DeadGrass => 0.04f,
        SurfaceType.Mud or SurfaceType.DeepSnow => 0.05f,
        SurfaceType.Field => 0.06f,
        SurfaceType.Shallow or SurfaceType.Water => 0.02f,
        _ => 0f,
    };

    /// <summary>
    /// TrackDef.ViewZones: where the car is in a zone's rectangle (full) or within its ramps outside it (partly), the overhead view pulls back and tilts to show what the zone is for. Each zone's weight is its share;
    /// the zones' eyes and tilts are blended by their weights, and the view's share is the sum of the weights (at most 1): where one zone's ramp runs over the next one's, the view goes from the one to the other
    /// without returning to the standard view in between. The camera eases to it (ChaseCamera.ZoneTarget). Directed look-ahead only: the classic camera never takes a zone.
    /// </summary>
    void ViewZoneFeel()
    {
        if (!ChaseCamera.DirectedLead)      // (the classic camera view keeps its own picture everywhere: the zones are for the directed one)
        {
            _cam.ZoneTarget = 0f;
            _dayNight?.FogRelief(0f);
            return;
        }
        float side = _track.ViewTurned ? -1f : 1f, sum = 0f, tilt = 0f;      // (a turned view looks from the other side)
        var eye = Vector3.Zero;
        foreach (var z in _track.ViewZones)
        {
            // (the distance outside the rectangle, each side in its own ramp: the east and north ones can be long, to start the change early and slowly)
            float dw = Math.Max(z.X0 - _car.Pos.X, 0f) / Math.Max(z.RampWest ?? z.Ramp, 0.1f), de = Math.Max(_car.Pos.X - z.X1, 0f) / Math.Max(z.RampEast ?? z.Ramp, 0.1f);
            float dn = Math.Max(z.Z0 - _car.Pos.Z, 0f) / Math.Max(z.RampNorth ?? z.Ramp, 0.1f), ds = Math.Max(_car.Pos.Z - z.Z1, 0f) / Math.Max(z.RampSouth ?? z.Ramp, 0.1f);
            float w = Mathf.SmoothStep(0f, 1f, 1f - Math.Clamp(MathF.Sqrt(dw * dw + de * de + dn * dn + ds * ds), 0f, 1f));
            if (w <= 0f) continue;
            sum += w;
            // (the eye stands Height up and Back behind the car; the view is tilted up from the line to the car until it looks Pitch degrees down: positive tilt, the car lower on the screen; negative, higher)
            eye += w * new Vector3(0f, z.Height, side * z.Back);
            tilt += w * (MathF.Atan2(z.Height, z.Back) - Mathf.DegToRad(z.Pitch));
        }
        if (sum > 0f) { _cam.ZoneEye = eye / sum; _cam.ZoneTilt = tilt / sum; }
        float best = MathF.Min(1f, sum);
        _cam.ZoneTarget = best;
        _dayNight?.FogRelief(best);
    }

    void CameraFeel(float dt, bool replayDriving = false)
    {
        if (_track.ViewZones.Count > 0) ViewZoneFeel();
        bool live = _state == State.Racing && !InSchool;
        // Ghosts carry speed but not suspension airtime or drive acceleration.
        // Never inherit those transient values from the previous live drive.
        bool tapeDriving = replayDriving && Replaying;
        float rumble = 0, landing = 0;
        if (live && _car.GroundSurface is SurfaceType s)
            rumble = RumbleOf(s) * Mathf.SmoothStep(3f, 30f, _car.Speed);
        if (live && _feelAir > 0.25f && _car.AirTime == 0) landing = _feelAir;      // a real jump, not a bump
        _feelAir = live ? _car.AirTime : 0;
        // The start's countdown counts too: the view waits close to the car and pulls back on the go.
        bool driving = live || tapeDriving || _state == State.Countdown && !InSchool;
        _cam.Acceleration(_car.Vel, live && _car.AirTime == 0 && _car.LastImpact < 2 ? _car.DriveAcceleration : 0, dt);
        _cam.Feel(driving ? _car.Speed * 3.6f : -1f, rumble, live && _car.AirTime > 0.2f, live ? landing : 0f, dt);
    }
}
