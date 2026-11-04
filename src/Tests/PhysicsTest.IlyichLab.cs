using System;
using System.Linq;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>ILYICHLAB=1: the Ilyich's Lamp school's bends: where each technique works (and its opposite does not).</summary>
    void IlyichLab()
    {
        var track = TrackBuilder.Build(TrackDef.Load($"res://tracks/{(OS.GetEnvironment("LABTRACK") is { Length: > 0 } lt ? lt : "ilyich")}.json"));      // env LABTRACK: another track (map, ditch)
        CarSim New() => new(new CarParams(), track) { Obstacles = track.Obstacles, WorldMax = track.WorldSize - 4 };
        int n = track.Center.Length;
        if (OS.GetEnvironment("ILYICHLAB") == "terrain")
        {
            IlyichTerrainTest(track);
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        if (OS.GetEnvironment("ILYICHLAB") == "ditch")
        {
            // Off the road (8-30 m out), facing up the steepest slope: how far does full gas get the car in 4 s?
            for (int i = 0; i < n; i += 15)
            for (int side = -1; side <= 1; side += 2)
            for (float off = 8; off <= 30; off += 4)
            {
                Vector2 d = track.CenterDir(i), nrm = new(-d.Y, d.X);
                Vector2 p = track.Center[i] + nrm * (side * (track.HalfWidth[i] + off));
                if (track.NearestCenter(p, out float dist) >= 0 && dist < track.HalfWidth[i] + 6f) continue;
                float h = track.MeshHeight(p.X, p.Y, out Vector3 nn);
                Vector2 up = new(-nn.X, -nn.Z);
                if (up.Length() < 0.15f) continue;      // flat
                up = up.Normalized();
                var car = New();
                car.Reset(new Vector3(p.X, h + 0.7f, p.Y), TrackData.YawFor(up));
                for (float t = 0; t < 4f; t += Dt) { car.SetControls(1f, 0f, 0f, false, false); car.Step(Dt); }
                float moved = new Vector2(car.Pos.X, car.Pos.Z).DistanceTo(p);
                if (moved < 6f) GD.Print($"  stuck at metre {i} side {side} off {off}: ({p.X:F0},{p.Y:F0}) slope {Mathf.RadToDeg(nn.AngleTo(Vector3.Up)):F0} deg, up ({up.X:F2},{up.Y:F2}), moved {moved:F1} m, {car.Wheels[2].Surface}");
            }
            GetTree().Quit();
            return;
        }
        if (OS.GetEnvironment("ILYICHLAB") is "rollers" or "ford")
        {
            // The track tour's tips: a held speed over the rollers and the jump (225-345 m), or a
            // line through the ford (370-440 m, metres right of the centre line): time, air, upsets.
            bool ford = OS.GetEnvironment("ILYICHLAB") == "ford";
            int from = ford ? 360 : 225, to = ford ? 440 : 345;
            foreach (float kmh in ford ? new[] { 50f, 70f, 90f } : new[] { 50f, 60f, 70f, 80f, 90f, 100f, 110f })
                foreach (float lat in ford ? new[] { -3.5f, -1.5f, 0f, 1.5f, 3.5f } : new[] { 0f })
                {
                    var car = New();
                    int h2 = from;
                    SchoolScript.SetRolling(car, track, SchoolScript.RoadPoint(track, from, lat), track.CenterDir(from), kmh / 3.6f);
                    float air = 0, airSum = 0, maxAir = 0, maxPitch = 0, minUp = 1, minKmh = 999, t = 0;
                    int idx = from, lands = 0; bool wasAir = false;
                    for (; t < 20f && idx < to; t += Dt)
                    {
                        float k = car.Speed * 3.6f;
                        float steer = SchoolScript.SlideSteer(car, track, ref h2, lat);
                        car.SetControls(k < kmh ? 1f : 0f, k > kmh + 8f ? 0.4f : 0f, steer, false, false);
                        car.Step(Dt);
                        idx = SchoolScript.Follow(track, new Vector2(car.Pos.X, car.Pos.Z), ref h2);
                        bool inAir = true;
                        foreach (var w in car.Wheels) if (w.Contact) inAir = false;
                        if (wasAir && !inAir && air > 0.1f) lands++;
                        air = inAir ? air + Dt : 0; maxAir = Math.Max(maxAir, air); if (inAir) airSum += Dt;
                        wasAir = inAir;
                        maxPitch = Math.Max(maxPitch, MathF.Abs(Mathf.RadToDeg(MathF.Asin(Math.Clamp(car.Forward.Y, -1f, 1f)))));
                        minUp = Math.Min(minUp, car.Up.Y);
                        if (idx > from + 5) minKmh = Math.Min(minKmh, car.Speed * 3.6f);
                    }
                    GD.Print($"== {(ford ? "ford" : "rollers")} {kmh} km/h lat {lat}: {t:F2} s, air {airSum:F2} s (longest {maxAir:F2}, {lands} landings), max pitch {maxPitch:F0}, min up {minUp:F2}, min {minKmh:F0} km/h");
                }
            GetTree().Quit();
            return;
        }
        if (OS.GetEnvironment("ILYICHLAB") == "props")
        {
            // How finished is each kind of object: mesh nodes and triangles per instance (batched meshes count their triangles).
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var root = Props.Build(track);
            GD.Print($"Props.Build of the track: {sw.ElapsedMilliseconds} ms (CPU, one thread)");
            if (OS.HasEnvironment("PROPTIME"))
            {
                // The part builders of Props.Build one by one (run again, so the second time is warm: JIT and caches are in).
                void Time(string name, Func<Node> make) { sw.Restart(); var made = make(); GD.Print($"  build time {name,-14} {sw.ElapsedMilliseconds,5} ms"); made.QueueFree(); }
                Time("Yards", () => Village.Yards(track));
                Time("VillageLife", () => VillageLife.Build(track));
                Time("GrainDelivery", () => GrainDelivery.Build(track));
                Time("DachaGrounds", () => PartyDacha.Grounds(track));
                Time("Rowboat", () => PartyRowboat.Build(track));
                Time("Duck", () => PartyPondDuck.Build(track));
                Time("Workers", () => KolkhozWorkers.Build(track));
                Time("FordGuides", () => FordGuides.Build(track));
                var again = System.Diagnostics.Stopwatch.StartNew();
                Props.Build(track);
                GD.Print($"Props.Build a second time (warm): {again.ElapsedMilliseconds} ms");
            }
            var stats = new System.Collections.Generic.Dictionary<string, (int n, int nodes, long tris, int lights)>();
            void Count(Node node, ref int nodes, ref long tris, ref int lights)
            {
                if (node is MeshInstance3D mi && mi.Mesh != null)
                {
                    nodes++;
                    for (int sIdx = 0; sIdx < mi.Mesh.GetSurfaceCount(); sIdx++)
                    {
                        var arr = mi.Mesh.SurfaceGetArrays(sIdx);
                        int idx = arr[(int)Mesh.ArrayType.Index].AsInt32Array().Length;
                        tris += (idx > 0 ? idx : arr[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length) / 3;
                    }
                }
                if (node is Light3D) lights++;
                foreach (var c in node.GetChildren()) Count(c, ref nodes, ref tris, ref lights);
            }
            foreach (Node child in root.GetChildren())
            {
                string kind = child.HasMeta("prop") ? track.Props[(int)child.GetMeta("prop")].Type : child.Name.ToString();
                int nodes = 0, lights = 0; long tris = 0;
                Count(child, ref nodes, ref tris, ref lights);
                var cur = stats.TryGetValue(kind, out var v) ? v : default;
                stats[kind] = (cur.n + 1, cur.nodes + nodes, cur.tris + tris, cur.lights + lights);
            }
            foreach (var kv in stats.OrderByDescending(k => k.Value.tris / k.Value.n))
                GD.Print($"  {kv.Key,-14} x{kv.Value.n,-4} per object: {kv.Value.nodes / kv.Value.n,5} mesh nodes, {kv.Value.tris / kv.Value.n,7} triangles, {kv.Value.lights / (float)kv.Value.n:F1} lights");
            // Vehicles, figures and the fences, built one by one.
            void Report(string label, Node node)
            {
                int nodes = 0, lights = 0; long tris = 0;
                Count(node, ref nodes, ref tris, ref lights);
                GD.Print($"  {label,-22} {nodes,5} mesh nodes, {tris,7} triangles");
            }
            GD.Print("World builders of the track:");
            var def = TrackDef.Load($"res://tracks/{(OS.GetEnvironment("LABTRACK") is { Length: > 0 } lt2 ? lt2 : "ilyich")}.json");
            Report("runway (Airbase)", Airbase.BuildRunway(track));
            if (track.Banks != null) { Report("snowbanks", SnowbankMesh.Build(track)); Report("snow poles", SnowPoles.Build(track)); }
            if (track.Winter) Report("taiga mesh", TaigaMesh.Build(track));
            if (track.LogPiles.Count > 0) Report("log piles", LogPiles.Build(track));
            if (def.Gulag != null) Report("labour camp", GulagCamp.Build(track, def.Gulag, new System.Collections.Generic.List<Obstacle>()));
            Report("MiK-21 jet", Airbase.Balalaika());
            Report("hangar", Airbase.Hangar(track.Winter));
            GD.Print("Vehicles, figures, fences:");
            var bus = new SovietBus(); Report("bus (SovietBus)", bus);
            var tractor = new Node3D(); DrunkTractor.BuildMtAgro(tractor, false, out _, out _); Report("tractor MT-Agro", tractor);
            var plough = new Node3D(); DrunkTractor.BuildMtAgro(plough, true, out _, out _); Report("tractor + plough", plough);
            foreach (KolkhozJob job in Enum.GetValues<KolkhozJob>()) Report($"worker {job}", KolkhozWorkerModel.Build(job, 0, 0f, 1));
            Report("bus shelter", BusShelter.Build());
            Report("fences (all)", Fences.Build(track));
            GetTree().Quit();
            return;
        }
        if (OS.GetEnvironment("ILYICHLAB") == "drop")
        {
            // A clean landing: the car falls from a height at a speed, slightly turning and rolled (as off a jump), coasting.
            // Run twice, with the old suspension (a bump stop that throws the body back, no yaw damping) and the new one.
            int at = OS.GetEnvironment("DROPAT") is { Length: > 0 } da ? int.Parse(da) : 20;
            foreach (bool old in new[] { true, false })
            foreach (float ms in new[] { 15f, 28f })
            foreach (float drop in new[] { 0.4f, 1.0f, 2.0f })
            {
                var car = New();
                if (old) { car.P.ReboundDamper = car.P.Damper; car.P.BumpStopDamp = 0; car.P.BumpStopReturn = 1; car.P.AirYawDamp = 0; car.P.LandingSideAssist = 1; car.P.LandingYawDamp = 0; }
                SchoolScript.SetRolling(car, track, SchoolScript.RoadPoint(track, at, 0), track.CenterDir(at), ms);
                car.Pos += Vector3.Up * drop;
                car.Rot = new Basis(Vector3.Forward, Mathf.DegToRad(4f)) * car.Rot;      // rolled 4 degrees
                car.AngVel = Vector3.Up * 0.25f;                                           // 14 deg/s of yaw
                float reboundVy = 0, slipMax = 0, yawMax = 0, rollMax = 0, landT = -1, h0 = car.Pos.Y; int bounces = 0, stops = 0; bool wasAir = false, relaunch = false;
                for (float t = 0; t < 3f; t += Dt)
                {
                    car.SetControls(0.3f, 0f, 0f, false, false);
                    car.Step(Dt);
                    bool inAir = true;
                    foreach (var w in car.Wheels) if (w.Contact) inAir = false;
                    if (landT < 0 && !inAir) landT = t;
                    if (landT >= 0)
                    {
                        if (inAir && !relaunch && t - landT > 0.05f) { bounces++; relaunch = true; }
                        if (!inAir) relaunch = false;
                        foreach (var w in car.Wheels) if (w.Contact && w.Compression > car.P.MaxTravel) { stops++; break; }
                        reboundVy = Math.Max(reboundVy, car.Vel.Y);
                        var hv2 = new Vector2(car.Vel.X, car.Vel.Z);
                        if (hv2.Length() > 3f) slipMax = Math.Max(slipMax, MathF.Abs(Mathf.RadToDeg(hv2.AngleTo(new Vector2(car.Forward.X, car.Forward.Z)))));
                        yawMax = Math.Max(yawMax, MathF.Abs(Mathf.RadToDeg(car.AngVel.Dot(car.Up))));
                        rollMax = Math.Max(rollMax, MathF.Abs(Mathf.RadToDeg(MathF.Asin(Math.Clamp(car.Right.Y, -1f, 1f)))));
                    }
                    wasAir = inAir;
                }
                GD.Print($"== drop {(old ? "OLD" : "NEW")} {ms * 3.6f:F0} km/h from {drop:F1} m: landed {landT:F2} s, bounces {bounces}, rebound vy {reboundVy:F1} m/s, bump-stop frames {stops}, sideslip max {slipMax:F0} deg, yaw max {yawMax:F0} deg/s, roll max {rollMax:F0} deg, end up {car.Up.Y:F2}");
            }
            GetTree().Quit();
            return;
        }
        if (OS.GetEnvironment("ILYICHLAB") == "jump")
        {
            // Full throttle over the ridge's rollers and the jump down to the river: how close to a flip?
            for (int i = 225; i < 360; i += 5) GD.Print($"  height {i}: {track.CenterH[i]:F2}");
            int from = OS.GetEnvironment("JUMPFROM") is { Length: > 0 } jf ? int.Parse(jf) : 225;
            foreach (float kmh in new[] { 60f, 80f, 100f, 120f })
            {
                var car = New();
                if (OS.HasEnvironment("OLDSUSP")) { car.P.ReboundDamper = car.P.Damper; car.P.BumpStopDamp = 0; car.P.BumpStopReturn = 1; car.P.AirYawDamp = 0; car.P.LandingSideAssist = 1; car.P.LandingYawDamp = 0; }
                int h2 = from;
                SchoolScript.SetRolling(car, track, SchoolScript.RoadPoint(track, from, 0), track.CenterDir(from), kmh / 3.6f);
                float minUp = 1f, minUpAt = 0, maxPitch = 0, pitchAt = 0, air = 0, maxAir = 0, topKmh = 0;
                int flips = 0; bool wasFlipped = false;
                // Landing feel: re-launches after the first touchdown, the rebound's peak upward speed, bump stops reached,
                // and the sideways swing (heading against the path, yaw rate) in the 1.5 s after it.
                float landT = -1, airNow = 0, reboundVy = 0, slipMax = 0, yawMax = 0; int bounces = 0, stops = 0; bool landed = false, relaunch = false;
                for (float t = 0; t < 9f; t += Dt)
                {
                    car.SetControls(1f, 0f, SchoolScript.SlideSteer(car, track, ref h2), false, false);
                    car.Step(Dt);
                    int idx = SchoolScript.Follow(track, new Vector2(car.Pos.X, car.Pos.Z), ref h2);
                    if (car.Up.Y < minUp) { minUp = car.Up.Y; minUpAt = idx; }
                    float pitch = Mathf.RadToDeg(MathF.Asin(Math.Clamp(car.Forward.Y, -1f, 1f)));
                    if (MathF.Abs(pitch) > MathF.Abs(maxPitch)) { maxPitch = pitch; pitchAt = idx; }
                    bool inAir = true;
                    foreach (var w in car.Wheels) if (w.Contact) inAir = false;
                    air = inAir ? air + Dt : 0; maxAir = Math.Max(maxAir, air);
                    topKmh = Math.Max(topKmh, car.Speed * 3.6f);
                    airNow = inAir ? airNow + Dt : 0;
                    if (airNow > 0.3f) { landed = false; landT = -1; }
                    else if (!inAir && airNow == 0 && !landed && air == 0 && maxAir > 0.3f && landT < 0) { landed = true; landT = t; }
                    if (landed && t - landT < 1.5f)
                    {
                        foreach (var w in car.Wheels) if (w.Contact && w.Compression > car.P.MaxTravel) { stops++; break; }
                        reboundVy = Math.Max(reboundVy, car.Vel.Y);
                        if (inAir && t - landT > 0.05f && !relaunch) { bounces++; relaunch = true; }
                        if (!inAir) relaunch = false;
                        var hv2 = new Vector2(car.Vel.X, car.Vel.Z);
                        if (hv2.Length() > 3f) slipMax = Math.Max(slipMax, MathF.Abs(Mathf.RadToDeg(hv2.AngleTo(new Vector2(car.Forward.X, car.Forward.Z)))));
                        yawMax = Math.Max(yawMax, MathF.Abs(Mathf.RadToDeg(car.AngVel.Dot(car.Up))));
                    }
                    bool flipped = car.Up.Y < 0f;
                    if (flipped && !wasFlipped) flips++;
                    wasFlipped = flipped;
                    if (OS.GetEnvironment("JUMPTRACE") is { Length: > 0 } jt && kmh == float.Parse(jt) && idx >= int.Parse(OS.GetEnvironment("TRACEFROM") is { Length: > 0 } tf ? tf : "255") && idx <= int.Parse(OS.GetEnvironment("TRACETO") is { Length: > 0 } tt ? tt : "292") && (int)(t / Dt) % 3 == 0)
                    {
                        Vector3 wl = car.Rot.Transposed() * car.AngVel;
                        string c = ""; foreach (var w in car.Wheels) c += w.Contact ? "#" : ".";
                        GD.Print($"   t {t:F2} m {idx} pitch {pitch:F0} roll {Mathf.RadToDeg(MathF.Asin(Math.Clamp(car.Right.Y, -1f, 1f))):F0} wx {wl.X:F2} wy {wl.Y:F2} wz {wl.Z:F2} wheels {c} vy {car.Vel.Y:F1} kmh {car.Speed * 3.6f:F0} y {car.Pos.Y:F2} comp {car.Wheels[0].Compression:F2}/{car.Wheels[2].Compression:F2}");
                    }
                    if (idx > 400) break;
                }
                GD.Print($"== jump from {from} at {kmh} km/h: top {topKmh:F0} km/h, min up {minUp:F2} at {minUpAt}, max pitch {maxPitch:F0} deg at {pitchAt}, longest air {maxAir:F2} s, flips {flips} | landing: bounces {bounces}, rebound vy {reboundVy:F1} m/s, bump-stop frames {stops}, sideslip {slipMax:F0} deg, yaw {yawMax:F0} deg/s");
            }
            GetTree().Quit();
            return;
        }
        if (OS.GetEnvironment("ILYICHLAB") == "sig")
        {
            GD.Print($"  signature {TrackSignature.Compute(track)}");      // compare with user://track_<id>.sig: records stay valid
            GetTree().Quit();
            return;
        }
        if (OS.GetEnvironment("ILYICHLAB") == "map")
        {
            for (int i = 0; i < n; i += 10)
            {
                var d = track.CenterDir(i);
                float turn = Mathf.RadToDeg(d.AngleTo(track.CenterDir((i + 20) % n)));
                GD.Print($"  {i,4}: ({track.Center[i].X,6:F0},{track.Center[i].Y,6:F0}) dir ({d.X,5:F2},{d.Y,5:F2}) turn20 {turn,4:F0} {track.CenterSurf[i]} hw {track.HalfWidth[i]:F1}");
            }
            GetTree().Quit();
            return;
        }
        // The bends: total heading change over the next 40 m from each metre, the sharpest starts.
        for (int i = 480; i < 1000; i += 10)
        {
            float turn = Mathf.RadToDeg(track.CenterDir(i).AngleTo(track.CenterDir((i + 40) % n)));
            if (MathF.Abs(turn) > 35f) GD.Print($"  bend at {i}: {turn:F0} deg in 40 m, {track.CenterSurf[i]}, half width {track.HalfWidth[i]:F1}");
        }
        // (result, max metres over the edge, min km/h, heading change, technique seen)
        int hint = 0;
        float Steer(CarSim c) => SchoolScript.SlideSteer(c, track, ref hint);
        int back = OS.GetEnvironment("LABBACK") is { Length: > 0 } lb ? int.Parse(lb) : 45;      // env LABBACK: the run starts this far before the bend
        (float over, float minKmh, float turned, string tech, int reached) Run(int entry, float v0, int bp, Action<CarSim, float, int> drive)
        {
            hint = SchoolScript.Wrap(track, entry - back);
            var car = New();
            SchoolScript.SetRolling(car, track, SchoolScript.RoadPoint(track, entry - back, 0), track.CenterDir(entry - back), v0 / 3.6f);
            float t = 0, maxOver = -99, minK = 999, t0 = -1;
            float yaw0 = MathF.Atan2(car.Forward.X, car.Forward.Z), total = 0, prev = yaw0;
            string tech = "-";
            int idx = entry - back;
            for (int k = 0; t < 9f; t += Dt, k++)
            {
                if (k % 2 == 1) { car.Step(Dt); continue; }
                int fh = hint;
                idx = SchoolScript.Follow(track, new Vector2(car.Pos.X, car.Pos.Z), ref fh);
                Vector2 cp = new(car.Pos.X, car.Pos.Z);
                float over = cp.DistanceTo(track.Center[idx]) - track.HalfWidth[idx];
                int s = SchoolScript.Along(track, entry, idx);
                if (s > -5 && s < 60) { maxOver = Math.Max(maxOver, over); minK = Math.Min(minK, car.Speed * 3.6f); }
                if (s > 70) break;
                if (s < bp && t0 < 0) car.SetControls(car.ForwardSpeed * 3.6f < v0 ? 1f : 0f, 0, Steer(car), false, false);
                else { if (t0 < 0) t0 = t; drive(car, t - t0, s); }
                car.Step(Dt);
                float y = MathF.Atan2(car.Forward.X, car.Forward.Z);
                total += Mathf.AngleDifference(prev, y);
                prev = y;
                if (car.LastTechnique != null && tech == "-") tech = car.LastTechnique;
            }
            return (maxOver, minK, Mathf.RadToDeg(MathF.Abs(total)), tech, idx);
        }
        // Each technique at each bend: the timings that keep the car on the road (over < 1.5 m) through it.
        var entries = OS.GetEnvironment("LABENTRIES") is { Length: > 0 } le ? Array.ConvertAll(le.Split(','), int.Parse) : new[] { 500, 510, 620, 780, 785, 790 };   // env LABENTRIES=945,950: other bends
        foreach (int entry in entries)
        {
            Vector2 d0 = track.CenterDir(entry), d1 = track.CenterDir((entry + 25) % n);
            float side = MathF.Sign(-d0.AngleTo(d1));
            var speeds = OS.GetEnvironment("LABKMH") is { Length: > 0 } lkm ? Array.ConvertAll(lkm.Split(','), float.Parse) : entry >= 780 ? new[] { 40f, 45f, 50f } : new[] { 65f, 70f, 75f };   // env LABKMH=60,70
            foreach (float v0 in speeds)
            {
                var lines = new System.Collections.Generic.List<string>();
                var st = Run(entry, v0, -10, (c, t, s) => c.SetControls(1f, 0, Steer(c), false, false));
                lines.Add($"steer {st.over:F1} reached {st.reached}");
                var lk = Run(entry, v0, -14, (c, t, s) => c.SetControls(0, c.Speed * 3.6f > 4 ? 1f : 0f, Steer(c) > 0 ? 1f : -1f, false, false));
                var pu = Run(entry, v0, -16, (c, t, s) => { if (c.Speed * 3.6f > 38) c.SetControls(0, t % 0.3f < 0.17f ? 1f : 0f, Steer(c), false, false); else c.SetControls(1f, 0, Steer(c), false, false); });
                lines.Add($"locked {lk.over:F1} pump {pu.over:F1}/{pu.reached}");
                foreach (int bp in new[] { -12, -6, 0 })
                {
                    foreach (float hold in new[] { 0.25f, 0.45f })
                    {
                        var k = Run(entry, v0, bp, (c, t, s) => { c.Clutch = t < hold; c.SetControls(1f, 0, Steer(c), false, false); });
                        if (k.over < 1.5f && k.tech == "clutch" && SchoolScript.Along(track, entry, k.reached) > 50) lines.Add($"KICK bp{bp} {hold}: {k.over:F1} {k.minKmh:F0}kmh {k.turned:F0}deg");
                        var h = Run(entry, v0, bp, (c, t, s) => c.SetControls(t > hold + 0.2f ? 1f : 0f, 0, Steer(c), t < hold, false));
                        if (h.over < 1.5f && h.tech == "handbrake" && SchoolScript.Along(track, entry, h.reached) > 50) lines.Add($"HAND bp{bp} {hold}: {h.over:F1} {h.minKmh:F0}kmh {h.turned:F0}deg");
                        var fl = Run(entry, v0, bp - 8, (c, t, s) => c.SetControls(t > 0.3f + hold ? 1f : 0f, 0, t < 0.3f ? -side : Steer(c), false, false));
                        if (fl.over < 1.5f && fl.tech == "flick" && SchoolScript.Along(track, entry, fl.reached) > 50) lines.Add($"FLICK bp{bp - 8} lift {hold}: {fl.over:F1} {fl.minKmh:F0}kmh {fl.turned:F0}deg");
                        var fg = Run(entry, v0, bp - 8, (c, t, s) => c.SetControls(1f, 0, t < 0.3f + (hold - 0.25f) ? -side : Steer(c), false, false));
                        if (fg.over < 1.5f && fg.tech == "flick" && SchoolScript.Along(track, entry, fg.reached) > 50) lines.Add($"GASFLICK bp{bp - 8} away {0.3f + hold - 0.25f:F2}: {fg.over:F1} {fg.minKmh:F0}kmh {fg.turned:F0}deg");
                    }
                }
                GD.Print($"== {entry} {v0} km/h: " + string.Join(" | ", lines));
            }
        }
        GetTree().Quit();
    }
}
