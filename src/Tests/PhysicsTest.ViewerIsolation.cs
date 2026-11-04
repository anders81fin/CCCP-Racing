using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

// --viewer-isolation-tests: a nested viewer world (a second RaceScene in a SubViewport with its own World3D) must leave no trace in the live world
// (docs/player-replay-phase2-proposal.md, build/player-replay/phase2/report.md section 3). Each case is ONE headless child of this same test scene
// (--viewer-isolation-case=KIND:ARGS, own /tmp/cccp-h-iso-* data and XDG dirs) that builds a live world, opens and closes nested ones the way the
// viewer session does (live frozen, ViewerGlobals captured before and restored after the nested scene left the tree) and prints VISO| lines; the parent judges them.
// Both worlds are plain RaceScenes of a track (the school's InstructionTrack path, no lap needed) with their own _Process switched off, and their
// DayNight/Weather driven by hand, so the light is exactly what the case says. Not covered here: the real viewer's lap playback (the lifecycle tests).
// Options: --viewer-isolation-only=TRACK  --viewer-isolation-sig-out=FILE (the plain-run DayNight signatures, for comparing two builds).
public partial class PhysicsTest
{
    static readonly string[] IsoTracks = { "ilyich", "siberia", "atomgrad", "bonus" };
    const BindingFlags IsoAny = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    static string IsoDir(string label) => $"/tmp/cccp-h-iso-{label}";

    // live track, nested track, live light, nested light, restore run
    static readonly (string live, string nested, string liveLight, string nestedLight)[] IsoCycles =
    {
        ("ilyich", "ilyich", "day", "day"), ("ilyich", "ilyich", "night", "night"), ("ilyich", "ilyich", "day", "night"), ("ilyich", "ilyich", "night", "day"),
        ("siberia", "siberia", "night", "night"), ("atomgrad", "atomgrad", "night", "day"), ("atomgrad", "atomgrad", "day", "night"),
        ("ilyich", "atomgrad", "night", "night"), ("siberia", "bonus", "night", "day"), ("atomgrad", "ilyich", "night", "day"), ("bonus", "siberia", "day", "night"),
    };

    void ViewerIsolationTests()
    {
        GD.Print("Viewer isolation (a nested viewer world leaves no trace in the live one; one headless child per case)");
        if (!OperatingSystem.IsLinux()) { GD.Print("viewer isolation: Linux only"); return; }
        string only = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--viewer-isolation-only="))?["--viewer-isolation-only=".Length..];
        string sigOut = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--viewer-isolation-sig-out="))?["--viewer-isolation-sig-out=".Length..];
        var sigs = new StringBuilder();
        foreach (var t in IsoTracks.Where(t => only == null || t == only))
        {
            var lines = IsoChild("sig-" + t, "sig:" + t, 300);
            var s = lines.FirstOrDefault(l => l.StartsWith("VISO|sig|"));
            Check(s != null, $"isolation sig:{t}: the plain run printed its DayNight signature");
            if (s != null) { GD.Print(s); sigs.AppendLine(s); }
        }
        if (sigOut != null) File.WriteAllText(sigOut, sigs.ToString());
        if (sigOut != null && OS.GetCmdlineUserArgs().Contains("--viewer-isolation-sig-only")) return;

        foreach (var (live, nested, liveLight, nestedLight) in IsoCycles.Where(c => only == null || c.live == only || c.nested == only))
        {
            string name = $"{live}-{nested}-{liveLight}-{nestedLight}";
            var lines = IsoChild(name, $"cycle:{live}:{nested}:{liveLight}:{nestedLight}:1", 600);
            foreach (var l in lines.Where(l => l.StartsWith("VISO|"))) GD.Print(l);
            var res = lines.FirstOrDefault(l => l.StartsWith("VISO|result|"));
            Check(res != null && res.Contains("|diffs=0|"), $"isolation {name}: everything of the live world equals what it was before, after both open/close cycles and the restore ({res ?? "no result line"})");
            Check(!lines.Any(l => l.StartsWith("VISO|during|") && l.Contains("|bad=1")), $"isolation {name}: while the nested world was open the live lists, the nested lights and the boat binding were its own");
        }
        // Detection power: the same cross-track night case without the restore must show differences (so the equality above is not vacuous).
        {
            var lines = IsoChild("norestore", "cycle:ilyich:atomgrad:night:night:0", 600);
            var res = lines.FirstOrDefault(l => l.StartsWith("VISO|result|"));
            foreach (var l in lines.Where(l => l.StartsWith("VISO|diff|")).Take(12)) GD.Print(l);
            Check(res != null && !res.Contains("|diffs=0|"), $"isolation norestore (control): without the documented restore the same case differs ({res ?? "no result line"})");
        }
    }

    /// <summary>One child of this scene to its end (or killed at the timeout by its PID); returns its output lines.</summary>
    string[] IsoChild(string label, string caseArg, int timeoutSec)
    {
        string dir = IsoDir(label);
        foreach (var d in new[] { dir, dir + ".xdg" }) try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
        Directory.CreateDirectory(dir);
        string log = dir + ".log";
        var psi = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        foreach (var s in new[] { "-c", "exec \"$0\" \"$@\" > \"$ISO_OUT\" 2>&1", OS.GetExecutablePath(), "--headless", "--fixed-fps", "60", "--path", TapeProjectDir, "res://scenes/tests/PhysicsTest.tscn", "--" }) psi.ArgumentList.Add(s);
        foreach (var a in new[] { "--viewer-isolation-case=" + caseArg, "--no-intro", "--babushka-seed=1", "--rng-seed=1", "--no-record", "--data-dir=" + dir }) psi.ArgumentList.Add(a);
        psi.Environment["ISO_OUT"] = log;
        psi.Environment["XDG_DATA_HOME"] = dir + ".xdg";
        var sw = Stopwatch.StartNew();
        using var p = Process.Start(psi);
        while (!p.WaitForExit(100))
            if (sw.Elapsed.TotalSeconds > timeoutSec) { try { p.Kill(true); } catch { } p.WaitForExit(); GD.Print($"isolation {label}: TIMEOUT after {timeoutSec} s"); _failures++; break; }
        var lines = File.Exists(log) ? File.ReadAllLines(log) : Array.Empty<string>();
        GD.Print($"isolation {label}: exit {p.ExitCode}, {sw.Elapsed.TotalSeconds:F0} s, log {log}");
        if (p.ExitCode != 0) _failures++;
        return lines;
    }

    // ------------------------------------------------------------ child side

    static void IsoLog(string kind, string text) => GD.Print($"VISO|{kind}|{text}");

    async void ViewerIsolationCase(string spec)
    {
        try
        {
            var parts = spec.Split(':');
            switch (parts[0])
            {
                case "sig": await IsoSignature(parts[1]); break;
                case "cycle": await IsoCycle(parts[1], parts[2], parts[3] == "night", parts[4] == "night", parts[5] == "1"); break;
                default: throw new InvalidOperationException("unknown isolation case " + spec);
            }
        }
        catch (Exception e) { GD.PrintErr(e); _failures++; }
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    async Task IsoFrames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }

    /// <summary>A plain world of a track (the school's path, which OpenInstruction uses for its nested one), with its own _Process switched off after the build settled.</summary>
    async Task<RaceScene> IsoBuild(Node parent, string track)
    {
        var scene = new RaceScene();
        void Set(string field, object value) => typeof(RaceScene).GetField(field, IsoAny).SetValue(scene, value);
        Set("InstructionTrack", track); Set("_noRecord", true);
        Set("_dataDir", OS.GetCmdlineUserArgs().First(a => a.StartsWith("--data-dir="))["--data-dir=".Length..]);
        parent.AddChild(scene);
        await IsoFrames(20);
        scene.SetProcess(false);
        scene.SetPhysicsProcess(false);
        return scene;
    }

    static T IsoField<T>(object o, string name) => (T)o.GetType().GetField(name, IsoAny).GetValue(o);

    static string IsoSha(IEnumerable<string> lines)
    {
        var sb = new StringBuilder();
        foreach (var l in lines) sb.Append(l).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16];
    }

    /// <summary>What a DayNight collected, in order and without instance ids (those differ per process): colour and keep of every unshaded material, kind, energy and scene path of every scenery light.</summary>
    static (string unlit, string scenery, int nUnlit, int nScenery) IsoDayNightSignature(DayNight dn, Node scene)
    {
        var unlit = IsoField<System.Collections.IDictionary>(dn, "_unlit");
        var u = new List<string>();
        foreach (System.Collections.DictionaryEntry e in unlit)
        {
            var m = (StandardMaterial3D)e.Key;
            var tup = (System.Runtime.CompilerServices.ITuple)e.Value;
            Color c = (Color)tup[0];
            u.Add($"{c.R:F3},{c.G:F3},{c.B:F3},{c.A:F3}|{(float)tup[1]:F2}|{m.AlbedoTexture != null}");
        }
        var scenery = IsoField<System.Collections.IList>(dn, "_sceneryLights");
        var s = new List<string>();
        foreach (var e in scenery)
        {
            var t = (System.Runtime.CompilerServices.ITuple)e;
            var light = (Light3D)t[0];
            s.Add($"{light.GetType().Name}|{(float)t[1]:F2}|{scene.GetPathTo(light)}");
        }
        return (IsoSha(u), IsoSha(s), u.Count, s.Count);
    }

    async Task IsoSignature(string track)
    {
        var live = await IsoBuild(this, track);
        var dn = (DayNight)live.FindChild("DayNight", true, false);
        var (u, s, nu, ns) = IsoDayNightSignature(dn, live);
        IsoLog("sig", $"track={track}|unlit={nu}:{u}|scenery={ns}:{s}|glows={NightGlow.Glows.Count}|lamps={NightGlow.Lamps.Count}|radars={RadarHead.All.Count}");
        live.QueueFree();
    }

    // ------------------------------------------------------------ the open/close cycles

    /// <summary>Puts the world's light where the case says (laps 8 = full night, 1 = plain day) and ticks its weather to the same clock.</summary>
    static void IsoDrive(RaceScene scene, bool night)
    {
        float clock = night ? 8.3f : 1.1f;
        var dn = (DayNight)scene.FindChild("DayNight", true, false);
        dn.PinnedClock = clock;
        dn.Update(1f / 60f, true, 0, 0f, true, 0f);
        foreach (var w in IsoAll<Weather>(scene)) w.Tick(clock, 1f / 60f);
    }

    /// <summary>Every node of a C# type below a scene (FindChildren matches native types only).</summary>
    static List<T> IsoAll<T>(Node root) where T : Node
    {
        var found = new List<T>();
        void Walk(Node n) { if (n is T t) found.Add(t); foreach (var c in n.GetChildren()) Walk(c); }
        Walk(root);
        return found;
    }

    static string Ids(IEnumerable<GodotObject> os) => string.Join(",", os.Select(o => GodotObject.IsInstanceValid(o) ? o.GetInstanceId().ToString() : "dead"));

    /// <summary>Everything of the live world that a nested one could have touched, as strings to compare.</summary>
    Dictionary<string, string> IsoCapture(RaceScene live)
    {
        var dn = (DayNight)live.FindChild("DayNight", true, false);
        var d = new Dictionary<string, string>();
        var unlit = IsoField<System.Collections.IDictionary>(dn, "_unlit");
        d["unlit.ids"] = Ids(unlit.Keys.Cast<GodotObject>());
        d["unlit.colours"] = string.Join(",", unlit.Keys.Cast<StandardMaterial3D>().Select(m => $"{m.AlbedoColor.R:F4}/{m.AlbedoColor.G:F4}/{m.AlbedoColor.B:F4}/{m.AlbedoColor.A:F4}"));
        var scenery = IsoField<System.Collections.IList>(dn, "_sceneryLights");
        d["scenery.ids"] = Ids(scenery.Cast<System.Runtime.CompilerServices.ITuple>().Select(t => (GodotObject)t[0]));
        d["scenery.state"] = string.Join(",", scenery.Cast<System.Runtime.CompilerServices.ITuple>().Select(t => (Light3D)t[0]).Select(l => GodotObject.IsInstanceValid(l) ? $"{l.Visible}/{l.LightEnergy:F3}" : "dead"));
        d["NightGlow.Level"] = NightGlow.Level.ToString("F4", CultureInfo.InvariantCulture);
        d["dn.Glow"] = dn.Glow.ToString("F4", CultureInfo.InvariantCulture);
        d["NightGlow.Glows"] = Ids(NightGlow.Glows.Select(g => (GodotObject)g.mat));
        d["glow.emission"] = string.Join(",", NightGlow.Glows.Select(g => g.mat.EmissionEnergyMultiplier.ToString("F3", CultureInfo.InvariantCulture)));
        d["NightGlow.Lamps"] = Ids(NightGlow.Lamps);
        d["lamp.scale"] = string.Join(",", NightGlow.Lamps.Where(GodotObject.IsInstanceValid).Select(l => l.Scale.X.ToString("F3", CultureInfo.InvariantCulture)));
        d["Surfaces.Wetness"] = Surfaces.Wetness.ToString("F4", CultureInfo.InvariantCulture);
        d["RadarHead.Heads(live)"] = Ids(RadarHead.InWorld(WorldScope.Of(live)));
        d["NormPlan"] = $"{string.Join(",", NormPlan.Ladder)}|{string.Join(",", NormPlan.BaseLadder)}|{NormPlan.Factor}|{NormPlan.BronzeAt}|{NormPlan.SilverAt}";
        d["RaceScene._trackId"] = (string)typeof(RaceScene).GetField("_trackId", IsoAny).GetValue(null);
        d["DachaPatrol.boat"] = string.Join(",", IsoAll<DachaPatrol>(live).Select(p => IsoField<PartyRowboat>(p, "_boat")?.GetInstanceId().ToString() ?? "null"));
        d["NodeAdded.connections"] = GetTree().GetSignalConnectionList(SceneTree.SignalName.NodeAdded).Count.ToString();
        var gulag = typeof(GulagTruck).GetField("_onRoad", IsoAny).GetValue(null) as GulagTruck;
        d["GulagTruck._onRoad"] = gulag == null ? "null" : gulag.GetInstanceId().ToString();
        // every shared Props material's recorded day colour is the colour it was made with (the key)
        var mats = (System.Collections.IDictionary)typeof(Props).GetField("Materials", IsoAny).GetValue(null);
        int badDay = 0, recorded = 0;
        foreach (System.Collections.DictionaryEntry e in mats)
            if (((StandardMaterial3D)e.Value) is { } m && m.HasMeta("night_day_colour")) { recorded++; if (!m.GetMeta("night_day_colour").AsColor().IsEqualApprox((Color)e.Key)) badDay++; }
        d["Props.Materials.badDayRecords"] = badDay.ToString();
        return d;
    }

    async Task IsoCycle(string liveTrack, string nestedTrack, bool liveNight, bool nestedNight, bool restore)
    {
        var live = await IsoBuild(this, liveTrack);
        IsoDrive(live, liveNight);
        await IsoFrames(2);
        IsoDrive(live, liveNight);
        var before = IsoCapture(live);
        int diffs = 0;
        void Compare(string when, bool log = true)
        {
            var after = IsoCapture(live);
            foreach (var (k, v) in before)
                if (after[k] != v) { diffs++; IsoLog("diff", $"{when}|{k}|before={Trim(v)}|after={Trim(after[k])}"); }
        }
        static string Trim(string s) => s.Length > 140 ? s[..140] + "..." : s;

        for (int cycle = 1; cycle <= 2; cycle++)
        {
            // open: the viewer session's recipe (RaceScene.ReplayViewer.cs): capture, freeze the live scene, build the nested one in its own world
            var globals = ViewerGlobals.Capture(live);
            live.ProcessMode = ProcessModeEnum.Disabled; live.Visible = false;
            var vp = new SubViewport { Size = new Vector2I(480, 270), OwnWorld3D = true, HandleInputLocally = false, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(vp);
            var nested = await IsoBuild(vp, nestedTrack);
            IsoDrive(nested, nestedNight);
            await IsoFrames(3);
            IsoDrive(nested, nestedNight);
            // while open: the live lists untouched, the nested world's own lights and boat
            var nestedWorld = WorldScope.Of(nested);
            var ndn = (DayNight)nested.FindChild("DayNight", true, false);
            int foreign = IsoField<System.Collections.IList>(ndn, "_sceneryLights").Cast<System.Runtime.CompilerServices.ITuple>().Count(t => WorldScope.Of((Node)t[0]) != nestedWorld);
            int foreignBoat = IsoAll<DachaPatrol>(nested).Count(p => IsoField<PartyRowboat>(p, "_boat") is not { } b || WorldScope.Of(b) != nestedWorld);
            int liveChanged = 0;
            var mid = IsoCapture(live);
            foreach (var k in new[] { "unlit.ids", "scenery.ids", "DachaPatrol.boat", "NodeAdded.connections" })
                if (k != "NodeAdded.connections" && mid[k] != before[k]) liveChanged++;
            IsoLog("during", $"cycle={cycle}|nestedScenery={IsoField<System.Collections.IList>(ndn, "_sceneryLights").Count}|foreignLights={foreign}|nestedPatrols={IsoAll<DachaPatrol>(nested).Count}|nestedWeathers={IsoAll<Weather>(nested).Count}|liveWeathers={IsoAll<Weather>(live).Count}|foreignBoats={foreignBoat}|liveListsChanged={liveChanged}|bad={(foreign + foreignBoat + liveChanged > 0 ? 1 : 0)}");
            // close: free the nested scene, wait for its deferred exit, then the documented restore
            vp.QueueFree();
            int waited = 0;
            while (GodotObject.IsInstanceValid(nested) && waited++ < 20) await IsoFrames(1);
            await IsoFrames(2);
            if (restore) globals.Restore(live);
            live.ProcessMode = ProcessModeEnum.Inherit; live.Visible = true;
            Compare($"cycle{cycle}-after-restore");
            await IsoFrames(3);      // (the live scene's own _Process stays off: only the restore is what is judged)
            Compare($"cycle{cycle}-3-frames-later");
        }
        IsoLog("result", $"live={liveTrack}|nested={nestedTrack}|liveNight={liveNight}|nestedNight={nestedNight}|restore={restore}|diffs={diffs}|");
        live.QueueFree();
    }
}
