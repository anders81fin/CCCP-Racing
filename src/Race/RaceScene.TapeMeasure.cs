using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    // TAPEMEASURE=1: records 30 s of the actors that are marked (Tape.Part), prints what the world tape costs and quits.
    // TAPEMEASURE=all: the same with every Node3D under every actor marked as a part (the busiest world the tape could ever see).
    WorldTapeRecorder _tmRec;
    WorldTapeIndex _tmIndex;
    float _tmClock;
    long _tmAlloc;
    readonly List<double> _tmMs = new(2048);

    void TapeMeasureFrame(float dt)
    {
        var mode = OS.GetEnvironment("TAPEMEASURE");
        if (mode == "" || _state != State.Racing || _tmClock < 0) return;
        if (_tmRec == null)
        {
            var actors = new List<(string, Node3D)>(); var seen = new HashSet<string>();
            foreach (var a in _actors)
            {
                if (a.Node == null || !IsInstanceValid(a.Node)) continue;
                var key = $"{a.Who}:{a.Node.Name}";
                if (seen.Add(key)) actors.Add((key, a.Node));
            }
            if (mode == "all") foreach (var (_, root) in actors) TapeMeasureMarkAll(root, root, "n");
            _tmIndex = new WorldTapeIndex(actors); _tmRec = new WorldTapeRecorder();
            var per = new List<string>();
            foreach (var key in _tmIndex.ActorKeys) { int n = 0; for (int i = 0; i < _tmIndex.Count; i++) if (_tmIndex.IdOf(i).ActorKey == key) n++; if (n > 0) per.Add($"{key}={n}"); }
            GD.Print($"tapemeasure: mode {mode}, {actors.Count} actors, {_tmIndex.Count} parts ({string.Join(", ", per)})");
            _tmRec.Begin(_tmIndex);
            return;
        }
        _tmClock += dt;
        int before = _tmRec.Samples; long a0 = GC.GetAllocatedBytesForCurrentThread(); long t0 = Stopwatch.GetTimestamp();
        _tmRec.Record(_tmClock);
        long t1 = Stopwatch.GetTimestamp(); long a1 = GC.GetAllocatedBytesForCurrentThread();
        if (_tmRec.Samples > before && before > 0) { _tmMs.Add((t1 - t0) * 1000.0 / Stopwatch.Frequency); if (_tmClock > 1f) _tmAlloc += a1 - a0; }
        if (_tmClock < 30f) return;
        _tmMs.Sort();
        double avg = 0; foreach (var m in _tmMs) avg += m; avg /= Math.Max(1, _tmMs.Count);
        var sw = Stopwatch.StartNew();
        var tape = _tmRec.Finish(_tmClock, 1UL); long finishMs = sw.ElapsedMilliseconds;
        int size = WorldTapeCodec.Encode(tape).Length;
        GD.Print($"tapemeasure: {_tmRec.Samples} samples in {_tmClock:F1} s, {_tmIndex.Count} parts, changed per sample avg {(double)_tmRec.Entries / Math.Max(1, _tmRec.Samples):F1}, record avg {avg:F3} ms, p99 {_tmMs[(int)(_tmMs.Count * 0.99)]:F3} ms, max {_tmMs[^1]:F3} ms, allocated {_tmAlloc} bytes, unregistered spin {_tmRec.UnregisteredSpinners}, finish {finishMs} ms, encoded {size / 1024} KB");
        _tmClock = -1;
        CCCPRacing.Audio.AudioMix.Quit(this);
    }

    static void TapeMeasureMarkAll(Node3D actor, Node n, string path)
    {
        if (n is Node3D n3 && !n.HasMeta(WorldTapeConst.PartMeta)) n3.SetMeta(WorldTapeConst.PartMeta, n == actor ? "root" : path);
        int i = 0;
        foreach (var c in n.GetChildren()) TapeMeasureMarkAll(actor, c, path + "_" + i++);
    }
}
