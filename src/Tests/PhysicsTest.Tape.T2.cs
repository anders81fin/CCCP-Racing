using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

/// <summary>For the forced-kill test: the child process blocks inside the save at a named step and waits to be killed.</summary>
class TapeKillOps : ITapeFileOps
{
    readonly string _stop, _ready;
    public TapeKillOps(string stop, string ready) { _stop = stop; _ready = ready; }
    public void Step(string name) { if (name == _stop) { File.WriteAllText(_ready, "1"); Thread.Sleep(Timeout.Infinite); } }
    public void Replace(string tmp, string final) => WorldTapeRealOps.Instance.Replace(tmp, final);
    public void Move(string tmp, string final) => WorldTapeRealOps.Instance.Move(tmp, final);
    public void Warn(string message) { }
}

// T2's own storage tests, run from the install hook (the T5a test list cannot be edited). They use the real file system, the real Ghost.Save/Load and the real loader.
public partial class PhysicsTest
{
    partial void TapeStorageTests();

    partial void TapeStorageTests()
    {
        var args = OS.GetCmdlineUserArgs();
        if (Array.IndexOf(args, "--tape-t2-child") >= 0) { TapeT2Child(args); return; }
        if (Array.IndexOf(args, "--tape-mutation-tests") >= 0) return;
        GD.Print("World tape storage tests (T2)");
        TapeRun("tape T2a", TapeT2Interruptions); TapeRun("tape T2a-kill", TapeT2Kills); TapeRun("tape T2b", TapeT2RecordStore); TapeRun("tape T2c", TapeT2Golden);
    }

    // ---- shared ----
    static Ghost T2Ghost(string path) => Ghost.Load(path, TapeParams, null);
    static bool T2SameTape(WorldTape a, WorldTape b) => a != null && b != null && a.GhostFingerprint == b.GhostFingerprint && a.SampleCount == b.SampleCount && a.LapTime == b.LapTime && a.Payload.AsSpan().SequenceEqual(b.Payload);

    /// <summary>Re-loads the final files with the real loaders and says what they hold: ghost "old"/"new"/"?" and tape "old"/"new"/"none"/"?".</summary>
    (string Ghost, string Tape) T2State(PairFx f)
    {
        var g = T2Ghost(f.Gp);
        string gs = g == null ? "?" : TapeSeams.Fingerprint(g) == TapeSeams.Fingerprint(f.G0) && g.Count == f.G0.Count ? "old" : TapeSeams.Fingerprint(g) == TapeSeams.Fingerprint(f.G1) && g.Count == f.G1.Count ? "new" : "?";
        if (g == null) return (gs, "?");
        var t = TapeSeams.LoadTape(f.Tp, g);
        return (gs, t == null ? "none" : T2SameTape(t, f.T0) ? "old" : T2SameTape(t, f.T1) ? "new" : "?");
    }

    // ---- (a) injected stops, re-loaded with the real loaders ----
    void TapeT2Interruptions()
    {
        var cases = new (string Name, string Stop, int ThrowReplace, string Ghost, string Tape)[]
        {
            ("no stop", null, 0, "new", "new"), ("stop after the tmp files", "tmp-written", 0, "old", "old"),
            ("stop between the two replacements", "ghost-replaced", 0, "new", "none"), ("stop in the second replacement", null, 2, "new", "none"),
            ("stop after verification", "verified", 0, "old", "old"),
        };
        foreach (var (name, stop, thr, wantG, wantT) in cases)
        {
            var f = TapePair(true);
            try
            {
                var ops = new TapeInjectOps { StopAt = stop, ThrowOnReplaceNo = thr };
                bool ok = TapeSeams.SavePair(f.Gp, f.Tp, f.G1, f.T1, ops).Whole;
                var (gs, ts) = T2State(f);
                TapeCheck(gs == wantG && ts == wantT, $"tape T2a ({name}): the final files re-load with the real Ghost.Load and tape loader as ghost {wantG} / tape {wantT} (got {gs} / {ts})");
                TapeCheck(Tmps(f.Dir).Length == 0 && ok == (wantT == "new") && (wantT == "new" || ops.Warnings.Count == 1), $"tape T2a ({name}): no .tmp remains, result {ok}, warnings {ops.Warnings.Count}");
            }
            finally { TapeRmDir(f.Dir); }
        }
        var f2 = TapePair(false);
        try
        {
            bool ok = TapeSeams.SavePair(f2.Gp, f2.Tp, f2.G1, f2.T1, TapeSeams.RealOps()).Whole;
            var g = T2Ghost(f2.Gp);
            TapeCheck(ok && g != null && T2SameTape(TapeSeams.LoadTape(f2.Tp, g), f2.T1), "tape T2a (no earlier files): the pair is created and re-loads");
        }
        finally { TapeRmDir(f2.Dir); }
    }

    // ---- (a) forced kill: a child Godot process is really killed inside the save ----
    void TapeT2Child(string[] a)
    {
        int i = Array.IndexOf(a, "--tape-t2-child"); string dir = a[i + 1], stop = a[i + 2];
        var ng = T2Ghost(Path.Combine(dir, "new", "ghost_t.bin")); var nt = TapeSeams.DecodeTape(File.ReadAllBytes(Path.Combine(dir, "new", "tape_t.bin")));
        TapeSeams.SavePair(Path.Combine(dir, "ghost_t.bin"), Path.Combine(dir, "tape_t.bin"), ng, nt, new TapeKillOps(stop, Path.Combine(dir, "ready")));
        File.WriteAllText(Path.Combine(dir, "done"), "1");      // only reached when the stop step never came
    }

    void TapeT2Kills()
    {
        foreach (var (stop, wantG, wantT, tmps) in new[] { ("tmp-written", "old", "old", 2), ("ghost-replaced", "new", "none", 1) })
        {
            var f = TapePair(true);
            try
            {
                Directory.CreateDirectory(Path.Combine(f.Dir, "new"));
                TapeCheck(TapeSeams.SavePair(Path.Combine(f.Dir, "new", "ghost_t.bin"), Path.Combine(f.Dir, "new", "tape_t.bin"), f.G1, f.T1, TapeSeams.RealOps()).Whole, $"tape T2a-kill ({stop}): setup, the new pair is on disk");
                var psi = new ProcessStartInfo(OS.GetExecutablePath()) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var s in new[] { "--headless", "--path", ProjectSettings.GlobalizePath("res://"), "res://scenes/tests/PhysicsTest.tscn", "--", "--tape-tests", "--tape-t2-child", f.Dir, stop }) psi.ArgumentList.Add(s);
                Process p;
                try { p = Process.Start(psi); } catch (Exception ex) { TapeInfo($"tape T2a-kill ({stop}): could not start a child process ({ex.Message}); the forced-kill test did not run"); continue; }
                p.OutputDataReceived += (_, _) => { }; p.ErrorDataReceived += (_, _) => { }; p.BeginOutputReadLine(); p.BeginErrorReadLine();
                string ready = Path.Combine(f.Dir, "ready"); var sw = Stopwatch.StartNew();
                while (!File.Exists(ready) && !p.HasExited && sw.Elapsed.TotalSeconds < 90) Thread.Sleep(50);
                bool reached = File.Exists(ready);
                if (!p.HasExited) p.Kill(true);
                p.WaitForExit();
                TapeCheck(reached && !File.Exists(Path.Combine(f.Dir, "done")), $"tape T2a-kill ({stop}): the child reached the step and was killed there ({sw.Elapsed.TotalSeconds:F1} s)");
                var (gs, ts) = T2State(f);
                TapeCheck(gs == wantG && ts == wantT, $"tape T2a-kill ({stop}): after the kill the final files re-load as ghost {wantG} / tape {wantT} (got {gs} / {ts})");
                TapeCheck(Tmps(f.Dir).Length == tmps, $"tape T2a-kill ({stop}): the kill left {Tmps(f.Dir).Length} .tmp file(s) (expected {tmps})");
                int removed = WorldTapeFile.CleanStale(f.Dir); var (gs2, ts2) = T2State(f);
                TapeCheck(removed == tmps && Tmps(f.Dir).Length == 0 && gs2 == wantG && ts2 == wantT, $"tape T2a-kill ({stop}): cleanup removes the {removed} stale file(s), nothing is restored, the ghost still loads");
            }
            finally { TapeRmDir(f.Dir); }
        }
    }

    // ---- (b) a real RecordStore directory ----
    void TapeT2RecordStore()
    {
        var dir = TapeTmpDir() + "/";
        try
        {
            var f = TapePair(false); TapeRmDir(f.Dir);
            var rs = new RecordStore(dir, "ilyich", "sigA");
            TapeCheck(rs.TapeForSet(0) == dir + "tape_ilyich.bin" && rs.TapeForSet(1) == dir + "tape_ilyich_stakhanov.bin" && rs.TapeForSet(2) == dir + "tape_ilyich_diesel.bin" && rs.TapeForSet(3) == dir + "tape_ilyich_rocket.bin", "tape T2b: TapeForSet is GhostForSet with tape_ for ghost_ in every set");
            for (int set = 0; set < RecordStore.Sets; set++)
                TapeCheck(TapeSeams.SavePair(rs.GhostForSet(set), rs.TapeForSet(set), f.G1, f.T1, TapeSeams.RealOps()).Whole, $"tape T2b: set {set} saves a pair into the record directory");
            rs.WriteSignature(); File.WriteAllText(rs.BoardPath, "board");
            var g = T2Ghost(rs.GhostForSet(1));
            TapeCheck(g != null && T2SameTape(TapeSeams.LoadTape(rs.TapeForSet(1), g), f.T1), "tape T2b: the saved pair of a set re-loads from the record directory");

            // stale tmp files are removed when the records are opened, records are not touched
            File.WriteAllBytes(rs.GhostPath + ".tmp", new byte[] { 1 }); File.WriteAllBytes(rs.TapeForSet(0) + ".tmp", new byte[] { 2 }); File.WriteAllBytes(dir + "settings.tmp", new byte[] { 3 });
            var before = File.ReadAllBytes(rs.GhostPath);
            rs = new RecordStore(dir, "ilyich", "sigA");
            TapeCheck(!File.Exists(rs.GhostPath + ".tmp") && !File.Exists(rs.TapeForSet(0) + ".tmp") && File.Exists(dir + "settings.tmp") && File.ReadAllBytes(rs.GhostPath).AsSpan().SequenceEqual(before), "tape T2b: opening the records deletes the stale ghost and tape .tmp files and nothing else");

            // a changed road: the tape is archived with its ghost
            var moved = new RecordStore(dir, "ilyich", "sigB").ArchiveIfChanged();
            bool allThere = Enumerable.Range(0, RecordStore.Sets).All(s => !File.Exists(rs.TapeForSet(s)) && !File.Exists(rs.GhostForSet(s)));
            TapeCheck(allThere && File.Exists(dir + "tape_ilyich_v02.bin") && File.Exists(dir + "ghost_ilyich_v02.bin") && File.Exists(dir + "tape_ilyich_stakhanov_v02.bin") && File.Exists(dir + "tape_ilyich_rocket_v02.bin") && moved.Count == 4 + 4 + 1 + 1, $"tape T2b: a changed signature archives each tape with its ghost ({moved.Count} files moved)");
            var ag = T2Ghost(dir + "ghost_ilyich_v02.bin");
            TapeCheck(ag != null && T2SameTape(TapeSeams.LoadTape(dir + "tape_ilyich_v02.bin", ag), f.T1), "tape T2b: the archived tape still pairs with the archived ghost");

            // records from before the two record sets: ghost, board and tape go to _v03
            var dir2 = TapeTmpDir() + "/";
            try
            {
                var rs2 = new RecordStore(dir2, "ilyich", "sigA");
                TapeSeams.SavePair(rs2.GhostPath, rs2.TapeForSet(0), f.G1, f.T1, TapeSeams.RealOps()); File.WriteAllText(rs2.BoardPath, "board");
                rs2.ArchiveOldLayout();
                TapeCheck(File.Exists(dir2 + "tape_ilyich_v03.bin") && File.Exists(dir2 + "ghost_ilyich_v03.bin") && !File.Exists(rs2.TapeForSet(0)), "tape T2b: the old-layout archive moves the tape with the ghost");
            }
            finally { TapeRmDir(dir2.TrimEnd('/')); }
        }
        finally { TapeRmDir(dir.TrimEnd('/')); }
    }

    // ---- (c) an old ghost file ----
    // Captured from the unchanged Ghost.Save (before the atomic change): TapeGhost(3), then Save. Count 3, LapTime 0.1, fingerprint below.
    const string GoldenGhostB64 = "Q0NDM83MzD0DAAAAAAAAAM3MTD8AAAAAAAAAAAAAAAAAAAAAAACAPwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAGFEAAAAAAAAAAAAAAAAAAAAAAAAAAABAD0K1z4AAAAAAAAAAAAAPQrXPgAAAAAAAAAAAAA9Ctc+AAAAAAAAAAAAAD0K1z4AAAAAAAAAAAAAAACAvwAAAD/NzEw/GViZPgAAAADe1qM7AAAAAC7/fz8AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABhRAAAAAAAAAAAAAAAAAAAAAAAAAAAAQA9Ctc+AAAAAAAAAAAAAD0K1z4AAAAAAAAAAAAAPQrXPgAAAAAAAAAAAAA9Ctc+AAAAAAAAAAAAAAAAgL8AAIA/zcxMP/uTGD8AAAAAWNYjPAAAAAC5/H8/AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAYUQAAAAAAAAAAAAAAAAAAAAAAAAAAAEAPQrXPgAAAAAAAAAAAAA9Ctc+AAAAAAAAAAAAAD0K1z4AAAAAAAAAAAAAPQrXPgAAAAAAAAAAAAAAAIC/";
    const ulong GoldenFingerprint = 15036231056290961529UL;

    void TapeT2Golden()
    {
        var dir = TapeTmpDir();
        try
        {
            var bytes = Convert.FromBase64String(GoldenGhostB64); var path = Path.Combine(dir, "ghost_old.bin");
            File.WriteAllBytes(path, bytes);
            var g = T2Ghost(path);
            TapeCheck(g != null && g.Count == 3 && TapeSeams.Fingerprint(g) == GoldenFingerprint, $"tape T2c: an old ghost file (written before the atomic Save) still loads and fingerprints identically (count {g?.Count})");
            if (g == null) return;
            var again = Path.Combine(dir, "ghost_again.bin"); g.Save(again);
            TapeCheck(File.ReadAllBytes(again).AsSpan().SequenceEqual(bytes) && Tmps(dir).Length == 0, "tape T2c: Ghost.Save writes the same bytes as before and leaves no .tmp");
            g.Save(path);
            TapeCheck(File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes) && Tmps(dir).Length == 0, "tape T2c: saving over an existing ghost replaces it atomically with the same bytes");
        }
        finally { TapeRmDir(dir); }
    }
}
