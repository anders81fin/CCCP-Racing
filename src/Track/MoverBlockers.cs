using System;
using System.Collections.Generic;
using CCCPRacing.Car;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// What the moving things of the world must not drive through (the owner's report of 2026-11-22: the player's car and the mechanic were air to them):
/// the player's car (its collision circles, refreshed by the race each step before the actors move) is added to the solids every vehicle already yields to
/// (GulagTruck.Solids: the bus's lane look-ahead, the ploughs and TrafficCap, BlocksAt for the tractors and the Kozlik, the BTRs and the tanks); and a
/// vehicle that rolls into a person (the mechanic, a drunk, an officer, a worker: anything Breakables knows as knockable) knocks them over, as the car does.
/// Pure functions on the track's obstacle list, so that they can be tested without a scene.
/// </summary>
public static class MoverBlockers
{
    public readonly record struct Circle(Vector2 Centre, float Radius, Vector2 Velocity);

    static readonly List<Circle> _cars = new();
    /// <summary>The cars in the world now (the player's, and the LAN drivers' on the host), as the circles they collide with.</summary>
    public static IReadOnlyList<Circle> Cars => _cars;

    public static void Clear() => _cars.Clear();

    // Vehicles that are not solid to the player's car for a reason of their own (the Kozlik on the car's tail, for the autograph: it must not be shunted on when the car stops dead) are still solid to every OTHER vehicle:
    // they register their circles here instead of leaving the world (the owner's report: the Kozlik's UAZ was air to the traffic during the stop and the ticket).
    static readonly Dictionary<object, Circle[]> _ghosts = new();
    public static IEnumerable<KeyValuePair<object, Circle[]>> Ghosts => _ghosts;
    public static void SetGhost(object owner, Circle[] circles) { if (circles == null || circles.Length == 0) _ghosts.Remove(owner); else _ghosts[owner] = circles; }
    public static void ClearGhosts() => _ghosts.Clear();

    public static void AddCar(CarSim car)
    {
        if (car == null) return;
        foreach (float cz in CarSim.CollisionCircleZ)
        {
            Vector3 rel = car.Rot * new Vector3(0, 0, cz);
            _cars.Add(new Circle(new Vector2(car.Pos.X + rel.X, car.Pos.Z + rel.Z), CarSim.CollisionRadius, new Vector2(car.Vel.X, car.Vel.Z)));
        }
    }

    /// <summary>A mover at least this fast (m/s) knocks over what it rolls into (a tractor creeps at 1.4).</summary>
    public const float KnockSpeed = 0.9f;

    /// <summary>
    /// The movers' circles (the track's obstacles that have a velocity: the vehicles set it) against the knockable people: any person overlapped by a mover's circle
    /// is knocked over (tryBreak, the Breakables' own: it returns true when they fall). Returns how many fell. A person the mover is already past is not hit twice:
    /// a fallen one is out of the knockable set by then.
    /// </summary>
    public static int KnockPass(TrackData t, IEnumerable<int> knockables, Func<int, Vector3, bool> tryBreak)
    {
        int fell = 0;
        var list = new List<int>(knockables);
        if (list.Count == 0) return 0;
        for (int m = 0; m < t.Obstacles.Count; m++)
        {
            var mover = t.Obstacles[m];
            if (mover.Radius < 0.6f || mover.Center.X < -9000f || mover.Velocity.LengthSquared() < KnockSpeed * KnockSpeed) continue;
            foreach (int i in list)
            {
                if (i == m) continue;
                var man = t.Obstacles[i];
                if (man.Center.X < -9000f) continue;
                float reach = mover.Radius + man.Radius;
                if (mover.Center.DistanceSquaredTo(man.Center) >= reach * reach) continue;
                if (tryBreak(i, new Vector3(mover.Velocity.X, 0f, mover.Velocity.Y))) fell++;
            }
        }
        return fell;
    }
}
