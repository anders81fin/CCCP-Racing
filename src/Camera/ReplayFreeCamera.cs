using System;
using Godot;
using CCCPRacing.Replay;

namespace CCCPRacing.Camera;

/// <summary>Transient replay framing. Position follows world axes, never car rotation; aim tracking is independent.
/// No tape, edit or gameplay camera settings are written.</summary>
public sealed class ReplayFreeCamera
{
    public Vector3 Position { get; private set; }
    public Basis Basis => Basis.FromEuler(new Vector3(_pitch, _yaw, 0));
    public bool Follow { get; private set; }
    public bool Look { get; private set; }
    Vector3 _target;
    float _pitch, _yaw;

    public ReplayFreeCamera(Transform3D pose, Vector3 target)
    {
        Position = pose.Origin; _target = target;
        var e = pose.Basis.GetEuler(); _pitch = e.X; _yaw = e.Y;
    }
    public LapFreeCamera Capture(Vector3 target, float fov, float near, float far)
    {
        UpdateTarget(target);
        var offset = Position - target;
        return new LapFreeCamera { X = Position.X, Y = Position.Y, Z = Position.Z,
            OffsetX = offset.X, OffsetY = offset.Y, OffsetZ = offset.Z,
            Yaw = Mathf.Wrap(_yaw, -Mathf.Pi, Mathf.Pi), Pitch = _pitch,
            Fov = fov, Near = near, Far = far, Follow = Follow, Look = Look };
    }
    /// <summary>Absolute evaluation at this car pose: no accumulated follow integration or dependence on previous seeks.</summary>
    public void Restore(LapFreeCamera shot, Vector3 target)
    {
        Position = shot.Follow ? target + new Vector3(shot.OffsetX, shot.OffsetY, shot.OffsetZ) : new Vector3(shot.X, shot.Y, shot.Z);
        _target = target; _yaw = shot.Yaw; _pitch = shot.Pitch; Follow = shot.Follow; Look = shot.Look;
        UpdateTarget(target);
    }
    public void SetFollow(bool on, Vector3 target) { UpdateTarget(target); Follow = on; }
    public void SetLook(bool on, Vector3 target) { Look = on; UpdateTarget(target); }
    public void UpdateTarget(Vector3 target)
    {
        if (!target.IsFinite()) return;
        if (Follow) Position += target - _target;
        _target = target;
        if (Look && Position.DistanceSquaredTo(target) > .0001f)
        {
            var d = (target - Position).Normalized();
            _yaw = Mathf.Atan2(-d.X, -d.Z);
            _pitch = Mathf.Asin(Mathf.Clamp(d.Y, -1f, 1f));
            _pitch = Mathf.Clamp(_pitch, -Mathf.DegToRad(89), Mathf.DegToRad(89));
        }
    }
    public void Move(Vector3 axes, float dt, float speed)
    {
        if (!axes.IsFinite() || !float.IsFinite(dt) || !float.IsFinite(speed) || dt <= 0 || speed < 0) return;
        var right = new Vector3(Mathf.Cos(_yaw), 0, -Mathf.Sin(_yaw));
        var forward = new Vector3(-Mathf.Sin(_yaw), 0, -Mathf.Cos(_yaw));
        Position += (right * axes.X + Vector3.Up * axes.Y + forward * axes.Z).LimitLength() * (Mathf.Min(dt, .1f) * speed);
        if (Look) UpdateTarget(_target);
    }
    /// <summary>Keep the eye above the actual rendered terrain, also after a seek/follow update.</summary>
    public void ConstrainGround(Func<float, float, float> height, float clearance)
    {
        float floor = height(Position.X, Position.Z) + clearance;
        if (!float.IsFinite(floor) || !float.IsFinite(clearance) || clearance < 0) return;
        if (Position.Y < floor)
        {
            Position = new Vector3(Position.X, floor, Position.Z);
            if (Look) UpdateTarget(_target);
        }
    }
    public void Turn(Vector2 mouse)
    {
        if (Look || !mouse.IsFinite()) return;
        _yaw = Mathf.Wrap(_yaw - mouse.X * .003f, -Mathf.Pi, Mathf.Pi);
        _pitch = Mathf.Clamp(_pitch - mouse.Y * .003f, -Mathf.DegToRad(89), Mathf.DegToRad(89));
    }
}
