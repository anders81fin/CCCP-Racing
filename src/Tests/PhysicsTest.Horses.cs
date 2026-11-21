using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CCCPRacing.Car;
using CCCPRacing.Net;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    const string HorseKey = "Zone:ZoneLife";

    (TrackData t, TrackDef def, ZoneLife zone, int prefix) HorseWorld(int staticPrefix = -1)
    {
        var def = TrackDef.Load("res://tracks/atomgrad.json");
        var t = TrackBuilder.Build(def);
        int prefix = t.Obstacles.Count;      // the static world; the zone's own slots come after it
        var zone = new ZoneLife { Name = "ZoneLife" }; AddChild(zone);
        zone.Init(t, null, def.Zone, staticPrefix);
        return (t, def, zone, prefix);
    }

    /// <summary>The physical body (half width 0.36 m, spine from the tail tip to the muzzle) against every static obstacle near the herd, brute force and independent of the zone's grid; negative: the body overlaps something.</summary>
    static float HorsePhysicalClearance(List<Obstacle> near, Vector2 pos, Vector2 face)
    {
        Vector2 a = pos - face * 1.15f, b = pos + face * 1.95f; float best = float.MaxValue;
        foreach (var o in near)
        {
            var ab = b - a; float l2 = ab.LengthSquared(); float u = l2 < 1e-9f ? 0 : Math.Clamp((o.Center - a).Dot(ab) / l2, 0, 1);
            best = MathF.Min(best, o.Center.DistanceTo(a + ab * u) - o.Radius - 0.36f);
        }
        return best;
    }

    /// <summary>A car that drives past the herd to and fro: the horses are driven away from it, into the yard's fence on one side and the south gate rows on the other.</summary>
    sealed class HorseDrive
    {
        public CarSim Car; public float Clock; public float Speed = 8f;
        public HorseDrive(TrackData t) { Car = new CarSim(new CarParams(), t); }
        public void Step(float dt, float z0)
        {
            Clock += dt;
            float cycle = (Clock * Speed) % 90f, z = z0 + 6f * MathF.Sin(Clock * 0.21f);
            bool west = cycle < 45f;
            float x = west ? 215f - cycle : 125f + (cycle - 45f);
            Car.Pos = new Vector3(x, 0, z); Car.Vel = new Vector3(west ? -Speed : Speed, 0, 0);
        }
    }

    sealed record HorseRun(float MinClear, int Violations, float MinX, float MaxTravel, float MaxStep, int Ticks);

    HorseRun RunHorses(ZoneLife zone, TrackData t, List<Obstacle> near, float seconds, float dt, bool car = true, float z0 = 262f, StreamWriter csv = null)
    {
        var drive = new HorseDrive(t);
        float min = float.MaxValue, minX = float.MaxValue, travel = 0, maxStep = 0; int bad = 0, n = (int)(seconds / dt);
        var prev = Enumerable.Range(0, zone.HorseCount).Select(zone.HorsePos).ToArray();
        for (int tick = 0; tick < n; tick++)
        {
            drive.Step(dt, z0);
            zone.Tick(dt, car ? drive.Car : null);
            for (int i = 0; i < zone.HorseCount; i++)
            {
                Vector2 p = zone.HorsePos(i), f = zone.HorseFace(i);
                // The whole move, interpolated: a big step must not carry the body through a fence between two ticks.
                float step = p.DistanceTo(prev[i]); maxStep = MathF.Max(maxStep, step);
                int sub = Math.Max(1, (int)MathF.Ceiling(step / 0.1f));
                for (int s = 1; s <= sub; s++)
                {
                    float c = HorsePhysicalClearance(near, prev[i].Lerp(p, s / (float)sub), f);
                    min = MathF.Min(min, c); if (c < -0.02f) bad++;
                }
                csv?.WriteLine(FormattableString.Invariant($"{i},{tick * dt:F3},{p.X:F3},{p.Y:F3},{f.X:F3},{f.Y:F3}"));
                minX = MathF.Min(minX, p.X); travel = MathF.Max(travel, p.DistanceTo(zone.HorseHome(i)));
                prev[i] = p;
            }
        }
        return new HorseRun(min, bad, minX, travel, maxStep, n);
    }

    List<Obstacle> HorseNeighbourhood(TrackData t, TrackDef def, int prefix)
        => t.Obstacles.Take(prefix).Where(o => o.Center.DistanceTo(new Vector2(def.Zone.Horses.X, def.Zone.Horses.Z)) < 70f && o.Radius > 0).ToList();

    void HorseTests()
    {
        GD.Print("Atomgrad horses: gait, terrain, fences, determinism, world tape, LAN replication");
        {
            var tw = TrackBuilder.Build(TrackDef.Load("res://tracks/atomgrad.json")); var herd = new Vector2(165, 262);
            int best = 0; for (int i = 0; i < tw.Center.Length; i++) if (tw.Center[i].DistanceTo(herd) < tw.Center[best].DistanceTo(herd)) best = i;
            GD.Print($"  herd lies {tw.Center[best].DistanceTo(herd):F1} m from centre index {best} of {tw.Center.Length} at {tw.Center[best]}");
        }
        HorseCollisionTests();
        HorseGaitAndTerrainTests();
        HorseStopTests();
        HorseDeterminismTests();
        HorseTapeTests();
        HorseLanTests();
    }

    void HorseCollisionTests()
    {
        // 1. The static set is the static prefix only: an actor slot added after it (it may still have Velocity 0 at this moment) is not scenery.
        var def1 = TrackDef.Load("res://tracks/atomgrad.json");
        var d3 = TrackBuilder.Build(def1); int p3 = d3.Obstacles.Count;
        d3.Obstacles.Add(new Obstacle(new Vector2(def1.Zone.Horses.X, def1.Zone.Horses.Z), 4f));      // like a babushka's slot in production
        var zoneA = new ZoneLife(); AddChild(zoneA); zoneA.Init(d3, null, def1.Zone, p3);
        var zoneB = new ZoneLife(); AddChild(zoneB); zoneB.Init(d3, null, def1.Zone);      // default: everything is static, the decoy included
        var zoneC = new ZoneLife(); AddChild(zoneC); zoneC.Init(d3, null, def1.Zone, 1);       // a prefix of one slot: almost nothing is static
        Check(zoneA.HorseClearance(zoneA.HorseHome(0), new Vector2(0, 1)) >= 0.12f && zoneB.HorseClearance(zoneB.HorseHome(0), new Vector2(0, 1)) < 0.12f
            && zoneC.HorseClearance(zoneC.HorseHome(0), new Vector2(0, 1)) > 5f,
            "horses: Init(staticObstacleCount) indexes only the static prefix (an actor slot after it, or the yard's fence rows beyond a short prefix, are not scenery); the default counts every slot");
        zoneA.QueueFree(); zoneB.QueueFree(); zoneC.QueueFree(); d3 = null;

        // 2. Homes are clear and the herd, driven towards the yard's fence by a car, keeps its bodies out of every fence post, at 120 Hz and with big steps.
        var (t, def, zone, prefix) = HorseWorld();
        var near = HorseNeighbourhood(t, def, prefix);
        int posts = near.Count(o => o.Radius <= 0.25f);
        bool homes = Enumerable.Range(0, zone.HorseCount).All(i => HorsePhysicalClearance(near, zone.HorsePos(i), zone.HorseFace(i)) > 0.05f);
        Check(posts > 20 && homes, $"horses: {posts} fence-post circles near the herd; every horse spawns clear of them with a free heading");
        foreach (float dt in new[] { 1f / 120f, 1f / 60f, 0.1f, 0.25f })
        {
            zone.Reset();
            var run = RunHorses(zone, t, near, 90f, dt);
            GD.Print($"  horses dt={dt:F4}: min clearance {run.MinClear:F2} m, violations {run.Violations}, westmost x {run.MinX:F1}, farthest from home {run.MaxTravel:F1} m, largest step {run.MaxStep:F2} m");
            Check(run.Violations == 0 && run.MinClear >= -0.02f && run.MaxTravel > 6f, $"horses: dt {dt:F4} s - body never overlaps a fence post or obstacle ({run.MinClear:F2} m) while driven {run.MaxTravel:F0} m");
        }
        // The scenario must be able to fail: the same drive with the avoidance switched off puts the body through the fence.
        string evidence = ProjectSettings.GlobalizePath("res://media/atomgrad-horses/paths");
        Directory.CreateDirectory(evidence);
        zone.Reset();
        using (var withCsv = new StreamWriter(evidence + "/paths_avoidance_on.csv")) { withCsv.WriteLine("horse,t,x,z,fx,fz"); RunHorses(zone, t, near, 90f, 1f / 60f, csv: withCsv); }
        using (var postFile = new StreamWriter(evidence + "/fence_posts.csv")) { postFile.WriteLine("x,z,r"); foreach (var o in near) if (o.Radius <= 0.25f) postFile.WriteLine(FormattableString.Invariant($"{o.Center.X:F3},{o.Center.Y:F3},{o.Radius:F2}")); }
        zone.Reset(); zone.HorseAvoidance = false;
        StreamWriter off = new(evidence + "/paths_avoidance_off.csv"); off.WriteLine("horse,t,x,z,fx,fz");
        var broken = RunHorses(zone, t, near, 90f, 1f / 60f, csv: off); off.Dispose();
        Check(broken.Violations > 0, $"horses: control - without the avoidance the same drive overlaps the fence ({broken.Violations} ticks, {broken.MinClear:F2} m)");
        zone.HorseAvoidance = true;

        // 3. Turning next to a fence: every heading, 1.0-2.2 m from a post, with the car pushing the horse into it.
        var yardPost = near.Where(o => o.Radius <= 0.25f).OrderBy(o => o.Center.DistanceTo(new Vector2(def.Zone.Horses.X, def.Zone.Horses.Z))).First();
        int turnBad = 0, turnRuns = 0; float turnMin = float.MaxValue;
        Vector2 herd = new(def.Zone.Horses.X, def.Zone.Horses.Z);
        foreach (float gap in new[] { 1.0f, 1.4f, 2.2f })
        for (int k = 0; k < 8; k++)
        {
            Vector2 toHerd = (herd - yardPost.Center).Normalized();
            Vector2 p = yardPost.Center + toHerd * (gap + 1.95f * 0.0f);
            Vector2 face = new Vector2(0, 1).Rotated(k * MathF.PI / 4f);
            zone.Reset(); zone.HorsePlace(0, p, face);
            if (HorsePhysicalClearance(near, p, face) < 0f) continue;      // this heading starts inside the fence: not a case
            var car = new CarSim(new CarParams(), t); car.Vel = new Vector3(8f, 0, 0);
            Vector2 behind = p + toHerd * 9f;      // the car is on the herd's side, so the flee heading points into the fence
            car.Pos = new Vector3(behind.X, 0, behind.Y); car.Vel = new Vector3(-toHerd.X, 0, -toHerd.Y) * 8f;
            Vector2 prev = zone.HorsePos(0);
            for (int tick = 0; tick < 360; tick++)
            {
                zone.Tick(1f / 120f, car);
                var q = zone.HorsePos(0); var f = zone.HorseFace(0);
                float c = HorsePhysicalClearance(near, q, f); turnMin = MathF.Min(turnMin, c); if (c < -0.02f) turnBad++;
                prev = q;
            }
            turnRuns++;
        }
        Check(turnRuns >= 12 && turnBad == 0, $"horses: turning next to the fence ({turnRuns} start poses, min body clearance {turnMin:F2} m) never swings the muzzle or tail into it");
        // A big step and a sudden scare next to the fence (dt 0.25 s, car at 8 m/s two metres away).
        int scareBad = 0;
        foreach (float dt in new[] { 0.1f, 0.25f })
        for (int k = 0; k < 8; k++)
        {
            Vector2 toHerd = (herd - yardPost.Center).Normalized(); Vector2 p = yardPost.Center + toHerd * 1.6f;
            Vector2 face = new Vector2(0, 1).Rotated(k * MathF.PI / 4f);
            zone.Reset(); zone.HorsePlace(1, p, face);
            if (HorsePhysicalClearance(near, p, face) < 0f) continue;
            var car = new CarSim(new CarParams(), t); Vector2 c0 = p + toHerd * 2f; car.Pos = new Vector3(c0.X, 0, c0.Y); car.Vel = new Vector3(-toHerd.X, 0, -toHerd.Y) * 8f;
            Vector2 prevP = p;
            for (int tick = 0; tick < (int)(8f / dt); tick++)
            {
                zone.Tick(dt, car); var q = zone.HorsePos(1); var f = zone.HorseFace(1);
                int sub = Math.Max(1, (int)MathF.Ceiling(q.DistanceTo(prevP) / 0.1f));
                for (int s = 1; s <= sub; s++) if (HorsePhysicalClearance(near, prevP.Lerp(q, s / (float)sub), f) < -0.02f) scareBad++;
                prevP = q;
            }
        }
        Check(scareBad == 0, "horses: a sudden scare with a 0.1-0.25 s step beside the fence never carries the body through it");
        zone.QueueFree();
    }

    void HorseGaitAndTerrainTests()
    {
        var (t, def, zone, _) = HorseWorld();
        // Walk (grazing, no car) and trot (driven): the legs swing, the diagonal pairs move together at a trot, the knees only fold backwards, and nothing snaps.
        float walkAmp = 0, trotAmp = 0, maxJump = 0, shinMax = -1f, standHip = 0; int standing = 0;
        var fl = new List<float>(); var rr = new List<float>(); var fr = new List<float>(); var wfl = new List<float>(); var wrr = new List<float>();
        var drive = new HorseDrive(t); float dt = 1f / 120f; float prevHip = 0, stoodFor = 0; float minFoot = float.MaxValue; float maxSlope = 0, worstPitch = 0, worstRoll = 0, worstY = 0;
        for (int tick = 0; tick < 120 * 80; tick++)
        {
            bool chase = tick >= 120 * 40;
            if (chase) drive.Step(dt, 262f);
            zone.Tick(dt, chase ? drive.Car : null);
            var hip = zone.HorseHip(0, 0).Rotation.X; float shin = zone.HorseShin(0, 0).Rotation.X;
            shinMax = MathF.Max(shinMax, shin); maxJump = MathF.Max(maxJump, MathF.Abs(hip - prevHip)); prevHip = hip;
            float speed = zone.HorseSpeed(0);
            if (speed < 0.001f) { stoodFor += dt; if (stoodFor > 0.5f) { standing++; standHip = MathF.Max(standHip, MathF.Abs(hip)); } } else stoodFor = 0;
            if (!chase && speed > 0.5f) { walkAmp = MathF.Max(walkAmp, MathF.Abs(hip)); wfl.Add(hip); wrr.Add(zone.HorseHip(0, 3).Rotation.X); }
            if (chase && speed > 4.5f) { trotAmp = MathF.Max(trotAmp, MathF.Abs(hip)); fl.Add(hip); rr.Add(zone.HorseHip(0, 3).Rotation.X); fr.Add(zone.HorseHip(0, 1).Rotation.X); }
            // Feet against the ground, and the body against the slope under it (all five horses).
            if (tick % 6 == 0)
                for (int i = 0; i < zone.HorseCount; i++)
                {
                    Vector2 p = zone.HorsePos(i), f = zone.HorseFace(i), r = new(-f.Y, f.X);
                    for (int l = 0; l < 4; l++)
                    {
                        var foot = zone.HorseShin(i, l).GlobalTransform * new Vector3(0, -0.41f, 0);
                        minFoot = MathF.Min(minFoot, foot.Y - t.MeshHeight(foot.X, foot.Z));
                    }
                    float hF = t.MeshHeight(p.X + f.X * 0.9f, p.Y + f.Y * 0.9f), hB = t.MeshHeight(p.X - f.X * 0.9f, p.Y - f.Y * 0.9f);
                    float hR = t.MeshHeight(p.X + r.X * 0.4f, p.Y + r.Y * 0.4f), hL = t.MeshHeight(p.X - r.X * 0.4f, p.Y - r.Y * 0.4f);
                    float slope = MathF.Atan2(hF - hB, 1.8f); maxSlope = MathF.Max(maxSlope, MathF.Abs(slope));
                    var basis = zone.HorseRoot(i).GlobalTransform.Basis.Orthonormalized();
                    float pitch = MathF.Asin(Math.Clamp(-basis.Z.Y, -1f, 1f)), roll = MathF.Asin(Math.Clamp(basis.X.Y, -1f, 1f));
                    worstPitch = MathF.Max(worstPitch, MathF.Abs(pitch - Math.Clamp(slope, -0.45f, 0.45f)));
                    worstRoll = MathF.Max(worstRoll, MathF.Abs(roll - Math.Clamp(MathF.Atan2(hR - hL, 0.8f), -0.45f, 0.45f)));
                    worstY = MathF.Max(worstY, MathF.Abs(zone.HorseRoot(i).GlobalPosition.Y - (hF + hB) * 0.5f));
                }
        }
        float Corr(List<float> a, List<float> b)
        {
            int n = Math.Min(a.Count, b.Count); if (n < 10) return 0;
            float ma = a.Take(n).Average(), mb = b.Take(n).Average(); float sab = 0, saa = 0, sbb = 0;
            for (int i = 0; i < n; i++) { sab += (a[i] - ma) * (b[i] - mb); saa += (a[i] - ma) * (a[i] - ma); sbb += (b[i] - mb) * (b[i] - mb); }
            return sab / MathF.Sqrt(MathF.Max(1e-9f, saa * sbb));
        }
        float trotDiag = Corr(fl, rr), trotLat = Corr(fl, fr), walkDiag = Corr(wfl, wrr);
        GD.Print($"  gait: walk amp {walkAmp:F2} trot amp {trotAmp:F2} diag {trotDiag:F2} lateral {trotLat:F2} walk diag {walkDiag:F2} max hip step/tick {maxJump:F3} standing ticks {standing} stand hip {standHip:F4} knee max {shinMax:F3}; foot min {minFoot:F2}; slope max {maxSlope:F2} pitch err {worstPitch:F3} roll err {worstRoll:F3} y err {worstY:F3}");
        Check(walkAmp > 0.15f && trotAmp > 0.45f && trotAmp > walkAmp * 1.2f,      // (the legs are placed on the ground now: a walk needs a wide swing too, a trot only a little wider)
             $"horses gait: legs swing, wider at a trot ({walkAmp:F2} -> {trotAmp:F2} rad)");
        Check(trotDiag > 0.8f && trotLat < -0.7f && walkDiag < 0.5f, $"horses gait: a trot moves diagonal legs together ({trotDiag:F2}) and opposite sides apart ({trotLat:F2}); a walk is a different sequence ({walkDiag:F2})");
        Check(shinMax <= 1e-5f && maxJump < 0.13f && standing > 120 && standHip < 1e-3f, $"horses gait: knees only fold back, no leg moves more than {maxJump:F3} rad in a tick (a snap is 0.3+; a steady trot of the foot-placed gait peaks at ~0.11) and the legs are still half a second after the horse stops ({standing} ticks)");
        Check(minFoot > -0.5f, $"horses terrain: no hoof sinks more than half a metre into the ground (lowest {minFoot:F2} m)");
        Check(worstPitch < 0.2f && worstRoll < 0.25f && worstY < 0.2f, $"horses terrain: the body follows the slope (pitch error {worstPitch:F2}, roll error {worstRoll:F2} rad, height error {worstY:F2} m (the body sinks up to 0.14 m in a trot so that the legs can reach), steepest sampled {maxSlope:F2} rad)");
        zone.QueueFree();
    }

    void HorseStopTests()
    {
        // A trotting horse that is stopped dead (what a blocked horse does) must not snap its legs: the leg angle follows the shown gait down, and after a short settle the legs are neutral.
        var (t, def, zone, _) = HorseWorld();
        var drive = new HorseDrive(t); float dt = 1f / 120f;
        for (int tick = 0; tick < 120 * 12; tick++) { drive.Step(dt, 262f); zone.Tick(dt, drive.Car); if (zone.HorseSpeed(0) > 5.9f) break; }
        float trotSpeed = zone.HorseSpeed(0);
        float steadyMax = 0, prev = zone.HorseHip(0, 0).Rotation.X;
        for (int tick = 0; tick < 120 * 6; tick++) { drive.Step(dt, 262f); zone.Tick(dt, drive.Car); float h = zone.HorseHip(0, 0).Rotation.X; steadyMax = MathF.Max(steadyMax, MathF.Abs(h - prev)); prev = h; }
        zone.HorseHardStop(0);
        float stopMax = 0, neutralAt = -1; prev = zone.HorseHip(0, 0).Rotation.X; float t0 = 0;
        for (int tick = 0; tick < 120 * 3; tick++)
        {
            // The car stays away: no new scare, the horse only stands.
            zone.Tick(dt, null); t0 += dt;
            float h = zone.HorseHip(0, 0).Rotation.X; stopMax = MathF.Max(stopMax, MathF.Abs(h - prev)); prev = h;
            float legs = 0; for (int l = 0; l < 4; l++) legs = MathF.Max(legs, MathF.Max(MathF.Abs(zone.HorseHip(0, l).Rotation.X), MathF.Abs(zone.HorseShin(0, l).Rotation.X)));
            if (neutralAt < 0 && legs < 1e-4f && zone.HorseSpeed(0) < 0.001f) neutralAt = t0;
        }
        GD.Print($"  horse stop: trot {trotSpeed:F1} m/s, steady trot hip step max {steadyMax:F4}/tick, after a hard stop {stopMax:F4}/tick, legs neutral after {neutralAt:F2} s");
        Check(trotSpeed > 5f && stopMax <= steadyMax * 1.2f + 0.003f, $"horses gait: a dead stop from a trot moves the legs no faster per tick than the trot itself ({stopMax:F4} vs {steadyMax:F4} rad)");
        Check(neutralAt > 0 && neutralAt < 1.2f, $"horses gait: the legs are exactly neutral {neutralAt:F2} s after the stop (no residual swing)");
        zone.QueueFree();
    }

    void HorseSceneTests()
    {
        string dir = ProjectSettings.GlobalizePath("res://media/atomgrad-horses/production-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
        Directory.CreateDirectory(dir); string laps = dir + "/laps"; Directory.CreateDirectory(laps);
        var record = TapeGame(dir + "/record-data", new[] { "--track=atomgrad", "--start", "--autodrive", "--no-record", "--tape-debug", "--tape-session-laps=" + laps, "--tape-quit-laps=1" }, 240);
        Check(record.Exit == 0 && !record.TimedOut && !record.Crashed && !record.Has("ERROR:"), "horses production: the real game drives and records a whole Atomgrad lap");
        var lap = Directory.GetDirectories(laps, "lap-*").OrderBy(x => x).FirstOrDefault();
        if (lap == null) { Check(false, "horses production: a paired lap was saved"); return; }
        var def = TrackDef.Load("res://tracks/atomgrad.json"); var track = TrackBuilder.Build(def);
        var ghost = Ghost.Load(lap + "/ghost_atomgrad.bin", new CarParams(), track);
        var tape = WorldTapeFile.Load(lap + "/tape_atomgrad.bin", ghost, WorldTapeCodec.Decode);
        Check(tape != null && tape.ActorStructure.TryGetValue(HorseKey, out var st) && st.Version == 2 && st.ParentPaths.ContainsKey("1/legFL") && st.ParentPaths.ContainsKey("5/shinRR"),
            "horses production: the saved lap pair holds Zone:ZoneLife version 2 with the horses' legs and knees");
        // Playback of the saved lap into a fresh world: the legs of the recorded horses move, and the horses stay out of the yard's fence in the recording itself.
        var zone = new ZoneLife { Name = "ZoneLife" }; AddChild(zone); zone.Init(track, null, def.Zone);
        var near = HorseNeighbourhood(track, def, track.Obstacles.Count - 14 > 0 ? track.Obstacles.Count : 0).Where(o => o.Radius <= 0.25f).ToList();
        var player = new WorldTapePlayer(tape, new WorldTapeIndex(new[] { (HorseKey, (Node3D)zone) }), new TapeSink());
        float dur = tape.LapTime; float legMax = 0, minClear = float.MaxValue; int samples = 0;
        for (float s = 0; s < dur; s += 0.5f)
        {
            player.Seek(s); samples++;
            for (int i = 0; i < zone.HorseCount; i++)
            {
                for (int l = 0; l < 4; l++) legMax = MathF.Max(legMax, MathF.Abs(zone.HorseHip(i, l).Rotation.X));
                var root = zone.HorseRoot(i); var f3 = -root.GlobalTransform.Basis.Z; var face = new Vector2(f3.X, f3.Z).Normalized();
                minClear = MathF.Min(minClear, HorsePhysicalClearance(near, new Vector2(root.GlobalPosition.X, root.GlobalPosition.Z), face));
            }
        }
        player.Release();
        Check(player != null && samples > 20 && legMax > 0.1f, $"horses production: the recorded lap ({dur:F0} s) replays moving legs (largest hip angle {legMax:F2} rad over {samples} seeks)");
        Check(minClear > -0.05f, $"horses production: in the recorded lap itself the horses never overlap the yard fence (closest {minClear:F2} m)");
        zone.QueueFree();
        var watch = TapeGame(lap, new[] { "--track=atomgrad", "--watch", "--tape-debug", "--tape-replay-start=4", "--tape-wrap-every=2", "--tape-replay-quit=8" }, 240);
        Check(watch.Exit == 0 && !watch.TimedOut && !watch.Crashed && !watch.Has("ERROR:") && watch.All("from tape: covers").Any(l => l.Split("], live")[0].Contains(HorseKey)),
            "horses production: Watch covers Zone:ZoneLife, seeks to 4 s, loops repeatedly and exits cleanly");

        // An Atomgrad lap saved before this change (original kept; a copy is replayed).
        string oldSrc = ProjectSettings.GlobalizePath("res://media/review-after-limit-20261112/atomgrad-radiation/watch-data");
        string oldDir = dir + "/old-tape-data";
        if (Directory.Exists(oldSrc))
        {
            Directory.CreateDirectory(oldDir);
            foreach (var f in Directory.GetFiles(oldSrc)) File.Copy(f, oldDir + "/" + Path.GetFileName(f));
            string before = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(oldSrc + "/tape_atomgrad.bin")));
            var old = TapeGame(oldDir, new[] { "--track=atomgrad", "--watch", "--tape-debug", "--tape-replay-start=4", "--tape-wrap-every=2", "--tape-replay-quit=8" }, 240);
            string after = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(oldSrc + "/tape_atomgrad.bin")));
            Check(old.Exit == 0 && !old.TimedOut && !old.Crashed && !old.Has("ERROR:") && old.Has("Zone tape: recorded ZoneLife version 1 is incompatible")
                && !old.All("from tape: covers").Any(l => l.Split("], live")[0].Contains(HorseKey)) && before == after,
                "horses production: an old lap tape (ZoneLife version 1) still replays its other actors, says visibly that the zone runs live, and its original is untouched");
            File.WriteAllText(dir + "/old-tape.sha256", before + "\n");
        }
        else Check(false, "horses production: the old Atomgrad lap for the compatibility run exists");
        GD.Print("HORSE PRODUCTION OUTPUT " + dir);
    }

    ulong HorseHash(ZoneLife zone)
    {
        ulong h = 1469598103934665603UL;
        void Mix(float v) { h = (h ^ (uint)BitConverter.SingleToInt32Bits(v)) * 1099511628211UL; }
        for (int i = 0; i < zone.HorseCount; i++)
        {
            Mix(zone.HorsePos(i).X); Mix(zone.HorsePos(i).Y); Mix(zone.HorseFace(i).X); Mix(zone.HorseFace(i).Y); Mix(zone.HorseSpeed(i));
            for (int l = 0; l < 4; l++) { Mix(zone.HorseHip(i, l).Rotation.X); Mix(zone.HorseShin(i, l).Rotation.X); }
            Mix(zone.HorseHead(i).Rotation.X); Mix(zone.HorseTailNode(i).Rotation.X); Mix(zone.HorseRoot(i).GlobalPosition.Y);
        }
        return h;
    }

    void HorseDeterminismTests()
    {
        var (t, def, zone, _) = HorseWorld();
        ulong Run()
        {
            zone.Reset(); var drive = new HorseDrive(t);
            for (int tick = 0; tick < 120 * 40; tick++) { drive.Step(1f / 120f, 262f); zone.Tick(1f / 120f, tick < 120 * 10 || tick > 120 * 20 ? null : drive.Car); }
            return HorseHash(zone);
        }
        ulong first = Run(), second = Run();
        zone.Reset(); ulong rest1 = HorseHash(zone);
        for (int k = 0; k < 500; k++) zone.Tick(1f / 120f, null);
        zone.Reset(); ulong rest2 = HorseHash(zone);
        Check(first == second && rest1 == rest2, "horses: Reset restores positions, headings, grazing phase, gait and RNG exactly - two identical runs end identically and the rest pose is the same after any history");
        var fresh = HorseWorld(); zone.Reset(); fresh.zone.Reset();
        Check(HorseHash(fresh.zone) == HorseHash(zone), "horses: a freshly built world and a reset one start from the same state");
        fresh.zone.QueueFree(); zone.QueueFree();
    }

    void HorseTapeTests()
    {
        string dir = ProjectSettings.GlobalizePath("res://media/atomgrad-horses/tests-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
        Directory.CreateDirectory(dir);
        var (t, def, zone, _) = HorseWorld();
        var index = new WorldTapeIndex(new[] { (HorseKey, (Node3D)zone) });
        var st = index.StructureOf(HorseKey);
        var names = new[] { "1/horse", "1/horseHead", "1/tail", "1/legFL", "1/legFR", "1/legRL", "1/legRR", "1/shinFL", "1/shinFR", "1/shinRL", "1/shinRR", "5/legRR", "5/shinRR", "catfish", "catfishRear", "catfishTail" };
        Check(names.All(st.ParentPaths.ContainsKey) && st.Version == 3, $"horses tape: stable part names for the horse, head, tail and four hips and knees of all five horses (ZoneLife tape_ver {st.Version})");
        // Rest pose: the index built after any live history is the one built at world build, because Init/Reset pose the horses the same way.
        for (int k = 0; k < 400; k++) zone.Tick(1f / 120f, null);
        zone.Reset();
        Check(new WorldTapeIndex(new[] { (HorseKey, (Node3D)zone) }).StructureOf(HorseKey).Hash == st.Hash, "horses tape: the rest pose in the index is independent of the horses' live history (Reset gives it back)");

        var recorder = new WorldTapeRecorder(); recorder.Begin(index); var ghost = new Ghost();
        var drive = new HorseDrive(t); var car = new CarSim(new CarParams(), t);
        var refs = new Dictionary<int, float[]>();
        float dt = 1f / 120f;
        for (int tick = 0; tick <= 120 * 30; tick++)
        {
            float time = tick * dt;
            drive.Step(dt, 262f);
            zone.Tick(dt, time > 6f ? drive.Car : null);
            recorder.Record(time); ghost.Record(time, car, 0, 0, 0);
            if (tick % 8 == 0) refs[tick / 8] = HorseSample(zone);
        }
        var saved = ghost.CopyWithTime(30f); var tape = recorder.Finish(30f, WorldTapeFingerprint.Of(saved)); tape.TrackId = "atomgrad";
        var pair = WorldTapeFile.SavePair(dir + "/ghost_atomgrad.bin", dir + "/tape_atomgrad.bin", saved, tape, WorldTapeRealOps.Instance, WorldTapeCodec.Encode, WorldTapeCodec.Decode);
        var loaded = WorldTapeFile.Load(dir + "/tape_atomgrad.bin", saved, WorldTapeCodec.Decode);
        Check(pair.Whole && loaded != null && loaded.ActorStructure.TryGetValue(HorseKey, out var tapeSt) && tapeSt.Hash == st.Hash && tapeSt.ParentPaths.ContainsKey("3/legRL"),
            "horses tape: record, real paired save and load keep the leg, knee, tail and head parts");

        var playZone = new ZoneLife { Name = "ZoneLife" }; AddChild(playZone); playZone.Init(t, null, def.Zone);
        var playIndex = new WorldTapeIndex(new[] { (HorseKey, (Node3D)playZone) });
        var player = new WorldTapePlayer(loaded, playIndex, new TapeSink());
        Check(player.Covers(HorseKey), "horses tape: the same-version live actor is covered");
        float worst = 0; int compared = 0, legsMoved = 0;
        foreach (int step in new[] { 0, 60, 135, 200, 330, 400, 60, 0, 449, 135 })      // seeks forward, back and a loop to the start (15 Hz samples)
        {
            player.Seek(step / 15f);
            float e = HorseSampleError(refs[step], HorseSample(playZone), out bool moved); worst = MathF.Max(worst, e); compared++; if (moved) legsMoved++;
        }
        Check(worst < 0.04f && legsMoved >= 3, $"horses tape: seek/loop restores root pose, head, tail, hips and knees at {compared} instants (worst error {worst:F3}; {legsMoved} with walking legs)");
        bool smooth = true; player.Seek(0);
        for (int tick = 1; tick <= 15 * 12; tick++) { float tt = tick / 15f; player.Apply(tt, 1f / 15f); var a = HorseSample(playZone); player.Seek(tt); smooth &= HorseSampleError(a, HorseSample(playZone), out _) < 0.02f; }
        Check(smooth, "horses tape: ordinary playback of twelve seconds matches seeking to the same instants");
        Vector2 posAtExit = playZone.HorsePos(0);
        player.Release();
        playZone.Tick(dt, null);
        Check(playZone.HorsePos(0).DistanceTo(posAtExit) < 1f && HorseSample(playZone).All(float.IsFinite), "horses tape: leaving playback hands the horses back to the live simulation without a jump");

        // Compatibility: a tape from before the legs (version 1, root and head only) and a wrong-version actor are not covered and cannot pose the live horses.
        var legacy = new ZoneLife { Name = "ZoneLife" }; AddChild(legacy); legacy.Init(t, null, def.Zone);
        for (int i = 0; i < legacy.HorseCount; i++) foreach (var node in new Node3D[] { legacy.HorseTailNode(i) }.Concat(Enumerable.Range(0, 4).SelectMany(l => new[] { legacy.HorseHip(i, l), legacy.HorseShin(i, l) })))
            node.RemoveMeta("tape_part");
        Tape.Version(legacy, 1);
        var oldIndex = new WorldTapeIndex(new[] { (HorseKey, (Node3D)legacy) });
        var oldRec = new WorldTapeRecorder(); oldRec.Begin(oldIndex); var oldGhost = new Ghost();
        for (int tick = 0; tick <= 600; tick++) { legacy.Tick(dt, null); oldRec.Record(tick * dt); oldGhost.Record(tick * dt, car, 0, 0, 0); }
        var oldSaved = oldGhost.CopyWithTime(5f); var oldTape = oldRec.Finish(5f, WorldTapeFingerprint.Of(oldSaved)); oldTape.TrackId = "atomgrad";
        var oldPair = WorldTapeFile.SavePair(dir + "/ghost_old.bin", dir + "/tape_old.bin", oldSaved, oldTape, WorldTapeRealOps.Instance, WorldTapeCodec.Encode, WorldTapeCodec.Decode);
        var oldLoaded = WorldTapeFile.Load(dir + "/tape_old.bin", oldSaved, WorldTapeCodec.Decode);
        var liveBefore = HorseSample(playZone);
        var oldPlayer = new WorldTapePlayer(oldLoaded, playIndex, new TapeSink()); oldPlayer.Seek(3);
        Check(oldPair.Whole && oldLoaded.ActorStructure[HorseKey].Version == 1 && !oldPlayer.Covers(HorseKey) && HorseSample(playZone).SequenceEqual(liveBefore),
            "horses tape: an old version-1 tape does not cover the new horses and cannot overwrite them (they run live; RaceScene.Tape logs the Zone tape message)");
        oldPlayer.Release();
        Tape.Version(playZone, 3);
        var wrong = new WorldTapePlayer(loaded, new WorldTapeIndex(new[] { (HorseKey, (Node3D)playZone) }), new TapeSink()); wrong.Seek(2);
        Check(!wrong.Covers(HorseKey) && HorseSample(playZone).SequenceEqual(liveBefore), "horses tape: a changed tape_ver makes the actor incompatible and leaves it live");
        wrong.Release();
        zone.QueueFree(); playZone.QueueFree(); legacy.QueueFree();
        GD.Print("HORSE TAPE OUTPUT " + dir);
    }

    static float[] HorseSample(ZoneLife z)
    {
        var s = new List<float>();
        foreach (int i in new[] { 0, 2, 4 })
        {
            var g = z.HorseRoot(i).GlobalPosition; var q = z.HorseRoot(i).Quaternion;
            s.AddRange(new[] { g.X, g.Y, g.Z, q.X, q.Y, q.Z, q.W, z.HorseHead(i).Rotation.X, z.HorseTailNode(i).Rotation.X });
            for (int l = 0; l < 4; l++) { s.Add(z.HorseHip(i, l).Rotation.X); s.Add(z.HorseShin(i, l).Rotation.X); }
        }
        return s.ToArray();
    }

    static float HorseSampleError(float[] a, float[] b, out bool legsMoved)
    {
        float worst = 0; legsMoved = false;
        for (int i = 0; i < a.Length; i++)
        {
            float e = MathF.Abs(a[i] - b[i]);
            if (i % 17 is 3 or 4 or 5 or 6) e = MathF.Min(e, MathF.Abs(a[i] + b[i]));      // a quaternion and its negative are one rotation
            worst = MathF.Max(worst, e);
            if (i % 17 >= 9 && MathF.Abs(a[i]) > 0.15f) legsMoved = true;
        }
        return worst;
    }

    void HorseLanTests()
    {
        // The host's horses reach a client by the generic world replication: every leg, knee and the tail is a mesh leaf of the same actor and travels with its global pose.
        var (t, def, zone, _) = HorseWorld();
        var sender = new LanWorld(this); var clientRoot = new Node3D(); AddChild(clientRoot); var receiver = new LanWorld(clientRoot);
        var drive = new HorseDrive(t);
        for (int tick = 0; tick < 120 * 20; tick++) { drive.Step(1f / 120f, 262f); zone.Tick(1f / 120f, tick > 120 * 5 ? drive.Car : null); }
        var packets = new List<(byte[] data, bool mesh)>();
        sender.Capture(new Node3D[] { zone }, t.Obstacles, Array.Empty<int>(), (b, m) => packets.Add((b, m)));
        foreach (var p in packets.Where(p => p.mesh)) receiver.MeshIn(p.data);
        foreach (var p in packets.Where(p => !p.mesh)) receiver.StateIn(p.data, t.Obstacles);
        receiver.Snap();
        var leaves = zone.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().Where(m => m.Mesh != null && m.GetParent()?.GetParent()?.Name.ToString().StartsWith("Horse") != false).ToList();
        var replicas = clientRoot.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>().ToList();
        int horseLeaves = zone.FindChildren("Horse*", "Node3D", false, false).Sum(h => h.FindChildren("*", "MeshInstance3D", true, false).Count);
        var sentOrigins = zone.FindChildren("Horse*", "Node3D", false, false).SelectMany(h => h.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>()).Select(m => m.GlobalPosition).ToList();
        int matched = sentOrigins.Count(o => replicas.Any(r => r.GlobalPosition.DistanceTo(o) < 0.02f));
        Check(horseLeaves >= 5 * 18 && matched == sentOrigins.Count, $"horses LAN: all {horseLeaves} horse mesh leaves (legs, knees, hooves, tail, head) arrive on the replica at the host's poses ({matched} matched)");
        bool moving = Enumerable.Range(0, zone.HorseCount).Any(i => zone.HorseSpeed(i) > 3f);
        Check(moving, "horses LAN: the snapshot was taken while the herd was trotting");
        sender.Free(); receiver.Free(); clientRoot.QueueFree(); zone.QueueFree();
    }
}
