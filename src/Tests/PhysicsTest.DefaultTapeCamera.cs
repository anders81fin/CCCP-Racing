using System;
using CCCPRacing.Camera;
using Godot;
namespace CCCPRacing.Tests;
public partial class PhysicsTest
{
    void DefaultTapeCameraTests()
    {
        GD.Print($"Godot SmoothStep diagnostic: SmoothStep(35,120,90)={Mathf.SmoothStep(35,120,90)}");
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
    }
}
