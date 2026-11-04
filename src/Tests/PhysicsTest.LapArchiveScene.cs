using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CCCPRacing.Race;
using CCCPRacing.Replay;
using Godot;

namespace CCCPRacing.Tests;

// --lap-archive-scene-tests: the player lap archive (phase 1) in the REAL game. Every case starts one headless child game (autodriven, its own --data-dir, XDG dirs under
// /tmp/cccp-lapark-scene-<stamp>/) and judges the child's exit, the explicit "lap archive:" marker lines and the files / LapArchive API of what it left. It never judges by the
// absence of an "ERROR:" line: release 0.76 has an intermittent shutdown-only "ERROR: 1 resources still in use at exit" (radio resource), which is only counted and reported as [info].
// Not covered here (no cheap way to run them, and not faked): LAN races, the driving schools, trailer / tour / preview runs and --no-record runs are excluded by the eligibility expression
// in RaceScene.LapArchive.cs only. Logs of every child: media/lap-archive-v1/scene-<stamp>/.
public partial class PhysicsTest
{
    sealed record SceneLapCap(string Track, int Set, string Kind, float Secs, string Id, bool Kept, float Ms, bool Tape, int Line);

    static readonly Regex SceneLapCapRx = new(@"^lap archive: (\S+) set (\d+) (Normal|Record|Void) (\d+[.,]\d+) s: (?:kept (\S+)|refused: .*), main thread (\d+[.,]\d+) ms, tape (yes|no)", RegexOptions.Compiled);

    string _lapSceneRoot, _lapSceneMedia;
    int _lapShutdownErrors;
    readonly List<float> _lapMsCold = new(), _lapMsWarm = new();      // the first capture of a child process (JIT of the capture path) and the later ones

    static float LapF(string s) => float.Parse(s.Replace(',', '.'), CultureInfo.InvariantCulture);

    static List<SceneLapCap> SceneLapCaps(TapeChild c)
    {
        var l = new List<SceneLapCap>();
        for (int i = 0; i < c.Lines.Length; i++)
        {
            var m = SceneLapCapRx.Match(c.Lines[i]);
            if (m.Success) l.Add(new SceneLapCap(m.Groups[1].Value, int.Parse(m.Groups[2].Value), m.Groups[3].Value, LapF(m.Groups[4].Value), m.Groups[5].Value, m.Groups[5].Success, LapF(m.Groups[6].Value), m.Groups[7].Value == "yes", i));
        }
        return l;
    }

    /// <summary>Marker lines that mean the archive itself had a problem (a refusal, a failed retain, a worker that did not finish, a damaged file).</summary>
    static IEnumerable<string> LapProblems(TapeChild c) => c.Lines.Where(l => (l.StartsWith("lap archive:") || l.Contains("lap archive: ") && l.StartsWith("WARNING"))
        && (l.Contains("FAILED") || l.Contains("refused") || l.Contains("cannot") || l.Contains("did not finish") || l.Contains("not kept") || l.Contains("damaged") || l.Contains("does not match")));

    /// <summary>One child game. Its data dir, XDG dirs and log are under the test root; the log is copied to media/ afterwards.</summary>
    TapeChild LapRun(string name, string[] args, int timeoutSec, Action<string> prepare = null, string seed = "1")
    {
        string dir = Path.Combine(_lapSceneRoot, name);
        Directory.CreateDirectory(dir);
        prepare?.Invoke(dir);
        var c = TapeGame(dir, args, timeoutSec, env: new() { ["XDG_CONFIG_HOME"] = dir + ".xdgc", ["XDG_CACHE_HOME"] = dir + ".xdgk", ["LAPDBG"] = "1" }, seed: seed);
        try { File.Copy(dir + ".log", Path.Combine(_lapSceneMedia, name + ".log"), true); } catch { }
        int shut = c.All("resources still in use at exit").Count();
        _lapShutdownErrors += shut;
        var capsAll = SceneLapCaps(c);
        for (int i = 0; i < capsAll.Count; i++) (i == 0 ? _lapMsCold : _lapMsWarm).Add(capsAll[i].Ms);
        TapeInfo($"lap archive scene: child '{name}' ({c.Seconds:F0} s, exit {c.Exit}, {SceneLapCaps(c).Count} capture lines, shutdown 'resources still in use' message x{shut}): {string.Join(' ', args)}");
        return c;
    }

    static string LapLapsDir(string dir, int file = 1) => Path.Combine(dir, "profiles", file.ToString(), "laps");

    static string LapHash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    bool LapCleanExit(TapeChild c, string what)
    {
        var problems = LapProblems(c).ToList();
        bool ok = !c.TimedOut && c.Exit == 0 && !c.Crashed && problems.Count == 0;
        TapeCheck(ok, $"{what}: the child ended cleanly (exit {c.Exit}, timed out {c.TimedOut}, crash lines {c.Crashed}, archive problem lines {problems.Count}{(problems.Count > 0 ? ": " + problems[0] : "")}, {c.Seconds:F0} s)");
        return ok;
    }

    /// <summary>Every capture line has a retained entry with matching meta, a ghost whose fingerprint matches, and (when taped) a tape that decodes and pairs with the ghost. Returns the infos by id.</summary>
    Dictionary<string, LapSource> LapVerifyFolder(string what, string dir, List<SceneLapCap> caps, string expectMode, int expectSet, int file = 1)
    {
        var res = new Dictionary<string, LapSource>();
        var archive = new LapArchive(LapLapsDir(dir, file));
        var list = archive.List();
        TapeCheck(list.Count == caps.Count(x => x.Kept) && list.All(x => x.Retained && x.State == LapEntryState.Ready && x.Openable),
            $"{what}: the folder lists {list.Count} laps, all retained/ready/openable (capture lines {caps.Count(x => x.Kept)})");
        foreach (var cap in caps.Where(x => x.Kept))
        {
            var info = list.FirstOrDefault(x => x.Id == cap.Id);
            var m = info?.Meta;
            bool metaOk = m != null && m.TrackId == cap.Track && m.Set == cap.Set && m.Set == expectSet && m.Kind.ToString() == cap.Kind && Math.Abs(m.LapSeconds - cap.Secs) < 0.006f
                && m.Mode == expectMode && m.HasTape == cap.Tape && m.Profile == file.ToString() && m.Retained && m.Ranked == (cap.Kind != "Void");
            TapeCheck(metaOk, $"{what}: {cap.Id} meta is track {m?.TrackId}, set {m?.Set}, {m?.Kind}, {m?.LapSeconds:F3} s (log {cap.Kind} {cap.Secs:F2} s), mode {m?.Mode}, tape {m?.HasTape}, ranked {m?.Ranked}, profile {m?.Profile}");
            var src = archive.Open(cap.Id, out string why);
            if (src == null) { TapeCheck(false, $"{what}: {cap.Id} opens ({why})"); continue; }
            res[cap.Id] = src;
            var fp = LapGhostBytes.Fingerprint(src.GhostBytes.Span, out int samples, out float lt);
            TapeCheck(fp != null && fp.Value == m.GhostFingerprint && Math.Abs(lt - cap.Secs) < 0.006f && samples > 100 && src.Warning == null,
                $"{what}: {cap.Id} ghost bytes fingerprint {fp:X} matches meta {m?.GhostFingerprint:X}, {samples} samples, ghost lap time {lt:F3} s, no warning ({src.Warning})");
            if (cap.Tape)
            {
                var tape = src.HasTape ? WorldTapeCodec.Decode(src.TapeBytes.ToArray()) : null;
                TapeCheck(tape != null && WorldTapeCodec.Validate(tape) && fp != null && tape.GhostFingerprint == fp.Value && tape.TrackId == cap.Track && tape.Set == cap.Set && tape.SampleCount > 100 && Math.Abs(tape.LapTime - lt) < 1e-3f,
                    $"{what}: {cap.Id} tape decodes, validates and pairs with the ghost (samples {tape?.SampleCount}, track {tape?.TrackId}, set {tape?.Set}, lap {tape?.LapTime:F2} s)");
            }
            else TapeCheck(!src.HasTape, $"{what}: {cap.Id} has no tape, as the capture line said");
            string folder = info.Dir ?? "";
            TapeCheck(File.Exists(Path.Combine(folder, "ghost.bin")) && File.Exists(Path.Combine(folder, "meta.json")) && (!cap.Tape || File.Exists(Path.Combine(folder, "tape.bin"))), $"{what}: {cap.Id} folder has ghost.bin, meta.json{(cap.Tape ? ", tape.bin" : "")}");
        }
        return res;
    }

    /// <summary>The retain lines (made at the scene's exit) all come after the last capture line and after the tape summary of the last lap, never between laps.</summary>
    void LapCheckRetainAtExit(TapeChild c, List<SceneLapCap> caps, string what)
    {
        int lastCap = caps.Count == 0 ? -1 : caps.Max(x => x.Line);
        var retains = Enumerable.Range(0, c.Lines.Length).Where(i => c.Lines[i].StartsWith("lap archive: retain ")).ToList();
        int summary = Array.FindLastIndex(c.Lines, l => l.Contains("summary (laps done)"));
        TapeCheck(retains.Count > 0 && retains.All(i => i > lastCap && (summary < 0 || i > summary)) && retains.All(i => c.Lines[i].EndsWith(": ok")),
            $"{what}: {retains.Count} retain lines, all ok and all after the last capture line ({lastCap}) and the final tape summary ({summary}); first at {(retains.Count > 0 ? retains[0] : -1)}");
    }

    void LapArchiveSceneTests()
    {
        GD.Print("Lap archive scene tests (real game, one headless child at a time)");
        if (!OperatingSystem.IsLinux()) { Skip("lap archive scene: the child runs go through /bin/sh (Linux only)"); return; }
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
        _lapSceneRoot = $"/tmp/cccp-lapark-scene-{stamp}";
        _lapSceneMedia = ProjectSettings.GlobalizePath("res://media/lap-archive-v1/scene-" + stamp);
        Directory.CreateDirectory(_lapSceneRoot); Directory.CreateDirectory(_lapSceneMedia);
        GD.Print($"LAP ARCHIVE SCENE: children under {_lapSceneRoot}, logs under {_lapSceneMedia}");
        string only = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--lap-archive-scene-only="))?["--lap-archive-scene-only=".Length..];
        void Case(string name, Action act) { if (only == null || name.StartsWith(only)) TapeRun(name, act); }
        try
        {
            foreach (var track in new[] { "ilyich", "siberia", "atomgrad", "bonus" }) Case("1 " + track, () => LapSceneTrack(track));
            Case("2 void", LapSceneVoid);
            Case("2b fake", LapSceneFake);
            Case("3 stakhanov", LapSceneStakhanov);
            Case("4 isolation", LapSceneIsolation);
            Case("5 cost", LapSceneCost);
            Case("6 eviction", LapSceneEviction);
            Case("7 profile", LapSceneProfile);
        }
        finally
        {
            TapeInfo($"lap archive scene: the known shutdown message 'ERROR: 1 resources still in use at exit' appeared {_lapShutdownErrors} time(s) over all children (information only, not a pass criterion)");
            // the children's data dirs are removed, their logs stay in media/
            try { if (_lapSceneRoot.StartsWith("/tmp/cccp-lapark-scene-") && Directory.Exists(_lapSceneRoot) && only == null) Directory.Delete(_lapSceneRoot, true); } catch { }
        }
        GD.Print(_failures == 0 ? "Lap archive scene tests: all passed" : $"Lap archive scene tests: {_failures} FAILED");
    }

    // ---- 1: two laps per track in Rally, all of them in the folder with matching meta, ghost and tape ----
    readonly Dictionary<string, List<SceneLapCap>> _lapTrackCaps = new();
    void LapSceneTrack(string track)
    {
        // lap 2 is driven slower (--tape-lap-speeds) so that it cannot be a record; the Bonus track needs the 0.8 autopilot of the other tape scenes (at full speed it does not finish a lap)
        var args = new List<string> { "--start", "--autodrive", "--track=" + track, "--tape-quit-laps=2", "--tape-lap-speeds=1,0.7", "--lap-archive-retain-all" };
        if (track == "bonus") args.Add("--ap-speed=0.8");
        var c = LapRun("t-" + track, args.ToArray(), 600);
        var caps = SceneLapCaps(c); _lapTrackCaps[track] = caps;
        if (!LapCleanExit(c, $"scene 1 {track}")) return;
        TapeInfo($"scene 1 {track}: capture lines: {string.Join("; ", caps.Select(x => $"{x.Kind} {x.Secs:F2} s tape {(x.Tape ? "yes" : "no")} {x.Ms:F2} ms"))}");
        TapeCheck(caps.Count >= 2 && caps.All(x => x.Kept && x.Track == track && x.Set == 0), $"scene 1 {track}: at least two laps were captured and kept in set 0 ({caps.Count})");
        TapeCheck(caps.Count(x => x.Kind == "Normal") >= 1, $"scene 1 {track}: at least one NON-record (Normal) lap was archived ({caps.Count(x => x.Kind == "Normal")} Normal, {caps.Count(x => x.Kind == "Record")} Record)");
        TapeCheck(caps.Count(x => x.Kind == "Record") >= 1, $"scene 1 {track}: the first lap on a fresh profile is archived as a Record");
        TapeCheck(caps.All(x => x.Tape), $"scene 1 {track}: every capture line says the world tape was recorded");
        LapCheckRetainAtExit(c, caps, $"scene 1 {track}");
        string dir = Path.Combine(_lapSceneRoot, "t-" + track);
        var srcs = LapVerifyFolder($"scene 1 {track}", dir, caps, "rally", 0);
        // the newest Record lap is byte-for-byte the ghost of the record slot, and its tape pairs with the slot's tape
        var lastRec = caps.LastOrDefault(x => x.Kind == "Record" && x.Kept);
        string slot = TapeGhostFile(dir, track, 0);
        if (lastRec != null && File.Exists(slot) && srcs.TryGetValue(lastRec.Id, out var s))
        {
            var slotFp = LapGhostBytes.Fingerprint(File.ReadAllBytes(slot), out _, out _);
            var archFp = LapGhostBytes.Fingerprint(s.GhostBytes.Span, out _, out _);
            TapeCheck(slotFp != null && slotFp == archFp, $"scene 1 {track}: the newest archived Record lap is the record slot's ghost (fingerprint {archFp:X} vs slot {slotFp:X})");
        }
        else TapeCheck(false, $"scene 1 {track}: a record slot ghost and an archived Record lap exist to compare");
    }

    // ---- 2: a void lap (R pressed by --void-at) ----
    void LapSceneVoid()
    {
        foreach (var track in new[] { "ilyich", "atomgrad" })
        {
            var c = LapRun("void-" + track, new[] { "--start", "--autodrive", "--track=" + track, "--void-at=10", "--tape-quit-laps=1", "--lap-archive-retain-all" }, 420);
            var caps = SceneLapCaps(c);
            if (!LapCleanExit(c, $"scene 2 void {track}")) continue;
            TapeInfo($"scene 2 {track}: capture lines: {string.Join("; ", caps.Select(x => $"{x.Kind} {x.Secs:F2} s tape {(x.Tape ? "yes" : "no")} {x.Ms:F2} ms"))}");
            var v = caps.FirstOrDefault(x => x.Kind == "Void");
            TapeCheck(v != null && v.Kept && v.Track == track && v.Set == 0, $"scene 2 {track}: a Void lap was captured and kept ({v?.Secs:F2} s, {v?.Id})");
            TapeCheck(caps.Count == 2 && caps[0].Kind == "Void" && caps[1].Kind == "Record", $"scene 2 {track}: the void lap came first and the lap after it is the Record ({string.Join(",", caps.Select(x => x.Kind))})");
            string dir = Path.Combine(_lapSceneRoot, "void-" + track);
            LapCheckRetainAtExit(c, caps, $"scene 2 {track}");
            var srcs = LapVerifyFolder($"scene 2 {track}", dir, caps, "rally", 0);
            if (v != null && srcs.TryGetValue(v.Id, out var vs))
            {
                TapeCheck(vs.Meta.Kind == LapKind.Void && !vs.Meta.Ranked, $"scene 2 {track}: the void entry on disk says Kind {vs.Meta.Kind}, ranked {vs.Meta.Ranked}");
                var slotFp = File.Exists(TapeGhostFile(dir, track, 0)) ? LapGhostBytes.Fingerprint(File.ReadAllBytes(TapeGhostFile(dir, track, 0)), out _, out _) : null;
                TapeCheck(slotFp != null && slotFp != vs.Meta.GhostFingerprint, $"scene 2 {track}: the void lap is not the record slot's ghost (slot {slotFp:X}, void {vs.Meta.GhostFingerprint:X})");
            }
        }
    }

    // ---- 2b: a fake lap (--lap-done-at) is never archived ----
    void LapSceneFake()
    {
        var c = LapRun("fake", new[] { "--start", "--autodrive", "--track=ilyich", "--lap-done-at=3:50.0", "--tape-quit-laps=2", "--lap-archive-retain-all" }, 420);
        var caps = SceneLapCaps(c);
        if (!LapCleanExit(c, "scene 2b fake")) return;
        TapeInfo($"scene 2b: capture lines: {string.Join("; ", caps.Select(x => $"{x.Kind} {x.Secs:F2} s"))}; LAPDBG lap lines: {string.Join(" | ", c.Lines.Where(l => l.StartsWith("lap ") && l.Contains(" s, flicks")))}");
        TapeCheck(c.Lines.Any(l => l.StartsWith("lap 50.00 s")), $"scene 2b: the fake lap (50.00 s) really happened in the child ({c.Lines.FirstOrDefault(l => l.StartsWith("lap 50.00 s"))})");
        TapeCheck(caps.All(x => Math.Abs(x.Secs - 50f) > 0.5f), $"scene 2b: no capture line for the 50.00 s fake lap ({caps.Count} capture lines: {string.Join(", ", caps.Select(x => x.Secs.ToString("F2")))})");
        var list = new LapArchive(LapLapsDir(Path.Combine(_lapSceneRoot, "fake"))).List();
        TapeCheck(list.Count == caps.Count && list.All(x => Math.Abs(x.Meta.LapSeconds - 50f) > 0.5f), $"scene 2b: the folder holds {list.Count} laps (= capture lines {caps.Count}) and none of them is the fake lap");
    }

    // ---- 3: Stakhanov (training) ----
    void LapSceneStakhanov()
    {
        var c = LapRun("stakhanov", new[] { "--start", "--autodrive", "--track=ilyich", "--training", "--tape-quit-laps=1", "--lap-archive-retain-all" }, 420);
        var caps = SceneLapCaps(c);
        if (!LapCleanExit(c, "scene 3 stakhanov")) return;
        TapeCheck(caps.Count >= 1 && caps.All(x => x.Kept && x.Set == 1 && x.Track == "ilyich"), $"scene 3: the Stakhanov lap is captured with set 1 ({string.Join("; ", caps.Select(x => $"set {x.Set} {x.Kind} {x.Secs:F2} s"))})");
        string dir = Path.Combine(_lapSceneRoot, "stakhanov");
        LapCheckRetainAtExit(c, caps, "scene 3");
        var srcs = LapVerifyFolder("scene 3", dir, caps, "stakhanov", 1);
        TapeCheck(srcs.Values.Any(s => s.Meta.Mode == "stakhanov" && s.Meta.Set == 1) && !File.Exists(TapeGhostFile(dir, "ilyich", 0)) && File.Exists(TapeGhostFile(dir, "ilyich", 1)),
            "scene 3: Mode stakhanov, RecordSet 1 on disk, and only the Stakhanov record slot (not the petrol one) was written");
    }

    // ---- 4: the archive never touches the record slots: same seeds, with and without --no-lap-archive ----
    void LapSceneIsolation()
    {
        var on = LapRun("iso-on", new[] { "--start", "--autodrive", "--track=ilyich", "--tape-quit-laps=2", "--lap-archive-retain-all" }, 420);
        var off = LapRun("iso-off", new[] { "--start", "--autodrive", "--track=ilyich", "--tape-quit-laps=2", "--no-lap-archive", "--lap-archive-retain-all" }, 420);
        var off2 = LapRun("iso-off2", new[] { "--start", "--autodrive", "--track=ilyich", "--tape-quit-laps=2", "--no-lap-archive" }, 420);      // a second run WITHOUT the archive: is the baseline itself repeatable?
        string dOn = Path.Combine(_lapSceneRoot, "iso-on"), dOff = Path.Combine(_lapSceneRoot, "iso-off"), dOff2 = Path.Combine(_lapSceneRoot, "iso-off2");
        TapeCheck(!on.TimedOut && on.Exit == 0 && !on.Crashed && !off.TimedOut && off.Exit == 0 && !off.Crashed, $"scene 4: both runs ended cleanly (exit {on.Exit}/{off.Exit})");
        TapeCheck(SceneLapCaps(on).Count == 2 && SceneLapCaps(off).Count == 0 && !off.Lines.Any(l => l.StartsWith("lap archive:")), $"scene 4: the archive run captured {SceneLapCaps(on).Count} laps, the --no-lap-archive run printed no archive line at all");
        TapeCheck(!Directory.Exists(LapLapsDir(dOff)) || Directory.GetFileSystemEntries(LapLapsDir(dOff)).Length == 0, "scene 4: --no-lap-archive left no laps folder content");
        bool Rec(string f) { var n = Path.GetFileName(f); return n.StartsWith("ghost_") || n.StartsWith("tape_") || n.StartsWith("leaderboard_") || n.StartsWith("track_") || n.StartsWith("records_"); }
        var a = Directory.GetFiles(dOn).Where(Rec).Select(Path.GetFileName).OrderBy(x => x).ToList();
        var b = Directory.GetFiles(dOff).Where(Rec).Select(Path.GetFileName).OrderBy(x => x).ToList();
        TapeCheck(a.Count >= 4 && a.SequenceEqual(b), $"scene 4: the same record files exist in both runs ({string.Join(", ", a)} | {string.Join(", ", b)})");
        foreach (var f in a.Intersect(b))
        {
            string pOn = Path.Combine(dOn, f), pOff = Path.Combine(dOff, f), pOff2 = Path.Combine(dOff2, f);
            bool same = LapHash(pOn) == LapHash(pOff), base2 = File.Exists(pOff2) && LapHash(pOff) == LapHash(pOff2);
            string diff = "";
            if (!same) { var x = File.ReadAllBytes(pOn); var y = File.ReadAllBytes(pOff); int i = 0; while (i < Math.Min(x.Length, y.Length) && x[i] == y[i]) i++; diff = $"; with {x.Length} B, without {y.Length} B, first difference at byte {i}; two runs WITHOUT the archive are {(base2 ? "identical (the archive changed it)" : "different too (the file is not repeatable without the archive either)")}"; }
            bool tapeFile = f.StartsWith("tape_");
            if (!same && tapeFile && !base2)
            {
                // Measured: the world tape's bytes are not repeatable even between two runs without the archive (some live-world content is not seeded by --rng-seed), so byte identity is
                // judged against that baseline: the archive must not change more than the baseline does. The ghost (which the tape pairs with) IS byte-identical; the tape must pair and agree in its header.
                TapeInfo($"scene 4: {f} differs between runs even WITHOUT the archive{diff}");
                var tOn = WorldTapeCodec.Decode(File.ReadAllBytes(pOn)); var tOff = WorldTapeCodec.Decode(File.ReadAllBytes(pOff)); var tOff2 = WorldTapeCodec.Decode(File.ReadAllBytes(pOff2));
                TapeCheck(tOn != null && tOff != null && tOff2 != null && tOn.GhostFingerprint == tOff.GhostFingerprint && tOff.GhostFingerprint == tOff2.GhostFingerprint && tOn.TrackId == tOff.TrackId && tOn.Set == tOff.Set
                    && Math.Abs(tOn.SampleCount - tOff.SampleCount) <= 2 && Math.Abs(tOff.SampleCount - tOff2.SampleCount) <= 2 && tOn.ActorStructure.Keys.OrderBy(x => x).SequenceEqual(tOff.ActorStructure.Keys.OrderBy(x => x)),
                    $"scene 4: {f} with the archive pairs with the identical ghost and agrees with the archive-free runs in track, set, actor list and sample count (samples {tOn?.SampleCount} / {tOff?.SampleCount} / {tOff2?.SampleCount})");
            }
            else TapeCheck(same, $"scene 4: {f} is byte-identical with and without the archive ({new FileInfo(pOn).Length} bytes{diff})");
        }
        var other = new[] { "campaign.cfg", "speedtrap.cfg", "settings.cfg" };
        foreach (var f in other)
        {
            string p1 = Path.Combine(LapLapsDir(dOn).Replace(Path.DirectorySeparatorChar + "laps", ""), f), p2 = Path.Combine(LapLapsDir(dOff).Replace(Path.DirectorySeparatorChar + "laps", ""), f);
            if (File.Exists(p1) || File.Exists(p2)) TapeInfo($"scene 4: profile file {f}: {(File.Exists(p1) && File.Exists(p2) ? (LapHash(p1) == LapHash(p2) ? "identical" : "differs (informational, not a record file)") : "present in one run only")}");
        }
        var inLaps = Directory.GetDirectories(LapLapsDir(dOn)).SelectMany(Directory.GetFiles).Select(Path.GetFileName).Distinct().OrderBy(x => x).ToList();
        TapeCheck(!inLaps.Any(n => n.StartsWith("ghost_") || n.StartsWith("tape_") || n.StartsWith("leaderboard_")) && inLaps.Contains("ghost.bin") && inLaps.Contains("tape.bin"), $"scene 4: the lap folders hold {string.Join(", ", inLaps)} (no ghost_/tape_/leaderboard_ record names, only ghost.bin / tape.bin)");
        TapeCheck(!Directory.GetFiles(LapLapsDir(dOn)).Any(), "scene 4: nothing but lap folders directly in the laps folder");
    }

    // ---- 5: the finish-line cost, over every capture line of every child ----
    void LapSceneCost()
    {
        if (_lapMsCold.Count + _lapMsWarm.Count < 4 || _lapMsWarm.Count == 0) { Skip("scene 5: too few capture lines were measured (a single-case run?)"); return; }
        string St(List<float> l) { var v = l.OrderBy(x => x).ToList(); return $"n {v.Count}, min {v[0]:F2}, median {v[v.Count / 2]:F2}, avg {v.Average():F2}, max {v[^1]:F2} ms"; }
        TapeInfo($"scene 5: main thread at the finish line, FIRST capture of a process (cold, includes the JIT of the capture path): {St(_lapMsCold)}; later captures (warm): {St(_lapMsWarm)}; all: min {Math.Min(_lapMsCold.Min(), _lapMsWarm.Min()):F2} / avg {_lapMsCold.Concat(_lapMsWarm).Average():F2} / max {Math.Max(_lapMsCold.Max(), _lapMsWarm.Max()):F2} ms");
        // Bounds: a cold capture below 3 frames at 60 fps (50 ms), a warm one below 5 ms (a third of a frame); the measured numbers sit well below both.
        TapeCheck(_lapMsCold.Max() < 50f, $"scene 5: every cold capture is below 50 ms (max {_lapMsCold.Max():F2} ms)");
        TapeCheck(_lapMsWarm.Max() < 5f, $"scene 5: every warm capture is below 5 ms (max {_lapMsWarm.Max():F2} ms)");
    }

    // ---- 6: eviction in the real scene: 7 laps on the shortest track, at most 5 stay, the oldest are the ones missing ----
    void LapSceneEviction()
    {
        var c = LapRun("evict", new[] { "--start", "--autodrive", "--track=ilyich", "--tape-quit-laps=7", "--lap-archive-retain-all" }, 600);
        var caps = SceneLapCaps(c);
        if (!LapCleanExit(c, "scene 6 evict")) return;
        TapeCheck(caps.Count == 7 && caps.All(x => x.Kept), $"scene 6: seven laps captured and kept ({caps.Count}; {string.Join(",", caps.Select(x => x.Kind[0]))})");
        string dir = Path.Combine(_lapSceneRoot, "evict");
        var list = new LapArchive(LapLapsDir(dir)).List();
        var ids = caps.Select(x => x.Id).ToList();
        var expectKept = ids.Skip(Math.Max(0, ids.Count - 5)).ToHashSet();
        TapeCheck(list.Count <= 5, $"scene 6: the folder holds {list.Count} laps, at most 5 (the recent limit)");
        TapeCheck(list.Select(x => x.Id).ToHashSet().SetEquals(expectKept), $"scene 6: the five NEWEST laps are the ones kept ({string.Join(",", list.Select(x => x.Id[^4..]).OrderBy(x => x))} vs expected {string.Join(",", expectKept.Select(x => x[^4..]).OrderBy(x => x))})");
        TapeCheck(ids.Take(ids.Count - 5).All(id => list.All(x => x.Id != id)) && c.All("dropped the oldest recent lap").Count() >= 2, $"scene 6: the two oldest laps are missing and the log says they were dropped ({c.All("dropped the oldest recent lap").Count()} drop messages)");
        LapCheckRetainAtExit(c, caps, "scene 6");
        var kept = caps.Where(x => expectKept.Contains(x.Id)).ToList();
        LapVerifyFolder("scene 6", dir, kept, "rally", 0);
    }

    // ---- 7: the active personnel file chooses the folder ----
    void LapSceneProfile()
    {
        var c = LapRun("profile", new[] { "--start", "--autodrive", "--track=ilyich", "--tape-quit-laps=1", "--lap-archive-retain-all" }, 420,
            prepare: d => File.WriteAllText(Path.Combine(d, "settings.cfg"), "[files]\n\nactive=3\n"));
        var caps = SceneLapCaps(c);
        if (!LapCleanExit(c, "scene 7 profile")) return;
        string dir = Path.Combine(_lapSceneRoot, "profile");
        TapeCheck(caps.Count == 1 && Directory.Exists(LapLapsDir(dir, 3)) && Directory.GetDirectories(LapLapsDir(dir, 3)).Length == 1 && !Directory.Exists(LapLapsDir(dir, 1)),
            $"scene 7: the lap is in profiles/3/laps ({(Directory.Exists(LapLapsDir(dir, 3)) ? Directory.GetDirectories(LapLapsDir(dir, 3)).Length : 0)} folders) and profiles/1/laps does not exist");
        LapVerifyFolder("scene 7", dir, caps, "rally", 0, file: 3);
    }
}
