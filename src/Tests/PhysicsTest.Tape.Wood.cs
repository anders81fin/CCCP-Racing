using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

// tape 24: wooden fence breaks are events with the RNG state at the break. The real fence's
// replay path rebuilds the same debris at a seek age, preserves the live RNG stream, and caps
// retained debris at the production limit.
public partial class PhysicsTest
{
    sealed class WoodTapeSink : IWorldTapeSink
    {
        public readonly TapeFxPlayer Fx;
        public WoodTapeSink(TapeFxPlayer fx) { Fx = fx; }
        public Node3D Spawn(string kind, string parent, int instance) => null;
        public void Despawn(int instance) { }
        public void Effect(in TapeEvent e, float age) => Fx.Effect(e, age);
        public void ClearEffects() => Fx.Clear();
        public void SetBrokenBreakables(IReadOnlyCollection<int> ids) { }
    }

    static TapeEvent TapeWoodBreakEvent(float time, int id, Vector2 hit, Vector3 vel, ulong rng)
        => new(time, TapeEventType.Effect, Source: "None:WoodFences", Kind: TapeFxKind.WoodBreak,
            Parent: rng.ToString("X16", CultureInfo.InvariantCulture), Pos: new Vector3(hit.X, 0, hit.Y),
            Velocity: vel, Param: id, Life: float.MaxValue);

    static float TapeWoodPoseDiff(IReadOnlyList<Transform3D> a, IReadOnlyList<Transform3D> b)
    {
        if (a.Count != b.Count) return 999f;
        float worst = 0f;
        for (int i = 0; i < a.Count; i++)
        {
            worst = MathF.Max(worst, a[i].Origin.DistanceTo(b[i].Origin));
            foreach (var (x, y) in new[] { (a[i].Basis.X, b[i].Basis.X), (a[i].Basis.Y, b[i].Basis.Y), (a[i].Basis.Z, b[i].Basis.Z) })
                worst = MathF.Max(worst, (x - y).Length());
        }
        return worst;
    }

    void TapeT24()
    {
        var track = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        var fx = new TyreEffects { Name = "FxT24" }; fx.Init(track); AddChild(fx);
        WoodFences Make(string name)
        {
            var wood = new WoodFences { Name = name };
            wood.Init(track, fx, new StandardMaterial3D());
            AddChild(wood);
            return wood;
        }
        const float dt = 1f / 120f;
        var vel = new Vector3(7f, 0f, -2f);

        // A real live break records the event inputs and RNG state. Replaying those inputs at
        // the same age must put every board at the same transform.
        var live = Make("WoodT24Live");
        int eventCount = 0, eventId = -1; Vector2 eventHit = default; Vector3 eventVel = default; ulong eventRng = 0;
        live.TapeBroken += (id, hit, velocity, rng) => { eventCount++; eventId = id; eventHit = hit; eventVel = velocity; eventRng = rng; };
        live.BreakNetwork(0, vel);
        for (int i = 0; i < 72; i++) live.Tick(dt);
        var sequential = live.DebrisPoses();
        TapeCheck(eventCount == 1 && eventId == 0 && eventVel == vel && live.BrokenCount == 1 && sequential.Count > 0,
            $"tape 24: a real wooden span break emits one event with its stable id, hit, velocity and RNG state (events {eventCount}, id {eventId}, debris {sequential.Count})");
        live.Reset();
        live.ReplayBreak(eventId, eventHit, eventVel, eventRng, 0f);
        for (int i = 1; i <= 72; i++) live.ReplayUpdate(i * dt);
        float diff = TapeWoodPoseDiff(sequential, live.DebrisPoses());
        TapeCheck(live.BrokenCount == 1 && diff < 1e-3f,
            $"tape 24: replaying the captured break and advancing to 0.6 s restores the same real debris poses (worst difference {diff:F6} m)");

        // A seek at several ages must restore the same result as ticking the real pieces at
        // 120 Hz. Reuse these two actors so each iteration also checks reset and loop cleanup.
        int restartCount = 0; live.Restarted += () => restartCount++;
        foreach (float age in new[] { 0.05f, 0.6f, 1.5f, 4f, 20f })
        {
            live.Reset();
            live.ReplayBreak(eventId, eventHit, eventVel, eventRng, 0f);
            int steps = (int)MathF.Floor(age * 120f + 1e-3f);
            for (int i = 0; i < steps; i++) live.Tick(dt);
            var ticked = live.DebrisPoses();
            live.Reset();
            live.ReplayBreak(eventId, eventHit, eventVel, eventRng, age, -age);
            live.ReplayUpdate(0f);
            float seekDiff = TapeWoodPoseDiff(ticked, live.DebrisPoses());
            TapeCheck(live.BrokenCount == 1 && ticked.Count > 0 && seekDiff < 1e-3f,
                $"tape 24: a seek to wood debris age {age:F2} s equals the real 120 Hz tick result (worst difference {seekDiff:F6} m)");
        }
        live.Reset();
        TapeCheck(restartCount >= 10 && live.BrokenCount == 0 && live.DebrisCount == 0,
            $"tape 24: repeated seeks and loop resets stand every span up and remove its debris ({restartCount} restart notifications)");

        // A long run of float clock additions drifts off the exact 120 Hz grid. The event age
        // is the difference between two such lap clocks, so replay must round it back to the
        // nearest simulation step instead of losing one at a late break.
        float worstLateDiff = 0f; string lateWhere = "";
        for (int trial = 0; trial < 3; trial++)
        {
            live.Reset();
            float lapClock = 0f;
            for (int i = 0; i < 8400; i++) lapClock += dt;
            int id = trial + 2; Vector2 hit = default; ulong rng = 0;
            live.TapeBroken += Capture;
            void Capture(int brokenId, Vector2 at, Vector3 velocity, ulong state)
            {
                if (brokenId == id) { hit = at; rng = state; }
            }
            live.BreakNetwork(id, vel);
            float eventTime = lapClock;
            int steps = new[] { 24, 72, 240 }[trial];
            for (int i = 0; i < steps; i++) { lapClock += dt; live.Tick(dt); }
            var lateLive = live.DebrisPoses();
            live.TapeBroken -= Capture;
            live.Reset();
            float age = lapClock - eventTime;
            live.ReplayBreak(id, hit, vel, rng, age, eventTime);
            live.ReplayUpdate(lapClock);
            float d = TapeWoodPoseDiff(lateLive, live.DebrisPoses());
            if (d > worstLateDiff) { worstLateDiff = d; lateWhere = $"{steps} ticks, event {eventTime:R}, age {age:R}"; }
        }
        TapeCheck(worstLateDiff < 1e-3f,
            $"tape 24: late-lap float clock drift still restores breaks after 24, 72 and 240 ticks ({lateWhere}; worst pose difference {worstLateDiff:F6} m)");
        live.Reset();

        // Replay and reset must not disturb the RNG stream used by later live breaks. Compare
        // a world that performed a replay between two live breaks with an untouched twin.
        var withReplay = Make("WoodT24RngA"); var control = Make("WoodT24RngB");
        ulong laterA = 0, laterB = 0;
        withReplay.TapeBroken += (id, hit, velocity, rng) => { if (id == 1) laterA = rng; };
        control.TapeBroken += (id, hit, velocity, rng) => { if (id == 1) laterB = rng; };
        withReplay.BreakNetwork(0, vel); control.BreakNetwork(0, vel);
        var firstA = withReplay.DebrisPoses(); var firstB = control.DebrisPoses();
        withReplay.Reset(); control.Reset();
        withReplay.ReplayBreak(0, eventHit, vel, eventRng, 0f); withReplay.ReplayUpdate(0.25f); withReplay.Reset();
        withReplay.BreakNetwork(1, vel); control.BreakNetwork(1, vel);
        float rngDiff = TapeWoodPoseDiff(withReplay.DebrisPoses(), control.DebrisPoses());
        TapeCheck(TapeWoodPoseDiff(firstA, firstB) < 1e-4f && laterA != 0 && laterA == laterB && rngDiff < 1e-4f,
            $"tape 24: recording, replaying and resetting a first break leaves the next live break's RNG stream unchanged (states {laterA}/{laterB}, pose difference {rngDiff:F6} m)");

        // Add synthetic fence runs for this cap-only fixture: Ilyich itself yields 96 pieces,
        // below the production cap. Break all spans so the oldest pieces must be evicted.
        var capTrack = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        for (int i = 0; i < 90; i++)
        {
            float x = 300f + i * 4f;
            capTrack.Fences.Add(new FenceRun("wood", new List<Vector3> { new(x, 0, 300), new(x + 2f, 0, 300) }));
        }
        var capFx = new TyreEffects { Name = "FxT24Cap" }; capFx.Init(capTrack); AddChild(capFx);
        var capWood = new WoodFences { Name = "WoodT24Cap" }; capWood.Init(capTrack, capFx, new StandardMaterial3D()); AddChild(capWood);
        capWood.ReplayBreak(0, eventHit, vel, eventRng, 0f);
        var oldest = capWood.DebrisPoses();
        for (int id = 1; id < capWood.SpanCount; id++) capWood.ReplayBreak(id, eventHit, vel, eventRng, 0f);
        var capped = capWood.DebrisPoses();
        TapeCheck(capWood.DebrisCount == 160 && capped.Count == 160 && capWood.BrokenCount == capWood.SpanCount
                && oldest.Count > 0 && oldest.All(p => !TapeWoodHasOrigin(capped, p.Origin)),
            $"tape 24: breaking all {capWood.SpanCount} spans evicts the oldest debris and retains exactly 160 pieces ({capWood.DebrisCount} pieces; oldest removed {oldest.Count > 0 && oldest.All(p => !TapeWoodHasOrigin(capped, p.Origin))})");

        TapeWoodProductionChain(track, fx, eventId, eventVel);

        live.Free(); withReplay.Free(); control.Free(); capWood.Free(); capFx.Free(); fx.Free();
    }

    static bool TapeWoodHasOrigin(IReadOnlyList<Transform3D> poses, Vector3 origin)
        => poses.Any(p => p.Origin.DistanceTo(origin) < 1e-4f);

    void TapeWoodProductionChain(TrackData track, TyreEffects fx, int liveId, Vector3 liveVel)
    {
        const string Key = "None:WoodFences";
        var source = new WoodFences { Name = "WoodT24Record" }; source.Init(track, fx, new StandardMaterial3D()); AddChild(source);
        // Capture a genuinely live break that remains present at lap start. Its 60 pre-lap
        // ticks make the carried event half a second old when recording begins.
        int carriedId = Math.Min(1, source.SpanCount - 1); Vector2 carriedHit = default; Vector3 carriedVel = new(3f, 0, 1f); ulong carriedRng = 0;
        source.TapeBroken += (id, hit, vel, rng) => { if (id == carriedId) { carriedHit = hit; carriedVel = vel; carriedRng = rng; } };
        source.BreakNetwork(carriedId, carriedVel);
        const float dt = 1f / 120f;
        for (int i = 0; i < 60; i++) source.Tick(dt);

        var recIndex = TapeSeams.NewIndex(new[] { (Key, (Node3D)source) });
        var rec = TapeSeams.NewRecorder(); rec.Begin(recIndex);
        rec.Event(TapeWoodBreakEvent(-0.5f, carriedId, carriedHit, carriedVel, carriedRng));
        float clock = 0f;
        source.TapeBroken += (id, hit, vel, rng) => rec.Event(TapeWoodBreakEvent(clock, id, hit, vel, rng));
        rec.Record(0f);
        var sourceAt = new Dictionary<int, List<Transform3D>> { [0] = source.DebrisPoses() };
        foreach (int step in new[] { 30, 60, 90, 600 }) sourceAt[step] = null;
        for (int step = 1; step <= 600; step++)
        {
            source.Tick(dt);
            clock = step * dt;
            if (step == 60) source.BreakNetwork(liveId, liveVel);
            rec.Record(clock);
            if (sourceAt.ContainsKey(step)) sourceAt[step] = source.DebrisPoses();
        }
        var recorded = rec.Finish(5f, 1UL);
        var bytes = WorldTapeCodec.Encode(recorded);
        var loaded = WorldTapeCodec.Decode(bytes);
        var body = WorldTapeCodec.ReadBody(loaded?.Payload);
        var woodEvents = body?.Events.Where(e => e.Kind == TapeFxKind.WoodBreak).OrderBy(e => e.Time).ToList() ?? new List<TapeEvent>();
        TapeCheck(loaded != null && woodEvents.Count == 2 && woodEvents[0].Time < 0 && woodEvents[1].Time == 0.5f
                && woodEvents.All(e => e.Source == Key && e.Parent?.Length == 16 && e.Life == float.MaxValue),
            $"tape 24: the production recorder and codec save/load a carried and a live wood-break event ({woodEvents.Count} events; times {string.Join(",", woodEvents.Select(e => e.Time.ToString("F2")))})");

        var target = new WoodFences { Name = "WoodT24Replay" }; target.Init(track, fx, new StandardMaterial3D()); AddChild(target);
        var fxRoot = new Node3D { Name = "FxRootT24" }; AddChild(fxRoot);
        var playerFx = new TapeFxPlayer(fx, track, null, fxRoot) { Wood = target, WoodReplay = true };
        var sink = new WoodTapeSink(playerFx);
        var index = TapeSeams.NewIndex(new[] { (Key, (Node3D)target) });
        var player = new WorldTapePlayer(loaded, index, sink);
        bool covers = player.Covers(Key);
        player.Seek(0.25f); playerFx.Update(0.25f);
        int beforeId = target.BrokenCount; int beforeDebris = target.DebrisCount;
        float diffBefore = TapeWoodPoseDiff(sourceAt[30], target.DebrisPoses());
        player.Seek(0.5f); playerFx.Update(0.5f);
        int newbornId = target.BrokenCount; int newbornDebris = target.DebrisCount;
        float diffNewborn = TapeWoodPoseDiff(sourceAt[60], target.DebrisPoses());
        player.Seek(0.75f); playerFx.Update(0.75f);
        int afterId = target.BrokenCount; int afterDebris = target.DebrisCount;
        float diffQuarter = TapeWoodPoseDiff(sourceAt[90], target.DebrisPoses());
        player.Seek(5f); playerFx.Update(5f);
        float diffLate = TapeWoodPoseDiff(sourceAt[600], target.DebrisPoses());
        player.Seek(0.75f); playerFx.Update(0.75f); // a loop/seek back must rebuild the same pieces once
        float diffLoop = TapeWoodPoseDiff(sourceAt[90], target.DebrisPoses());
        TapeCheck(covers && beforeId == 1 && beforeDebris > 0 && diffBefore < 1e-3f
                && newbornId == 2 && newbornDebris > beforeDebris && diffNewborn < 1e-3f
                && afterId == 2 && diffQuarter < 1e-3f && diffLate < 1e-3f && diffLoop < 1e-3f,
            $"tape 24: the production recorder/player matches source debris before the live break, at its newborn frame, at 0.75 s, at 5 s and after a seek loop (counts {beforeId}/{newbornId}/{afterId}; pose differences {diffBefore:F5}/{diffNewborn:F5}/{diffQuarter:F5}/{diffLate:F5}/{diffLoop:F5} m)");
        player.Release();
        TapeCheck(target.BrokenCount == 0 && target.DebrisCount == 0,
            $"tape 24: leaving the replay clears every wooden span and piece ({target.BrokenCount}/{target.DebrisCount})");

        // A wrong RNG-state payload must be visible in the real TapeFxPlayer path: it creates
        // the right spans, but the wrong scatter and tumble for the boards.
        var wrongSeed = new WoodFences { Name = "WoodT24WrongSeed" }; wrongSeed.Init(track, fx, new StandardMaterial3D()); AddChild(wrongSeed);
        var wrongRoot = new Node3D { Name = "FxRootT24WrongSeed" }; AddChild(wrongRoot);
        var wrongFx = new TapeFxPlayer(fx, track, null, wrongRoot) { Wood = wrongSeed, WoodReplay = true };
        var carriedBreak = woodEvents.Single(e => e.Time < 0f);
        wrongFx.Effect(carriedBreak, 0.75f - carriedBreak.Time);
        var liveBreak = woodEvents.Single(e => e.Time == 0.5f);
        ulong rightSeed = ulong.Parse(liveBreak.Parent, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        wrongFx.Effect(TapeWoodBreakEvent(liveBreak.Time, (int)liveBreak.Param, new Vector2(liveBreak.Pos.X, liveBreak.Pos.Z),
            liveBreak.Velocity, rightSeed ^ 0xDEADBEEFCAFEBABEUL), 0.25f);
        wrongFx.Update(0.75f);
        float wrongSeedDiff = TapeWoodPoseDiff(sourceAt[90], wrongSeed.DebrisPoses());
        TapeCheck(wrongSeed.BrokenCount == 2 && wrongSeed.DebrisCount == sourceAt[90].Count && wrongSeedDiff > 1e-2f,
            $"tape 24: a validly shaped but wrong wood RNG payload restores the span with visibly different debris poses (difference {wrongSeedDiff:F4} m)");
        wrongFx.Free(); wrongRoot.Free(); wrongSeed.Free();

        // Same key, deliberately changed root version: the player must drop that actor's
        // events when its layout no longer matches the recorded structure.
        var incompatible = new WoodFences { Name = "WoodT24Incompatible" }; incompatible.Init(track, fx, new StandardMaterial3D()); AddChild(incompatible);
        incompatible.SetMeta(WorldTapeConst.VersionMeta, 123456789);
        var badFxRoot = new Node3D { Name = "FxRootT24Bad" }; AddChild(badFxRoot);
        var badFx = new TapeFxPlayer(fx, track, null, badFxRoot) { Wood = incompatible, WoodReplay = true };
        var badPlayer = new WorldTapePlayer(loaded, TapeSeams.NewIndex(new[] { (Key, (Node3D)incompatible) }), new WoodTapeSink(badFx));
        badPlayer.Seek(0.75f); badFx.Update(0.75f);
        TapeCheck(!badPlayer.Covers(Key) && incompatible.BrokenCount == 0 && incompatible.DebrisCount == 0,
            $"tape 24: the player filters wood events for an incompatible root structure (covers {badPlayer.Covers(Key)}, spans {incompatible.BrokenCount}, pieces {incompatible.DebrisCount})");
        badPlayer.Release();

        playerFx.Free(); badFx.Free(); fxRoot.Free(); badFxRoot.Free(); target.Free(); incompatible.Free(); source.Free();
    }
}
