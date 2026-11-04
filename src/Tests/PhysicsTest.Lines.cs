using System;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>
    /// The rescue puts the car back on the stretch it was on, not on a nearby leg of the road (Path of
    /// Ilyich: 580 m and 820 m are 34 m apart); a lap ends only through the line between its posts.
    /// </summary>
    void LinesTest()
    {
        GD.Print("The road followed (rescue) and the finish line between its posts");
        var t = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        int n = t.Center.Length;
        var timer = new LapTimer(t);
        timer.Start();
        for (int i = 0; i <= 580; i += 2) timer.Update(0.1f, t.Center[i]);
        // Off the road at 580 towards the 820 m leg, closer to it than to its own.
        Vector2 a = t.Center[580], b = t.Center[820], p = a.Lerp(b, 0.62f);
        int global = t.NearestCenter(p, out _), followed = timer.Follow(p, out _);
        GD.Print($"  car between the legs at ({p.X:F0},{p.Y:F0}): nearest road metre {global}, followed {followed}");
        Check(Math.Abs(followed - 580) < 30 && Math.Abs(global - 820) < 40, "the rescue follows the car's own stretch (580 m), not the nearer leg (820 m)");
        // Cut across country from 580 m to the church leg and press R there: back where it left the road.
        Vector2 dir580 = t.CenterDir(580);
        for (int k = 1; k <= 20; k++) timer.Update(0.1f, a.Lerp(b, k / 20f) + new Vector2(-dir580.Y, dir580.X) * 0.5f);
        int rescue = timer.RescueMetre(b);
        GD.Print($"  cut across to the church leg (820 m): the rescue puts it at {rescue}");
        Check(Math.Abs(rescue - 580) < 40, "a cut across country to another stretch is not progress: the rescue goes back to where the car left its road");

        // Round the lap, then over the line: once wide of the posts, once between them.
        timer.Start();
        for (int i = 0; i < n - 4; i += 2) timer.Update(0.1f, t.Center[i]);
        int laps = timer.LapsDone;
        Vector2 dir = t.CenterDir(0), side = new(-dir.Y, dir.X), c0 = t.Center[0];
        Vector2 wide = c0 + side * (t.LineHalf + 3f);
        timer.Update(0.1f, wide - dir * 2f);
        timer.Update(0.1f, wide + dir * 2f);
        timer.Update(0.1f, wide + dir * 6f);
        bool outside = timer.LapsDone == laps;
        timer.Update(0.1f, c0 - dir * 2f);
        timer.Update(0.1f, c0 + dir * 2f);
        timer.Update(0.1f, c0 + dir * 4f);
        GD.Print($"  wide of the posts (half {t.LineHalf:F1} m): {(outside ? "no lap" : "a lap")}; between them: {timer.LapsDone - laps} lap");
        Check(outside && timer.LapsDone == laps + 1, "a lap ends only through the line between its posts");
    }
}
