using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace CCCPRacing.Export;

/// <summary>Per-renderer hidden windows: X11 map suppression on Linux, hidden startup plus child-owned HWND suppression on Windows.
/// The GNU loader audit helper is embedded in the assembly, extracted to the owned job,
/// and inherited only by the renderer child. The parent and FFmpeg are untouched.</summary>
public static class ExportWindowHider
{
    public static bool Supported => OperatingSystem.IsWindows() && Environment.Is64BitProcess
        || OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64;

    public static string Configure(ProcessStartInfo child, string jobDir)
    {
        if (!Supported) throw new InvalidOperationException("BACKGROUND EXPORT REQUIRES WINDOWS 64-BIT OR LINUX X86_64");
        if (OperatingSystem.IsWindows()) return ConfigureWindows(child,jobDir);
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

    internal static string ConfigureWindows(ProcessStartInfo child,string jobDir)
    {
        string ready=Path.GetFullPath(Path.Combine(jobDir,"window-hidden.ready"));
        child.Environment["APPDATA"]=Path.Combine(jobDir,"appdata");
        child.Environment["LOCALAPPDATA"]=Path.Combine(jobDir,"localappdata");
        child.WindowStyle=ProcessWindowStyle.Hidden; // .NET 8+: honored with UseShellExecute=false
        child.CreateNoWindow=true;
        child.Environment["CCCP_EXPORT_HIDE_WINDOW"]="win32-v1";
        child.Environment["CCCP_EXPORT_WINDOW_READY"]=ready;
        return ready;
    }

    static bool _windowsReady;
    /// <summary>Called only inside the renderer child. Hide its own HWNDs, leaving the parent's window alone.</summary>
    public static void EnsureCurrentProcessHidden()
    {
        if(!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("CCCP_EXPORT_HIDE_WINDOW")!="win32-v1") return;
        string ready=Environment.GetEnvironmentVariable("CCCP_EXPORT_WINDOW_READY");
        if(string.IsNullOrEmpty(ready))return;
        bool found=false,hidden=true;
        uint own=(uint)Environment.ProcessId;
        EnumWindows((hwnd,_)=>{
            GetWindowThreadProcessId(hwnd,out uint pid);
            if(pid!=own)return true;
            found=true;
            if(IsWindowVisible(hwnd))ShowWindow(hwnd,0); // SW_HIDE, never minimize (the movie renderer keeps drawing)
            if(IsWindowVisible(hwnd))hidden=false;
            return true;
        },IntPtr.Zero);
        if(found && hidden && !_windowsReady) {
            string part=ready+".part";
            File.WriteAllText(part,"hidden-win32-v1\n");File.Move(part,ready,true);_windowsReady=true;
        }
    }

    delegate bool WindowCallback(IntPtr hwnd,IntPtr state);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]
    static extern bool EnumWindows(WindowCallback callback,IntPtr state);
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint processId);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]
    static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]
    static extern bool ShowWindow(IntPtr hwnd,int command);

    public static bool Ready(string path)
    {
        try { if(!File.Exists(path))return false;string mark=File.ReadAllText(path);return mark is "hidden-x11-v1\n" or "hidden-win32-v1\n"; }
        catch(IOException){return false;}
        catch(UnauthorizedAccessException){return false;}
    }
}
