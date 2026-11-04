using System;
using System.Linq;
using Godot;
using CCCPRacing.Track;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void AtomgradRailYardTest()
    {
        GD.Print("Atomgrad freight yard: clear rail corridor, buffer stop and reversing train");
        var def = TrackDef.Load("res://tracks/atomgrad.json");
        var t = TrackBuilder.Build(def);
        var yard = t.Props.Single(p => p.Type == "railyard");
        var trainDef = def.Zone.Train;
        Vector2 Local(Obstacle o) => o.Center - new Vector2(yard.Position.X, yard.Position.Z);
        Check(!t.Props.Any(p => p.Type == "wreck" && MathF.Abs(p.Position.X - trainDef.X) < 2f),
            "old wrecks no longer obstruct the service railway");
        Check(!t.Obstacles.Any(o => {
            var p = Local(o);
            return p.Y >= -24f && p.Y <= 31f && MathF.Abs(p.X) < o.Radius + 1.6f;
        }), "static yard obstacles leave the train corridor open");
        Check(t.Obstacles.Any(o => Local(o).DistanceTo(new Vector2(0, -27)) < 1.5f),
            "buffer stop has a collision footprint beyond the locomotive stop");
        Check(!t.Obstacles.Any(o => Local(o).DistanceTo(new Vector2(15.5f, 31)) < o.Radius + 2.5f),
            "service gate leaves room for a truck");
        Check(!t.Props.Any(p => p.Type is "birch" or "pine" or "spruce" or "redpine" or "pole"
            && p.Position.X >= 125 && p.Position.X <= 158 && p.Position.Z >= 209 && p.Position.Z <= 269),
            "procedural trees and poles stay out of the working yard");

        var zone = new ZoneLife(); AddChild(zone); zone.Init(t, null, def.Zone);
        var train = zone.GetNode<Node3D>("Train");
        Vector3 previous = train.GlobalPosition;
        bool north = false, south = false, parkedInYard = false, continuous = true, collisionsFollow = true;
        for (int step = 0; step < 6000; step++)
        {
            zone.Tick(0.05f, null);
            var delta = train.GlobalPosition - previous;
            continuous &= new Vector2(delta.X, delta.Z).Length() <= 0.451f && train.Rotation.Length() < 0.001f;
            north |= delta.Z < -0.01f; south |= delta.Z > 0.01f;
            float nose = train.GlobalPosition.Z - 13f;
            if (MathF.Abs(nose - trainDef.End) < 0.05f && MathF.Abs(delta.Z) < 0.0001f)
            {
                parkedInYard = true;
                continuous &= train.GlobalPosition.Z + 38.6f < trainDef.Cross - 10f;
            }
            for (int i = 0; i < 9; i++)
            {
                var obstacle = t.Obstacles[t.Obstacles.Count - 9 + i];
                collisionsFollow &= obstacle.Center.DistanceTo(new Vector2(trainDef.X, train.GlobalPosition.Z - 11.5f + i * 6.25f)) < 0.001f;
            }
            previous = train.GlobalPosition;
        }
        Check(north && south && parkedInYard, "train reaches the freight yard, stops and reverses during 300 simulated seconds");
        Check(continuous, "turnarounds do not rotate or teleport the consist; parked train clears the road");
        Check(collisionsFollow, "all nine moving collision circles follow the consist in both directions");
        zone.QueueFree();
    }
}
