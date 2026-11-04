using System;
using System.Linq;
using Godot;
using CCCPRacing.Track;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void AtomgradServicesTest()
    {
        var def = TrackDef.Load("res://tracks/atomgrad.json");
        var t = TrackBuilder.Build(def);
        var wash = new Vector2(def.Radiation.WashX, def.Radiation.WashZ);
        var building = t.Props.Single(p => p.Type == "wash");
        var bus = t.BusStops.Single();
        var fuel = FuelDepot.Layout(t, def.Depot);
        Check(new Vector2(building.Position.X, building.Position.Z) == wash,
            "Atomgrad services: wash trigger and building share the new forecourt");
        Check(wash.DistanceTo(bus.Position) > 100 && wash.DistanceTo(fuel.Zone) > 100 && bus.Position.DistanceTo(fuel.Zone) > 100,
            "Atomgrad services: all three service markers are more than 100 metres apart");
        bool Paved(Vector2 p)
        {
            var surface = t.Sample(p.X, p.Y).Surface;
            if (surface is SurfaceType.Concrete or SurfaceType.Pothole or SurfaceType.Gravel or SurfaceType.Puddle) return true;
            // The fuel entry joins an authored mud/gravel road; only the racing road may remain loose.
            var tileCentre = new Vector2((MathF.Floor(p.X / t.Tile) + 0.5f) * t.Tile, (MathF.Floor(p.Y / t.Tile) + 0.5f) * t.Tile);
            int i = t.NearestCenter(tileCentre, out float distance);
            return surface is SurfaceType.Mud or SurfaceType.Dirt && distance <= t.HalfWidth[i];
        }
        bool RouteClear(Vector2[] route, bool avoidTank = false)
        {
            for (int i = 0; i < route.Length - 1; i++)
            {
                int steps = (int)MathF.Ceiling(route[i].DistanceTo(route[i + 1]) * 2);
                for (int k = 0; k <= steps; k++)
                {
                    var p = route[i].Lerp(route[i + 1], k / (float)steps);
                    // Check the full car footprint rather than just a narrow line through the grass.
                    foreach (var offset in new[] { Vector2.Zero, Vector2.Left, Vector2.Right, Vector2.Up, Vector2.Down })
                        if (!Paved(p + offset * 0.9f))
                        {
                            var sample = p + offset * 0.9f;
                            GD.Print($"service route: unpaved at {sample}, surface {t.Sample(sample.X, sample.Y).Surface}");
                            return false;
                        }
                    if (t.Obstacles.FirstOrDefault(o => o.Center.DistanceTo(p) < o.Radius + 0.95f) is var obstacle && obstacle.Radius > 0)
                    {
                        GD.Print($"service route: blocked at {p}, obstacle {obstacle.Center} radius {obstacle.Radius}");
                        return false;
                    }
                    if (avoidTank && fuel.Tank.DistanceTo(p) < 2.4f) { GD.Print($"service route: tank too close at {p}"); return false; }
                }
            }
            return true;
        }
        Check(RouteClear(new[] { new Vector2(49, 217), new(62, 217), new(68, 221), new(68, 243), new(63, 248), new(44, 248) }),
            "Atomgrad services: a car-wide paved, obstacle-free loop connects both wash entrances to the road");
        Check(RouteClear(new[] { new Vector2(281, 198), new(273.5f, 193), fuel.Zone, new(278, 175), new(286, 174) }, true),
            "Atomgrad services: fuel forecourt has paved ingress and egress clear of the tank");
        Check(Paved(bus.Position) && bus.Position.X > t.Center[bus.Index].X,
            "Atomgrad services: bus shelter is paved and on the counter-running bus's kerb");
    }
}
