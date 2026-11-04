using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Replay;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

// Phase 1 of the player replay: the lap archive core (src/Replay). Production-shaped laps: a real Ghost with damage states (V4), a real recorder over a TapeWorld, the real codec
// and the real SavePairBytes. Everything on disk goes under a temp folder that is removed at the end. Marker lines start with "LAPARCHIVE:".
public partial class PhysicsTest
{
    /// <summary>Injects failures into the file operations of the pair save and of the small files.</summary>
    sealed class LapFaultOps : ITapeFileOps
    {
        public Func<string, bool> FailStep, FailPlace;
        public readonly List<string> Warnings = new();
        public void Step(string name) { if (FailStep?.Invoke(name) == true) throw new IOException("injected failure at " + name); }
        public void Replace(string tmp, string final) { if (FailPlace?.Invoke(final) == true) throw new IOException("injected failure replacing " + Path.GetFileName(final)); WorldTapeRealOps.Instance.Replace(tmp, final); }
        public void Move(string tmp, string final) { if (FailPlace?.Invoke(final) == true) throw new IOException("injected failure moving " + Path.GetFileName(final)); WorldTapeRealOps.Instance.Move(tmp, final); }
        public void Warn(string message) { lock (Warnings) Warnings.Add(message); }
    }

    /// <summary>A live ghost, recorder, world and damaged car like the race scene keeps them; Drive() records one lap into them, reusing the same objects lap after lap.</summary>
    sealed class LapRig : IDisposable
    {
        public readonly TapeWorld World; public readonly CarVisual Vis; public readonly TyreEffects Fx; public readonly CarSim Car;
        public readonly Ghost Live = new(); public readonly WorldTapeRecorder Rec = new(); readonly IGround _ground = new FlatGround(); int _lap;
        public LapRig(Node host, int actors, int parts)
        {
            World = TapeWorld.Build(actors, parts, true);
            var prm = new CarParams(); Car = new CarSim(prm, _ground) { WorldMin = -1e6f, WorldMax = 1e6f }; Car.Reset(new Vector3(0, 0.8f, 0), 0);
            Vis = CarVisual.Create(prm, Colors.Red); host.AddChild(Vis);
            Fx = new TyreEffects(); Fx.Init(TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"))); host.AddChild(Fx);
        }
        /// <summary>Records one lap into the live objects (the recorder and the ghost are cleared first, like at the finish line) and returns the lap's ghost copy.</summary>
        public Ghost Drive(float seconds, int hitFrame, float hitStrength)
        {
            Live.Clear(); Rec.Clear(); float off = 40f * _lap; World.Tick(off); Rec.Begin(World.Index);
            int ticks = (int)MathF.Round(seconds * 60f); var dmg = Vis.Damage;
            for (int n = 0; n <= ticks; n++)
            {
                float t = n / 60f; World.Tick(off + t); Rec.Record(t);
                Car.Pos = new Vector3(_lap * 1000f + n * 0.4f, 0.8f, MathF.Sin(n * 0.01f + _lap) * 3); Car.Rot = new Basis(new Quaternion(Vector3.Up, n * 0.002f));
                if (n == hitFrame) dmg.Hit(hitStrength, new(.7f, .8f, -1.9f), Vector3.Back, Car, Fx);
                dmg.Tick(1f / 60, _ground);
                Live.Record(t + .00001f, Car, 0, 0, 0, 400, damage: dmg);
            }
            _lap++;
            return Live.CopyWithTime(seconds);
        }
        public void Dispose() { World.Dispose(); Vis.QueueFree(); Fx.QueueFree(); }
    }

    static LapCapture LapCap(Ghost g, WorldTapeRecorder rec, string track = "siberia", int set = 0, LapKind kind = LapKind.Normal, DateTime? utc = null, string sig = "sig-1")
        => LapCapture.FromLive(g, rec, track, set, "petrol", kind, kind != LapKind.Void, 12.5f, "profile-1", sig, utc);

    static WorldTape WithTrack(WorldTape t, string track, int set) { t.TrackId = track; t.Set = set; return t; }

    static string LapHex(ReadOnlySpan<byte> b) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b));

    void LapArchiveTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "cccp-lapark-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var rig = new LapRig(this, 6, 3);
        int n0 = 0; string Dir() => Path.Combine(root, "d" + n0++);
        try
        {
            // ---- (1) capture -> List -> Open round trip, production-shaped
            {
                var archive = new LapArchive(Dir());
                var ghost = rig.Drive(8f, 100, 30);
                byte[] expectGhost = ghost.ToBytes(); ulong fp = WorldTapeFingerprint.Of(ghost);
                var expectTape = WorldTapeRecorder.Compress(rig.Rec.Snapshot(), 8f, fp); expectTape.TrackId = "siberia"; expectTape.Set = 0;
                byte[] expectTapeBytes = WorldTapeCodec.Encode(expectTape);
                var cap = LapCap(ghost, rig.Rec, kind: LapKind.Record);
                var sub = archive.Submit(cap);
                Check(sub.Accepted && Regex.IsMatch(sub.Id, @"^lap-\d{14}-siberia-s0-[0-9a-f]{4}$"), $"lap archive 1: accepted with a well-formed id ({sub.Id})");
                Check(!archive.Submit(cap).Accepted, "lap archive 1: the same capture cannot be submitted twice");
                var list = archive.List();
                Check(list.Count == 1 && list[0].Id == sub.Id && list[0].Meta.Kind == LapKind.Record && list[0].Meta.Ranked && list[0].Meta.TrackId == "siberia" && Math.Abs(list[0].Meta.LapSeconds - 8f) < 1e-4f
                    && list[0].Meta.GhostFingerprint == fp && list[0].Meta.Profile == "profile-1" && list[0].Meta.TrackSignature == "sig-1" && list[0].Meta.Layout == RecordStore.Layout && !list[0].Retained && list[0].InMemory,
                    "lap archive 1: List shows the lap with its metadata, recent, not retained");
                var src = archive.Open(sub.Id, out var why);
                Check(src != null && src.GhostBytes.Span.SequenceEqual(expectGhost) && src.HasTape && src.TapeBytes.Span.SequenceEqual(expectTapeBytes),
                    $"lap archive 1: Open gives the ghost file's exact bytes and the tape the recorder gives ({why})");
                var tape = WorldTapeCodec.Decode(src.TapeBytes.ToArray());
                var fromBytes = LapGhostBytes.Fingerprint(src.GhostBytes.Span, out int samples, out float lt);
                Check(tape != null && WorldTapeCodec.Validate(tape) && tape.GhostFingerprint == fp && fromBytes == fp && samples == ghost.Count && Math.Abs(lt - 8f) < 1e-4f && ghost.HasDamage,
                    "lap archive 1: the tape decodes, validates and carries the ghost's fingerprint (also computed from the file bytes, same value as WorldTapeFingerprint.Of); ghost has damage (V4)");
                var parsed = LapMeta.TryParse("{\"Version\":1,\"Id\":\"x\",\"Kind\":\"Void\",\"Surprise\":[1,2],\"Name\":\"n\"}", out var tol, out _);
                Check(parsed && tol.Kind == LapKind.Void && tol.Name == "n" && tol.DayClock == -1 && !LapMeta.TryParse("{not json", out _, out var bad) && bad != null && LapMeta.TryParse(list[0].Meta.ToJson(), out var again, out _) && again == list[0].Meta,
                    "lap archive 1: meta.json is versioned, tolerant of unknown and missing fields, and round-trips");
                archive.Flush(TimeSpan.FromSeconds(10));
                GD.Print($"LAPARCHIVE: round trip ok id={sub.Id} ghost={expectGhost.Length} B tape={expectTapeBytes.Length} B");
            }

            // ---- (2) immutability: the live ghost, recorder and damage change while the worker is held
            {
                var archive = new LapArchive(Dir());
                var gate = new ManualResetEventSlim(false); var reached = new ManualResetEventSlim(false);
                archive.WorkerHook = s => { if (s.StartsWith("compress:")) { reached.Set(); gate.Wait(30000); } };
                var ghostA = rig.Drive(6f, 60, 20);
                var expectA = WorldTapeRecorder.Compress(rig.Rec.Snapshot(), 6f, WorldTapeFingerprint.Of(ghostA)); expectA.TrackId = "siberia"; expectA.Set = 0;
                byte[] expectTapeA = WorldTapeCodec.Encode(expectA), expectGhostA = ghostA.ToBytes();
                var idA = archive.Submit(LapCap(ghostA, rig.Rec)).Id;
                bool held = reached.Wait(20000);
                string hashBefore;
                var ghostB = rig.Drive(9f, 10, 60);           // the same live ghost, recorder and car: lap N+1 with more damage, while the worker still holds lap N's snapshot
                Check(held && ghostB.ToBytes().Length != expectGhostA.Length && !ghostB.ToBytes().AsSpan().SequenceEqual(expectGhostA), "lap archive 2: the live objects were reused for a different lap while the worker was held");
                gate.Set();
                var src = archive.Open(idA, out var why);
                hashBefore = src?.Sha256();
                string expected = LapHex(expectGhostA.Concat(new byte[] { 1 }).Concat(expectTapeA).ToArray());
                Check(src != null && src.GhostBytes.Span.SequenceEqual(expectGhostA) && src.TapeBytes.Span.SequenceEqual(expectTapeA), $"lap archive 2: lap N's ghost and tape are byte for byte lap N's, not lap N+1's ({why})");
                var idB = archive.Submit(LapCap(ghostB, rig.Rec)).Id; archive.Flush(TimeSpan.FromSeconds(30));
                rig.Drive(5f, 5, 90); archive.Flush(TimeSpan.FromSeconds(30));
                var again = archive.Open(idA);
                Check(again != null && again.Sha256() == hashBefore && archive.Open(idB) != null && archive.Open(idB).Sha256() != hashBefore, "lap archive 2: after more laps (and lap N+1 archived) lap N's source hash is unchanged");
                GD.Print($"LAPARCHIVE: immutability ok sha256={hashBefore}");
                // the same with the deferred capture: the worker is held before it has made the ghost bytes, and the live objects move on
                var archive2 = new LapArchive(Dir()); var gate2 = new ManualResetEventSlim(false); var reached2 = new ManualResetEventSlim(false);
                archive2.WorkerHook = s => { if (s.StartsWith("compress:")) { reached2.Set(); gate2.Wait(30000); } };
                var ghostC = rig.Drive(6f, 40, 30); byte[] expectGhostC = ghostC.ToBytes();
                var expC = WorldTapeRecorder.Compress(rig.Rec.Snapshot(), 6f, WorldTapeFingerprint.Of(ghostC)); expC.TrackId = "siberia"; expC.Set = 0; byte[] expectTapeC = WorldTapeCodec.Encode(expC);
                var idC = archive2.Submit(LapCapture.FromGhostCopy(rig.Live.CopyWithTime(6f), rig.Rec, "siberia", 0, "petrol", LapKind.Normal, true, -1, "p", "s")).Id;
                bool heldC = reached2.Wait(20000);
                rig.Drive(7f, 5, 80);
                gate2.Set();
                var srcC = archive2.Open(idC, out var whyC);
                Check(heldC && srcC != null && srcC.GhostBytes.Span.SequenceEqual(expectGhostC) && srcC.TapeBytes.Span.SequenceEqual(expectTapeC) && srcC.Meta.GhostFingerprint == WorldTapeFingerprint.Of(ghostC),
                    $"lap archive 2: a deferred capture is also frozen: the ghost bytes made on the worker and the tape are lap N's although the live ghost and recorder were reused ({whyC})");
                _ = expected;
            }

            // ---- (3) eviction across 8 laps with one retained (a limit of five: the shared rig's state carries on to the next test, so the number of laps driven here stays as it was)
            {
                string dir = Dir(); var archive = new LapArchive(dir, maxRecent: 5);
                var ids = new List<string>(); var t0 = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
                const int laps = 8;
                for (int k = 0; k < laps; k++)
                {
                    var g = rig.Drive(3f, 30, 10 + k);
                    ids.Add(archive.Submit(LapCap(g, rig.Rec, utc: t0.AddMinutes(k))).Id);
                    archive.Flush(TimeSpan.FromSeconds(30));
                    if (k == 1)
                    {
                        var r = archive.Retain(ids[1], "keeper").Wait(30000);
                        Check(r, "lap archive 3: the second lap is retained");
                    }
                }
                var rows = archive.List();
                var recent = rows.Where(r => !r.Retained).Select(r => r.Id).ToList();
                var keeper = rows.FirstOrDefault(r => r.Id == ids[1]);
                Check(recent.Count == 5 && recent.SequenceEqual(new[] { ids[7], ids[6], ids[5], ids[4], ids[3] }), $"lap archive 3: five recent laps remain, the newest first ({recent.Count})");
                Check(!rows.Any(r => r.Id == ids[0] || r.Id == ids[2]), "lap archive 3: the oldest non-retained laps were dropped");
                Check(keeper != null && keeper.Retained && keeper.Meta.Name == "keeper" && keeper.State == LapEntryState.Ready && archive.Open(ids[1]) != null, "lap archive 3: the retained lap survived eviction and still opens");
                string kd = Path.Combine(dir, ids[1]);
                Check(File.Exists(Path.Combine(kd, "ghost.bin")) && File.Exists(Path.Combine(kd, "tape.bin")) && File.Exists(Path.Combine(kd, "meta.json")) && File.Exists(Path.Combine(kd, "track.sig")) && File.Exists(Path.Combine(kd, "records.ver"))
                    && Directory.GetDirectories(dir).Length == 1, "lap archive 3: the retained lap's five files are on disk, nothing else was written");
                var msgs = archive.DrainMessages().ToList();
                Check(msgs.Count(m => m.Contains("dropped the oldest recent lap")) == 2 && msgs.Any(m => m.Contains(ids[0])) && msgs.Any(m => m.Contains(ids[2])), "lap archive 3: each drop was reported");
            }

            // ---- (4) byte ceiling and per-lap cap
            {
                var probe = new LapArchive(Dir()); var g0 = rig.Drive(4f, 30, 10);
                var capProbe = LapCap(g0, rig.Rec); long raw = LapArchive.CaptureBytes(capProbe);
                probe.Submit(capProbe); probe.Flush(TimeSpan.FromSeconds(30)); long one = probe.List()[0].SizeBytes;
                Check(one < raw, $"lap archive 4: a finished lap ({one} B) is smaller than its pending raw recording ({raw} B)");
                var ceiling = new LapArchive(Dir(), maxRecent: 5, maxRecentBytes: raw + one + one / 2);       // room for one pending newest + one finished lap, not two finished + one pending
                var ids = new List<string>(); bool within = true;
                for (int k = 0; k < 4; k++)
                {
                    var g = rig.Drive(4f, 30, 10 + k); ids.Add(ceiling.Submit(LapCap(g, rig.Rec, utc: DateTime.UtcNow.AddSeconds(k))).Id); ceiling.Flush(TimeSpan.FromSeconds(30));
                    within &= ceiling.List().Sum(r => r.SizeBytes) <= raw + one + one / 2;
                }
                var left = ceiling.List();
                Check(within && left.Count == 2 && left[0].Id == ids[3] && left[1].Id == ids[2], $"lap archive 4: the byte ceiling dropped laps before the count limit did ({left.Count} of 4 left, newest kept)");
                ceiling.DrainMessages();
                var g1 = rig.Drive(4f, 30, 10); var cap1 = LapCap(g1, rig.Rec);
                var small = new LapArchive(Dir(), maxLapBytes: LapArchive.CaptureBytes(cap1) - 1);
                var refused = small.Submit(cap1);
                var said = small.DrainMessages().ToList();
                Check(!refused.Accepted && refused.Reason.Contains("too large") && said.Any(m => m.Contains("lap not kept") && m.Contains("per lap")) && small.List().Count == 0, $"lap archive 4: a lap over the per-lap cap is refused with a visible message ({refused.Reason})");
                var exact = new LapArchive(Dir(), maxLapBytes: LapArchive.CaptureBytes(cap1));
                Check(exact.Submit(LapCap(g1, rig.Rec)).Accepted, "lap archive 4: a lap exactly at the cap is kept");
                Check(ReferenceEquals(LapArchive.For(Path.Combine(root, "same")), LapArchive.For(Path.Combine(root, "same", "..", "same"))) && !ReferenceEquals(LapArchive.For(Path.Combine(root, "same")), LapArchive.For(Path.Combine(root, "other"))), "lap archive 4: For() returns one shared instance per directory");
            }

            // ---- (5) retain round trip across archive instances
            string retainedId, retainedDir = Dir(), retainedHash;
            {
                var a = new LapArchive(retainedDir); var ghost = rig.Drive(6f, 50, 25);
                retainedId = a.Submit(LapCap(ghost, rig.Rec, sig: "track-signature-77")).Id;
                var r = a.Retain(retainedId, "  Fast lap\u0007 ").Result;
                retainedHash = a.Open(retainedId).Sha256();
                string d = Path.Combine(retainedDir, retainedId);
                Check(r.Ok && r.Dir == d && File.ReadAllText(Path.Combine(d, "track.sig")).Trim() == "track-signature-77" && File.ReadAllText(Path.Combine(d, "records.ver")).Trim() == RecordStore.Layout.ToString()
                    && File.ReadAllBytes(Path.Combine(d, "ghost.bin")).AsSpan().SequenceEqual(ghost.ToBytes()) && !Directory.GetFiles(d, "*.tmp").Any(), $"lap archive 5: retained files are written, no tmp left ({r.Message})");
                var b = new LapArchive(retainedDir); var rows = b.List();
                var src = b.Open(retainedId, out var why);
                Check(rows.Count == 1 && rows[0].Retained && !rows[0].InMemory && rows[0].State == LapEntryState.Ready && rows[0].Meta.Name == "Fast lap" && rows[0].SizeBytes > 0 && rows[0].Meta.Retained, "lap archive 5: a new archive instance finds the retained lap, name cleaned, ready");
                Check(src != null && src.Sha256() == retainedHash && src.Warning == null && LapGhostBytes.Fingerprint(src.GhostBytes.Span, out _, out _) == WorldTapeCodec.Decode(src.TapeBytes.ToArray()).GhostFingerprint, $"lap archive 5: Open gives identical bytes and the fingerprint pairing verifies ({why})");
                var rn = b.Retain(retainedId, "Renamed").Result; var c = new LapArchive(retainedDir);
                Check(rn.Ok && c.List()[0].Meta.Name == "Renamed" && c.Open(retainedId).Sha256() == retainedHash, "lap archive 5: retaining again only renames, the pair is untouched");
                GD.Print($"LAPARCHIVE: retain round trip ok {retainedId}");
            }

            // ---- (6) failure injection
            {
                string dir = retainedDir;       // holds the good retained lap X from (5)
                string xd = Path.Combine(dir, retainedId);
                string XHash() => string.Join("|", new[] { "ghost.bin", "tape.bin", "meta.json", "track.sig", "records.ver" }.Select(f => LapHex(File.ReadAllBytes(Path.Combine(xd, f)))));
                string before = XHash();
                var ops = new LapFaultOps(); var a = new LapArchive(dir, ops);
                var gy = rig.Drive(5f, 20, 15); var y = a.Submit(LapCap(gy, rig.Rec)).Id; a.Flush(TimeSpan.FromSeconds(30)); string ySha = a.Open(y).Sha256();
                // a) the write fails before anything is placed
                ops.FailStep = s => s == "verified";
                var r1 = a.Retain(y, "never").Result; var msgs = a.DrainMessages().ToList();
                var row = a.List().First(x => x.Id == y);
                string yd = Path.Combine(dir, y);
                Check(!r1.Ok && r1.Message.Contains("not saved") && msgs.Any(m => m.Contains("could not keep") && m.Contains(y)) && row.State == LapEntryState.Failed && row.Message.Contains("could not be kept") && !row.Retained,
                    $"lap archive 6a: a failed retain is reported visibly and marks the recent lap Failed ({r1.Message})");
                Check(a.Open(y)?.Sha256() == ySha && !File.Exists(Path.Combine(yd, "ghost.bin")) && !File.Exists(Path.Combine(yd, "meta.json")) && !Directory.GetFiles(yd, "*.tmp").Any() && XHash() == before && new LapArchive(dir).Open(retainedId)?.Sha256() == retainedHash,
                    "lap archive 6a: the recent lap is still usable, nothing was placed, no tmp is left, and the earlier retained pair is byte-identical");
                // b) the ghost lands, the tape cannot be placed: incomplete folder is listed, not hidden
                ops.FailStep = null; ops.FailPlace = f => f.EndsWith("tape.bin");
                var r2 = a.Retain(y, "half").Result;
                var fresh = new LapArchive(dir).List().First(x => x.Id == y);
                Check(!r2.Ok && File.Exists(Path.Combine(yd, "ghost.bin")) && !File.Exists(Path.Combine(yd, "tape.bin")) && fresh.State == LapEntryState.Failed && fresh.Message.Contains("meta.json") && a.Open(y)?.Sha256() == ySha && XHash() == before,
                    $"lap archive 6b: a half-written retain is refused, listed as Failed on disk, the recent lap stays usable ({r2.Message})");
                // c) a retry on a healthy disk completes the same folder
                ops.FailPlace = null;
                var r3 = a.Retain(y, "whole").Result; var done = new LapArchive(dir);
                Check(r3.Ok && done.List().First(x => x.Id == y).State == LapEntryState.Ready && done.Open(y)?.Sha256() == ySha && XHash() == before, "lap archive 6c: a retry after the failure succeeds and gives the same bytes");
                // d) the rename of a retained lap fails: the old meta stays
                var nameBefore = new LapArchive(dir).List().First(x => x.Id == retainedId).Meta.Name;
                ops.FailPlace = f => f.EndsWith("meta.json"); var rn = a.Retain(retainedId, "should not stick").Result; ops.FailPlace = null;
                Check(!rn.Ok && new LapArchive(dir).List().First(x => x.Id == retainedId).Meta.Name == nameBefore && new LapArchive(dir).Open(retainedId)?.Sha256() == retainedHash, "lap archive 6d: a failed rename keeps the previous metadata and the pair");
                // e) the tape file is corrupt
                var gz = rig.Drive(4f, 20, 15); var z = a.Submit(LapCap(gz, rig.Rec)).Id; a.Retain(z, "z").Wait(30000);
                string zt = Path.Combine(dir, z, "tape.bin"); var zGood = File.ReadAllBytes(zt); var junk = (byte[])zGood.Clone(); for (int i = 40; i < junk.Length; i += 7) junk[i] ^= 0x5A; File.WriteAllBytes(zt, junk);
                var b = new LapArchive(dir); var zs = b.Open(z, out var zwhy); var zm = b.DrainMessages().ToList();
                Check(zs != null && !zs.HasTape && zs.Warning != null && zs.Warning.Contains("tape.bin") && zs.GhostBytes.Span.SequenceEqual(gz.ToBytes()) && zm.Any(m => m.Contains(z) && m.Contains("ghost only")) && b.Open(retainedId)?.Sha256() == retainedHash,
                    $"lap archive 6e: a corrupt tape.bin opens the lap ghost only with a visible warning ({zs?.Warning}); the other retained pair is intact");
                // f) the tape belongs to another ghost
                var tp2 = WorldTapeCodec.Decode(zGood); tp2.GhostFingerprint ^= 1; File.WriteAllBytes(zt, WorldTapeCodec.Encode(tp2));
                var zs2 = new LapArchive(dir).Open(z);
                Check(zs2 != null && !zs2.HasTape && zs2.Warning.Contains("another ghost"), "lap archive 6f: a tape that belongs to another ghost is dropped with a visible warning, the ghost opens");
                File.WriteAllBytes(zt, zGood);
                Check(new LapArchive(dir).Open(z)?.HasTape == true, "lap archive 6f: and the untouched pair verifies again");
                // g) incompatible layout and a newer meta
                File.WriteAllText(Path.Combine(dir, z, "records.ver"), "1\n");
                var cc = new LapArchive(dir); var zi = cc.List().First(x => x.Id == z); var zopen = cc.Open(z, out var zr);
                Check(zi.State == LapEntryState.Incompatible && zi.Message.Contains("layout") && !zi.Openable && zopen == null && zr.Contains("layout") && cc.DrainMessages().Any(m => m.Contains("INCOMPATIBLE")), "lap archive 6g: an old record layout is listed as Incompatible, visible, never opened");
                File.WriteAllText(Path.Combine(dir, z, "records.ver"), RecordStore.Layout + "\n");
                File.WriteAllText(Path.Combine(dir, z, "meta.json"), new LapMeta { Id = z, Version = 99 }.ToJson());
                var zn = new LapArchive(dir).List().First(x => x.Id == z);
                Check(zn.State == LapEntryState.Incompatible && zn.Message.Contains("newer"), "lap archive 6g: a newer meta version is Incompatible");
                File.WriteAllText(Path.Combine(dir, z, "meta.json"), "{ broken");
                Check(new LapArchive(dir).List().First(x => x.Id == z).State == LapEntryState.Failed, "lap archive 6g: an unreadable meta.json is Failed, not hidden");
                Check(new LapArchive(dir).Open(retainedId)?.Sha256() == retainedHash && XHash() == before, "lap archive 6: through all of this the first retained pair never changed");
            }

            // ---- (7) a pending compression does not make Open fail
            {
                var gate = new ManualResetEventSlim(false); var reached = new ManualResetEventSlim(false); int hold = 0;
                var gA = rig.Drive(5f, 20, 20);
                var expA = WorldTapeRecorder.Compress(rig.Rec.Snapshot(), 5f, WorldTapeFingerprint.Of(gA)); expA.TrackId = "siberia"; expA.Set = 0; byte[] expectA = WorldTapeCodec.Encode(expA);
                var recA = rig.Rec.Snapshot();
                var gB = rig.Drive(5f, 25, 20);
                var expB = WorldTapeRecorder.Compress(rig.Rec.Snapshot(), 5f, WorldTapeFingerprint.Of(gB)); expB.TrackId = "siberia"; expB.Set = 0; byte[] expectB = WorldTapeCodec.Encode(expB);
                var held = new LapArchive(Dir());
                held.WorkerHook = s => { if (s.StartsWith("compress:") && Interlocked.Exchange(ref hold, 1) == 0) { reached.Set(); gate.Wait(60000); } };      // holds the first compression only
                var capA = new LapCapture { GhostBytes = gA.ToBytes(), GhostFingerprint = WorldTapeFingerprint.Of(gA), Recording = recA, TrackId = "siberia", LapSeconds = 5f, Ranked = true, RecordedUtc = DateTime.UtcNow.AddSeconds(-5) };
                var idHeld = held.Submit(capA).Id;
                var idNext = held.Submit(LapCap(gB, rig.Rec)).Id;       // the recorder still holds lap B
                Check(reached.Wait(20000), "lap archive 7: the worker is held inside the first lap's compression");
                var row = held.List().First(x => x.Id == idHeld);
                Check(row.State == LapEntryState.Pending && row.Openable && row.Meta.HasTape, "lap archive 7: while pending the lap is listed, Pending, openable");
                // a second lap queued behind the held worker: Open compresses just that entry itself
                var swB = Stopwatch.StartNew(); var sB = held.Open(idNext, out var whyB); swB.Stop();
                Check(sB != null && sB.HasTape && sB.TapeBytes.Span.SequenceEqual(expectB) && held.List().First(x => x.Id == idHeld).State == LapEntryState.Pending && !gate.IsSet,
                    $"lap archive 7: Open of a later lap does not wait for the held one ({swB.ElapsedMilliseconds} ms, {whyB})");
                // Open of the held lap itself waits for just its compression
                var task = Task.Run(() => held.Open(idHeld));
                bool doneEarly = task.Wait(300);
                gate.Set();
                var sA = task.Result;
                Check(!doneEarly && sA != null && sA.HasTape && sA.TapeBytes.Span.SequenceEqual(expectA) && sA.Warning == null, "lap archive 7: Open of the held lap waited for its compression and then succeeded with the full pair");
                held.Flush(TimeSpan.FromSeconds(30));
                Check(held.List().All(x => x.State == LapEntryState.Ready), "lap archive 7: both laps end Ready");
                // a compression that throws on the worker leaves a usable ghost-only lap
                var broken = new LapArchive(Dir()); broken.WorkerHook = s => throw new InvalidOperationException("injected compress failure");
                var idBroken = broken.Submit(LapCap(gA, rig.Rec)).Id; broken.Flush(TimeSpan.FromSeconds(30)); var sBroken = broken.Open(idBroken, out _); var rb = broken.List()[0];
                Check(sBroken != null && !sBroken.HasTape && sBroken.Warning != null && rb.State == LapEntryState.Failed && rb.Openable && broken.DrainMessages().Any(m => m.Contains("world tape not made")), "lap archive 7: a failed compression is visible and leaves the lap openable, ghost only");
            }

            // ---- (9) ghost-only entry
            {
                string dir = Dir(); var archive = new LapArchive(dir); var g = rig.Drive(4f, 20, 20);
                var id = archive.Submit(LapCap(g, null, kind: LapKind.Void)).Id;
                var row = archive.List()[0]; var src = archive.Open(id, out var why);
                Check(row.Id == id && !row.Meta.HasTape && row.State == LapEntryState.Ready && row.Meta.Kind == LapKind.Void && !row.Meta.Ranked && src != null && !src.HasTape && src.Warning == null && src.GhostBytes.Span.SequenceEqual(g.ToBytes()),
                    $"lap archive 9: a ghost-only void lap is listed (no tape), Ready, and opens ({why})");
                var rr = archive.Retain(id, "no tape").Result; var other = new LapArchive(dir); var os = other.Open(id);
                Check(rr.Ok && !File.Exists(Path.Combine(dir, id, "tape.bin")) && File.Exists(Path.Combine(dir, id, "ghost.bin")) && os != null && !os.HasTape && os.Sha256() == src.Sha256() && !other.List()[0].Meta.HasTape, "lap archive 9: a ghost-only lap retains without a tape file and opens again");
            }

            // ---- (10) memory bounds under a held worker
            LapMemoryBounds(rig, Dir);

            // ---- (8) the finish-line cost on a real-size lap
            RealSizeMeasure();
        }
        finally
        {
            rig.Dispose();
            try { Directory.Delete(root, true); } catch { }
        }
    }

    // ------------------------------------------------------------------ memory bounds (review fixes): eviction clears payloads at once, in-flight accounting, retain admission, process-wide cap

    /// <summary>Submits one lap and returns weak references to the heavy objects the capture handed over (recording, ghost copy), so the caller keeps no strong reference to them.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    static (string Id, WeakReference Rec, WeakReference Src, long Raw, bool Accepted) LapSubmitWeak(LapArchive a, LapRig rig, float secs, int hit, bool deferred, DateTime utc)
    {
        var g = rig.Drive(secs, hit, 20);
        var cap = deferred ? LapCapture.FromGhostCopy(g, rig.Rec, "siberia", 0, "petrol", LapKind.Normal, true, -1, "p", "s", utc) : LapCap(g, rig.Rec, utc: utc);
        long raw = LapArchive.CaptureBytes(cap); var wr = new WeakReference(cap.Recording); var ws = new WeakReference(cap.OwnedGhost);
        var res = a.Submit(cap);
        return (res.Id, wr, ws, raw, res.Accepted);
    }

    static void LapGc() { for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); } }

    static bool LapEmpty(LapArchive.Entry e) => e.Ghost == null && e.Tape == null && e.Rec == null && e.GhostSrc == null;

    void LapMemoryBounds(LapRig rig, Func<string> Dir)
    {
        var t0 = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
        Action<LapArchive, ManualResetEventSlim, ManualResetEventSlim> holdFirst = (a, reached, gate) =>
        {
            int hold = 0;
            a.WorkerHook = s => { if (s.StartsWith("compress:") && Interlocked.Exchange(ref hold, 1) == 0) { reached.Set(); gate.Wait(60000); } };
        };

        // ---- (10a) eviction under a held worker, eager and deferred captures
        foreach (bool deferred in new[] { false, true })
        {
            string tag = deferred ? "deferred" : "eager";
            var a = new LapArchive(Dir(), maxRecent: 2) { EvictedTrace = new() };
            var gate = new ManualResetEventSlim(false); var reached = new ManualResetEventSlim(false); holdFirst(a, reached, gate);
            var l1 = LapSubmitWeak(a, rig, 4f, 30, deferred, t0);
            bool held = reached.Wait(20000);
            var s1 = a.Accounting();
            Check(held && s1.InFlightBytes > 0 && s1.OwnedBytes + s1.InFlightBytes == l1.Raw && s1.RecentCount == 1,
                $"lap archive 10a ({tag}): the payload being compressed is counted once as in-flight ({s1.InFlightBytes} B) and is no longer owned by the entry ({s1.OwnedBytes} B), together the lap's {l1.Raw} B");
            var l2 = LapSubmitWeak(a, rig, 4f, 31, deferred, t0.AddSeconds(1));      // queued behind the held worker, never started
            var l3 = LapSubmitWeak(a, rig, 4f, 32, deferred, t0.AddSeconds(2));       // evicts l1 (compressing)
            var l4 = LapSubmitWeak(a, rig, 4f, 33, deferred, t0.AddSeconds(3));       // evicts l2 (queued)
            var trace = a.EvictedTrace.ToList(); var s2 = a.Accounting();
            Check(trace.Count == 2 && trace[0].Id == l1.Id && trace[1].Id == l2.Id && s2.RecentCount == 2, $"lap archive 10a ({tag}): the two oldest were evicted, the worker still held");
            Check(trace.Count == 2 && LapEmpty(trace[1]) && trace[1].Evicted && LapEmpty(trace[0]), $"lap archive 10a ({tag}): evicted entries own no Ghost, Tape, Rec or GhostSrc (the queued one and the compressing one)");
            Check(s2.OwnedBytes == l3.Raw + l4.Raw && s2.InFlightBytes == s1.InFlightBytes, $"lap archive 10a ({tag}): the evicted queued lap owns zero bytes: owned {s2.OwnedBytes} B = the two live laps' {l3.Raw + l4.Raw} B; in-flight stays {s2.InFlightBytes} B for the compressing one");
            LapGc();
            Check(!l2.Rec.IsAlive && !l2.Src.IsAlive, $"lap archive 10a ({tag}): the evicted queued lap's recording and ghost copy are collectable although the worker is held (rec alive={l2.Rec.IsAlive}, copy alive={l2.Src.IsAlive})");
            GD.Print($"LAPARCHIVE: held worker, compressing-and-evicted lap ({tag}): recording alive={l1.Rec.IsAlive} copy alive={l1.Src.IsAlive} (the in-flight allowance, {s2.InFlightBytes} B)");
            gate.Set(); bool flushed = a.Flush(TimeSpan.FromSeconds(60));
            var s3 = a.Accounting(); var rows = a.List();
            Check(flushed && LapEmpty(trace[0]) && a.Open(l1.Id) == null, $"lap archive 10a ({tag}): the lap evicted during compression was not repopulated when the worker was released (ghost, tape, recording, copy all null)");
            LapGc();
            Check(!l1.Rec.IsAlive && !l1.Src.IsAlive && s3.InFlightBytes == 0 && s3.PendingRetainBytes == 0, $"lap archive 10a ({tag}): after release the in-flight payload is gone (accounting {s3.InFlightBytes} B, collectable)");
            Check(rows.Count == 2 && rows.All(r => r.State == LapEntryState.Ready) && s3.OwnedBytes == rows.Sum(r => r.SizeBytes) && a.Open(rows[0].Id)?.HasTape == true && a.Open(rows[1].Id)?.HasTape == true,
                $"lap archive 10a ({tag}): steady state: two Ready laps with tapes, owned {s3.OwnedBytes} B = their sizes {rows.Sum(r => r.SizeBytes)} B");
        }

        // ---- (10b) retain admission under a held worker
        {
            var probe = new LapArchive(Dir()); var capP = LapCap(rig.Drive(4f, 30, 20), rig.Rec); long probeRaw = LapArchive.CaptureBytes(capP);
            long ceiling = 5 * probeRaw;
            var a = new LapArchive(Dir(), maxRecentBytes: ceiling);
            var gate = new ManualResetEventSlim(false); var reached = new ManualResetEventSlim(false); holdFirst(a, reached, gate);
            var laps = new List<(string Id, WeakReference Rec, WeakReference Src, long Raw, bool Accepted)>();
            for (int k = 0; k < 4; k++) laps.Add(LapSubmitWeak(a, rig, 4f, 30 + k, false, t0.AddSeconds(k)));
            bool held = reached.Wait(20000);
            var r1 = a.Retain(laps[0].Id, "one"); var r2 = a.Retain(laps[1].Id, "two");
            var refused = new List<Task<RetainResult>>(); for (int i = 0; i < 40; i++) refused.Add(a.Retain(laps[2 + i % 2].Id, "more"));
            var again = a.Retain(laps[0].Id, "again");
            var sa = a.Accounting();
            Check(held && !r1.IsCompleted && !r2.IsCompleted && refused.All(t => t.IsCompleted && !t.Result.Ok && t.Result.Message.Contains("waiting to be saved")) && again.IsCompleted && again.Result.Message.Contains("already"),
                "lap archive 10b: with the worker held, the 3rd and every later Retain is refused with a visible reason; the same lap twice says it is already being saved");
            Check(sa.PendingRetainCount == LapArchiveLimits.MaxPendingRetains && sa.PendingRetainBytes == laps[0].Raw + laps[1].Raw && sa.PendingRetainBytes <= LapArchiveLimits.MaxPendingRetainBytes,
                $"lap archive 10b: pending-retain accounting {sa.PendingRetainCount} laps / {sa.PendingRetainBytes} B (limit {LapArchiveLimits.MaxPendingRetains} / {LapArchiveLimits.MaxPendingRetainBytes} B)");
            // pinned laps cannot be evicted: keep submitting while held and watch the total
            bool within = true; long worst = 0;
            for (int k = 4; k < 14; k++)
            {
                LapSubmitWeak(a, rig, 4f, 30 + k, false, t0.AddSeconds(k)); var st = a.Accounting();
                worst = Math.Max(worst, st.OwnedBytes + st.InFlightBytes); within &= st.OwnedBytes + st.InFlightBytes <= ceiling;
            }
            var ids = a.List().Select(r => r.Id).ToHashSet();
            Check(within && ids.Contains(laps[0].Id) && ids.Contains(laps[1].Id) && a.Accounting().RecentCount <= 5, $"lap archive 10b: ten more laps under the held worker: owned + in-flight never above the ceiling (worst {worst} B of {ceiling} B), the two pinned laps stay");
            gate.Set(); bool flushed = a.Flush(TimeSpan.FromSeconds(60));
            bool done = Task.WaitAll(new Task[] { r1, r2 }, 30000);
            var sb = a.Accounting();
            Check(flushed && done && r1.Result.Ok && r2.Result.Ok && sb.PendingRetainBytes == 0 && sb.PendingRetainCount == 0 && sb.InFlightBytes == 0 && LapEmpty(a.Probe(laps[0].Id)) && LapEmpty(a.Probe(laps[1].Id)),
                $"lap archive 10b: after release both retains finish, pending-retain drains to 0, retained laps own no memory (owned {sb.OwnedBytes} B)");
            var newest = a.List().First(r => !r.Retained).Id; var late = a.Retain(newest, "late").Result;
            Check(late.Ok && a.Accounting().PendingRetainBytes == 0, $"lap archive 10b: a Retain is accepted again once the queue drained ({late.Message})");

            // byte limit: one lap fits, a second does not
            var b = new LapArchive(Dir(), maxPendingRetains: 5, maxPendingRetainBytes: probeRaw + probeRaw / 2);
            var gate2 = new ManualResetEventSlim(false); var reached2 = new ManualResetEventSlim(false); holdFirst(b, reached2, gate2);
            var bl = new List<(string Id, WeakReference Rec, WeakReference Src, long Raw, bool Accepted)>(); for (int k = 0; k < 3; k++) bl.Add(LapSubmitWeak(b, rig, 4f, 30, false, t0.AddSeconds(k)));
            reached2.Wait(20000);
            var q1 = b.Retain(bl[0].Id, "a"); var q2 = b.Retain(bl[1].Id, "b"); var sbb = b.Accounting();
            Check(!q1.IsCompleted && q2.IsCompleted && !q2.Result.Ok && q2.Result.Message.Contains("MB") && sbb.PendingRetainCount == 1 && sbb.PendingRetainBytes <= probeRaw + probeRaw / 2, "lap archive 10b: the pending-retain byte limit refuses a second lap that would pin more than the limit");
            gate2.Set(); b.Flush(TimeSpan.FromSeconds(60)); Check(q1.Wait(30000) && q1.Result.Ok && b.Accounting().PendingRetainBytes == 0, "lap archive 10b: the admitted retain finishes and the accounting drains");
        }

        // ---- (10c) process-wide cap across two archives
        {
            var probe = new LapArchive(Dir()); var g0 = rig.Drive(4f, 30, 20); var cp = LapCap(g0, rig.Rec); long raw = LapArchive.CaptureBytes(cp);
            probe.Submit(cp); probe.Flush(TimeSpan.FromSeconds(30)); long one = probe.List()[0].SizeBytes;
            long cap = raw + 2 * one + one / 2;
            var group = new LapArchiveGroup(cap);
            var a1 = new LapArchive(Dir(), group: group); var a2 = new LapArchive(Dir(), group: group);
            var order = new List<string>(); bool within = true; long worst = 0;
            for (int k = 0; k < 8; k++)
            {
                var a = k % 3 == 2 ? a2 : (k % 2 == 0 ? a1 : a2);       // a profile switch back and forth: both archives keep growing
                order.Add(LapSubmitWeak(a, rig, 4f, 30, false, t0.AddSeconds(k)).Id);
                a1.Flush(TimeSpan.FromSeconds(30)); a2.Flush(TimeSpan.FromSeconds(30));
                var st = group.Accounting(); worst = Math.Max(worst, st.OwnedBytes + st.InFlightBytes); within &= st.OwnedBytes + st.InFlightBytes <= cap;
            }
            var left = a1.List().Concat(a2.List()).Select(r => r.Id).ToHashSet(); var gs = group.Accounting();
            var msgs = a1.DrainMessages().Concat(a2.DrainMessages()).ToList();
            Check(within && gs.RecentCount == 3 && left.SetEquals(order.Skip(5)) && gs.OwnedBytes == a1.Accounting().OwnedBytes + a2.Accounting().OwnedBytes,
                $"lap archive 10c: two archives share the cap ({cap} B): owned never above it (worst {worst} B), the three newest laps of either archive remain ({gs.RecentCount})");
            Check(a1.Accounting().RecentCount < 5 && a2.Accounting().RecentCount < 5 && msgs.Any(m => m.Contains("process-wide memory limit")), "lap archive 10c: the drops came from the process-wide cap (neither archive was at its own limit) and were reported");
            // the shared (For) archives use the static process group
            string da = Path.Combine(Dir(), "pa"), db = Path.Combine(Dir(), "pb");
            var sa = LapArchive.For(da); var sb = LapArchive.For(db); LapArchive.For(da);
            var before = LapArchive.Process.Accounting();
            LapSubmitWeak(sa, rig, 3f, 20, false, t0); LapSubmitWeak(sb, rig, 3f, 20, false, t0.AddSeconds(1)); sa.Flush(TimeSpan.FromSeconds(30)); sb.Flush(TimeSpan.FromSeconds(30));
            var after = LapArchive.Process.Accounting();
            Check(LapArchive.Process.MaxBytes == LapArchiveLimits.MaxProcessBytes && LapArchiveLimits.MaxProcessBytes >= LapArchiveLimits.MaxRecentBytes && after.RecentCount - before.RecentCount == 2 && after.OwnedBytes - before.OwnedBytes == sa.Accounting().OwnedBytes + sb.Accounting().OwnedBytes,
                $"lap archive 10c: For() archives of two profile folders both count in the process group ({LapArchiveLimits.MaxProcessBytes >> 20} MiB cap)");
        }
    }

        void RealSizeMeasure()
    {
        string root = Path.Combine(Path.GetTempPath(), "cccp-lapark-real-" + Guid.NewGuid().ToString("N"));
        var rig = new LapRig(this, 28, 5);
        try
        {
            var sw = Stopwatch.StartNew();
            rig.Drive(120f, 600, 40);        // the production laps are 66-150 s; 120 s is the Siberia/Atomgrad class
            var ghost = rig.Live.CopyWithTime(120f);
            long buildMs = sw.ElapsedMilliseconds;
            var archive = new LapArchive(root);
            var takes = new List<double>(); var submits = new List<double>(); long raw = 0, ghostBytes = 0, tapeBytes = 0, rawRec = 0;
            for (int k = 0; k < 3; k++)
            {
                var t0 = Stopwatch.GetTimestamp(); var cap = LapCap(ghost, rig.Rec, utc: DateTime.UtcNow.AddSeconds(k)); var t1 = Stopwatch.GetTimestamp();
                var sub = archive.Submit(cap); var t2 = Stopwatch.GetTimestamp();
                takes.Add(Stopwatch.GetElapsedTime(t0, t1).TotalMilliseconds); submits.Add(Stopwatch.GetElapsedTime(t1, t2).TotalMilliseconds);
                raw = LapArchive.CaptureBytes(cap); ghostBytes = cap.GhostBytes.Length; rawRec = raw - ghostBytes;
                Check(sub.Accepted, $"lap archive 8: real-size lap {k} accepted");
                archive.Flush(TimeSpan.FromSeconds(60));
            }
            var done = archive.List()[0]; tapeBytes = done.SizeBytes - ghostBytes;
            var tb = Stopwatch.GetTimestamp(); var bytes = ghost.ToBytes(); var tc = Stopwatch.GetTimestamp(); WorldTapeFingerprint.Of(ghost); var td = Stopwatch.GetTimestamp(); rig.Rec.Snapshot(); var te = Stopwatch.GetTimestamp();
            GD.Print($"LAPARCHIVE: capture breakdown: ghost.ToBytes {Stopwatch.GetElapsedTime(tb, tc).TotalMilliseconds:F2} ms ({bytes.Length} B), WorldTapeFingerprint.Of {Stopwatch.GetElapsedTime(tc, td).TotalMilliseconds:F2} ms, recorder.Snapshot {Stopwatch.GetElapsedTime(td, te).TotalMilliseconds:F2} ms");
            var comp = Stopwatch.StartNew(); var tape = WorldTapeRecorder.Compress(rig.Rec.Snapshot(), 120f, WorldTapeFingerprint.Of(ghost)); long compressMs = comp.ElapsedMilliseconds;
            double take = takes.Min(), submit = submits.Max();
            GD.Print($"LAPARCHIVE: real-size fixture 120 s, {ghost.Count} ghost samples, {rig.Rec.Samples} tape samples (built in {buildMs} ms)");
            GD.Print($"LAPARCHIVE: sizes ghost.bin={ghostBytes} B (V4 damage={ghost.HasDamage}) raw recording={rawRec} B tape.bin={tapeBytes} B pending-in-memory={raw} B finished-in-memory={done.SizeBytes} B; compress on the worker took {compressMs} ms");
            GD.Print($"LAPARCHIVE: main-thread cost at the finish line: capture (ToBytes + fingerprint + Snapshot) {string.Join(" / ", takes.Select(x => x.ToString("F2")))} ms; Submit {string.Join(" / ", submits.Select(x => x.ToString("F2")))} ms");
            GD.Print($"LAPARCHIVE: limits max-recent={LapArchiveLimits.MaxRecent} ceiling={LapArchiveLimits.MaxRecentBytes} B per-lap cap={LapArchiveLimits.MaxLapBytes} B; five finished fixture laps = {5 * done.SizeBytes} B, one pending = {raw} B");
            // the cheap capture: the archive makes the ghost bytes on its worker from its own copy of the ghost
            var deferredMs = new List<double>(); string deferredSha = null;
            for (int k = 0; k < 3; k++)
            {
                var t0 = Stopwatch.GetTimestamp(); var own = rig.Live.CopyWithTime(120f); var cap = LapCapture.FromGhostCopy(own, rig.Rec, "siberia", 0, "petrol", LapKind.Normal, true, 12.5f, "p", "s", DateTime.UtcNow.AddSeconds(10 + k));
                var sub = archive.Submit(cap); var t1 = Stopwatch.GetTimestamp(); deferredMs.Add(Stopwatch.GetElapsedTime(t0, t1).TotalMilliseconds);
                var held = Stopwatch.StartNew(); var src = archive.Open(sub.Id, out var why); held.Stop();
                if (k == 0) deferredSha = src?.Sha256();
                Check(sub.Accepted && src != null && src.HasTape && src.GhostBytes.Span.SequenceEqual(ghost.ToBytes()) && src.TapeBytes.Span.SequenceEqual(WorldTapeCodec.Encode(WithTrack(WorldTapeRecorder.Compress(rig.Rec.Snapshot(), 120f, WorldTapeFingerprint.Of(ghost)), "siberia", 0))),
                    $"lap archive 8: deferred capture {k} gives the same ghost bytes and tape as the eager one (first Open waited {held.ElapsedMilliseconds} ms for the worker: {why})");
                archive.Flush(TimeSpan.FromSeconds(60));
            }
            GD.Print($"LAPARCHIVE: deferred capture (CopyWithTime + Snapshot + Submit) on the calling thread: {string.Join(" / ", deferredMs.Select(x => x.ToString("F2")))} ms");
            Check(deferredMs.Max() < 25 && deferredMs.Max() * 4 < takes.Min(), $"lap archive 8: the deferred capture costs {deferredMs.Max():F2} ms at most on the calling thread against {takes.Min():F2} ms for the eager one");
            Check(submit < compressMs && submit < 25, $"lap archive 8: Submit takes {submit:F2} ms on the calling thread (the compression of {compressMs} ms is on the worker)");
            Check(raw * 2 <= LapArchiveLimits.MaxLapBytes && LapArchiveLimits.MaxRecent * done.SizeBytes + raw <= LapArchiveLimits.MaxRecentBytes, "lap archive 8: the real-size lap is under half the per-lap cap, and five finished laps plus one pending fit the ceiling");
            Check(tape.SampleCount > 0, "lap archive 8: fixture tape is real");
        }
        finally { rig.Dispose(); try { Directory.Delete(root, true); } catch { } }
    }
}
