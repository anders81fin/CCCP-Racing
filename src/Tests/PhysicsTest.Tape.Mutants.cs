using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

// Deliberately WRONG variants of the reference model (section 10: mutation check). Each is plugged into the seams and the same test code must fail.

/// <summary>Stand-in for the game scene: a chain type reading it must be caught by the reflection check of test 17.</summary>
static class FakeRaceScene { public static bool IsRecordLap = false; }

class MutCoarseRecorder : RefRecorder { protected override float PosScale => 100f; }                      // 1 cm instead of 1 mm

class MutInferSpinRecorder : RefRecorder                                                                  // angle guessed from the sparse rotation
{
    protected override float AngleOf(Node3D n, ITapeSpinner s, float prev)
    {
        var q = n.Basis.GetRotationQuaternion(); float a = 2f * MathF.Atan2(q.X * s.Axis.X + q.Y * s.Axis.Y + q.Z * s.Axis.Z, q.W);
        return prev + Mathf.Wrap(a - prev, -MathF.PI, MathF.PI);
    }
}

class MutSeekPlayer : RefPlayer                                                                           // a seek that ignores keyframes
{
    public MutSeekPlayer(WorldTape t, IWorldTapeIndex i, IWorldTapeSink s) : base(t, i, s) { }
    public override void Seek(float clock)
    {
        int i = SampleIndex(clock); I = i;
        ApplyDelta(i);
        Ev = 0; while (Ev < D.Events.Count && D.Events[Ev].Time <= clock) Ev++;
        Clock = clock; Has = true; PoseNodes(clock);
    }
}

class MutFreshEffectPlayer : RefPlayer                                                                    // restores an effect with a fresh life
{
    public MutFreshEffectPlayer(WorldTape t, IWorldTapeIndex i, IWorldTapeSink s) : base(t, i, s) { }
    protected override void RestoreEffect(in TapeEvent e, float age) => Sink.Effect(e, 0f);
}

class MutBrokenStickyPlayer : RefPlayer                                                                   // the first seek places the breakables, a wrap does not: what the pass before broke stays broken
{
    public MutBrokenStickyPlayer(WorldTape t, IWorldTapeIndex i, IWorldTapeSink s) : base(t, i, s) { }
    protected override void SetBroken(IReadOnlyCollection<int> broken) { if (!Has) base.SetBroken(broken); }
}

class MutNoClearPlayer : RefPlayer                                                                        // a seek that never clears the effects it started
{
    public MutNoClearPlayer(WorldTape t, IWorldTapeIndex i, IWorldTapeSink s) : base(t, i, s) { }
    protected override void ClearEffectsOnSeek() { }
}

class MutUnfilteredPlayer : RefPlayer                                                                     // events of an incompatible actor are not filtered
{
    public MutUnfilteredPlayer(WorldTape t, IWorldTapeIndex i, IWorldTapeSink s) : base(t, i, s) { }
    protected override bool EventCovered(in TapeEvent e) => true;
}

class MutDeleteSaver : RefSaver                                                                           // deletes the target before renaming
{
    protected override void Commit(ITapeFileOps ops, string tg, string gp, string tt, string tp) { Del(ops, tg, gp); ops.Step("ghost-replaced"); Del(ops, tt, tp); ops.Step("tape-replaced"); }
    static void Del(ITapeFileOps ops, string tmp, string final)
    {
        if (File.Exists(final)) { File.Delete(final); ops.Step("deleted"); Thread.Sleep(30); }
        ops.Move(tmp, final);
    }
}

class MutBakSaver : RefSaver                                                                              // .bak scheme
{
    protected override void Commit(ITapeFileOps ops, string tg, string gp, string tt, string tp) { Bak(ops, tg, gp); ops.Step("ghost-replaced"); Bak(ops, tt, tp); ops.Step("tape-replaced"); }
    static void Bak(ITapeFileOps ops, string tmp, string final)
    {
        bool had = File.Exists(final);
        if (had) { ops.Move(final, final + ".bak"); ops.Step("bak"); Thread.Sleep(30); }
        ops.Move(tmp, final);
        if (had) File.Delete(final + ".bak");
    }
}

class MutUnverifiedSaver : RefSaver                                                                       // the rename's failure is not noticed
{
    protected override void Commit(ITapeFileOps ops, string tg, string gp, string tt, string tp)
    {
        try { Put(ops, tg, gp); } catch { }
        try { Put(ops, tt, tp); } catch { }
    }
}

class MutFallbackSaver : RefSaver                                                                         // falls back to delete + move when Replace throws
{
    protected override void Commit(ITapeFileOps ops, string tg, string gp, string tt, string tp) { Fb(ops, tg, gp); Fb(ops, tt, tp); }
    static void Fb(ITapeFileOps ops, string tmp, string final)
    {
        try { Put(ops, tmp, final); } catch { File.Delete(final); ops.Move(tmp, final); }
    }
}

class MutSceneRecorder : RefRecorder                                                                      // records only on a "record lap": needs the scene's flag
{
    public override void Record(float lapClock) { if (FakeRaceScene.IsRecordLap) base.Record(lapClock); }
}

class MutSceneReadingRecorder : RefRecorder                                                               // behaves, but reads the scene flag
{
    public override void Record(float lapClock) { if (FakeRaceScene.IsRecordLap) Unregistered--; base.Record(lapClock); }
}

public partial class PhysicsTest
{
    static ulong TapeFingerprintEvery10th(Ghost g)
    {
        var h = TapeHash.Start().Add(g.Count).Add(g.LapTime);
        for (int i = 0; i < g.Count; i += 10) { var p = g.PositionAt(i); var q = g.RotationAt(i); h = h.Add(p.X).Add(p.Y).Add(p.Z).Add(q.X).Add(q.Y).Add(q.Z).Add(q.W); }
        return h.Value;
    }

    /// <summary>The whole reference pipeline (recorder, codec, player, store), round-trip compatible: the base of every mutant that replaces a recorder, the fingerprint or the saver.</summary>
    static void TapeRefPipeline()
    {
        TapeSeams.NewRecorder = () => new RefRecorder(); TapeSeams.UnregisteredSpinCount = r => ((RefRecorder)r).Unregistered;
        TapeSeams.EncodeTape = RefFiles.Encode; TapeSeams.DecodeTape = RefFiles.Decode;
        TapeSeams.NewPlayer = (t, i, s) => new RefPlayer(t, i, s);
        TapeSeams.SavePair = new RefSaver().Save; TapeSeams.LoadTape = RefFiles.Load;
    }

    /// <summary>The real recorder and codec with the reference player behind the bridge (the base of every player mutant).</summary>
    static void TapeBridgePipeline() => TapeSeams.NewPlayer = (t, i, s) => new RefPlayer(TapeT1Bridge.ForReference(t), i, s);

    void TapeMutationTests()
    {
        GD.Print("World tape mutation checks");
        InstallRealTape();
        var tests = new Dictionary<string, Action>
        {
            ["1"] = TapeT1, ["2"] = TapeT2, ["3"] = TapeT3, ["4"] = TapeT4, ["5"] = TapeT5, ["6"] = TapeT6, ["14a"] = TapeT14a, ["16"] = TapeT16, ["17"] = TapeT17, ["18"] = TapeT18, ["18b"] = TapeT18b, ["19"] = TapeT19, ["20"] = TapeT20, ["20b"] = TapeT20b, ["23"] = TapeT23,
        };
        // Name, test, the VALID baseline pipeline (the unmutated version of what is replaced: it must pass every check of the test), the mutation on top of it,
        // and the check the mutant must fail FIRST (a substring of that check's text). Any other first failure, or an exception, is "detected for the wrong reason".
        Action refP = TapeRefPipeline, bridgeP = TapeBridgePipeline, realP = () => { };
        var mutants = new (string Name, string Test, Action Base, Action Mut, string Expect)[]
        {
            ("coarse-position-quantisation (1 cm)", "1", refP, () => TapeSeams.NewRecorder = () => new MutCoarseRecorder(), "tape 1: positions within 1 mm"),
            ("spinner-angle-inferred-from-sparse-poses", "2", refP, () => TapeSeams.NewRecorder = () => new MutInferSpinRecorder(), "tape 2: the decoded angle never reverses"),
            ("seek-ignores-keyframes", "3", bridgeP, () => TapeSeams.NewPlayer = (t, i, s) => new MutSeekPlayer(TapeT1Bridge.ForReference(t), i, s), "tape 3: seek equals sequential apply"),
            ("effect-restored-with-fresh-life", "4", bridgeP, () => TapeSeams.NewPlayer = (t, i, s) => new MutFreshEffectPlayer(TapeT1Bridge.ForReference(t), i, s), "gives the sink the effect once with age = clock - time"),
            ("seek-never-clears-effects", "4", bridgeP, () => TapeSeams.NewPlayer = (t, i, s) => new MutNoClearPlayer(TapeT1Bridge.ForReference(t), i, s), "zero stray effects after 10 loops"),
            ("events-of-incompatible-actor-not-filtered", "5", bridgeP, () => TapeSeams.NewPlayer = (t, i, s) => new MutUnfilteredPlayer(TapeT1Bridge.ForReference(t), i, s), "the changed actor's events and instances are dropped"),
            ("fingerprint-over-every-10th-ghost-sample", "6", refP, () => TapeSeams.Fingerprint = TapeFingerprintEvery10th, "a ghost whose last sample differs by one float drops the tape"),
            ("save-deletes-target-before-rename", "14a", refP, () => TapeSeams.SavePair = new MutDeleteSaver().Save, "the final paths hold a complete old or new file at every observation"),
            ("save-uses-bak-scheme", "14a", refP, () => TapeSeams.SavePair = new MutBakSaver().Save, "the final paths hold a complete old or new file at every observation"),
            ("rename-failure-not-verified", "16", refP, () => TapeSeams.SavePair = new MutUnverifiedSaver().Save, "when File.Replace throws the save is refused and the old pair is untouched"),
            ("fallback-delete-and-move-when-replace-throws", "16", refP, () => TapeSeams.SavePair = new MutFallbackSaver().Save, "when File.Replace throws the save is refused and the old pair is untouched"),
            ("seek-leaves-the-instances-it-made", "18", bridgeP, () => TapeSeams.NewPlayer = (t, i, s) => new MutKeepInstPlayer(TapeT1Bridge.ForReference(t), i, s), "tape 18: a wrap despawns each instance exactly once"),
            ("keyframe-forgets-the-instance-spawned-at-its-own-time", "18", refP, () => TapeSeams.NewRecorder = () => new MutKeyframeDropsSpawnRecorder(), "tape 18: a lap that starts with live instances has them in its first keyframe"),
            ("breakables-not-reset-on-wrap", "18b", bridgeP, () => TapeSeams.NewPlayer = (t, i, s) => new MutBrokenStickyPlayer(TapeT1Bridge.ForReference(t), i, s), "tape 18b: in each of 10 passes the breakable is not broken before a"),
            ("recorder-forgets-the-effects-alive-at-the-lap-start", "19", refP, () => TapeSeams.NewRecorder = () => new MutKeyframeDropsEffectRecorder(), "tape 19: a lap that starts with effects alive has them in its first keyframe"),
            ("seek-restores-the-settled-tree-whatever-the-age", "20", refP, () => TapeBreakAges = _ => null, "tape 20: a tree's ("),
            ("production-seek-sink-drops-the-clock (the one-argument call)", "20b", bridgeP, () => TapeChainDropsClock = true, "tape 20b: through the real player and sink"),
            ("recorder-needs-scene-flag-IsRecordLap (behaviour)", "17", refP, () => TapeSeams.NewRecorder = () => new MutSceneRecorder(), "a record-like span recorded its samples"),
            ("recorder-reads-scene-flag-IsRecordLap (reflection only)", "17", refP, () => TapeSeams.NewRecorder = () => new MutSceneReadingRecorder(), "references RaceScene"),
            ("async-snapshot-aliases-the-recorder-buffers", "23", realP, () => TapeMut = "alias", "tape 23a: the first saved pair's tape is the FIRST lap's"),
            ("async-next-save-does-not-wait-for-the-pending-one", "23", realP, () => TapeMut = "nowait", "tape 23d: the earlier save was waited for and applied first"),
            ("async-result-applied-before-the-file-is-written", "23", realP, () => TapeMut = "early", "tape 23a: while the worker is held nothing is applied"),
        };
        int problems = 0;
        foreach (var (name, test, baseline, mutate, expect) in mutants)
        {
            var saved = TapeSeams.Save();
            // 1. the baseline must pass every check of the test (a mutant means something only against a valid starting point)
            baseline(); _tapeMute = true; int before = _failures; _tapeFails.Clear();
            TapeRun("tape " + test, tests[test]);
            var baseFails = _tapeFails.ToList(); _failures = before; _tapeMute = false;
            TapeSeams.Restore(saved);
            if (baseFails.Count > 0) { problems++; GD.Print($"mutant {name}: BASELINE INVALID, the unmutated pipeline fails: {baseFails[0]}"); continue; }
            // 2. the mutant must fail the intended check first, with no exception
            baseline(); mutate(); _tapeMute = true; before = _failures; _tapeFails.Clear();
            TapeRun("tape " + test, tests[test]);
            var fails = _tapeFails.ToList(); _failures = before; _tapeMute = false;
            TapeSeams.Restore(saved); TapeBreakAges = a => a; TapeChainDropsClock = false; TapeMut = "";
            string actual = fails.FirstOrDefault(), thrown = fails.FirstOrDefault(f => f.Contains(" threw "));
            bool right = actual != null && actual.Contains(expect) && thrown == null;
            if (!right) problems++;
            string verdict = right ? "DETECTED" : actual == null ? "SURVIVED" : "detected for the wrong reason: " + name + ": " + (thrown ?? actual);
            GD.Print($"mutant {name} | expected: {expect} | actual first failing: {actual ?? "(none)"}{(thrown != null ? " | EXCEPTION: " + thrown : "")} | {verdict}");
        }
        GD.Print(problems == 0 ? "World tape mutation checks: every mutant detected (for the intended reason)" : $"World tape mutation checks: {problems} problem(s)");
        _failures += problems;
    }
}
