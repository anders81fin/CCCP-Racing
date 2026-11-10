using System;
using System.IO;
using System.Linq;
using System.Text;
using CCCPRacing.Audio;
using CCCPRacing.Replay;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// --import-take=DIR: puts a recorded title take (a --title-rec lap-NNN folder: ghost, world tape, fuel list, track signature) into the active personnel file's lap archive as a kept recording
/// named TITLE TAKE, so the replay viewer can open it (KGB FILES > RECORDINGS) and the MP4 exporter can render it with its fuel changes and its day. Nothing else is written; the game quits after the import.
/// Run it as: godot-mono --headless --path . -- --import-take=KANSIO/lap-001
/// </summary>
public partial class RaceScene
{
    string _importTakeDir;

    void ImportTitleTake(string dir)
    {
        try
        {
            dir = Path.GetFullPath(dir);
            string One(string pattern) => Directory.GetFiles(dir, pattern).OrderBy(f => f).FirstOrDefault();
            string ghostPath = One("ghost_*.bin"), tapePath = One("tape_*.bin"), sigPath = One("track_*.sig"), fuelsPath = Path.Combine(dir, "fuels.txt");
            if (ghostPath == null || sigPath == null) { GD.PushError($"Import take: {dir} has no ghost_*.bin and track_*.sig (a --title-rec lap folder)"); return; }
            string signature = File.ReadAllText(sigPath).Trim();
            if (signature != _records.Signature) { GD.PushError($"Import take: the take was driven on another track shape ({signature}, this game has {_records.Signature}): refused"); return; }
            byte[] ghost = File.ReadAllBytes(ghostPath), tape = tapePath != null ? File.ReadAllBytes(tapePath) : null;
            var fingerprint = LapGhostBytes.Fingerprint(ghost, out _, out float seconds);
            if (fingerprint == null) { GD.PushError("Import take: ghost.bin is damaged"); return; }
            string fuels = File.Exists(fuelsPath) ? File.ReadAllText(fuelsPath).Trim() : "";
            string id = "lap-title-take-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string name = "TITLE TAKE " + LapTimer.Format(seconds);
            var meta = new LapMeta
            {
                Id = id, TrackId = _track.Id, Set = RecordStore.SetOf(FuelGrade.Diesel), Mode = "title take", LapSeconds = seconds, LapNumber = 1, Kind = LapKind.Normal, Ranked = false,
                RecordedUtc = File.GetLastWriteTimeUtc(ghostPath), DayClock = -1, HasTape = tape != null && tape.Length > 0, GhostFingerprint = fingerprint.Value,
                TrackSignature = signature, Layout = RecordStore.Layout, Name = name, Retained = true, TotalBytes = ghost.Length + (tape?.Length ?? 0), Fuels = fuels,
            };
            var archive = LapArchiveBox;
            var stage = archive.StageImport(id, Encoding.UTF8.GetBytes(meta.ToJson()), ghost, meta.HasTape ? tape : null,
                new LapArchive.ImportExpect(_track.Id, meta.Set, seconds, signature, fingerprint.Value, name));
            string error = stage.ExistingId != null ? null : archive.CommitImport(stage);
            if (stage.ExistingId != null) GD.Print($"Import take: this take is already in the archive as {stage.ExistingId}");
            else if (error != null) GD.PushError("Import take: refused: " + error);
            else GD.Print($"Import take: {name} is in the lap archive as {id}, {LapArchiveDir}: KGB FILES > RECORDINGS shows it (fuels {fuels})");
        }
        catch (Exception ex) { GD.PushError("Import take: " + ex.Message); }
    }
}
