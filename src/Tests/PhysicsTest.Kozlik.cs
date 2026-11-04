using System;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void KozlikSteeringTest()
    {
        foreach (string id in new[] { "bonus", "siberia" })
        {
            var definition=TrackDef.Load($"res://tracks/{id}.json");
            var track = TrackBuilder.Build(definition);
            var fx = new TyreEffects(); fx.Init(track); AddChild(fx);
            var patrol = new KozlikPatrol { AlwaysGuard = true }; AddChild(patrol);
            patrol.Init(track, fx, null, null, 0);
            // Production adds the GAI ambush after actor Init; Bonus has no hangar/radar props now.
            if(definition.SpeedTrap is {} trap)patrol.AddTrapSpot((int)trap.At,trap.Side=="left"?-1:1);
            float minimumTurnRadius=2.4f*patrol.Body.Scale.X/MathF.Tan(.61f);
            bool followsNose = true, finiteTurn = true, stopped = true;
            int parkedFrames = 0, departures = 0;
            bool wasParked = false, makesProgress = true;
            Vector2 progressAnchor = patrol.Position2D;
            int lastProgress = 0;
            for (int frame = 0; frame < 60 * 600; frame++)
            {
                Vector2 before = patrol.Position2D, forward = patrol.Forward2D;
                bool parked = patrol.Guarding;
                patrol.Tick(1f / 60, null, 0);
                Vector2 moved = patrol.Position2D - before;
                float distance = moved.Length(), turn = forward.AngleTo(patrol.Forward2D);
                if (distance > 0.0001f)
                {
                    Vector2 mid = forward.Rotated(turn * 0.5f);
                    followsNose &= Math.Abs(mid.Dot(moved.Normalized())) > 0.999f;
                    finiteTurn &= Math.Abs(turn) <= distance / minimumTurnRadius + 0.0001f;
                }
                else finiteTurn &= Math.Abs(turn) < 0.0001f;
                if (parked && patrol.Guarding) stopped &= distance < 0.00001f;
                if (patrol.Guarding) parkedFrames++;
                if (patrol.Guarding || patrol.Position2D.DistanceTo(progressAnchor) > 5)
                { progressAnchor = patrol.Position2D; lastProgress = frame; }
                makesProgress &= frame - lastProgress < 60 * 30;
                if (wasParked && !patrol.Guarding) departures++;
                wasParked = patrol.Guarding;
            }
            Check(followsNose && finiteTurn && stopped && makesProgress && parkedFrames > 60 && departures > 1,
                $"Kozlik {id}: forward/reverse motion, finite turning circle, stationary while parked, leaves guard bay ({parkedFrames} parked frames, {departures} departures, position={patrol.Position2D}, nose={followsNose}, turn={finiteTurn}, progress={makesProgress})");
            if (id == "bonus")
            {
                patrol.AddTrapSpot(120, -1); patrol.GuardAtTrap(); patrol.StartChase();
                var car = new CarSim(new CarParams(), track);
                bool chaseSteers = true;
                int movingFrames = 0;
                for (int frame = 0; frame < 60 * 20; frame++)
                {
                    float road = 180 + frame * (20f / 60);
                    SchoolScript.SetRolling(car, track, SchoolScript.RoadPoint(track, (int)road, frame % 240 < 120 ? -3 : 3), track.CenterDir((int)road), 20);
                    Vector2 before = patrol.Position2D, forward = patrol.Forward2D;
                    patrol.Tick(1f / 60, car, 0);
                    Vector2 moved = patrol.Position2D - before;
                    if (moved.Length() > 0.0001f)
                    {
                        Vector2 mid = forward.Rotated(forward.AngleTo(patrol.Forward2D) * 0.5f);
                        chaseSteers &= Math.Abs(mid.Dot(moved.Normalized())) > 0.999f;
                        movingFrames++;
                    }
                }
                Check(chaseSteers && movingFrames > 60, "Kozlik chase: leaves ambush and follows changing lanes by steering, without sliding");
                patrol.StopChase(); patrol.GuardAtTrap(); patrol.StartChase();
                bool caught = false;
                patrol.Caught += () => caught = true;
                SchoolScript.SetRolling(car, track, SchoolScript.RoadPoint(track, 185, -3), track.CenterDir(185), 0);
                for (int frame = 0; frame < 60 * 30 && !caught; frame++) patrol.Tick(1f / 60, car, 0);
                patrol.EndAutograph();
                for (int frame = 0; frame < 60; frame++) patrol.Tick(1f / 60, car, 0);
                Vector2 parkedAt = patrol.Position2D, parkedForward = patrol.Forward2D;
                for (int frame = 0; frame < 120; frame++) patrol.Tick(1f / 60, car, 0);
                Check(caught && parkedAt.DistanceTo(patrol.Position2D) < 0.00001f
                    && parkedForward.Dot(patrol.Forward2D) > 0.99999f,
                    "Kozlik autograph: brakes to a stop and waits without sideways movement or rotating in place");
            }
            patrol.Free(); fx.Free();
        }
    }
}
