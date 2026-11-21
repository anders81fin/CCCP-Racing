using System;
using System.Collections.Generic;
using System.Linq;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>The prisoner column's legs have knees: marching, the knee folds on the forward swing and the foot lifts; a man knocked down and jogging back to his place folds it further (a higher knee, a longer step), the other leg's knee follows half a step later,
    /// the stoop and the column's own rhythm (each man's own phase) stay, and a man lying down has straight legs.</summary>
    void PrisonerKneeTest()
    {
        var t = TrackBuilder.Build(TrackDef.Load("res://tracks/siberia.json"));
        var column = new PrisonerColumn { Name = "PrisonerColumn" }; AddChild(column);
        column.Init(t, 900f, 6);
        const float dt = 1f / 60f;
        int man = 3;
        float marchKneeL = 0, marchKneeR = 0, marchHip = 0, bodyStoop = 0; var phaseOffsets = new HashSet<int>();
        for (int i = 0; i < 60 * 8; i++)
        {
            column.Tick(dt, null);
            marchKneeL = MathF.Max(marchKneeL, -column.WalkerKnee(man, true).Rotation.X); marchKneeR = MathF.Max(marchKneeR, -column.WalkerKnee(man, false).Rotation.X);
            marchHip = MathF.Max(marchHip, MathF.Abs(column.WalkerHip(man, true).Rotation.X));
            bodyStoop = column.WalkerBody(man).Rotation.X;
        }
        // Knocked over, lying (straight legs), up again and jogging back.
        column.HitWalker(man);
        float lyingKnee = 0, jogKnee = 0, jogHip = 0; bool jogged = false, wasDown = false;
        for (int i = 0; i < 60 * 12; i++)
        {
            column.Tick(dt, null);
            if (column.WalkerDown(man)) { wasDown = true; lyingKnee = MathF.Max(lyingKnee, -column.WalkerKnee(man, true).Rotation.X); }
            else if (column.WalkerStraggling(man) && column.WalkerGait(man) > 0.99f) { jogged = true; jogKnee = MathF.Max(jogKnee, -column.WalkerKnee(man, true).Rotation.X); jogHip = MathF.Max(jogHip, MathF.Abs(column.WalkerHip(man, true).Rotation.X)); }
        }
        bool oneLegAtATime = true;
        for (int i = 0; i < 60 * 3; i++)
        {
            column.Tick(dt, null);
            float l = -column.WalkerKnee(2, true).Rotation.X, r = -column.WalkerKnee(2, false).Rotation.X;
            if (l > 0.1f && r > 0.1f) oneLegAtATime = false;      // (the swinging leg's knee folds while the standing leg's stays straight)
        }
        // Each man keeps his own phase: the column does not step in unison.
        float p1 = column.WalkerHip(1, true).Rotation.X, p2 = column.WalkerHip(2, true).Rotation.X, p3 = column.WalkerHip(3, true).Rotation.X;
        Check(marchKneeL > 0.4f && marchKneeR > 0.4f && marchHip > 0.4f && MathF.Abs(bodyStoop - 0.18f) < 0.06f && !(MathF.Abs(p1 - p2) < 1e-3f && MathF.Abs(p2 - p3) < 1e-3f),
            $"prisoners march with knees: fold {marchKneeL:F2}/{marchKneeR:F2} rad, hip {marchHip:F2}, stoop {bodyStoop:F2} kept, each man on his own phase");
        Check(wasDown && lyingKnee < 1e-4f && jogged && jogKnee > marchKneeL + 0.25f && jogHip > marchHip && oneLegAtATime,
            $"prisoners jogging back: knee {jogKnee:F2} rad (the march {marchKneeL:F2}), hip {jogHip:F2}, straight legs while lying ({lyingKnee:F3}), one knee at a time ({oneLegAtATime})");
        column.QueueFree();
        PrisonerKneeTapeTest(t);
    }

    /// <summary>The knees are tape parts (PrisonerColumn tape_ver 2): a march with a man knocked down and jogging back, recorded and replayed after a file-format round trip and seeks, shows the same knees; a tape of the old version (no knees) does not cover the new actor and leaves it live.</summary>
    void PrisonerKneeTapeTest(TrackData t)
    {
        const string key = "Column:PrisonerColumn";
        PrisonerColumn Make() { var c = new PrisonerColumn { Name = "PrisonerColumn" }; AddChild(c); c.Init(t, 900f, 6); return c; }
        var source = Make();
        var rec = new WorldTapeRecorder(); rec.Begin(new WorldTapeIndex(new[] { (key, (Node3D)source) })); rec.Record(0);
        const float dt = 1f / 60f; var knees = new Dictionary<int, (float l, float r)>();
        for (int frame = 1; frame <= 60 * 14; frame++)
        {
            if (frame == 60 * 2) source.HitWalker(3);
            source.Tick(dt, null); rec.Record(frame * dt);
            if (frame % 4 == 0) knees[frame] = (-source.WalkerKnee(3, true).Rotation.X, -source.WalkerKnee(3, false).Rotation.X);
        }
        var loaded = WorldTapeCodec.Decode(WorldTapeCodec.Encode(rec.Finish(14, 1UL)));
        var replay = Make();
        var player = new WorldTapePlayer(loaded, new WorldTapeIndex(new[] { (key, (Node3D)replay) }), new TapeSink());
        bool covered = player.Covers(key);
        float worst = 0, folded = 0;
        foreach (int frame in new[] { 60, 200, 360, 500, 700, 360, 60, 800 })
        {
            player.Seek(frame / 60f);
            var want = knees[frame - frame % 4]; var gotL = -replay.WalkerKnee(3, true).Rotation.X; var gotR = -replay.WalkerKnee(3, false).Rotation.X;
            worst = MathF.Max(worst, MathF.Max(MathF.Abs(want.l - gotL), MathF.Abs(want.r - gotR)));
            folded = MathF.Max(folded, MathF.Max(gotL, gotR));
        }
        var old = Make(); old.SetMeta(WorldTapeConst.VersionMeta, 1);
        var stale = new WorldTapePlayer(loaded, new WorldTapeIndex(new[] { (key, (Node3D)old) }), new TapeSink());
        Check(covered && worst < 0.25f && folded > 0.3f && !stale.Covers(key),
            $"prisoner tape: the knees replay after a file round trip and seeks (worst {worst:F3} rad, folded up to {folded:F2}); an old-version actor is not covered and stays live ({!stale.Covers(key)})");
        source.QueueFree(); replay.QueueFree(); old.QueueFree();
    }
}
