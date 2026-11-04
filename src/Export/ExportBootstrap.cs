using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CCCPRacing.Export;

/// <summary>How the exporter child is started: which executable, which arguments, which folder holds its job-owned bootstrap, and what the bootstrap costs on disk.</summary>
public sealed record ExportLaunch(string Exe, IReadOnlyList<string> Args, string WorkDir, long CopiedBytes);

/// <summary>
/// The exporter child must render the NATIVE 480x270 pixel frame (Godot's movie writer takes its frame size from the project's display settings, read at startup, and from an
/// override.cfg beside the REAL executable / project.godot). So the child runs from a job-owned bootstrap folder:
///   packaged (Linux): a COPY of the executable + symlinks to its pck and data_* folder + a merged override.cfg (the installed game is never touched);
///   dev (editor binary + project): a job project of symlinks to a precise allowlist of the project's folders + project.godot + a merged override.cfg (never the 42 GB of media).
/// Windows and macOS are not implemented (layouts unverified): they fail with a clear message.
/// </summary>
public static class ExportBootstrap
{
    public const string UnsupportedText = "EXPORT IS NOT AVAILABLE ON THIS SYSTEM YET";

    // dev job project: exactly what the game needs to run from res://
    static readonly string[] ProjectAllowlist = { "project.godot", "CCCPRacing.csproj", "CCCPRacing.sln", "nuget.config", ".godot", "assets", "scenes", "src", "tracks" };

    /// <summary>Estimated bytes the bootstrap copies (the executable in packaged mode, nothing in dev).</summary>
    public static long CopyBytes(string exePath, bool dev) => dev ? 0 : new FileInfo(exePath).Length;

    public static bool Supported(out string why)
    {
        why = null;
        if (ExportWindowHider.Supported) return true;
        why = UnsupportedText;
        return false;
    }

    /// <param name="exePath">OS.GetExecutablePath()</param>
    /// <param name="projectDir">the project folder (res://) in dev mode, else null (packaged)</param>
    public static ExportLaunch Build(string jobDir, string exePath, string projectDir, string planPath, string dataDir, int fps, string title, long viewportWidth, long viewportHeight)
    {
        if (!OperatingSystem.IsLinux()) throw new InvalidOperationException(UnsupportedText);
        string game = Path.Combine(jobDir, "game"); Directory.CreateDirectory(game);
        string movie = Path.Combine(jobDir, "movie"); Directory.CreateDirectory(movie);
        string exeDir = Path.GetDirectoryName(exePath);
        string originalOverride = Path.Combine(projectDir ?? exeDir, "override.cfg");
        long copied = 0; string launchExe; var args = new List<string>();
        if (projectDir != null)
        {
            foreach (var name in ProjectAllowlist)
            {
                string src = Path.Combine(projectDir, name);
                if (Directory.Exists(src)) Directory.CreateSymbolicLink(Path.Combine(game, name), src);
                else if (File.Exists(src)) File.CreateSymbolicLink(Path.Combine(game, name), src);
            }
            launchExe = exePath; args.Add("--path"); args.Add(game);
        }
        else
        {
            string exeName = Path.GetFileName(exePath);
            string pck = Path.ChangeExtension(exePath, ".pck");
            if (!File.Exists(pck)) throw new InvalidOperationException("the game's pck was not found beside the executable");
            launchExe = Path.Combine(game, exeName);
            File.Copy(exePath, launchExe); copied = new FileInfo(launchExe).Length;
            File.SetUnixFileMode(launchExe, File.GetUnixFileMode(exePath));
            File.CreateSymbolicLink(Path.Combine(game, Path.GetFileName(pck)), pck);
            foreach (var d in Directory.GetDirectories(exeDir, "data_*")) Directory.CreateSymbolicLink(Path.Combine(game, Path.GetFileName(d)), d);
        }
        WriteOverride(Path.Combine(game, "override.cfg"), originalOverride, viewportWidth, viewportHeight);
        args.AddRange(new[] { "--display-driver", "x11", "--disable-vsync", "--fixed-fps", fps.ToString(), "--write-movie", Path.Combine(movie, "m.png"), "--resolution", $"{viewportWidth}x{viewportHeight}", "--",
            "--export-plan=" + planPath, "--no-intro", "--data-dir=" + dataDir });
        return new ExportLaunch(launchExe, args, game, copied);
    }

    /// <summary>The job's override.cfg: the ORIGINAL one (if the install has one, e.g. the Direct3D 12 backend) with only the display keys replaced. The original file is read, never written.</summary>
    static void WriteOverride(string target, string original, long w, long h)
    {
        var lines = new List<string>();
        bool inDisplay = false; var skip = new HashSet<string> { "window/size/viewport_width", "window/size/viewport_height", "window/size/mode", "window/size/no_focus" };
        if (File.Exists(original))
            foreach (var l in File.ReadAllLines(original))
            {
                string t = l.Trim();
                if (t.StartsWith("[")) inDisplay = t == "[display]";
                if (inDisplay && skip.Contains(t.Split('=')[0].Trim())) continue;
                lines.Add(l);
            }
        if (!lines.Any(l => l.Trim() == "[display]")) { lines.Add(""); lines.Add("[display]"); }
        int at = lines.FindIndex(l => l.Trim() == "[display]") + 1;
        // The child-local audit helper prevents mapping the window. Keep the engine in
        // drawable mode: minimizing is a compositor-dependent request, not invisibility.
        lines.InsertRange(at, new[] { "", $"window/size/viewport_width={w}", $"window/size/viewport_height={h}", "window/size/mode=0", "window/size/no_focus=true" });
        File.WriteAllLines(target, lines);
    }
}
