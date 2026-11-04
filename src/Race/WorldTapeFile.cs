using System;
using System.IO;
using CCCPRacing.Car;
using Godot;
using IOAccess = System.IO.FileAccess;

namespace CCCPRacing.Race;

/// <summary>
/// The tape file and the atomic ghost+tape pair save (replay-tape.md section 8). Knows paths, delegates for the byte encoding and an
/// ITapeFileOps for every replacement; nothing of the scene, the menu or the record store.
/// </summary>
public static class WorldTapeFile
{
    static readonly CarParams Params = new();

    internal static string Real(string path) => path.StartsWith("user://") || path.StartsWith("res://") ? ProjectSettings.GlobalizePath(path) : path;

    /// <summary>
    /// Writes both tmp files, verifies them, then replaces the ghost and then the tape. The result says what is on disk: GhostSaved the instant
    /// the ghost replacement has returned, TapeSaved likewise (before any Step hook). On any failure the files not yet replaced stay, the tmp
    /// files are removed and, unless both were saved, one warning is given. Never deletes a target and keeps no backup.
    /// </summary>
    public static TapePairResult SavePair(string ghostPath, string tapePath, Ghost ghost, WorldTape tape, ITapeFileOps ops, Func<WorldTape, byte[]> encode, Func<byte[], WorldTape> decode)
        => SavePairBytes(Real(ghostPath), Real(tapePath), ghost.ToBytes(), WorldTapeFingerprint.Of(ghost), tape, ops, encode, decode);

    /// <summary>
    /// The pair save on plain data only: absolute paths, the ghost file's bytes and the ghost's fingerprint (all made by the caller), and the finished tape. Nothing of Godot is touched,
    /// so it can run on a worker thread. Same order and steps as ever: both tmp files are written and flushed, read back and verified (the ghost's bytes are the ones given, the tape
    /// decodes and carries the ghost's fingerprint), then the ghost is replaced and then the tape; any failure leaves the old files and removes the tmp files.
    /// </summary>
    public static TapePairResult SavePairBytes(string gp, string tp, byte[] ghostBytes, ulong ghostFingerprint, WorldTape tape, ITapeFileOps ops, Func<WorldTape, byte[]> encode, Func<byte[], WorldTape> decode)
    {
        string tg = gp + ".tmp", tt = tp + ".tmp";
        bool ghostDone = false, tapeDone = false;
        try
        {
            using (var fs = new FileStream(tg, FileMode.Create, IOAccess.Write)) { fs.Write(ghostBytes, 0, ghostBytes.Length); fs.Flush(true); }
            using (var fs = new FileStream(tt, FileMode.Create, IOAccess.Write)) { var b = encode(tape); fs.Write(b, 0, b.Length); fs.Flush(true); }
            ops.Step("tmp-written");
            var back = File.ReadAllBytes(tg);
            if (back.Length != ghostBytes.Length || !back.AsSpan().SequenceEqual(ghostBytes)) throw new IOException("ghost tmp unreadable");
            var t = decode(File.ReadAllBytes(tt)) ?? throw new IOException("tape tmp unreadable");
            if (t.GhostFingerprint != ghostFingerprint) throw new IOException("pair fingerprint mismatch");
            ops.Step("verified");
            Place(ops, tg, gp); ghostDone = true; ops.Step("after-replace"); ops.Step("ghost-replaced");
            Place(ops, tt, tp); tapeDone = true; ops.Step("after-replace"); ops.Step("tape-replaced");
            return new TapePairResult(true, true);
        }
        catch (Exception ex)
        {
            if (!tapeDone) ops.Warn(ghostDone ? $"world tape: ghost saved, tape not ({ex.Message})" : $"world tape: save refused ({ex.Message})");
            foreach (var f in new[] { tg, tt }) try { if (File.Exists(f)) File.Delete(f); } catch { }
            return new TapePairResult(ghostDone, tapeDone);
        }
    }

    static void Place(ITapeFileOps ops, string tmp, string final)
    {
        ops.Step("before-replace");
        if (File.Exists(final)) ops.Replace(tmp, final); else ops.Move(tmp, final);
    }

    static void Put(ITapeFileOps ops, string tmp, string final) { Place(ops, tmp, final); ops.Step("after-replace"); }

    /// <summary>Replaces (or moves) a finished tmp file into place with the real file operations; on failure removes the tmp and returns false.</summary>
    public static bool CommitFile(string tmp, string final)
    {
        string t = Real(tmp), f = Real(final);
        try
        {
            Sync(t);
            Put(WorldTapeRealOps.Instance, t, f);
            return true;
        }
        catch { try { if (File.Exists(t)) File.Delete(t); } catch { } return false; }
    }

    static void Sync(string path) { try { using var fs = new FileStream(path, FileMode.Open, IOAccess.ReadWrite); fs.Flush(true); } catch { } }

    /// <summary>The tape at this path, or null when absent, unreadable, or recorded against another ghost than <paramref name="ghost"/>.</summary>
    public static WorldTape Load(string tapePath, Ghost ghost, Func<byte[], WorldTape> decode)
    {
        string p = Real(tapePath);
        try
        {
            if (!File.Exists(p)) return null;
            var t = decode(File.ReadAllBytes(p));
            if (t == null || t.SampleCount < 0 || !WorldTapeCodec.Validate(t)) { GD.Print($"World tape: {tapePath} unreadable, ignored"); return null; }
            if (t.GhostFingerprint != WorldTapeFingerprint.Of(ghost)) { GD.Print($"World tape: {tapePath} belongs to another ghost, ignored"); return null; }
            return t;
        }
        catch (Exception ex) { GD.Print($"World tape: {tapePath} failed to load ({ex.Message}), ignored"); return null; }
    }

    /// <summary>Deletes leftover ghost/tape .tmp files of an interrupted save (cleanup only, nothing is restored); returns how many.</summary>
    public static int CleanStale(string dir)
    {
        int n = 0;
        try
        {
            foreach (var f in Directory.GetFiles(Real(dir)))
            {
                var name = Path.GetFileName(f);
                if ((name.StartsWith("ghost_") || name.StartsWith("tape_")) && name.EndsWith(".bin.tmp")) { try { File.Delete(f); n++; } catch { } }
            }
        }
        catch { }
        return n;
    }
}

/// <summary>The real file operations: File.Replace (atomic) when the target exists, File.Move when not.</summary>
public sealed class WorldTapeRealOps : ITapeFileOps
{
    public static readonly WorldTapeRealOps Instance = new();
    public void Step(string name) { }
    public void Replace(string tmp, string final) => File.Replace(WorldTapeFile.Real(tmp), WorldTapeFile.Real(final), null);
    public void Move(string tmp, string final) => File.Move(WorldTapeFile.Real(tmp), WorldTapeFile.Real(final));
    public void Warn(string message) => GD.PushWarning(message);
}

/// <summary>WorldTapeFile bound to one encoder/decoder (the production wiring picks the codec).</summary>
public sealed class WorldTapeStorage
{
    readonly Func<WorldTape, byte[]> _encode;
    readonly Func<byte[], WorldTape> _decode;
    public WorldTapeStorage(Func<WorldTape, byte[]> encode, Func<byte[], WorldTape> decode) { _encode = encode; _decode = decode; }
    public TapePairResult SavePair(string ghostPath, string tapePath, Ghost ghost, WorldTape tape, ITapeFileOps ops) => WorldTapeFile.SavePair(ghostPath, tapePath, ghost, tape, ops, _encode, _decode);
    /// <summary>The same on plain data (absolute paths, the ghost's bytes and fingerprint): safe on a worker thread.</summary>
    public TapePairResult SavePairBytes(string gp, string tp, byte[] ghostBytes, ulong fingerprint, WorldTape tape, ITapeFileOps ops) => WorldTapeFile.SavePairBytes(gp, tp, ghostBytes, fingerprint, tape, ops, _encode, _decode);
    public WorldTape Load(string tapePath, Ghost ghost) => WorldTapeFile.Load(tapePath, ghost, _decode);
}
