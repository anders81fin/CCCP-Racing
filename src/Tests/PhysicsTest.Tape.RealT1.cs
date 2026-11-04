using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

// T1 plugs the real index, recorder and codec into the tape tests. Until T3 (the real player) installs its own NewPlayer, the
// reference player is fed through TapeT1Bridge, which re-lays a real tape body in the layout the reference player reads. The
// bridge is test scaffolding only: it is not listed in TapeSeams.Real and T3's assignment replaces it.
public partial class PhysicsTest
{
    partial void InstallRealTapeT1()
    {
        TapeSeams.NewIndex = actors => new WorldTapeIndex(actors);
        TapeSeams.MarkPart = Tape.Part;
        TapeSeams.RegisterSpinner = (node, spinner) => { Tape.Spinner(node, spinner); RefReg.Spin.AddOrUpdate(node, spinner); };       // (RefReg: the reference player reads its own table)
        TapeSeams.RegisterSound = (node, channel, get, set) => { Tape.Sound(node, channel, get, set); RefReg.Sound.GetOrCreateValue(node).Add(new RefReg.SoundReg(channel, get, set)); };
        TapeSeams.NewRecorder = () => new WorldTapeRecorder();
        TapeSeams.UnregisteredSpinCount = recorder => recorder.UnregisteredSpinners;
        TapeSeams.EncodeTape = WorldTapeCodec.Encode;
        TapeSeams.DecodeTape = WorldTapeCodec.Decode;
        TapeSeams.NewPlayer = (tape, index, sink) => new RefPlayer(TapeT1Bridge.ForReference(tape), index, sink);
        foreach (var name in new[] { "NewIndex", "MarkPart", "RegisterSpinner", "RegisterSound", "NewRecorder", "UnregisteredSpinCount", "EncodeTape", "DecodeTape" }) TapeSeams.Real.Add(name);
        TapeT7Tests();
    }
}

static class TapeT1Bridge
{
    static readonly ConditionalWeakTable<WorldTape, WorldTape> Cache = new();

    /// <summary>The tape with its body re-laid in the reference layout; a payload that is not a real body (a reference recorder's) passes unchanged.</summary>
    public static WorldTape ForReference(WorldTape t)
    {
        if (Cache.TryGetValue(t, out var done)) return done;
        var body = WorldTapeCodec.ReadBody(t.Payload);
        if (body == null) return t;
        float ps = body.PosScale;
        RefPose P(TapePartState s)
        {
            var q = s.Rot; if (q.W < 0) q = new Quaternion(-q.X, -q.Y, -q.Z, -q.W);
            short Q(float v) => (short)System.MathF.Round(v * 32767f);
            return new RefPose(s.Visible, (int)System.MathF.Round(s.Pos.X * ps), (int)System.MathF.Round(s.Pos.Y * ps), (int)System.MathF.Round(s.Pos.Z * ps), Q(q.X), Q(q.Y), Q(q.Z), Q(q.W), s.Spin, s.Angle);
        }
        List<(int, RefPose)> L((int Part, TapePartState State)[] a) => a.Select(x => (x.Part, P(x.State))).ToList();
        var d = new RefData { PosScale = ps, Parts = body.Parts.ToList(), Events = body.Events.ToList() };
        foreach (var s in body.Samples) d.Samples.Add(L(s));
        foreach (var s in body.Sounds) d.Sounds.Add(s.ToList());
        foreach (var k in body.Keys)
            d.Keys.Add(new RefKey { Sample = k.Sample, Poses = L(k.Poses), Sounds = new(k.Sounds), Inst = k.LiveInstances.ToList(), Eff = k.LiveEffects.ToList(), Broken = k.Broken.ToList() });
        var tape = new WorldTape { TrackId = t.TrackId, Set = t.Set, LapTime = t.LapTime, SampleCount = t.SampleCount, GhostFingerprint = t.GhostFingerprint, ActorStructure = t.ActorStructure, Payload = RefCodec.Write(d) };
        Cache.Add(t, tape);
        return tape;
    }
}
