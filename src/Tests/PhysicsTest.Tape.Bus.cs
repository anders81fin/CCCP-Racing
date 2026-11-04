using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    const string TapeBusKey = "Bus:Bus";

    // This host follows the production host's next-sample spawn timestamp. It observes the
    // actual bus's ignition and passenger births; the fixture never calls Ignite directly.
    sealed class BusRecordHost : ITapeFxHost, ITapeInstanceHost
    {
        public readonly WorldTapeRecorder Rec;
        public readonly IWorldTapeIndex Index;
        public readonly Dictionary<Node3D, int> Live = new();
        public readonly List<TapeEvent> Fires = new();
        public float Clock;
        int _next;
        public BusRecordHost(WorldTapeRecorder rec, IWorldTapeIndex index) { Rec = rec; Index = index; }
        public void Fx(in TapeEvent e)
        {
            var ev = e with { Source = TapeBusKey, Time = Clock - e.Time };
            Fires.Add(ev); Rec.Event(ev);
        }
        public void Born(Node3D n)
        {
            int id = ++_next; Live.Add(n, id); Index.AddInstance(TapeBusKey, id, n);
            Rec.Event(new TapeEvent(Rec.Samples / WorldTapeConst.TapeRate, TapeEventType.Spawn,
                Source: TapeBusKey, Parent: TapeBusKey, Kind: (string)n.GetMeta(WorldTapeConst.KindMeta), Instance: id));
        }
        public void Gone(Node3D n)
        {
            if (!Live.Remove(n, out int id)) return;
            Index.RemoveInstance(id);
            Rec.Event(new TapeEvent(Rec.Samples / WorldTapeConst.TapeRate, TapeEventType.Despawn,
                Source: TapeBusKey, Instance: id));
        }
        public void Carry()
        {
            foreach (var (n, id) in Live)
                Rec.Event(new TapeEvent(0f, TapeEventType.Spawn, Source: TapeBusKey, Parent: TapeBusKey,
                    Kind: (string)n.GetMeta(WorldTapeConst.KindMeta), Instance: id));
        }
    }

    sealed class BusReplaySink : IWorldTapeSink
    {
        public readonly TapeFxPlayer Fx;
        public readonly Node3D Root;
        public readonly Dictionary<int, Node3D> Live = new();
        public BusReplaySink(TapeFxPlayer fx, Node3D root) { Fx = fx; Root = root; }
        public Node3D Spawn(string kind, string parent, int instance)
        {
            const string prefix = "bus-passenger/";
            if (parent != TapeBusKey || kind?.StartsWith(prefix, StringComparison.Ordinal) != true) return null;
            var n = SovietBus.BuildReplayPassenger(kind[prefix.Length..]);
            Root.AddChild(n); Live.Add(instance, n); return n;
        }
        public void Despawn(int instance)
        {
            if (!Live.Remove(instance, out var n)) return;
            n.GetParent()?.RemoveChild(n); n.Free();
        }
        public void Effect(in TapeEvent e, float age) => Fx.Effect(e, age);
        public void ClearEffects() => Fx.Clear();
        public void SetBrokenBreakables(IReadOnlyCollection<int> ids) { }
    }

    static T TapeBusField<T>(SovietBus bus, string name)
        => (T)typeof(SovietBus).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bus);

    static IEnumerable<Node> TapeBusTree(Node n)
    {
        yield return n;
        foreach (var c in n.GetChildren()) foreach (var d in TapeBusTree(c)) yield return d;
    }

    static List<Transform3D> TapeBusPoses(Node n)
        => TapeBusTree(n).OfType<Node3D>().Select(p => p.Transform).ToList();

    static Dictionary<string, (Transform3D Pose, bool Visible)> TapeBusMarked(Node3D n)
        => TapeBusTree(n).OfType<Node3D>().Where(p => p.HasMeta(WorldTapeConst.PartMeta))
            .ToDictionary(p => (string)p.GetMeta(WorldTapeConst.PartMeta), p => (p.Transform, p.Visible));

    static float TapeBusPassengerDiff(Dictionary<int, Dictionary<string, (Transform3D Pose, bool Visible)>> expected,
        IReadOnlyDictionary<int, Node3D> actual)
    {
        if (!expected.Keys.OrderBy(k => k).SequenceEqual(actual.Keys.OrderBy(k => k))) return 999f;
        float worst = 0f;
        foreach (var (id, want) in expected)
        {
            var got = TapeBusMarked(actual[id]);
            if (!want.Keys.OrderBy(k => k).SequenceEqual(got.Keys.OrderBy(k => k))) return 999f;
            foreach (var (part, pose) in want)
            {
                if (pose.Visible != got[part].Visible) return 999f;
                worst = Math.Max(worst, TapeWoodPoseDiff(new[] { pose.Pose }, new[] { got[part].Pose }));
            }
        }
        return worst;
    }

    static float TapeBusSoundEnvelope(FireSound sound)
        => (float)typeof(FireSound).GetField("_lvl", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sound);

    static List<Vector3> TapeBusShoutPositions(SovietBus bus)
        => bus.ActiveShouts().Select(s => s.pos).OrderBy(p => p.X).ThenBy(p => p.Z).ToList();

    static float TapeBusShoutDiff(IReadOnlyList<Vector3> expected, IReadOnlyList<Vector3> actual)
        => expected.Count != actual.Count ? 999f : expected.Zip(actual, (a, b) => a.DistanceTo(b)).DefaultIfEmpty(0f).Max();

    sealed record BusSnapshot(List<Transform3D> Wreck, float Char, float Level, float Age, bool Spare,
        Dictionary<int, Dictionary<string, (Transform3D Pose, bool Visible)>> Passengers, List<Vector3> Shouts);

    static BusSnapshot TapeBusSnapshot(SovietBus bus, BusRecordHost host)
    {
        var wreck = TapeBusField<Node3D>(bus, "_wreck");
        return new BusSnapshot(TapeBusPoses(wreck), TapeBusField<StandardMaterial3D>(bus, "_burnt").AlbedoColor.A,
            TapeBusField<FireSound>(bus, "_crackle").Level, TapeBusField<float>(bus, "_burnT"), bus.Model.Visible,
            host.Live.ToDictionary(kv => kv.Value, kv => TapeBusMarked(kv.Key)), TapeBusShoutPositions(bus));
    }

    void TapeT25()
    {
        const float dt = 1f / 120f;
        (TrackData Track, TyreEffects Fx, SovietBus Bus) Make(string name)
        {
            var track = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
            var fx = new TyreEffects { Name = name + "Fx" }; fx.Init(track); AddChild(fx);
            int staticObs = track.Obstacles.Count;
            var bus = new SovietBus { Name = name, TestInto = "fire" }; AddChild(bus);
            bus.Init(track, fx, track.Center.Length * .5f, null, staticObs);
            TapeBusField<RandomNumberGenerator>(bus, "_rng").Seed = 0xB055F1EUL;
            return (track, fx, bus);
        }
        var source = Make("BusT25Source"); var control = Make("BusT25Control");
        var sourceIndex = new WorldTapeIndex(new[] { (TapeBusKey, (Node3D)source.Bus) });
        var staticParts = Enumerable.Range(0, sourceIndex.Count).Select(sourceIndex.NodeOf).ToArray();
        var recorder = new WorldTapeRecorder(); recorder.Begin(sourceIndex);
        var host = new BusRecordHost(recorder, sourceIndex);
        source.Bus.TapeFx = host; source.Bus.TapeHost = host;
        recorder.Record(0f);
        var snapshots = new Dictionary<int, BusSnapshot>();
        bool sameLive = true; float liveDiff = 0f;
        for (int step = 1; step <= 80 * 120; step++)
        {
            host.Clock = step * dt;
            source.Bus.Tick(dt, null); control.Bus.Tick(dt, null);
            recorder.Record(host.Clock);
            if (step % 120 == 0)
            {
                float d = TapeWoodPoseDiff(TapeBusPoses(source.Bus), TapeBusPoses(control.Bus));
                liveDiff = Math.Max(liveDiff, d);
                sameLive &= d < 1e-5f && TapeBusField<RandomNumberGenerator>(source.Bus, "_rng").State
                    == TapeBusField<RandomNumberGenerator>(control.Bus, "_rng").State;
            }
            if (step is 480 or 6480 or 9600 && TapeBusField<Node3D>(source.Bus, "_wreck") != null)
                snapshots[step / 120] = TapeBusSnapshot(source.Bus, host);
        }
        var loaded = WorldTapeCodec.Decode(WorldTapeCodec.Encode(recorder.Finish(80f, 1UL)));
        var body = WorldTapeCodec.ReadBody(loaded?.Payload);
        var fireEvents = body?.Events.Where(e => e.Kind == TapeFxKind.BusFire).ToList() ?? new();
        var births = body?.Events.Where(e => e.Type == TapeEventType.Spawn).ToList() ?? new();
        bool ignited = fireEvents.Count == 1 && host.Fires.Count == 1 && snapshots.Count == 3;
        TapeCheck(ignited && fireEvents[0].Time > 0 && fireEvents[0].Time < 4f && fireEvents[0].Life == float.MaxValue
                && fireEvents[0].Source == TapeBusKey && births.Count is 2 or 3
                && births.All(e => e.Kind.StartsWith("bus-passenger/", StringComparison.Ordinal)),
            $"tape 25: an actual obstacle collision records a persistent bus fire and its coat-specific passenger births through recorder and codec ({fireEvents.Count} fires, {births.Count} passengers)");
        TapeCheck(sameLive && liveDiff < 1e-5f,
            $"tape 25: recording the real fire leaves all live bus/wreck/passenger transforms and the native RNG stream unchanged (worst difference {liveDiff:F6} m)");
        TapeCheck(staticParts.All(n => GodotObject.IsInstanceValid(n) && !n.IsQueuedForDeletion())
                && staticParts.Contains(source.Bus.Model) && snapshots.TryGetValue(80, out var final) && final.Spare,
            "tape 25: ignition and spare departure preserve every original indexed bus node, and the spare reuses the original model");
        if (!ignited)
        {
            source.Bus.Free(); control.Bus.Free(); source.Fx.Free(); control.Fx.Free(); return;
        }

        var target = Make("BusT25Target");
        var targetIndex = new WorldTapeIndex(new[] { (TapeBusKey, (Node3D)target.Bus) });
        target.Bus.TapeDriven = true;
        var fxRoot = new Node3D { Name = "BusT25ReplayFx" }; AddChild(fxRoot);
        var replayFx = new TapeFxPlayer(target.Fx, target.Track, null, fxRoot);
        var sink = new BusReplaySink(replayFx, target.Bus);
        var player = new WorldTapePlayer(loaded, targetIndex, sink);
        player.Seek(0f); replayFx.Update(0f);
        TapeCheck(player.Covers(TapeBusKey) && replayFx.BusFires == 0 && sink.Live.Count == 0 && target.Bus.Model.Visible,
            "tape 25: a compatible production replay starts with the bus in service and no future fire or passengers");

        for (int step = 1; step <= 480; step++)
        {
            player.Apply(step * dt, dt); replayFx.Update(step * dt);
        }
        float sequentialPax = TapeBusPassengerDiff(snapshots[4].Passengers, sink.Live);
        TapeCheck(replayFx.BusFires == 1 && sequentialPax < .004f && replayFx.Particles > 0
                && Math.Abs(replayFx.BusFireAges().Single() - snapshots[4].Age) < .02f,
            $"tape 25: ordinary forward playback crosses ignition once and advances the real passenger poses and ongoing fire (passenger difference {sequentialPax:F5})");
        TapeCheck(snapshots[4].Shouts.Count > 0 && TapeBusShoutDiff(snapshots[4].Shouts, TapeBusShoutPositions(target.Bus)) < .004f,
            "tape 25: ordinary playback restores the real passengers' active HUD shout bubbles and their world positions");

        foreach (int at in new[] { 4, 54, 80, 4 })
        {
            player.Seek(at); replayFx.Update(at);
            var want = snapshots[at];
            var wreck = fxRoot.GetChildren().OfType<Node3D>().FirstOrDefault(n => n.Name.ToString().Contains("wreck", StringComparison.OrdinalIgnoreCase));
            var meshes = wreck == null ? new List<GeometryInstance3D>() : TapeBusTree(wreck).OfType<GeometryInstance3D>().ToList();
            var burnt = meshes.FirstOrDefault()?.MaterialOverlay as StandardMaterial3D;
            var sound = TapeBusTree(fxRoot).OfType<FireSound>().SingleOrDefault();
            float poseDiff = wreck == null ? 999f : TapeWoodPoseDiff(want.Wreck, TapeBusPoses(wreck));
            float paxDiff = TapeBusPassengerDiff(want.Passengers, sink.Live);
            bool levels = burnt != null && sound != null && Math.Abs(burnt.AlbedoColor.A - want.Char) < .002f
                && Math.Abs(sound.Level - want.Level) < .002f;
            TapeCheck(replayFx.BusFires == 1 && poseDiff < .002f && paxDiff < .004f && levels && replayFx.Particles > 0
                    && meshes.Count > 0 && meshes.All(m => m.MaterialOverlay == burnt)
                    && target.Bus.Model.Visible == want.Spare && Math.Abs(replayFx.BusFireAges().Single() - want.Age) < .02f,
                $"tape 25: seek/loop to {at}s restores the real wreck, all passenger poses, char, crackle and spare state (wreck {poseDiff:F5}, passengers {paxDiff:F5}, level {sound?.Level:F3}/{want.Level:F3})");
            float envelope = sound == null ? 0f : TapeBusSoundEnvelope(sound);
            TapeCheck(envelope > .29f && Math.Abs(envelope - want.Level) < .002f,
                $"tape 25: seeking to {at}s primes the actual crackle audio envelope at the old fire's level ({envelope:F3}/{want.Level:F3})");
            float shoutDiff = TapeBusShoutDiff(want.Shouts, TapeBusShoutPositions(target.Bus));
            TapeCheck(shoutDiff < .004f && (at != 54 || want.Shouts.Count == 0),
                $"tape 25: seek/loop to {at}s restores exactly the real active HUD shout bubbles ({want.Shouts.Count}, position difference {shoutDiff:F5})");
            var frozen = TapeBusPoses(target.Bus);
            target.Bus.Tick(1f, null);
            TapeCheck(TapeWoodPoseDiff(frozen, TapeBusPoses(target.Bus)) < 1e-6f
                    && TapeBusField<Node3D>(target.Bus, "_wreck") == null,
                "tape 25: tape-driven bus ticks do not move passengers or simulate a second native wreck/fire");
        }

        // Same event shape, same wreck and sound counts: a mistakenly newborn old fire must
        // visibly fail the material and audio checks even when node-count tests would pass.
        var wrongRoot = new Node3D { Name = "BusT25AgeZero" }; AddChild(wrongRoot);
        var wrongFx = new TapeFxPlayer(target.Fx, target.Track, null, wrongRoot);
        wrongFx.Effect(fireEvents[0], 0f);
        var wrongSound = TapeBusTree(wrongRoot).OfType<FireSound>().SingleOrDefault();
        var wrongOverlay = TapeBusTree(wrongRoot).OfType<GeometryInstance3D>().Select(n => n.MaterialOverlay).OfType<StandardMaterial3D>().FirstOrDefault();
        TapeCheck(wrongFx.BusFires == 1 && wrongSound != null && wrongOverlay != null
                && Math.Abs(wrongSound.Level - snapshots[80].Level) > .5f
                && TapeBusSoundEnvelope(wrongSound) < .001f
                && Math.Abs(wrongOverlay.AlbedoColor.A - snapshots[80].Char) > .5f,
            "tape 25: an equally shaped age-zero fire is detected by the real char overlay and crackle intensity at smoulder age");
        wrongFx.Free(); wrongRoot.Free();

        player.Seek(0f); replayFx.Update(0f);
        TapeCheck(replayFx.BusFires == 0 && sink.Live.Count == 0 && replayFx.Nodes == 0 && target.Fx.ReplayParticles == 0,
            "tape 25: seeking before ignition removes the wreck, crackle, passengers and every fire particle");
        player.Seek(54f); replayFx.Update(54f); player.Release();
        TapeCheck(replayFx.BusFires == 0 && sink.Live.Count == 0 && replayFx.Nodes == 0 && target.Fx.ReplayParticles == 0,
            "tape 25: releasing a burning replay clears every spawned passenger, wreck, sound and tagged particle");
        target.Bus.TapeDriven = false;
        TapeCheck(!target.Bus.TapeDriven && target.Bus.Model.Visible && TapeBusField<Node3D>(target.Bus, "_wreck") == null,
            "tape 25: replay exit restores a live service bus without a residual native or replay wreck");

        // Begin a second span while the actual fire still smoulders. Its negative event time
        // must survive the initial keyframe, not restart when another lap starts.
        recorder.Begin(sourceIndex); host.Carry();
        recorder.Event(fireEvents[0] with { Time = fireEvents[0].Time - 80f });
        recorder.Record(0f);
        var carriedTape = WorldTapeCodec.Decode(WorldTapeCodec.Encode(recorder.Finish(1f, 2UL)));
        target.Bus.TapeDriven = true;
        var carriedPlayer = new WorldTapePlayer(carriedTape, targetIndex, sink);
        carriedPlayer.Seek(0f); replayFx.Update(0f);
        float carryPaxDiff = TapeBusPassengerDiff(snapshots[80].Passengers, sink.Live);
        TapeCheck(carriedPlayer.Covers(TapeBusKey) && replayFx.BusFires == 1 && carryPaxDiff < .004f
                && Math.Abs(replayFx.BusFireAges().Single() - snapshots[80].Age) < .02f
                && Math.Abs(replayFx.BusFireLevels().Single() - snapshots[80].Level) < .002f,
            $"tape 25: a next lap's initial keyframe carries the already-smouldering fire and existing passengers at their real age (passenger difference {carryPaxDiff:F5})");
        carriedPlayer.Release();

        int resetNotifications = 0; source.Bus.TapeReset += () => resetNotifications++;
        source.Bus.Reset();
        var resetBody = WorldTapeCodec.ReadBody(recorder.Finish(1f, 3UL).Payload);
        TapeCheck(resetNotifications == 1 && host.Live.Count == 0 && sourceIndex.Count == staticParts.Length
                && resetBody.Events.Count(e => e.Type == TapeEventType.Despawn) == births.Count
                && TapeBusField<Node3D>(source.Bus, "_wreck") == null,
            "tape 25: a native bus reset notifies the fire host and records every real passenger removal before releasing its indexed nodes");

        target.Bus.SetMeta(WorldTapeConst.VersionMeta, 1);
        var oldIndex = new WorldTapeIndex(new[] { (TapeBusKey, (Node3D)target.Bus) });
        var incompatible = new WorldTapePlayer(loaded, oldIndex, sink);
        incompatible.Seek(54f); replayFx.Update(54f);
        TapeCheck(!incompatible.Covers(TapeBusKey) && replayFx.BusFires == 0 && sink.Live.Count == 0,
            "tape 25: the old bus version is visibly incompatible and drops its fire and passenger events for safe live fallback");
        incompatible.Release();
        replayFx.Free(); fxRoot.Free();
        source.Bus.TapeFx = null; source.Bus.TapeHost = null;
        source.Bus.Free(); control.Bus.Free(); target.Bus.Free();
        source.Fx.Free(); control.Fx.Free(); target.Fx.Free();
    }
}
