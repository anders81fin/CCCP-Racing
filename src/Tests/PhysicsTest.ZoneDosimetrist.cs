using System;
using System.Linq;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>The dosimetrist's check: he turns to the car where it stands, sweeps the probe, glances at the meter twice and goes back to rest; a restart, a rescue (the car far away) and a knock-over bring him back too.</summary>
    void ZoneDosimetristTest()
    {
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/atomgrad.json"));
        var man = ZoneDosimetrist.Build(track);
        AddChild(man);
        var figure = man.GetChild<Node3D>(0);
        var probe = figure.GetNode<Node3D>("ProbeArm"); var meter = figure.GetNode<Node3D>("MeterArm"); var head = figure.GetNode<Node3D>("Head");
        Vector3 at = figure.GlobalPosition;
        Vector3 car = at + new Vector3(5f, 0, -3f);       // (a car on the road a few metres off)
        const float dt = 1f / 60f, total = 10f;
        Vector2 restFront = new Vector2(figure.GlobalBasis.Z.X, figure.GlobalBasis.Z.Z).Normalized();
        bool rest = Math.Abs(probe.Rotation.X) + Math.Abs(meter.Rotation.X) + Math.Abs(head.Rotation.X) < 1e-4f;
        float faced = 0, probeMax = 0, meterMax = 0, headMax = 0; int glances = 0; bool inGlance = false;
        for (float e = 0; e < total; e += dt)
        {
            man.Inspect(e, total, car); man._Process(dt);
            if (e > 2f && e < 5f) { Vector2 front = new Vector2(figure.GlobalBasis.Z.X, figure.GlobalBasis.Z.Z).Normalized(), want = new Vector2(car.X - at.X, car.Z - at.Z).Normalized(); faced = Math.Max(faced, front.Dot(want)); }
            probeMax = Math.Max(probeMax, probe.Rotation.X); meterMax = Math.Max(meterMax, -meter.Rotation.X); headMax = Math.Max(headMax, head.Rotation.X);
            bool up = -meter.Rotation.X > 0.8f;
            if (up && !inGlance) glances++;
            inGlance = up;
        }
        for (float e = total; e < total + 1.5f; e += dt) { man.Inspect(e, total, car); man._Process(dt); }
        bool back = Math.Abs(probe.Rotation.X) < 0.05f && Math.Abs(meter.Rotation.X) < 0.05f && Math.Abs(head.Rotation.X) < 0.05f && new Vector2(figure.GlobalBasis.Z.X, figure.GlobalBasis.Z.Z).Normalized().Dot(restFront) > 0.999f;
        Check(rest && faced > 0.9f && probeMax > 0.4f && meterMax > 1f && headMax > 0.4f && glances == 2 && back,
            $"dosimetrist check: at rest, turns to the car ({faced:F2}), probe down {probeMax:F2} rad, two glances at the meter ({glances}, arm {meterMax:F2}, head {headMax:F2}), back to rest after the check ({back})");

        // A restart in the middle snaps him to rest; a car taken far away (a rescue) and a knock-over send him back to rest without any probe held out at nothing.
        for (float e = 0; e < 4f; e += dt) { man.Inspect(e, total, car); man._Process(dt); }
        bool busy = probe.Rotation.X > 0.3f;
        man.Reset();
        bool snapped = Math.Abs(probe.Rotation.X) < 1e-4f && Math.Abs(meter.Rotation.X) < 1e-4f;
        for (float e = 0; e < 4f; e += dt) { man.Inspect(e, total, car); man._Process(dt); }
        for (float e = 4f; e < 7f; e += dt) { man.Inspect(e, total, at + new Vector3(80f, 0, 0)); man._Process(dt); }
        bool released = Math.Abs(probe.Rotation.X) < 0.05f;
        man.Reset();
        for (float e = 0; e < 2f; e += dt) { man.Inspect(e, total, car); man._Process(dt); }
        foreach (var (_, hit) in man.Knocks()) hit(new Vector3(2f, 0, 0));
        for (float e = 2f; e < 4f; e += dt) { man.Inspect(e, total, car); man._Process(dt); }
        bool downQuiet = Math.Abs(probe.Rotation.X) < 0.1f;
        Check(busy && snapped && released && downQuiet, $"dosimetrist check: a restart snaps him to rest ({snapped}), a car taken away ({released}) or a knock-over ({downQuiet}) puts the probe down");
        man.QueueFree();
        ZoneDosimetristTapeTest(track);
    }

    sealed class FxCollector : CCCPRacing.Race.ITapeFxHost
    {
        public readonly System.Collections.Generic.List<CCCPRacing.Race.TapeEvent> Events = new();
        public void Fx(in CCCPRacing.Race.TapeEvent e) => Events.Add(e);
    }

    /// <summary>The dosimetrist is a tape actor (None:Dosimetrist): the check recorded, saved and replayed after seeks gives the same turn, probe, meter glances and wash flag; while the replay shows him his _Process writes nothing and his obstacle is parked; an old-version actor stays live.</summary>
    void ZoneDosimetristTapeTest(TrackData track)
    {
        const string key = "None:Dosimetrist";
        ZoneDosimetrist Make() { var m = ZoneDosimetrist.Build(track); AddChild(m); return m; }
        var source = Make();
        var figure = source.GetChild<Node3D>(0); var probe = figure.GetNode<Node3D>("ProbeArm"); var meter = figure.GetNode<Node3D>("MeterArm");
        var rec = new CCCPRacing.Race.WorldTapeRecorder(); rec.Begin(new CCCPRacing.Race.WorldTapeIndex(new[] { (key, (Node3D)source) })); rec.Record(0);
        Vector3 car = figure.GlobalPosition + new Vector3(5f, 0, -3f);
        const float dt = 1f / 60f;
        var seen = new System.Collections.Generic.Dictionary<int, (float probe, float meter, bool wash)>();
        for (int frame = 1; frame <= 60 * 12; frame++)
        {
            float e = frame * dt;
            source.Inspect(e < 10f ? e : -1f, 10f, car); source._Process(dt);
            source.SetWash(e > 1f && e < 3f, car);
            rec.Record(e);
            if (frame % 4 == 0) seen[frame] = (probe.Rotation.X, meter.Rotation.X, source.WashFlag.Visible);
        }
        var loaded = CCCPRacing.Race.WorldTapeCodec.Decode(CCCPRacing.Race.WorldTapeCodec.Encode(rec.Finish(12, 1UL)));
        var replay = Make();
        var rProbe = replay.GetChild<Node3D>(0).GetNode<Node3D>("ProbeArm"); var rMeter = replay.GetChild<Node3D>(0).GetNode<Node3D>("MeterArm");
        var player = new CCCPRacing.Race.WorldTapePlayer(loaded, new CCCPRacing.Race.WorldTapeIndex(new[] { (key, (Node3D)replay) }), new TapeSink());
        bool covered = player.Covers(key);
        int parkedBefore = track.Obstacles.Count(o => o.Center.X < -9000f);
        replay.SetTapeDriven(true);
        float worst = 0, probeSeen = 0, washShown = 0; bool washRight = true, parked = track.Obstacles.Count(o => o.Center.X < -9000f) == parkedBefore + 1;
        foreach (int frame in new[] { 90, 150, 220, 300, 420, 150, 90, 700 })      // seeks forward, back and on
        {
            player.Seek(frame * dt);
            replay._Process(1.0);      // (tape-driven: this must not write a pose)
            var want = seen[frame - frame % 4];
            worst = Math.Max(worst, Math.Max(Math.Abs(want.probe - rProbe.Rotation.X), Math.Abs(want.meter - rMeter.Rotation.X)));
            probeSeen = Math.Max(probeSeen, rProbe.Rotation.X);
            washRight &= want.wash == replay.WashFlag.Visible; if (want.wash) washShown++;
        }
        replay.SetTapeDriven(false);
        bool released = Math.Abs(rProbe.Rotation.X) < 1e-4f && track.Obstacles.Count(o => o.Center.X < -9000f) == parkedBefore;      // (leaving the replay: at rest, his obstacle back)
        var old = Make(); old.SetMeta(CCCPRacing.Race.WorldTapeConst.VersionMeta, 0);
        var stale = new CCCPRacing.Race.WorldTapePlayer(loaded, new CCCPRacing.Race.WorldTapeIndex(new[] { (key, (Node3D)old) }), new TapeSink());
        Check(covered && worst < 0.3f && probeSeen > 0.3f && washRight && washShown >= 1 && parked && released && !stale.Covers(key),
            $"dosimetrist tape: the check replays after a file round trip and seeks (worst {worst:F3} rad, probe down {probeSeen:F2}, wash flag matches in {washShown:F0} washing samples), his obstacle is parked ({parked}) and back after ({released}); an old-version actor is not covered ({!stale.Covers(key)})");
        // The hose over the car in the wash bay in a replay: the emitters make the spray from the flag while it is visible, nothing when it is hidden.
        var fxe = new TyreEffects(); AddChild(fxe); fxe.Init(track);
        var emitters = new TapeEmitters(fxe, track); emitters.Add(replay.WashFlag, TapeEmitters.Kind.Wash);
        replay.WashFlag.GlobalPosition = car;
        replay.WashFlag.Visible = false;
        for (int i = 0; i < 120; i++) emitters.Tick(dt);
        int hidden = fxe.ReplayParticles;
        replay.WashFlag.Visible = true;
        for (int i = 0; i < 120; i++) emitters.Tick(dt);
        int washing = fxe.ReplayParticles;
        Check(hidden == 0 && washing > 30 && emitters.Made.TryGetValue("wash-spray", out int made) && made > 30,
            $"dosimetrist wash tape: the replay's emitters make the hose's spray from the wash flag while it is visible ({washing} drops in 2 s) and none while it is hidden ({hidden})");
        fxe.QueueFree();
        source.QueueFree(); replay.QueueFree(); old.QueueFree();
    }
}

public partial class PhysicsTest
{
    /// <summary>Development measurement (--horse-feet): how far each hoof floats above the ground and slides along it while the leg is in stance (the hip angle going back), walking and trotting.</summary>
    void HorseFeetMeasure()
    {
        var (t, def, zone, _) = HorseWorld();
        var drive = new HorseDrive(t); float dt = 1f / 120f;
        var prevHip = new float[5, 4]; var prevFoot = new Vector3[5, 4];
        float[] floatMax = new float[2], slipMax = new float[2], slipMean = new float[2]; int[] n = new int[2];
        for (int tick = 0; tick < 120 * 80; tick++)
        {
            bool chase = tick >= 120 * 40;
            if (chase) drive.Step(dt, 262f);
            zone.Tick(dt, chase ? drive.Car : null);
            int mode = chase ? 1 : 0;
            for (int i = 0; i < zone.HorseCount; i++)
            for (int l = 0; l < 4; l++)
            {
                float hip = zone.HorseHip(i, l).Rotation.X;
                var foot = zone.HorseShin(i, l).GlobalTransform * new Vector3(0, -0.44f, 0.01f);
                if (zone.HorseSpeed(i) > (chase ? 4.5f : 0.5f) && tick > 3)
                {
                    float height = foot.Y - t.MeshHeight(foot.X, foot.Z);
                    float speed = new Vector2(foot.X - prevFoot[i, l].X, foot.Z - prevFoot[i, l].Z).Length() / dt;
                    if (speed < 0.3f) floatMax[mode] = MathF.Max(floatMax[mode], height);      // a hoof standing still in the world is in stance: how high does it hover?
                    if (height < 0.008f) { slipMax[mode] = MathF.Max(slipMax[mode], speed); slipMean[mode] += speed; n[mode]++; }      // a hoof on the ground: how fast does it slide?
                }
                prevHip[i, l] = hip; prevFoot[i, l] = foot;
            }
        }
        GD.Print($"HORSEFEET walk: float max {floatMax[0]:F3} m, hoof speed in stance max {slipMax[0]:F2} mean {slipMean[0] / Math.Max(1, n[0]):F2} m/s ({n[0]}); trot: float max {floatMax[1]:F3} m, speed max {slipMax[1]:F2} mean {slipMean[1] / Math.Max(1, n[1]):F2} m/s ({n[1]})");
        zone.QueueFree();
    }
}

public partial class PhysicsTest
{
    sealed class FakeExportSource : CCCPRacing.Export.IExportSource
    {
        public string Kind => "fake"; public string Title => "fake"; public string TrackId => "ilyich"; public float DurationSeconds => 80f;
        public CCCPRacing.Export.ExportStaged Stage(string sourceDir) => new("x", System.Array.Empty<string>(), 0);
    }

    /// <summary>The export card offers an active IN/OUT cut first (the player can still pick the whole lap, and that choice is not carried over), the whole lap when there is no cut, and says 60 FPS at normal speed.</summary>
    void ExportCardDefaultsTest()
    {
        var env = new CCCPRacing.Export.ExportEnvironment("exe", null, "jobs", "dest", "profile", null, 1);
        var cut = new CCCPRacing.Export.ExportUiController(() => new CCCPRacing.Export.ExportContext { Source = new FakeExportSource(), Cut = (10f, 25f) }, env);
        cut.Open();
        bool cutFirst = cut.Rows[0].Contains("THE CUT") && cut.Rows[4].Contains("0:15.0");
        cut.Handle(CCCPRacing.Export.ExportUiAction.Accept);      // (the first row is selected: Enter flips the span)
        bool whole = cut.Rows[0].Contains("WHOLE") && cut.Rows[4].Contains("1:20.0");
        cut.Handle(CCCPRacing.Export.ExportUiAction.Back);
        cut.Open();
        bool cutAgain = cut.Rows[0].Contains("THE CUT");
        var plain = new CCCPRacing.Export.ExportUiController(() => new CCCPRacing.Export.ExportContext { Source = new FakeExportSource(), Cut = null }, env);
        plain.Open();
        bool wholeNoCut = plain.Rows[0] == "SPAN  WHOLE";
        bool note = plain.Lines.Contains("60 FPS · NORMAL SPEED") && cut.Lines.Contains("60 FPS · NORMAL SPEED");
        Check(cutFirst && whole && cutAgain && wholeNoCut && note, $"export card: an active cut is offered first ({cutFirst}), WHOLE is one key away ({whole}) and not remembered ({cutAgain}), no cut means WHOLE ({wholeNoCut}), and the card says 60 FPS · NORMAL SPEED ({note})");
    }
}
