using System;
using System.Collections.Generic;
using CCCPRacing.Camera;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void ReplayCameraTest()
    {
        // A clear straight permits all four shots and verifies that the fixed stand really stays fixed.
        var straight = new TrackData { N = 160, Tile = 2, Heights = new float[161 * 161],
            Center = new Vector2[700], HalfWidth = new float[700], CenterH = new float[700] };
        for (int i = 0; i < 700; i++) { straight.Center[i] = new Vector2(100, i - 150); straight.HalfWidth[i] = 6; }
        var director = new ReplayCameraDirector();
        var shots = new HashSet<ReplayCameraDirector.Shot>();
        var counts = new int[4];
        Vector3 stand = Vector3.Zero;
        bool fixedStand = true, framed = true;
        for (int i = 0; i < 40 * 60; i++)
        {
            float t = i / 60f;
            Vector3 car = new(100, .7f, t * 8);
            var view = director.Frame(straight, car, Vector3.Back * 15, new Basis(Vector3.Up, MathF.PI),
                car + Vector3.Back * 30, t, 1f / 60);
            shots.Add(view.Shot);
            counts[(int)view.Shot]++;
            if (view.Shot == ReplayCameraDirector.Shot.Trackside)
            { if (stand != Vector3.Zero) fixedStand &= view.Eye.DistanceTo(stand) < .001f; stand = view.Eye; }
            Vector3 subject = view.Shot == ReplayCameraDirector.Shot.Bonnet ? car + Vector3.Back * 10 : car;
            Vector3 point = Basis.LookingAt(view.Target - view.Eye, Vector3.Up).Inverse() * (subject - view.Eye);
            framed &= point.Z < 0 && Math.Abs(point.X / -point.Z) < MathF.Tan(Mathf.DegToRad(view.Fov / 2)) * (16f / 9);
        }
        Check(shots.Count == 4 && fixedStand && framed,
            $"replay: four deliberate shots ({string.Join(",", shots)}), fixed stand {fixedStand}, framed {framed}");
        Check(counts[0] > counts[3] && counts[3] > counts[2] && counts[2] > counts[1],
            $"replay emphasis: top-down {counts[0] / 60f:F0}s, bonnet {counts[3] / 60f:F0}s, trackside {counts[2] / 60f:F0}s, follow {counts[1] / 60f:F0}s");
        straight.Obstacles.Add(new Obstacle(new(103, 60), 1.5f, new(0, -6)));
        Check(ReplayCameraDirector.ClosePass(straight, new(100, .7f, 35), Vector3.Back * 15),
            "replay: predicts close oncoming plough pass before the vehicles meet");
        var encounter = new ReplayCameraDirector();
        encounter.Frame(straight, new(100, .7f, 0), Vector3.Back * 15, new Basis(Vector3.Up, MathF.PI),
            new(100, .7f, 30), 0, 1f / 60);
        var highlight = encounter.Frame(straight, new(100, .7f, 35), Vector3.Back * 15, new Basis(Vector3.Up, MathF.PI),
            new(100, .7f, 65), 4, 1f / 60);
        Check(highlight.Shot == ReplayCameraDirector.Shot.Bonnet,
            "replay: close pass selects bonnet during an otherwise top-down segment");
        straight.Obstacles[0] = new Obstacle(new(115, 60), 1.5f, new(0, -6));
        Check(!ReplayCameraDirector.ClosePass(straight, new(100, .7f, 35), Vector3.Back * 15),
            "replay: distant traffic does not trigger a bonnet highlight");
        straight.Obstacles[0] = new Obstacle(new(103, 60), 1.5f, Vector2.Zero);
        Check(!ReplayCameraDirector.ClosePass(straight, new(100, .7f, 35), Vector3.Back * 15),
            "replay: static scenery does not count as a traffic encounter");
        Check(!ReplayCameraDirector.TerrainHighlight(straight, new(100, .7f, 35)),
            "replay: flat road does not request a terrain highlight");
        straight.Obstacles.Clear();
        var rewind = director.Frame(straight, new(100, .7f, 0), Vector3.Back * 15,
            new Basis(Vector3.Up, MathF.PI), new(100, .7f, 30), 0, 1f / 60);
        Check(rewind.Shot == ReplayCameraDirector.Shot.Overview, "replay: lap rewind resets to overview");
        foreach (string id in new[] { "ilyich", "siberia", "atomgrad", "bonus" })
        {
            var track = TrackBuilder.Build(TrackDef.Load($"res://tracks/{id}.json"));
            if (id == "atomgrad")
            {
                bool hills = false;
                for (int i = 115; i < 320; i += 5) hills |= ReplayCameraDirector.TerrainHighlight(track, track.CenterPos(i));
                Check(hills && !ReplayCameraDirector.TerrainHighlight(track, new(80, 1.8f, 127)),
                    "replay: Atomgrad forest hills request a highlight while level town streets do not");
            }
            director.Reset(); shots.Clear();
            bool valid = true, clear = true;
            for (int i = 0; i < 62 * 30; i++)
            {
                float t = i / 30f;
                int at = (int)(t * 18) % track.Center.Length, next = (at + 36) % track.Center.Length;
                var car = track.CenterPos(at) + Vector3.Up * .7f;
                var d = track.CenterDir(at);
                var body = new Basis(Vector3.Up, TrackData.YawFor(d));
                var view = director.Frame(track, car, new Vector3(d.X, 0, d.Y) * 18, body,
                    track.CenterPos(next) + Vector3.Up * .7f, t, 1f / 30);
                shots.Add(view.Shot);
                valid &= view.Eye.IsFinite() && view.Target.IsFinite() && view.Eye.DistanceTo(view.Target) > .1f;
                clear &= view.Eye.Y > WorldBackdrop.HeightAt(track, new(view.Eye.X, view.Eye.Z)) + .5f;
                if (view.Shot == ReplayCameraDirector.Shot.Trackside)
                    clear &= ReplayCameraDirector.ClearView(track, view.Eye, car);
            }
            Check(valid && clear && shots.Count >= 2, $"replay {id}: varied finite views clear terrain and blocked trackside positions");
        }
    }
}
