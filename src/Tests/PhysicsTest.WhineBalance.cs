using System;
using System.IO;
using System.Linq;
using CCCPRacing.Audio;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void WhineBalanceTest()
    {
        const int rate = 22050;
        string output = OS.GetCmdlineUserArgs().First(a => a.StartsWith("--whine-out="))[12..];
        if (Directory.Exists(output)) { Check(false, "whine: choose a fresh review folder"); return; }
        Directory.CreateDirectory(output);
        SoundBalance.EngineGain = SoundBalance.RoadGain = 1;
        float[] Render(float level, float driven = 1, float engineLevel = 1, float speed = 25)
        {
            var sound = new EngineSound { GearboxWhineLevel = level, Driven = driven,
                EngineLevel = engineLevel, Speed = speed, Gear = 3 };
            var samples = new float[rate * 9];
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)rate;
                sound.Rpm = t < 3 ? 2500 + t * 1100 : t < 6 ? 5800 - (t - 3) * 600 : 4000 + (t - 6) * 700;
                sound.Throttle = t < 3 || t >= 6 ? 1 : 0;
                if (i == rate * 6) { sound.Gear = 4; sound.GearChange(true, 1); }
                samples[i] = sound.NextSample();
            }
            sound.Free();
            return samples;
        }
        void Wav(string name, float[] samples)
        {
            using var f = new BinaryWriter(File.Open(Path.Combine(output, name + ".wav"), FileMode.CreateNew));
            f.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); f.Write(36 + samples.Length * 2);
            f.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); f.Write(16); f.Write((short)1); f.Write((short)1);
            f.Write(rate); f.Write(rate * 2); f.Write((short)2); f.Write((short)16);
            f.Write(System.Text.Encoding.ASCII.GetBytes("data")); f.Write(samples.Length * 2);
            foreach (float sample in samples) f.Write((short)Math.Clamp((int)MathF.Round(sample * 32767), -32767, 32767));
        }
        var before = Render(.10f); var after = Render(.05f); var noWhine = Render(0);
        Wav("ennen", before); Wav("jalkeen", after);
        Wav("ennen-jalkeen", before.Concat(new float[rate]).Concat(after).ToArray());
        Check(after.All(float.IsFinite) && after.Max(v => Math.Abs(v)) < .98f,
            "whine: acceleration, overrun and loaded shift retain output headroom");
        Check(!before.SequenceEqual(after), "whine: mix changes under load and on overrun");
        foreach (var state in new[] { (driven: 0f, engine: 1f, speed: 25f),
            (driven: 1f, engine: 0f, speed: 25f), (driven: 1f, engine: 1f, speed: 0f) })
            Check(Render(.10f, state.driven, state.engine, state.speed)
                .SequenceEqual(Render(.05f, state.driven, state.engine, state.speed)),
                $"whine: disengaged/stopped/dead engine unchanged ({state})");
        // Subtract the identical deterministic engine/road mix: the added voice remains stronger
        // on lift-off than under power, without replacing the original engine or shift transient.
        double VoiceEnergy(float[] samples, int start, int end) => Enumerable.Range(start * rate, (end - start) * rate)
            .Sum(i => Math.Pow(samples[i] - noWhine[i], 2));
        Check(VoiceEnergy(after, 4, 5) > VoiceEnergy(after, 1, 2) * 2,
            "whine: overrun still exposes the mechanical voice more strongly than power");
        File.WriteAllText(Path.Combine(output, "README.md"),
            "# Vaihteistovinkunan balanssi\n\nennen-jalkeen.wav: vanha 9 s, tauko 1 s, uusi 9 s. " +
            "Molemmissa sama tuotannon EngineSound ja samat ajoarvot, ei normalisointia. " +
            "0–3 s kiihdytys, 3–6 s moottorijarrutus, 6 s kuormitettu vaihto ja uusi kiihdytys. " +
            "Vain vinkunan kerroin muuttuu 0,10 → 0,05 (−6,02 dB). " +
            "Tämä on synteettinen ajoarvosarja, ei radalla nauhoitettu ajosuoritus.\n");
        GD.Print($"Whine review: {output}; new peak {after.Max(v => Math.Abs(v)):F4}");
    }
}
