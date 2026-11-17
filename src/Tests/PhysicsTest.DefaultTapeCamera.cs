using System;
using CCCPRacing.Camera;
using Godot;
namespace CCCPRacing.Tests;
public partial class PhysicsTest
{
    void DefaultTapeCameraTests()
    {
        GD.Print($"Godot SmoothStep diagnostic: SmoothStep(35,120,90)={Mathf.SmoothStep(35,120,90)}");
        // The classic look-ahead (the experiment off, or --camera-classic) keeps its original contract; the directed one is tested in PhysicsTest.CameraLead.cs.
        bool wasDirected = ChaseCamera.DirectedLead; ChaseCamera.DirectedLead = false;
        var cam = new ChaseCamera { ShakeEnabled = false }; AddChild(cam);
        foreach (float kmh in new[] { 0f, 10f, 35f, 80f, 120f, 300f })
        {
            Vector3 velocity = Vector3.Forward * (kmh / 3.6f);
            cam.SnapDriving(Vector3.Zero, velocity);
            Vector3 lead = velocity * cam.LookAhead;
            float scale = (cam.GlobalPosition - lead).Length() / cam.Offset.Length();
            bool endpoint = kmh == 0 ? Math.Abs(scale-.87f) < .001f : kmh == 35 ? Math.Abs(scale-1) < .001f
                : kmh >= 120 ? Math.Abs(scale-1.25f) < .001f : scale >= .87f && scale <= 1.25f;
            Check(endpoint && cam.Fov == 45, $"default tape camera: {kmh:F0} km/h uses bounded normal driving distance with45deg lens");
            Vector3 first = cam.GlobalPosition;
            for (int n=0; n<60; n++) { cam.Feel(kmh,0,false,0,1/30f); cam.Follow(Vector3.Zero,velocity,1/30f); }
            Check(first.DistanceTo(cam.GlobalPosition) < .001f, $"default tape camera: cut at {kmh:F0} km/h has no extra zoom settling");
        }
        cam.MaxLead=8; cam.SnapDriving(Vector3.Zero,Vector3.Forward*100);
        Check(Math.Abs((cam.GlobalPosition-Vector3.Forward*8).Length()/cam.Offset.Length()-1.25f)<.001f,
            "default tape camera: overdrive lead limit retained without changing camera distance");
        cam.Free();
        // The directed look-ahead: the same bounded distance, and no settling after a cut at any speed.
        ChaseCamera.DirectedLead = true;
        var dcam = new ChaseCamera { ShakeEnabled = false }; AddChild(dcam);
        foreach (float kmh in new[] { 0f, 10f, 35f, 80f, 120f, 300f })
        {
            Vector3 velocity = Vector3.Forward * (kmh / 3.6f);
            dcam.SnapDriving(Vector3.Zero, velocity);
            var lead = new Vector3(dcam.LeadOffset.X, 0, dcam.LeadOffset.Y);
            float scale = (dcam.GlobalPosition - dcam.Focus).Length() / dcam.Offset.Length();
            bool endpoint = kmh == 0 ? Math.Abs(scale - .87f) < .001f : kmh == 35 ? Math.Abs(scale - 1) < .001f : kmh >= 120 ? Math.Abs(scale - 1.25f) < .001f : scale >= .87f && scale <= 1.25f;
            Check(endpoint && dcam.Fov == 45, $"default tape camera (directed lead): {kmh:F0} km/h uses the bounded normal driving distance with the 45 degree lens");
            Vector3 first = dcam.GlobalPosition;
            Vector3 car = Vector3.Zero;
            for (int n = 0; n < 60; n++) { dcam.Feel(kmh, 0, false, 0, 1 / 30f); car += velocity / 30f; dcam.FollowDrive(car, velocity, 1 / 30f); }
            first += car;      // (a moving car: the view moves with it)
            Check(first.DistanceTo(dcam.GlobalPosition) < .01f, $"default tape camera (directed lead): cut at {kmh:F0} km/h has no extra settling ({first.DistanceTo(dcam.GlobalPosition):F4} m)");
        }
        dcam.MaxLead = 8; dcam.SnapDriving(Vector3.Zero, Vector3.Forward * 100);
        float snapLimit = new Vector2(dcam.Focus.X, dcam.Focus.Z).Length();
        Check(snapLimit <= 8.001f && snapLimit >= 7.9f && Math.Abs((dcam.GlobalPosition - dcam.Focus).Length() / dcam.Offset.Length() - 1.25f) < .001f,
            $"default tape camera (directed lead): overdrive lead limit 8 m holds on the final car-to-centre vector ({snapLimit:F2} m) without changing camera distance");
        dcam.Free(); ChaseCamera.DirectedLead = wasDirected;
    }
}
