using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CCCPRacing.Replay;
using Godot;

namespace CCCPRacing.Tests;

// Phase 4B persisted edits: model, validation, binding and the archive's LoadEdits/SaveEdits/ResetEdits (src/Replay/LapEdits.cs, LapArchive.Edits.cs). Marker lines start with "LAPEDITS:".
public partial class PhysicsTest
{
    sealed class EditsFx
    {
        public LapArchive A; public LapFaultOps Ops; public string Id, Dir, LapDir; public LapMeta Meta;
        public string Edits => Path.Combine(LapDir, "edits.json");
        public string Gp => Path.Combine(LapDir, "ghost.bin");
        public string Tp => Path.Combine(LapDir, "tape.bin");
        public string Mp => Path.Combine(LapDir, "meta.json");
        public string PairHash() => string.Join("|", new[] { Gp, Tp, Mp }.Select(p => File.Exists(p) ? LapHex(File.ReadAllBytes(p)) : "-"));
        public string[] Files(string pat) => Directory.GetFiles(LapDir, pat).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    EditsFx EditsFixture(LapRig rig, string dir, bool withTape = true, float seconds = 4f)
    {
        var ops = new LapFaultOps(); var a = new LapArchive(dir, ops);
        var g = rig.Drive(seconds, 20, 15);
        var id = a.Submit(LapCap(g, withTape ? rig.Rec : null, utc: DateTime.UtcNow)).Id; a.Flush(TimeSpan.FromSeconds(30));
        var fx = new EditsFx { A = a, Ops = ops, Id = id, Dir = dir, LapDir = Path.Combine(dir, id) };
        return fx;
    }

    void EditsRetain(EditsFx fx)
    {
        Check(fx.A.Retain(fx.Id, "edit me").Result.Ok, "lap edits: fixture lap retained");
        fx.Meta = fx.A.List().First(x => x.Id == fx.Id).Meta;
    }

    static LapEdits Choices(float dur) => new()
    {
        Pinned = true, TrimEnabled = true, In = dur * 0.25f, Out = dur * 0.75f,
        CameraKeys = new() { new LapCameraKey { T = 0.5f, Mode = "Bonnet" }, new LapCameraKey { T = 1.5f, Mode = "Auto" } },
        Playback = new LapEditsPlayback { Speed = 0.5f, Loop = true, Camera = "Cinematic", Overlay = false },
    };

    void LapEditsTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "cccp-lapedits-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var rig = new LapRig(this, 6, 3);
        int n0 = 0; string Dir() => Path.Combine(root, "e" + n0++);
        string H64 = new('a', 64);
        try
        {
            // ---- (1) the model: round trip, defaults, every invalid class
            {
                var meta = new LapMeta { Id = "x", LapSeconds = 10f, GhostFingerprint = 77 };
                var d = LapEdits.Default(meta, H64, LapEdits.Absent, 0);
                var c = Choices(10f) with { GhostSha256 = H64, TapeSha256 = H64, GhostFingerprint = 77, TapeBytes = 1234, LapSeconds = 10f };
                var k = LapEdits.TryParse(c.ToJson(), out var back, out var why);
                Check(k == LapEditsLoadKind.Ok && back.ToJson() == c.ToJson() && back.Validate(10f) == null && back.In == 2.5f && back.Out == 7.5f && back.CameraKeys.Count == 2 && back.CameraKeys[1].Mode == "Auto" && !back.Playback.Overlay && back.Playback.Loop && back.Playback.Speed == 0.5f && back.Pinned,
                    $"lap edits 1: JSON round trip keeps every field ({why})");
                Check(d.Validate(10f) == null && d.TrimEnabled && d.In == null && d.Out == null && d.CameraKeys.Count == 0 && d.Playback.Speed == 1f && d.Playback.Camera == "TopDown" && d.Playback.Overlay && !d.Pinned && d.TapeSha256 == "absent", "lap edits 1: the default sidecar is valid and neutral");
                string minimal = "{\"GhostSha256\":\"" + H64 + "\",\"TapeSha256\":\"absent\",\"LapSeconds\":10,\"Surprise\":{\"x\":1}}";
                var km = LapEdits.TryParse(minimal, out var m1, out _);
                Check(km == LapEditsLoadKind.Ok && m1.Validate(10f) == null && m1.Version == 1 && m1.TrimEnabled && m1.CameraKeys.Count == 0 && m1.Playback.Speed == 1f && m1.In == null, "lap edits 1: missing optional fields take defaults, unknown members are ignored");
                string P(LapEdits e) => e.Validate(10f);
                Check(P(c) == null, "lap edits 1: valid fixture is valid");
                Check(P(c with { In = float.NaN }) != null && P(c with { Out = float.PositiveInfinity }) != null && P(c with { In = float.NegativeInfinity, Out = null }) != null, "lap edits 1: NaN and infinity are refused");
                Check(P(c with { In = 5f, Out = 5f }) != null && P(c with { In = 6f, Out = 5f }) != null && P(c with { In = 5f, Out = 6f }) == null, "lap edits 1: In >= Out refused, In < Out accepted");
                Check(P(c with { In = -0.1f }) != null && P(c with { Out = 10.01f }) != null && P(c with { In = 11f, Out = null }) != null && P(c with { In = null, Out = 10f }) == null && P(c with { In = 0f, Out = null }) == null, "lap edits 1: out of range refused, an open end and the exact bounds accepted");
                Check(P(c with { CameraKeys = new() { new() { T = 2f, Mode = "Auto" }, new() { T = 1f, Mode = "Auto" } } }) != null
                    && P(c with { CameraKeys = new() { new() { T = 1f, Mode = "Auto" }, new() { T = 1f, Mode = "Bonnet" } } }) != null
                    && P(c with { CameraKeys = new() { new() { T = 11f, Mode = "Auto" } } }) != null && P(c with { CameraKeys = new() { new() { T = -1f, Mode = "Auto" } } }) != null
                    && P(c with { CameraKeys = new() { new() { T = float.NaN, Mode = "Auto" } } }) != null && P(c with { CameraKeys = new() { null } }) != null, "lap edits 1: unsorted, duplicate, out-of-range, NaN and empty camera keys refused");
                Check(P(c with { CameraKeys = new() { new() { T = 1f, Mode = "Drone" } } }) != null && P(c with { CameraKeys = new() { new() { T = 1f, Mode = "" } } }) != null && P(c with { CameraKeys = new() { new() { T = 1f, Mode = "topdown" } } }) != null
                    && P(c with { Playback = new() { Camera = "Free" } }) != null, "lap edits 1: an unknown or empty camera mode is refused");
                Check(P(c with { Playback = new() { Speed = 3f } }) != null && P(c with { Playback = new() { Speed = 0f } }) != null && P(c with { Playback = new() { Speed = float.NaN } }) != null && LapEdits.Speeds.All(s => P(c with { Playback = new() { Speed = s } }) == null), "lap edits 1: speed must be from 0.25 / 0.5 / 1 / 2");
                Check(P(c with { GhostSha256 = "xyz" }) != null && P(c with { GhostSha256 = "" }) != null && P(c with { TapeSha256 = "ABSENT" }) != null && P(c with { TapeSha256 = LapEdits.Absent }) == null && P(c with { LapSeconds = float.NaN }) != null && P(c with { Version = LapEdits.CurrentVersion + 1 }) != null, "lap edits 1: binding fields are strict (sentinel 'absent' accepted)");
                Check(P(c with { CameraKeys = Enumerable.Range(0, 300).Select(i => new LapCameraKey { T = i * 0.03f, Mode = "Auto" }).ToList() }) != null, "lap edits 1: more than 256 keys refused");
                Check(LapEdits.TryParse("{\"Version\":999,\"Anything\":[1]}", out _, out var nv) == LapEditsLoadKind.NewerVersion && nv.Contains("newer")
                    && LapEdits.TryParse("{\"Version\":\"one\"}", out _, out _) == LapEditsLoadKind.Invalid && LapEdits.TryParse("{not json", out _, out var gj) == LapEditsLoadKind.Invalid && gj.Length > 0
                    && LapEdits.TryParse("[1,2]", out _, out _) == LapEditsLoadKind.Invalid && LapEdits.TryParse("", out _, out _) == LapEditsLoadKind.Invalid && LapEdits.TryParse("null", out _, out _) == LapEditsLoadKind.Invalid
                    && LapEdits.TryParse("{\"In\":NaN}", out _, out _) == LapEditsLoadKind.Invalid && (LapEdits.TryParse("{\"In\":1e999}", out var ov, out _) != LapEditsLoadKind.Ok || ov.Validate(10f) != null)
                    && LapEdits.TryParse(minimal.Replace("\"LapSeconds\":10", "\"LapSeconds\":\"ten\""), out _, out _) == LapEditsLoadKind.Invalid, "lap edits 1: newer version, garbage, wrong shape, NaN and overflowing numbers are classified, never thrown");
            }

            // ---- (2) archive: non-retained laps are refused, a kept lap round-trips, list markers, load never writes
            {
                var fx = EditsFixture(rig, Dir());
                var l0 = fx.A.LoadEdits(fx.Id).Result; var s0 = fx.A.SaveEdits(fx.Id, Choices(4f)).Result; var r0 = fx.A.ResetEdits(fx.Id).Result;
                Check(l0.Kind == LapEditsLoadKind.Refused && l0.Reason.Contains("keep it first") && !s0.Ok && s0.Message.Contains("keep it first") && !r0.Ok && r0.Message.Contains("keep it first") && s0.Id == fx.Id && !Directory.Exists(fx.LapDir),
                    $"lap edits 2: a lap that is not kept is refused by load, save and reset ('{s0.Message}') and nothing is written");
                var nx = fx.A.LoadEdits("lap-nope").Result;
                Check(nx.Kind == LapEditsLoadKind.Refused && !fx.A.SaveEdits("lap-nope", Choices(4f)).Result.Ok, "lap edits 2: an unknown id is refused");
                EditsRetain(fx);
                string pair0 = fx.PairHash();
                Check(fx.A.LoadEdits(fx.Id).Result.Kind == LapEditsLoadKind.Missing && fx.A.List().First(x => x.Id == fx.Id).EditsState == LapEditsState.None, "lap edits 2: no sidecar = Missing, marker None");
                float dur = fx.Meta.LapSeconds;
                var want = Choices(dur);
                var sv = fx.A.SaveEdits(fx.Id, want).Result;
                var ld = fx.A.LoadEdits(fx.Id).Result;
                Check(sv.Ok && sv.Id == fx.Id && sv.Edits != null && ld.Kind == LapEditsLoadKind.Ok && ld.Edits.ToJson() == sv.Edits.ToJson() && ld.Edits.Pinned && ld.Edits.In == want.In && ld.Edits.Out == want.Out && ld.Edits.CameraKeys.Count == 2
                    && ld.Edits.Playback.Camera == "Cinematic" && ld.Edits.Playback.Speed == 0.5f && ld.Edits.GhostFingerprint == fx.Meta.GhostFingerprint && ld.Edits.TapeBytes == new FileInfo(fx.Tp).Length && ld.Edits.TapeSha256 != "absent"
                    && ld.Edits.GhostSha256 == LapHex(File.ReadAllBytes(fx.Gp)).ToLowerInvariant() && ld.Edits.TapeSha256 == LapHex(File.ReadAllBytes(fx.Tp)).ToLowerInvariant(),
                    $"lap edits 2: save then load returns the same choices, bound to the SHA-256 of both files ({sv.Message}{ld.Reason})");
                Check(fx.Files("edits.json*").SequenceEqual(new[] { "edits.json" }), "lap edits 2: first save leaves exactly edits.json (no tmp, no bak)");
                var fresh = new LapArchive(fx.Dir).List().First(x => x.Id == fx.Id);
                Check(fresh.Pinned && fresh.HasTrim && fresh.HasCameraKeys && fresh.EditsState == LapEditsState.Ok && fx.A.List().First(x => x.Id == fx.Id).Pinned, "lap edits 2: a fresh listing shows Pinned / HasTrim / HasCameraKeys / Ok without hashing");
                var plain = fx.A.SaveEdits(fx.Id, new LapEdits()).Result;
                var fresh2 = new LapArchive(fx.Dir).List().First(x => x.Id == fx.Id);
                Check(plain.Ok && !fresh2.Pinned && !fresh2.HasTrim && !fresh2.HasCameraKeys && fresh2.EditsState == LapEditsState.Ok, "lap edits 2: default choices give plain markers, state Ok");
                // old API consumers: the old 8-argument construction still works
                var legacy = new LapEntryInfo(fx.Meta, LapEntryState.Ready, "", true, false, 1, true, fx.LapDir);
                Check(!legacy.Pinned && legacy.EditsState == LapEditsState.None, "lap edits 2: LapEntryInfo keeps its old constructor shape");
                // a load of a mismatching sidecar rewrites nothing
                string before = LapHex(File.ReadAllBytes(fx.Edits)); var mt = File.GetLastWriteTimeUtc(fx.Edits);
                File.AppendAllText(fx.Gp, "");              // untouched
                Check(fx.A.LoadEdits(fx.Id).Result.Kind == LapEditsLoadKind.Ok && LapHex(File.ReadAllBytes(fx.Edits)) == before && File.GetLastWriteTimeUtc(fx.Edits) == mt, "lap edits 2: a load does not rewrite the sidecar");
                // invalid edits are refused before any IO
                string e0 = LapHex(File.ReadAllBytes(fx.Edits)); int nfiles = fx.Files("edits.json*").Length;
                var rIn = fx.A.SaveEdits(fx.Id, new LapEdits { In = 3f, Out = 1f }).Result; var rKey = fx.A.SaveEdits(fx.Id, new LapEdits { CameraKeys = new() { new() { T = 2f, Mode = "Auto" }, new() { T = 1f, Mode = "Auto" } } }).Result;
                var rDur = fx.A.SaveEdits(fx.Id, new LapEdits { Out = dur + 1f }).Result; var rNull = fx.A.SaveEdits(fx.Id, null).Result;
                Check(!rIn.Ok && rIn.Message.Contains("In") && !rKey.Ok && !rDur.Ok && !rNull.Ok && LapHex(File.ReadAllBytes(fx.Edits)) == e0 && fx.Files("edits.json*").Length == nfiles, "lap edits 2: invalid edits are refused with a reason and nothing changes on disk");
                Check(fx.PairHash() == pair0, "lap edits 2: ghost.bin, tape.bin and meta.json are byte-identical after load/save/refusals");
            }

            // ---- (3) binding: a same-length mutation of either file is a mismatch; no silent rebind; cached hashing
            {
                var fx = EditsFixture(rig, Dir()); EditsRetain(fx);
                var sv = fx.A.SaveEdits(fx.Id, Choices(fx.Meta.LapSeconds)).Result;
                int runs1 = fx.A.HashRuns;
                var l1 = fx.A.LoadEdits(fx.Id).Result; var l2 = fx.A.LoadEdits(fx.Id).Result; var s2 = fx.A.SaveEdits(fx.Id, new LapEdits { Pinned = true }).Result;
                Check(sv.Ok && l1.Kind == LapEditsLoadKind.Ok && l2.Kind == LapEditsLoadKind.Ok && s2.Ok && runs1 == 2 && fx.A.HashRuns == 2, $"lap edits 3: the pair is hashed once (2 files), later loads and saves reuse the cache ({runs1} then {fx.A.HashRuns})");
                fx.A.SaveEdits(fx.Id, Choices(fx.Meta.LapSeconds));
                fx.A.Flush(TimeSpan.FromSeconds(10));
                string sidecar = LapHex(File.ReadAllBytes(fx.Edits)); string pair = fx.PairHash();
                foreach (var target in new[] { fx.Gp, fx.Tp })
                {
                    var orig = File.ReadAllBytes(target); var origTime = File.GetLastWriteTimeUtc(target);
                    var mut = (byte[])orig.Clone(); mut[mut.Length / 2] ^= 0x01;
                    File.WriteAllBytes(target, mut); File.SetLastWriteTimeUtc(target, origTime.AddSeconds(9));
                    var lm = fx.A.LoadEdits(fx.Id).Result; var row = fx.A.List().First(x => x.Id == fx.Id);
                    bool right = lm.Kind == LapEditsLoadKind.Mismatch && lm.Reason.Contains(Path.GetFileName(target)) && lm.Edits == null;
                    var sm = fx.A.SaveEdits(fx.Id, new LapEdits { Pinned = false }).Result;      // a save on a mutated pair writes a new binding, the old mismatching file is preserved as .bad
                    Check(right && row.EditsState == LapEditsState.Mismatch && !row.Pinned, $"lap edits 3: a same-length change of {Path.GetFileName(target)} is a Mismatch ({lm.Kind}: {lm.Reason}) and the marker follows");
                    Check(sm.Ok && fx.Files("edits.json.bad-*").Length == 1 && LapHex(File.ReadAllBytes(fx.Files("edits.json.bad-*").Select(f => Path.Combine(fx.LapDir, f)).First())) == sidecar, $"lap edits 3: saving over a mismatching sidecar preserves the old bytes as .bad-1 ({sm.Ok} {sm.Message} {string.Join(",", fx.Files("edits.json*"))})");
                    File.WriteAllBytes(target, orig); File.SetLastWriteTimeUtc(target, origTime);
                    foreach (var f in fx.Files("edits.json.bad-*")) File.Delete(Path.Combine(fx.LapDir, f));      // the test's own cleanup only
                    fx.A.SaveEdits(fx.Id, Choices(fx.Meta.LapSeconds)).Wait(30000);      // the sidecar bound to the mutated pair now mismatches the restored one: it becomes .bad again
                    foreach (var f in fx.Files("edits.json.bad-*")) File.Delete(Path.Combine(fx.LapDir, f));
                    sidecar = LapHex(File.ReadAllBytes(fx.Edits));
                    Check(fx.A.LoadEdits(fx.Id).Result.Kind == LapEditsLoadKind.Ok, "lap edits 3: restored pair and a fresh save load Ok again");
                }
                Check(fx.PairHash() == pair, "lap edits 3: the pair is back to the original bytes (the test restored it) and the code never wrote it");
                // no silent rebind: mutate, load, the sidecar bytes stay
                var ob = File.ReadAllBytes(fx.Tp); var ot = File.GetLastWriteTimeUtc(fx.Tp); var mb = (byte[])ob.Clone(); mb[7] ^= 0x10; File.WriteAllBytes(fx.Tp, mb); File.SetLastWriteTimeUtc(fx.Tp, ot.AddSeconds(5));
                var filesBefore = fx.Files("edits.json*"); string sc = LapHex(File.ReadAllBytes(fx.Edits)); var smt = File.GetLastWriteTimeUtc(fx.Edits);
                var lx = fx.A.LoadEdits(fx.Id).Result; var lx2 = new LapArchive(fx.Dir).LoadEdits(fx.Id).Result;
                Check(lx.Kind == LapEditsLoadKind.Mismatch && lx2.Kind == LapEditsLoadKind.Mismatch && LapHex(File.ReadAllBytes(fx.Edits)) == sc && File.GetLastWriteTimeUtc(fx.Edits) == smt && fx.Files("edits.json*").SequenceEqual(filesBefore), "lap edits 3: a mismatching sidecar is never rebound or rewritten by a load, also from a new archive instance");
                File.WriteAllBytes(fx.Tp, ob); File.SetLastWriteTimeUtc(fx.Tp, ot);
                // legacy fields
                var good = File.ReadAllText(fx.Edits);
                void Hand(string text) { File.WriteAllText(fx.Edits, text); }
                var ed = LapEdits.TryParse(good, out var parsed, out _) == LapEditsLoadKind.Ok ? parsed : null;
                Hand((ed with { GhostFingerprint = ed.GhostFingerprint + 1 }).ToJson()); var mf = new LapArchive(fx.Dir).LoadEdits(fx.Id).Result;
                Hand((ed with { TapeBytes = ed.TapeBytes + 1 }).ToJson()); var mtb = new LapArchive(fx.Dir).LoadEdits(fx.Id).Result;
                Hand((ed with { LapSeconds = ed.LapSeconds + 0.01f }).ToJson()); var md = new LapArchive(fx.Dir).LoadEdits(fx.Id).Result;
                Hand((ed with { LapSeconds = ed.LapSeconds + 0.0004f }).ToJson()); var mok = new LapArchive(fx.Dir).LoadEdits(fx.Id).Result;
                Check(mf.Kind == LapEditsLoadKind.Mismatch && mf.Reason.Contains("fingerprint") && mtb.Kind == LapEditsLoadKind.Mismatch && mtb.Reason.Contains("tape size") && md.Kind == LapEditsLoadKind.Mismatch && md.Reason.Contains("duration") && mok.Kind == LapEditsLoadKind.Ok,
                    "lap edits 3: the legacy fingerprint, tape size and duration (1 ms) are checked too");
                Hand((ed with { Out = fx.Meta.LapSeconds + 5f }).ToJson()); var lr = new LapArchive(fx.Dir).LoadEdits(fx.Id).Result;
                Hand("{ \"Version\": 7 }"); var ln = new LapArchive(fx.Dir).LoadEdits(fx.Id).Result; var rowN = new LapArchive(fx.Dir).List().First(x => x.Id == fx.Id);
                Hand("garbage{"); var lg = new LapArchive(fx.Dir).LoadEdits(fx.Id).Result; var rowG = new LapArchive(fx.Dir).List().First(x => x.Id == fx.Id);
                Check(lr.Kind == LapEditsLoadKind.Invalid && lr.Reason.Contains("Out") && ln.Kind == LapEditsLoadKind.NewerVersion && rowN.EditsState == LapEditsState.NewerVersion && lg.Kind == LapEditsLoadKind.Invalid && rowG.EditsState == LapEditsState.Unreadable && !rowG.Pinned,
                    $"lap edits 3: out-of-range / newer / garbage sidecars are Invalid / NewerVersion / Invalid with the list markers NewerVersion / Unreadable ({lr.Reason})");
                Check(File.ReadAllText(fx.Edits) == "garbage{", "lap edits 3: the unreadable sidecar was left untouched by load and list");
            }

            // ---- (4) absent tape sentinel
            {
                var fx = EditsFixture(rig, Dir(), withTape: false); EditsRetain(fx);
                var sv = fx.A.SaveEdits(fx.Id, new LapEdits { Pinned = true }).Result; var ld = fx.A.LoadEdits(fx.Id).Result;
                Check(!File.Exists(fx.Tp) && sv.Ok && ld.Kind == LapEditsLoadKind.Ok && ld.Edits.TapeSha256 == "absent" && ld.Edits.TapeBytes == 0, "lap edits 4: a ghost-only lap binds the tape as 'absent'");
                // a tape appears later (same ghost): no longer absent -> mismatch
                var other = EditsFixture(rig, Dir()); EditsRetain(other);
                File.Copy(other.Tp, fx.Tp);
                var lm = new LapArchive(fx.Dir).LoadEdits(fx.Id).Result;
                Check(lm.Kind == LapEditsLoadKind.Mismatch && lm.Reason.Contains("tape"), $"lap edits 4: a tape that appears after the save is a Mismatch ({lm.Reason})");
                // and the other way: a sidecar bound to a tape whose file vanished
                File.Delete(fx.Tp);
                var sv2 = fx.A.SaveEdits(other.Id, Choices(4f)).Result;      // other archive's lap is unknown here: refused
                Check(!sv2.Ok, "lap edits 4: an id from another archive is refused");
                var s3 = other.A.SaveEdits(other.Id, Choices(other.Meta.LapSeconds)).Result; File.Delete(other.Tp);
                var lv = new LapArchive(other.Dir).LoadEdits(other.Id).Result;
                Check(s3.Ok && lv.Kind == LapEditsLoadKind.Mismatch, $"lap edits 4: a tape that vanished after the save is a Mismatch ({lv.Reason})");
            }

            // ---- (5) fault injection at every save step: the old sidecar is intact, nothing half-written is left
            {
                var fx = EditsFixture(rig, Dir()); EditsRetain(fx);
                float dur = fx.Meta.LapSeconds;
                Check(fx.A.SaveEdits(fx.Id, Choices(dur)).Result.Ok, "lap edits 5: sidecar A saved");
                string pair = fx.PairHash(); byte[] aBytes = File.ReadAllBytes(fx.Edits); string aHash = LapHex(aBytes);
                var B = new LapEdits { Pinned = false, In = 0.1f, Out = 0.9f };
                var seen = new List<string>(); fx.Ops.FailStep = s => { seen.Add(s); return false; };
                Check(fx.A.SaveEdits(fx.Id, B).Result.Ok, "lap edits 5: sidecar B saved normally (records the step order)");
                fx.Ops.FailStep = null;
                Check(seen.SequenceEqual(new[] { "edits-tmp-written", "edits-verified", "edits-bak-staged", "edits-bak-written", "edits-before-replace", "edits-after-replace" }), "lap edits 5: step order is tmp, verify, bak, replace (" + string.Join(",", seen) + ")");
                Check(File.ReadAllBytes(fx.Files("edits.json.bak").Select(f => Path.Combine(fx.LapDir, f)).First()).AsSpan().SequenceEqual(aBytes), "lap edits 5: the previous good file is kept as edits.json.bak");
                // restore A as the live sidecar for the injection rounds
                File.WriteAllBytes(fx.Edits, aBytes);
                foreach (var step in new[] { "edits-tmp-written", "edits-verified", "edits-bak-staged", "edits-bak-written", "edits-before-replace" })
                {
                    fx.Ops.FailStep = s => s == step;
                    var r = fx.A.SaveEdits(fx.Id, B).Result; fx.Ops.FailStep = null;
                    var l = fx.A.LoadEdits(fx.Id).Result;
                    Check(!r.Ok && r.Message.Contains(step) && LapHex(File.ReadAllBytes(fx.Edits)) == aHash && l.Kind == LapEditsLoadKind.Ok && l.Edits.Pinned && fx.Files("*.tmp").Length == 0 && fx.Files("edits.json.bad*").Length == 0 && fx.PairHash() == pair,
                        $"lap edits 5: a failure at {step} leaves sidecar A intact and loadable, no tmp, no .bad, pair untouched ({r.Message})");
                }
                foreach (var which in new[] { "edits.json", "edits.json.bak" })
                {
                    fx.Ops.FailPlace = f => f.EndsWith(which);
                    var r = fx.A.SaveEdits(fx.Id, B).Result; fx.Ops.FailPlace = null;
                    Check(!r.Ok && LapHex(File.ReadAllBytes(fx.Edits)) == aHash && fx.A.LoadEdits(fx.Id).Result.Kind == LapEditsLoadKind.Ok && fx.Files("*.tmp").Length == 0, $"lap edits 5: a failed placement of {which} leaves sidecar A intact ({r.Message})");
                }
                // a failure after the replace: the new file is in place and good, still no tmp
                fx.Ops.FailStep = s => s == "edits-after-replace";
                var ra = fx.A.SaveEdits(fx.Id, B).Result; fx.Ops.FailStep = null;
                var la = fx.A.LoadEdits(fx.Id).Result;
                Check(!ra.Ok && la.Kind == LapEditsLoadKind.Ok && !la.Edits.Pinned && fx.Files("*.tmp").Length == 0, "lap edits 5: a failure after the replace is reported, the file on disk is the new good one and no tmp is left");
                // the busy flag is released after failures
                Check(fx.A.SaveEdits(fx.Id, B).Result.Ok, "lap edits 5: the lap can be saved again after failures");
                File.WriteAllBytes(fx.Edits, aBytes);

                // an invalid existing file: the new one is staged and verified first, the old is preserved as .bad-n only afterwards
                File.WriteAllText(fx.Edits, "{ broken");
                string brokenHash = LapHex(File.ReadAllBytes(fx.Edits)); string bakBefore = string.Join(",", fx.Files("edits.json.bak").Select(f => LapHex(File.ReadAllBytes(Path.Combine(fx.LapDir, f)))));
                foreach (var step in new[] { "edits-tmp-written", "edits-verified" })
                {
                    fx.Ops.FailStep = s => s == step; var r = fx.A.SaveEdits(fx.Id, B).Result; fx.Ops.FailStep = null;
                    Check(!r.Ok && fx.Files("edits.json.bad*").Length == 0 && LapHex(File.ReadAllBytes(fx.Edits)) == brokenHash && fx.Files("*.tmp").Length == 0, $"lap edits 5: a failure at {step} does not touch the broken sidecar and creates no .bad yet");
                }
                seen.Clear(); fx.Ops.FailStep = s => { seen.Add(s); return s == "edits-bad-written"; }; var rb = fx.A.SaveEdits(fx.Id, B).Result; fx.Ops.FailStep = null;
                string[] bad1 = fx.Files("edits.json.bad-*");
                Check(!rb.Ok && bad1.SequenceEqual(new[] { "edits.json.bad-1" }) && LapHex(File.ReadAllBytes(fx.Edits)) == brokenHash && fx.Files("*.tmp").Length == 0, "lap edits 5: a failure after the .bad copy keeps the broken sidecar in place (a copy, never a move)");
                int iv = seen.IndexOf("edits-verified"), ib = seen.IndexOf("edits-bad-staged"), iw = seen.IndexOf("edits-bad-written");
                var rok = fx.A.SaveEdits(fx.Id, B).Result;
                string[] bad2 = fx.Files("edits.json.bad-*");
                Check(rok.Ok && iv >= 0 && ib > iv && iw > ib && !seen.Contains("edits-before-replace") && bad2.SequenceEqual(new[] { "edits.json.bad-1" }) && LapHex(File.ReadAllBytes(Path.Combine(fx.LapDir, bad2[0]))) == brokenHash && fx.A.LoadEdits(fx.Id).Result.Kind == LapEditsLoadKind.Ok && string.Join(",", fx.Files("edits.json.bak").Select(f => LapHex(File.ReadAllBytes(Path.Combine(fx.LapDir, f))))) == bakBefore,
                    "lap edits 5: the broken file is preserved as .bad-1 only after the new one was staged and verified, a retry does not duplicate it, and a broken file does not become the .bak");
                // a second broken generation gets .bad-2, .bad files are never overwritten
                File.WriteAllText(fx.Edits, "{ other broken");
                Check(fx.A.SaveEdits(fx.Id, B).Result.Ok && fx.Files("edits.json.bad-*").SequenceEqual(new[] { "edits.json.bad-1", "edits.json.bad-2" }) && File.ReadAllText(Path.Combine(fx.LapDir, "edits.json.bad-1")) == "{ broken", "lap edits 5: a new broken sidecar gets the next .bad number, the old .bad is untouched");
                Check(fx.PairHash() == pair, "lap edits 5: the pair is byte-identical after all the failures");
            }

            // ---- (6) reset
            {
                var fx = EditsFixture(rig, Dir()); EditsRetain(fx);
                float dur = fx.Meta.LapSeconds; string pair = fx.PairHash();
                var rm = fx.A.ResetEdits(fx.Id).Result;
                Check(rm.Ok && fx.A.LoadEdits(fx.Id).Result.Kind == LapEditsLoadKind.Ok && fx.Files("edits.json*").SequenceEqual(new[] { "edits.json" }), "lap edits 6: a reset with no sidecar saves a valid default one");
                var A = Choices(dur); Check(fx.A.SaveEdits(fx.Id, A).Result.Ok, "lap edits 6: A saved");
                var Bq = new LapEdits { Pinned = true, In = 0.5f };
                Check(fx.A.SaveEdits(fx.Id, Bq).Result.Ok, "lap edits 6: B saved (A is now the .bak)");
                byte[] bBytes = File.ReadAllBytes(fx.Edits);
                File.WriteAllText(Path.Combine(fx.LapDir, "edits.json.bad-1"), "old bad"); string badHash = LapHex(File.ReadAllBytes(Path.Combine(fx.LapDir, "edits.json.bad-1")));
                var rr = fx.A.ResetEdits(fx.Id).Result; var lr = fx.A.LoadEdits(fx.Id).Result;
                Check(rr.Ok && lr.Kind == LapEditsLoadKind.Ok && !lr.Edits.Pinned && !lr.Edits.HasTrim && !lr.Edits.HasCameraKeys && lr.Edits.GhostSha256 == fx.A.LoadEdits(fx.Id).Result.Edits.GhostSha256,
                    "lap edits 6: reset gives a valid default sidecar bound to the pair");
                Check(File.ReadAllBytes(Path.Combine(fx.LapDir, "edits.json.bak")).AsSpan().SequenceEqual(bBytes), "lap edits 6: the previous sidecar B is kept as edits.json.bak");
                Check(LapHex(File.ReadAllBytes(Path.Combine(fx.LapDir, "edits.json.bad-1"))) == badHash && fx.Files("edits.json.bad-*").Length == 1 && fx.PairHash() == pair && fx.Files("*.tmp").Length == 0, "lap edits 6: reset leaves .bad files, the pair and meta alone");
                var again = fx.A.LoadEdits(fx.Id).Result; var freshRow = new LapArchive(fx.Dir).List().First(x => x.Id == fx.Id);
                Check(again.Kind == LapEditsLoadKind.Ok && !again.Edits.Pinned && !freshRow.Pinned && freshRow.EditsState == LapEditsState.Ok, "lap edits 6: the .bak is never resurrected (a load after the reset is still the default)");
                // reset over a broken file: preserved as .bad, .bak is not replaced by garbage
                byte[] bakNow = File.ReadAllBytes(Path.Combine(fx.LapDir, "edits.json.bak"));
                File.WriteAllText(fx.Edits, "{ nope");
                var rb = fx.A.ResetEdits(fx.Id).Result;
                Check(rb.Ok && fx.Files("edits.json.bad-*").Length == 2 && File.ReadAllText(Path.Combine(fx.LapDir, "edits.json.bad-2")) == "{ nope" && File.ReadAllBytes(Path.Combine(fx.LapDir, "edits.json.bak")).AsSpan().SequenceEqual(bakNow) && fx.A.LoadEdits(fx.Id).Result.Kind == LapEditsLoadKind.Ok,
                    "lap edits 6: a reset over a broken sidecar preserves it as .bad-2 and keeps the .bak");
                // a failing reset leaves the old sidecar
                Check(fx.A.SaveEdits(fx.Id, Bq).Result.Ok, "lap edits 6: B saved again");
                string keep = LapHex(File.ReadAllBytes(fx.Edits));
                foreach (var step in new[] { "edits-verified", "edits-bak-written", "edits-before-replace" })
                {
                    fx.Ops.FailStep = s => s == step; var r = fx.A.ResetEdits(fx.Id).Result; fx.Ops.FailStep = null;
                    Check(!r.Ok && LapHex(File.ReadAllBytes(fx.Edits)) == keep && fx.Files("*.tmp").Length == 0 && fx.A.LoadEdits(fx.Id).Result.Edits.Pinned, $"lap edits 6: a reset failing at {step} keeps the sidecar");
                }
                Check(fx.PairHash() == pair, "lap edits 6: the pair is byte-identical after all resets");
            }

            // ---- (7) worker identity: a second save of the same lap while busy is refused visibly, other work waits its turn
            {
                var fx = EditsFixture(rig, Dir()); EditsRetain(fx);
                var reached = new ManualResetEventSlim(false); var gate = new ManualResetEventSlim(false);
                fx.A.WorkerHook = s => { if (s.StartsWith("edits-save:")) { reached.Set(); gate.Wait(30000); } };
                fx.A.DrainMessages();
                var t1 = fx.A.SaveEdits(fx.Id, new LapEdits { Pinned = true });
                bool held = reached.Wait(20000);
                var t2 = fx.A.SaveEdits(fx.Id, new LapEdits { Pinned = false }); var t3 = fx.A.ResetEdits(fx.Id);
                Check(held && t2.IsCompleted && !t2.Result.Ok && t2.Result.Message.Contains("already") && t3.IsCompleted && !t3.Result.Ok && !t1.IsCompleted, "lap edits 7: a second save and a reset of the same lap while busy are refused at once");
                Check(fx.A.DrainMessages().Any(m => m.Contains("cannot save edits") && m.Contains("already being saved")), "lap edits 7: the refusal is a visible archive message");
                var tl = fx.A.LoadEdits(fx.Id); Check(!tl.IsCompleted, "lap edits 7: a load queues behind the running save");
                gate.Set();
                Check(t1.Wait(30000) && t1.Result.Ok && tl.Wait(30000) && tl.Result.Kind == LapEditsLoadKind.Ok && tl.Result.Edits.Pinned && t1.Result.Id == fx.Id, "lap edits 7: the first save completes with its own lap id, the queued load sees it");
                fx.A.WorkerHook = null;
                Check(fx.A.SaveEdits(fx.Id, new LapEdits()).Result.Ok, "lap edits 7: the lap is free again afterwards");
            }

            // ---- (8) lap edits never write the pair: SHA-256 across a whole session of operations, also a second archive in the same folder
            {
                var fx = EditsFixture(rig, Dir()); EditsRetain(fx); string pair = fx.PairHash();
                var times = new[] { fx.Gp, fx.Tp, fx.Mp }.Select(File.GetLastWriteTimeUtc).ToArray();
                for (int i = 0; i < 3; i++) { fx.A.SaveEdits(fx.Id, Choices(fx.Meta.LapSeconds) with { Pinned = i % 2 == 0 }).Wait(30000); fx.A.LoadEdits(fx.Id).Wait(30000); }
                fx.Ops.FailStep = s => s == "edits-bak-written"; fx.A.SaveEdits(fx.Id, new LapEdits()).Wait(30000); fx.Ops.FailStep = null;
                fx.A.ResetEdits(fx.Id).Wait(30000); new LapArchive(fx.Dir).LoadEdits(fx.Id).Wait(30000);
                Check(fx.PairHash() == pair && new[] { fx.Gp, fx.Tp, fx.Mp }.Select(File.GetLastWriteTimeUtc).SequenceEqual(times), "lap edits 8: ghost.bin, tape.bin and meta.json keep their SHA-256 and mtime across load/save/reset/failure");
                var src = new LapArchive(fx.Dir).Open(fx.Id, out var why);
                Check(src != null && src.HasTape, $"lap edits 8: the lap still opens with its tape ({why})");
            }
        }
        finally { rig.Dispose(); try { Directory.Delete(root, true); } catch { } }
    }
}
