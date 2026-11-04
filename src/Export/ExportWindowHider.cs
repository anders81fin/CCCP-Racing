using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace CCCPRacing.Export;

/// <summary>Per-renderer X11 map suppression, independent of the desktop's minimize support.
/// The GNU loader audit helper is embedded in the assembly, extracted to the owned job,
/// and inherited only by the renderer child. The parent and FFmpeg are untouched.</summary>
public static class ExportWindowHider
{
    public static bool Supported => OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64;

    public static string Configure(ProcessStartInfo child, string jobDir)
    {
        if (!Supported) throw new InvalidOperationException("BACKGROUND EXPORT REQUIRES LINUX X86_64");
        string helper = Path.GetFullPath(Path.Combine(jobDir, "game", "export-window.so"));
        string ready = Path.GetFullPath(Path.Combine(jobDir, "window-hidden.ready"));
        using (var input = typeof(ExportWindowHider).Assembly.GetManifestResourceStream("CCCPRacing.Export.HiddenWindow.x64")
            ?? throw new InvalidOperationException("BACKGROUND EXPORT HELPER IS MISSING"))
        using (var output = new FileStream(helper, FileMode.CreateNew, FileAccess.Write)) input.CopyTo(output);
        string previous = child.Environment.TryGetValue("LD_AUDIT", out var audit) ? audit : null;
        child.Environment["LD_AUDIT"] = string.IsNullOrEmpty(previous) ? helper : previous + ":" + helper;
        child.Environment["CCCP_EXPORT_WINDOW_READY"] = ready;
        return ready;
    }

    public static bool Ready(string path) => File.Exists(path) && File.ReadAllText(path) == "hidden-x11-v1\n";
}
