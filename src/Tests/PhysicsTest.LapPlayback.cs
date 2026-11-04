using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Replay;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

// Phase 2 of the player replay: the viewer's private playback inputs (Ghost.FromBytes, LapPlayback.Prepare). Fixtures come from the lap archive tests' helpers (LapRig, LapCap).
// Marker lines start with "LAPPLAYBACK:".
public partial class PhysicsTest
{
    static LapSource LapSrc(LapSource s, byte[] ghost = null, byte[] tape = null, bool dropTape = false, LapMeta meta = null)
        => new(meta ?? s.Meta, ghost ?? s.GhostBytes.ToArray(), dropTape ? null : tape ?? s.TapeBytes.ToArray());

    static Vector3[] LapPositions(TapeWorld w) => w.Parts.SelectMany(l => l).Select(n => n.Position).ToArray();

    void LapPlaybackTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "cccp-lapplay-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var rig = new LapRig(this, 6, 3);
        var prm = new CarParams(); IGround ground = new FlatGround();
        try
        {
            var archive = new LapArchive(Path.Combine(root, "arch"));
            var ghost = rig.Drive(8f, 100, 30);
            // what the recorded world looked like at 4 s (the world is a pure function of the clock; lap 0 started at offset 0)
            rig.World.Tick(4f);
            var expect4 = LapPositions(rig.World);
            var id = archive.Submit(LapCap(ghost, rig.Rec, kind: LapKind.Record)).Id;
            var src = archive.Open(id, out var why);
            Check(src != null && src.HasTape && ghost.HasDamage, $"lap playback: fixture lap opened with ghost (V4, damage) and tape ({why})");
            string hash0 = src.Sha256();

            // ---- (1) FromBytes equals Load of the same file
            {
                string gp = Path.Combine(root, "g.bin"); File.WriteAllBytes(gp, src.GhostBytes.ToArray());
                var a = Ghost.FromBytes(src.GhostBytes.ToArray(), prm, ground); var b = Ghost.Load(gp, prm, ground);
                Check(a != null && b != null && a.ToBytes().AsSpan().SequenceEqual(b.ToBytes()) && a.ToBytes().AsSpan().SequenceEqual(src.GhostBytes.Span), "lap playback 1: FromBytes and Load give the same ghost, byte for byte through ToBytes, equal to the source bytes");
                Check(a.Count == b.Count && a.LapTime == b.LapTime && a.HasDamage && b.HasDamage && a.HasDayClock == b.HasDayClock && WorldTapeFingerprint.Of(a) == WorldTapeFingerprint.Of(b) && WorldTapeFingerprint.Of(a) == src.Meta.GhostFingerprint,
                    "lap playback 1: count, lap time, damage, day clock and fingerprint are equal (and are the meta's)");
                var s1 = a.Sample(3.3f); var s2 = b.Sample(3.3f);
                Check(s1.Origin == s2.Origin && s1.Basis == s2.Basis, "lap playback 1: sampled pose equal");
                // a V3 ghost (no damage) and a hand-made pose-only V1 file
                var live = new Ghost(); var car0 = new CarSim(prm, ground) { WorldMin = -1e6f, WorldMax = 1e6f }; car0.Reset(new Vector3(0, 0.8f, 0), 0);
                for (int i = 0; i < 90; i++) { car0.Pos = new Vector3(i * .3f, .8f, 0); live.Record(i / 30f + .00001f, car0, 0, 0, 0, 100); }
                string g3 = Path.Combine(root, "g3.bin"); File.WriteAllBytes(g3, live.ToBytes());
                var v3a = Ghost.FromBytes(live.ToBytes(), prm, ground); var v3b = Ghost.Load(g3, prm, ground);
                Check(v3a != null && !v3a.HasDamage && v3a.ToBytes().AsSpan().SequenceEqual(v3b.ToBytes()) && v3a.ToBytes().AsSpan().SequenceEqual(live.ToBytes()), "lap playback 1: a V3 ghost (no damage) also round-trips and equals Load");
                var ms = new MemoryStream();
                using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) { w.Write(0x43434350u); w.Write(2f); w.Write(60u); for (int i = 0; i < 60; i++) { w.Write(i * .5f); w.Write(.8f); w.Write(0f); w.Write(0f); w.Write(0f); w.Write(0f); w.Write(1f); } }
                string g1 = Path.Combine(root, "g1.bin"); File.WriteAllBytes(g1, ms.ToArray());
                var v1a = Ghost.FromBytes(ms.ToArray(), prm, ground); var v1b = Ghost.Load(g1, prm, ground);
                Check(v1a != null && v1b != null && v1a.Count == 60 && v1a.ToBytes().AsSpan().SequenceEqual(v1b.ToBytes()) && Ghost.FromBytes(ms.ToArray(), prm, null) == null, "lap playback 1: a pose-only V1 file is reconstructed identically to Load (and refused without a ground, not thrown)");
                // independence: clearing one parse does not change another, nor the source
                var c = Ghost.FromBytes(src.GhostBytes.ToArray(), prm, ground); var d = Ghost.FromBytes(src.GhostBytes.ToArray(), prm, ground);
                c.Clear();
                Check(c.Count == 0 && !c.HasDamage && d.HasDamage && d.ToBytes().AsSpan().SequenceEqual(src.GhostBytes.Span) && src.Sha256() == hash0, "lap playback 1: a parsed ghost shares no state with another parse or with the source (clear one, the other and the bytes stand)");
                var arr = src.GhostBytes.ToArray(); var e = Ghost.FromBytes(arr, prm, ground); Array.Clear(arr);
                Check(e.ToBytes().AsSpan().SequenceEqual(src.GhostBytes.Span), "lap playback 1: the parsed ghost does not alias the input array");
                // refusals: null with a reason, no exception
                var g = src.GhostBytes.ToArray(); bool threw = false; int nulls = 0, tries = 0;
                try
                {
                    foreach (var cut in new[] { 0, 3, 11, 12, 100, g.Length / 2, g.Length - 5, g.Length - 1 }) { tries++; if (Ghost.FromBytes(g.AsSpan(0, cut).ToArray(), prm, ground, out var er) == null && er != null) nulls++; }
                    var junk = (byte[])g.Clone(); junk[0] ^= 0xFF; tries++; if (Ghost.FromBytes(junk, prm, ground) == null) nulls++;
                    var big = (byte[])g.Clone(); big[8] = 0xFF; big[9] = 0xFF; big[10] = 0xFF; tries++; if (Ghost.FromBytes(big, prm, ground) == null) nulls++;
                    tries++; if (Ghost.FromBytes(null, prm, ground) == null) nulls++;
                }
                catch (Exception ex) { threw = true; GD.PrintErr(ex); }
                Check(!threw && nulls == tries, $"lap playback 1: truncated, wrong-magic, oversized and null ghost bytes give null with a reason, never an exception ({nulls}/{tries})");
                GD.Print($"LAPPLAYBACK: FromBytes == Load ok ({src.GhostBytes.Length} B V4, {live.ToBytes().Length} B V3, {ms.Length} B V1)");
            }

            // ---- (2) Prepare: a good lap with a tape, seek information, a private player in a viewer world
            {
                var r = LapPlayback.Prepare(src, prm, ground);
                Check(r.Ok && r.HasTape && r.Error == LapPlaybackError.None && r.TapeError == LapPlaybackError.None && r.Ghost.HasDamage && r.Tape.Samples > 100, $"lap playback 2: Prepare gives ghost and tape for the lap ({r.Message})");
                Check(r.CanSeek && Math.Abs(r.LapSeconds - 8f) < .05f && r.KeyframeSeconds == WorldTapeConst.KeyframeSeconds && r.Tape.Keyframes == 2 && r.Tape.KeyframeBefore(7f) == WorldTapeConst.KeyframeSeconds && r.Tape.KeyframeBefore(2f) == 0f && Math.Abs(r.Tape.Seconds - 8f) < .2f,
                    $"lap playback 2: lap length {r.LapSeconds:F2} s, tape {r.Tape.Seconds:F2} s with {r.Tape.Keyframes} keyframes every {r.KeyframeSeconds:F1} s, seek restores the keyframe before the clock");
                Check(LapPlayback.Prepare(src, prm, ground, "sig-1").Ok && LapPlayback.Prepare(src, prm, ground, "other").Error == LapPlaybackError.TrackMismatch, "lap playback 2: the expected track signature is checked when given");
                using var view = TapeWorld.Build(6, 3, true); var sink = new TapeSink { World = view };
                var pl = r.Tape.CreatePlayer(view.Index, sink);
                Check(pl.Covers(TapeWorld.Key(0)) && pl.Covers(TapeWorld.Key(5)), "lap playback 2: a player made from the prepared tape covers the viewer world's actors");
                pl.Apply(4f, 0); var at4 = LapPositions(view);
                float worst = 0; for (int i = 0; i < at4.Length; i++) worst = Math.Max(worst, (at4[i] - expect4[i]).Length());
                Check(worst < .05f && at4.Zip(expect4).Any(z => z.First != new Vector3()), $"lap playback 2: the viewer world is posed as the recorded world was at 4 s (worst part error {worst:F4} m)");
                pl.Apply(7.5f, 0.016f); pl.Apply(1f, 0.016f);      // a forward jump, then a backward one
                pl.Apply(4f, 0); var back = LapPositions(view);
                Check(back.Zip(at4).All(z => z.First == z.Second), "lap playback 2: a forward jump, a backward jump and back to 4 s pose exactly as the first pass (seek both ways)");
                pl.Apply(7.9f, 0); pl.Apply(0f, 0); var wrap = LapPositions(view);
                pl.Seek(0f); var zero = LapPositions(view);
                Check(wrap.Zip(zero).All(z => z.First == z.Second), "lap playback 2: loop wrap (clock 7.9 to 0) poses the lap start");
                var car = new CarSim(prm, ground) { WorldMin = -1e6f, WorldMax = 1e6f };
                r.Ghost.Rewind(); r.Ghost.Apply(3f, car, .016f); var p3 = car.Pos; r.Ghost.Rewind(); r.Ghost.Apply(6f, car, .016f); r.Ghost.Rewind(); r.Ghost.Apply(3f, car, .016f);
                Check(car.Pos == p3 && p3 != new Vector3(), "lap playback 2: the ghost seeks both ways to the same pose");
                pl.Release();
            }

            // ---- (3) two simultaneous Prepare calls on one source are independent
            {
                var ra = LapPlayback.Prepare(src, prm, ground); var rb = LapPlayback.Prepare(src, prm, ground);
                Check(!ReferenceEquals(ra.Ghost, rb.Ghost) && !ReferenceEquals(ra.Tape, rb.Tape) && !ReferenceEquals(ra.Tape.Tape, rb.Tape.Tape) && !ReferenceEquals(ra.Tape.Body, rb.Tape.Body), "lap playback 3: two Prepare calls give distinct ghost, tape and body objects");
                byte[] before = rb.Ghost.ToBytes(); int bs = rb.Tape.Body.Samples.Count; ulong fpb = WorldTapeFingerprint.Of(rb.Ghost);
                ra.Ghost.Clear(); ra.Tape.Tape.Payload[10] ^= 0x5A; ra.Tape.Body.Samples.Clear(); ra.Tape.Tape.ActorStructure.Clear();
                Check(rb.Ghost.ToBytes().AsSpan().SequenceEqual(before) && rb.Tape.Body.Samples.Count == bs && rb.Tape.Tape.ActorStructure.Count > 0 && WorldTapeFingerprint.Of(rb.Ghost) == fpb, "lap playback 3: clearing and damaging one result leaves the other unchanged");
                var rc = LapPlayback.Prepare(src, prm, ground);
                Check(rc.HasTape && rc.Ghost.ToBytes().AsSpan().SequenceEqual(src.GhostBytes.Span) && WorldTapeCodec.Encode(rc.Tape.Tape).AsSpan().SequenceEqual(src.TapeBytes.Span) && src.Sha256() == hash0, "lap playback 3: a third Prepare after the damage still equals the source bytes, which are unchanged");
                var results = new LapPlaybackResult[8];
                Parallel.For(0, results.Length, i => results[i] = LapPlayback.Prepare(src, new CarParams(), new FlatGround()));
                Check(results.All(x => x.HasTape && x.Ghost.ToBytes().AsSpan().SequenceEqual(src.GhostBytes.Span)) && results.Select(x => x.Ghost).Distinct().Count() == results.Length && src.Sha256() == hash0, "lap playback 3: eight Prepare calls on worker threads at once all succeed with distinct objects");
            }

            // ---- (4) a ghost-only lap
            {
                var gonly = rig.Drive(5f, 20, 25);
                var so = archive.Open(archive.Submit(LapCap(gonly, null, kind: LapKind.Normal)).Id, out var why2);
                var r = so == null ? null : LapPlayback.Prepare(so, prm, ground);
                Check(so != null && !so.HasTape && r.Ok && !r.HasTape && r.Tape == null && r.TapeError == LapPlaybackError.None && r.CanSeek && r.KeyframeSeconds == 0f && Math.Abs(r.LapSeconds - 5f) < .05f, $"lap playback 4: a ghost-only lap prepares with no tape and no tape error ({why2})");
                var nt = LapPlayback.Prepare(LapSrc(src, dropTape: true), prm, ground);
                Check(nt.Ok && nt.TapeError == LapPlaybackError.TapeMissing, "lap playback 4: a lap whose meta says tape but whose source has none is ghost only with TapeMissing");
            }

            // ---- (5) typed failures, never an exception
            {
                var g = src.GhostBytes.ToArray(); var t = src.TapeBytes.ToArray(); bool threw = false;
                LapPlaybackResult R(LapSource s) { try { return LapPlayback.Prepare(s, prm, ground); } catch (Exception ex) { threw = true; GD.PrintErr(ex); return null; } }
                var r0 = R(null); Check(r0 != null && !r0.Ok && r0.Error == LapPlaybackError.NoSource && r0.Message.Length > 0, "lap playback 5: a null source is NoSource");
                var rg = R(LapSrc(src, ghost: g.AsSpan(0, g.Length / 2).ToArray())); Check(rg != null && !rg.Ok && rg.Ghost == null && rg.Error == LapPlaybackError.GhostDamaged && rg.Message.Length > 0, "lap playback 5: a truncated ghost is GhostDamaged");
                var gm = (byte[])g.Clone(); gm[12 + 3] ^= 0x40; var rm = R(LapSrc(src, ghost: gm)); Check(rm != null && !rm.Ok && rm.Error == LapPlaybackError.GhostMismatch, "lap playback 5: a ghost whose poses changed (same length) is GhostMismatch");
                var other = rig.Drive(8f, 30, 50); var otherSrc = archive.Open(archive.Submit(LapCap(other, rig.Rec)).Id);
                var rp = R(LapSrc(src, tape: otherSrc.TapeBytes.ToArray())); Check(rp != null && rp.Ok && rp.Tape == null && rp.TapeError == LapPlaybackError.TapePairMismatch, "lap playback 5: a tape of another lap is TapePairMismatch, the lap stays ghost only");
                var wrongTrack = WorldTapeCodec.Decode(t); wrongTrack.TrackId = "atomgrad"; var rt = R(LapSrc(src, tape: WorldTapeCodec.Encode(wrongTrack))); Check(rt != null && rt.Ok && rt.TapeError == LapPlaybackError.TapePairMismatch, "lap playback 5: a tape of another track is TapePairMismatch");
                var legacy = (byte[])t.Clone(); legacy[4] = 0; var rl = R(LapSrc(src, tape: legacy)); Check(rl != null && rl.Ok && rl.Tape == null && rl.TapeError == LapPlaybackError.TapeLegacy && rl.Message.Contains("format"), "lap playback 5: a tape of another format version is TapeLegacy");
                var newer = (byte[])t.Clone(); newer[4] = 9; Check(R(LapSrc(src, tape: newer))?.TapeError == LapPlaybackError.TapeLegacy, "lap playback 5: a newer format version is also TapeLegacy");
                var rn = R(LapSrc(src, tape: t.AsSpan(0, t.Length / 2).ToArray())); Check(rn != null && rn.Ok && rn.TapeError == LapPlaybackError.TapeDamaged, "lap playback 5: a truncated tape is TapeDamaged");
                var rs = R(LapSrc(src, tape: new byte[] { 1, 2, 3 })); Check(rs != null && rs.TapeError == LapPlaybackError.TapeDamaged, "lap playback 5: a 3-byte tape is TapeDamaged");
                var rz = R(LapSrc(src, tape: new byte[64])); Check(rz != null && rz.TapeError == LapPlaybackError.TapeDamaged, "lap playback 5: a zeroed tape is TapeDamaged");
                int typed = 0, total = 0; var rnd = new Random(7);
                for (int k = 0; k < 40; k++)
                {
                    var x = (byte[])t.Clone(); for (int q = 0; q < 3; q++) x[rnd.Next(x.Length)] ^= (byte)(1 + rnd.Next(255));
                    var rr = R(LapSrc(src, tape: x)); total++; if (rr != null && rr.Ok && (rr.Tape != null || rr.TapeError != LapPlaybackError.None)) typed++;
                }
                Check(!threw && typed == total, $"lap playback 5: 40 randomly corrupted tapes all return a typed result without an exception ({typed}/{total}; a corruption that still decodes gives a playable tape)");
                Check(!threw && src.Sha256() == hash0, "lap playback 5: no exception reached the caller, and the source hash is unchanged after all the damaged variants");
                GD.Print($"LAPPLAYBACK: typed failures ok, source sha256={hash0}");
            }
            archive.Flush(TimeSpan.FromSeconds(30));
        }
        finally { rig.Dispose(); try { Directory.Delete(root, true); } catch { } }
    }
}
