using System;
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

    void CameraFeel(float dt, bool replayDriving = false)
    {
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
