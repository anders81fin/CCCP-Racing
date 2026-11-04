using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

// Phase 2 of the world tape (T4a): the dynamic vehicles (the Hinds of the Bonus exercise and the battery's missiles) made and freed during a lap.
//   tape 18 (--tape-tests): the instance machinery on the fake world: a lap that starts with live instances, seek equals sequential apply, a seek into an
//     instance's life, ten wraps without a duplicate or a leak, the release, the allocation per sample with instances alive, an unknown kind.
//   scene 11 (--tape-scene-tests): the real game on the Bonus track, one headless child at a time: two laps (the second a record that starts with the first
//     one's Hinds alive), the title replay in the same scene and in a reloaded one, ten wraps, a trip into a race and a preview, corrupt and foreign tapes,
//     and the recording-on versus recording-off Hind sorties.

/// <summary>A wrong player: a seek leaves the instances it made before where they are.</summary>
class MutKeepInstPlayer : RefPlayer
{
    public MutKeepInstPlayer(WorldTape t, IWorldTapeIndex i, IWorldTapeSink s) : base(t, i, s) { }
    protected override void DespawnAllOnSeek() { }
}

/// <summary>A wrong recorder: a keyframe forgets the instance that was spawned at its own time (so a lap that starts with live instances loses them).</summary>
class MutKeyframeDropsSpawnRecorder : RefRecorder
{
    protected override bool SpawnKept(in TapeEvent e, float t) => e.Time < t;
}

/// <summary>Counts what the player asks of the sink, per instance.</summary>
sealed class TapeCountSink : IWorldTapeSink
{
    public readonly TapeSink Inner;
    public readonly Dictionary<int, int> Spawns = new(), Despawns = new();
    public int Duplicates;
    public TapeCountSink(TapeSink inner) { Inner = inner; }
    public Node3D Spawn(string kind, string parentActorKey, int instance)
    {
        if (Inner.Inst.ContainsKey(instance)) Duplicates++;
        Spawns[instance] = Spawns.GetValueOrDefault(instance) + 1;
        return Inner.Spawn(kind, parentActorKey, instance);
    }
    public void Despawn(int instance)
    {
        if (Inner.Inst.ContainsKey(instance)) Despawns[instance] = Despawns.GetValueOrDefault(instance) + 1;
        Inner.Despawn(instance);
    }
    public void Effect(in TapeEvent e, float age) => Inner.Effect(e, age);
    public void ClearEffects() => Inner.ClearEffects();
    public void SetBrokenBreakables(IReadOnlyCollection<int> ids) => Inner.SetBrokenBreakables(ids);
}

/// <summary>A game side that cannot build any kind (an old build replaying a newer tape).</summary>
sealed class TapeNoKindSink : IWorldTapeSink
{
    public int Asked, Removed;
    public Node3D Spawn(string kind, string parentActorKey, int instance) { Asked++; return null; }
    public void Despawn(int instance) => Removed++;
    public void Effect(in TapeEvent e, float age) { }
    public void ClearEffects() { }
    public void SetBrokenBreakables(IReadOnlyCollection<int> ids) { }
}

public partial class PhysicsTest
{
    static int TapeKindNodes(TapeWorld w)
    {
        int n = 0;
        void Walk(Node x) { if (x.HasMeta(WorldTapeConst.KindMeta)) n++; foreach (var c in x.GetChildren()) Walk(c); }
        foreach (var (_, root) in w.Actors) Walk(root);
        return n;
    }

    // ---- 18: dynamic instances (the Hinds, the missiles) in the tape ----
    void TapeT18()
    {
        const int Actors = 3, Parts = 3; const float End = 24f;
        string A0 = TapeWorld.Key(0), A1 = TapeWorld.Key(1), A2 = TapeWorld.Key(2);
        var life = new Dictionary<int, (float Spawn, float T0, float? Gone)>();      // the tape time of each instance's Spawn and Despawn event, and the t0 of its motion
        WorldTape tape;
        using (var w = TapeWorld.Build(Actors, Parts))
        {
            w.SpawnInstance(A1, 1, "hind", -2f); w.SpawnInstance(A2, 2, "missile", -0.5f);      // alive before the lap starts: the previous lap's
            var rec = TapeSeams.NewRecorder();
            w.Tick(0f); rec.Begin(w.Index);
            int next = 0;      // the samples the recorder has taken: the first one taken from now on is where a spawn or a despawn begins to show
            void Born(int id, string actor, string kind, float t, bool atStart = false)
            {
                if (!atStart) w.SpawnInstance(actor, id, kind, t);
                float et = atStart ? 0f : next / WorldTapeConst.TapeRate;
                rec.Event(new TapeEvent(et, TapeEventType.Spawn, Source: actor, Kind: kind, Parent: actor, Instance: id));
                life[id] = (et, atStart ? (id == 1 ? -2f : -0.5f) : t, null);
            }
            void Gone(int id, string actor)
            {
                w.DespawnInstance(id);
                float et = next / WorldTapeConst.TapeRate;
                rec.Event(new TapeEvent(et, TapeEventType.Despawn, Source: actor, Instance: id));
                life[id] = (life[id].Spawn, life[id].T0, et);
            }
            Born(1, A1, "hind", 0f, true); Born(2, A2, "missile", 0f, true);
            var script = new (float T, Action Do)[]
            {
                (4.2f, () => Gone(2, A2)), (6.1f, () => Born(3, A0, "missile", 6.1f)), (13.3f, () => Gone(1, A1)),
                (15.5f, () => Born(4, A2, "hind", 15.5f)), (20f, () => Gone(3, A0)), (23.9f, () => Born(5, A1, "missile", 23.9f)),
            };
            int s = 0, ticks = (int)MathF.Round(End * 60f);
            for (int n = 0; n <= ticks; n++)
            {
                float t = n / 60f;
                while (s < script.Length && script[s].T <= t) script[s++].Do();
                w.Tick(t); rec.Record(t);
                while (next / WorldTapeConst.TapeRate <= t + 1e-3f) next++;
            }
            tape = rec.Finish(End, 1UL);
        }
        bool real = TapeSeams.NewPlayer(tape, TapeSeams.NewIndex(new List<(string, Node3D)>()), new TapeSink()) is WorldTapePlayer;

        HashSet<int> LiveAt(float t) => life.Where(kv => kv.Value.Spawn <= t && !(kv.Value.Gone is float g && g <= t)).Select(kv => kv.Key).ToHashSet();
        float err = 0;
        bool PosOk(TapeSink s, float t) { foreach (var id in LiveAt(t)) { if (!s.Inst.TryGetValue(id, out var n)) return false; err = MathF.Max(err, n.Position.DistanceTo(TapeWorld.InstancePos(id, life[id].T0, t))); } return err < 0.005f; }

        // (a) a lap that starts with live instances: the first keyframe holds them, spawned at 0
        { var (w, s, p) = TapePlay(tape, Actors, Parts); p.Seek(0f);
          TapeCheck(s.Inst.Keys.OrderBy(k => k).SequenceEqual(new[] { 1, 2 }) && PosOk(s, 0f), $"tape 18: a lap that starts with live instances has them in its first keyframe: a seek to 0 restores {s.Inst.Count} of 2 at their poses (error {err * 1000:F2} mm)");
          if (p is WorldTapePlayer wp) TapeCheck(wp.LiveInstances.All(kv => kv.Value == 0f), "tape 18: the instances alive at the start were spawned at 0");
          w.Dispose(); }
        // (b) seek equals sequential apply with instances (40 times, off the sample grid too)
        { var rng = new Random(18); string bad = null; int checks = 0;
          for (int k = 0; k < 40 && bad == null; k++)
          {
              float t = k < 6 ? new[] { 0f, 4.2f, 6.1f, 13.3f, 15.5f, 23.9f }[k] : (float)(rng.NextDouble() * (End - 0.2));
              var (w1, s1, p1) = TapePlay(tape, Actors, Parts); for (int n = 0; n / 60f < t; n++) p1.Apply(n / 60f, 1 / 60f); p1.Apply(t, 1 / 60f);
              var (w2, s2, p2) = TapePlay(tape, Actors, Parts); p2.Seek(t);
              var d = TapeDiff(TapeTake(w1, s1), TapeTake(w2, s2)); checks++;
              if (d != null) bad = $"t={t:F3}: {d}";
              w1.Dispose(); w2.Dispose();
          }
          TapeCheck(bad == null, $"tape 18: seek equals sequential apply with instances alive ({checks} times){(bad == null ? "" : ": " + bad)}"); }
        // (c) a seek into the middle of an instance's life restores its pose and its age
        { err = 0; bool setOk = true, ageOk = true; var at = new[] { 75, 141, 210, 315, 352 }.Select(k => k / WorldTapeConst.TapeRate).ToArray();
          foreach (var t in at)
          {
              var (w, s, p) = TapePlay(tape, Actors, Parts); p.Seek(t);
              setOk &= s.Inst.Keys.ToHashSet().SetEquals(LiveAt(t)) && PosOk(s, t);
              if (p is WorldTapePlayer wp) foreach (var kv in wp.LiveInstances) ageOk &= MathF.Abs(kv.Value - life[kv.Key].Spawn) < 1e-4f && t - kv.Value >= 0f;
              w.Dispose();
          }
          TapeCheck(setOk, $"tape 18: a seek into the middle of an instance's life restores the live set and its pose (worst error {err * 1000:F2} mm at {at.Length} times)");
          TapeCheck(ageOk, "tape 18: a seek into the middle of an instance's life restores its age (the spawn time of the tape)"); }
        // (d) ten wraps: every instance is spawned once per loop, removed once, no duplicate, no node left
        { var (w, inner, _) = TapePlay(tape, Actors, Parts); var cs = new TapeCountSink(inner); var p = TapeSeams.NewPlayer(tape, w.Index, cs);
          const int Loops = 10; var endLive = LiveAt(End); bool nodesOk = true; string seen = "";
          for (int loop = 0; loop < Loops; loop++)
          {
              p.Apply(0f, 1 / 60f);
              for (int n = 1; n <= (int)(End * 60); n++) p.Apply(n / 60f, 1 / 60f);
              nodesOk &= TapeKindNodes(w) == inner.Inst.Count && inner.Inst.Keys.ToHashSet().SetEquals(endLive);
          }
          bool once = life.Keys.All(id => cs.Spawns.GetValueOrDefault(id) == Loops && cs.Despawns.GetValueOrDefault(id) == Loops - (endLive.Contains(id) ? 1 : 0));
          seen = string.Join(" ", life.Keys.OrderBy(k => k).Select(id => $"#{id}:{cs.Spawns.GetValueOrDefault(id)}/{cs.Despawns.GetValueOrDefault(id)}"));
          TapeCheck(cs.Duplicates == 0 && once, $"tape 18: a wrap despawns each instance exactly once and spawns it once per loop: no duplicates after {Loops} loops (spawns/despawns {seen}, duplicates {cs.Duplicates})");
          TapeCheck(nodesOk, $"tape 18: after every loop the nodes of the kind in the world are the sink's live instances ({TapeKindNodes(w)} of {inner.Inst.Count})");
          if (p is WorldTapePlayer wp)
          {
              wp.Release();
              TapeCheck(inner.Inst.Count == 0 && TapeKindNodes(w) == 0 && w.Index.Count == w.Parts.Sum(l => l.Count), $"tape 18: leaving the replay (Release) despawns every instance the player made and takes their parts out of the index ({w.Index.Count} parts left)");
              p.Apply(5f, 1 / 60f);
              TapeCheck(inner.Inst.Count == LiveAt(5f).Count, "tape 18: after a release the next replay starts from a seek");
          }
          w.Dispose(); }
        // (e) a game side that cannot build the kind: nothing breaks, nothing is added
        if (real)
        {
            var w = TapeWorld.Build(Actors, Parts); w.Index = TapeSeams.NewIndex(w.Actors); var ns = new TapeNoKindSink(); var p = TapeSeams.NewPlayer(tape, w.Index, ns); int statics = w.Index.Count; string thrown = null;
            try { p.Seek(8f); for (int n = 480; n <= (int)(End * 60); n++) p.Apply(n / 60f, 1 / 60f); p.Seek(0f); } catch (Exception ex) { thrown = ex.GetType().Name; }
            TapeCheck(thrown == null && ns.Asked > 0 && w.Index.Count == statics, $"tape 18: a sink that cannot build a kind is asked ({ns.Asked}x), adds no part and nothing throws ({thrown})");
            w.Dispose();
        }
        // (f) an instance under an actor is never one of the actor's static parts
        { var w0 = TapeWorld.Build(Actors, Parts); int baseCount = w0.Index.Count; uint h0 = w0.Index.StructureOf(A1).Hash;
          var inst = TapeWorld.MakeInstanceNode("hind"); w0.Root(A1).AddChild(inst);
          var idx = TapeSeams.NewIndex(w0.Actors);
          TapeCheck(idx.Count == baseCount && idx.StructureOf(A1).Hash == h0, $"tape 18: a live instance under an actor is no static part of it: index {idx.Count} parts as without it ({baseCount}), the actor's structure hash unchanged");
          w0.Dispose(); }
        // (g) the recorder allocates nothing per sample with instances alive; a spawn or a despawn may (reported)
        { var w = TapeWorld.Build(Actors, Parts); var rec = TapeSeams.NewRecorder();
          if (rec is WorldTapeRecorder)
          {
              w.SpawnInstance(A0, 1, "hind", -1f); w.SpawnInstance(A1, 2, "hind", -1f); w.SpawnInstance(A2, 3, "missile", -1f);
              w.Tick(0f); rec.Begin(w.Index); long total = 0; int counted = 0;
              for (int n = 0; n <= 3600; n++)
              {
                  float t = n / 60f; w.Tick(t); long a0 = GC.GetAllocatedBytesForCurrentThread(); rec.Record(t); long a1 = GC.GetAllocatedBytesForCurrentThread();
                  if (t > 3f) { total += a1 - a0; counted++; }
              }
              TapeCheck(total <= 4096, $"tape 18: with 3 instances alive the recorder allocates at most 4 KB over 60 s of samples ({total} bytes in {counted} calls)");
              long spawnBytes = 0; int spawns = 0; w.Tick(60f);
              for (int k = 0; k < 6; k++)
              {
                  int id = 10 + k; w.SpawnInstance(A0, id, "hind", 61f + k); float t = 61f + k; w.Tick(t); long a0 = GC.GetAllocatedBytesForCurrentThread(); rec.Record(t); spawnBytes += GC.GetAllocatedBytesForCurrentThread() - a0; spawns++;
                  w.DespawnInstance(id); t += 0.5f; w.Tick(t); a0 = GC.GetAllocatedBytesForCurrentThread(); rec.Record(t); spawnBytes += GC.GetAllocatedBytesForCurrentThread() - a0; spawns++;
              }
              TapeInfo($"tape 18: a record call that takes an instance in or out of the index allocates about {spawnBytes / spawns} bytes ({spawns} calls); the steady-state calls {total / Math.Max(1, counted)} bytes each");
          }
          else Skip("tape 18: allocation per sample (the reference recorder is not the real one)");
          w.Dispose(); }
    }

    // ================================================================ the real game

    static string TapeSceneIdsOf(string line) => string.Join(",", Regex.Matches(line ?? "", @"#(\d+)").Select(m => m.Groups[1].Value));

    /// <summary>"Spawn at time, not despawned by it": the instances a tape has alive at <paramref name="t"/>, with their spawn times.</summary>
    static Dictionary<int, float> TapeLiveAt(IEnumerable<TapeEvent> events, float t)
    {
        var d = new Dictionary<int, float>();
        foreach (var e in events.OrderBy(e => e.Time))
        {
            if (e.Time > t) break;
            if (e.Type == TapeEventType.Spawn) d[e.Instance] = e.Time; else if (e.Type == TapeEventType.Despawn) d.Remove(e.Instance);
        }
        return d;
    }

    sealed record TapeSeekInst(int Seek, float At, string Hash, List<(int Id, float Spawn, Vector3? P)> Inst, int Nodes, string Raw);

    static List<TapeSeekInst> TapeSeekLines(TapeChild c)
    {
        var list = new List<TapeSeekInst>();
        foreach (var l in c.All("tape: seek #"))
        {
            var m = Regex.Match(l, @"seek #(\d+) at (-?[\d.]+) poses ([0-9a-f]+) instances \[([^\]]*)\] nodes (.*)$");
            if (!m.Success) continue;
            var inst = Regex.Matches(m.Groups[4].Value, @"(\d+)@(-?\d+\.\d+)(?:=\((-?[\d.]+),(-?[\d.]+),(-?[\d.]+)\))?")
                .Select(x => (int.Parse(x.Groups[1].Value), float.Parse(x.Groups[2].Value, CultureInfo.InvariantCulture),
                    x.Groups[3].Success ? new Vector3(float.Parse(x.Groups[3].Value, CultureInfo.InvariantCulture), float.Parse(x.Groups[4].Value, CultureInfo.InvariantCulture), float.Parse(x.Groups[5].Value, CultureInfo.InvariantCulture)) : (Vector3?)null)).ToList();
            int nodes = Regex.Matches(m.Groups[5].Value, @"=(\d+)").Sum(x => int.Parse(x.Groups[1].Value));
            list.Add(new TapeSeekInst(int.Parse(m.Groups[1].Value), float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), m.Groups[3].Value, inst, nodes, l));
        }
        return list;
    }

    static Dictionary<int, Vector3> TapePosAtLine(TapeChild c, int lap)
    {
        var l = c.All($"pos-at 40.00 lap {lap}:").LastOrDefault();      // (the last: a void lap at the start line is recorded too)
        var d = new Dictionary<int, Vector3>();
        if (l != null) foreach (Match m in Regex.Matches(l, @"(\d+)=\((-?[\d.]+),(-?[\d.]+),(-?[\d.]+)\)"))
            d[int.Parse(m.Groups[1].Value)] = new Vector3(float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), float.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture), float.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture));
        return d;
    }

    /// <summary>The checks every Hind replay shares: the first seek (at 40 s) restores exactly the instances the tape has alive then, at the poses the recording had; every seek of a loop is alike; the end state is consistent; the exit despawns all.</summary>
    void TapeScene4Replay(TapeChild c, WorldTape t, int lap, string label, int minSeeks)
    {
        var body = WorldTapeCodec.ReadBody(t.Payload);
        var seeks = TapeSeekLines(c);
        var want = TapeLiveAt(body.Events, 40f);
        var first = seeks.FirstOrDefault();
        bool setOk = first != null && Math.Abs(first.At - 40f) < 0.01f && first.Inst.Select(i => i.Id).OrderBy(i => i).SequenceEqual(want.Keys.OrderBy(i => i)) && first.Inst.All(i => Math.Abs(i.Spawn - want[i.Id]) < 0.006f);
        TapeCheck(setOk && want.Count >= 2, $"{label}: the first seek (40.00 s, the middle of the Hinds' lives) restores exactly the {want.Count} instances the tape has alive then, each with its spawn time (ids {string.Join(",", want.Keys.OrderBy(k => k))} vs {string.Join(",", (first?.Inst.Select(i => i.Id).OrderBy(i => i).ToArray() ?? new int[0]))})");
        var rec = TapePosAtLine(c, lap); float worst = 0; int compared = 0;
        if (first != null) foreach (var i in first.Inst) if (i.P is Vector3 p && rec.TryGetValue(i.Id, out var q)) { worst = MathF.Max(worst, p.DistanceTo(q)); compared++; }
        TapeCheck(compared >= 2 && worst < 0.03f, $"{label}: those instances stand where the recording had them at 40.00 s (worst {worst * 100:F1} cm over {compared} instances; the log rounds to 1 cm)");
        TapeCheck(seeks.Count >= minSeeks && seeks.All(s => s.Hash == seeks[0].Hash && s.Inst.Count == seeks[0].Inst.Count && s.Inst.Zip(seeks[0].Inst).All(z => z.First.Id == z.Second.Id && z.First.P == z.Second.P)),
            $"{label}: {seeks.Count} seeks (the start and every wrap) restore the same instances at the same poses");
        TapeCheck(seeks.Skip(1).All(s => s.Nodes == s.Inst.Count), $"{label}: at every seek after the first the nodes of the kinds in the tree are exactly the restored instances (no leak: {string.Join(",", seeks.Skip(1).Take(14).Select(s => s.Nodes))} for {seeks.Skip(1).FirstOrDefault()?.Inst.Count})");
        var inst = c.Lines.LastOrDefault(l => l.StartsWith("tape: replay instances (replay done)"));
        int made = (int)TapeNum(inst, "made"), removed = (int)TapeNum(inst, "removed"), live = (int)TapeNum(inst, "live");
        int inTree = Regex.Matches(Regex.Match(inst ?? "", @"in the tree \[([^\]]*)\]").Groups[1].Value, @"=(\d+)").Sum(x => int.Parse(x.Groups[1].Value));
        TapeCheck(inst != null && made > 0 && made - removed == live && inTree == live && inst.Contains("live actors' own [0]"), $"{label}: at the end made {made} - removed {removed} = live {live} = the nodes in the tree {inTree}, and no live instance of the actor was registered during the replay ({inst?.Trim()})");
        var exit = c.Find("scene exit: replay instances");
        TapeCheck(exit != null && exit.Contains("0 left") && (int)TapeNum(exit, "instances") >= made, $"{label}: leaving the scene despawned every replay-owned instance ({exit?.Trim()})");
    }

    void TapeScene4()
    {
        string a = TapeSceneDir("h2a"), off = TapeSceneDir("h2off"), rel = TapeSceneDir("h2rel");
        string trA = a + ".trace.txt", trOff = off + ".trace.txt";
        try
        {
            var lapArgs = new[] { "--start", "--autodrive", "--track=bonus", "--ap-speed=0.8", "--tape-lap-speeds=0,1", "--tape-debug", "--tape-pos-at=40" };
            var env = new Dictionary<string, string> { ["HINDDBG"] = "1", ["WORLDTRACE"] = trA, ["WORLDTRACE_FRAMES"] = "60000" };
            // ---- A: lap 1 (slow) then lap 2 (a record that starts with lap 1's Hinds alive); then the title replay in the SAME scene: starts at 40 s, wraps every 6 s
            var c = TapeGame(a, lapArgs.Concat(new[] { "--tape-title-after-lap=2", "--tape-replay-start=40", "--tape-wrap-every=6", "--tape-replay-quit=66" }).ToArray(), 1200, env: env);
            var l1 = c.Find("tape: lap 1 "); var l2 = c.Find("tape: lap 2 ");
            TapeCheck(!c.TimedOut && c.Exit == 0 && !c.Crashed, $"scene 11 (same scene): the child ended cleanly (exit {c.Exit}, {c.Seconds:F0} s)");
            TapeCheck(l1 != null && l2 != null && l1.Contains("record True") && l2.Contains("record True") && l2.Contains("recording True"), $"scene 11 (same scene): lap 1 and the faster lap 2 are both records and lap 2 was recorded ({l2?.Trim()})");
            var (g, t, paired) = TapeSceneLoad(a, "bonus", 0);
            float lap2 = float.Parse(Regex.Match(l2 ?? "tape: lap 2 0 s", @"lap 2 (\d+\.\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
            TapeCheck(c.All("ghost+tape saved").Count() == 2 && g != null && t != null && paired && Math.Abs(t.LapTime - lap2) < 0.02f && Math.Abs(g.LapTime - lap2) < 0.02f, $"scene 11 (same scene): PROOF a lap was saved: two pair saves reported, ghost_bonus.bin and tape_bonus.bin on disk are lap 2's pair (tape {t?.LapTime:F2} s, lap 2 {lap2:F2} s, paired {paired})");
            var i1 = c.Find("instances of lap 1: "); var i2 = c.Find("instances of lap 2: ");
            string endIds1 = TapeSceneIdsOf(i1?[(i1.IndexOf("alive now") + 9)..]), startIds2 = Regex.Match(i2 ?? "", @"alive at the start \d+ \[([\d,]*)\]").Groups[1].Value;
            int n2 = (int)TapeNum(i2, "alive at the start");
            TapeCheck(n2 >= 2 && endIds1.Length > 0 && startIds2 == endIds1, $"scene 11 (same scene): lap 2 starts with the Hinds that lap 1 ended with: alive at its start [{startIds2}] = alive at the end of lap 1 [{endIds1}] ({i2?.Trim()})");
            var body = t == null ? null : WorldTapeCodec.ReadBody(t.Payload);
            var zero = body?.Events.Where(e => e.Type == TapeEventType.Spawn && e.Time == 0f).Select(e => e.Instance).OrderBy(x => x).ToList();
            var kf0 = body?.Keys[0].LiveInstances.Select(e => e.Instance).OrderBy(x => x).ToList();
            TapeCheck(body != null && zero.SequenceEqual(startIds2.Split(',').Select(int.Parse).OrderBy(x => x)) && kf0.SequenceEqual(zero), $"scene 11 (same scene): the saved tape records them as spawned at 0 and holds them in its first keyframe (events at 0: [{string.Join(",", zero ?? new())}], keyframe 0: [{string.Join(",", kf0 ?? new())}])");
            TapeCheck(body != null && body.Events.Count(e => e.Type == TapeEventType.Spawn) >= n2 + 1 && body.Events.Any(e => e.Type == TapeEventType.Despawn) && body.Events.Where(e => e.Type is TapeEventType.Spawn or TapeEventType.Despawn).All(e => e.Time < 1e-6f || MathF.Abs(e.Time * WorldTapeConst.TapeRate - MathF.Round(e.Time * WorldTapeConst.TapeRate)) < 1e-3f), "scene 11 (same scene): spawns and despawns during the lap are events on the sample grid (the first sample that shows or lacks the instance)");
            var cost = c.Find("record cost of lap 2"); var fin = c.Lines.Where(x => x.Contains("World tape: set 0 lap")).LastOrDefault();
            TapeInfo($"scene 11 (Bonus, phase 2): tape file {(t != null ? new FileInfo(TapeTapeFile(a, "bonus", 0)).Length / 1024 : 0)} KB; {cost?.Trim()}; {i2?.Trim()}; {fin?.Trim()}");
            TapeCheck(cost != null && TapeNum(cost, "avg") < 1.0f && TapeNum(cost, "p99") < 3.0f, $"scene 11 (same scene): the recording cost per sample with Hinds in the index is within twice the budget (avg {TapeNum(cost, "avg"):F3} ms, p99 {TapeNum(cost, "p99"):F3} ms)");
            TapeCheck(fin != null && TapeNum(fin, "main thread") < 50, $"scene 11 (same scene): the finish line costs the main thread {TapeNum(fin, "main thread")} ms (under 50 ms; the pair save took {TapeNum(fin, "pair save")} ms on a worker)");
            TapeCheck(c.Has("title entered in the same scene #1 after lap 2: replaying True") && !c.Has("scene build #2") && TapeKeyList(c.Find("from tape: covers ["), "covers").Contains("Jets:HindSorties"), $"scene 11 (same scene): the title replays the fresh tape in the same scene and covers the Hind sorties ({c.Find("from tape: covers [")?.Split("live [")[0].Trim()})");
            TapeScene4Replay(c, t, 2, "scene 11 (same scene)", 10);
            // the sorties: live before the replay, none during it; the crew and the battery tick all the same
            int titleAt = Array.FindIndex(c.Lines, x => x.Contains("title entered in the same scene"));
            int hindBefore = c.Lines.Take(titleAt).Count(x => x.StartsWith("HIND ")), hindAfter = c.Lines.Skip(titleAt).Count(x => x.StartsWith("HIND "));
            var sum = TapeSummaryLine(c, "replay done"); var inst = c.Lines.LastOrDefault(x => x.StartsWith("tape: replay instances (replay done)"));
            TapeCheck(hindBefore > 40 && hindAfter == 0, $"scene 11 (same scene): a covered Hind sortie does not run live in the replay: {hindBefore} sortie lines in the two laps, {hindAfter} after the title (the live aircraft are not simulated)");
            TapeCheck(TapeCountOf(sum, "skipped", "Jets:HindSorties") > 3000 && TapeCountOf(sum, "live", "Jets:HindSorties") == 0 && TapeNum(inst, "hind crew ticks") > 3000, $"scene 11 (same scene): the Hind actor's aircraft are skipped every step ({TapeCountOf(sum, "skipped", "Jets:HindSorties")}) while its crew and battery tick on ({TapeNum(inst, "hind crew ticks")} steps); uncovered actors still tick (live [{TapeBracket(sum, "live")}])");
            TapeCheck(TapeCountOf(sum, "live", "Details:SiberiaDetails") > 3000, "scene 11 (same scene): uncovered content still ticks in the replay");
            // ---- A': the same two laps with recording OFF, to the same step: the Hind sorties and the world trace are identical
            var envOff = new Dictionary<string, string> { ["HINDDBG"] = "1", ["WORLDTRACE"] = trOff, ["WORLDTRACE_FRAMES"] = "60000" };
            var o = TapeGame(off, lapArgs.Concat(new[] { "--tape-quit-laps=2" }).ToArray(), 1200, tapeOff: true, env: envOff);
            TapeCheck(!o.TimedOut && o.Exit == 0 && !o.Crashed && !File.Exists(TapeTapeFile(off, "bonus", 0)) && File.Exists(TapeGhostFile(off, "bonus", 0)), $"scene 11 (recording off): the two laps ran with CCCP_TAPE=off, a ghost and no tape ({o.Seconds:F0} s)");
            var hOn = c.Lines.Take(titleAt).Where(x => x.StartsWith("HIND ")).ToList(); var hOff = o.Lines.Where(x => x.StartsWith("HIND ")).ToList();
            int common = Math.Min(hOn.Count, hOff.Count);
            TapeCheck(common > 40 && hOn.Take(common).SequenceEqual(hOff.Take(common)) && Math.Abs(hOn.Count - hOff.Count) <= 1, $"scene 11 (recording off): the Hind sorties (phases, times, positions, targets) are identical with recording on and off over two laps and a shoot-down ({common} lines, on {hOn.Count} off {hOff.Count})");
            var lapsOn = c.Lines.Where(x => x.StartsWith("tape: lap ")).Select(x => Regex.Match(x, @"lap \d+ (\d+\.\d+)").Groups[1].Value).Take(2).ToList(); var lapsOff = o.Lines.Where(x => x.StartsWith("tape: lap ")).Select(x => Regex.Match(x, @"lap \d+ (\d+\.\d+)").Groups[1].Value).Take(2).ToList();
            TapeCheck(lapsOn.Count == 2 && lapsOn.SequenceEqual(lapsOff), $"scene 11 (recording off): the lap times are identical ({string.Join(", ", lapsOn)} vs {string.Join(", ", lapsOff)})");
            if (File.Exists(trA) && File.Exists(trOff))
            {
                var ta = File.ReadAllLines(trA); var tb = File.ReadAllLines(trOff);
                int seenRacing = -1, cut = ta.Length;
                for (int k = 0; k < ta.Length; k++) { var st = ta[k].Split(' ', 3); if (st.Length > 2 && st[1] == "Racing") seenRacing = k; else if (seenRacing >= 0 && st.Length > 2 && st[1].StartsWith("Title")) { cut = k; break; } }
                int n = Math.Min(cut, tb.Length); bool same = n > 8000 && cut >= tb.Length - 3 && ta.Take(n).SequenceEqual(tb.Take(n));      // (the off run quits on the lap-done step; the on run waits for the pair save's worker before it enters the title, so it has a few more racing steps)
                TapeCheck(same, $"scene 11 (recording off): the car and world trace is identical with recording on and off for the {n} steps of two Bonus laps with their sorties (off ran {tb.Length} steps)");
            }
            else TapeCheck(false, "scene 11 (recording off): both trace files exist");
            TapeSceneRm(off);

            // ---- B: record lap 1, reload the scene, replay in the fresh one (new index, new actors, the ids of the first scene)
            var cb = TapeGame(rel, new[] { "--start", "--autodrive", "--track=bonus", "--tape-debug", "--tape-pos-at=40", "--tape-reload-after-lap=1", "--tape-replay-start=40", "--tape-wrap-every=6", "--tape-replay-quit=30" }, 900);
            var (gb, tb2, pb) = TapeSceneLoad(rel, "bonus", 0);
            TapeCheck(!cb.TimedOut && cb.Exit == 0 && !cb.Crashed && gb != null && tb2 != null && pb && cb.Has("ghost+tape saved") && cb.Has("reloading the scene"), $"scene 11 (reload): PROOF a lap was saved in scene #1 (ghost+tape pair on disk, matching), then the scene was reloaded ({cb.Seconds:F0} s)");
            TapeCheck(cb.Has("scene build #2") && TapeKeyList(cb.Find("from tape: covers ["), "covers").Contains("Jets:HindSorties"), "scene 11 (reload): the fresh scene #2 (fresh actors, fresh index) covers the Hind sorties from the tape of scene #1");
            if (tb2 != null) TapeScene4Replay(cb, tb2, 1, "scene 11 (reload)", 4);
            TapeSceneRm(rel);

            // ---- C: replay-only runs on lap 2's pair (copies of A's directory)
            TapeScene4Trips(a);
            TapeScene4Foreign(a);
        }
        finally { TapeSceneRm(a); TapeSceneRm(off); TapeSceneRm(rel); foreach (var f in new[] { trA, trOff }) try { File.Delete(f); } catch { } }
    }

    // leaving the replay for gameplay (a race, a preview): the replay's instances go, the live sorties come back; coming back re-seeks to the same instances
    void TapeScene4Trips(string src)
    {
        foreach (bool preview in new[] { false, true })
        {
            string d = TapeSceneDir("h2t");
            try
            {
                TapeCopyDir(src, d);
                var c = TapeGame(d, new[] { "--track=bonus", "--tape-debug", "--tape-replay-start=40", "--tape-title-trip=4,10" + (preview ? ",preview" : ""), "--tape-replay-quit=12" }, 240, env: new() { ["HINDDBG"] = "1" });
                string label = $"scene 11 ({(preview ? "preview" : "race")} trip)";
                var seeks = TapeSeekLines(c);
                var rel = c.Find("released: covered parts back at rest");
                TapeCheck(c.Exit == 0 && !c.Crashed && seeks.Count == 2 && seeks[0].Inst.Count >= 2 && seeks[0].Hash == seeks[1].Hash && seeks[0].Inst.Select(i => i.Id).SequenceEqual(seeks[1].Inst.Select(i => i.Id)), $"{label}: the replay seeks at the start and again on return, to the same instances ({seeks.Count} seeks, {seeks.FirstOrDefault()?.Inst.Count} instances)");
                var m = Regex.Match(rel ?? "", @"replay instances (\d+) made, (\d+) removed, (\d+) left");
                TapeCheck(m.Success && m.Groups[1].Value == m.Groups[2].Value && m.Groups[3].Value == "0" && int.Parse(m.Groups[1].Value) >= seeks[0].Inst.Count, $"{label}: leaving the replay despawned every replay-owned instance ({rel?.Trim()})");
                // after leaving, the live Hinds are the actor's again (the exercise is reset, its parked aircraft stand there): only live ones are in the tree
                var live = Regex.Match(rel ?? "", @"nodes (.*)$").Groups[1].Value;
                int liveHinds = Regex.Matches(live, @"hind=(\d+)").Sum(x => int.Parse(x.Groups[1].Value));
                TapeCheck(liveHinds is >= 1 and <= 4, $"{label}: after the release only the live parked Hinds are in the tree ({live})");
                var sum = TapeSummaryLine(c, "replay done"); var inst = c.Lines.LastOrDefault(x => x.StartsWith("tape: replay instances (replay done)"));
                TapeCheck((int)TapeNum(inst, "made") - (int)TapeNum(inst, "removed") == (int)TapeNum(inst, "live") && (int)TapeNum(inst, "live") == seeks[1].Inst.Count && c.Find("scene exit: replay instances")?.Contains("0 left") == true, $"{label}: no instance leaks over the trip ({inst?.Trim()})");
                TapeCheck(TapeCountOf(sum, "skipped", "Jets:HindSorties") > 300, $"{label}: the Hind sorties are skipped in the replay again after the return");
            }
            finally { TapeSceneRm(d); }
        }
    }

    // corrupt, foreign and older tapes: the replay falls back to live (or only the Hinds do) and nothing is left over
    void TapeScene4Foreign(string src)
    {
        var tpName = Path.GetFileName(TapeTapeFile(src, "bonus", 0));
        var variants = new List<(string Name, Action<string> Make, Func<TapeChild, bool> Expect)>
        {
            ("truncated body", d => { var f = Path.Combine(d, tpName); var b = File.ReadAllBytes(f); File.WriteAllBytes(f, b[..(b.Length * 6 / 10)]); }, c => !c.Has("from tape")),
            ("garbage file", d => File.WriteAllBytes(Path.Combine(d, tpName), Enumerable.Range(0, 5000).Select(i => (byte)(i * 31 + 7)).ToArray()), c => !c.Has("from tape")),
            ("another ghost's fingerprint", d => { var f = Path.Combine(d, tpName); var t = WorldTapeCodec.Decode(File.ReadAllBytes(f)); t.GhostFingerprint ^= 1; File.WriteAllBytes(f, WorldTapeCodec.Encode(t)); }, c => !c.Has("from tape") && c.Has("belongs to another ghost")),
            ("the Hind actor's structure hash differs", d => { var f = Path.Combine(d, tpName); var t = WorldTapeCodec.Decode(File.ReadAllBytes(f)); t.ActorStructure["Jets:HindSorties"].Hash ^= 1; File.WriteAllBytes(f, WorldTapeCodec.Encode(t)); },
                c => TapeKeyList(c.Find("from tape: covers ["), "covers") is { Length: > 3 } cv && !cv.Contains("Jets:HindSorties") && cv.Contains("Btr:BtrPatrols")),
            ("a tape of the first phase (no Hind actor in it)", d => { var f = Path.Combine(d, tpName); var t = WorldTapeCodec.Decode(File.ReadAllBytes(f)); t.ActorStructure.Remove("Jets:HindSorties"); File.WriteAllBytes(f, WorldTapeCodec.Encode(t)); },
                c => TapeKeyList(c.Find("from tape: covers ["), "covers") is { Length: > 3 } cv && !cv.Contains("Jets:HindSorties") && cv.Contains("Btr:BtrPatrols")),
        };
        foreach (var (name, make, expect) in variants)
        {
            string d = TapeSceneDir("h2v");
            try
            {
                TapeCopyDir(src, d); make(d);
                var c = TapeGame(d, new[] { "--track=bonus", "--tape-debug", "--tape-replay-start=40", "--tape-replay-quit=14" }, 240, env: new() { ["HINDDBG"] = "1" });
                var inst = c.Lines.LastOrDefault(x => x.StartsWith("tape: replay instances (replay done)"));
                bool fromTape = c.Has("from tape");
                bool hindLive = c.Lines.Count(x => x.StartsWith("HIND ")) > 0;      // the sorties run live (the first lifts off at 5 s)
                TapeCheck(c.Exit == 0 && !c.TimedOut && !c.Crashed && expect(c) && inst != null && (int)TapeNum(inst, "made") == 0 && hindLive && (!fromTape || TapeNum(inst, "hind crew ticks") == 0), $"scene 11 (foreign tape: {name}): replays without exception, no instance is made, the Hind sorties run live{(c.Crashed ? " (CRASHED)" : "")}");
            }
            finally { TapeSceneRm(d); }
        }
    }
}
