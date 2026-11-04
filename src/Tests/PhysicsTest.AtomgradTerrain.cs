using System;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void AtomgradTerrainTest()
    {
        var def = TrackDef.Load("res://tracks/atomgrad.json");
        var track = TrackBuilder.Build(def);
        float cityLow = float.PositiveInfinity, cityHigh = float.NegativeInfinity;
        foreach (var p in new[] { new Vector2(55, 127), new Vector2(112, 150), new Vector2(80, 180) })
        {
            float h = track.MeshHeight(p.X, p.Y);
            cityLow = Math.Min(cityLow, h); cityHigh = Math.Max(cityHigh, h);
        }
        Check(cityLow > 1.6f && cityHigh - cityLow < .15f, $"town streets share a raised terrace: {cityLow:F2}-{cityHigh:F2} m");
        float forest = track.MeshHeight(241, 253), ridge = track.MeshHeight(288, 180);
        Check(ridge - forest > 3.5f, $"forest hollow rises to graveyard ridge: {ridge - forest:F2} m");
        float forestLow = float.PositiveInfinity, forestHigh = float.NegativeInfinity, forestGrade = 0;
        for (int i = 115; i <= 320; i++)
        {
            forestLow = Math.Min(forestLow, track.CenterH[i]);
            forestHigh = Math.Max(forestHigh, track.CenterH[i]);
            forestGrade = Math.Max(forestGrade, Math.Abs(track.CenterH[i + 1] - track.CenterH[i]));
        }
        Check(forestHigh - forestLow > 6 && forestGrade > .12f,
            $"forest road has substantial rollers: {forestHigh - forestLow:F2} m range, {forestGrade * 100:F1}% grade");
        float grade = 0, error = 0;
        for (int i = 0; i < track.Center.Length; i++)
        {
            grade = Math.Max(grade, Math.Abs(track.CenterH[(i + 1) % track.Center.Length] - track.CenterH[i]));
            var p = track.Center[i];
            error = Math.Max(error, Math.Abs(track.CenterH[i] - track.MeshHeight(p.X, p.Y)));
        }
        Check(grade < .25f && error < .2f, $"road slopes and collision mesh are smooth: {grade * 100:F1}% grade, {error:F3} m error");
        foreach (var p in new[] { new Vector2(24, 100), new Vector2(25, 205) })
        {
            float low = float.PositiveInfinity, high = float.NegativeInfinity;
            for (float z = p.Y - 24; z <= p.Y + 24; z += 2)
            foreach (float x in new[] { p.X - 5, p.X, p.X + 5 })
            { float h = track.MeshHeight(x, z); low = Math.Min(low, h); high = Math.Max(high, h); }
            Check(high - low < .12f, $"western apartment foundation at {p} stays level: {high - low:F3} m");
        }
        var markings = Atomgrad.RoadMarkings(track);
        var paint = markings.GetChild<MeshInstance3D>(0).Mesh;
        var arrays = paint.SurfaceGetArrays(0);
        var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        bool follows = true, concrete = true;
        foreach (var p in vertices)
        {
            follows &= Math.Abs(p.Y - track.MeshHeight(p.X, p.Z) - .04f) < .0001f;
            concrete &= track.TileSurface((int)(p.X / track.Tile), (int)(p.Z / track.Tile)) == SurfaceType.Concrete;
        }
        Check(follows && concrete && vertices.Length > 500 && vertices.Length < 30000,
            $"worn markings follow concrete only, clear puddles and stay bounded: {vertices.Length} vertices");
        foreach (var site in new[] { new Vector2(44, 104), new Vector2(80, 127), new Vector2(82, 180) })
        {
            int near = 0;
            foreach (var p in vertices) if (site.DistanceTo(new Vector2(p.X, p.Z)) < 5) near++;
            Check(near > 60, $"crosswalk present near {site}: {near} paint vertices");
        }
        markings.Free();
        foreach (float aggression in new[] { 1f, 1.1f })
        {
            var car = new CarSim(CarParams.ForTrack(def), track) { Obstacles = track.Obstacles, WorldMax = track.WorldSize - 4 };
            car.Reset(track.StartPos, track.StartYaw);
            var driver = new AutoPilot(track) { Aggression = aggression };
            var timer = new LapTimer(track); timer.Start();
            float elapsed = 0, minUp = 1, air = 0;
            for (; elapsed < 180 && timer.LapsDone == 0; elapsed += Dt)
            {
                driver.Drive(car, Dt); car.Step(Dt);
                timer.Update(Dt, new Vector2(car.Pos.X, car.Pos.Z));
                minUp = Math.Min(minUp, car.Up.Y); air = Math.Max(air, car.AirTime);
            }
            Check(timer.LapsDone > 0 && minUp > .7f, $"Atomgrad pace {aggression:F1}: lap {elapsed:F1}s, upright {minUp:F2}, air {air:F2}s");
        }
    }
}
