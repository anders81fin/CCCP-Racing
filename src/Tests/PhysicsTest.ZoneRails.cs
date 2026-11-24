using System;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>Atomgrad: the dosimetrist's walk round the stopped car is not put back to his post by his own frame update, and the railway is solid under the tyres (a ridge along each rail, none between or beside them).</summary>
    void ZoneRailsAndWalkTest()
    {
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/atomgrad.json"));
        var man = ZoneDosimetrist.Build(track);
        AddChild(man);
        var figure = man.GetChild<Node3D>(0);
        Vector2 home = man.Home, away = home + new Vector2(6f, -4f);
        man.PoseServiceWalk(away, new Vector2(1, 0), 1f, measuring: true);
        for (int i = 0; i < 5; i++) man._Process(1f / 60f);
        Check(new Vector2(figure.GlobalPosition.X, figure.GlobalPosition.Z).DistanceTo(away) < 0.01f, $"dosimetrist: the figure stays where the service walk put him over the frame updates ({figure.GlobalPosition.X:F2},{figure.GlobalPosition.Z:F2} vs {away.X:F2},{away.Y:F2})");
        man.EndServiceWalk();
        man._Process(1f / 60f);
        Check(new Vector2(figure.GlobalPosition.X, figure.GlobalPosition.Z).DistanceTo(home) < 0.05f, "dosimetrist: back at his post when the walk ends");

        var rails = track.Rails;
        Check(rails.Count == 1, $"atomgrad: one railway is registered as solid ({rails.Count})");
        if (rails.Count != 1) return;
        var r = rails[0];
        // Away from the road crossing: along the line a few metres off its middle, across the rails.
        Vector2 P(float across, float along) => r.Origin + r.Across * across + r.Along * along;
        float onRail = 0, between = 0, beside = 0;
        foreach (float along in new[] { -40f, 25f, 40f })
        {
            var a = P(0.76f, along); var b = P(0f, along); var c = P(2.5f, along);
            float h = track.Sample(a.X, a.Y).Height - track.MeshHeight(a.X, a.Y), hb = track.Sample(b.X, b.Y).Height - track.MeshHeight(b.X, b.Y), hc = track.Sample(c.X, c.Y).Height - track.MeshHeight(c.X, c.Y);
            onRail = Math.Max(onRail, h); between = Math.Max(between, Math.Abs(hb)); beside = Math.Max(beside, Math.Abs(hc));
        }
        Check(track.OnRailway(P(0f, 5f), 2.4f) && track.OnRailway(P(2.3f, 5f), 2.4f) && !track.OnRailway(P(3.5f, 5f), 2.4f) && !track.OnRailway(P(0f, r.Half + 1f), 2.4f), "atomgrad rails: the wet concrete's railway test covers the rails and their ballast and nothing beside or past them");
        var past = P(0.76f, r.Half + 3f);
        float outside = track.Sample(past.X, past.Y).Height - track.MeshHeight(past.X, past.Y);
        var edge = P(0.76f + 0.1f, 40f);
        var slope = track.Sample(edge.X, edge.Y).Slope;
        Check(onRail > 0.08f && between < 0.05f && beside < 0.05f && Math.Abs(outside) < 0.05f && slope.Length() > 0.1f,
            $"atomgrad rails: a ridge of {onRail:F3} m on the rail, {between:F3} between, {beside:F3} beside, {outside:F3} past the line's end, slope {slope.Length():F2} on its flank");
    }
}
