using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CCCPRacing.Export;

public sealed record ExportLaunch(string Exe, IReadOnlyList<string> Args, string WorkDir, long CopiedBytes);

/// <summary>The movie renderer runs from its own bootstrap and override.cfg. Linux links read-only resources;
/// Windows copies them, requiring no symlink privilege. The installed game and its backend override are never changed.</summary>
public static class ExportBootstrap
{
    public const string UnsupportedText = "EXPORT IS NOT AVAILABLE ON THIS SYSTEM YET";
    static readonly string[] ProjectAllowlist = { "project.godot", "CCCPRacing.csproj", "CCCPRacing.sln", "nuget.config", ".godot", "assets", "scenes", "src", "tracks" };

    public static long CopyBytes(string exePath, bool dev, string projectDir = null)
        => CopyBytesFor(exePath, dev, projectDir, OperatingSystem.IsWindows());

    internal static long CopyBytesFor(string exePath, bool dev, string projectDir, bool windows)
    {
        if (!windows) return dev ? 0 : new FileInfo(exePath).Length;
        return dev ? ProjectAllowlist.Sum(n => Size(Path.Combine(projectDir ?? throw new InvalidOperationException("the project folder is missing"), n)))
            : PackageFiles(exePath).Sum(Size);
    }

    static IEnumerable<string> PackageFiles(string exe)
    {
        yield return exe;
        string pck = Path.ChangeExtension(exe, ".pck");
        if (!File.Exists(pck)) throw new InvalidOperationException("the game's pck was not found beside the executable");
        yield return pck;
        string dir = Path.GetDirectoryName(exe);
        foreach (var d in Directory.GetDirectories(dir, "data_*")) yield return d;
        foreach (var f in Directory.GetFiles(dir, "*.dll")) yield return f;
    }

    static void CheckLink(string path)
    {
        if ((Directory.Exists(path) ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path)).LinkTarget != null)
            throw new IOException("the export bootstrap cannot copy a linked path: " + path);
    }
    static long Size(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path)) return 0;
        CheckLink(path);
        if (!Directory.Exists(path)) return new FileInfo(path).Length;
        return Directory.EnumerateFileSystemEntries(path).Sum(Size);
    }
    static long Copy(string source, string target)
    {
        if (!Directory.Exists(source) && !File.Exists(source)) return 0;
        CheckLink(source);
        if (!Directory.Exists(source)) {
            File.Copy(source, target);
            File.SetAttributes(target, File.GetAttributes(target) & ~FileAttributes.ReadOnly);
            return new FileInfo(target).Length;
        }
        Directory.CreateDirectory(target);
        long bytes = 0;
        foreach (var p in Directory.EnumerateFileSystemEntries(source)) bytes += Copy(p, Path.Combine(target, Path.GetFileName(p)));
        return bytes;
    }

    public static bool Supported(out string why)
    {
        why = ExportWindowHider.Supported ? null : UnsupportedText;
        return why == null;
    }

    public static ExportLaunch Build(string jobDir, string exePath, string projectDir, string planPath, string dataDir, int fps, string title, long viewportWidth, long viewportHeight)
    {
        if (!Supported(out string why)) throw new InvalidOperationException(why);
        return BuildFor(jobDir, exePath, projectDir, planPath, dataDir, fps, title, viewportWidth, viewportHeight, OperatingSystem.IsWindows());
    }

    // The platform argument lets Linux tests exercise the actual Windows copy/argument path without launching a Windows binary.
    internal static ExportLaunch BuildFor(string jobDir, string exePath, string projectDir, string planPath, string dataDir, int fps, string title, long viewportWidth, long viewportHeight, bool windows)
    {
        string game = Path.Combine(jobDir, "game"); Directory.CreateDirectory(game);
        string movie = Path.Combine(jobDir, "movie"); Directory.CreateDirectory(movie);
        string exeDir = Path.GetDirectoryName(exePath);
        string originalOverride = Path.Combine(projectDir ?? exeDir, "override.cfg");
        long copied = 0; string launchExe; var args = new List<string>();
        if (projectDir != null)
        {
            foreach (var name in ProjectAllowlist)
            {
                string src = Path.Combine(projectDir, name), dest = Path.Combine(game, name);
                if (windows) copied += Copy(src, dest);
                else if (Directory.Exists(src)) Directory.CreateSymbolicLink(dest, src);
                else if (File.Exists(src)) File.CreateSymbolicLink(dest, src);
            }
            launchExe = exePath; args.Add("--path"); args.Add(game);
        }
        else if (windows)
        {
            foreach (var p in PackageFiles(exePath)) copied += Copy(p, Path.Combine(game, Path.GetFileName(p)));
            launchExe = Path.Combine(game, Path.GetFileName(exePath));
        }
        else
        {
            string pck = Path.ChangeExtension(exePath, ".pck");
            if (!File.Exists(pck)) throw new InvalidOperationException("the game's pck was not found beside the executable");
            launchExe = Path.Combine(game, Path.GetFileName(exePath));
            File.Copy(exePath, launchExe); copied = new FileInfo(launchExe).Length;
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(launchExe, File.GetUnixFileMode(exePath));
            File.CreateSymbolicLink(Path.Combine(game, Path.GetFileName(pck)), pck);
            foreach (var d in Directory.GetDirectories(exeDir, "data_*")) Directory.CreateSymbolicLink(Path.Combine(game, Path.GetFileName(d)), d);
        }
        WriteOverride(Path.Combine(game, "override.cfg"), originalOverride, viewportWidth, viewportHeight);
        if (!windows) args.AddRange(new[] { "--display-driver", "x11" });
        args.AddRange(new[] { "--log-file", Path.Combine(jobDir,"godot.log"), "--disable-vsync", "--fixed-fps", fps.ToString(), "--write-movie", Path.Combine(movie, "m.png"), "--resolution", $"{viewportWidth}x{viewportHeight}", "--",
            "--export-plan=" + planPath, "--no-intro", "--data-dir=" + dataDir });
        return new ExportLaunch(launchExe, args, game, copied);
    }

    static void WriteOverride(string target, string original, long w, long h)
    {
        var lines = new List<string>(); string section = "";
        var display = new HashSet<string> { "window/size/viewport_width", "window/size/viewport_height", "window/size/mode", "window/size/no_focus" };
        if (File.Exists(original)) foreach (var l in File.ReadAllLines(original))
        {
            string t = l.Trim(); if (t.StartsWith("[")) section = t;
            string key = t.Split('=')[0].Trim();
            if (section == "[display]" && display.Contains(key) || section == "[application]" && key == "boot_splash/show_image") continue;
            lines.Add(l);
        }
        void Put(string name, params string[] values)
        {
            int at = lines.FindIndex(l => l.Trim() == name);
            if (at < 0) { lines.Add(""); lines.Add(name); at = lines.Count - 1; }
            lines.InsertRange(at + 1, values);
        }
        Put("[display]", "", $"window/size/viewport_width={w}", $"window/size/viewport_height={h}", "window/size/mode=0", "window/size/no_focus=true");
        Put("[application]", "boot_splash/show_image=false"); // only the hidden renderer; normal game startup retains its splash
        File.WriteAllLines(target, lines);
    }
}
