using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>
    /// The collision is no longer flat (the owner's report of 2026-11-27: a car that jumped far over the dacha's stone walls still hit them): a low obstacle (Height above the ground) is passed by a car whose
    /// tyres are above its top, and still collides with one that is on the ground or only just over it; an obstacle that says no height is as tall as ever. The wooden fences are cleared the same way.
    /// </summary>
    void ObstacleHeightTest()
    {
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        int At = RamStraight(track);
        Vector2 spot = track.Center[At], dir = track.CenterDir(At);
        float ground = track.MeshHeight(spot.X, spot.Y);

        // One step of a car driving at 20 m/s along the road with its origin liftY above where it rests, onto an obstacle on its path (the nose circle meets it).
        (float speedAfter, long contacts) Drive(float liftY, float height, int source)
        {
            var obstacles = new List<Obstacle>(track.Obstacles);
            Vector2 wallAt = spot + dir * 2.3f;      // (just ahead of the nose circle, 1.3 m from the centre, and inside its reach)
            obstacles.Add(new Obstacle(wallAt, 0.45f, default, false, 0f, false, Material: ImpactMaterial.Stone, Source: source, Height: height));
            var car = new CarSim(new CarParams(), track) { Obstacles = obstacles };
            SchoolScript.SetRolling(car, track, spot, dir, 20f);
            car.Pos.Y = ground + 0.9f + liftY; car.Vel.Y = 0f;
            long before = car.ContactSerial;
            car.Step(1f / 120f);
            long mine = 0; foreach (var c in car.Contacts) if (c.Serial > before && c.Source == source) mine++;
            return (new Vector2(car.Vel.X, car.Vel.Z).Length(), mine);
        }

        // The hairpin's stone wall (Ilyich 732-853 m) is three runs that meet at 812 and 824 m; where two meet the wall must not step: the offset at the end of one is that at the start of the next.
        {
            var def = TrackDef.Load("res://tracks/ilyich.json"); var stones = new List<FenceDef>(); foreach (var f in def.Fences) if (f.Type == "stone" && f.From >= 700f && f.To <= 860f && f.Side == "left") stones.Add(f);
            bool joined = stones.Count >= 3; float worst = 0f;
            for (int k = 0; k + 1 < stones.Count; k++) if (Math.Abs(stones[k].To - stones[k + 1].From) < 1e-3f) worst = Math.Max(worst, Math.Abs(stones[k].OffsetAt(stones[k].To) - stones[k + 1].OffsetAt(stones[k + 1].From)));
            Check(joined && worst < 1e-3f && Math.Abs(stones[1].OffsetAt(818f) - 2.0f) < 1e-3f, $"hairpin wall: {stones.Count} runs, no step where they meet (worst {worst:F3} m), 2.0 m from the road edge at the apex");
        }
        var onGround = Drive(0f, TrackBuilder.StoneWallHeight, 4711);
        Check(onGround.contacts > 0, $"obstacle height: a car on the ground meets a low wall ({onGround.contacts} contacts)");
        var jumped = Drive(2.5f, TrackBuilder.StoneWallHeight, 4712);
        Check(jumped.contacts == 0 && jumped.speedAfter > 19f, $"obstacle height: a car 2.5 m up clears the wall without a touch (contacts {jumped.contacts}, {jumped.speedAfter:F1} m/s kept)");
        var grazing = Drive(0.3f, TrackBuilder.StoneWallHeight, 4713);
        Check(grazing.contacts > 0, $"obstacle height: a car only 0.3 m up, its tyres below the top, still hits the wall ({grazing.contacts} contacts)");
        var tall = Drive(2.5f, 0f, 4714);
        Check(tall.contacts > 0, $"obstacle height: an obstacle without a height (a tree, a house) is as tall as ever: a car 2.5 m up meets it ({tall.contacts} contacts)");

        // The stone walls of the track say their height; the dacha yard's walls are among them.
        int low = 0, all = 0; foreach (var seg in track.WallSegments) for (int k = 0; k < seg.Count; k++) { all++; if (Math.Abs(track.Obstacles[seg.First + k].Height - TrackBuilder.StoneWallHeight) < 1e-4f) low++; }
        Check(all > 0 && low == all, $"obstacle height: every one of the {all} stone wall obstacles says its height ({low})");

        // The wooden fence: a car in the air above it breaks nothing, a car on the ground breaks it.
        var fx = new TyreEffects(); AddChild(fx); fx.Init(track);
        var wood = new WoodFences(); AddChild(wood); wood.Init(track, fx, new StandardMaterial3D());
        FenceRun run = null; foreach (var r in track.Fences) if (r.Type == "wood" && r.Points.Count > 1) { run = r; break; }
        if (run != null)
        {
            Vector3 a = run.Points[0], b = run.Points[1]; Vector3 mid = (a + b) * .5f; Vector3 along = (b - a).Normalized();
            Vector3 across = new Vector3(-along.Z, 0, along.X);
            int Smashed(float liftY)
            {
                var car = new CarSim(new CarParams(), track);
                car.Reset(new Vector3(mid.X, mid.Y + 0.9f + liftY, mid.Z), MathF.Atan2(-across.X, -across.Z));
                car.Vel = across * 10f;
                wood.Reset();
                int was = wood.BrokenThisTick;      // (the counter runs on until the next Tick)
                wood.Smash(car);
                return wood.BrokenThisTick - was;
            }
            int onFence = Smashed(0f), over = Smashed(2.5f);
            Check(onFence > 0 && over == 0, $"obstacle height: a car on the ground breaks the wooden fence ({onFence} spans), one 2.5 m above it breaks nothing ({over})");
        }
        else Check(true, "obstacle height: (this track has no wooden run to try)");
        wood.QueueFree(); fx.QueueFree();
        ObstacleHeightPathsTest();
        ObstacleHeightActorsTest();
    }

    /// <summary>A ground with a step: height 0 below StepX, StepH from there on (an obstacle that stands on higher ground than the car).</summary>
    sealed class StepGround : IGround
    {
        public float StepX = 1000f, StepH = 0f;
        public GroundSample Sample(float x, float z) => new() { Height = x >= StepX ? StepH : 0f, Normal = Vector3.Up, Surface = SurfaceType.Concrete };
    }

    /// <summary>
    /// The vertical rule in every contact path of the car (the owner's order of 2026-11-27, Codex): one rule, CarSim.VerticallyClear (the tyres' underside and the roof against the other's bottom and top). Here it
    /// is tried on the unit table, on the real heights of the built things (the low props, the movers), on the collision, on the replay's smash, the fallen trunk's bump, the traffic cones and the LAN's car pairs,
    /// each: clear (nothing happens: no impulse, no impact, no break, no kick) and on the ground (the old hit stays).
    /// </summary>
    void ObstacleHeightPathsTest()
    {
        // 0. The rule on a table: tyres above the top, roof below the bottom, otherwise a touch; unknown or zero heights never clear.
        Check(CarSim.VerticallyClear(2f, 3.4f, 0f, 1f) && CarSim.VerticallyClear(-3f, -1f, 0f, 1f) && !CarSim.VerticallyClear(0.5f, 1.9f, 0f, 1f) && !CarSim.VerticallyClear(1f, 2.3f, 0f, 1f)
            && !CarSim.VerticallyClear(5f, 7f, 0f, 0f) && !CarSim.VerticallyClear(5f, 7f, float.NaN, 1f) && !CarSim.VerticallyClear(5f, 7f, 0f, float.NaN),
            "obstacle height: the vertical rule on a table (above the top and below the bottom clear; touching, zero or unknown heights never)");
        // The roof is the model's (the car's glb, with the rack and the wing, measured).
        var vis = CarVisual.Create(new CarParams(), new Color(.8f, .1f, .1f)); AddChild(vis);
        float modelTop = ModelBounds(vis).End.Y, modelBottom = ModelBounds(vis).Position.Y; vis.QueueFree();
        var probeCar = new CarSim(new CarParams(), new FlatGround()); probeCar.Reset(new Vector3(0, 0.55f, 0), 0f);
        Check(modelTop <= CarSim.RoofRise + 0.01f && modelTop >= CarSim.RoofRise - 0.15f && Math.Abs(probeCar.LowestY(0f) - (0.55f + modelBottom)) < 0.02f, $"obstacle height: the car's roof ({CarSim.RoofRise:F2} m over its origin) and tyres' underside are the model's ({modelTop:F2} up, {modelBottom:F2} down)");

        // 1. The low props: each constant is at least the top of its model as built, for every one on the real tracks.
        foreach (string id in new[] { "ilyich", "siberia", "bonus" })
        {
            var track = TrackBuilder.Build(TrackDef.Load($"res://tracks/{id}.json"));
            var root = Props.Build(track); AddChild(root);
            int checkedProps = 0, tooLow = 0; float worst = 0f; string which = "";
            foreach (var n in root.GetChildren())
            {
                if (n is not Node3D n3 || !n3.HasMeta("prop")) continue;
                var pi = track.Props[(int)n3.GetMeta("prop")];
                if (pi.Type is not ("rock" or "stump" or "hay" or "powercabinet")) continue;
                var at = new Vector2(pi.Position.X, pi.Position.Z); int obs = -1;
                for (int i = 0; i < track.Obstacles.Count && obs < 0; i++) if (track.Obstacles[i].Center.DistanceTo(at) < 0.05f && track.Obstacles[i].Height > 0f) obs = i;
                if (obs < 0) { if (pi.Type == "rock" && pi.Size < 0.8f) continue; tooLow++; which += $" {pi.Type}@{at}(no height)"; continue; }
                float top = ModelBounds(n3).End.Y * n3.Transform.Basis.Scale.Y; checkedProps++;
                if (track.Obstacles[obs].Height < top - 1e-3f) { tooLow++; worst = Math.Max(worst, top - track.Obstacles[obs].Height); which += $" {pi.Type}(top {top:F2} > {track.Obstacles[obs].Height:F2})"; }
            }
            Check(checkedProps > 0 && tooLow == 0, $"obstacle height: {id}: the {checkedProps} low props (rocks, stumps, bales, cabinets) are each as tall as their model ({tooLow} too low){(which.Length > 0 ? which[..Math.Min(which.Length, 160)] : "")}");
            root.QueueFree();
        }

        // 2. The movers: each circle of a vehicle is at least the vehicle's built height (the meshes of its actor near the circle, above the ground there; a contaminated upper bound).
        {
            var ilyich = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json")); var siberia = TrackBuilder.Build(TrackDef.Load("res://tracks/siberia.json")); var bonus = TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"));
            var fxI = new TyreEffects(); AddChild(fxI); fxI.Init(ilyich); var fxS = new TyreEffects(); AddChild(fxS); fxS.Init(siberia); var fxB = new TyreEffects(); AddChild(fxB); fxB.Init(bonus);
            void Measured(string name, Node actor, TrackData t, int first, float maxOver)
            {
                var meshes = new List<(Vector3 c, float hi)>();
                void Walk(Node x) { if (x is MeshInstance3D mi && mi.Mesh != null && mi.IsVisibleInTree()) { var bb = mi.GlobalTransform * mi.Mesh.GetAabb(); meshes.Add((bb.GetCenter(), bb.End.Y)); } foreach (var c in x.GetChildren()) Walk(c); }
                Walk(actor);
                int n = 0, low = 0, none = 0; float worst = 0f;
                for (int i = first; i < t.Obstacles.Count; i++)
                {
                    var o = t.Obstacles[i]; if (o.Center.X < -9000f) continue; n++;
                    float g = t.MeshHeight(o.Center.X, o.Center.Y), hi = g;
                    foreach (var m in meshes) if (new Vector2(m.c.X, m.c.Z).DistanceTo(o.Center) < o.Radius + 1.2f) hi = Math.Max(hi, m.hi);
                    if (o.Height <= 0f) { none++; continue; }
                    if (o.Height < hi - g - 0.01f) { low++; worst = Math.Max(worst, hi - g - o.Height); }
                }
                Check(n > 0 && low == 0 && none == 0, $"obstacle height: {name}: {n} circles, each at least the built height ({low} too low by up to {worst:F2} m, {none} without a height)");
            }
            int f0 = ilyich.Obstacles.Count; var tr = new DrunkTractor(); AddChild(tr); tr.Init(ilyich, fxI, ilyich.Center[100], 0f); Measured("tractor", tr, ilyich, f0, 0f);
            f0 = ilyich.Obstacles.Count; var sl = new SlurryTractor(); AddChild(sl); sl.Init(ilyich); Measured("slurry tractor", sl, ilyich, f0, 0f);
            f0 = ilyich.Obstacles.Count; var kz = new KozlikPatrol(); AddChild(kz); kz.Init(ilyich, fxI, null, null, 0); Measured("Kozlik", kz, ilyich, f0, 0f);
            f0 = ilyich.Obstacles.Count; var bus = new SovietBus(); AddChild(bus); bus.Init(ilyich, fxI, 0f, null, f0); Measured("bus", bus, ilyich, f0, 0f);
            f0 = siberia.Obstacles.Count; var pl = new SnowPloughs(); AddChild(pl); pl.Init(siberia, fxS, new List<PloughDef> { new() { Start = 100, Kind = "plough" } }); Measured("snowplough", pl, siberia, f0, 0f);
            f0 = siberia.Obstacles.Count; var mx = new SnowPloughs(); AddChild(mx); mx.Init(siberia, fxS, new List<PloughDef> { new() { Start = 100, Kind = "mixer" } }); Measured("concrete mixer", mx, siberia, f0, 0f);
            var trg = bonus.TankRange; f0 = bonus.Obstacles.Count; var tanks = new TankBattle(); AddChild(tanks); tanks.Init(bonus, fxB, new Rect2(trg.X0, trg.Z0, trg.X1 - trg.X0, trg.Z1 - trg.Z0), 1); tanks.Tick(1f / 60f, null); Measured("tank", tanks, bonus, f0, 0f);
            f0 = bonus.Obstacles.Count; var btr = new BtrPatrols(); AddChild(btr); btr.Init(bonus, fxB); Measured("BTR", btr, bonus, f0, 0f);
            foreach (Node nd in new Node[] { tr, sl, kz, bus, pl, mx, tanks, btr, fxI, fxS, fxB }) nd.QueueFree();
        }

        // 3. The collision: a mover-sized obstacle (3.5 m) moving across the car's path. In the air above it: no touch, nothing changes; on the ground: the old hit; terrain: an obstacle on higher ground still hits.
        {
            var ground = new StepGround(); var p = new CarParams();
            (float impact, Vector3 vel, Vector3 ang, long contacts, Vector3 pos) Hit(StepGround g, float carY, Obstacle o)
            {
                var car = new CarSim(p, g) { Obstacles = new List<Obstacle> { o } };
                car.Reset(new Vector3(100f, carY, 100f), 0f); car.Vel = new Vector3(0f, 0f, -12f);      // (heading -Z)
                Vector3 v0 = car.Vel; long before = car.ContactSerial; float impact = 0f;
                for (int i = 0; i < 12; i++) { car.Step(1f / 120f); impact = Math.Max(impact, car.LastImpact); }
                long mine = 0; foreach (var c in car.Contacts) if (c.Serial > before) mine++;
                return (impact, car.Vel - v0, car.AngVel, mine, car.Pos);
            }
            var mover = new Obstacle(new Vector2(100f, 97.7f), 1.3f, new Vector2(0f, 3f), Material: ImpactMaterial.Metal, Source: 4801, Height: ObstacleHeights.Tractor);
            var onGround = Hit(ground, 0.55f, mover); var inAir = Hit(ground, 4.8f, mover);
            Check(onGround.impact > 1f && onGround.contacts > 0, $"obstacle height: a car on the ground hits a tractor-sized mover (impact {onGround.impact:F1} m/s)");
            Check(inAir.impact == 0f && inAir.contacts == 0 && inAir.ang.Length() < 1e-3f && inAir.pos.Z < 100f - 1f, $"obstacle height: a car 4.3 m above the ground clears it: no impulse, no impact, no spin (impact {inAir.impact:F2}, contacts {inAir.contacts})");
            var low = Hit(ground, 2.2f, mover);
            Check(low.impact > 0f, $"obstacle height: a car only 1.7 m up, tyres below its top, still hits it ({low.impact:F1} m/s)");
            // The obstacle stands on ground 2 m higher than the car's (the step is at x = 99): the car at 4 m is above the car's ground by 3.4 m but only 1.4 m above the obstacle's top... it still hits.
            var stepped = new StepGround { StepX = 99.5f, StepH = 2.0f };
            var terrain = Hit(stepped, 3.6f, new Obstacle(new Vector2(100f, 97.7f), 1.3f, default, Material: ImpactMaterial.Metal, Height: 2.0f));
            var terrainFlat = Hit(ground, 3.6f, new Obstacle(new Vector2(100f, 97.7f), 1.3f, default, Material: ImpactMaterial.Metal, Height: 2.0f));
            Check(terrain.impact > 0f && terrainFlat.impact == 0f, $"obstacle height: an obstacle on 2 m higher ground still hits a car that would clear it on level ground (hit {terrain.impact:F1} m/s, level ground {terrainFlat.impact:F1})");
            // A thing that hangs above the road (Base 3 m up, 1 m tall): the car under it passes beneath, on the ground and its roof 1.9 m up.
            var hanging = Hit(ground, 0.55f, new Obstacle(new Vector2(100f, 97.7f), 1.3f, default, Height: 1.0f, Base: 3.0f));
            var hangingLow = Hit(ground, 2.2f, new Obstacle(new Vector2(100f, 97.7f), 1.3f, default, Height: 1.0f, Base: 3.0f));
            Check(hanging.impact == 0f && hangingLow.impact > 0f, $"obstacle height: a car passes under a thing that hangs 3 m up (impact {hanging.impact:F1}) but not when its roof reaches it ({hangingLow.impact:F1})");
            // No height, no clearing: the old behaviour for anything that says none (a tree, a house).
            var tall = Hit(ground, 4.8f, new Obstacle(new Vector2(100f, 97.7f), 1.3f, default));
            Check(tall.impact > 0f, $"obstacle height: an obstacle that says no height is hit from any height as ever ({tall.impact:F1} m/s)");
            // 4. Landing on top: the car comes down on a 4.3 m mover from 6 m up: no NaN, no throw (the displacement of any step is bounded) and no endless sinking.
            var landCar = new CarSim(p, ground) { Obstacles = new List<Obstacle> { new Obstacle(new Vector2(100f, 100f), 1.8f, default, Material: ImpactMaterial.Metal, Source: 4802, Height: ObstacleHeights.Tank) } };
            landCar.Reset(new Vector3(100f, 6f, 100.6f), 0f); landCar.Vel = Vector3.Zero;
            float maxStep = 0f; bool finite = true;
            for (int i = 0; i < 240; i++)
            {
                Vector3 before = landCar.Pos; landCar.Step(1f / 120f);
                maxStep = Math.Max(maxStep, new Vector2(landCar.Pos.X - before.X, landCar.Pos.Z - before.Z).Length());
                finite &= float.IsFinite(landCar.Pos.X + landCar.Pos.Y + landCar.Pos.Z + landCar.Vel.X + landCar.Vel.Y + landCar.Vel.Z);
            }
            Check(finite && maxStep < 0.6f, $"obstacle height: a car landing on top of a 4.3 m tank-sized body stays finite and is eased off, never thrown across it (largest sideways step {maxStep:F2} m, wide {2f * 1.8f:F1} m)");
        }

        // 5. The replay's smash: the title replay's car (a pose, no collision) breaks what it touches; a bale under a car in the air is not broken, one it touches is.
        {
            var track = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json")); var root = Props.Build(track); AddChild(root);
            var br = new Breakables(); br.Init(track, null, root, null); AddChild(br);
            int hay = -1; for (int i = 0; i < track.Obstacles.Count && hay < 0; i++) if (track.Obstacles[i].Height == TrackBuilder.HayHeight && track.Obstacles[i].Crumbles) hay = i;
            Check(hay >= 0, "obstacle height: Ilyich has hay bales that give way");
            if (hay >= 0)
            {
                bool Smashes(float lift)
                {
                    var o = track.Obstacles[hay]; var g = track.MeshHeight(o.Center.X, o.Center.Y);
                    var car = new CarSim(new CarParams(), track); car.Reset(new Vector3(o.Center.X, g + 0.55f + lift, o.Center.Y), 0f); car.Vel = new Vector3(0f, 0f, -14f);
                    for (int k = 0; k < 8; k++) br.Smash(car, 14f);
                    bool gone = track.Obstacles[hay].Center.X < -9000f;
                    return gone;
                }
                bool ground = Smashes(0f);
                // (a fresh world for the second try: the bale of the first is gone)
                var track2 = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json")); var root2 = Props.Build(track2); AddChild(root2);
                var br2 = new Breakables(); br2.Init(track2, null, root2, null); AddChild(br2);
                var o2 = track2.Obstacles[hay]; float g2 = track2.MeshHeight(o2.Center.X, o2.Center.Y);
                var car2 = new CarSim(new CarParams(), track2); car2.Reset(new Vector3(o2.Center.X, g2 + 0.55f + 3.5f, o2.Center.Y), 0f); car2.Vel = new Vector3(0f, 0f, -14f);
                for (int k = 0; k < 8; k++) br2.Smash(car2, 14f);
                bool air = track2.Obstacles[hay].Center.X < -9000f;
                Check(ground && !air, $"obstacle height: the replay's smash breaks a bale the car touches ({ground}) and leaves one under a car in the air ({!air})");
                root2.QueueFree(); br2.QueueFree();
            }
            root.QueueFree(); br.QueueFree();
        }

        // 6. A fallen trunk's bump (Breakables.Jolts): a wheel pair rolling over it is kicked; an axle in the air above it is not.
        {
            const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic;
            var ground = new FlatGround(); var p = new CarParams();
            float Kick(bool airborne)
            {
                var br = new Breakables(); AddChild(br);
                var logs = (List<(Vector2 a, Vector2 b, float h)>)typeof(Breakables).GetField("_logs", f).GetValue(br);
                logs.Add((new Vector2(97f, 99.5f), new Vector2(103f, 99.5f), 1f));
                var car = new CarSim(p, ground); car.Reset(new Vector3(100f, airborne ? 3f : 0.55f, 102f), 0f); car.Vel = new Vector3(0f, 0f, -12f);
                car.Step(1f / 240f);      // (the wheels learn whether they touch the ground)
                br.Tick(1f / 60f, car, true);
                car.Pos = new Vector3(100f, car.Pos.Y, 100f);      // across the log's line (heading -Z: the front axle goes first)
                float vy = car.Vel.Y; br.Tick(1f / 60f, car, true);
                float kick = car.Vel.Y - vy; br.QueueFree(); return kick;
            }
            float onGround = Kick(false), inAir = Kick(true);
            Check(onGround > 0.1f && Math.Abs(inAir) < 1e-4f, $"obstacle height: a car rolling over a fallen trunk is kicked up ({onGround:F2} m/s), one in the air above it is not ({inAir:F2})");
        }

        // 7. The traffic cones (Siberia's ice road): a cone knocked by a car on the road, not by one above its top.
        {
            var track = TrackBuilder.Build(TrackDef.Load("res://tracks/siberia.json"));
            const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic;
            int Knocked(float lift)
            {
                var cones = TrafficCones.Build(track); AddChild(cones);
                var list = (System.Collections.IList)typeof(TrafficCones).GetField("_cones", f).GetValue(cones);
                object cone = list[0]; var ct = cone.GetType(); var home = (Vector3)ct.GetField("Home").GetValue(cone);
                var car = new CarSim(new CarParams(), track); car.Reset(new Vector3(home.X, home.Y + 0.55f + lift, home.Z), 0f); car.Vel = new Vector3(0f, 0f, -12f);
                cones.Tick(1f / 60f, car);
                int down = 0; foreach (object c in list) if ((bool)ct.GetField("Down").GetValue(c)) down++;
                cones.QueueFree(); return down;
            }
            int onRoad = Knocked(0f), above = Knocked(3.5f);
            Check(onRoad > 0 && above == 0, $"obstacle height: a cone is knocked by a car on the road ({onRoad}) and not by one 3.5 m above its top ({above})");
        }

        // 8. The LAN's car pairs (CarContacts): the tyres and the roof of each against the other's, per pair of body circles; the old 1.45 m centre gap let a car 1.5-2.1 m above another through.
        {
            var p = new CarParams(); var ground = new FlatGround();
            CarSim Make(Vector3 pos, Vector3 vel) { var c = new CarSim(p, ground); c.Reset(pos, 0f); c.Vel = vel; return c; }
            float Pair(float dy, out Vector3 aVel)
            {
                var a = Make(new Vector3(100f, 0.55f + dy, 100f), new Vector3(10f, 0f, 0f)); var b = Make(new Vector3(101.7f, 0.55f, 100f), Vector3.Zero);
                float hit = CarContacts.Resolve(a, b); aVel = a.Vel; return hit;
            }
            float level = Pair(0f, out var vLevel), close = Pair(1.3f, out _), over = Pair(1.8f, out var vOver), above = Pair(2.4f, out var vAbove);
            Check(level > 5f && vLevel.X < 10f && close > 0f && over > 0f && above == 0f && vAbove.X == 10f,
                $"obstacle height: LAN pairs: level cars hit ({level:F1}), 1.3 m and 1.8 m above still overlap the roof and hit ({close:F1}, {over:F1}), 2.4 m above clears without an impulse ({above:F1}, speed kept {vAbove.X:F1})");
            var pred = Make(new Vector3(100f, 0.55f + 2.4f, 100f), new Vector3(10f, 0f, 0f)); var remote = Make(new Vector3(101.7f, 0.55f, 100f), Vector3.Zero); Vector3 at = pred.Pos;
            float predicted = CarContacts.Predict(pred, remote);
            Check(predicted == 0f && pred.Pos == at, "obstacle height: LAN prediction against a remote car beneath is the same rule: no hit, no move");
        }
    }

    /// <summary>One step of a car at 12 m/s (heading -Z) at height carY (origin above flat ground) into a copy of this obstacle standing at (100, 97.7): the impact it makes and the change of its spin.</summary>
    static (float impact, float spin) FlatHitOf(Obstacle o, float carY)
    {
        var copy = o with { Center = new Vector2(100f, 97.7f), Velocity = default, Base = float.IsNaN(o.Base) ? float.NaN : o.Base };
        var car = new CarSim(new CarParams(), new FlatGround()) { Obstacles = new List<Obstacle> { copy } };
        car.Reset(new Vector3(100f, carY, 100f), 0f); car.Vel = new Vector3(0f, 0f, -12f);
        float impact = 0f;
        for (int i = 0; i < 12; i++) { car.Step(1f / 120f); impact = Math.Max(impact, car.LastImpact); }
        return (impact, car.AngVel.Length());
    }

    /// <summary>
    /// The active bodies that a jump can clear, each by name (Codex, 2026-11-28): every circle it puts on the ground has a height at least that of its built model (the meshes of the actor round the circle), the car on the
    /// ground hits it as ever, a car above its top passes with no impact and no spin. A thing that is away is parked at (-1e4, -1e4) and never in reach; the rest that says no height says so on purpose (ObstacleHeights).
    /// </summary>
    void ObstacleHeightActorsTest()
    {
        var ily = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json")); var siberia = TrackBuilder.Build(TrackDef.Load("res://tracks/siberia.json"));
        var bonus = TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json")); var atom = TrackBuilder.Build(TrackDef.Load("res://tracks/atomgrad.json")); var adef = TrackDef.Load("res://tracks/atomgrad.json");
        var fxI = new TyreEffects(); AddChild(fxI); fxI.Init(ily); var fxS = new TyreEffects(); AddChild(fxS); fxS.Init(siberia); var fxB = new TyreEffects(); AddChild(fxB); fxB.Init(bonus); var fxA = new TyreEffects(); AddChild(fxA); fxA.Init(atom);
        var made = new List<Node>();
        void Named(string name, Node actor, TrackData t, int first, IEnumerable<int> only = null)
        {
            var meshes = new List<(Vector3 c, float hi)>();
            void Walk(Node x) { if (x is MeshInstance3D mi && mi.Mesh != null && mi.IsVisibleInTree()) { var bb = mi.GlobalTransform * mi.Mesh.GetAabb(); meshes.Add((bb.GetCenter(), bb.End.Y)); } foreach (var c in x.GetChildren()) Walk(c); }
            if (actor != null) Walk(actor);
            var indices = only != null ? only.ToList() : Enumerable.Range(first, t.Obstacles.Count - first).ToList();
            int n = 0, noHeight = 0, tooLow = 0; float worst = 0f; Obstacle? sample = null;
            foreach (int i in indices)
            {
                var o = t.Obstacles[i]; if (o.Center.X < -9000f) continue; n++;
                float g = t.MeshHeight(o.Center.X, o.Center.Y), hi = g;
                foreach (var m in meshes) if (new Vector2(m.c.X, m.c.Z).DistanceTo(o.Center) < o.Radius + 1.2f) hi = Math.Max(hi, m.hi);
                if (o.Height <= 0f) { noHeight++; continue; }
                sample ??= o;
                if (o.Height < hi - g - 0.01f) { tooLow++; worst = Math.Max(worst, hi - g - o.Height); }
            }
            bool bounds = n > 0 && noHeight == 0 && tooLow == 0;
            Check(bounds, $"obstacle height: {name}: {n} circles, each with a height at least its model's ({noHeight} without, {tooLow} too low by up to {worst:F2} m)");
            if (sample is Obstacle s0)
            {
                var onGround = FlatHitOf(s0, 0.55f); var over = FlatHitOf(s0, s0.Height + 0.76f + 0.8f);
                Check(onGround.impact > 1f && over.impact == 0f && over.spin < 1e-3f, $"obstacle height: {name}: hit by a car on the ground ({onGround.impact:F1} m/s), cleared by one {s0.Height + 0.8f:F1} m up with no impact, no spin ({over.impact:F1}, {over.spin:F3})");
            }
        }
        // The prisoners and their guards (Siberia's column), after a tick: the circles they put on the road.
        { int f = siberia.Obstacles.Count; var col = new PrisonerColumn(); AddChild(col); col.Init(siberia, 100f, 6); col.Tick(1f / 60f, null); Named("prisoner column", col, siberia, f); made.Add(col); }
        // The burnt bus in the ditch: its wreck circles after the fire starts.
        {
            int f = ily.Obstacles.Count; var bus = new SovietBus(); AddChild(bus); bus.Init(ily, fxI, 0f, null, f);
            typeof(SovietBus).GetMethod("Ignite", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(bus, null);
            var wreckObs = (int[])typeof(SovietBus).GetField("_wreckObs", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bus);
            Named("bus wreck", bus, ily, f, wreckObs); made.Add(bus);
        }
        // A Hind on the ground (Poligon), the Buk's launchers, support and loader.
        {
            int f = bonus.Obstacles.Count; var hind = new HindSorties(); AddChild(hind); hind.Init(bonus, fxB); hind.Tick(1f / 60f, null);
            var hindObs = Enumerable.Range(f, 15).ToList(); Named("Hind on the ground", hind, bonus, f, hindObs); made.Add(hind);
            Named("Buk launchers, support and loader", hind, bonus, f + 15); 
        }
        // The Lunokhod and the train (Atomgrad's zone).
        { int f = atom.Obstacles.Count; var zone = new ZoneLife(); AddChild(zone); zone.Init(atom, fxA, adef.Zone, f); zone.Tick(1f / 60f, null); Named("Lunokhod and train", zone, atom, f, Enumerable.Range(f, atom.Obstacles.Count - f).Where(i => atom.Obstacles[i].Radius > 1.4f)); made.Add(zone); }
        // The camp gate, the depot's tank, the hen coop, the speed trap's booth and sign.
        { int f = bonus.Obstacles.Count; var gate = new CampGate(); AddChild(gate); gate.Init(bonus); Named("camp gate", gate, bonus, f); made.Add(gate); }
        { int f = bonus.Obstacles.Count; var depot = new FuelDepot(); AddChild(depot); depot.Init(bonus); Named("fuel depot tank", depot, bonus, f); made.Add(depot); }
        { int f = ily.Obstacles.Count; var coop = VillageLife.Build(ily); AddChild(coop); Named("hen coop", coop, ily, f); made.Add(coop); }
        { int f = siberia.Obstacles.Count; var trap = new SpeedTrap(); AddChild(trap); trap.Init(siberia, TrackDef.Load("res://tracks/siberia.json").SpeedTrap); Named("speed trap booth and sign", trap, siberia, f + 1); made.Add(trap); }
        // The MiK-21s on the ground (Siberia's airbase) and the dosimetrist (Atomgrad).
        {
            // The MiK-21s are in the air or away at the start (their circles come with the sorties); the constant is the airbase prop's own model, a MiK on the ground.
            var root = Props.Build(siberia); AddChild(root); float top = 0f;
            foreach (var n in root.GetChildren()) if (n is Node3D n3 && n3.HasMeta("prop") && siberia.Props[(int)n3.GetMeta("prop")].Type == "balalaika") top = Math.Max(top, ModelBounds(n3).End.Y * n3.Transform.Basis.Scale.Y);
            Check(top > 1f && top <= ObstacleHeights.Jet, $"obstacle height: MiK on the ground: the built jet is {top:F2} m, the constant {ObstacleHeights.Jet:F1} m");
            var onGround = FlatHitOf(new Obstacle(Vector2.Zero, 2f, default, Height: ObstacleHeights.Jet), 0.55f); var over = FlatHitOf(new Obstacle(Vector2.Zero, 2f, default, Height: ObstacleHeights.Jet), ObstacleHeights.Jet + 1.6f);
            Check(onGround.impact > 1f && over.impact == 0f && over.spin < 1e-3f, $"obstacle height: MiK on the ground: hit from the ground ({onGround.impact:F1}), cleared above ({over.impact:F1})");
            root.QueueFree();
        }
        { int f = atom.Obstacles.Count; var dosi = ZoneDosimetrist.Build(atom); AddChild(dosi); Named("dosimetrist", dosi, atom, f); made.Add(dosi); }
        foreach (var nd in made) nd.QueueFree();
        fxI.QueueFree(); fxS.QueueFree(); fxB.QueueFree(); fxA.QueueFree();
    }
}
