using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    const int TurboRate = 22050;      // (EngineSound.MixRate)

    /// <summary>
    /// The nine-second drive of the fuel voices (idle, a pull from 2500 to 6800 rpm, a quick lift, steady 5500 rpm with the booster) rendered offline with the public inputs only:
    /// the RMS of each half second. The Diesel's and the Petrol's voices do not have a turbo, so these numbers must stay what they were before the turbo's sound was reworked.
    /// </summary>
    static float[] TurboGoldenRms(int grade)
    {
        var snd = new EngineSound { Grade = grade, Volume = 1f };
        int win = TurboRate / 2;
        var rms = new List<float>();
        double sq = 0; int n = 0;
        for (int i = 0; i < TurboRate * 9; i++)
        {
            float t = i / (float)TurboRate;
            float rpm, thr;
            if (t < 1.5f) { rpm = 1200; thr = 0; }
            else if (t < 5.5f) { rpm = 2500 + (t - 1.5f) / 4f * 4300; thr = 1; }
            else if (t < 6.5f) { rpm = 6800 - (t - 5.5f) * 2000; thr = Math.Max(0f, 1f - (t - 5.5f) / 0.12f); }
            else { rpm = 5500; thr = 0.8f; }
            snd.Rpm = rpm; snd.Throttle = thr; snd.Driven = t > 1.5f ? 1 : 0; snd.Speed = t > 1.5f ? 20 : 0; snd.Boosted = t > 7f;
            float x = snd.NextSample();
            sq += x * x; n++;
            if (n == win) { rms.Add((float)Math.Sqrt(sq / n)); sq = 0; n = 0; }
        }
        snd.Free();
        return rms.ToArray();
    }

    // Measured on the code before the rework (the Diesel is grade 0, the Petrol grade 1): the RMS of each half second of TurboGoldenRms.
    static readonly float[] TurboGoldenDiesel = { 0.22644f, 0.23213f, 0.23180f, 0.29273f, 0.30018f, 0.28637f, 0.29145f, 0.28459f, 0.27576f, 0.27143f, 0.26735f, 0.24222f, 0.25724f, 0.26850f, 0.53131f, 0.32892f, 0.31068f, 0.31280f };
    static readonly float[] TurboGoldenPetrol = { 0.15990f, 0.15571f, 0.15934f, 0.32825f, 0.33468f, 0.33620f, 0.33502f, 0.33416f, 0.33798f, 0.33616f, 0.33749f, 0.20699f, 0.17492f, 0.29786f, 0.55856f, 0.36399f, 0.33581f, 0.33783f };

    /// <summary>One lift of the pedal for the turbo's voices: full throttle at the revs until 2.5 s, then the pedal falls to nothing in `LiftTime` seconds and the revs run down 6 % a second.</summary>
    sealed record TurboLift(string Name, float Rpm, float LiftTime, int Grade, bool Five, bool AntiLag = false)
    {
        public const float At = 2.5f, Length = 5f;
        public float Throttle(float t) => t < At ? 1f : Math.Max(0f, 1f - (t - At) / LiftTime);
        public float RpmAt(float t) => t < At ? Rpm : Rpm * (1f - .06f * (t - At));
        /// <summary>When the pedal is below 0.3: the moment the vent fires.</summary>
        public float VentTime => VentTimeOf(LiftTime);
        public static float VentTimeOf(float liftTime) => At + .7f * liftTime;
    }

    static readonly TurboLift[] TurboLifts =
    {
        new("jet-fast", 6000, .10f, 2, false), new("jet-slow", 6000, 1.2f, 2, false), new("jet-low-boost", 3000, .10f, 2, false), new("jet-half-boost", 4250, .10f, 2, false),
        new("sputnik-fast", 6000, .10f, 2, true), new("sputnik-slow", 6000, 1.2f, 2, true), new("sputnik-low-boost", 3000, .10f, 2, true), new("sputnik-antilag", 6000, .10f, 2, true, true),
        new("diesel-fast", 6000, .10f, 0, false), new("petrol-fast", 6000, .10f, 1, false),
    };

    /// <summary>A lift rendered offline: the whole mix and each of the turbo's voices on their own, with the shaft's speed and the boost every 10 ms.</summary>
    sealed class TurboTrace
    {
        public float[] Mix, Whistle, Vent, Wastegate;
        public readonly List<float> Shaft = new(), Boost = new();
        public float ShaftAt(float t) => Shaft[Math.Min(Shaft.Count - 1, (int)(t * 100))];
        public float BoostAt(float t) => Boost[Math.Min(Boost.Count - 1, (int)(t * 100))];
    }

    static TurboTrace TurboRenderLift(TurboLift l, Func<float, bool> antiLagAt = null)
    {
        var snd = new EngineSound { Grade = l.Grade, FiveCylinder = l.Five, Volume = 1f, Speed = 28f, Driven = 1f, Gear = 3, TurboSpool = l.Five ? .4f : .5f };
        int n = (int)(TurboLift.Length * TurboRate);
        var tr = new TurboTrace { Mix = new float[n], Whistle = new float[n], Vent = new float[n], Wastegate = new float[n] };
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)TurboRate;
            snd.Rpm = l.RpmAt(t); snd.Throttle = l.Throttle(t);
            snd.AntiLag = antiLagAt != null ? antiLagAt(t) : l.AntiLag && t >= TurboLift.At;
            tr.Mix[i] = snd.NextSample();
            tr.Whistle[i] = snd.VoiceWhistle; tr.Vent[i] = snd.VoiceVent; tr.Wastegate[i] = snd.VoiceWastegate;
            if (i % (TurboRate / 100) == 0) { tr.Shaft.Add(snd.Shaft); tr.Boost.Add(snd.BoostNow); }
        }
        snd.Free();
        return tr;
    }

    static void TurboWriteWav(string path, float[] pcm, float gain = 1f)
    {
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8.ToArray()); w.Write(36 + pcm.Length * 2); w.Write("WAVEfmt "u8.ToArray());
        w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(TurboRate); w.Write(TurboRate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(pcm.Length * 2);
        foreach (float x in pcm) w.Write((short)(Math.Clamp(x * gain, -1f, 1f) * 32000f));
    }

    /// <summary>The RMS of a signal in windows of `ms` milliseconds.</summary>
    static float[] TurboEnvelope(float[] x, float ms)
    {
        int win = Math.Max(1, (int)(ms * TurboRate / 1000f));
        var env = new float[x.Length / win];
        for (int w = 0; w < env.Length; w++) { double sq = 0; for (int i = 0; i < win; i++) { float v = x[w * win + i]; sq += v * v; } env[w] = (float)Math.Sqrt(sq / win); }
        return env;
    }

    static int TurboPeakIndex(float[] env, int from) { int p = from; for (int i = from; i < env.Length; i++) if (env[i] > env[p]) p = i; return p; }

    /// <summary>The largest rise of an envelope after it has gone down: over the stretch from the peak where the envelope is still at least a fifth of it, the greatest env[i] / (the lowest value since the peak).
    /// A smooth decay is about 1 (a little over for the noise's ripple); a flutter, the voice gated on and off, is several.</summary>
    static float TurboReRise(float[] env, int from)
    {
        int peak = TurboPeakIndex(env, from);
        float lowest = env[peak], worst = 1f;
        for (int i = peak; i < env.Length; i++)
        {
            lowest = Math.Min(lowest, Math.Max(env[i], 1e-9f));
            if (env[i] >= .2f * env[peak]) worst = Math.Max(worst, env[i] / lowest);
        }
        return worst;
    }

    /// <summary>The seconds from the envelope's peak until it has fallen below a tenth of it.</summary>
    static float TurboLength(float[] env, float ms, int from)
    {
        int peak = TurboPeakIndex(env, from), end = peak;
        while (end < env.Length && env[end] > .1f * env[peak]) end++;
        return (end - peak) * ms / 1000f;
    }

    static float TurboRms(float[] x, float t0, float t1)
    {
        int a = (int)(t0 * TurboRate), b = Math.Min(x.Length, (int)(t1 * TurboRate)); double sq = 0;
        for (int i = a; i < b; i++) sq += x[i] * x[i];
        return (float)Math.Sqrt(sq / Math.Max(1, b - a));
    }

    /// <summary>Zero crossings a second of a stretch: for a tone twice its pitch, for noise a measure of how bright it is.</summary>
    static float TurboCrossings(float[] x, float t0, float t1)
    {
        int a = (int)(t0 * TurboRate), b = Math.Min(x.Length - 1, (int)(t1 * TurboRate)), c = 0;
        for (int i = a + 1; i <= b; i++) if (x[i - 1] < 0 != x[i] < 0) c++;
        return c / (t1 - t0);
    }

    /// <summary>The variation of the 2 ms RMS over a stretch (stddev / mean): about 0.1 for steady noise, much more for noise that beats with the engine's firings.</summary>
    static float TurboBeat(float[] x, float t0, float t1)
    {
        var env = TurboEnvelope(x, 2f);
        int a = (int)(t0 * 500), b = Math.Min(env.Length, (int)(t1 * 500));
        double mean = 0; for (int i = a; i < b; i++) mean += env[i]; mean /= Math.Max(1, b - a);
        double var = 0; for (int i = a; i < b; i++) var += (env[i] - mean) * (env[i] - mean);
        return mean < 1e-9 ? 0f : (float)(Math.Sqrt(var / Math.Max(1, b - a)) / mean);
    }

    /// <summary>
    /// The turbo's voices (EngineSound): a lift of the pedal vents the charge pipe's pressure once, short and in proportion to the pressure (no flutter in any lift), the shaft's whistle spins down slower than
    /// the boost and is kept up by the Sputnik's anti-lag, the Sputnik's wastegate is the exhaust side at full throttle and not the intake side's vent, and the Diesel and the Petrol are what they were.
    /// TURBOWAV=dir also writes the lifts as WAV files (the mix and each voice on its own) and a report; TURBOGOLDEN=1 prints the Diesel's and the Petrol's reference numbers.
    /// </summary>
    void TurboSoundTest()
    {
        if (OS.HasEnvironment("TURBOGOLDEN"))
        {
            foreach (int g in new[] { 0, 1 }) GD.Print($"TURBOGOLDEN grade {g}: {string.Join(", ", TurboGoldenRms(g).Select(v => v.ToString("0.00000", System.Globalization.CultureInfo.InvariantCulture) + "f"))}");
            return;
        }
        var report = new List<string>();
        void Say(string line) { report.Add(line); GD.Print(line); }
        const float At = TurboLift.At;
        var T = TurboLifts.ToDictionary(l => l.Name, l => TurboRenderLift(l));
        var E = T.ToDictionary(kv => kv.Key, kv => TurboEnvelope(kv.Value.Vent, 10f));
        int from = (int)(At * 100);      // (10 ms windows)
        var N = new Dictionary<string, (float peak, float len, float rerise)>();
        foreach (var l in TurboLifts)
        {
            var e = E[l.Name];
            N[l.Name] = (e.Skip(from).DefaultIfEmpty(0f).Max(), TurboLength(e, 10f, from), TurboReRise(e, from));
            Say($"TURBO {l.Name,-18} vent peak {N[l.Name].peak:F3}  length {N[l.Name].len * 1000f:F0} ms  re-rise {N[l.Name].rerise:F2}  shaft at +0.5 s {T[l.Name].ShaftAt(l.VentTime + .5f):F2} (boost {T[l.Name].BoostAt(l.VentTime + .5f):F2})");
        }

        // The detector itself: a vent gated on and off like the old flutter is caught, a smooth decay is not.
        var rnd = new Random(7);
        float[] gated = new float[TurboRate], smooth = new float[TurboRate];
        for (int i = 0; i < gated.Length; i++)
        {
            float t = i / (float)TurboRate, n = (float)rnd.NextDouble() * 2 - 1, decay = MathF.Exp(-t / .2f);
            smooth[i] = n * decay; gated[i] = n * decay * ((t * 24f) % 1f < .5f ? 1f : .15f);
        }
        float gatedRise = TurboReRise(TurboEnvelope(gated, 10f), 0), smoothRise = TurboReRise(TurboEnvelope(smooth, 10f), 0);
        Say($"TURBO detector: gated noise re-rise {gatedRise:F2}, smooth decay {smoothRise:F2}");
        Check(gatedRise > 2.5f && smoothRise < 1.5f, "turbo sound: the flutter detector catches a gated vent (the old fluttering blow-off) and passes a smooth decay");

        // 1. no flutter in any lift, of any fuel; the vent fires once, when the pedal comes below 0.3 and not before.
        foreach (var l in TurboLifts.Where(l => l.Grade == 2))
            Check(N[l.Name].rerise < 1.5f, $"turbo sound: {l.Name}: the vent is one smooth decay with no flutter (re-rise {N[l.Name].rerise:F2})");
        {
            var fast = E["jet-fast"]; int pk = TurboPeakIndex(fast, from);
            Check(TurboRms(T["jet-fast"].Vent, 0f, TurboLift.At) < 1e-6f, "turbo sound: no vent before the pedal comes up");
            Check(Math.Abs(pk * .01f - TurboLift.At - .7f * .10f) < .05f, $"turbo sound: the vent peaks within 50 ms of the pedal coming below 0.3 (at {pk * .01f - TurboLift.At:F2} s after the lift began)");
            int bursts = 0; bool on = false;
            for (int i = from; i < fast.Length; i++) { bool now = fast[i] > .3f * fast[pk]; if (now && !on) bursts++; on = now; }
            Check(bursts == 1, $"turbo sound: a lift vents once ({bursts} bursts above 30 % of the peak)");
        }

        // 2. the vent follows the pressure: short and clear after a quick lift off full boost, restrained after a slow lift or with little boost.
        float fastPeak = N["jet-fast"].peak, fastLen = N["jet-fast"].len;
        Check(fastLen >= .2f && fastLen <= .65f, $"turbo sound: a quick lift off full boost vents for a short while ({fastLen * 1000f:F0} ms to a tenth)");
        Check(N["jet-slow"].peak <= .35f * fastPeak, $"turbo sound: a pedal let up over 1.2 s vents little ({N["jet-slow"].peak / fastPeak:P0} of the quick lift's)");
        Check(N["jet-low-boost"].peak <= .25f * fastPeak && N["jet-low-boost"].peak > 0f && N["jet-low-boost"].len < fastLen, $"turbo sound: a quick lift with little boost on is a restrained, shorter vent ({N["jet-low-boost"].peak / fastPeak:P0}, {N["jet-low-boost"].len * 1000f:F0} ms)");
        Check(N["jet-half-boost"].peak > N["jet-low-boost"].peak && N["jet-half-boost"].peak < fastPeak && N["jet-half-boost"].len < fastLen, $"turbo sound: half the pressure vents less and shorter than the full ({N["jet-half-boost"].peak / fastPeak:P0}, {N["jet-half-boost"].len * 1000f:F0} ms)");

        // 3. the whistle is the shaft's: it outlives the boost and falls in pitch, without a step.
        {
            var j = T["jet-fast"]; float t0 = TurboLift.At + .1f;
            Check(j.BoostAt(TurboLift.At) > .9f && j.ShaftAt(TurboLift.At) > .9f, "turbo sound: full boost, the shaft at speed before the lift");
            Check(j.BoostAt(t0 + .5f) < .1f && j.ShaftAt(t0 + .5f) > .3f, $"turbo sound: half a second after the lift the boost is gone ({j.BoostAt(t0 + .5f):F2}) and the shaft still spins ({j.ShaftAt(t0 + .5f):F2})");
            bool falling = true; for (int i = (int)(TurboLift.At * 100) + 1; i < j.Shaft.Count; i++) if (j.Shaft[i] > j.Shaft[i - 1] + 1e-4f) falling = false;
            Check(falling, "turbo sound: after the lift the shaft only slows down");
            float early = TurboCrossings(j.Whistle, t0, t0 + .1f), late = TurboCrossings(j.Whistle, t0 + .8f, t0 + .9f);
            Check(late < .75f * early && late > 0f, $"turbo sound: the whistle falls in pitch after the lift ({early / 2f:F0} Hz to {late / 2f:F0} Hz, by the zero crossings)");
        }

        // 4. the Sputnik: the intake side's vent is its own (shorter and duller than the jet fuel's), the wastegate is the exhaust side at full throttle and beats with the five's firings.
        {
            var s = T["sputnik-fast"]; var j = T["jet-fast"];
            Check(N["sputnik-fast"].len <= .85f * fastLen && N["sputnik-fast"].len >= .12f, $"turbo sound: the Sputnik's vent is shorter than the jet fuel's ({N["sputnik-fast"].len * 1000f:F0} against {fastLen * 1000f:F0} ms)");
            float cs = TurboCrossings(s.Vent, TurboLift.VentTimeOf(.10f), TurboLift.VentTimeOf(.10f) + .15f), cj = TurboCrossings(j.Vent, TurboLift.VentTimeOf(.10f), TurboLift.VentTimeOf(.10f) + .15f);
            Check(cs < .85f * cj, $"turbo sound: the Sputnik's vent is duller than the jet fuel's ({cs:F0} against {cj:F0} crossings a second)");
            float wgBefore = TurboRms(s.Wastegate, At - .4f, At), wgAfter = TurboRms(s.Wastegate, At + .3f, At + .6f);
            Check(wgBefore > .003f && wgAfter < .05f * wgBefore, $"turbo sound: the Sputnik's wastegate is open at full throttle and shut at the lift (rms {wgBefore:F4} then {wgAfter:F4})");
            Check(TurboRms(j.Wastegate, 0f, TurboLift.Length) == 0f, "turbo sound: the jet fuel has no wastegate voice");
            Check(TurboRms(s.Vent, 0f, At) < 1e-6f, "turbo sound: the wastegate is not the vent (no vent voice while the pedal is down)");
            float beatSputnik = TurboBeat(s.Wastegate, At - .4f, At);
            var plain = new float[s.Wastegate.Length]; var rn = new Random(3); for (int i = 0; i < plain.Length; i++) plain[i] = (float)rn.NextDouble() * 2 - 1;
            float beatNoise = TurboBeat(plain, 0f, 1f);
            Check(beatSputnik > 2f * beatNoise && beatSputnik > .3f, $"turbo sound: the wastegate's rasp beats with the five's firings (variation {beatSputnik:F2}, steady noise {beatNoise:F2})");
            Check(TurboRms(T["sputnik-slow"].Wastegate, At + .9f, At + 1.2f) < .05f * TurboRms(T["sputnik-slow"].Wastegate, At - .4f, At), "turbo sound: the wastegate is shut while the pedal is let up slowly too");
        }

        // 5. anti-lag: the shaft is kept spinning and the whistle stays; the vent and the wastegate are not touched by it.
        {
            var plain = T["sputnik-fast"]; var al = T["sputnik-antilag"]; float t = TurboLift.VentTimeOf(.10f);
            Check(al.ShaftAt(t + 1.4f) >= .3f && plain.ShaftAt(t + 1.4f) <= .15f, $"turbo sound: with anti-lag the shaft keeps spinning ({al.ShaftAt(t + 1.4f):F2}), without it it has run down ({plain.ShaftAt(t + 1.4f):F2})");
            float ca = TurboCrossings(al.Whistle, t + 1.3f, t + 1.4f), cp = TurboCrossings(plain.Whistle, t + 1.3f, t + 1.4f);
            Check(ca > 1.3f * cp, $"turbo sound: the anti-lag whistle stays high ({ca / 2f:F0} Hz against {cp / 2f:F0} Hz)");
            Check(al.Vent.SequenceEqual(plain.Vent) && al.Wastegate.SequenceEqual(plain.Wastegate), "turbo sound: anti-lag changes neither the vent nor the wastegate");
            var jetAl = TurboRenderLift(TurboLifts.First(x => x.Name == "jet-fast"), tt => tt >= At);
            Check(jetAl.Shaft.SequenceEqual(T["jet-fast"].Shaft), "turbo sound: the jet fuel (four cylinders, no anti-lag) ignores the anti-lag flag");
            var released = TurboRenderLift(TurboLifts.First(x => x.Name == "sputnik-antilag"), tt => tt >= At && tt < At + 1f);
            Check(released.ShaftAt(At + 1f) >= .4f && released.ShaftAt(At + 2.5f) < .2f, $"turbo sound: when anti-lag stops (the pause, a collision, a replay) the shaft runs down again ({released.ShaftAt(At + 1f):F2} to {released.ShaftAt(At + 2.5f):F2})");
            var beats = AntiLagPattern.Beats;
            Check(beats.Length == 5 && beats.Max() >= 3f * beats.Min() && AntiLagPattern.Gap(4, 1f, 1f) < AntiLagPattern.Gap(4, 0f, 1f), "turbo sound: the anti-lag's bangs come in the five-cylinder's beat (four quick, one long) and faster at high revs");
        }

        // 6. the Diesel and the Petrol are what they were: no turbo voice, and the whole mix of the reference drive unchanged.
        foreach (var name in new[] { "diesel-fast", "petrol-fast" })
            Check(TurboRms(T[name].Whistle, 0, TurboLift.Length) == 0f && TurboRms(T[name].Vent, 0, TurboLift.Length) == 0f && TurboRms(T[name].Wastegate, 0, TurboLift.Length) == 0f, $"turbo sound: {name}: no turbo voices");
        foreach (var (g, golden) in new[] { (0, TurboGoldenDiesel), (1, TurboGoldenPetrol) })
        {
            var now = TurboGoldenRms(g);
            float worst = 0f; for (int i = 0; i < golden.Length; i++) worst = Math.Max(worst, Math.Abs(now[i] - golden[i]));
            Check(now.Length == golden.Length && worst < 2e-4f, $"turbo sound: the {(g == 0 ? "Diesel" : "Petrol")}'s voice is unchanged (largest difference of a half second's RMS {worst:E1})");
        }
        {
            var snd = new EngineSound { Grade = 1, FiveCylinder = true, AntiLag = true, Volume = 1f };
            float w = 0; for (int i = 0; i < TurboRate; i++) { snd.Rpm = 6000; snd.Throttle = i < TurboRate / 2 ? 1 : 0; snd.NextSample(); w = Math.Max(w, Math.Abs(snd.VoiceWhistle) + Math.Abs(snd.VoiceVent) + Math.Abs(snd.VoiceWastegate)); }
            snd.Free();
            Check(w == 0f, "turbo sound: a Petrol voice with the five-cylinder and anti-lag flags still has no turbo");
        }

        // 7. replays and the title demonstration drive the sound from the lap's own rpm and throttle: the same inputs give the same sound, sample for sample.
        {
            var again = TurboRenderLift(TurboLifts.First(x => x.Name == "sputnik-antilag"));
            Check(again.Mix.SequenceEqual(T["sputnik-antilag"].Mix), "turbo sound: the same inputs render the same samples");
        }

        // 8. the scene's rule for anti-lag (the bangs and the held whistle follow it): the Sputnik, live, the pedal up and the revs high; never in a replay or after a crash.
        {
            var scene = new RaceScene(); var type = typeof(RaceScene); const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic;
            void Set(string field, object value) => type.GetField(field, f).SetValue(scene, value);
            var rule = type.GetMethod("AntiLagActive", f);
            bool Active(float throttle, float rpm) => (bool)rule.Invoke(scene, new object[] { throttle, rpm });
            var parameters = new CarParams(); Set("_params", parameters);
            float redline = parameters.Redline;
            var stateType = type.GetNestedType("State", BindingFlags.NonPublic);
            var rallyFuel = type.GetField("_rallyFuel", BindingFlags.Static | BindingFlags.NonPublic);
            var savedFuel = (FuelGrade)rallyFuel.GetValue(null);
            Set("_runMode", Enum.Parse(type.GetField("_runMode", f).FieldType, "Rally"));
            rallyFuel.SetValue(null, FuelGrade.Sputnik);
            Set("_state", Enum.Parse(stateType, "Racing"));
            bool live = Active(0f, redline * .8f);
            rallyFuel.SetValue(null, FuelGrade.Rocket); bool jet = Active(0f, redline * .8f);
            rallyFuel.SetValue(null, FuelGrade.Sputnik);
            bool pedal = Active(.3f, redline * .8f), slow = Active(0f, redline * .3f);
            Set("_collisionQuiet", 1f); bool crash = Active(0f, redline * .8f); Set("_collisionQuiet", 0f);
            Set("_state", Enum.Parse(stateType, "Paused")); bool paused = Active(0f, redline * .8f);
            rallyFuel.SetValue(null, savedFuel);
            scene.Free();
            Check(live && !jet && !pedal && !slow && !crash && !paused, $"turbo sound: the anti-lag rule: live Sputnik {live}; jet fuel {jet}, pedal down {pedal}, low revs {slow}, after a crash {crash}, paused {paused}");
        }

        BackfireReplayTest();

        if (OS.GetEnvironment("TURBOWAV") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            foreach (var l in TurboLifts)
            {
                TurboWriteWav(Path.Combine(dir, $"{l.Name}.wav"), T[l.Name].Mix);
                if (l.Grade != 2) continue;
                TurboWriteWav(Path.Combine(dir, $"{l.Name}-vent-only.wav"), T[l.Name].Vent, 2f);
                TurboWriteWav(Path.Combine(dir, $"{l.Name}-whistle-only.wav"), T[l.Name].Whistle, 2f);
                if (l.Five) TurboWriteWav(Path.Combine(dir, $"{l.Name}-wastegate-only.wav"), T[l.Name].Wastegate, 8f);
            }
            File.WriteAllLines(Path.Combine(dir, "report.txt"), report);
            GD.Print($"TURBOWAV: {TurboLifts.Length} lifts and {report.Count} report lines in {dir}");
        }
    }

    /// <summary>One run of the scene's exhaust controller (UpdateBackfire) at the fixed 60 Hz tick, with a spy for a voice: the ticks at which it was asked for a bang (and whether an explosion).</summary>
    static List<(int Tick, int Bangs, int Explosions)> BackfireRun(bool replay, FuelGrade fuel, ulong seed, float clockOffset, int ticks, Func<int, (float Throttle, float Rpm)> drive, int[] maxPops = null)
    {
        var scene = new RaceScene(); var type = typeof(RaceScene); const BindingFlags f = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
        void Set(string field, object value) => type.GetField(field, f).SetValue(scene, value);
        var parameters = new CarParams(); var car = new CarSim(parameters, new FlatGround()); var snd = new EngineSound();
        Set("_params", parameters); Set("_car", car); Set("_snd", snd);
        var stateType = type.GetNestedType("State", BindingFlags.NonPublic);
        var savedFuel = (FuelGrade)type.GetField("_rallyFuel", f).GetValue(null);
        if (replay)
        {
            scene.ViewerMode = true;      // (a viewer plays a lap's tape like the title's replay: the fuel is the ghost slot's)
            var ghosts = (Ghost[])type.GetField("_ghosts", f).GetValue(scene);
            ghosts[fuel switch { FuelGrade.Sputnik => 4, FuelGrade.Rocket => 3, FuelGrade.Diesel => 2, _ => 1 }] = new Ghost();
        }
        else
        {
            Set("_runMode", Enum.Parse(type.GetField("_runMode", f).FieldType, "Rally")); type.GetField("_rallyFuel", f).SetValue(null, fuel);
            Set("_state", Enum.Parse(stateType, "Racing"));
            ((RandomNumberGenerator)type.GetField("_rng", f).GetValue(scene)).Seed = seed;
        }
        var update = type.GetMethod("UpdateBackfire", f); var clear = type.GetMethod("ClearCarFeedback", f);
        var log = new List<(int, int, int)>();
        int lastBangs = 0, lastExpl = 0;
        for (int tick = 0; tick < ticks; tick++)
        {
            var (thr, rpm) = drive(tick);
            car.Throttle = thr; car.Rpm = rpm;
            Set("_replayClock", clockOffset + tick / 60f);
            update.Invoke(scene, new object[] { 1f / 60f });
            if (maxPops != null) maxPops[0] = Math.Max(maxPops[0], (int)type.GetField("_pops", f).GetValue(scene));
            if (snd.Reports != lastBangs || snd.Explosions != lastExpl) { log.Add((tick, snd.Reports - lastBangs, snd.Explosions - lastExpl)); lastBangs = snd.Reports; lastExpl = snd.Explosions; }
        }
        type.GetField("_rallyFuel", f).SetValue(null, savedFuel);
        snd.Free(); scene.Free();
        return log;
    }

    /// <summary>
    /// A replay's exhaust bangs: the lift's pops and the Sputnik's anti-lag crackle are the same every time the replay plays (a generator of the replay's own, seeded by its clock) and a live run
    /// still draws from the scene's own generator exactly as it always did (the same draws in the same order).
    /// </summary>
    void BackfireReplayTest()
    {
        // The lift: 0.5 s at full throttle at 6000 rpm, then the foot off for 3 s with the revs running down.
        Func<int, (float, float)> lift = t => t < 30 ? (1f, 6000f) : (0f, Math.Max(3500f, 6000f - (t - 30) * 14f));
        const int Ticks = 240;

        // Live, Petrol: the pops of one lift against an independent copy of the old draw order (count, first delay, then a strength and a delay for each pop).
        var liveRng = new RandomNumberGenerator { Seed = 4242 };
        int count = liveRng.RandiRange(2, 5); float timer = liveRng.RandfRange(0.03f, 0.12f);
        var expected = new List<int>(); int pops = count;
        for (int t = 30; t < Ticks && pops > 0; t++)
        {
            timer -= 1f / 60f;
            if (timer > 0) continue;
            liveRng.RandfRange(0.6f, 1f); expected.Add(t); pops--; timer = liveRng.RandfRange(0.06f, 0.22f);
        }
        var live = BackfireRun(false, FuelGrade.Petrol, 4242, 0f, Ticks, lift);
        Check(live.Select(e => e.Tick).SequenceEqual(expected) && live.Count == count, $"backfire: a live lift's pops draw from the scene's generator as before ({live.Count} pops at ticks {string.Join(",", live.Select(e => e.Tick))})");

        // Live, Sputnik: the anti-lag's bangs against an independent copy of the old draws (a big one or a small one, its strength, a flame, the next gap).
        var rg = new RandomNumberGenerator { Seed = 777 };
        var expectedAl = new List<int>(); float al = 0.05f; int beat = 0;
        for (int t = 30; t < Ticks; t++)
        {
            float rpm = lift(t).Item2;
            if (rpm <= new CarParams().Redline * 0.4f) { al = 0.05f; continue; }
            al -= 1f / 60f;
            if (al > 0) continue;
            float load = Math.Clamp((rpm / new CarParams().Redline - 0.4f) / 0.5f, 0f, 1f);
            beat = (beat + 1) % 5;
            bool big = rg.Randf() < (beat == 4 ? 0.5f : 0.08f); rg.RandfRange(0.5f, 1f);
            if (!big) rg.Randf();
            al = AntiLagPattern.Gap(beat, load, rg.RandfRange(0.85f, 1.15f)); expectedAl.Add(t);
        }
        var liveAl = BackfireRun(false, FuelGrade.Sputnik, 777, 0f, Ticks, lift);
        Check(liveAl.Count >= 8 && liveAl.Select(e => e.Tick).SequenceEqual(expectedAl), $"backfire: a live Sputnik's anti-lag draws from the scene's generator as before ({liveAl.Count} bangs, {expectedAl.Count} expected)");

        // Replays: the same sequence on every play, after a wrap and from a clock that is the same; and it is the clock that seeds it.
        foreach (var (name, fuel) in new[] { ("Petrol's pops", FuelGrade.Petrol), ("Sputnik's anti-lag", FuelGrade.Sputnik) })
        {
            var a = BackfireRun(true, fuel, 0, 12.5f, Ticks, lift); var b = BackfireRun(true, fuel, 0, 12.5f, Ticks, lift); var other = BackfireRun(true, fuel, 0, 31.1f, Ticks, lift);
            Check(a.Count >= 2 && a.SequenceEqual(b), $"backfire: a replay's {name} are the same every time it plays ({a.Count} bangs)");
            Check(!a.SequenceEqual(other), $"backfire: a replay's {name} are seeded by the clock (another lift time, another crackle)");
        }
        var sputnikReplay = BackfireRun(true, FuelGrade.Sputnik, 0, 12.5f, Ticks, lift);
        Check(sputnikReplay.Count >= 8, $"backfire: the Sputnik's anti-lag crackles in a replay ({sputnikReplay.Count} bangs in 3.5 s)");
        // Two lifts in one replay: the second crackles exactly as it would have on its own (each crackle starts at the top of the beat), so a loop or a second lift is no different from the first.
        Func<int, (float, float)> twice = t => t < 30 || t is >= 150 and < 180 ? (1f, 6000f) : (0f, 5200f);
        var both = BackfireRun(true, FuelGrade.Sputnik, 0, 12.5f, 330, twice);
        var alone = BackfireRun(true, FuelGrade.Sputnik, 0, 12.5f + 150 / 60f, 180, t => t < 30 ? (1f, 6000f) : (0f, 5200f));
        Check(alone.Count >= 6 && both.Where(e => e.Tick >= 180).Select(e => (e.Tick - 150, e.Bangs, e.Explosions)).SequenceEqual(alone), $"backfire: a second lift in a replay crackles as it would on its own ({alone.Count} bangs)");
        Check(BackfireRun(true, FuelGrade.Sputnik, 0, 12.5f, Ticks, t => t < 30 ? (1f, 6000f) : (0.5f, 6000f)).Count == 0, "backfire: no crackle in a replay while the pedal is down");

        // A Sputnik's lift with the anti-lag running queues no burst of pops ahead of the crackle, in a replay too; with the revs too low for anti-lag the ordinary pops are still there.
        var popsHigh = new int[1]; var popsLow = new int[1]; var popsPetrol = new int[1];
        BackfireRun(true, FuelGrade.Sputnik, 0, 12.5f, Ticks, lift, popsHigh);
        BackfireRun(true, FuelGrade.Sputnik, 0, 12.5f, Ticks, t => t < 30 ? (1f, 6000f) : (0f, 2600f), popsLow);
        BackfireRun(true, FuelGrade.Petrol, 0, 12.5f, Ticks, lift, popsPetrol);
        Check(popsHigh[0] == 0 && popsLow[0] > 0 && popsPetrol[0] > 0, $"backfire: in a replay the Sputnik's anti-lag takes the lift's place and the ordinary pops stay for the low revs and the Petrol (queued pops: {popsHigh[0]}, {popsLow[0]}, {popsPetrol[0]})");
        AntiLagWhistleTest();
        Check(BackfireRun(true, FuelGrade.Rocket, 0, 12.5f, Ticks, lift).Count(e => e.Explosions > 0) == 0, "backfire: the jet fuel's lift in a replay has no anti-lag crackle (that is the Sputnik's)");
    }

    /// <summary>The engine's held whistle (the scene's AntiLagWhistle, the frame update's `_snd.AntiLag`): on for the Sputnik with the pedal up and the revs high in live play, the title's real replay and the viewer; not in a pause, not with the pedal down, not for another fuel.</summary>
    void AntiLagWhistleTest()
    {
        const BindingFlags f = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
        var type = typeof(RaceScene); var stateType = type.GetNestedType("State", BindingFlags.NonPublic);
        var savedFuel = (FuelGrade)type.GetField("_rallyFuel", f).GetValue(null);
        bool Whistle(string where, FuelGrade fuel, float throttle, float rpm, bool paused = false)
        {
            var scene = new RaceScene(); var parameters = new CarParams(); var car = new CarSim(parameters, new FlatGround()) { Throttle = throttle, Rpm = rpm };
            void Set(string field, object value) => type.GetField(field, f).SetValue(scene, value);
            Set("_params", parameters); Set("_car", car);
            if (where == "live") { Set("_runMode", Enum.Parse(type.GetField("_runMode", f).FieldType, "Rally")); type.GetField("_rallyFuel", f).SetValue(null, fuel); Set("_state", Enum.Parse(stateType, "Racing")); }
            else
            {
                var ghost = new Ghost(); var puppet = new CarSim(parameters, new FlatGround());
                if (where == "title") for (int i = 0; i < 40; i++) ghost.Record(i / 30f, puppet, 0, 0, 0);      // (a real replay: the title with a ghost of more than 30 samples)
                else scene.ViewerMode = true;
                ((Ghost[])type.GetField("_ghosts", f).GetValue(scene))[fuel switch { FuelGrade.Sputnik => 4, FuelGrade.Rocket => 3, _ => 1 }] = ghost;
            }
            bool replaying = (bool)type.GetProperty("Replaying", f).GetValue(scene);
            if (where == "title" && !replaying) throw new InvalidOperationException("the title's replay was not set up");
            bool on = (bool)type.GetMethod("AntiLagWhistle", f).Invoke(scene, new object[] { paused });
            scene.Free();
            return on;
        }
        float high = new CarParams().Redline * .8f;
        bool live = Whistle("live", FuelGrade.Sputnik, 0f, high), title = Whistle("title", FuelGrade.Sputnik, 0f, high), viewer = Whistle("viewer", FuelGrade.Sputnik, 0f, high);
        bool paused = Whistle("title", FuelGrade.Sputnik, 0f, high, paused: true), pedal = Whistle("title", FuelGrade.Sputnik, .5f, high), slow = Whistle("title", FuelGrade.Sputnik, 0f, 1500f), jet = Whistle("title", FuelGrade.Rocket, 0f, high);
        type.GetField("_rallyFuel", f).SetValue(null, savedFuel);
        Check(live && title && viewer && !paused && !pedal && !slow && !jet, $"anti-lag whistle: held in live play {live}, the title's replay {title} and the viewer {viewer}; not paused {paused}, with the pedal down {pedal}, at low revs {slow}, on the jet fuel {jet}");
    }
}
