using System;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void TractorResetCollisionTest()
    {
        var t = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        var fx = new TyreEffects(); AddChild(fx); fx.Init(t);
        var tractor = new DrunkTractor(); AddChild(tractor);
        Vector2 start = new(142, 36);
        tractor.Init(t, fx, start, MathF.PI);
        // Simulate a completed run before configuring the barn departure for the next rally.
        for (int i = 0; i < 1800; i++) tractor.Tick(1f / 60f);
        Vector2 lastPosition = t.Obstacles[tractor.BodyObstacle].Center;
        Check(lastPosition.DistanceTo(start) > 8, "Tractor has left its start before the reset regression check");
        var doors = new BarnDoors(5, 5, 5, Colors.Brown, Colors.Gray);
        AddChild(doors);
        tractor.ConfigureDeparture(doors);
        bool aligned = t.Obstacles[tractor.BodyObstacle].Center.DistanceTo(start) < 0.01f
            && t.Obstacles[tractor.PloughObstacle].Center.DistanceTo(start - tractor.Forward2D * 3.5f) < 0.01f;
        Check(aligned && new Vector2(tractor.GetNode<Node3D>("MT-Agro").GlobalPosition.X,
            tractor.GetNode<Node3D>("MT-Agro").GlobalPosition.Z).DistanceTo(start) < 0.01f,
            "Restart moves both tractor collision circles back with the visible tractor immediately");
        for (int i = 0; i < 600; i++) tractor.Tick(1f / 60f, true);
        Check(t.Obstacles[tractor.BodyObstacle].Center.DistanceTo(start) < 0.01f
            && t.Obstacles[tractor.BodyObstacle].Center.DistanceTo(lastPosition) > 8,
            "Barn departure waiting leaves no invisible obstacle at the previous run's position");
        RemoveChild(tractor); tractor.Free();
        RemoveChild(doors); doors.Free();
        RemoveChild(fx); fx.Free();
    }
}
