using System;
using System.IO;
using System.Threading;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    // --stall-watch-test: a heartbeat that goes quiet is reported with what the main thread was last known to do, and the end of the silence is reported too.
    static volatile bool _stallSpinStop;
    void StallWatchTest()
    {
        string dir = Path.Combine(Path.GetTempPath(), "cccp-stallwatch-" + System.Environment.ProcessId);
        Directory.CreateDirectory(dir);
        string log = Path.Combine(dir, "stall.log");
        StallWatch.Start(dir, 700, 600);
        StallWatch.Load("ilyich", 321, 307);
        StallWatch.Waiting("the pair save of set 1 at the scene's exit", () => "worker phase 'compressing', task Running");
        var spin = new Thread(() => { double x = 0; while (!_stallSpinStop) x += Math.Sqrt(x + 1); }) { IsBackground = true, Name = "spin" }; spin.Start();
        for (int i = 0; i < 8; i++) { Thread.Sleep(250); }      // (no beats: a frozen main thread)
        StallWatch.Beat(); Thread.Sleep(600); StallWatch.Beat();
        StallWatch.Stop(); _stallSpinStop = true;
        string text = File.Exists(log) ? File.ReadAllText(log) : "";
        GD.Print("  stall.log:\n" + text);
        Check(text.Contains("STALL #1") && text.Contains("STALL #2"), "a silent main thread is reported, and again while it lasts");
        Check(text.Contains("track 'ilyich', checkpoint 321") && text.Contains("RaceScene line 307"), "the report names the loading checkpoint");
        Check(text.Contains("the pair save of set 1 at the scene's exit") && text.Contains("worker phase 'compressing'"), "the report names the wait and the worker's phase");
        Check(!OperatingSystem.IsLinux() || text.Contains("THREAD SAMPLE") && text.Contains("rip "), "a busy thread and the main thread are sampled: where they are (library and offset)");
        Check(text.Contains("STALL over"), "the end of the silence is reported");
        try { Directory.Delete(dir, true); } catch { }
    }
}
