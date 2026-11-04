using System;
using System.Collections.Generic;
using System.Linq;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

// Phase 3 of the world tape (T4c), the parts that need no game: breakables and effects on the fake world.
//   tape 18b (spec test 18): a breakable breaks at time a; a seek before a, after a and a wrap give the sink the broken set of that instant, and nothing stays broken across a wrap.
//   tape 19: a lap that starts with effects alive from the previous lap has them in its first keyframe; a seek into the middle of such an effect restores its age;
//     the event log allocates nothing per event once its blocks exist (spawn-time allocation is reported).
// The game side (the sink that makes real particles, nodes and fallen trees) is judged by --tape-scene-tests, scene 12 (PhysicsTest.Tape.Scene5.cs).

/// <summary>A wrong recorder: a keyframe forgets the effects that began before the lap did (so a lap that starts with smoke, fires and warning lamps going loses them).</summary>
class MutKeyframeDropsEffectRecorder : RefRecorder
{
    protected override bool EffectKept(in TapeEvent e, float t) => e.Time >= 0f;
}

public partial class PhysicsTest
{
    static (float, Action<IWorldTapeRecorder>) TapeBreak(float t, int id, float at = 0f)
        => (at, r => r.Event(new TapeEvent(t, TapeEventType.Effect, Effect: TapeEffect.BreakableBroken, Pos: new Vector3(id, 0, 2 * id), Dir: new Vector3(1, 0, 0), Param: id)));

    // ---- 18b: breakables ----
    void TapeT18b()
    {
        const float End = 30f; const float a = 12f;      // the keyframes are at 0, 5, 10, 15 ...: a falls between two of them
        WorldTape tape;
        using (var w = TapeWorld.Build(2, 3, false))
            tape = TapeRecord(w, End, new() { TapeBreak(0f, 3), TapeBreak(a, 7, a), TapeBreak(21f, 9, 21f), TapeBreak(15f, 8, 15f) });
        string Show(IEnumerable<int> s) => "{" + string.Join(",", s.OrderBy(x => x)) + "}";
        bool Is(TapeSink s, params int[] want) => s.Broken.OrderBy(x => x).SequenceEqual(want.OrderBy(x => x));

        // (a) a seek before a, between a and the next keyframe, after the next keyframe, at the end: the sink gets the broken set of that instant
        foreach (var (t, want) in new (float, int[])[] { (0f, new[] { 3 }), (8f, new[] { 3 }), (11.9f, new[] { 3 }), (12.1f, new[] { 3, 7 }), (14.9f, new[] { 3, 7 }), (16f, new[] { 3, 7, 8 }), (22f, new[] { 3, 7, 8, 9 }), (29.9f, new[] { 3, 7, 8, 9 }) })
        {
            var (w, s, p) = TapePlay(tape, 2, 3, false); using var _ = w;
            p.Seek(t);
            TapeCheck(Is(s, want) && s.BrokenSets == 1, $"tape 18b: a seek to {t:F1} s gives the sink the broken set {Show(want)} once (got {Show(s.Broken)} in {s.BrokenSets} calls)");
        }
        // (b) sequential playing and a seek agree at 40 random times (events fire as effects with a life of 0: the sink's set grows as it plays)
        { var rng = new Random(18); string bad = null;
          for (int k = 0; k < 40 && bad == null; k++)
          {
              float t = (float)(rng.NextDouble() * (End - 0.2));
              var (w1, s1, p1) = TapePlay(tape, 2, 3, false); for (int n = 0; n / 60f < t; n++) p1.Apply(n / 60f, 1 / 60f); p1.Apply(t, 1 / 60f);
              var (w2, s2, p2) = TapePlay(tape, 2, 3, false); p2.Seek(t);
              if (!s1.Broken.OrderBy(x => x).SequenceEqual(s2.Broken.OrderBy(x => x))) bad = $"t={t:F3}: playing {Show(s1.Broken)}, seek {Show(s2.Broken)}";
              w1.Dispose(); w2.Dispose();
          }
          TapeCheck(bad == null, $"tape 18b: the broken set after playing from the start equals the set after a seek (40 times){(bad == null ? "" : ": " + bad)}"); }
        // (c) the replay wraps ten times: in every pass the breakable a is not broken before a and is broken after it, and what the pass broke is standing again at the wrap
        { var (w, s, p) = TapePlay(tape, 2, 3, false); using var _ = w; int badWraps = 0, sets0 = 0; string seen = "";
          for (int loop = 0; loop < 10; loop++)
          {
              bool ok = true;
              for (int n = 0; n <= (int)(End * 60); n++)
              {
                  float t = n / 60f; p.Apply(t, 1 / 60f);
                  if (n == 6) ok &= Is(s, 3);                                  // 0.1 s: only what was broken before the lap
                  if (n == (int)(a * 60) - 6) ok &= Is(s, 3);                  // just before a
                  if (n == (int)(a * 60) + 6) ok &= Is(s, 3, 7);               // just after a
                  if (n == (int)(End * 60)) ok &= Is(s, 3, 7, 8, 9);
              }
              if (!ok) { badWraps++; seen = Show(s.Broken); }
          }
          sets0 = s.BrokenSets;
          TapeCheck(badWraps == 0, $"tape 18b: in each of 10 passes the breakable is not broken before a, is broken after it, and nothing stays broken across the wrap (bad passes {badWraps}{(badWraps == 0 ? "" : ", last " + seen)})");
          TapeCheck(sets0 == 10, $"tape 18b: every wrap gives the sink the broken set of the restored keyframe ({sets0} calls in 10 passes)"); }
        // (d) a release stands everything up (the real player says so to the sink before the replay is over)
        { var (w, s, p) = TapePlay(tape, 2, 3, false); using var _ = w; p.Seek(25f);
          if (p is WorldTapePlayer wp) { wp.Release(); TapeCheck(s.Broken.Count == 0, $"tape 18b: leaving the replay stands the breakables up (broken {Show(s.Broken)})"); }
          else Skip("tape 18b: release (the reference player has none)"); }
    }

    // ---- 19: effects alive at the lap's start, mid-life seeks, the event log ----
    void TapeT19()
    {
        string A0 = TapeWorld.Key(0);
        (float, Action<IWorldTapeRecorder>) Fx(float time, float life, TapeEffect fx = TapeEffect.Smoke, string kind = "k", float at = 0f)
            => (at, r => r.Event(new TapeEvent(time, TapeEventType.Effect, Source: A0, Kind: kind, Effect: fx, Pos: new Vector3(time, 1, life), Life: life)));
        // The previous lap's smoke (2 s old, 5 s long), a flash (0.5 s old, 1 s long) and a spark that is over (1 s ago, instant) are going when this lap starts; a new smoke begins at 8 s.
        WorldTape tape;
        using (var w = TapeWorld.Build(2, 3, false))
            tape = TapeRecord(w, 20f, new() { Fx(-2f, 5f, kind: "smoke"), Fx(-0.5f, 1f, TapeEffect.MuzzleBlast, "flash"), Fx(-1f, 0f, TapeEffect.CannonBurst, "spark"), Fx(8f, 6f, kind: "late", at: 8f) });
        (List<(TapeEvent E, float Age)> D, int Live) At(float t)
        {
            var (w, s, p) = TapePlay(tape, 2, 3, false); using var _ = w; p.Seek(t); return (s.Delivered.ToList(), s.Live.Count);
        }
        string Ages(List<(TapeEvent E, float Age)> d) => string.Join(", ", d.Select(x => $"{x.E.Kind}@{x.Age:F2}"));
        var (d0, l0) = At(0f);
        TapeCheck(d0.Count == 2 && l0 == 2 && d0.Any(x => x.E.Kind == "smoke" && MathF.Abs(x.Age - 2f) < 1e-3f) && d0.Any(x => x.E.Kind == "flash" && MathF.Abs(x.Age - 0.5f) < 1e-3f),
            $"tape 19: a lap that starts with effects alive has them in its first keyframe, with the age they already had (a seek to 0 gives [{Ages(d0)}]; the spent spark is not there)");
        var (d1, l1) = At(1f);
        TapeCheck(d1.Count == 1 && d1[0].E.Kind == "smoke" && MathF.Abs(d1[0].Age - 3f) < 1e-3f && l1 == 1, $"tape 19: a seek to 1 s restores the smoke aged 3 s and not the flash that ended at 0.5 s (got [{Ages(d1)}])");
        var (d2, _) = At(2.9f);
        TapeCheck(d2.Count == 1 && MathF.Abs(d2[0].Age - 4.9f) < 1e-3f, $"tape 19: a seek into the last moments of a carried-over effect restores its age (got [{Ages(d2)}])");
        var (d3, l3) = At(3.1f);
        TapeCheck(d3.Count == 0 && l3 == 0, $"tape 19: a seek just after its end restores nothing (got [{Ages(d3)}])");
        var (d4, _) = At(10f);
        TapeCheck(d4.Count == 1 && d4[0].E.Kind == "late" && MathF.Abs(d4[0].Age - 2f) < 1e-3f, $"tape 19: a seek into an effect begun during the lap restores it aged 2 s (got [{Ages(d4)}])");
        // playing from the start: the same effects arrive once, with their age, and the sink's live set agrees with a seek at every second
        { var (w1, s1, p1) = TapePlay(tape, 2, 3, false); using var _1 = w1; string bad = null; int checks = 0;
          for (int n = 0; n <= 20 * 60 && bad == null; n++)
          {
              float t = n / 60f; s1.Tick(1 / 60f); p1.Apply(t, 1 / 60f);
              if (n % 60 != 30) continue;
              var (w2, s2, p2) = TapePlay(tape, 2, 3, false); p2.Seek(t); checks++;
              var live1 = s1.Live.Select(f => (f.E.Kind, MathF.Round(f.Age, 1))).OrderBy(x => x.Kind).ToList(); var live2 = s2.Live.Select(f => (f.E.Kind, MathF.Round(f.Age, 1))).OrderBy(x => x.Kind).ToList();
              if (!live1.SequenceEqual(live2)) bad = $"t={t:F2}: playing [{string.Join(",", live1)}] vs seek [{string.Join(",", live2)}]";
              w2.Dispose();
          }
          TapeCheck(bad == null && s1.Delivered.Count(x => x.E.Kind == "smoke") == 1, $"tape 19: the carried-over smoke arrives once when played from the start and the live effects agree with a seek ({checks} times){(bad == null ? "" : ": " + bad)}"); }

        // the event log: after its blocks exist, an event costs no allocation at all (the first lap that logs 3000 events pays for the blocks, once)
        var rec = TapeSeams.NewRecorder();
        if (rec is WorldTapeRecorder wr)
        {
            using var w = TapeWorld.Build(2, 3, false); var ev = new TapeEvent(1f, TapeEventType.Effect, Source: A0, Kind: "k", Effect: TapeEffect.Smoke, Pos: new Vector3(1, 2, 3), Life: 2f);
            long[] bytes = new long[2];
            for (int lap = 0; lap < 2; lap++)
            {
                w.Tick(0f); wr.Begin(w.Index);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 3000; i++) wr.Event(ev);
                bytes[lap] = GC.GetAllocatedBytesForCurrentThread() - a0;
                long r0 = GC.GetAllocatedBytesForCurrentThread();
                for (int n = 0; n <= 3600; n++) { w.Tick(n / 60f); wr.Record(n / 60f); }
                TapeInfo($"tape 19: lap {lap + 1}: 3000 events cost {bytes[lap]} bytes; recording 60 s with the log holding them cost {GC.GetAllocatedBytesForCurrentThread() - r0} bytes");
            }
            TapeCheck(bytes[1] <= 1024, $"tape 19: the second lap logs 3000 events without allocating (first lap {bytes[0]} bytes = {bytes[0] / 3000} per event for the blocks, second {bytes[1]})");
        }
        else Skip("tape 19: the event log allocation (the reference recorder is not the real one)");
    }
}
