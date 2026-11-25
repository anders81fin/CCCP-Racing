using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// Where a track's JSON comes from. The four campaign tracks ship inside the game (res://tracks/ID.json); a player's own tracks are folders in the user data
/// (user://tracks/NAME/track.json) and carry the id "custom-NAME" in the game, so they never collide with a built-in track or its records.
/// Custom tracks are data only: the loader hands them to the same TrackDef/TrackBuilder as the built-in ones, after TrackValidator accepted them.
/// </summary>
public static class TrackSource
{
    public const string CustomPrefix = "custom-";
    public const string CustomRoot = "user://tracks/";
    public const string FileName = "track.json";
    static readonly Regex FolderName = new("^[a-z0-9][a-z0-9-]{0,31}$", RegexOptions.Compiled);

    public static bool IsCustom(string id) => id != null && id.StartsWith(CustomPrefix, StringComparison.Ordinal) && id.Length > CustomPrefix.Length;

    /// <summary>A folder name a custom track may have: lower-case letters, digits and hyphens, 1-32 characters.</summary>
    public static bool ValidFolderName(string name) => name != null && FolderName.IsMatch(name);

    /// <summary>An id that is only letters, digits, hyphens and underscores (1-40): an id that reaches here from a replay or a LAN message cannot walk out of tracks/.</summary>
    static bool SafeCampaignId(string id) => !string.IsNullOrEmpty(id) && id.Length <= 40 && id.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_');

    public static string FolderOf(string id) => IsCustom(id) ? id[CustomPrefix.Length..] : null;

    /// <summary>The file of a track id: a campaign track's res:// file, or a custom track's user:// file (null for a malformed custom id).</summary>
    public static string PathOf(string id)
    {
        if (!IsCustom(id)) return SafeCampaignId(id) ? $"res://tracks/{id}.json" : null;
        string folder = FolderOf(id);
        return ValidFolderName(folder) ? $"{CustomRoot}{folder}/{FileName}" : null;
    }

    /// <summary>
    /// Reads a track by id. A custom track is made to fit what custom tracks are (RaceScene's laps: no fuel, no depot, the default norm ladder, its own id);
    /// the validator checks it on every read (no build) and refuses every field that only the campaign tracks may use: a bad file throws InvalidDataException with the first error.
    /// </summary>
    public static TrackDef Load(string id)
    {
        string path = PathOf(id) ?? throw new ArgumentException("not a track id: " + id);
        if (IsCustom(id)) return FromText(FileAccess.FileExists(path) ? FileAccess.GetFileAsString(path) : null, id);
        return TrackDef.Load(path);
    }

    /// <summary>The norm ladder definition of a track for the pause menu's resume and the car tour's card: a campaign track's own; a custom track has none (the default ladder), and is not read at all
    /// (the file may have been edited or removed since the race began: the world already built is what runs).</summary>
    public static NormsDef NormsOf(string id) => IsCustom(id) ? null : Load(id).Norms;

    /// <summary>A custom track from its text, read once: the text that is validated is the text that is parsed (a file saved between two reads cannot slip through). Throws InvalidDataException with the first error.</summary>
    public static TrackDef FromText(string text, string id)
    {
        var report = TrackValidator.Validate(text, build: false);
        if (!report.Ok) throw new System.IO.InvalidDataException($"custom track '{id}': {report.FirstError()}");
        var def = TrackDef.Parse(text);
        Normalize(def, id);
        return def;
    }

    public static void Normalize(TrackDef def, string id)
    {
        def.Id = id;
        def.Fuel = false;
        def.Norms = null;
    }

    /// <summary>One custom track found in the user data.</summary>
    public sealed record Entry(string Id, string Folder, string Path, string Name, string Error);

    /// <summary>Every folder of user://tracks/ with a track.json, sorted by folder name. A track that does not validate stays in the list with its Error (the card says why); it cannot be started.</summary>
    public static List<Entry> Scan(string root = CustomRoot)
    {
        var list = new List<Entry>();
        using var dir = DirAccess.Open(root);
        if (dir == null) return list;
        var folders = new List<string>();
        dir.ListDirBegin();
        for (string n = dir.GetNext(); n != ""; n = dir.GetNext()) if (dir.CurrentIsDir() && !n.StartsWith('.')) folders.Add(n);
        dir.ListDirEnd();
        folders.Sort(StringComparer.Ordinal);
        foreach (string folder in folders)
        {
            string path = $"{root}{folder}/{FileName}";
            string id = CustomPrefix + folder;
            if (!ValidFolderName(folder)) { list.Add(new Entry(id, folder, path, folder.ToUpperInvariant(), "THE FOLDER NAME MUST BE LOWER-CASE LETTERS, DIGITS AND HYPHENS")); continue; }
            if (!FileAccess.FileExists(path)) continue;
            var report = TrackValidator.Validate(FileAccess.GetFileAsString(path), build: false);
            // the name as the file has it only when the card can draw it (the report's name is read before the checks); else the folder's name
            string fallback = folder.ToUpperInvariant(); if (fallback.Length > TrackValidator.MaxNameLength) fallback = fallback[..TrackValidator.MaxNameLength];
            string name = report.Name is { Length: > 0 } n2 && n2.Length <= TrackValidator.MaxNameLength && n2.All(CCCPRacing.UI.PixelFont.CanDraw) ? n2 : fallback;
            list.Add(new Entry(id, folder, path, name, report.Ok ? null : report.FirstError()));
        }
        return list;
    }
}
