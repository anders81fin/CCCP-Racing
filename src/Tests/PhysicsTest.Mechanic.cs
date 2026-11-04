using System;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void MechanicRepairTest()
    {
        foreach (string id in new[] { "ilyich", "bonus" })
        {
            var track = TrackBuilder.Build(TrackDef.Load($"res://tracks/{id}.json"));
            var crew = new DrunkMen(); AddChild(crew); crew.Init(track);
            var car = new CarSim(new CarParams(), new FlatGround());
            var post = crew.MechanicPostPosition.Value;
            car.Reset(new Vector3(post.X + 5, .6f, post.Y), 0);
            var health = new EngineHealth();
            bool begun = false, done = false, interrupted = false;
            int blows = 0, lastBlows = 0, completions = 0;
            crew.RepairStarted += () => begun = true;
            crew.Repaired += () => { done = true; completions++; health.Repair(); };
            crew.RepairInterrupted += () => interrupted = true;
            crew.Whack += (_, big) => { blows++; if (big) lastBlows++; };

            (float work, float condition, int hits) Run(float start, float dt, bool stopEarly = false)
            {
                crew.ResetMechanic(); health.Reset(true); health.Set(start);
                begun = done = interrupted = false; blows = lastBlows = completions = 0;
                float working = 0;
                for (int tick = 0; tick < 60 / dt; tick++)
                {
                    bool wasWorking = crew.Repairing;
                    crew.MechanicTick(dt, car, true, true, !done, health.Health);
                    if (wasWorking) working += dt;
                    if (crew.Repairing && !done) health.RepairTo(start + (1 - start) * crew.RepairProgress);
                    crew.Tick(dt, car, 0); // Exercise the matching hammer/bottle animation too.
                    if (stopEarly && begun && working >= .5f)
                    {
                        crew.Interrupt();
                        float kept = health.Health;
                        for (int i = 0; i < 60; i++) crew.MechanicTick(dt, car, true, false, true, health.Health);
                        Check(interrupted && !done && kept > start && kept < 1 && health.Health == kept,
                            $"mechanic {id}: interruption keeps only completed work");
                        return (working, health.Health, blows);
                    }
                    if (done) break;
                }
                float expected = DrunkMen.RepairWorkFor(start);
                Check(begun && done && completions == 1 && health.Health == 1 && Math.Abs(working - expected) <= dt * 1.1f,
                    $"mechanic {id}: {start * 100:F0}% to full in {working:F3} s (expected {expected:F3})");
                Check(blows >= 1 && lastBlows == 1, $"mechanic {id}: repair ends with one final hammer blow");
                return (working, health.Health, blows);
            }

            var heavy = Run(.01f, 1f / 120);
            var medium = Run(.5f, 1f / 120);
            var light = Run(.99f, 1f / 120);
            Check(heavy.work > medium.work && medium.work > light.work && heavy.hits > light.hits,
                $"mechanic {id}: more damage takes longer and needs more hammer blows");
            Run(1, 1f / 120); // Body-only damage still gets a real repair and completion event.
            Run(.99f, .25f); // Low frame rate must not defer completion until all skipped blows fire.
            var partial = Run(.01f, 1f / 120, true);
            var resumed = Run(partial.condition, 1f / 120);
            Check(resumed.work < heavy.work, $"mechanic {id}: returning with a partly repaired engine takes less time");
            crew.Free();
        }
    }
}
