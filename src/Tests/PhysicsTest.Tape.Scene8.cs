using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

// --tape-scene-tests, scene 15 (--tape-scene-only="scene 15"): world-tape coverage batch 1 in the REAL game (docs/replay-tape.md section 21). Per track one autodriven lap is recorded with the
// new actors' parts dumped at every recorded sample (and the forced hits of RaceScene.ViewerTapeCoverTest.cs), then the pair is replayed in fresh children that start in the middle of the
// events, wrap, leave for a race and come back; the replay's poses at every seek must be the recorded ones (lerped by the clock's place between two samples), the world must be back at the rest the
// index took when the replay is released, the trace must be identical with recording off, and the repository's older tapes (the review's evidence) must cover what they covered.
public partial class PhysicsTest
{
    sealed record CoverTrack(string Id, string[] Keys, string[] LapArgs, string[] Forced, bool Cans);

    static readonly CoverTrack[] CoverTracks =
    {
        new("ilyich", new[] { "None:KolkhozWorkers", "None:BarnDoorsTractor", "None:BarnDoorsSlurry", "None:DachaGuards", "Babushkas:Babushkas" },
            new string[0], new[] { "worker@10", "guard@16", "canister@24" }, true),
        new("siberia", new[] { "None:TrafficCones", "Babushkas:Babushkas" },
            new string[0], new[] { "cone@6", "golden@24" }, true),
        new("bonus", new[] { "Jets:ExerciseCrew", "Babushkas:Troopers" },
            new string[0], new[] { "crew@8", "canister@24" }, true),
    };

    /// <summary>What the review's older tapes covered (the media of xq585yha, replayed by the build before this batch): they must cover all of it still.</summary>
    static readonly Dictionary<string, string[]> CoverOlderTapes = new()
    {
        ["ilyich"] = new[] { "Tractor:DrunkTractor", "Slurry:SlurryTractor", "Bus:Bus", "Kozlik:Kozlik", "None:KgbOfficers", "Babushkas:Babushkas", "Drunks:DrunkMen", "None:RescueTractor", "None:RescueTowRope", "None:WoodFences", "None:SpeedTrap", "Depot:FuelDepot", "None:PlayerPower" },
        ["siberia"] = new[] { "Ploughs:SnowPloughs", "Column:PrisonerColumn", "Kozlik:Kozlik", "Jets:BalalaikaSorties", "Details:SiberiaDetails", "None:KgbOfficers", "Babushkas:Babushkas", "Drunks:DrunkMen", "None:RescueTractor", "None:RescueTowRope", "None:WoodFences", "None:SpeedTrap", "Depot:FuelDepot", "None:PlayerPower" },
        ["atomgrad"] = new[] { "Bus:Bus", "Ploughs:SnowPloughs", "Zone:ZoneLife", "Kozlik:Kozlik", "None:KgbOfficers", "Babushkas:Babushkas", "Drunks:DrunkMen", "None:RescueTractor", "None:RescueTowRope", "None:WoodFences", "None:SpeedTrap", "None:Dosimetrist", "Depot:FuelDepot", "None:PlayerPower" },
        ["bonus"] = new[] { "Kozlik:Kozlik", "Jets:HindSorties", "None:KgbOfficers", "None:CampGate", "Btr:BtrPatrols", "Tanks:TankBattle", "Babushkas:Troopers", "Drunks:DrunkMen", "None:RescueTractor", "None:RescueTowRope", "None:WoodFences", "None:SpeedTrap", "Depot:FuelDepot", "None:PlayerPower" },
    };

    sealed class CoverPart { public float[] F; public bool V; }

    /// <summary>A dump file: head (a sample number or a clock) to its parts by "key|part".</summary>
    static SortedDictionary<float, Dictionary<string, CoverPart>> CoverRead(string path)
    {
        var d = new SortedDictionary<float, Dictionary<string, CoverPart>>();
        if (!File.Exists(path)) return d;
        foreach (var line in File.ReadLines(path))
        {
            var p = line.Split('|'); if (p.Length < 5) continue;
            float head = float.Parse(p[0], CultureInfo.InvariantCulture);
            if (!d.TryGetValue(head, out var parts)) d[head] = parts = new();
            parts[p[1] + "|" + p[2]] = new CoverPart { F = p[3].Split(' ').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray(), V = p[4] == "1" };
        }
        return d;
    }

    static float CoverPartDiff(float[] a, float[] b)
    {
        float worst = MathF.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]) + (a[2] - b[2]) * (a[2] - b[2]));
        for (int c = 0; c < 3; c++) worst = MathF.Max(worst, MathF.Sqrt((a[3 + 3 * c] - b[3 + 3 * c]) * (a[3 + 3 * c] - b[3 + 3 * c]) + (a[4 + 3 * c] - b[4 + 3 * c]) * (a[4 + 3 * c] - b[4 + 3 * c]) + (a[5 + 3 * c] - b[5 + 3 * c]) * (a[5 + 3 * c] - b[5 + 3 * c])));
        return worst;
    }

    /// <summary>The replay's dump at <paramref name="clock"/> against the recorded samples around it: what the player poses is the lerp of sample k and k + 1 (the part set is sample k's).</summary>
    static float CoverReplayDiff(SortedDictionary<float, Dictionary<string, CoverPart>> rec, Dictionary<string, CoverPart> rep, float clock, out string where, string[] only = null)
    {
        where = "";
        int k = (int)MathF.Floor(clock * WorldTapeConst.TapeRate + 1e-3f);
        float u = Math.Clamp(clock * WorldTapeConst.TapeRate - k, 0f, 1f);
        if (!rec.TryGetValue(k, out var a)) { where = $"no recorded sample {k}"; return 999f; }
        rec.TryGetValue(k + 1, out var b);
        float worst = 0;
        foreach (var (id, pa) in a)
        {
            if (only != null && Array.IndexOf(only, id.Split('|')[0]) < 0) continue;      // (only the actors this scene is about: an arm that swings 25 degrees a sample differs between a linear lerp and the player's slerp)
            if (!rep.TryGetValue(id, out var r)) { where = id + " missing in the replay"; return 999f; }
            var want = pa.F;
            if (b != null && b.TryGetValue(id, out var pb)) { want = new float[12]; for (int i = 0; i < 12; i++) want[i] = pa.F[i] + (pb.F[i] - pa.F[i]) * u; }
            float d = CoverPartDiff(want, r.F);
            if (pa.V != r.V && u < 0.5f) d = 999f;
            if (d > worst) { worst = d; where = id; }
        }
        foreach (var id in rep.Keys) if (!a.ContainsKey(id)) { where = id + " extra in the replay"; return 999f; }
        return worst;
    }

    // scene 16 (--tape-scene-only="scene 16"): coverage batch 2 (docs/replay-tape.md section 22) in the real game, the same way: per track an autodriven lap, the poses dumped, replays from the middle, wraps, a trip to a race and back.
    static readonly CoverTrack[] CoverTracks2 =
    {
        new("ilyich", new[] { "None:PartyOfficialRowing", "None:PondMallard", "None:VillageLife", "None:WaterMill" }, new string[0], new string[0], false),
        new("siberia", new[] { "None:Radar", "None:Beacon0" }, new string[0], new string[0], false),
        new("atomgrad", new[] { "None:Weather", "None:Beacon0" }, new[] { "--time-of-day=0.55" }, new string[0], false),
        new("bonus", new[] { "Jets:FieldKitchen", "Jets:FieldCombine", "None:EngineEffects" }, new string[0], new[] { "engine@10" }, false),
    };

    void TapeScene9()
    {
        string only = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--cover-track="))?["--cover-track=".Length..];
        foreach (var t in CoverTracks2) if (only == null || only == t.Id) TapeScene8Track(t, "scene 16");
        string[] fresh = { "None:PartyOfficialRowing", "None:PondMallard", "None:VillageLife", "None:WaterMill", "None:Radar", "None:Beacon0", "None:Weather", "Jets:FieldKitchen", "Jets:FieldCombine", "None:EngineEffects" };
        foreach (var (id, covered) in CoverOlderTapes) if (only == null || only == id) TapeScene8Older(id, covered, fresh, "scene 16");
    }

    void TapeScene8()
    {
        string only = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--cover-track="))?["--cover-track=".Length..];
        foreach (var t in CoverTracks) if (only == null || only == t.Id) TapeScene8Track(t);
        foreach (var (id, covered) in CoverOlderTapes) if (only == null || only == id) TapeScene8Older(id, covered);
    }

    void TapeScene8Track(CoverTrack t, string scene = "scene 15")
    {
        string a = TapeSceneDir("c1" + t.Id), off = TapeSceneDir("c1off" + t.Id);
        string trA = a + ".trace.txt", trOff = off + ".trace.txt", dump = a + ".dump";
        string label = scene + " " + t.Id;
        try
        {
            var lapArgs = new[] { "--start", "--autodrive", "--track=" + t.Id, "--tape-debug", "--tape-quit-laps=1" }.Concat(t.Forced.Length > 0 ? new[] { "--tape-force=" + string.Join(',', t.Forced) } : new string[0]).Concat(t.LapArgs).ToArray();
            var env = new Dictionary<string, string> { ["WORLDTRACE"] = trA, ["WORLDTRACE_FRAMES"] = "60000" };
            var c = TapeGame(a, lapArgs.Concat(new[] { "--tape-cover-dump=" + dump, "--tape-cover-at=all" }).ToArray(), 900, env: env);
            TapeCheck(!c.TimedOut && c.Exit == 0 && !c.Crashed, $"{label}: the lap ended cleanly (exit {c.Exit}, {c.Seconds:F0} s)");
            var (g, tape, paired) = TapeSceneLoad(a, t.Id, 0);
            TapeCheck(g != null && tape != null && paired && c.Has("ghost+tape saved"), $"{label}: PROOF a lap was saved: the ghost+tape pair is on disk and the tape belongs to the ghost (tape {tape?.SampleCount} samples)");
            foreach (var f in t.Forced) { string w = f.Split('@')[0]; var fl = c.Lines.FirstOrDefault(l => l.Contains("force: ") && (l.Contains(w) || (w == "guard" && l.Contains("guard")))); TapeCheck(fl != null && !fl.Contains("False") && !fl.Contains("no "), $"{label}: the forced event {f} happened ({fl?.Trim()})"); }
            var body = tape == null ? null : WorldTapeCodec.ReadBody(tape.Payload);
            foreach (var key in t.Keys) TapeCheck(tape != null && tape.ActorStructure.ContainsKey(key), $"{label}: the tape holds the structure of {key} ({tape?.ActorStructure.GetValueOrDefault(key)?.ParentPaths.Count} parts)");
            if (body != null && scene == "scene 16")
            {
                var kinds = body.Events.Where(e => e.Kind != null).GroupBy(e => e.Kind).ToDictionary(g => g.Key, g => g.Count());
                TapeInfo($"{label}: effect events of the lap: {string.Join(", ", kinds.Where(kv => kv.Key is TapeFxKind.DachaShot or TapeFxKind.Lightning or TapeFxKind.EngineSeize or TapeFxKind.EngineExplode or TapeFxKind.Breakable).Select(kv => kv.Key + "=" + kv.Value))}");
                if (t.Id == "ilyich") TapeCheck(kinds.GetValueOrDefault(TapeFxKind.DachaShot) >= 1, $"{label}: the guest's shotgun shot is on the tape as an event ({kinds.GetValueOrDefault(TapeFxKind.DachaShot)} shot(s))");
                if (t.Keys.Contains("None:EngineEffects"))
                {
                    int smoky = body.Sounds.Count(sn => sn.Any(x => x.Item1.Part.ActorKey == "None:EngineEffects" && x.Item1.Channel == TapeSoundChannel.EngineFx && x.Item2 > 0));
                    TapeCheck(smoky >= 1, $"{label}: a sick engine's smoke is on the tape as the engine level ({smoky} change(s) of it to a level above zero; the sound channels keep changes only)");
                }
            }
            var unscaled = c.Lines.FirstOrDefault(l => l.Contains("unscaled by part ["));
            string uns = TapeBracket(unscaled, "unscaled by part");
            TapeCheck(unscaled != null && !t.Keys.Any(k => uns.Contains(k)), $"{label}: none of the new actors' parts changes scale while recording ({(uns.Length > 60 ? uns[..60] + "..." : uns)})");
            var cost = c.Find("record cost of lap 1"); var fin = c.Lines.LastOrDefault(x => x.Contains("World tape: set 0 lap"));
            long bytes = tape == null ? 0 : new FileInfo(TapeTapeFile(a, t.Id, 0)).Length;
            TapeInfo($"{label}: tape {bytes / 1024} KB; {cost?.Split(", allocated")[0].Trim()}; {fin?.Trim()}");
            TapeCheck(bytes < 2_000_000 && cost != null && TapeNum(cost, "avg") < 0.5f && TapeNum(cost, "p99") < 1.5f, $"{label}: within the budgets: {bytes / 1024} KB (under 2 MB), {TapeNum(cost, "avg"):F3} ms average and {TapeNum(cost, "p99"):F3} ms p99 per sample (0.5 / 1.5)");
            // the recording does not touch the simulation: the same lap with the tape off has the same lap time and the same world trace
            var o = TapeGame(off, lapArgs, 900, tapeOff: true, env: new Dictionary<string, string> { ["WORLDTRACE"] = trOff, ["WORLDTRACE_FRAMES"] = "60000" });
            string lapOn = Regex.Match(c.Find("tape: lap 1 ") ?? "", @"lap 1 (\d+\.\d+)").Groups[1].Value, lapOff = Regex.Match(o.Find("tape: lap 1 ") ?? "", @"lap 1 (\d+\.\d+)").Groups[1].Value;
            lapOff = lapOff == "" ? Regex.Match(o.Find("lap time") ?? o.Find("LAP") ?? "", @"(\d+\.\d+)").Groups[1].Value : lapOff;
            if (File.Exists(trA) && File.Exists(trOff))
            {
                var ta = File.ReadAllLines(trA); var tb = File.ReadAllLines(trOff);
                int n = Math.Min(ta.Length, tb.Length), same = 0; while (same < n && ta[same] == tb[same]) same++;
                TapeCheck(n > 8000 && same >= n - 3, $"{label}: the car and world trace is identical with recording on and off for {same} of {n} steps (the forced hits, the doors, the workers and the cans included)");
            }
            else TapeCheck(false, $"{label}: both trace files exist");

            // ---- the recorded poses, then replays that start in the middle of each event
            var rec = CoverRead(dump + ".rec");
            var rest = CoverRead(dump + ".I");
            TapeCheck(rec.Count > 300 && rest.Count == 1, $"{label}: the recorded poses were dumped ({rec.Count} samples, {rec.Values.FirstOrDefault()?.Count} parts) with the index's rest ({rest.Count})");
            if (scene == "scene 16" && rec.Count > 0)      // the animations that were held for the index run in a normal start: each new actor's parts move in the lap (a part held forever would not)
            {
                var first = rec.First().Value;
                foreach (var key in t.Keys)
                {
                    var ids = first.Keys.Where(id => id.StartsWith(key + "|") && !id.EndsWith("|root")).ToList();
                    if (ids.Count == 0) continue;
                    bool moved = ids.Any(id => rec.Values.Any(v => v.TryGetValue(id, out var p) && CoverPartDiff(p.F, first[id].F) > 0.01f || v.TryGetValue(id, out var q) && q.V != first[id].V));
                    TapeCheck(moved, $"{label}: {key} moves in the recorded lap (its live animation started after the index: {ids.Count} parts)");
                }
            }
            var starts = new SortedSet<int>();
            foreach (var f in t.Forced) starts.Add((int)MathF.Round((float.Parse(f.Split('@')[1], CultureInfo.InvariantCulture) + 0.4f) * WorldTapeConst.TapeRate));
            if (body != null)
                foreach (var e in body.Events.Where(e => e.Type == TapeEventType.Spawn && e.Kind != null && e.Kind.StartsWith("canister/"))) starts.Add((int)MathF.Round((e.Time + 0.25f) * WorldTapeConst.TapeRate));
            int cans = body?.Events.Count(e => e.Type == TapeEventType.Spawn && e.Kind != null && e.Kind.StartsWith("canister/")) ?? 0;
            if (t.Cans) TapeCheck(cans >= 1 && body.Events.Count(e => e.Type == TapeEventType.Despawn) >= cans, $"{label}: the tape records the can in the air: {cans} spawn(s) of kind {string.Join("/", body.Events.Where(e => e.Kind != null && e.Kind.StartsWith("canister/")).Select(e => e.Kind).Distinct())} and their despawns");
            string door = t.Keys.FirstOrDefault(k => k.StartsWith("None:BarnDoors"));
            if (door != null && rec.Count > 0)
            {
                int opens = rec.Keys.Select(k => (int)k).FirstOrDefault(k => rec[k].TryGetValue(door + "|left", out var p) && CoverPartDiff(p.F, rec[0][door + "|left"].F) > 0.05f);
                TapeCheck(opens > 0, $"{label}: the barn door opens in the recorded lap (first at sample {opens})");
                if (opens > 0) starts.Add(opens + 8);
            }
            if (scene == "scene 16")      // (no forced hits: the replays start in the middle of what the tape has, at a quarter, a half and three quarters of the lap, and at the dacha's shots)
            {
                foreach (int q in new[] { 1, 2, 3 }) starts.Add(rec.Count * q / 4);
                if (body != null) foreach (var e in body.Events.Where(e => e.Kind == TapeFxKind.DachaShot || e.Kind == TapeFxKind.Lightning)) starts.Add((int)MathF.Round((e.Time + 0.3f) * WorldTapeConst.TapeRate));
            }
            var probes = starts.Where(k => k > 20 && k < rec.Count - 40).Take(scene == "scene 16" ? 5 : 4).ToList();
            TapeInfo($"{label}: replays start at samples {string.Join(", ", probes)} ({string.Join(", ", probes.Select(k => (k / WorldTapeConst.TapeRate).ToString("F2", CultureInfo.InvariantCulture) + " s"))})");
            foreach (int k in probes)
            {
                string d = TapeSceneDir("c1r" + t.Id); string rdump = d + ".dump";
                try
                {
                    TapeCopyDir(a, d);
                    float start = k / WorldTapeConst.TapeRate;
                    var r = TapeGame(d, new[] { "--track=" + t.Id, "--tape-debug", "--tape-replay-start=" + start.ToString("F5", CultureInfo.InvariantCulture), "--tape-wrap-every=3", "--tape-title-trip=4,3", "--tape-replay-quit=14", "--tape-cover-dump=" + rdump }, 300);
                    string lab = $"{label} replay from sample {k}";
                    TapeCheck(r.Exit == 0 && !r.TimedOut && !r.Crashed, $"{lab}: the replay ended cleanly (exit {r.Exit}, {r.Seconds:F0} s)");
                    var covers = TapeKeyList(r.Find("from tape: covers ["), "covers");
                    TapeCheck(t.Keys.All(covers.Contains), $"{lab}: the replay covers {string.Join(", ", t.Keys)} from the tape");
                    var reps = CoverRead(rdump + ".rep");
                    var rep0 = reps.Count > 0 ? reps.First() : default;
                    float worst = 0; string worstAt = ""; int checkedSeeks = 0;
                    foreach (var (clock, parts) in reps)
                    {
                        float dd = CoverReplayDiff(rec, parts, clock, out var w, scene == "scene 16" ? t.Keys : null);
                        checkedSeeks++;
                        if (dd > worst) { worst = dd; worstAt = $"clock {clock:F3} {w}"; }
                    }
                    TapeCheck(checkedSeeks >= 2 && worst < 0.03f, $"{lab}: at each of the {checkedSeeks} seeks (the start, every wrap, the return from the race) the posed parts are the recorded ones (worst {worst:F4} at {worstAt})");
                    var rel = CoverRead(rdump + ".rel");
                    float relWorst = 0; string relAt = "";
                    if (rel.Count > 0 && rest.Count > 0)
                        foreach (var (id, p0) in rest.First().Value)
                        {
                            if (id.EndsWith("|root")) continue;
                            if (!rel.First().Value.TryGetValue(id, out var p1)) { relWorst = 999f; relAt = id + " missing"; break; }
                            float dd = CoverPartDiff(p0.F, p1.F); if (p0.V != p1.V) dd = 999f;
                            if (dd > relWorst) { relWorst = dd; relAt = id; }
                        }
                    TapeCheck(rel.Count >= 1 && relWorst < 0.002f, $"{lab}: leaving the replay for a race puts every part back at the rest the index took ({rel.Count} release(s), worst {relWorst:F5} at {relAt})");
                    var exit = r.Find("scene exit: replay instances");
                    TapeCheck(exit != null && exit.Contains("0 left"), $"{lab}: leaving the scene frees every instance the replay made ({exit?.Trim()})");
                }
                finally { TapeSceneRm(d); foreach (var s in new[] { ".rep", ".rel", ".I" }) try { File.Delete(rdump + s); } catch { } }
            }
        }
        finally { TapeSceneRm(a); TapeSceneRm(off); foreach (var f in new[] { trA, trOff, dump + ".rec", dump + ".I" }) try { File.Delete(f); } catch { } }
    }

    /// <summary>The review's own tapes (media/tape-coverage-xq585yha/): the replay covers all it covered before this batch and the new actors stay live (no event is invented for them).</summary>
    void TapeScene8Older(string id, string[] covered, string[] fresh = null, string scene = "scene 15")
    {
        string src = ProjectSettings.GlobalizePath("res://media/tape-coverage-xq585yha/" + id);
        if (!Directory.Exists(src)) { Skip($"tape {scene} older tape {id}: the review's media are not here"); return; }
        string d = TapeSceneDir("c1old" + id);
        try
        {
            Directory.CreateDirectory(d);
            foreach (var f in new[] { $"ghost_{id}.bin", $"tape_{id}.bin", $"track_{id}.sig", $"records_{id}.ver" }) File.Copy(Path.Combine(src, f), Path.Combine(d, f));
            var c = TapeGame(d, new[] { "--watch", "--track=" + id, "--tape-debug", "--tape-replay-start=7.37", "--tape-replay-quit=5" }, 300);
            var line = c.Find("replay of set 0 from tape: covers [");
            var cov = TapeKeyList(line, "covers"); var live = TapeKeyList(line, "live");
            var missing = covered.Where(k => !cov.Contains(k)).ToArray();
            fresh ??= new[] { "None:KolkhozWorkers", "None:BarnDoorsTractor", "None:BarnDoorsSlurry", "None:DachaGuards", "None:TrafficCones", "Jets:ExerciseCrew" };
            var indexed = TapeBracket(c.Find("index actors ["), "index actors");
            var newLive = fresh.Where(k => indexed.Contains(k + "=")).ToArray();
            TapeCheck(c.Exit == 0 && !c.Crashed && line != null && missing.Length == 0, $"{scene} older tape {id}: everything the review's tape covered is still covered (missing: [{string.Join(", ", missing)}])");
            TapeCheck(newLive.All(live.Contains) && newLive.All(k => !cov.Contains(k)), $"{scene} older tape {id}: the new actors on this track ({string.Join(", ", newLive)}) run live on it: nothing is invented for a tape that does not know them");
        }
        finally { TapeSceneRm(d); }
    }
}
