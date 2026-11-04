using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Load profiler (env CCCP_LOADPROF=1; =quit also quits when the build is done). Records the wall time between
/// consecutive LoadingFrame checkpoints (gap = work with no checkpoint) and between real yields (slice),
/// labelled with the RaceScene.cs call-site lines, and prints the 10 longest of each. Test aid only.
/// </summary>
public static class LoadProf
{
    public static readonly bool On = System.Environment.GetEnvironmentVariable("CCCP_LOADPROF") is { Length: > 0 };
    public static bool Quit => System.Environment.GetEnvironmentVariable("CCCP_LOADPROF") == "quit";
    static readonly System.Diagnostics.Stopwatch Clock = new();
    static readonly System.Diagnostics.Stopwatch Total = new();
    static int _lastLine;
    /// <summary>The call-site line of the latest checkpoint that really gave a frame (a test aid for attributing frame stalls).</summary>
    public static int LastYield;
    static string _lastTag = "";
    static string _tag = "";
    static readonly List<(double ms, string label)> Gaps = new(), Slices = new();
    static double _sliceMs;
    static int _sliceFrom;

    /// <summary>Names the work that follows (shown in the label of the gap that ends at the next checkpoint).</summary>
    public static void Tag(string tag) { if (On) _tag = tag; }

    public static void Start() { if (!On) return; Gaps.Clear(); Slices.Clear(); _lastLine = 0; _tag = ""; _lastTag = "start"; _sliceMs = 0; _sliceFrom = 0; Clock.Restart(); Total.Restart(); }

    /// <summary>The work since the previous mark/checkpoint just finished and was called this.</summary>
    public static void Mark(string label) { if (!On) return; Piece(label); }

    /// <summary>Mark by the caller's file and line (put before a yield return null).</summary>
    public static void At([System.Runtime.CompilerServices.CallerFilePath] string f = "", [System.Runtime.CompilerServices.CallerLineNumber] int l = 0)
    { if (On) Piece(System.IO.Path.GetFileNameWithoutExtension(f) + ":" + l); }

    static void Piece(string label)
    {
        double g = Clock.Elapsed.TotalMilliseconds;
        Gaps.Add((g, label));
        if (_sliceMs == 0) _sliceFrom = _lastLine;
        _sliceMs += g;
        Clock.Restart();
    }

    /// <summary>Every LoadingFrame call; yielded = the call really gave a frame.</summary>
    public static void Checkpoint(int line, bool yielded)
    {
        if (!On) return;
        double g = Clock.Elapsed.TotalMilliseconds;
        string label = $"tail L{_lastLine} -> L{line}";
        Gaps.Add((g, label));
        if (_sliceMs == 0) _sliceFrom = _lastLine;
        _sliceMs += g;
        if (yielded) LastYield = line;
        if (yielded) { Slices.Add((_sliceMs, $"L{_sliceFrom} .. L{line}[{_tag}]")); _sliceMs = 0; }
        _lastLine = line; _lastTag = _tag;
        Clock.Restart();
    }

    /// <summary>Called after the yield (the frame is not build work).</summary>
    public static void Resume() { if (On) Clock.Restart(); }

    public static void Report(string track)
    {
        if (!On) return;
        Checkpoint(-1, true);
        double total = Total.Elapsed.TotalMilliseconds;
        GD.Print($"LOADPROF track={track} total_ms={total:F0} checkpoints={Gaps.Count} slices={Slices.Count}");
        if (System.Environment.GetEnvironmentVariable("CCCP_LOADPROF_FULL") != null) { int k = 0; foreach (var x in Gaps) { k++; GD.Print($"LOADPROF P {k} {(x.label.StartsWith("tail ") ? "C" : "M")} {x.ms:F2} {x.label}"); } }
        foreach (var (name, list) in new[] { ("gap", Gaps), ("slice", Slices) })
        {
            GD.Print($"LOADPROF {name}: max={list.Max(x => x.ms):F0} n>100={list.Count(x => x.ms > 100)} n>250={list.Count(x => x.ms > 250)} sum={list.Sum(x => x.ms):F0}");
            int i = 0;
            foreach (var x in list.OrderByDescending(x => x.ms).Take(10)) GD.Print($"LOADPROF {name} #{++i} {x.ms:F0} ms  {x.label}");
        }
    }
}
