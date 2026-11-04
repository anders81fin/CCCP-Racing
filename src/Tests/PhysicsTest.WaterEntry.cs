using System;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>A trench along -Z (the river's bed, 0.7 m below the banks) of the given surface.</summary>
    sealed class TrenchGround : IGround
    {
        public SurfaceType Bed = SurfaceType.Water;
        public float Z0 = -30f, Z1 = -44f, Depth = -0.9f, Ramp = 3f;
        public TrenchGround(float width = 14f) { Z1 = Z0 - width; }
        public GroundSample Sample(float x, float z)
        {
            bool inside = z < Z0 && z > Z1;
            // sloped banks, 3 m each (a river bank, not a wall): the bed between
            float h = z >= Z0 ? 0f : z > Z0 - Ramp ? Depth * (Z0 - z) / Ramp : z > Z1 + Ramp ? Depth : z > Z1 ? Depth * (z - Z1) / Ramp : 0f;
            return new() { Height = h, Normal = Vector3.Up, Surface = inside ? Bed : SurfaceType.Gravel, WaterDepth = inside && Bed is SurfaceType.Water or SurfaceType.Shallow ? Math.Max(0f, -h) : 0f };
        }
    }

    /// <summary>Speed into the water (m/s, a jump or a straight run), speed out on the far bank, the rebound off the bed and what the water took.</summary>
    (float entry, float exit, float rebound, float minSpeed) WaterRun(float speed, SurfaceType bed, float airHeight = 0f, float startZ = -5f, float width = 14f)
    {
        var car = NewCar(new TrenchGround(width) { Bed = bed }, new CarParams());
        car.Reset(new Vector3(0, 0.8f + airHeight, startZ), 0);    
        car.Vel = new Vector3(0, 0, -speed);
        float minSpeed = speed, entry = 0, exit = 0, rebound = 0; bool inside = false;
        float maxBedY = float.MinValue, lastVy = 0; int bounces = 0;
        for (float t = 0; t < 4f; t += Dt)
        {
            car.SetControls(true, false, 0, false);
            car.Step(Dt);
            float z = car.Pos.Z, v = new Vector2(car.Vel.X, car.Vel.Z).Length();
            if (!inside && z < -30f) { inside = true; entry = v; }
            if (inside && z < -30f - width) { exit = v; break; }
            if (OS.HasEnvironment("WATERTRACE") && width < 10f && bed == SurfaceType.Water && (int)(t / Dt) % 12 == 0 && z < -30f) GD.Print($"    t {t:F2} z {z:F1} y {car.Pos.Y:F2} vy {car.Vel.Y:F2} v {new Vector2(car.Vel.X, car.Vel.Z).Length() * 3.6f:F0}");
            if (inside)
            {
                minSpeed = Math.Min(minSpeed, v);
                if (z < -35f && z > -30f - width + 3.8f)      // (on the bed itself, not on the bank that climbs out)
                {
                    if (lastVy < -0.5f && car.Vel.Y > 0.5f) bounces++;       // a hop: down, then up again
                    rebound = Math.Max(rebound, car.Vel.Y);
                    lastVy = car.Vel.Y;
                }
            }
        }
        if (exit == 0) exit = new Vector2(car.Vel.X, car.Vel.Z).Length();
        GD.Print($"  water {bed}: {speed * 3.6f:F0} km/h, air {airHeight:F1} m -> entry {entry * 3.6f:F0}, exit {exit * 3.6f:F0} km/h, hops {bounces}, rebound {rebound:F1} m/s");
        return (entry, exit, rebound, minSpeed);
    }

    void WaterEntryTest()
    {
        if (OS.GetEnvironment("WATERGHOST") is { Length: > 0 } gp) { GhostTrace(gp); return; }
        GD.Print("Water: the car into a river");
        var drive = WaterRun(25f, SurfaceType.Water);
        var jump = WaterRun(25f, SurfaceType.Water, 0.6f, -24f);
        var fast = WaterRun(35f, SurfaceType.Water, 0.6f, -18f);
        var dry = WaterRun(25f, SurfaceType.Gravel);
        var river = WaterRun(25f, SurfaceType.Water, 0f, -5f, 10f);
        var ford = WaterRun(25f, SurfaceType.Shallow, 0f, -5f, 10f);
        Check(river.exit < river.entry * 0.8f, "a river of ten metres at 90 km/h takes at least a fifth of the speed");
        Check(ford.exit > river.exit, "the shallows take less than the deep water");
        Check(drive.exit < dry.exit * 0.8f, "wading at 90 km/h costs much of the speed (water takes it, gravel not)");
        Check(jump.rebound < 2.5f && fast.rebound < 2.5f, "a landing in the water does not throw the car back up (no rubber-ball hops)");
    }

    /// <summary>WATERGHOST=ghost.bin: the car's path (x, y, z, speed, vertical speed) every 0.1 s, to see what a recorded lap did.</summary>
    void GhostTrace(string path)
    {
        var bytes = System.IO.File.ReadAllBytes(path);
        var g = CCCPRacing.Race.Ghost.FromBytes(bytes, new CarParams(), new FlatGround(), out string err);
        if (g == null) { GD.Print("ghost: " + err); return; }
        var car = NewCar(new FlatGround());
        float prevY = 0, prevT = 0;
        for (float t = 0; t < g.Duration; t += 0.1f)
        {
            g.Apply(t, car, 0);
            float vy = t > 0 ? (car.Pos.Y - prevY) / (t - prevT) : 0; prevY = car.Pos.Y; prevT = t;
            GD.Print($"GT {t:F1} x {car.Pos.X:F1} y {car.Pos.Y:F2} z {car.Pos.Z:F1} v {car.Vel.Length() * 3.6f:F0} vy {vy:F1}");
        }
    }
}
