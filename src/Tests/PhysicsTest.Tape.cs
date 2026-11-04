using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

// World tape tests (docs/replay-tape.md section 10), written against WorldTape.Types.cs and TapeSeams only.

class TapeSnd { public float Speed, Doze; }

class TapeSpinSrc : ITapeSpinner { public Vector3 Axis => Vector3.Up; public float Angle { get; set; } }

/// <summary>A fake actor tree of plain Node3D nodes with scripted motion. Nothing here knows how the tape works.</summary>
class TapeWorld : IDisposable
{
    public readonly List<(string Key, Node3D Root)> Actors = new();
    public readonly List<List<Node3D>> Parts = new();          // per actor, [0] = root
    public readonly List<TapeSnd> Snds = new();
    public readonly Dictionary<int, (string Actor, Node3D Node, float T0)> Live = new();
    public IWorldTapeIndex Index;
    public TapeSpinSrc Spin;                                    // BuildSpin only
    public Node3D Rotor, Wheel;

    public static string Key(int a) => $"Test:A{a}";

    public static TapeWorld Build(int actors, int parts, bool sound = true)
    {
        var w = new TapeWorld();
        for (int a = 0; a < actors; a++)
        {
            var root = new Node3D { Name = $"A{a}" };
            TapeSeams.MarkPart(root, "root"); root.SetMeta(WorldTapeConst.VersionMeta, 1);
            var body = new Node3D { Name = "Body" }; root.AddChild(body);
            var ps = new List<Node3D> { root };
            for (int j = 1; j <= parts; j++)
            {
                var n = new Node3D { Name = $"P{j}", Position = Rest(j) };
                (j % 2 == 0 ? root : body).AddChild(n);
                TapeSeams.MarkPart(n, $"p{j}");
                ps.Add(n);
            }
            w.Actors.Add((Key(a), root)); w.Parts.Add(ps);
            if (sound)
            {
                var s = new TapeSnd(); w.Snds.Add(s);
                TapeSeams.RegisterSound(root, TapeSoundChannel.DieselSpeed, () => s.Speed, v => s.Speed = v);
                TapeSeams.RegisterSound(root, TapeSoundChannel.DieselDozing, () => s.Doze, v => s.Doze = v);
            }
        }
        w.Index = TapeSeams.NewIndex(w.Actors);
        return w;
    }

    /// <summary>One actor with a registered spinner (the rotor, spinning about Up) and optionally an unregistered turning wheel.</summary>
    public static TapeWorld BuildSpin(bool registered, bool wheel)
    {
        var w = new TapeWorld();
        var root = new Node3D { Name = "S" }; TapeSeams.MarkPart(root, "root");
        w.Rotor = new Node3D { Name = "Rotor" }; root.AddChild(w.Rotor); TapeSeams.MarkPart(w.Rotor, "rotor");
        w.Spin = new TapeSpinSrc();
        if (registered) TapeSeams.RegisterSpinner(w.Rotor, w.Spin);
        if (wheel) { w.Wheel = new Node3D { Name = "Wheel" }; root.AddChild(w.Wheel); TapeSeams.MarkPart(w.Wheel, "wheel"); }
        w.Actors.Add(("Test:S", root)); w.Parts.Add(new List<Node3D> { root, w.Rotor });
        w.Index = TapeSeams.NewIndex(w.Actors);
        return w;
    }

    static Vector3 Rest(int j) => new(0.3f * j, 0.5f, 0.1f * j);
    public Node3D Root(string key) => Actors.First(a => a.Key == key).Root;

    public static Node3D MakeInstanceNode(string kind)
    {
        var n = new Node3D { Name = kind }; TapeSeams.MarkPart(n, "root"); n.SetMeta(WorldTapeConst.KindMeta, kind);
        var wh = new Node3D { Name = "Wheel" }; n.AddChild(wh); TapeSeams.MarkPart(wh, "wheel");
        return n;
    }

    public static Vector3 InstancePos(int inst, float t0, float t) { float u = t - t0; return new(inst * 100f + 5f * u, 2f + MathF.Sin(u), 7f * MathF.Sin(0.8f * u)); }

    public void SpawnInstance(string actorKey, int inst, string kind, float t0)
    {
        var n = MakeInstanceNode(kind); Root(actorKey).AddChild(n);
        Live[inst] = (actorKey, n, t0); Index.AddInstance(actorKey, inst, n);
    }

    public void DespawnInstance(int inst)
    {
        var (_, n, _) = Live[inst]; Live.Remove(inst); Index.RemoveInstance(inst); n.GetParent().RemoveChild(n); n.Free();
    }

    public static float SpinAngleAt(float t)    // radians, unwrapped
    {
        (double a, double b, double r0, double r1)[] seg = { (0, 4, 0, 6), (4, 8, 6, 0), (8, 10, 0, 0), (10, 12, 0, 20), (12, 15, 20, 20), (15, 17, 20, 0) };
        double turns = 0;
        foreach (var (a, b, r0, r1) in seg)
        {
            if (t >= b) { turns += (r0 + r1) / 2 * (b - a); continue; }
            if (t > a) { double r = r0 + (r1 - r0) * (t - a) / (b - a); turns += (r0 + r) / 2 * (t - a); }
            break;
        }
        return (float)(turns * 2 * Math.PI);
    }

    public void Tick(float t)
    {
        if (Rotor != null)
        {
            Spin.Angle = SpinAngleAt(t); Rotor.Transform = new Transform3D(new Basis(new Quaternion(Vector3.Up, Spin.Angle)), Vector3.Zero);
            if (Wheel != null) Wheel.Transform = new Transform3D(new Basis(new Quaternion(Vector3.Right, 2 * MathF.PI * 3 * t)), Vector3.Zero);
            return;
        }
        for (int a = 0; a < Actors.Count; a++)
        {
            Actors[a].Root.Transform = new Transform3D(new Basis(new Quaternion(Vector3.Up, 0.4f * MathF.Sin(0.5f * t + a))), new Vector3(a * 30 + 6 * t * (1 + 0.1f * a), 0.3f * MathF.Sin(t), a * 4));
            var ps = Parts[a];
            for (int j = 1; j < ps.Count; j++)
            {
                int win = (int)MathF.Floor(t / 2f); float tp = (win + j) % 3 == 0 ? win * 2f : t;       // holds still in some windows
                ps[j].Transform = new Transform3D(new Basis(new Quaternion(new Vector3(1, j % 3, 1).Normalized(), 0.18f * MathF.Sin(tp + j))),
                    Rest(j) + new Vector3(0.4f * MathF.Sin(tp * (1 + 0.3f * j)), 0.2f * MathF.Cos(tp), 0));
                ps[j].Visible = (int)(t * 0.7f + j) % 4 != 3;
            }
            if (a < Snds.Count) { Snds[a].Speed = 20 + 15 * MathF.Sin(0.6f * t + a); Snds[a].Doze = 0.5f + 0.5f * MathF.Sin(0.3f * t + a); }
        }
        foreach (var kv in Live)
        {
            var (_, n, t0) = kv.Value;
            n.Position = InstancePos(kv.Key, t0, t);
            n.GetChild<Node3D>(0).Basis = new Basis(new Quaternion(Vector3.Right, 0.3f * (t - t0)));
        }
    }

    /// <summary>The busy-world script: about 6 % of the parts change at sample k.</summary>
    public void TickBig(int k)
    {
        int g = 0;
        foreach (var ps in Parts)
            foreach (var n in ps)
            {
                if ((g + k) % 16 == 0) n.Transform = new Transform3D(new Basis(new Quaternion(Vector3.Up, 0.1f * MathF.Sin(k * 0.3f + g))), Rest(g % 90) + new Vector3(0.5f * MathF.Sin(k * 0.2f + g), 0, 0.1f * k));
                g++;
            }
    }

    public void Dispose() { foreach (var (_, r) in Actors) if (GodotObject.IsInstanceValid(r)) r.Free(); }
}

class TapeFx { public TapeEvent E; public float Age, Remaining; }

/// <summary>The game side: spawns models, remembers effects, lets real time pass (Tick).</summary>
class TapeSink : IWorldTapeSink
{
    public TapeWorld World;
    public readonly Dictionary<int, Node3D> Inst = new();
    public readonly List<(TapeEvent E, float Age)> Delivered = new();
    public readonly List<TapeFx> Live = new();
    public int Spawns;

    public Node3D Spawn(string kind, string parentActorKey, int instance)
    {
        var n = TapeWorld.MakeInstanceNode(kind); World.Root(parentActorKey).AddChild(n); Inst[instance] = n; Spawns++; return n;
    }
    public void Despawn(int instance) { if (Inst.Remove(instance, out var n)) { n.GetParent()?.RemoveChild(n); n.Free(); } }
    public void Effect(in TapeEvent e, float age)
    {
        Delivered.Add((e, age));
        if (e.Type == TapeEventType.Effect && e.Effect == TapeEffect.BreakableBroken) { if (!Broken.Contains((int)e.Param)) Broken.Add((int)e.Param); return; }      // (a breakable is a state, not an effect with a life)
        Live.Add(new TapeFx { E = e, Age = age, Remaining = e.Life - age }); Live.RemoveAll(f => f.Remaining < 1e-3f);
    }
    public void ClearEffects() => Live.Clear();
    public List<int> Broken = new();
    public int BrokenSets;
    public void SetBrokenBreakables(IReadOnlyCollection<int> ids) { Broken = ids.ToList(); BrokenSets++; }
    public void Tick(float dt) { foreach (var f in Live) { f.Age += dt; f.Remaining -= dt; } Live.RemoveAll(f => f.Remaining < 1e-3f); }
}

class TapeSnap
{
    public SortedDictionary<string, (Vector3 P, Quaternion Q, bool V)> Poses = new(StringComparer.Ordinal);
    public SortedDictionary<string, byte> Snd = new(StringComparer.Ordinal);
    public List<int> Inst = new();
    public List<(float Time, TapeEventType Type, TapeEffect Eff, float Age)> Fx = new();
}

class TapeStop : Exception { }

class TapeInjectOps : ITapeFileOps
{
    readonly ITapeFileOps _real = TapeSeams.RealOps();
    public Action<string> OnStep;
    public string StopAt;
    public int ThrowOnReplaceNo;
    public bool ReplaceUnavailable;
    public int Replaces, Moves;
    public readonly List<string> Warnings = new();
    public void Step(string name) { OnStep?.Invoke(name); if (name == StopAt) throw new TapeStop(); }
    public void Replace(string tmp, string final)
    {
        Replaces++; OnStep?.Invoke("replace");
        if (ReplaceUnavailable) throw new NotSupportedException("File.Replace unavailable (injected)");
        if (Replaces == ThrowOnReplaceNo) throw new IOException("stop in replacement (injected)");
        _real.Replace(tmp, final); OnStep?.Invoke("replaced");
    }
    public void Move(string tmp, string final) { Moves++; OnStep?.Invoke("move"); _real.Move(tmp, final); OnStep?.Invoke("moved"); }
    public void Warn(string message) => Warnings.Add(message);
}

/// <summary>Watches a final path: it must hold the old or the new bytes at every observation, never nothing, never a part.</summary>
class TapeFinalProbe
{
    readonly string _path; readonly byte[] _old, _new; readonly bool _allowMissing;
    public int Seen, Missing, Bad;
    volatile bool _run; Thread _th;
    public TapeFinalProbe(string path, byte[] old, byte[] @new) { _path = path; _old = old; _new = @new; _allowMissing = old == null; }
    public void Observe()
    {
        byte[] b = null; bool missing = false, err = false;
        try { b = File.ReadAllBytes(_path); } catch (FileNotFoundException) { missing = true; } catch (DirectoryNotFoundException) { missing = true; } catch (IOException) { err = true; }
        lock (this)
        {
            Seen++;
            if (missing) { if (!_allowMissing) Missing++; }
            else if (err || !((_old != null && b.AsSpan().SequenceEqual(_old)) || b.AsSpan().SequenceEqual(_new))) Bad++;
        }
    }
    public void StartPolling() { _run = true; _th = new Thread(() => { while (_run) { Observe(); Thread.Yield(); } }); _th.Start(); }
    public void StopPolling() { _run = false; _th?.Join(); }
}

public partial class PhysicsTest
{
    bool _tapeMute;
    readonly List<string> _tapeFails = new();

    void TapeCheck(bool ok, string what)
    {
        if (!_tapeMute) { Check(ok, what); return; }
        if (!ok) { _failures++; _tapeFails.Add(what); }
    }
    void TapeInfo(string s) { if (!_tapeMute) GD.Print("  [info] " + s); }
    void Skip(string what) { if (!_tapeMute) GD.Print("  [skip] " + what); }
    void TapeRun(string name, Action a)
    {
        var before = TapeLeakCensus ? Node.GetOrphanNodeIds().ToHashSet() : null;
        try { a(); }
        catch (Exception ex) { TapeCheck(false, $"{name} threw {ex.GetType().Name}: {ex.Message}"); }
        if (before != null && !_tapeMute)
        {
            var left = Node.GetOrphanNodeIds().Where(id => !before.Contains(id)).Select(id => GodotObject.InstanceFromId((ulong)id) as Node).Where(n => n != null).ToList();
            if (left.Count > 0 || Node.GetOrphanNodeIds().Count != before.Count) GD.Print($"  [leak] {name} (orphan ids {before.Count} -> {Node.GetOrphanNodeIds().Count}): {left.Count} node(s) left without a parent after the test: {string.Join(", ", left.Take(8).Select(n => n.Name + (n.GetChildCount() > 0 ? $"(+{n.GetChildCount()} children)" : "")))}");
        }
    }
    static bool TapeLeakCensus => Array.IndexOf(OS.GetCmdlineUserArgs(), "--tape-leaks") >= 0;      // diagnostic: which test leaves nodes behind

    // ---- helpers ----

    static string TapeTmpDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "cccp-tape-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d); return d;
    }
    static void TapeRmDir(string d) { try { if (d.Contains("cccp-tape-tests-") && Directory.Exists(d)) Directory.Delete(d, true); } catch { } }

    static Ghost TapeGhost(int n, float shift = 0f, int tweakAt = -1)
    {
        var car = NewCar(new FlatGround()); var g = new Ghost();
        for (int i = 0; i < n; i++)
        {
            var p = new Vector3(i * 0.5f + shift, 0.8f, MathF.Sin(i * 0.1f) * 3);
            if (i == tweakAt) p.X = MathF.BitIncrement(p.X);
            car.Pos = p; car.Rot = new Basis(new Quaternion(Vector3.Up, i * 0.01f));
            g.Record((i + 0.5f) / Ghost.SampleRate, car, 0, 0, 0, -1);
        }
        return g.CopyWithTime(n / Ghost.SampleRate);
    }

    static readonly CarParams TapeParams = new();
    static Ghost TapeLoadGhost(string path) => Ghost.Load(path, TapeParams, null);
    static byte[] TapeGhostBytes(Ghost g, string dir, string name) { var p = Path.Combine(dir, name); g.Save(p); return File.ReadAllBytes(p); }

    /// <summary>Drives a scripted span at 60 Hz: world script, scripted actions (spawns, events), recorder. Returns the tape.</summary>
    static WorldTape TapeRecord(TapeWorld w, float seconds, List<(float T, Action<IWorldTapeRecorder> Do)> script = null, ulong fp = 1UL, Action<int, float> afterTick = null, IWorldTapeRecorder rec = null)
    {
        rec ??= TapeSeams.NewRecorder();
        w.Tick(0f); rec.Begin(w.Index);
        script = (script ?? new()).OrderBy(s => s.T).ToList(); int next = 0;
        int ticks = (int)MathF.Round(seconds * 60f);
        for (int n = 0; n <= ticks; n++)
        {
            float t = n / 60f;
            while (next < script.Count && script[next].T <= t) script[next++].Do(rec);
            w.Tick(t); rec.Record(t); afterTick?.Invoke(n, t);
        }
        return rec.Finish(seconds, fp);
    }

    static (float, Action<IWorldTapeRecorder>) TapeFx_(float t, string src, TapeEffect fx, float life, TapeEventType type = TapeEventType.Effect, Vector3 pos = default, Vector3 vel = default, float g = 0f, TapeProjectile pr = default)
        => (t, r => r.Event(new TapeEvent(t, type, Source: src, Effect: fx, Projectile: pr, Pos: pos, Velocity: vel, Gravity: g, Life: life)));
    static (float, Action<IWorldTapeRecorder>) TapeSpawn(TapeWorld w, float t, string actor, int inst, string kind)
        => (t, r => { w.SpawnInstance(actor, inst, kind, t); r.Event(new TapeEvent(t, TapeEventType.Spawn, Source: actor, Kind: kind, Parent: actor, Instance: inst)); });
    static (float, Action<IWorldTapeRecorder>) TapeDespawn(TapeWorld w, float t, string actor, int inst)
        => (t, r => { w.DespawnInstance(inst); r.Event(new TapeEvent(t, TapeEventType.Despawn, Source: actor, Instance: inst)); });

    /// <summary>A fresh player on a fresh copy of the world (same shape as the recorded one).</summary>
    static (TapeWorld W, TapeSink S, IWorldTapePlayer P) TapePlay(WorldTape tape, int actors, int parts, bool sound = true, Action<TapeWorld> mod = null)
    {
        var w = TapeWorld.Build(actors, parts, sound); mod?.Invoke(w); w.Index = TapeSeams.NewIndex(w.Actors);
        var s = new TapeSink { World = w };
        return (w, s, TapeSeams.NewPlayer(tape, w.Index, s));
    }

    static TapeSnap TapeTake(TapeWorld w, TapeSink s)
    {
        var sn = new TapeSnap();
        for (int i = 0; i < w.Index.Count; i++)
        {
            var n = w.Index.NodeOf(i); sn.Poses[w.Index.IdOf(i).ToString()] = (n.Position, n.Basis.GetRotationQuaternion(), n.Visible);
        }
        for (int a = 0; a < w.Snds.Count; a++)
        {
            sn.Snd[$"{a}:speed"] = TapeSoundRange.ToByte(TapeSoundChannel.DieselSpeed, w.Snds[a].Speed);
            sn.Snd[$"{a}:doze"] = TapeSoundRange.ToByte(TapeSoundChannel.DieselDozing, w.Snds[a].Doze);
        }
        sn.Inst = s.Inst.Keys.OrderBy(k => k).ToList();
        sn.Fx = s.Live.Select(f => (f.E.Time, f.E.Type, f.E.Effect, f.Age)).OrderBy(f => f.Time).ThenBy(f => f.Type).ToList();
        return sn;
    }

    static string TapeDiff(TapeSnap a, TapeSnap b, Func<string, bool> poseFilter = null)
    {
        foreach (var (k, p) in a.Poses)
        {
            if (poseFilter != null && !poseFilter(k)) continue;
            if (!b.Poses.TryGetValue(k, out var q)) return $"part {k} missing";
            if (p.P.DistanceTo(q.P) > 1e-3f || p.Q.AngleTo(q.Q) > 0.1f * MathF.PI / 180f) return $"pose of {k} differs by {p.P.DistanceTo(q.P):F4} m";
            if (p.V != q.V) return $"visibility of {k} differs";
        }
        if (poseFilter == null && a.Poses.Count != b.Poses.Count) return $"part count {a.Poses.Count} vs {b.Poses.Count}";
        if (poseFilter == null)
        {
            foreach (var (k, v) in a.Snd) if (!b.Snd.TryGetValue(k, out var u) || u != v) return $"sound {k} {v} vs {(b.Snd.TryGetValue(k, out var uu) ? uu.ToString() : "none")}";
            if (!a.Inst.SequenceEqual(b.Inst)) return $"instances [{string.Join(",", a.Inst)}] vs [{string.Join(",", b.Inst)}]";
            if (a.Fx.Count != b.Fx.Count) return $"live effects {a.Fx.Count} vs {b.Fx.Count}";
            for (int i = 0; i < a.Fx.Count; i++)
                if (a.Fx[i].Time != b.Fx[i].Time || a.Fx[i].Type != b.Fx[i].Type || MathF.Abs(a.Fx[i].Age - b.Fx[i].Age) > 0.02f) return $"effect {i} @{a.Fx[i].Time}: age {a.Fx[i].Age:F3} vs {b.Fx[i].Age:F3}";
        }
        return null;
    }

    // ---- entry points ----

    void TapeTests()
    {
        GD.Print("World tape tests");
        InstallRealTape();
        if (TapeSeams.ReferenceInUse) GD.Print("tape: reference model in use (real implementation not wired)");
        TapeRun("tape 1", TapeT1); TapeRun("tape 2", TapeT2); TapeRun("tape 3", TapeT3); TapeRun("tape 4", TapeT4); TapeRun("tape 5", TapeT5);
        TapeRun("tape 6", TapeT6); TapeRun("tape 7", TapeT7); TapeRun("tape 8-10", TapeT8to10); TapeRun("tape 11", TapeT11); TapeRun("tape 12", TapeT12);
        TapeRun("tape 13", TapeT13); TapeRun("tape 14a", TapeT14a); TapeRun("tape 14b", TapeT14b); TapeRun("tape 15", TapeT15); TapeRun("tape 16", TapeT16);
        TapeRun("tape 17", TapeT17); TapeRun("tape 18", TapeT18); TapeRun("tape 18b", TapeT18b); TapeRun("tape 19", TapeT19); TapeRun("tape 20", TapeT20); TapeRun("tape 20b", TapeT20b); TapeRun("tape 21", TapeT21); TapeRun("tape 22", TapeT22); TapeRun("tape 23", TapeT23); TapeRun("tape 24", TapeT24); TapeRun("tape 25", TapeT25);
        TapeRun("tape replay details", ReplayDetailTests);
        TapeRun("tape GAI gaze", GaiGazeTests);
        TapeRun("tape fuel depot", FuelTapeTests);
        TapeRun("tape rescue rope", RescueRopeTapeTests);
        if (TapeLeakCensus) Node.PrintOrphanNodes();
        if (TapeLeakCensus) GD.Print($"  [leak] at the end: orphan node monitor {Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount)}, nodes {Performance.GetMonitor(Performance.Monitor.ObjectNodeCount)}, objects {Performance.GetMonitor(Performance.Monitor.ObjectCount)}, orphan ids {Node.GetOrphanNodeIds().Count}");
        GD.Print(_failures == 0 ? "World tape tests: all passed" : $"World tape tests: {_failures} FAILED");
    }

    // ---- 1: poses and visibility survive record, encode, decode ----
    void TapeT1()
    {
        using var w = TapeWorld.Build(4, 7);
        var expected = new List<Dictionary<string, (Vector3 P, Quaternion Q, bool V)>>();
        var rec = TapeSeams.NewRecorder();
        var tape = TapeRecord(w, 12f, rec: rec, afterTick: (n, t) =>
        {
            if (n % 4 != 0) return;
            var d = new Dictionary<string, (Vector3, Quaternion, bool)>();
            for (int i = 0; i < w.Index.Count; i++) { var nd = w.Index.NodeOf(i); d[w.Index.IdOf(i).ToString()] = (nd.Position, nd.Basis.GetRotationQuaternion(), nd.Visible); }
            expected.Add(d);
        });
        TapeCheck(TapeSeams.UnregisteredSpinCount(rec) == 0, "tape 1: the safety-net counter is zero for the phase-1 style actors");
        var decoded = TapeSeams.DecodeTape(TapeSeams.EncodeTape(tape));
        TapeCheck(decoded != null && decoded.SampleCount == expected.Count && decoded.GhostFingerprint == tape.GhostFingerprint, $"tape 1: encode and decode keep the sample count ({expected.Count}) and fingerprint");
        if (decoded == null) return;
        var (w2, s2, p) = TapePlay(decoded, 4, 7); using var _ = w2;
        double maxPos = 0, maxRot = 0; int visBad = 0, hiddenSeen = 0, shownSeen = 0, missing = 0;
        for (int k = 0; k < expected.Count; k++)
        {
            p.Apply(k / WorldTapeConst.TapeRate, 1f / 60f);
            for (int i = 0; i < w2.Index.Count; i++)
            {
                var n = w2.Index.NodeOf(i);
                if (!expected[k].TryGetValue(w2.Index.IdOf(i).ToString(), out var e)) { missing++; continue; }
                maxPos = Math.Max(maxPos, n.Position.DistanceTo(e.P));
                maxRot = Math.Max(maxRot, n.Basis.GetRotationQuaternion().AngleTo(e.Q) * 180 / Math.PI);
                if (n.Visible != e.V) visBad++;
                if (e.V) shownSeen++; else hiddenSeen++;
            }
        }
        TapeCheck(missing == 0 && maxPos <= 0.001, $"tape 1: positions within 1 mm (worst {maxPos * 1000:F3} mm)");
        TapeCheck(maxRot <= 0.1, $"tape 1: rotations within 0.1 degree (worst {maxRot:F4})");
        TapeCheck(visBad == 0 && hiddenSeen > 0 && shownSeen > 0, $"tape 1: visibility flags exact, parts both hidden ({hiddenSeen}) and shown ({shownSeen})");
    }

    // ---- 2: registered spinners ----
    void TapeT2()
    {
        using var w = TapeWorld.BuildSpin(true, false);
        var rec = TapeSeams.NewRecorder();
        var tape = TapeRecord(w, 17f, rec: rec);
        TapeCheck(TapeSeams.UnregisteredSpinCount(rec) == 0, "tape 2: a registered spinner does not trip the safety net");
        var decoded = TapeSeams.DecodeTape(TapeSeams.EncodeTape(tape));
        var (w2, s2, p) = TapePlay2(decoded); using var _ = w2;
        double worstSample = 0, worstPlateau = 0; float prevWrapped = 0, unwrapped = 0, prevUnw = 0; int reversals = 0;
        for (int n = 0; n <= 17 * 60; n++)
        {
            float t = n / 60f; p.Apply(t, 1f / 60f);
            float a = SpinOf(w2.Rotor), truth = TapeWorld.SpinAngleAt(t);
            float dw = Mathf.Wrap(a - prevWrapped, -MathF.PI, MathF.PI); if (n == 0) { dw = 0; unwrapped = a; }
            unwrapped += dw; prevWrapped = a;
            if (unwrapped < prevUnw - 0.5f * MathF.PI / 180f) reversals++;
            prevUnw = Math.Max(prevUnw, unwrapped);
            double err = Math.Abs(Mathf.Wrap(a - truth, -MathF.PI, MathF.PI)) * 180 / Math.PI;
            if (n % 4 == 0) worstSample = Math.Max(worstSample, err);
            if (t >= 12.5f && t <= 14.5f) worstPlateau = Math.Max(worstPlateau, err);
        }
        TapeCheck(worstSample <= 0.5, $"tape 2: decoded angle equals the published angle at every sample within 0.5 degree (worst {worstSample:F3})");
        TapeCheck(reversals == 0, "tape 2: the decoded angle never reverses (0 to 6 to 0 turns/s, then 20 turns/s above the sample grid's Nyquist rate)");
        TapeCheck(worstPlateau <= 0.5, $"tape 2: between samples on a 20 turns/s plateau the angle follows the published one (worst {worstPlateau:F2} degree)");

        using var wu = TapeWorld.BuildSpin(false, true);
        var rec2 = TapeSeams.NewRecorder(); TapeRecord(wu, 4f, rec: rec2);
        TapeCheck(TapeSeams.UnregisteredSpinCount(rec2) > 0, "tape 2: an unregistered part turning more than 25 degrees per sample makes the safety-net counter non-zero");
    }
    static float SpinOf(Node3D n) { var q = n.Basis.GetRotationQuaternion(); return 2f * MathF.Atan2(q.Y, q.W); }
    (TapeWorld, TapeSink, IWorldTapePlayer) TapePlay2(WorldTape tape)
    {
        var w = TapeWorld.BuildSpin(true, false); var s = new TapeSink { World = w };
        return (w, s, TapeSeams.NewPlayer(tape, w.Index, s));
    }

    // ---- 3: seek equals sequential apply ----
    static List<(float, Action<IWorldTapeRecorder>)> TapeBusyScript(TapeWorld w) => new()
    {
        TapeSpawn(w, 2f, TapeWorld.Key(1), 1, "missile"), TapeDespawn(w, 9f, TapeWorld.Key(1), 1),
        TapeSpawn(w, 6f, TapeWorld.Key(2), 2, "hind"), TapeDespawn(w, 21.5f, TapeWorld.Key(2), 2),
        TapeSpawn(w, 17.25f, TapeWorld.Key(0), 3, "missile"), TapeDespawn(w, 29f, TapeWorld.Key(0), 3),
        TapeFx_(4f, TapeWorld.Key(0), TapeEffect.Smoke, 6f), TapeFx_(12.25f, TapeWorld.Key(1), TapeEffect.Explosion, 3f), TapeFx_(20f, TapeWorld.Key(2), TapeEffect.Smoke, 8.5f),
        TapeFx_(8f, TapeWorld.Key(3), TapeEffect.MuzzleBlast, 0f),
        TapeFx_(14f, TapeWorld.Key(0), TapeEffect.ShellImpact, 4f, TapeEventType.Projectile, new Vector3(0, 2, 0), new Vector3(40, 15, 0), 9.81f, TapeProjectile.Shell),
    };

    void TapeT3()
    {
        WorldTape tape; using (var w = TapeWorld.Build(4, 7)) tape = TapeRecord(w, 30f, TapeBusyScript(w));
        var rnd = new Random(7);
        var ns = new List<int> { 0, 1800 }; for (int i = 0; i < 48; i++) ns.Add(rnd.Next(1, 1800)); ns.Sort();
        var seq = new Dictionary<int, TapeSnap>(); string bad = null; int checkedN = 0, instSeen = 0, fxSeen = 0;
        var (ws, ss, ps) = TapePlay(tape, 4, 7); using var _a = ws;
        int cur = 0;
        foreach (int n in ns)
        {
            while (cur <= n) { ss.Tick(1f / 60f); ps.Apply(cur / 60f, 1f / 60f); cur++; }
            seq[n] = TapeTake(ws, ss);
            var (wf, sf, pf) = TapePlay(tape, 4, 7); using var _f = wf;
            pf.Seek(n / 60f); var fresh = TapeTake(wf, sf);
            bad ??= TapeDiff(seq[n], fresh) is string d ? $"at {n / 60f:F2} s: {d}" : null; checkedN++;
            if (seq[n].Inst.Count > 0) instSeen++; if (seq[n].Fx.Count > 0) fxSeen++;
        }
        TapeCheck(bad == null, $"tape 3: seek equals sequential apply on a fresh player at {checkedN} times (poses, sound bytes, instances, effects with ages){(bad == null ? "" : ": " + bad)}");
        TapeCheck(instSeen >= 5 && fxSeen >= 5, $"tape 3: the sampled times include live instances ({instSeen}) and live effects ({fxSeen})");
        var (wp, sp, pp) = TapePlay(tape, 4, 7); using var _p = wp; string bad2 = null;
        foreach (int n in ns.OrderBy(_ => rnd.Next()))
        {
            pp.Seek(n / 60f);
            bad2 ??= TapeDiff(seq[n], TapeTake(wp, sp)) is string d ? $"at {n / 60f:F2} s: {d}" : null;
        }
        TapeCheck(bad2 == null, $"tape 3: one player seeking in random order equals the sequential state{(bad2 == null ? "" : ": " + bad2)}");
        string bad3 = null;
        foreach (int n in new[] { 130, 560, 905, 1250, 1500 })
        {
            var (w1, s1, p1) = TapePlay(tape, 4, 7); using var _1 = w1; var (w2, s2, p2) = TapePlay(tape, 4, 7); using var _2 = w2;
            for (int i = 0; i <= n; i++) { s1.Tick(1f / 60f); p1.Apply(i / 60f, 1f / 60f); }
            p2.Seek(n / 60f);
            for (int i = n + 1; i <= n + 40; i++) { s1.Tick(1f / 60f); p1.Apply(i / 60f, 1f / 60f); s2.Tick(1f / 60f); p2.Apply(i / 60f, 1f / 60f); }
            bad3 ??= TapeDiff(TapeTake(w1, s1), TapeTake(w2, s2)) is string d ? $"after {n / 60f:F2} s: {d}" : null;
        }
        TapeCheck(bad3 == null, $"tape 3: after a seek the playback continues like the sequential one{(bad3 == null ? "" : ": " + bad3)}");
    }

    // ---- 4: effects restored by age ----
    void TapeT4()
    {
        WorldTape tape;
        using (var w = TapeWorld.Build(2, 3, false))
            tape = TapeRecord(w, 40f, new() { TapeFx_(12f, TapeWorld.Key(0), TapeEffect.Smoke, 6f), TapeFx_(20f, TapeWorld.Key(0), TapeEffect.MuzzleBlast, 0f), TapeFx_(30f, TapeWorld.Key(0), TapeEffect.Smoke, 4f), TapeFx_(36f, TapeWorld.Key(1), TapeEffect.Smoke, 8f) });
        bool Smoke12((TapeEvent E, float Age) d) => d.E.Time == 12f && d.E.Effect == TapeEffect.Smoke;

        var (w1, s1, p1) = TapePlay(tape, 2, 3, false); using var _1 = w1;
        p1.Seek(15f);
        var got = s1.Delivered.Where(Smoke12).ToList();
        TapeCheck(got.Count == 1 && MathF.Abs(got[0].Age - 3f) < 1e-3f && s1.Live.Count == 1, $"tape 4: a seek into the middle of a smoke effect gives the sink the effect once with age = clock - time ({(got.Count == 1 ? got[0].Age.ToString("F3") : got.Count + " deliveries")})");
        var (w2, s2, p2) = TapePlay(tape, 2, 3, false); using var _2 = w2;
        p2.Seek(19f);
        TapeCheck(s2.Delivered.Count == 0 && s2.Live.Count == 0, "tape 4: a seek past the end of an effect delivers nothing for it");
        var (w3, s3, p3) = TapePlay(tape, 2, 3, false); using var _3 = w3;
        p3.Apply(0f, 1f / 60f); p3.Apply(3f, 1f / 60f); p3.Apply(15f, 1f / 60f);
        var got3 = s3.Delivered.Where(Smoke12).ToList();
        TapeCheck(got3.Count == 1 && MathF.Abs(got3[0].Age - 3f) < 1e-3f, "tape 4: a forward jump of more than 2 s is a seek (the effect arrives once, with its age)");

        var (w4, s4, p4) = TapePlay(tape, 2, 3, false); using var _4 = w4;
        int strays = 0, live14 = 0, once = 0; bool at39 = true;
        for (int loop = 0; loop < 10; loop++)
            for (int n = 0; n <= 40 * 60; n++)
            {
                float t = n / 60f; s4.Tick(1f / 60f); p4.Apply(t, 1f / 60f);
                if (n == 0 && s4.Live.Count != 0) strays += s4.Live.Count;
                if (n == 14 * 60 && s4.Live.Count(f => f.E.Time == 12f) == 1 && s4.Live.Count == 1) live14++;
                if (n == 39 * 60 && s4.Live.Count != 1) at39 = false;
            }
        once = s4.Delivered.Count(d => Smoke12(d) && d.Age < 0.05f);
        p4.Apply(0f, 1f / 60f);
        TapeCheck(strays == 0 && s4.Live.Count == 0, $"tape 4: zero stray effects after 10 loops (strays at wrap {strays}, live at the end {s4.Live.Count})");
        TapeCheck(live14 == 10 && at39, $"tape 4: in every one of 10 passes the smoke is live once at 14 s ({live14}/10) and the late one at 39 s");
        TapeCheck(once == 10, $"tape 4: the smoke is fired once per pass ({once} firings in 10 passes)");
    }

    // ---- 5: an incompatible actor drops out, the rest replay ----
    void TapeT5()
    {
        string A0 = TapeWorld.Key(0), A1 = TapeWorld.Key(1), A2 = TapeWorld.Key(2);
        WorldTape tape;
        using (var w = TapeWorld.Build(3, 5))
            tape = TapeRecord(w, 20f, new()
            {
                TapeFx_(5f, A0, TapeEffect.Smoke, 4f), TapeFx_(6f, A1, TapeEffect.Explosion, 4f), TapeFx_(7f, A2, TapeEffect.Smoke, 4f),
                TapeSpawn(w, 8f, A1, 1, "hind"), TapeDespawn(w, 15f, A1, 1),
            });
        var mods = new (string Name, Action<TapeWorld> Mod)[]
        {
            ("part list", w => w.Parts[1][3].RemoveMeta(WorldTapeConst.PartMeta)),
            ("version", w => w.Actors[1].Root.SetMeta(WorldTapeConst.VersionMeta, 2)),
            ("rest scale", w => w.Parts[1][2].Scale = new Vector3(1.01f, 1f, 1f)),
            ("rest offset", w => w.Parts[1][2].Position += new Vector3(0.01f, 0, 0)),
            ("parent", w => { var n = w.Parts[1][2]; var body = w.Actors[1].Root.GetNode<Node3D>("Body"); n.GetParent().RemoveChild(n); body.AddChild(n); }),
        };
        TapeSnap control = null; List<(TapeEvent E, float Age)> controlFx = null;
        foreach (var (name, mod) in new (string, Action<TapeWorld>)[] { ("control", null) }.Concat(mods))
        {
            var (w, s, p) = TapePlay(tape, 3, 5, true, mod); using var _ = w;
            bool isControl = mod == null;
            TapeCheck(p.Covers(A1) == isControl && p.Covers(A0) && p.Covers(A2), $"tape 5 ({name}): coverage is {(isControl ? "complete" : "all but the changed actor")}");
            foreach (var n in w.Parts[1]) n.Position = new Vector3(123, 0, 0);
            w.Snds[1].Speed = 7.7f;
            bool ok = true;
            try
            {
                for (int i = 0; i <= 16 * 30; i++) { s.Tick(1f / 30f); p.Apply(i / 30f, 1f / 30f); }
                p.Seek(10f); p.Seek(3f); for (int i = 0; i <= 14 * 30; i++) { s.Tick(1f / 30f); p.Apply(3f + i / 30f, 1f / 30f); }
            }
            catch (Exception ex) { ok = false; TapeCheck(false, $"tape 5 ({name}): threw {ex.GetType().Name}: {ex.Message}"); }
            if (!ok) continue;
            var snap = TapeTake(w, s);
            if (isControl) { control = snap; controlFx = s.Delivered.ToList(); TapeCheck(s.Spawns > 0 && s.Delivered.Any(d => d.E.Source == A1), "tape 5 (control): all actors' events and instances arrive"); continue; }
            bool sentinel = w.Parts[1].Skip(1).All(n => n.Position.X == 123f) && w.Snds[1].Speed == 7.7f;
            TapeCheck(sentinel, $"tape 5 ({name}): the changed actor's poses and sounds are not written (it ticks live)");
            TapeCheck(!s.Delivered.Any(d => d.E.Source == A1) && s.Spawns == 0 && !s.Inst.ContainsKey(1), $"tape 5 ({name}): the changed actor's events and instances are dropped");
            string diff = TapeDiff(control, snap, k => !k.StartsWith(A1 + "/") && !k.Contains(":A1/"));
            TapeCheck(diff == null && s.Delivered.Count(d => d.E.Source == A0) > 0 && s.Delivered.Count(d => d.E.Source == A2) > 0, $"tape 5 ({name}): all other actors replay as in the control run{(diff == null ? "" : ": " + diff)}");
        }
    }

    // ---- 6: the pairing fingerprint ----
    void TapeT6()
    {
        var dir = TapeTmpDir();
        try
        {
            const int n = 95;
            Ghost a = TapeGhost(n), b = TapeGhost(n, 5f), c = TapeGhost(n, 0f, n - 1), d = TapeGhost(n, 0f, 33);
            WorldTape tape; using (var w = TapeWorld.Build(1, 2, false)) tape = TapeRecord(w, 2f, fp: TapeSeams.Fingerprint(a));
            string gp = Path.Combine(dir, "ghost_t.bin"), tp = Path.Combine(dir, "tape_t.bin");
            TapeCheck(TapeSeams.SavePair(gp, tp, a, tape, TapeSeams.RealOps()).Whole, "tape 6: the pair saves");
            TapeCheck(TapeSeams.LoadTape(tp, a) != null, "tape 6: the tape loads with the ghost it was recorded against");
            TapeCheck(TapeSeams.LoadTape(tp, TapeLoadGhost(gp)) != null, "tape 6: the tape loads with that ghost read back from its file");
            TapeCheck(TapeSeams.LoadTape(tp, b) == null, "tape 6: a different ghost drops the tape");
            TapeCheck(TapeSeams.LoadTape(tp, c) == null, "tape 6: a ghost whose last sample differs by one float drops the tape");
            TapeCheck(TapeSeams.LoadTape(tp, d) == null, "tape 6: a ghost with a differing sample in the middle drops the tape");
        }
        finally { TapeRmDir(dir); }
    }

    // ---- 7 and 11: need the real game ----
    void TapeT7()
    {
        if (!TapeSeams.RecorderIsReal || System.Environment.GetEnvironmentVariable("TAPE_TRACE_BASELINE") == null)
        { Skip("tape 7: replay of a ghost without a tape equals today's car trace (needs real implementation; set TAPE_TRACE_BASELINE to a world-trace.sh directory made before the feature)"); return; }
        string outDir = TapeTmpDir();
        try { TapeCheck(TapeTrace.Run(outDir) == 0 && TapeTrace.Compare(System.Environment.GetEnvironmentVariable("TAPE_TRACE_BASELINE"), outDir) == 0, "tape 7: title-* traces equal the baseline"); }
        finally { TapeRmDir(outDir); }
    }

    void TapeT11()
    {
        // A ~16 run Godot sweep (world-trace.sh, up to 6 in parallel, minutes long): never part of the ordinary run. TAPE_TRACE=1 asks for it, and
        // the recording switch (CCCP_TAPE=on|off, read by the RaceScene hook of the integration step) must exist for it to mean anything.
        if (System.Environment.GetEnvironmentVariable("TAPE_TRACE") != "1") { Skip("tape 11: world-trace.sh traces identical with recording on and off (a long sweep: run with TAPE_TRACE=1 once the RaceScene hook exists)"); return; }
        string on = TapeTmpDir(), off = TapeTmpDir();
        try
        {
            System.Environment.SetEnvironmentVariable("CCCP_TAPE", "on"); int a = TapeTrace.Run(on);
            System.Environment.SetEnvironmentVariable("CCCP_TAPE", "off"); int b = TapeTrace.Run(off);
            TapeCheck(a == 0 && b == 0 && TapeTrace.Compare(off, on) == 0, "tape 11: all 16 traces identical with recording on and off");
        }
        finally { System.Environment.SetEnvironmentVariable("CCCP_TAPE", null); TapeRmDir(on); TapeRmDir(off); }
    }

    // ---- 8, 9, 10: allocation, time, size on a busy world ----
    void TapeT8to10()
    {
        using var w = TapeWorld.Build(11, 91, false);
        TapeCheck(w.Index.Count >= 1000, $"tape 8-10: the fake index has the busiest track's node count ({w.Index.Count})");
        var rec = TapeSeams.NewRecorder(); w.TickBig(0); rec.Begin(w.Index);
        var ms = new List<double>(); long alloc = 0;
        for (int n = 0; n <= 70 * 60; n++)
        {
            float t = n / 60f; bool sample = n % 4 == 0;
            if (sample) w.TickBig(n / 4);
            long a0 = GC.GetAllocatedBytesForCurrentThread(); long t0 = Stopwatch.GetTimestamp();
            rec.Record(t);
            long t1 = Stopwatch.GetTimestamp(); long a1 = GC.GetAllocatedBytesForCurrentThread();
            if (n >= 60 && n < 61 * 60) alloc += a1 - a0;
            if (sample && n > 0) ms.Add((t1 - t0) * 1000.0 / Stopwatch.Frequency);
        }
        ms.Sort(); double avg = ms.Average(), p99 = ms[(int)(ms.Count * 0.99)];
        var tape = rec.Finish(70f, 1UL); int size = TapeSeams.EncodeTape(tape).Length;
        bool enforce = TapeSeams.RecorderIsReal;
        void Perf(bool ok, string text) { if (enforce) TapeCheck(ok, text); else TapeInfo($"{text} -> {(ok ? "would pass" : "would FAIL")} (reference model: not enforced)"); }
        Perf(alloc <= 4096, $"tape 8: the recorder allocates at most 4 KB over 60 s after warm-up ({alloc} bytes)");
        Perf(avg <= 1.0 && p99 <= 3.0, $"tape 9: per sample avg {avg:F3} ms, p99 {p99:F3} ms (provisional budget 0.5 / 1.5 ms, hard fail at 2x)");
        if (avg > 0.5 || p99 > 1.5) TapeInfo($"tape 9: above the provisional budget (avg {avg:F3} ms, p99 {p99:F3} ms) but within 2x or not enforced");
        Perf(size <= 2 * 1024 * 1024, $"tape 10: a 70 s lap with about 6 % changed parts is {size / 1024} KB (limit 2048 KB)");
    }

    // ---- 12: ballistic shell and guided missile ----
    void TapeT12()
    {
        string A0 = TapeWorld.Key(0), A1 = TapeWorld.Key(1);
        Vector3 p0 = new(0, 2, 0), v0 = new(40, 15, 0); const float g = 9.81f;
        WorldTape tape;
        using (var w = TapeWorld.Build(2, 3, false))
            tape = TapeRecord(w, 30f, new() { TapeFx_(5f, A0, TapeEffect.ShellImpact, 4f, TapeEventType.Projectile, p0, v0, g, TapeProjectile.Shell), TapeSpawn(w, 10f, A1, 5, "missile"), TapeDespawn(w, 18f, A1, 5) });
        Vector3 Truth(float tau) => p0 + v0 * tau + new Vector3(0, -0.5f * g * tau * tau, 0);
        float worst = 0; int seen = 0;
        var (w1, s1, p1) = TapePlay(tape, 2, 3, false); using var _1 = w1;
        var taus = new[] { 0.2f, 0.6f, 1.0f, 1.4f, 1.8f, 2.2f, 2.6f, 3.0f, 3.4f, 3.8f };
        int cur = 0;
        foreach (float tau in taus)
        {
            int target = (int)MathF.Round((5f + tau) * 60f);
            while (cur <= target) { s1.Tick(1f / 60f); p1.Apply(cur / 60f, 1f / 60f); cur++; }
            var f = s1.Live.FirstOrDefault(x => x.E.Type == TapeEventType.Projectile);
            if (f == null) continue; seen++;
            var pos = f.E.Pos + f.E.Velocity * f.Age + new Vector3(0, -0.5f * f.E.Gravity * f.Age * f.Age, 0);
            worst = MathF.Max(worst, pos.DistanceTo(Truth(target / 60f - 5f)));
        }
        TapeCheck(seen == 10 && worst <= 0.05f, $"tape 12: the shell's position matches the recorded path within 5 cm at 10 sample times (seen {seen}, worst {worst * 100:F2} cm)");
        for (int i = cur; i <= 9.6f * 60; i++) { s1.Tick(1f / 60f); p1.Apply(i / 60f, 1f / 60f); }
        TapeCheck(!s1.Live.Any(x => x.E.Type == TapeEventType.Projectile), "tape 12: the shell is gone after its life");
        var (w2, s2, p2) = TapePlay(tape, 2, 3, false); using var _2 = w2;
        p2.Seek(7.0f);
        var fl = s2.Live.FirstOrDefault(x => x.E.Type == TapeEventType.Projectile);
        TapeCheck(fl != null && MathF.Abs(fl.Age - 2f) < 1e-3f && (fl.E.Pos + fl.E.Velocity * fl.Age + new Vector3(0, -0.5f * fl.E.Gravity * fl.Age * fl.Age, 0)).DistanceTo(Truth(2f)) <= 0.05f, "tape 12: a shell seeked into mid-flight is where the path says it is");

        float worstM = 0; int seenM = 0; var (w3, s3, p3) = TapePlay(tape, 2, 3, false); using var _3 = w3; cur = 0;
        for (int i = 0; i < 10; i++)
        {
            float clock = 10.5f + 0.75f * i; int target = (int)MathF.Round(clock * 60f);
            while (cur <= target) { s3.Tick(1f / 60f); p3.Apply(cur / 60f, 1f / 60f); cur++; }
            if (s3.Inst.TryGetValue(5, out var node)) { seenM++; worstM = MathF.Max(worstM, node.Position.DistanceTo(TapeWorld.InstancePos(5, 10f, target / 60f))); }
        }
        TapeCheck(seenM == 10 && worstM <= 0.05f, $"tape 12: a guided missile replays from its sampled poses within 5 cm at 10 times (seen {seenM}, worst {worstM * 100:F2} cm)");
        TapeCheck(!s3.Inst.ContainsKey(5) || cur < 18 * 60, "tape 12: the missile is part of the replay only while it lives");
        var (w4, s4, p4) = TapePlay(tape, 2, 3, false); using var _4 = w4;
        p4.Seek(15f);
        TapeCheck(s4.Inst.TryGetValue(5, out var n4) && n4.Position.DistanceTo(TapeWorld.InstancePos(5, 10f, 15f)) <= 0.05f, "tape 12: a seek into the missile's flight restores it on its path");
        p4.Seek(20f);
        TapeCheck(!s4.Inst.ContainsKey(5), "tape 12: a seek past the impact leaves no missile");
    }

    // ---- 13: fingerprint is stable across Save and Load ----
    void TapeT13()
    {
        var dir = TapeTmpDir();
        try
        {
            var g = TapeGhost(120); ulong before = TapeSeams.Fingerprint(g);
            var p = Path.Combine(dir, "ghost_f.bin"); g.Save(p);
            var back = TapeLoadGhost(p);
            TapeCheck(back != null && TapeSeams.Fingerprint(back) == before, "tape 13: the fingerprint of a saved and loaded ghost equals the one taken before saving");
            TapeCheck(TapeSeams.Fingerprint(TapeGhost(120)) == before, "tape 13: the same lap fingerprints the same");
            TapeCheck(TapeSeams.Fingerprint(TapeGhost(120, 0f, 119)) != before && TapeSeams.Fingerprint(TapeGhost(119)) != before, "tape 13: one float or one sample more or less changes it");
        }
        finally { TapeRmDir(dir); }
    }

    // ---- 14a, 14b, 16: files ----
    class PairFx
    {
        public string Dir, Gp, Tp; public byte[] OldG, OldT, NewG, NewT; public Ghost G0, G1; public WorldTape T0, T1;
    }

    PairFx TapePair(bool existing)
    {
        var f = new PairFx { Dir = TapeTmpDir() };
        f.Gp = Path.Combine(f.Dir, "ghost_t.bin"); f.Tp = Path.Combine(f.Dir, "tape_t.bin");
        f.G0 = TapeGhost(60); f.G1 = TapeGhost(70, 2f);
        using (var w = TapeWorld.Build(1, 2, false)) { f.T0 = TapeRecord(w, 1f, fp: TapeSeams.Fingerprint(f.G0)); }
        using (var w = TapeWorld.Build(1, 2, false)) { f.T1 = TapeRecord(w, 1.5f, fp: TapeSeams.Fingerprint(f.G1)); }
        f.NewG = TapeGhostBytes(f.G1, f.Dir, "side_new.bin"); File.Delete(Path.Combine(f.Dir, "side_new.bin")); f.NewT = TapeSeams.EncodeTape(f.T1);
        if (existing) { f.OldG = TapeGhostBytes(f.G0, f.Dir, "ghost_t.bin"); f.OldT = TapeSeams.EncodeTape(f.T0); File.WriteAllBytes(f.Tp, f.OldT); }
        return f;
    }

    static bool SameFile(string path, byte[] bytes) => File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes);
    static string[] Tmps(string dir) => Directory.GetFiles(dir, "*.tmp");

    void TapeT14a()
    {
        var cases = new (string Name, string StopAt, int ThrowReplace, bool ghostNew, bool pairNew)[]
        {
            ("no stop", null, 0, true, true), ("stop after the tmp files", "tmp-written", 0, false, false),
            ("stop between the two replacements", "ghost-replaced", 0, true, false), ("stop in the second replacement", null, 2, true, false),
        };
        foreach (var (name, stop, thr, ghostNew, pairNew) in cases)
        {
            var f = TapePair(true);
            try
            {
                var ops = new TapeInjectOps { StopAt = stop, ThrowOnReplaceNo = thr };
                var pg = new TapeFinalProbe(f.Gp, f.OldG, f.NewG); var pt = new TapeFinalProbe(f.Tp, f.OldT, f.NewT);
                ops.OnStep = _ => { pg.Observe(); pt.Observe(); };
                pg.StartPolling(); pt.StartPolling();
                TapePairResult res = default; Exception thrown = null;
                try { res = TapeSeams.SavePair(f.Gp, f.Tp, f.G1, f.T1, ops); } catch (Exception ex) { thrown = ex; }
                bool ok = res.Whole;
                pg.StopPolling(); pt.StopPolling(); pg.Observe(); pt.Observe();
                TapeCheck(thrown == null, $"tape 14a ({name}): the save does not leak the injected stop{(thrown == null ? "" : " (" + thrown.GetType().Name + ")")}");
                TapeCheck(pg.Missing == 0 && pg.Bad == 0 && pt.Missing == 0 && pt.Bad == 0 && pg.Seen > 3, $"tape 14a ({name}): the final paths hold a complete old or new file at every observation ({pg.Seen} on the ghost: missing {pg.Missing}, truncated or foreign {pg.Bad}; tape missing {pt.Missing}, bad {pt.Bad})");
                TapeCheck(SameFile(f.Gp, ghostNew ? f.NewG : f.OldG) && SameFile(f.Tp, pairNew ? f.NewT : f.OldT), $"tape 14a ({name}): the files end as {(ghostNew ? "new" : "old")} ghost and {(pairNew ? "new" : "old")} tape");
                var gnow = TapeLoadGhost(f.Gp);
                bool tapeOk = TapeSeams.LoadTape(f.Tp, gnow) != null;
                TapeCheck(tapeOk == (ghostNew == pairNew), ghostNew && !pairNew ? $"tape 14a ({name}): a new ghost with an old tape drops the tape by the fingerprint" : $"tape 14a ({name}): the pair loads together");
                TapeCheck(res.GhostSaved == ghostNew && res.TapeSaved == pairNew, $"tape 14a ({name}): the result tells what was saved ({res}): a new ghost with a failed tape is a saved record, not a refused save");
                TapeCheck(Tmps(f.Dir).Length == 0 && ok == pairNew, $"tape 14a ({name}): no .tmp remains after a clean {(pairNew ? "save" : "failure")} and the result is {ok}");
            }
            finally { TapeRmDir(f.Dir); }
        }
    }

    void TapeT14b()
    {
        var f = TapePair(true);
        try
        {
            TapeCheck(TapeSeams.SavePair(f.Gp, f.Tp, f.G1, f.T1, TapeSeams.RealOps()).Whole, "tape 14b: setup, a valid pair is on disk");
            var gb = File.ReadAllBytes(f.Gp); var tb = File.ReadAllBytes(f.Tp);
            File.WriteAllBytes(f.Gp + ".tmp", new byte[] { 1, 2, 3 }); File.WriteAllBytes(f.Tp + ".tmp", new byte[] { 9 }); File.WriteAllBytes(Path.Combine(f.Dir, "ghost_other.bin.tmp"), new byte[] { 7, 7 });
            File.WriteAllText(Path.Combine(f.Dir, "leaderboard_t.cfg"), "keep");
            int removed = TapeSeams.CleanStale(f.Dir);
            TapeCheck(Tmps(f.Dir).Length == 0 && removed == 3, $"tape 14b: the stale .tmp files are deleted after a forced kill ({removed} removed)");
            TapeCheck(!File.Exists(Path.Combine(f.Dir, "ghost_other.bin")) && Directory.GetFiles(f.Dir).Length == 3, "tape 14b: nothing is restored or renamed");
            TapeCheck(File.ReadAllBytes(f.Gp).AsSpan().SequenceEqual(gb) && File.ReadAllBytes(f.Tp).AsSpan().SequenceEqual(tb) && File.ReadAllText(Path.Combine(f.Dir, "leaderboard_t.cfg")) == "keep", "tape 14b: the valid ghost, tape and other records are untouched");
            var g = TapeLoadGhost(f.Gp); TapeCheck(g != null && TapeSeams.LoadTape(f.Tp, g) != null, "tape 14b: the ghost at the final path loads, and its tape with it");
        }
        finally { TapeRmDir(f.Dir); }
    }

    // ---- 15: sound quantisation ----
    void TapeT15()
    {
        bool direct = true; string why = "";
        foreach (var ch in Enum.GetValues<TapeSoundChannel>())
        {
            var (lo, hi) = TapeSoundRange.Of(ch); float step = (hi - lo) / 255f; float worst = 0;
            for (int i = 0; i <= 2000; i++) { float v = lo + (hi - lo) * i / 2000f; worst = MathF.Max(worst, MathF.Abs(TapeSoundRange.FromByte(ch, TapeSoundRange.ToByte(ch, v)) - v)); }
            bool clamp = TapeSoundRange.ToByte(ch, lo - 5f) == 0 && TapeSoundRange.ToByte(ch, hi + 5f) == 255 && TapeSoundRange.ToByte(ch, float.MaxValue) == 255 && TapeSoundRange.FromByte(ch, 255) == hi;
            if (worst > step / 2 + 1e-5f || !clamp) { direct = false; why += $" {ch}(err {worst:F5}, step {step:F5}, clamp {clamp})"; }
        }
        TapeCheck(direct, "tape 15: every channel quantises across its range with error <= 1/2 step and clamps out-of-range values" + why);

        var chans = Enum.GetValues<TapeSoundChannel>();
        var cells = new float[chans.Length];
        var root = new Node3D { Name = "Snd" }; TapeSeams.MarkPart(root, "root");
        for (int i = 0; i < chans.Length; i++) { int k = i; TapeSeams.RegisterSound(root, chans[i], () => cells[k], v => cells[k] = v); }
        float Truth(int i, float t) { var (lo, hi) = TapeSoundRange.Of(chans[i]); return lo - 0.2f * (hi - lo) + (hi - lo) * 1.4f * Math.Clamp(t / 10f, 0, 1); }
        void Drive(float t) { for (int i = 0; i < cells.Length; i++) cells[i] = Truth(i, t); }
        var actors = new List<(string, Node3D)> { ("Test:Snd", root) };
        var idx = TapeSeams.NewIndex(actors); var rec = TapeSeams.NewRecorder();
        Drive(0); rec.Begin(idx);
        for (int n = 0; n <= 600; n++) { Drive(n / 60f); rec.Record(n / 60f); }
        var tape = rec.Finish(10f, 1UL);
        var root2 = new Node3D { Name = "Snd" }; TapeSeams.MarkPart(root2, "root"); var cells2 = new float[chans.Length];
        for (int i = 0; i < chans.Length; i++) { int k = i; TapeSeams.RegisterSound(root2, chans[i], () => cells2[k], v => cells2[k] = v); }
        var idx2 = TapeSeams.NewIndex(new List<(string, Node3D)> { ("Test:Snd", root2) });
        var player = TapeSeams.NewPlayer(tape, idx2, new TapeSink { World = null });
        float worstChain = 0; bool clampedSeen = false;
        for (int m = 0; m <= 100; m++)
        {
            player.Apply(m / 10f, 0.1f);
            for (int i = 0; i < chans.Length; i++)
            {
                var (lo, hi) = TapeSoundRange.Of(chans[i]); float truth = Math.Clamp(Truth(i, m / 10f), lo, hi);
                worstChain = MathF.Max(worstChain, (MathF.Abs(cells2[i] - truth) - (hi - lo) / 510f) / (hi - lo));
                if (Truth(i, m / 10f) < lo && cells2[i] == lo) clampedSeen = true;
            }
        }
        TapeCheck(worstChain <= 1e-4f && clampedSeen, $"tape 15: through the recorder and player every channel decodes within 1/2 step and clamps (excess {worstChain:E1})");
        root.Free(); root2.Free();
    }

    // ---- 16: the replacement ----
    void TapeT16()
    {
        var f = TapePair(true);
        try
        {
            var ops = new TapeInjectOps();
            var pg = new TapeFinalProbe(f.Gp, f.OldG, f.NewG); var pt = new TapeFinalProbe(f.Tp, f.OldT, f.NewT); ops.OnStep = _ => { pg.Observe(); pt.Observe(); };
            pg.StartPolling(); bool ok = TapeSeams.SavePair(f.Gp, f.Tp, f.G1, f.T1, ops).Whole; pg.StopPolling();
            TapeCheck(ok && pg.Missing == 0 && pg.Bad == 0 && pt.Missing == 0 && pt.Bad == 0 && ops.Replaces == 2 && ops.Moves == 0, $"tape 16: with an existing target the final paths are valid at every instant ({pg.Seen} observations) and use File.Replace twice ({ops.Replaces}), no Move ({ops.Moves})");
            TapeCheck(SameFile(f.Gp, f.NewG) && SameFile(f.Tp, f.NewT) && Tmps(f.Dir).Length == 0, "tape 16: with an existing target the files end as the new pair, no .tmp left");
        }
        finally { TapeRmDir(f.Dir); }
        f = TapePair(false);
        try
        {
            var ops = new TapeInjectOps(); bool ok = TapeSeams.SavePair(f.Gp, f.Tp, f.G1, f.T1, ops).Whole;
            TapeCheck(ok && SameFile(f.Gp, f.NewG) && SameFile(f.Tp, f.NewT) && ops.Moves == 2 && ops.Replaces == 0 && Tmps(f.Dir).Length == 0, "tape 16: without a target the pair is created (File.Move)");
        }
        finally { TapeRmDir(f.Dir); }
        f = TapePair(false);
        try
        {
            var ops = new TapeInjectOps { ReplaceUnavailable = true }; bool ok = TapeSeams.SavePair(f.Gp, f.Tp, f.G1, f.T1, ops).Whole;
            TapeCheck(ok && SameFile(f.Gp, f.NewG) && SameFile(f.Tp, f.NewT), "tape 16: when File.Replace is unavailable a missing target is still created");
        }
        finally { TapeRmDir(f.Dir); }
        f = TapePair(true);
        try
        {
            var ops = new TapeInjectOps { ReplaceUnavailable = true };
            var pg = new TapeFinalProbe(f.Gp, f.OldG, f.NewG); ops.OnStep = _ => pg.Observe();
            bool ok = TapeSeams.SavePair(f.Gp, f.Tp, f.G1, f.T1, ops).Whole;
            TapeCheck(!ok && SameFile(f.Gp, f.OldG) && SameFile(f.Tp, f.OldT), "tape 16: when File.Replace throws the save is refused and the old pair is untouched");
            TapeCheck(ops.Warnings.Count == 1 && Tmps(f.Dir).Length == 0, $"tape 16: the refused save logs exactly one warning ({ops.Warnings.Count}) and removes its .tmp files");
            TapeCheck(pg.Missing == 0 && pg.Bad == 0, "tape 16: the ghost path was valid at every observation of the refused save");
        }
        finally { TapeRmDir(f.Dir); }
    }

    // ---- 17: independence from the game ----
    static readonly string[] TapeForbiddenTypeNames = { "RaceScene", "RecordStore", "Menu" };
    static readonly string[] TapeForbiddenMemberNames = { "Replaying", "IsRecordLap" };

    static bool TapeForbiddenType(Type t)
    {
        if (t == null) return false;
        if (t.HasElementType) return TapeForbiddenType(t.GetElementType());
        if (t.IsGenericType && t.GetGenericArguments().Any(TapeForbiddenType)) return true;
        return TapeForbiddenTypeNames.Any(n => t.Name.Contains(n));
    }

    /// <summary>Everything the chain's types reference that must not be there: field/method/parameter/local types, member names, and members called or loaded in the IL.</summary>
    static List<string> TapeReflect(IEnumerable<Type> roots)
    {
        var bad = new List<string>(); var seen = new HashSet<Type>(); var queue = new Queue<Type>(roots.Where(r => r != null));
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        while (queue.Count > 0)
        {
            var t = queue.Dequeue(); if (!seen.Add(t)) continue;
            foreach (var n in t.GetNestedTypes(All)) queue.Enqueue(n);
            if (TapeForbiddenType(t) || TapeForbiddenType(t.BaseType) || t.GetInterfaces().Any(TapeForbiddenType)) bad.Add($"{t.Name} is or derives from a game type");
            foreach (var m in t.GetMembers(All))
            {
                if (TapeForbiddenMemberNames.Any(n => m.Name.Contains(n))) bad.Add($"{t.Name}.{m.Name}: forbidden name");
                switch (m)
                {
                    case FieldInfo fi when TapeForbiddenType(fi.FieldType): bad.Add($"{t.Name}.{m.Name}: field of type {fi.FieldType.Name}"); break;
                    case PropertyInfo pi when TapeForbiddenType(pi.PropertyType): bad.Add($"{t.Name}.{m.Name}: property of type {pi.PropertyType.Name}"); break;
                    case MethodBase mb:
                        if (mb is MethodInfo mi && TapeForbiddenType(mi.ReturnType)) bad.Add($"{t.Name}.{m.Name}: returns {mi.ReturnType.Name}");
                        foreach (var p in mb.GetParameters()) if (TapeForbiddenType(p.ParameterType)) bad.Add($"{t.Name}.{m.Name}: parameter of type {p.ParameterType.Name}");
                        MethodBody body = null; try { body = mb.GetMethodBody(); } catch { }
                        if (body == null) break;
                        foreach (var lv in body.LocalVariables) if (TapeForbiddenType(lv.LocalType)) bad.Add($"{t.Name}.{m.Name}: local of type {lv.LocalType.Name}");
                        var il = body.GetILAsByteArray();
                        for (int i = 0; il != null && i + 4 < il.Length; i++)
                        {
                            if (!(il[i] is 0x28 or 0x6F or 0x73 or 0x7B or 0x7C or 0x7D or 0x7E or 0x7F or 0x80 or 0xD0 or 0x74)) continue;
                            int tok = BitConverter.ToInt32(il, i + 1); int table = tok >> 24;
                            if (table is not (0x01 or 0x02 or 0x04 or 0x06 or 0x0A or 0x2B)) continue;
                            try
                            {
                                var mem = t.Module.ResolveMember(tok, t.IsGenericType ? t.GetGenericArguments() : null, mb is MethodInfo { IsGenericMethod: true } gm ? gm.GetGenericArguments() : null);
                                if (mem == null) continue;
                                if (TapeForbiddenMemberNames.Any(n => mem.Name.Contains(n)) || TapeForbiddenType(mem.DeclaringType) || mem is Type tt && TapeForbiddenType(tt))
                                    bad.Add($"{t.Name}.{m.Name}: uses {mem.DeclaringType?.Name}.{mem.Name}");
                            }
                            catch { }
                        }
                        break;
                }
            }
        }
        return bad.Distinct().ToList();
    }

    static bool TapeInChain(Type t)
    {
        if (t == null) return false;
        while (t.DeclaringType != null) t = t.DeclaringType;
        return t.Namespace != "CCCPRacing.Tests" || t.Name.StartsWith("Ref") || t.Name.StartsWith("Mut") || t.Name == "TapeReference";
    }

    void TapeT17()
    {
        var dir = TapeTmpDir();
        try
        {
            foreach (bool isRecord in new[] { true, false })
            {
                string label = isRecord ? "a record-like span" : "a span that is not a record";
                var gp = Path.Combine(dir, isRecord ? "ghost_span.bin" : "free_span.ref"); var tp = Path.Combine(dir, isRecord ? "tape_span.bin" : "free_span.tape");
                var reference = TapeGhost(isRecord ? 120 : 40);                       // the first reference recording; a ghost, but any lap would do
                WorldTape tape; using (var w = TapeWorld.Build(3, 4)) tape = TapeRecord(w, 6f, new() { TapeSpawn(w, 1f, TapeWorld.Key(1), 1, "hind"), TapeDespawn(w, 4f, TapeWorld.Key(1), 1), TapeFx_(2f, TapeWorld.Key(0), TapeEffect.Smoke, 3f) }, TapeSeams.Fingerprint(reference));
                tape.LapTime = isRecord ? 6f : -1f;
                bool saved = TapeSeams.SavePair(gp, tp, reference, tape, TapeSeams.RealOps()).Whole;
                var loaded = saved ? TapeSeams.LoadTape(tp, reference) : null;
                TapeCheck(saved && loaded != null, $"tape 17: {label} is saved to an arbitrary path and loaded back, with no scene, menu, store or record time");
                if (loaded == null) continue;
                TapeCheck(loaded.SampleCount >= 6 * 15 - 1, $"tape 17: {label} recorded its samples ({loaded.SampleCount} of about {6 * 15}) without any scene, menu or record-lap flag");
                var (w1, s1, p1) = TapePlay(loaded, 3, 4); using var _1 = w1;
                for (int i = 0; i <= 3.5f * 60; i++) { s1.Tick(1f / 60f); p1.Apply(i / 60f, 1f / 60f); }
                var seq = TapeTake(w1, s1);
                var (w2, s2, p2) = TapePlay(loaded, 3, 4); using var _2 = w2; p2.Seek(3.5f);
                string d = TapeDiff(seq, TapeTake(w2, s2));
                TapeCheck(d == null && seq.Inst.Count == 1 && seq.Fx.Count == 1, $"tape 17: {label} plays: apply and seek agree with an instance and an effect alive{(d == null ? "" : ": " + d)}");
            }
            var types = new List<Type>
            {
                typeof(RefRecorder), typeof(RefPlayer), typeof(RefIndex), typeof(RefCodec), typeof(RefFiles), typeof(RefSaver), typeof(RefFileOps), typeof(TapeReference), typeof(RefReg), typeof(WorldTapeFingerprint),
            };
            foreach (var o in TapeSeams.Save()) if (o is Delegate dl) { types.Add(dl.Method.DeclaringType); if (dl.Target != null) types.Add(dl.Target.GetType()); }
            var rec = TapeSeams.NewRecorder(); using (var w = TapeWorld.Build(1, 1, false)) { types.Add(w.Index.GetType()); var tape = TapeRecord(w, 1f, rec: rec); types.Add(rec.GetType()); var played = TapePlay(tape, 1, 1, false); types.Add(played.P.GetType()); played.W.Dispose(); }
            types = types.Where(TapeInChain).Distinct().ToList();
            var offenders = TapeReflect(types);
            TapeCheck(offenders.Count == 0, $"tape 17: none of the chain's {types.Count} types references RaceScene, RecordStore, a menu type or 'Replaying'{(offenders.Count == 0 ? "" : ": " + string.Join("; ", offenders.Take(3)))}");
        }
        finally { TapeRmDir(dir); }
    }
}

/// <summary>Runs tools/trace/world-trace.sh (tests 7 and 11; only once the real recording exists).</summary>
static class TapeTrace
{
    public static int Run(string outDir) => Sh("tools/trace/world-trace.sh", outDir);
    public static int Compare(string oldDir, string newDir) => Sh("tools/trace/world-trace.sh", "--compare", oldDir, newDir);
    static int Sh(params string[] args)
    {
        var psi = new ProcessStartInfo("bash") { WorkingDirectory = ProjectSettings.GlobalizePath("res://"), UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi); p.WaitForExit(); return p.ExitCode;
    }
}
