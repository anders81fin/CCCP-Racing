using System;
using System.Linq;
using System.Reflection;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>The drunks' swig is an animation, not a switch: the arm rises, the bottle tips neck-up, the throat works, everything comes back, and no frame jumps. The bottle has a shape.</summary>
    void DrunkAnimationTest()
    {
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        var crew = new DrunkMen(); AddChild(crew); crew.Init(track);
        var car = new CarSim(new CarParams(), new FlatGround()); car.Reset(new Vector3(-900, .6f, -900), 0);      // (far away: no cheering)
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var men = (System.Collections.IList)typeof(DrunkMen).GetField("_men", flags).GetValue(crew);
        object man = null;
        foreach (var m in men)
        {
            bool lying = (bool)m.GetType().GetField("Lying", flags).GetValue(m);
            var root = (Node3D)m.GetType().GetField("Root", flags).GetValue(m);
            if (!lying && GodotObject.IsInstanceValid(root) && (Node3D)m.GetType().GetField("Bottle", flags).GetValue(m) is { Visible: true }) { man = m; break; }
        }
        if (man == null) { Check(false, "drunk animation: a standing drunk with a bottle exists on the kolkhoz"); return; }
        var arm = (Node3D)man.GetType().GetField("ArmR", flags).GetValue(man);
        var head = (Node3D)man.GetType().GetField("Head", flags).GetValue(man);
        var bottle = (Node3D)man.GetType().GetField("Bottle", flags).GetValue(man);
        Check(bottle.GetChildCount() >= 4 && bottle.GetChildren().OfType<MeshInstance3D>().Select(c => c.Position.Y).Distinct().Count() >= 4, "drunk animation: the bottle is a bottle (body, shoulder, neck, cap), not a tube");

        void Set(string field, float value) => man.GetType().GetField(field, flags).SetValue(man, value);
        Set("Cheer", 0f); Set("CheerK", 0f); Set("Drinking", 0f); Set("DrinkTimer", 0.001f);
        float lastArm = arm.Rotation.X, lastHead = head.Rotation.X, maxArmStep = 0f, maxHeadStep = 0f, maxArm = lastArm, maxTip = 0f, minArm = lastArm;
        int intermediate = 0;
        for (int i = 0; i < 60 * 5; i++)
        {
            crew.Tick(1f / 60f, car, 0f);
            float ax = arm.Rotation.X, hx = head.Rotation.X;
            if (i > 2) { maxArmStep = Math.Max(maxArmStep, Math.Abs(ax - lastArm)); maxHeadStep = Math.Max(maxHeadStep, Math.Abs(hx - lastHead)); }
            lastArm = ax; lastHead = hx; maxArm = Math.Max(maxArm, ax); minArm = Math.Min(minArm, ax);
            if (ax > 0.9f && ax < 1.9f) intermediate++;
            // The bottle's axis (its +y) in the body's frame: pointing up and a little back at the top of the swig (the hold in the fist was turned 180 degrees on 2026-11-09, the swig was not).
            Vector3 axis = (arm.Basis * bottle.Basis) * Vector3.Up;
            if (ax > 2.15f) maxTip = Math.Max(maxTip, axis.Y);
        }
        Check(maxArm > 2.0f, $"drunk animation: the arm comes up to the mouth ({maxArm:F2} rad)");
        Check(maxArmStep < 0.1f && maxHeadStep < 0.08f, $"drunk animation: no frame jumps (a switch would be 1.9 rad in one frame) (largest step: arm {maxArmStep:F3}, head {maxHeadStep:F3} rad)");
        Check(intermediate > 12, $"drunk animation: the arm is seen between the poses, not only at the two ends ({intermediate} frames)");
        Check(maxTip > 0.6f, $"drunk animation: the bottle is turned neck-up for the swig (axis up {maxTip:F2})");
        crew.QueueFree();
    }
}
