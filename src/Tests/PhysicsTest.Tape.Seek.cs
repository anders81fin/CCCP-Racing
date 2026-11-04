using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

// tape 20: a breakable's FALL is seekable. The real Breakables on the real Ilyich props: a tree and a pole (with its wires) are felled the way the replay fells them (ReplayBreak, then the
// physics ticks), and the model transforms (and the wires' vertices) are compared with what a seek to the same clock shows (ReplaySet with the age), during the fall, at the landing,
// during the bounce and after it has settled. Before the fix a seek always showed the settled tree whatever the age.
public partial class PhysicsTest
{
    /// <summary>The ages a seek hands to the breakables (test seam: the mutation check swaps in a seek that has forgotten them).</summary>
    static Func<Dictionary<int, float>, Dictionary<int, float>> TapeBreakAges = a => a;

    static float TapeXformDiff(Transform3D a, Transform3D b)
    {
        float d = a.Origin.DistanceTo(b.Origin);
        foreach (var (x, y) in new[] { (a.Basis.X, b.Basis.X), (a.Basis.Y, b.Basis.Y), (a.Basis.Z, b.Basis.Z) }) d = MathF.Max(d, (x - y).Length());
        return d;
    }

    static float TapeFallAngle(Transform3D rest, Transform3D now) => (rest.Basis.Inverse() * now.Basis).GetRotationQuaternion().GetAngle();

    static Vector3[] TapeWires(MeshInstance3D mi) => mi?.Mesh is { } m && m.GetSurfaceCount() > 0 ? m.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array() : Array.Empty<Vector3>();

    void TapeT20()
    {
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        var root = Props.Build(track);
        var br = new Breakables { Name = "BreakablesT20" };
        br.Init(track, null, root, null);
        var lines = root.GetNodeOrNull("PowerLines");
        (int Id, Node3D Node, Transform3D Home, string Type)? Find(Func<string, bool> suits)
        {
            foreach (var c in root.GetChildren())
                if (c is Node3D n && n.HasMeta("top") && n.HasMeta("prop") && track.PropObstacle.TryGetValue(n.GetMeta("prop").AsInt32(), out int obs) && suits(track.Props[n.GetMeta("prop").AsInt32()].Type))
                    return (obs, n, n.Transform, track.Props[n.GetMeta("prop").AsInt32()].Type);
            return null;
        }
        const float Dt = 1f / 60f;
        foreach (var (label, pick) in new (string, Func<string, bool>)[] { ("tree", ty => ty is "birch" or "spruce" or "pine"), ("pole", ty => ty == "pole") })
        {
            var f = Find(pick);
            if (f == null) { TapeCheck(false, $"tape 20: Ilyich has a {label} that falls"); continue; }
            var (id, node, rest, type) = f.Value;
            var how = new Dictionary<int, (Vector3 Vel, Vector3 At)> { [id] = (new Vector3(3f, 0, 2f), rest.Origin) };
            var spans = lines == null ? new List<MeshInstance3D>() : lines.GetChildren().OfType<MeshInstance3D>().Where(m => m.HasMeta("poles") && (m.GetMeta("poles").AsVector2I().X == node.GetMeta("prop").AsInt32() || m.GetMeta("poles").AsVector2I().Y == node.GetMeta("prop").AsInt32())).ToList();
            float worst = 0, worstWire = 0, angleSeq = 0, angleSeek = 0, angleSettled = 0; string where = ""; int compared = 0;
            // (a) sequential from the start (age 0) and from a late start (the player fires an event up to one step late), against a seek to the same clock
            foreach (float late in new[] { 0f, 0.013f })
                foreach (int n in new[] { 3, 12, 42, 66, 78, 96, 150 })
                {
                    br.Reset();
                    br.ReplayBreak(id, how[id].Vel, how[id].At, late);
                    for (int k = 0; k < n; k++) br.Tick(Dt, null, false);
                    var seq = node.Transform; var wiresSeq = spans.Select(TapeWires).ToList(); float aSeq = TapeFallAngle(rest, seq);
                    float age = late + n * Dt;
                    br.ReplaySet(new[] { id }, how, TapeBreakAges(new Dictionary<int, float> { [id] = age }));
                    var seek = node.Transform; float aSeek = TapeFallAngle(rest, seek);
                    float d = TapeXformDiff(seq, seek); compared++;
                    if (d > worst) { worst = d; where = $"age {age:F3} s: playing {aSeq:F3} rad, seek {aSeek:F3} rad"; }
                    for (int s = 0; s < spans.Count; s++)
                    {
                        var a = wiresSeq[s]; var b = TapeWires(spans[s]);
                        worstWire = MathF.Max(worstWire, a.Length != b.Length ? 99f : a.Zip(b, (p, q) => p.DistanceTo(q)).DefaultIfEmpty(0f).Max());
                    }
                    if (late == 0f && n == 12) { angleSeq = aSeq; angleSeek = aSeek; }
                    if (late == 0f && n == 150) angleSettled = aSeq;
                }
            TapeCheck(worst < 1e-3f, $"tape 20: a {label}'s ({type}) model transform after a seek equals playing to the same clock at 14 clocks in the fall, at the landing, in the bounce and settled (worst difference {worst:F5}; {where})");
            TapeCheck(angleSeq > 0.01f && angleSeq < angleSettled * 0.2f && angleSettled > 1.0f, $"tape 20: at 0.2 s the {label} is only {angleSeq:F3} rad down (settled {angleSettled:F3} rad): a seek showing {angleSeek:F3} rad is {(MathF.Abs(angleSeek - angleSeq) < 0.01f ? "right" : "WRONG")}");
            if (spans.Count > 0) TapeCheck(worstWire < 1e-3f, $"tape 20: and the {spans.Count} power-line span(s) of the pole sag the same after a seek as when played (worst vertex difference {worstWire:F5} m)");
            else TapeInfo($"tape 20: the {label} has no power-line span");
            // (b) a seek with no age (an old caller) still lies the tree down settled, as before
            br.ReplaySet(new[] { id }, how);
            TapeCheck(MathF.Abs(TapeFallAngle(rest, node.Transform) - angleSettled) < 1e-3f, $"tape 20: a seek that gives no age shows the {label} settled ({TapeFallAngle(rest, node.Transform):F3} rad)");
            br.Reset();
            TapeCheck(TapeXformDiff(node.Transform, rest) < 1e-5f, $"tape 20: Reset stands the {label} up again");
        }
        br.Free(); root.Free();
    }

    // tape 21: what the replay makes of the covered vehicles' per-step effects and of the hits it did not simulate, on the real classes.
    //   The emitters (TapeEmitters): a plough, a tank and a BTR driven past at speed throw their snow, track dust, exhaust smoke and dust; standing still they make none;
    //   a jump (a seek) is not speed. The feel events (TapeFxPlayer.Feel): a babushka's launch makes its thump and shake only when it is happening now; a burst can's splash is made at any age.
    void TapeT21()
    {
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/siberia.json"));
        var fx = new TyreEffects { Name = "FxT21" }; fx.Init(track); AddChild(fx);
        var host = new Node3D { Name = "HostT21" }; AddChild(host);
        Node3D Hull() { var n = new Node3D(); host.AddChild(n); n.GlobalPosition = new Vector3(200, 10, 40); return n; }
        const float Dt = 1f / 60f;
        float drive(Node3D hull, TapeEmitters.Kind kind, float mps, int steps, out int made, out int particles)
        {
            hull.GlobalPosition = new Vector3(200, 10, 40);
            var em = new TapeEmitters(fx, track); em.Add(hull, kind);
            fx.ClearReplay(); em.Restart();
            for (int k = 0; k < steps; k++) { hull.GlobalPosition += new Vector3(0, 0, -mps * Dt); em.Tick(Dt); }
            made = em.Made.Values.Sum(); particles = fx.ReplayParticles; return mps;
        }
        foreach (var (kind, label, speed, key) in new[] { (TapeEmitters.Kind.Plough, "plough", 10f, "plough-spray"), (TapeEmitters.Kind.Tank, "tank", 6f, "tank-dust"), (TapeEmitters.Kind.Btr, "BTR", 8f, "btr-dust") })
        {
            var hull = Hull();
            drive(hull, kind, speed, 240, out int moving, out int pMoving);
            drive(hull, kind, 0f, 240, out int still, out int pStill);
            TapeCheck(moving > 0 && pMoving > 0, $"tape 21: a {label} driven at {speed:F0} m/s for 4 s makes its {key} ({moving} puffs, {pMoving} replay particles alive)");
            TapeCheck(still == 0 || (kind == TapeEmitters.Kind.Tank && still <= 8), $"tape 21: the {label} standing still makes {(kind == TapeEmitters.Kind.Tank ? "at most a little exhaust smoke" : "none")} ({still} puffs)");
            // a seek: the hull jumps 500 m between two steps, which is no speed (nothing is thrown for the jump itself)
            var em = new TapeEmitters(fx, track); em.Add(hull, kind); em.Restart(); hull.GlobalPosition = new Vector3(200, 10, 40); em.Tick(Dt);
            fx.ClearReplay(); hull.GlobalPosition = new Vector3(700, 10, 40); em.Tick(Dt);
            TapeCheck(fx.ReplayParticles == 0, $"tape 21: a jump of 500 m between two steps throws nothing from the {label} ({fx.ReplayParticles} particles)");
            hull.Free();
        }
        fx.ClearReplay();
        // the feel events
        var player = new TapeFxPlayer(fx, track, null, host);
        var felt = new List<(string Kind, float Age, float Param)>();
        player.Feel = (e, age) => felt.Add((e.Kind, age, e.Param));
        player.Effect(new TapeEvent(10f, TapeEventType.Effect, Kind: TapeFxKind.BabushkaLaunch, Effect: TapeEffect.BabushkaLaunch, Pos: new Vector3(5, 0, 5), Param: 0.6f, Life: 0.5f), 0f);
        player.Effect(new TapeEvent(10f, TapeEventType.Effect, Kind: TapeFxKind.BabushkaLaunch, Effect: TapeEffect.BabushkaLaunch, Pos: new Vector3(5, 0, 5), Param: 0.6f, Life: 0.5f), 0.4f);
        player.Effect(new TapeEvent(10f, TapeEventType.Effect, Kind: TapeFxKind.CanisterBurst, Effect: TapeEffect.Explosion, Pos: new Vector3(5, 0, 5), Life: 0.5f), 0.1f);
        player.Effect(new TapeEvent(10f, TapeEventType.Effect, Kind: TapeFxKind.CanisterBurst, Effect: TapeEffect.Explosion, Pos: new Vector3(5, 0, 5), Life: 0.5f), 0.6f);
        TapeCheck(felt.Count(f => f.Kind == TapeFxKind.BabushkaLaunch) == 1 && felt.First(f => f.Kind == TapeFxKind.BabushkaLaunch).Param == 0.6f, $"tape 21: a babushka's launch makes its thump and shake once, at age 0, not for one that is 0.4 s old ({felt.Count(f => f.Kind == TapeFxKind.BabushkaLaunch)} calls)");
        TapeCheck(felt.Count(f => f.Kind == TapeFxKind.CanisterBurst) == 1, $"tape 21: a burst can's splash is handed over for the one still going (age 0.1), not after its life of 0.5 s ({felt.Count(f => f.Kind == TapeFxKind.CanisterBurst)} calls)");
        TapeCheck(player.Made.GetValueOrDefault(TapeFxKind.BabushkaLaunch) == 2 && player.Made.GetValueOrDefault(TapeFxKind.CanisterBurst) == 1, $"tape 21: the player counts what it made (the babushka's second event, 0.4 s old, is made but silent; the can's stale one is not made) ({string.Join(", ", player.Made.Select(kv => kv.Key + "=" + kv.Value))})");
        player.Clear(); player.Free(); fx.ClearReplay(); fx.Free(); host.Free();
    }

    /// <summary>The production chain's sink for breakables: the real <see cref="TapeFxPlayer"/> over the real <see cref="Breakables"/>, with the two-argument contract (the clock) or, as a wrong one, without it.</summary>
    /// <summary>Mutation seam: the production chain's sink drops the clock (what a regression to the one-argument call would do).</summary>
    static bool TapeChainDropsClock;
    sealed class BreakChainSink : IWorldTapeSink
    {
        public readonly TapeFxPlayer Fx; public bool DropClock;
        public BreakChainSink(TapeFxPlayer fx) { Fx = fx; }
        public Node3D Spawn(string kind, string parent, int instance) => null;
        public void Despawn(int instance) { }
        public void Effect(in TapeEvent e, float age) => Fx.Effect(e, age);
        public void ClearEffects() => Fx.Clear();
        public void SetBrokenBreakables(IReadOnlyCollection<int> ids) => Fx.SetBroken(ids);
        public void SetBrokenBreakables(IReadOnlyCollection<int> ids, float clock) { if (DropClock || TapeChainDropsClock) Fx.SetBroken(ids); else Fx.SetBroken(ids, clock); }
    }

    // tape 20b: the same through the production chain: a recorded breakable event, the real player, a sink that is the real TapeFxPlayer over the real Breakables. A seek to a clock in the
    // fall (tree 0.2 s down, 0.7 s, in the bounce, settled) shows what playing to that clock shows (the scene's step: the player, then the scenery's tick), and a lap that starts with a tree
    // already 1.5 s into its fall (a carried-over breakable, recorded at -1.5 s) shows it so at the first frame. A sink that drops the clock is the negative control: the test sees it.
    void TapeT20b()
    {
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        var root = Props.Build(track);
        var br = new Breakables { Name = "BreakablesT20b" };
        var fx = new TyreEffects { Name = "FxT20b" }; fx.Init(track); AddChild(fx);
        br.Init(track, null, root, null);
        Node3D tree = null; int id = -1; Transform3D rest = default;
        foreach (var c in root.GetChildren())
            if (c is Node3D n && n.HasMeta("top") && n.HasMeta("prop") && track.PropObstacle.TryGetValue(n.GetMeta("prop").AsInt32(), out int obs) && track.Props[n.GetMeta("prop").AsInt32()].Type is "birch" or "spruce" or "pine") { tree = n; id = obs; rest = n.Transform; break; }
        if (tree == null) { TapeCheck(false, "tape 20b: Ilyich has a tree"); return; }
        const float Dt = 1f / 60f, A = 10f, End = 30f;
        WorldTape tape;
        using (var w = TapeWorld.Build(2, 3, false)) tape = TapeRecord(w, End, new() { TapeBreak(A, id, A), TapeBreak(-1.5f, id + 100000, 0f) });
        var body = WorldTapeCodec.ReadBody(tape.Payload);
        float Angle() => TapeFallAngle(rest, tree.Transform);
        float Diff(Transform3D a, Transform3D b) => TapeXformDiff(a, b);
        (float Seq, float Seek, float Diff, float Angle, float AngleSeek) Run(float x, bool dropClock)
        {
            var w2 = TapeWorld.Build(2, 3, false); w2.Index = TapeSeams.NewIndex(w2.Actors);
            var fxRoot = new Node3D(); var fxp = new TapeFxPlayer(fx, track, br, fxRoot); fxp.SetBreakInfo(body.Events);
            var sink = new BreakChainSink(fxp) { DropClock = dropClock };
            var p = new WorldTapePlayer(tape, w2.Index, sink);
            br.Reset(); p.Seek(A - 0.5f); br.Tick(Dt, null, false);
            float clock = A - 0.5f;
            while (clock < A + x - 1e-4f) { clock += Dt; p.Apply(clock, Dt); br.Tick(Dt, null, false); }
            var seq = tree.Transform; float aSeq = Angle();
            p.Seek(clock); br.Tick(Dt, null, false);
            var seek = tree.Transform;
            float d = Diff(seq, seek), aSeek = Angle();
            w2.Dispose(); fxp.Free(); fxRoot.Free();
            return (aSeq, aSeek, d, aSeq, aSeek);
        }
        float worst = 0; string where = "";
        foreach (float x in new[] { 0.05f, 0.2f, 0.7f, 1.1f, 1.3f, 3.0f })
        {
            var r = Run(x, false);
            if (r.Diff > worst) { worst = r.Diff; where = $"{x:F2} s after the break: playing {r.Seq:F3} rad, seek {r.Seek:F3} rad"; }
        }
        TapeCheck(worst < 2e-3f, $"tape 20b: through the real player and sink, a seek to a clock in the fall, at the landing, in the bounce and settled gives the tree's model the transform that playing to it gives (worst difference {worst:F5}; {where})");
        var ctl = Run(0.2f, true);
        TapeCheck(ctl.Diff > 0.5f, $"tape 20b: negative control: a sink that drops the clock shows the settled tree ({ctl.Seek:F3} rad) where playing shows {ctl.Seq:F3} rad (difference {ctl.Diff:F3}), and the test sees it");
        // a lap that starts with a tree already 1.5 s into its fall (recorded at -1.5): the first frame (and a seek half a second on) show it so
        {
            var w2 = TapeWorld.Build(2, 3, false); w2.Index = TapeSeams.NewIndex(w2.Actors);
            var fxRoot = new Node3D(); var fxp = new TapeFxPlayer(fx, track, br, fxRoot); fxp.SetBreakInfo(body.Events);
            // (the tape carries a second, invented id: use the real tree for it by re-recording with the right id)
            WorldTape tape2; using (var w = TapeWorld.Build(2, 3, false)) tape2 = TapeRecord(w, 5f, new() { TapeBreak(-1.5f, id, 0f) });
            var body2 = WorldTapeCodec.ReadBody(tape2.Payload); fxp.SetBreakInfo(body2.Events);
            var p = new WorldTapePlayer(tape2, w2.Index, new BreakChainSink(fxp));
            br.Reset(); p.Seek(0f); br.Tick(Dt, null, false); var first = tree.Transform; float a0 = Angle();
            br.Reset(); br.ReplayBreak(id, new Vector3(1, 0, 0), rest.Origin); for (int k = 0; k < 91; k++) br.Tick(Dt, null, false);      // 1.5 s of falling (+ the seek's own step)
            var want = tree.Transform;
            p.Seek(0.5f); br.Tick(Dt, null, false); float a2 = Angle();
            TapeCheck(Diff(first, want) < 2e-3f && a0 > 0.9f, $"tape 20b: a lap that starts with a tree 1.5 s into its fall shows it at its first frame as playing 1.5 s of fall shows it ({a0:F3} rad, difference {Diff(first, want):F5}), not upright and not already settled");
            br.Reset(); p.Seek(0.5f); br.Tick(Dt, null, false);
            TapeCheck(a2 > a0 - 0.01f && MathF.Abs(a2 - MathF.Max(a0, a2)) < 0.2f, $"tape 20b: half a second on it has settled ({a2:F3} rad)");
            w2.Dispose(); fxp.Free(); fxRoot.Free();
        }
        br.Free(); fx.Free(); root.Free();
    }

    // tape 22: the pair save on plain data. The ghost's managed serialisation is byte for byte the file the Godot writer makes, and a worker thread that is given only plain data saves a
    // pair that loads (the same verification and the same order of replacements as the main-thread save).
    void TapeT22()
    {
        var dir = TapeTmpDir();
        try
        {
            Ghost g = TapeGhost(300, 1.5f);
            string f1 = Path.Combine(dir, "ghost_a.bin");
            TapeCheck(g.WriteTo(f1) && File.ReadAllBytes(f1).AsSpan().SequenceEqual(g.ToBytes()), $"tape 22: the managed ghost serialisation is byte for byte what the Godot writer writes ({new FileInfo(f1).Length} bytes, 300 samples)");
            var back = TapeLoadGhost(f1);
            TapeCheck(back != null && TapeSeams.Fingerprint(back) == TapeSeams.Fingerprint(g), "tape 22: and the ghost loads back from it with the same fingerprint");
            WorldTape tape; using (var w = TapeWorld.Build(2, 3, false)) tape = TapeRecord(w, 3f, fp: TapeSeams.Fingerprint(g));
            string gp = Path.Combine(dir, "ghost_w.bin"), tp = Path.Combine(dir, "tape_w.bin");
            var store = new WorldTapeStorage(TapeSeams.EncodeTape, TapeSeams.DecodeTape);
            var bytes = g.ToBytes(); ulong fp = TapeSeams.Fingerprint(g);
            int threadsBefore = System.Environment.CurrentManagedThreadId; int onThread = -1;
            var res = System.Threading.Tasks.Task.Run(() => { onThread = System.Environment.CurrentManagedThreadId; return store.SavePairBytes(gp, tp, bytes, fp, tape, WorldTapeRealOps.Instance); }).GetAwaiter().GetResult();
            TapeCheck(onThread != threadsBefore && res.Whole && File.Exists(gp) && File.Exists(tp) && !File.Exists(gp + ".tmp") && !File.Exists(tp + ".tmp"), $"tape 22: a worker thread saved the pair from plain data ({res}, thread {onThread} vs main {threadsBefore}), no tmp file left");
            TapeCheck(TapeSeams.LoadTape(tp, TapeLoadGhost(gp)) != null && File.ReadAllBytes(gp).AsSpan().SequenceEqual(bytes), "tape 22: the pair on disk is the one given: the ghost file is its bytes and the tape loads with that ghost");
            // a wrong fingerprint is refused and leaves the old pair
            var oldG = File.ReadAllBytes(gp);
            var refused = System.Threading.Tasks.Task.Run(() => store.SavePairBytes(gp, tp, TapeGhost(300, 9f).ToBytes(), fp ^ 1, tape, WorldTapeRealOps.Instance)).GetAwaiter().GetResult();
            TapeCheck(refused.Refused && File.ReadAllBytes(gp).AsSpan().SequenceEqual(oldG) && !File.Exists(gp + ".tmp") && !File.Exists(tp + ".tmp"), $"tape 22: a pair whose tape belongs to another ghost is refused on the worker, the old pair stays, no tmp file ({refused})");
        }
        finally { TapeRmDir(dir); }
    }
}
