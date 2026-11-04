using System;
using System.Collections.Generic;
using System.Linq;
using CCCPRacing.Audio;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Race;

public sealed partial class TapeFxPlayer
{
    const int BusRate = 120;
    sealed class BurningBus
    {
        public TapeEvent Event;
        public Node3D Wreck;
        public StandardMaterial3D Burnt;
        public FireSound Sound;
        public int Step;
        public float Age;
    }
    readonly List<BurningBus> _busFires = new();
    public int BusFires => _busFires.Count;
    public IReadOnlyList<float> BusFireAges() => _busFires.Select(b => b.Age).ToArray();
    public IReadOnlyList<float> BusFireLevels() => _busFires.Select(b => b.Sound.Level).ToArray();

    void BusFire(in TapeEvent e, float age)
    {
        var wreck = SovietBus.BuildReplayWreck(e, out var burnt);
        _root.AddChild(wreck);
        var sound = new FireSound { Position = e.Pos + Vector3.Up * 1.5f };
        _root.AddChild(sound);
        var bus = new BurningBus { Event = e, Wreck = wreck, Burnt = burnt, Sound = sound };
        _busFires.Add(bus);
        // The ignition's longest smoke lives four seconds. Older seeks need only the emitter window.
        if (age < 4f)
        {
            _fx.BeginReplay(age);
            try { Seed(e); SovietBus.EmitIgnition(_fx, _rng, wreck.GlobalTransform); }
            finally { _fx.EndReplay(); }
        }
        bus.Step = Math.Max(0, (int)Math.Floor(age * BusRate) - 8 * BusRate);
        AdvanceBus(bus, age);
        sound.RestoreAge(age);
    }

    // Sum of the live blaze curve sampled on the replay's fixed grid. Starting a bounded
    // pre-roll with its real fractional emission phase makes a seek match sequential playback.
    static double BusBlazeSum(int steps)
    {
        int full = Math.Min(steps, 45 * BusRate);
        int fade = Math.Clamp(steps - 45 * BusRate, 0, 21 * BusRate);
        return full + fade - (double)fade * (fade + 1) / (2 * 30 * BusRate)
            + .3 * Math.Max(0, steps - 66 * BusRate);
    }

    void AdvanceBus(BurningBus bus, float age)
    {
        age = Math.Max(0f, age);
        int end = (int)Math.Floor(age * BusRate + .001f);
        for (int step = bus.Step + 1; step <= end; step++)
        {
            float at = (float)step / BusRate;
            double sum = BusBlazeSum(step - 1);
            double fire = 70 * sum / BusRate, smoke = (4d * (step - 1) + 9 * sum) / BusRate;
            float fireAcc = (float)(fire - Math.Floor(fire));
            float smokeAcc = (float)(smoke - Math.Floor(smoke));
            Seed(bus.Event); _rng.Seed ^= (ulong)step * 11400714819323198485UL;
            _fx.BeginReplay(Math.Max(0f, age - at));
            try { SovietBus.EmitFire(_fx, _rng, bus.Wreck.GlobalTransform, at, 1f / BusRate, ref fireAcc, ref smokeAcc); }
            finally { _fx.EndReplay(); }
        }
        bus.Step = end; bus.Age = age;
        SovietBus.SetFireAge(bus.Burnt, bus.Sound, age);
    }

    void UpdateBusFires(float clock)
    {
        foreach (var bus in _busFires) AdvanceBus(bus, clock - bus.Event.Time);
    }
}
