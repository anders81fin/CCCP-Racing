using System;
using System.Collections.Generic;
using System.Reflection;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>
    /// The bonus track's tanks crush the car they roll onto (the owner's order of 2026-11-26): a hull rolling into the car, at CrushSpeed or more along its push, raises CarCrushed; a car that drives into a standing tank,
    /// or scrapes along a moving one, or a tank that rolls away, only collides. The hulls' circles carry their source ids and the car notes the contacts (the same contact record the duty officer's ram reads).
    /// </summary>
    void TankCrushTest()
    {
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
        var fx = new TyreEffects(); AddChild(fx); fx.Init(track);
        var tanks = new TankBattle(); AddChild(tanks);
        var tr = track.TankRange;
        tanks.Init(track, fx, new Rect2(tr.X0, tr.Z0, tr.X1 - tr.X0, tr.Z1 - tr.Z0), tr.Tanks);
        const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var list = (System.Collections.IList)typeof(TankBattle).GetField("_tanks", f).GetValue(tanks);
        object tank = list[0]; var tt = tank.GetType();
        int obsBase = (int)tt.GetField("ObsBase", f).GetValue(tank);
        var consume = typeof(TankBattle).GetMethod("ConsumeContacts", f);
        int crushed = 0; float lastRolling = 0f;
        tanks.CarCrushed += (_, v) => { crushed++; lastRolling = v; };

        // A scene: the car stands on open ground inside the range; the hull's three circles are set by hand (as Tick does) along heading yaw, centred at 'at'.
        Vector2 spot = new((tr.X0 + tr.X1) * .5f, (tr.Z0 + tr.Z1) * .5f);
        void Hull(Vector2 at, float yaw, float speed)
        {
            tt.GetField("Pos", f).SetValue(tank, at); tt.GetField("Yaw", f).SetValue(tank, yaw); tt.GetField("Speed", f).SetValue(tank, speed);
            Vector2 fwd = new(-MathF.Sin(yaw), -MathF.Cos(yaw));
            for (int s = 0; s < 3; s++)
                track.Obstacles[obsBase + s] = new Obstacle(at + fwd * (-2.2f + s * 2.2f), 1.8f, fwd * speed, Material: ImpactMaterial.Metal, Source: TankBattle.SourceBase, Part: s);
        }
        (int events, long contacts) Run(Vector3 carPos, float carYaw, float carSpeed, Vector2 hullAt, float hullYaw, float hullSpeed, int ticks)
        {
            var car = new CarSim(new CarParams(), track) { Obstacles = track.Obstacles };
            SchoolScript.SetRolling(car, track, new Vector2(carPos.X, carPos.Z), new Vector2(-MathF.Sin(carYaw), -MathF.Cos(carYaw)), carSpeed);
            consume.Invoke(tanks, new object[] { car });      // (the first call only learns where the history ends)
            crushed = 0; long before = car.ContactSerial; long mine = 0;
            for (int i = 0; i < ticks; i++)
            {
                // The hull moves on as the tank would (a straight roll), then the car steps and the tank reads what it touched.
                Vector2 fwd = new(-MathF.Sin(hullYaw), -MathF.Cos(hullYaw)); hullAt += fwd * hullSpeed / 60f;
                Hull(hullAt, hullYaw, hullSpeed);
                if (carSpeed == 0f) { car.SetControls(false, false, 0f, true); car.Brake = 1f; car.Neutral = true; }
                for (int sub = 0; sub < 2; sub++) car.Step(1f / 120f);
                consume.Invoke(tanks, new object[] { car });
            }
            foreach (var c in car.Contacts) if (c.Serial > before && c.Source == TankBattle.SourceBase) mine++;
            return (crushed, mine);
        }

        // 1. A tank rolling at 3 m/s onto a stationary car from the side: contacts and a crush.
        Vector2 carAt = spot; var carP = new Vector3(carAt.X, track.MeshHeight(carAt.X, carAt.Y), carAt.Y);
        var rolled = Run(carP, 0f, 0f, carAt + new Vector2(-14f, 0f), -MathF.PI / 2f, 3f, 60 * 7);      // heading -x*... (yaw -90 deg = +X)
        Check(rolled.contacts > 0 && rolled.events >= 1 && lastRolling >= TankBattle.CrushSpeed, $"tank crush: a hull rolling at 3 m/s onto a standing car touches it ({rolled.contacts} contacts) and crushes it (events {rolled.events}, rolling {lastRolling:F1} m/s)");
        // 2. A standing tank and a car driving into it at 10 m/s: only a collision.
        var rammed = Run(carP + new Vector3(-14f, 0f, 0f), -MathF.PI / 2f, 10f, carAt, 0f, 0f, 60 * 3);
        Check(rammed.contacts > 0 && rammed.events == 0, $"tank crush: a car driving into a standing tank only collides (contacts {rammed.contacts}, crushes {rammed.events})");
        // 3. A tank moving along the car's side (its velocity across the push) does not crush: it passes the car at 1 m clear of the circles' touching distance.
        var along = Run(carP, 0f, 0f, carAt + new Vector2(-2.0f, 18f), MathF.PI, 4f, 60 * 7);      // heading +z -> -z... drives along x=-2.0, overlapping the car side
        Check(along.events == 0, $"tank crush: a tank that scrapes along the car's side does not crush it (contacts {along.contacts}, crushes {along.events})");
        // 4. A tank rolling away from the car (the car is behind it) does not crush it.
        var away = Run(carP, 0f, 0f, carAt + new Vector2(-2f, 0f), MathF.PI / 2f, 3f, 60 * 3);
        Check(away.events == 0, $"tank crush: a tank driving away from the car does not crush it (crushes {away.events})");

        // 5. The tanks do not yield to the player's car (the owner's order of 2026-11-26): its circles are not among the movers a tank steers clear of, while another vehicle's (a ghost, as the held officer's) are.
        var movers = (List<(Vector2 p, float r)>)typeof(TankBattle).GetField("_movers", f).GetValue(tanks);
        var car5 = new CarSim(new CarParams(), track);
        SchoolScript.SetRolling(car5, track, carAt, new Vector2(1f, 0f), 0f);
        MoverBlockers.Clear(); MoverBlockers.AddCar(car5);
        var ghostAt = carAt + new Vector2(0f, 40f);
        MoverBlockers.SetGhost(this, new[] { new MoverBlockers.Circle(ghostAt, 1.2f, Vector2.Zero) });
        tanks.Tick(1f / 60f, car5);
        bool carIn = false, ghostIn = false;
        foreach (var (p, r) in movers) { if (p.DistanceTo(carAt) < 2f) carIn = true; if (p.DistanceTo(ghostAt) < 0.1f) ghostIn = true; }
        Check(!carIn && ghostIn, $"tank crush: the tanks do not steer clear of the player's car (in their movers: {carIn}), but still of another vehicle's circles ({ghostIn})");
        MoverBlockers.ClearGhosts(); MoverBlockers.Clear();
        tanks.QueueFree(); fx.QueueFree();
    }
}
