using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// Phase 2 measurement spike 1 (docs/player-replay-phase2-proposal.md): nested world build time, memory and cleanup. Spike code only; owner: spike agent 1.
/// A child game started with --viewer-spike=MODE (MODE: comma list of run | control | nounsub | cross=TRACK | cycles=N) opens and closes a second RaceScene in a
/// SubViewport with OwnWorld3D over the live scene (live: ProcessMode.Disabled + hidden, as OpenInstruction) and prints "VSPIKE|kind|k=v|..." lines;
/// PhysicsTest.ViewerSpike.cs judges them.
/// </summary>
public partial class RaceScene
{
    static bool _spikeStarted;
    ulong _spikeEnterUs, _spikeReadyUs;

    [DllImport("libc", EntryPoint = "malloc_trim")] static extern int SpikeMallocTrim(nuint pad);

    public override void _EnterTree()
    {
        if (!_spikeStarted) return;      // production scenes pay nothing for the spike
        _spikeEnterUs = Time.GetTicksUsec();
        Ready += () => _spikeReadyUs = Time.GetTicksUsec();      // emitted when _Ready returns (the build is synchronous when not booting)
    }

    void ViewerSpikeTick(float dt)
    {
        if (_spikeStarted || ViewerMode || InstructionTrack != null) return;
        string mode = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--viewer-spike="))?["--viewer-spike=".Length..];
        if (mode == null) return;
        _spikeStarted = true;
        _ = ViewerSpikeRun(mode);
    }

    static string SpikeLoad() { try { return File.ReadAllText("/proc/loadavg").Split(' ')[0]; } catch { return "?"; } }
    static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    static void SpikeLog(string kind, params (string k, object v)[] kv)
    {
        var sb = new StringBuilder("VSPIKE|").Append(kind);
        foreach (var (k, v) in kv) sb.Append('|').Append(k).Append('=').Append(v is double d ? F(d) : v is float f ? F(f) : v);
        GD.Print(sb.ToString());
    }

    static (double rss, double hwm) SpikeMem()
    {
        double rss = -1, hwm = -1;
        try
        {
            foreach (var l in File.ReadLines("/proc/self/status"))
            {
                if (l.StartsWith("VmRSS:")) rss = double.Parse(l.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture) / 1024.0;
                else if (l.StartsWith("VmHWM:")) hwm = double.Parse(l.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1], CultureInfo.InvariantCulture) / 1024.0;
            }
        }
        catch { }
        return (rss, hwm);
    }

    static bool SpikeResetPeak() { try { File.WriteAllText("/proc/self/clear_refs", "5"); return true; } catch { return false; } }
    static int SpikeCount(string dir) { try { return Directory.GetFileSystemEntries(dir).Length; } catch { return -1; } }
    static int SpikeNodes(Node n) { int c = 1; foreach (var ch in n.GetChildren()) c += SpikeNodes(ch); return c; }

    static readonly (string type, string member)[] SpikeStatics =
    {
        ("NightGlow", "Level"), ("NightGlow", "Glows"), ("NightGlow", "Lamps"), ("RadarHead", "All"), ("Surfaces", "Wetness"),
        ("SeeThrough", "Car"), ("SeeThrough", "Off"), ("FadeUnder", "Car"), ("FadeUnder", "Off"), ("GulagTruck", "_onRoad"),
        ("Missile", "_serial"), ("Missile", "SeedBase"), ("Props", "Materials"), ("Props", "BatchCache"),
        ("NormPlan", "Ladder"), ("NormPlan", "BaseLadder"), ("NormPlan", "Factor"), ("NormPlan", "_def"),
        ("RaceScene", "_trackId"), ("RaceScene", "_resume"), ("RaceScene", "_loadingRunMode"), ("RaceScene", "_resumeReplay"), ("RaceScene", "_tapeScenes"), ("RaceScene", "_aaMode"), ("RaceScene", "_softLevel"), ("RaceScene", "_lapsFuelStart"), ("RaceScene", "_lapsFuelLast"), ("PixelFrame", "_native"),
        ("AudioMix", "Trims"), ("AudioMix", "Ducked"), ("CannonStrikeSound", "_players"), ("HindGunSound", "_players"),
        ("KolkhozWorkerModel", "WorkerMaterial"), ("WinterWorkerModel", "Material"), ("PosterGrade", "_table"), ("TrackLoadingScreen", "Active"), ("LapArchive", "Shared"),
    };

    static string SpikeStaticValue(string type, string member)
    {
        try
        {
            var t = typeof(RaceScene).Assembly.GetTypes().FirstOrDefault(x => x.Name == type);
            if (t == null) return "NOTYPE";
            const BindingFlags fl = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            object v;
            var f = t.GetField(member, fl); var p = t.GetProperty(member, fl);
            if (f != null) v = f.GetValue(null); else if (p != null) v = p.GetValue(null); else return "NOMEMBER";
            if (v == null) return "null";
            if (v is float fv) return F(fv);
            if (v is Vector3 v3) return $"{F(v3.X)}/{F(v3.Y)}/{F(v3.Z)}";
            if (v is float[] arr) return $"len{arr.Length}:{(arr.Length > 0 ? F(arr[0]) : "")}";
            if (v is IDictionary dict) return SpikeDescribe(dict.Values, dict.Count);
            if (v is ICollection c) return SpikeDescribe(c, c.Count);
            if (v is GodotObject go) return (GodotObject.IsInstanceValid(go) ? "obj#" : "FREED#") + go.GetInstanceId();
            return v.ToString().Replace('|', '/').Replace('=', ':');
        }
        catch (Exception e) { return "ERR:" + e.GetType().Name; }
    }

    /// <summary>"n{count} dead{freed objects} first#{instance id}": the count alone hides a list that now holds another world's objects.</summary>
    static string SpikeDescribe(IEnumerable items, int count)
    {
        int dead = 0; string first = "";
        foreach (var it in items)
        {
            object o = it is System.Runtime.CompilerServices.ITuple t && t.Length > 0 ? t[0] : it;
            if (o is GodotObject go)
            {
                if (first == "") first = "#" + go.GetInstanceId();
                if (!GodotObject.IsInstanceValid(go)) dead++;
            }
        }
        return $"n{count} dead{dead} first{first}";
    }

    static object SpikeField(object o, string name)
    {
        try { return o?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(o); } catch { return null; }
    }

    void SpikeStaticsLog(string phase, int cycle)
    {
        var kv = new List<(string, object)> { ("phase", phase), ("cycle", cycle) };
        foreach (var (t, m) in SpikeStatics) kv.Add(($"{t}.{m}", SpikeStaticValue(t, m)));
        kv.Add(("DayNight._unlit", SpikeField(_dayNight, "_unlit") is ICollection a ? "n" + a.Count : "?"));
        kv.Add(("DayNight._sceneryLights", SpikeField(_dayNight, "_sceneryLights") is ICollection b ? "n" + b.Count : "?"));
        kv.Add(("Engine.TimeScale", Engine.TimeScale));
        kv.Add(("Input.MouseMode", Input.MouseMode));
        var buses = new StringBuilder();
        for (int i = 0; i < AudioServer.BusCount; i++) buses.Append(AudioServer.GetBusName(i)).Append(':').Append(F(AudioServer.GetBusVolumeDb(i))).Append(AudioServer.IsBusMute(i) ? "M" : "").Append(AudioServer.GetBusEffectCount(i) > 0 ? "E" + AudioServer.GetBusEffectCount(i) : "").Append(',');
        kv.Add(("AudioBuses", buses.ToString().TrimEnd(',')));
        kv.Add(("live.ProcessMode", ProcessMode)); kv.Add(("live.Visible", Visible));
        SpikeLog("statics", kv.ToArray());
    }

    void SpikeSnap(string phase, int cycle)
    {
        var (rss, hwm) = SpikeMem();
        int players = 0;
        void Walk(Node n) { if (n is AudioStreamPlayer or AudioStreamPlayer3D) players++; foreach (var c in n.GetChildren()) Walk(c); }
        Walk(GetTree().Root);
        SpikeLog("snap", ("phase", phase), ("cycle", cycle), ("rss_mb", rss), ("hwm_mb", hwm), ("gc_heap_mb", GC.GetTotalMemory(false) / 1048576.0),
            ("mon_mem_static_mb", Performance.GetMonitor(Performance.Monitor.MemoryStatic) / 1048576.0),
            ("obj", (long)Performance.GetMonitor(Performance.Monitor.ObjectCount)), ("nodes", (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount)),
            ("orphans", (long)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount)), ("res", (long)Performance.GetMonitor(Performance.Monitor.ObjectResourceCount)),
            ("vram_mb", Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / 1048576.0), ("tex_mb", Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed) / 1048576.0),
            ("buf_mb", Performance.GetMonitor(Performance.Monitor.RenderBufferMemUsed) / 1048576.0),
            ("phys_objs", (long)Performance.GetMonitor(Performance.Monitor.Physics3DActiveObjects)),
            ("threads", SpikeCount("/proc/self/task")), ("fds", SpikeCount("/proc/self/fd")), ("players", players), ("buses", AudioServer.BusCount),
            ("nodeadded_conns", GetTree().GetSignalConnectionList(SceneTree.SignalName.NodeAdded).Count),
            ("joy_conns", Input.Singleton.GetSignalConnectionList(Input.SignalName.JoyConnectionChanged).Count),
            ("dn_unlit", SpikeField(_dayNight, "_unlit") is ICollection u ? u.Count : -1), ("dn_scenery", SpikeField(_dayNight, "_sceneryLights") is ICollection sl ? sl.Count : -1),
            ("load", SpikeLoad()));
    }

    async Task SpikeFrames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }

    async Task ViewerSpikeRun(string mode)
    {
        try
        {
            var opts = mode.Split(',');
            bool control = opts.Contains("control"), noUnsub = opts.Contains("nounsub"), purge = opts.Contains("purge");
            string cross = opts.FirstOrDefault(o => o.StartsWith("cross="))?["cross=".Length..];
            string liveTrack = _trackId, nestedTrack = cross ?? liveTrack;
            int cycles = int.Parse(opts.FirstOrDefault(o => o.StartsWith("cycles="))?["cycles=".Length..] ?? "10");
            const int openFrames = 120;
            SpikeLog("start", ("live_track", liveTrack), ("nested_track", nestedTrack), ("mode", mode), ("display", DisplayServer.GetName()),
                ("renderer", RenderingServer.GetCurrentRenderingDriverName()), ("live_build_ms", (_spikeReadyUs - _spikeEnterUs) / 1000.0), ("live_nodes", SpikeNodes(this)),
                ("uptime_ms", Time.GetTicksMsec()), ("data_files", SpikeCount(_records.Dir)), ("peak_reset_ok", SpikeResetPeak()), ("load", SpikeLoad()));
            await SpikeFrames(30);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); await SpikeFrames(2);
            SpikeSnap("baseline", 0); SpikeStaticsLog("baseline", 0);
            SpikeMallocTrim(0); SpikeSnap("baseline_trim", 0);
            for (int c = 1; c <= cycles; c++)
            {
                SpikeResetPeak();
                if (control) { await SpikeFrames(openFrames); SpikeSnap("open", c); await SpikeFrames(5); SpikeSnap("closed", c); }
                else
                {
                    var layer = (CanvasLayer)_hud.GetParent();
                    layer.Visible = false; ProcessMode = ProcessModeEnum.Disabled; Visible = false;
                    long t0 = Stopwatch.GetTimestamp();
                    var vp = new SubViewport
                    {
                        Size = (Vector2I)GetViewport().GetVisibleRect().Size, OwnWorld3D = true, HandleInputLocally = false, AudioListenerEnable3D = true,
                        RenderTargetUpdateMode = SubViewport.UpdateMode.Always, CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Nearest,
                    };
                    ApplyAntialiasing(vp);
                    GetParent().AddChild(vp);
                    var img = new TextureRect { Texture = vp.GetTexture(), Size = vp.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
                    GetParent().AddChild(img);
                    img.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                    var nested = new RaceScene { InstructionTrack = nestedTrack, ViewerMode = true, _dataDir = _records.Dir, _noRecord = true, _padMode = _padMode };
                    long t1 = Stopwatch.GetTimestamp();
                    vp.AddChild(nested);                  // the whole world is built here
                    long t2 = Stopwatch.GetTimestamp();
                    await SpikeFrames(1); long t3 = Stopwatch.GetTimestamp();
                    await SpikeFrames(1); long t4 = Stopwatch.GetTimestamp();
                    double Ms(long a, long b) => (b - a) * 1000.0 / Stopwatch.Frequency;
                    SpikeLog("build", ("cycle", c), ("track", nestedTrack), ("build_ms", Ms(t1, t2)), ("ready_signal_ms", (nested._spikeReadyUs - nested._spikeEnterUs) / 1000.0),
                        ("frame1_ms", Ms(t2, t3)), ("frame2_ms", Ms(t3, t4)), ("total_to_frame2_ms", Ms(t0, t4)), ("nested_nodes", SpikeNodes(nested)), ("load", SpikeLoad()));
                    await SpikeFrames(openFrames - 2);
                    SpikeSnap("open", c);
                    if (c == 1) SpikeStaticsLog("open", c);
                    if (!noUnsub) Input.Singleton.JoyConnectionChanged -= nested.OnJoyConnection;
                    vp.QueueFree(); img.QueueFree();
                    int waited = 0;
                    while (GodotObject.IsInstanceValid(nested) && waited < 20) { await SpikeFrames(1); waited++; }
                    await SpikeFrames(2);
                    SpikeLog("free", ("cycle", c), ("frames_until_nested_freed", waited), ("nested_valid_after", GodotObject.IsInstanceValid(nested)));
                    ProcessMode = ProcessModeEnum.Inherit; Visible = true; layer.Visible = true;
                    SpikeSnap("closed", c);
                    if (c == 1 || c == cycles) SpikeStaticsLog("closed", c);
                }
                if (purge) { (SpikeField(_dayNight, "_unlit") as IDictionary)?.Clear(); (SpikeField(_dayNight, "_sceneryLights") as IList)?.Clear(); }      // diagnostic: what if the live DayNight did not keep them
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); await SpikeFrames(2);
                SpikeSnap("closed_gc", c);
                SpikeMallocTrim(0); SpikeSnap("closed_trim", c);
                await SpikeFrames(2);
            }
            SpikeLog("end", ("data_files", SpikeCount(_records.Dir)), ("files", string.Join(",", Directory.GetFileSystemEntries(_records.Dir).Select(p => Path.GetFileName(p) + ":" + (File.Exists(p) ? new FileInfo(p).Length : -1)).OrderBy(x => x))));
        }
        catch (Exception e) { GD.Print("VSPIKE|exception|" + e.GetType().Name + ": " + e.Message.Replace('\n', ' ')); }
        CCCPRacing.Audio.AudioMix.Quit(this);
    }
}
