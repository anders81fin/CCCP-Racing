using System;
using System.Collections.Generic;
using System.Linq;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

// A seek keeps the dynamic instances the tape wants again (same id, kind and parent, a kind the sink calls recyclable) instead of freeing and rebuilding
// them (docs/replay-tape.md section 6). --tape-recycle-tests: the production WorldTapePlayer on the tape test world, a sink that recycles "hind" only and
// marks every node it hands out "dirty" so a kept node must be reset, against a plain fresh player + sink seeking to the same clock as the reference.
public partial class PhysicsTest
{
    /// <summary>The sink of the tape tests that also recycles (hind only); the dirt is what a kept model must not carry over.</summary>
    sealed class TapeRecycleSink : IWorldTapeSink, IWorldTapeSinkRecycle
    {
        public readonly TapeSink Inner;
        public int Recycled, Refused;
        public bool Legacy;      // never recycles: answers CanRecycle false
        public TapeRecycleSink(TapeWorld w) { Inner = new TapeSink { World = w }; }
        public Node3D Spawn(string kind, string parent, int instance) => Inner.Spawn(kind, parent, instance);
        public void Despawn(int instance) => Inner.Despawn(instance);
        public void Effect(in TapeEvent e, float age) => Inner.Effect(e, age);
        public void ClearEffects() => Inner.ClearEffects();
        public void SetBrokenBreakables(IReadOnlyCollection<int> ids) => Inner.SetBrokenBreakables(ids);
        public bool CanRecycle(string kind) => !Legacy && kind == "hind";
        public bool Recycle(int instance, string kind, string parent)
        {
            if (!CanRecycle(kind) || !Inner.Inst.TryGetValue(instance, out var n) || n.GetParent() != Inner.World.Root(parent)) { Refused++; return false; }
            n.SetMeta("dirty", false); n.Transform = Transform3D.Identity; n.Visible = true;      // (a real sink resets what the tape does not write)
            Recycled++; return true;
        }
    }

    static List<(float, Action<IWorldTapeRecorder>)> TapeRecycleScript(TapeWorld w) => new()
    {
        TapeSpawn(w, 2f, TapeWorld.Key(1), 1, "hind"), TapeDespawn(w, 20f, TapeWorld.Key(1), 1),
        TapeSpawn(w, 6f, TapeWorld.Key(2), 2, "hind"), TapeDespawn(w, 25f, TapeWorld.Key(2), 2),
        TapeSpawn(w, 10f, TapeWorld.Key(0), 3, "missile"), TapeDespawn(w, 28f, TapeWorld.Key(0), 3),
        TapeSpawn(w, 22f, TapeWorld.Key(1), 4, "hind"), TapeDespawn(w, 29f, TapeWorld.Key(1), 4),
        TapeFx_(12f, TapeWorld.Key(0), TapeEffect.Smoke, 6f),
    };

    void TapeRecycleTests()
    {
        GD.Print("World tape recycle tests");
        InstallRealTape();
        WorldTape tape; using (var w0 = TapeWorld.Build(4, 5)) tape = TapeRecord(w0, 30f, TapeRecycleScript(w0));
        TapeRun("recycle 1", () => TapeRecycleSequence(tape));
        TapeRun("recycle 2", () => TapeRecycleLegacyAndIncompatible(tape));
        TapeRun("recycle 3", () => TapeRecycleCycles(tape));
        TapeRun("recycle 4", () => TapeRecycleCleanup(tape));
        TapeRun("recycle 5", TapeRecycleRealHind);
        GD.Print($"World tape recycle tests: {(_failures == 0 ? "all ok" : _failures + " FAIL")}");
    }

    static string TapeRecycleReference(WorldTape tape, float t, out TapeSnap snap)
    {
        var (w, s, p) = TapePlay(tape, 4, 5); using var _w = w;
        p.Seek(t); snap = TapeTake(w, s); ((WorldTapePlayer)p).Release();
        return null;
    }

    int TapeSceneNodeCount(TapeWorld w) { int n = 0; void Walk(Node x) { n++; foreach (var c in x.GetChildren()) Walk(c); } foreach (var (_, r) in w.Actors) Walk(r); return n; }

    void TapeRecycleSequence(WorldTape tape)
    {
        var w = TapeWorld.Build(4, 5); using var _w = w;
        var sink = new TapeRecycleSink(w);
        var p = (WorldTapePlayer)TapeSeams.NewPlayer(tape, w.Index, sink);
        // (time, what the step is, expected kept hinds, expected new builds)
        (float T, string What, int Kept, int Built)[] steps =
        {
            (4f, "first seek: nothing to keep, hind 1 built", 0, 1),
            (5f, "same spawn set: hind 1 is kept", 1, 0),
            (8f, "crossing a spawn: hind 1 kept, hind 2 built", 1, 1),
            (12f, "hinds 1 and 2 kept, the missile (not recyclable) built", 2, 1),
            (11f, "backward within the set: both hinds kept, the missile rebuilt", 2, 1),
            (21f, "crossing the despawn of hind 1: hind 2 kept, missile rebuilt", 1, 1),
            (23f, "crossing a spawn: hind 2 kept, hind 4 and the missile built", 1, 2),
            (3f, "backward past hinds 2 and 4: hind 1 built anew", 0, 1),
            (0f, "the loop start: nothing is live", 0, 0),
        };
        // the expectation of live ids comes from the reference
        foreach (var (t, what, kept, built) in steps)
        {
            foreach (var n in w.Actors.SelectMany(a => a.Root.GetChildren().OfType<Node3D>()).Where(n => n.HasMeta(WorldTapeConst.KindMeta))) n.SetMeta("dirty", true);      // (stale state a kept node must lose)
            int spawns0 = sink.Inner.Spawns, rec0 = sink.Recycled;
            p.Seek(t);
            TapeRecycleReference(tape, t, out var want);
            var got = TapeTake(w, sink.Inner);
            string diff = TapeDiff(want, got);
            if (diff == null && !p.LiveInstances.Keys.OrderBy(i => i).SequenceEqual(want.Inst.OrderBy(i => i))) diff = "live instances differ";
            TapeCheck(diff == null, $"recycle 1 @{t}: {what}: the world equals a fresh seek's ({diff ?? "ok"})");
            TapeCheck(sink.Recycled - rec0 == kept && sink.Inner.Spawns - spawns0 == built, $"recycle 1 @{t}: kept {sink.Recycled - rec0} (want {kept}), built {sink.Inner.Spawns - spawns0} (want {built})");
            bool clean = true;
            foreach (var (id, node) in sink.Inner.Inst) if (node.HasMeta("dirty") && node.GetMeta("dirty").AsBool()) clean = false;
            TapeCheck(clean, $"recycle 1 @{t}: no model carries the dirt of before the seek");
        }
        p.Release();
        TapeCheck(sink.Inner.Inst.Count == 0, "recycle 1: release frees every instance");
    }

    void TapeRecycleLegacyAndIncompatible(WorldTape tape)
    {
        // a sink that never recycles (the legacy behaviour): every seek rebuilds, results unchanged
        var w = TapeWorld.Build(4, 5); using var _w = w;
        var legacy = new TapeRecycleSink(w) { Legacy = true };
        var p = (WorldTapePlayer)TapeSeams.NewPlayer(tape, w.Index, legacy);
        p.Seek(8f); int s0 = legacy.Inner.Spawns; p.Seek(9f);
        TapeCheck(legacy.Recycled == 0 && legacy.Inner.Spawns - s0 == 2, "recycle 2: a sink that does not recycle rebuilds both hinds on the second seek");
        TapeRecycleReference(tape, 9f, out var want);
        TapeCheck(TapeDiff(want, TapeTake(w, legacy.Inner)) == null, "recycle 2: legacy result equals the reference");
        p.Release();
        // a plain sink without the interface at all
        var (w2, s2, p2) = TapePlay(tape, 4, 5); using var _w2 = w2;
        p2.Seek(8f); int sp0 = s2.Spawns; p2.Seek(9f);
        TapeCheck(s2.Spawns - sp0 == 2, "recycle 2: a sink without the interface rebuilds (no exception)");
        ((WorldTapePlayer)p2).Release();
        // the sink refuses (its node moved to another parent): the player builds anew and the result is still right
        var w3 = TapeWorld.Build(4, 5); using var _w3 = w3;
        var s3 = new TapeRecycleSink(w3);
        var p3 = (WorldTapePlayer)TapeSeams.NewPlayer(tape, w3.Index, s3);
        p3.Seek(8f);
        var hind = s3.Inner.Inst[1]; hind.GetParent().RemoveChild(hind); w3.Root(TapeWorld.Key(3)).AddChild(hind);
        int b0 = s3.Inner.Spawns; p3.Seek(9f);
        TapeCheck(s3.Refused >= 1 && s3.Inner.Spawns - b0 >= 1, "recycle 2: a sink that refuses makes the player build a new instance");
        TapeRecycleReference(tape, 9f, out var want3);
        TapeCheck(TapeDiff(want3, TapeTake(w3, s3.Inner)) == null, "recycle 2: the refused instance's replacement is right");
        p3.Release();
    }

    void TapeRecycleCycles(WorldTape tape)
    {
        var w = TapeWorld.Build(4, 5); using var _w = w;
        var sink = new TapeRecycleSink(w);
        var p = (WorldTapePlayer)TapeSeams.NewPlayer(tape, w.Index, sink);
        p.Seek(8f);
        int baseNodes = TapeSceneNodeCount(w), baseIdx = w.Index.Count;
        int maxNodes = baseNodes, maxIdx = baseIdx, wrongs = 0;
        // a loop of 300 jumps: 8 s -> 24 s -> 12 s ... the live set 1,2 / 2,4 / 1,2,3 changes, nodes must plateau
        float[] cycle = { 8f, 24f, 12f, 0f, 9f, 21f };
        for (int k = 0; k < 300; k++)
        {
            float t = cycle[k % cycle.Length];
            p.Seek(t);
            maxNodes = Math.Max(maxNodes, TapeSceneNodeCount(w)); maxIdx = Math.Max(maxIdx, w.Index.Count);
            if (k % 25 == 0) { TapeRecycleReference(tape, t, out var want); if (TapeDiff(want, TapeTake(w, sink.Inner)) != null) wrongs++; }
        }
        TapeCheck(wrongs == 0, "recycle 3: 12 sampled seeks of 300 equal a fresh seek");
        TapeCheck(maxNodes <= baseNodes + 40 && maxIdx <= baseIdx + 40, $"recycle 3: plateau over 300 seeks (nodes max {maxNodes} vs start {baseNodes}, index max {maxIdx} vs {baseIdx})");
        TapeCheck(sink.Inner.Spawns < 300 * 2, $"recycle 3: far fewer builds than seeks x hinds (builds {sink.Inner.Spawns}, kept {sink.Recycled})");
        p.Release();
        TapeCheck(sink.Inner.Inst.Count == 0, "recycle 3: nothing left after release");
    }

    /// <summary>The production Hind replay aircraft: after a wreck-and-noise state, ResetReplayAircraft returns what the tape writes and the model's own state to a fresh one's.</summary>
    void TapeRecycleRealHind()
    {
        var host = new Node3D(); AddChild(host);
        var fresh = Track.HindSorties.BuildReplayAircraft("24"); var used = Track.HindSorties.BuildReplayAircraft("24");
        host.AddChild(fresh); host.AddChild(used);
        var fx = used.GetNode<Track.HindReplayFx>("TapeState");
        fx.State = 3; used.Scale = new Vector3(1, .55f, 1); used.Visible = false;
        var eng = used.GetChildren().OfType<Audio.RotorSound>().First(); eng.Level = .9f; eng.StreamPaused = false;
        var gun = used.GetChildren().OfType<Audio.HindGunSound>().First(); gun.Firing = true; used.GetNode<MeshInstance3D>("Gun/Flash").Visible = true;
        TapeCheck(Track.HindSorties.ResetReplayAircraft(used), "recycle 5: a replay aircraft can be reset");
        var fe = fresh.GetChildren().OfType<Audio.RotorSound>().First(); var fg = fresh.GetChildren().OfType<Audio.HindGunSound>().First();
        TapeCheck(fx.State == 0 && used.Scale == fresh.Scale && used.Visible == fresh.Visible, "recycle 5: state, scale and visibility are a new aircraft's");
        TapeCheck(eng.Level == 0 && eng.StreamPaused == fe.StreamPaused && gun.Firing == fg.Firing && !used.GetNode<MeshInstance3D>("Gun/Flash").Visible, "recycle 5: engine level, gun and flash are a new aircraft's (silent, as built)");
        var priv = typeof(Audio.RotorSound).GetField("_level", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        TapeCheck(priv != null && (float)priv.GetValue(eng) == 0f, "recycle 5: the engine sound's smoothing is back at zero");
        var junk = new Node3D(); TapeCheck(!Track.HindSorties.ResetReplayAircraft(junk), "recycle 5: a node that is not a replay aircraft is refused"); junk.Free();
        host.Free();      // (at once: a queued free never runs before the test process quits and shows as leaked instances)
    }

    void TapeRecycleCleanup(WorldTape tape)
    {
        var w = TapeWorld.Build(4, 5); using var _w = w;
        int clean = TapeSceneNodeCount(w), idx0 = w.Index.Count;
        var sink = new TapeRecycleSink(w);
        var p = (WorldTapePlayer)TapeSeams.NewPlayer(tape, w.Index, sink);
        p.Seek(8f); p.Seek(9f); p.Seek(0f);
        TapeCheck(p.LiveInstances.Count == 0 && sink.Inner.Inst.Count == 0 && TapeSceneNodeCount(w) == clean, "recycle 4: a seek to the start frees what the previous seek kept");
        p.Seek(12f); p.Release();
        TapeCheck(TapeSceneNodeCount(w) == clean && w.Index.Count == idx0, "recycle 4: after release the world is as built (nodes and index)");
        // seek, then a throw inside the sink while kept instances exist must not leave kept nodes registered
        p.Seek(9f);
        TapeCheck(p.LiveInstances.Count == 2, "recycle 4: a released player seeks again");
        p.Release();
    }
}
