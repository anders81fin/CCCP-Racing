using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// The command line check of a custom track, for the player's AI agent (it ships with the game):
///   cccp-racing --headless -- --validate-track=NAME|FOLDER|FILE.json
/// NAME is a folder of user://tracks/ (the player's data folder), a folder or a file is read where it stands. Prints one line per finding, a RESULT line and one TRACKCHECK line of JSON;
/// exits 0 when the track is acceptable (warnings allowed) and 1 when it is not.
/// </summary>
public static class TrackCheck
{
    const string Flag = "--validate-track=";

    /// <summary>The exit code when the command line asks for the check (it has been run), else null.</summary>
    public static int? FromCommandLine()
    {
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith(Flag, StringComparison.Ordinal)) return Run(a[Flag.Length..]);
            if (a == "--track-schema") { GD.Print(TrackSchema.Markdown()); return 0; }      // the field reference of docs/public/custom-tracks-reference.md
            if (a.StartsWith("--track-schema=", StringComparison.Ordinal)) { File.WriteAllText(a["--track-schema=".Length..], TrackSchema.Markdown()); return 0; }
        }
        return null;
    }

    public static string Resolve(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        // a bare NAME is the player's folder first (what the game will load), then a folder or file of that name where we stand
        if (TrackSource.ValidFolderName(target)) { string own = ProjectSettings.GlobalizePath($"{TrackSource.CustomRoot}{target}/{TrackSource.FileName}"); if (File.Exists(own)) return own; }
        string t = target.StartsWith("user://") ? ProjectSettings.GlobalizePath(target) : target;
        if (Directory.Exists(t)) t = Path.Combine(t, TrackSource.FileName);
        return File.Exists(t) ? t : null;
    }

    /// <summary>The check, never ending without a RESULT line and an exit code: any exception (a locked or unreadable file, a failure in the checks themselves) is printed as an error.</summary>
    public static int Run(string target)
    {
        try { return RunChecked(target); }
        catch (Exception e)
        {
            GD.Print($"ERROR: the check could not be completed: {e.GetType().Name}: {e.Message}");
            GD.Print("RESULT: FAILED");
            GD.Print("TRACKCHECK " + JsonSerializer.Serialize(new { ok = false, issues = new[] { new { level = "error", path = "", message = e.GetType().Name + ": " + e.Message } } }));
            return 1;
        }
    }

    static int RunChecked(string target)
    {
        string path = Resolve(target);
        if (path == null)
        {
            GD.Print($"ERROR: no track found for '{target}'. Give a folder name from {ProjectSettings.GlobalizePath(TrackSource.CustomRoot)}, a folder or a track.json file.");
            GD.Print("RESULT: FAILED");
            return 1;
        }
        var report = TrackValidator.Validate(File.ReadAllText(path), build: true);
        GD.Print($"Checking {path}");
        foreach (var i in report.Issues) GD.Print(i.ToString());
        GD.Print(report.Ok
            ? $"OK: '{report.Name}', {report.Points} control points, {report.Props} props, road {report.RoadLength:0} m{(report.Built ? $", built in {report.BuildSeconds:0.0} s, signature {report.Signature}" : "")}"
            : "The track cannot be played until the errors above are fixed.");
        GD.Print(report.Ok ? "RESULT: OK" : "RESULT: FAILED");
        GD.Print("TRACKCHECK " + JsonSerializer.Serialize(new
        {
            ok = report.Ok, name = report.Name, points = report.Points, props = report.Props, roadMetres = Math.Round(report.RoadLength), built = report.Built,
            buildSeconds = Math.Round(report.BuildSeconds, 1), signature = report.Signature,
            issues = report.Issues.Select(i => new { level = i.Error ? "error" : "warning", path = i.Path, message = i.Message }).ToArray(),
        }));
        return report.Ok ? 0 : 1;
    }
}
