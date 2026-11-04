using System;
using CCCPRacing.Camera;
using Godot;

namespace CCCPRacing.Tests;
public partial class PhysicsTest
{
    void FreeCameraTests()
    {
        var facing = new ReplayFreeCamera(Transform3D.Identity, Vector3.Zero);
        facing.Move(Vector3.Back, .1f, 12);
        Check(facing.Position.IsEqualApprox(Vector3.Forward * 1.2f), "free camera: W axis moves toward the lens forward direction");
        var c = new ReplayFreeCamera(new Transform3D(Basis.FromEuler(new Vector3(-1, .3f, 0)), new Vector3(10, 20, 30)), Vector3.Zero);
        Check(!c.Follow && !c.Look, "free camera: fixed position and manual aim by default");
        Vector3 p = c.Position; c.Move(Vector3.Forward, .1f, 12);
        Check(c.Position.Y == p.Y && Mathf.Abs(c.Position.DistanceTo(p) - 1.2f) < .0001f, "free camera: pitched horizontal movement stays on world plane");
        p = c.Position; c.Move(Vector3.Up, .1f, 12);
        Check((c.Position - p).IsEqualApprox(Vector3.Up * 1.2f), "free camera: height is world vertical");
        p = c.Position; var b = c.Basis; c.UpdateTarget(new Vector3(10, 5, 10));
        Check(c.Position == p && c.Basis.IsEqualApprox(b), "free camera: fixed position and direction ignore moving target");
        c.SetFollow(true, new Vector3(10, 5, 10)); c.UpdateTarget(new Vector3(20, 7, 9));
        Check((c.Position - p).IsEqualApprox(new Vector3(10, 2, -1)) && c.Basis.IsEqualApprox(b), "free camera: follow translates position without forcing aim");
        c.SetFollow(false, new Vector3(20, 7, 9)); p = c.Position;
        var target = Vector3.Zero; c.SetLook(true, target);
        Check(c.Position == p && (-c.Basis.Z).Dot((target - p).Normalized()) > .9998f, "free camera: look-at aims without moving the fixed camera");
        b = c.Basis; c.Turn(new Vector2(300, -200));
        Check(c.Basis.IsEqualApprox(b), "free camera: target aim owns orientation while enabled");
        c.SetLook(false, target); c.Turn(new Vector2(80, 30));
        Check(!c.Basis.IsEqualApprox(b) && c.Position == p, "free camera: mouse changes orientation only");
        c.Turn(new Vector2(0, 100000));
        Check(c.Basis.IsFinite() && Mathf.Abs(c.Basis.GetEuler().X) <= Mathf.DegToRad(89.01f), "free camera: pitch limit avoids flipping at vertical");
        var floor = new ReplayFreeCamera(new Transform3D(Basis.Identity, new Vector3(4, -20, 8)), Vector3.Zero);
        floor.ConstrainGround((x, z) => x + z, .7f);
        Check(Mathf.Abs(floor.Position.Y - 12.7f) < .0001f, "free camera: descending cannot put camera below sloping terrain");
        floor.SetLook(true, Vector3.Zero);
        floor.Move(Vector3.Right, .1f, 40);
        floor.ConstrainGround((x, z) => x + z + 50, .7f);
        Check(floor.Position.Y >= floor.Position.X + floor.Position.Z + 50.69f
            && (-floor.Basis.Z).Dot((-floor.Position).Normalized()) > .999f, "free camera: terrain correction also preserves independent look-at");
        p = c.Position; b = c.Basis; c.Move(new Vector3(float.NaN, 0, 0), .1f, 12); c.Move(Vector3.Up, float.NaN, 12); c.Turn(new Vector2(float.NaN, 0)); c.UpdateTarget(new Vector3(float.PositiveInfinity, 0, 0));
        Check(c.Position == p && c.Basis.IsEqualApprox(b), "free camera: invalid controls cannot poison the pose");
        p = c.Position; c.Move(Vector3.One, 10, 12);
        Check(Mathf.Abs(c.Position.DistanceTo(p) - 1.2f) < .0001f, "free camera: diagonal speed normalized and hitch movement bounded");
    }
}
