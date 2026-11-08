using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// Where a track's records live (record-lap ghost, leaderboard, track signature) and what happens
/// to them when the track changes shape: records made on another road are renamed out of the way
/// (never deleted) so the new track starts with an empty board. Each run (SOCIALIST RALLY,
/// STAKHANOV LAPS) has its own ghost and board; records from before that were one set for both
/// and are archived once (ArchiveOldLayout).
/// </summary>
public class RecordStore
{
    /// <summary>Suffix for archived records: they were made on the v0.2 terrain.</summary>
    public const string ArchiveTag = "_v02";
    public const string ArchivedNotice = "NEW CARS OR TERRAIN. OLD RECORDS ARCHIVED BY THE MINISTRY";
    /// <summary>Suffix for records from before the two record sets (v0.3 and older, one board for both runs).</summary>
    public const string LayoutTag = "_v03";
    public const string LayoutNotice = "TWO BOARDS NOW. OLD RECORDS ARCHIVED BY THE MINISTRY";
    /// <summary>Record layout written to LayoutPath: 2 = one set per run. A missing file is layout 1.</summary>
    public const int Layout = 2;

    public readonly string Dir;
    public readonly string TrackId;
    /// <summary>Signature of the current track (TrackSignature.Compute).</summary>
    public readonly string Signature;

    /// <summary>A store that only knows its paths: no directory is made, nothing is migrated or cleaned. For the replay viewer, which never touches a file.</summary>
    public static RecordStore Inert(string trackId, string signature) => new(trackId, signature, "user://", true);

    RecordStore(string trackId, string signature, string dir, bool inert)
    {
        Dir = string.IsNullOrEmpty(dir) ? "user://" : dir.EndsWith('/') ? dir : dir + "/";
        TrackId = trackId;
        Signature = signature;
    }

    /// <summary>The paths of another track's records in this directory, with no side effects (no directory made, nothing migrated or cleaned). For LAN board sharing.</summary>
    public static RecordStore PathsOnly(string dir, string trackId) => new(trackId, null, dir, true);

    /// <summary>The signature file's text when it exists and looks like a signature; null when it is missing or not one (never the legacy default).</summary>
    public string SavedSignatureOrNull()
    {
        if (!FileAccess.FileExists(SignaturePath)) return null;
        string s = FileAccess.GetFileAsString(SignaturePath).StripEdges();
        return Track.TrackSignature.LooksValid(s) ? s : null;
    }

    /// <summary>Any record file of this track (ghost, board or tape of any set, or the signature).</summary>
    public bool HasAnyRecordFile()
    {
        for (int set = 0; set < Sets; set++)
            if (FileAccess.FileExists(GhostForSet(set)) || FileAccess.FileExists(BoardForSet(set)) || FileAccess.FileExists(TapeForSet(set))) return true;
        return FileAccess.FileExists(SignaturePath);
    }

    /// <param name="dir">Data directory: "user://" normally, or a test directory (--data-dir).</param>
    public RecordStore(string dir, string trackId, string signature)
    {
        Dir = string.IsNullOrEmpty(dir) ? "user://" : dir.EndsWith('/') ? dir : dir + "/";
        TrackId = trackId;
        Signature = signature;
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(Dir));
        BonusTrackMigration.Migrate(Dir);
        WorldTapeFile.CleanStale(Dir);
    }

    /// <summary>SOCIALIST RALLY records (same names as before the split).</summary>
    public string GhostPath => $"{Dir}ghost_{TrackId}.bin";
    public string BoardPath => $"{Dir}leaderboard_{TrackId}.cfg";
    /// <summary>STAKHANOV LAPS records.</summary>
    public string StakhanovGhostPath => $"{Dir}ghost_{TrackId}_stakhanov.bin";
    public string StakhanovBoardPath => $"{Dir}leaderboard_{TrackId}_stakhanov.cfg";
    public string SignaturePath => $"{Dir}track_{TrackId}.sig";
    public string LayoutPath => $"{Dir}records_{TrackId}.ver";

    /// <summary>The SOCIALIST RALLY's DIESEL and JET FUEL records (PETROL has the rally's original files).</summary>
    public string DieselGhostPath => $"{Dir}ghost_{TrackId}_diesel.bin";
    public string DieselBoardPath => $"{Dir}leaderboard_{TrackId}_diesel.cfg";
    public string RocketGhostPath => $"{Dir}ghost_{TrackId}_rocket.bin";
    public string RocketBoardPath => $"{Dir}leaderboard_{TrackId}_rocket.cfg";
    public string SputnikGhostPath => $"{Dir}ghost_{TrackId}_sputnik.bin";
    public string SputnikBoardPath => $"{Dir}leaderboard_{TrackId}_sputnik.cfg";

    /// <summary>The record sets: 0 the rally on petrol, 1 Stakhanov laps, 2 the rally on diesel, 3 the rally on jet fuel, 4 the rally on the SPUTNIK.</summary>
    public const int Sets = 5;

    public string GhostForSet(int set) => set switch { 1 => StakhanovGhostPath, 2 => DieselGhostPath, 3 => RocketGhostPath, 4 => SputnikGhostPath, _ => GhostPath };
    /// <summary>The world tape that belongs to the set's ghost (same signature and set suffixes).</summary>
    public string TapeForSet(int set) => $"{Dir}tape_" + GhostForSet(set)[$"{Dir}ghost_".Length..];
    public string BoardForSet(int set) => set switch { 1 => StakhanovBoardPath, 2 => DieselBoardPath, 3 => RocketBoardPath, 4 => SputnikBoardPath, _ => BoardPath };

    /// <summary>The record set of a rally on this fuel.</summary>
    public static int SetOf(FuelGrade fuel) => fuel switch { FuelGrade.Diesel => 2, FuelGrade.Rocket => 3, FuelGrade.Sputnik => 4, _ => 0 };

    /// <summary>Signature the saved records were made on; files from before signatures are the v0.2 track.</summary>
    public string StoredSignature()
    {
        if (!FileAccess.FileExists(SignaturePath)) return Track.TrackSignature.LegacyBonus;
        return FileAccess.GetFileAsString(SignaturePath).StripEdges();
    }

    /// <summary>Call whenever the ghost or the leaderboard is saved: they belong to this track now.</summary>
    public void WriteSignature()
    {
        using var f = FileAccess.Open(SignaturePath, FileAccess.ModeFlags.Write);
        if (f == null) { GD.PrintErr($"Records: could not write {SignaturePath}: {FileAccess.GetOpenError()}"); return; }
        f.StoreString(Signature + "\n");
    }

    /// <summary>
    /// If a ghost or leaderboard (of either run) exists and was made on a differently shaped road,
    /// renames them all (and the old signature) to *_v02 (*_v02_2 ... if taken). Returns the new file names.
    /// </summary>
    public List<string> ArchiveIfChanged()
    {
        var paths = new List<string>(Sets * 3);
        for (int set = 0; set < Sets; set++)
        {
            paths.Add(GhostForSet(set));
            paths.Add(BoardForSet(set));
            paths.Add(TapeForSet(set));
        }
        var files = paths.FindAll(FileAccess.FileExists);
        if (files.Count == 0) return new List<string>();
        string stored = StoredSignature();
        if (stored == Signature) return new List<string>();
        if (FileAccess.FileExists(SignaturePath)) files.Add(SignaturePath);
        var moved = Move(files, ArchiveTag, out _);
        GD.Print($"Records: track {TrackId} changed shape (signature {stored} -> {Signature}); archived {string.Join(", ", moved)}");
        return moved;
    }

    /// <summary>Record layout of the data directory (LayoutPath); 1 when it has no layout file.</summary>
    public int StoredLayout()
    {
        if (!FileAccess.FileExists(LayoutPath)) return 1;
        return int.TryParse(FileAccess.GetFileAsString(LayoutPath).StripEdges(), out int v) ? v : 1;
    }

    /// <summary>
    /// Once: records from before the two record sets had no run, so the ghost and the board are
    /// renamed to *_v03 (*_v03_2 ... if taken) and nothing else; then the layout file is written so
    /// it never happens again. A fresh directory just gets the layout file. Returns the new file names.
    /// </summary>
    public List<string> ArchiveOldLayout()
    {
        if (StoredLayout() >= Layout) return new List<string>();
        var files = Existing(GhostPath, BoardPath, TapeForSet(0));
        var moved = Move(files, LayoutTag, out bool failed);
        if (failed) return moved;     // try again next launch rather than mix old and new
        using (var f = FileAccess.Open(LayoutPath, FileAccess.ModeFlags.Write))
        {
            if (f == null) GD.PrintErr($"Records: could not write {LayoutPath}: {FileAccess.GetOpenError()}");
            else f.StoreString(Layout + "\n");
        }
        if (moved.Count > 0) GD.Print($"Records: one set per run now; archived {string.Join(", ", moved)}");
        return moved;
    }

    static List<string> Existing(params string[] paths) => new(System.Array.FindAll(paths, FileAccess.FileExists));

    /// <summary>Renames files with one suffix for the whole set, free for every file in it.</summary>
    static List<string> Move(List<string> files, string baseTag, out bool failed)
    {
        var moved = new List<string>();
        failed = false;
        string tag = baseTag;
        for (int k = 2; files.Exists(p => FileAccess.FileExists(Archived(p, tag))); k++) tag = $"{baseTag}_{k}";
        foreach (var p in files)
        {
            string to = Archived(p, tag);
            var err = DirAccess.RenameAbsolute(ProjectSettings.GlobalizePath(p), ProjectSettings.GlobalizePath(to));
            if (err == Error.Ok) moved.Add(to);
            else { failed = true; GD.PrintErr($"Records: could not archive {p} -> {to}: {err}"); }
        }
        return moved;
    }

    static string Archived(string path, string tag)
    {
        int dot = path.LastIndexOf('.');
        return path[..dot] + tag + path[dot..];
    }
}
