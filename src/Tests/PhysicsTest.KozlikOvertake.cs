using System;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    // --kozlik-overtake-test: a slow vehicle (2.5 m/s, a tractor and trailer, 1.5 m radius) on the road ahead of the Kozlik's UAZ must be passed, without driving into it.
    void KozlikOvertakeTest()
    {
        foreach (string id in new[] { "ilyich", "bonus", "atomgrad" })
        foreach (float offset in new[] { 0f, 1.2f, -1.2f })
        {
            var track = TrackBuilder.Build(TrackDef.Load($"res://tracks/{id}.json"));
            var fx = new TyreEffects(); fx.Init(track); AddChild(fx);
            var patrol = new KozlikPatrol(); AddChild(patrol);
            patrol.Init(track, fx, null, null, 100);
            int n = track.Center.Length; int obs = track.Obstacles.Count; track.Obstacles.Add(new Obstacle(Vector2.Zero, 1.5f));
            float m = 160f; bool passed = false, hit = false; float closest = 99f;
            for (int frame = 0; frame < 60 * 150; frame++)
            {
                m = (m + 2.5f / 60f) % n; int mi = (int)m; Vector2 d = track.CenterDir(mi); Vector2 pos = track.Center[mi] + new Vector2(-d.Y, d.X) * offset;
                track.Obstacles[obs] = new Obstacle(pos, 1.5f, d * 2.5f, Material: ImpactMaterial.Metal);
                patrol.Tick(1f / 60, null, 0);
                if (patrol.Guarding) continue;
                float gap = patrol.Position2D.DistanceTo(pos); closest = MathF.Min(closest, gap);
                if (gap < 1.5f + 1.2f - 0.3f) hit = true;
                if ((patrol.Position2D - pos).Dot(d) > 6f && gap < 40f && frame > 120) passed = true;
                if (passed && frame > 60 * 140) break;
            }
            GD.Print($"KOZLIK OVERTAKE {id} offset {offset}: passed={passed} hit={hit} closest {closest:F2} m");
            Check(passed && !hit, $"{id} (tractor {offset:F1} m off the centre): the UAZ passes the slow vehicle without touching it");
            RemoveChild(patrol); patrol.Free(); RemoveChild(fx); fx.Free();
        }
    }
}
