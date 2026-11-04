using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void EngineCharacterTest()
    {
        const int rate = 22050;
        string output = OS.GetCmdlineUserArgs().First(a => a.StartsWith("--engine-out="))[13..];
        if (Directory.Exists(output)) { Check(false, "engine: choose a fresh review folder"); return; }
        Directory.CreateDirectory(output);
        SoundBalance.EngineGain = SoundBalance.RoadGain = 1;
        var parameters = new CarParams(); var ground = new FlatGround();
        var car = new CarSim(parameters, ground); var ghost = new Ghost();
        // Idle, free rev, loaded acceleration, lift-off, clutch/shift and another pull.
        for (int frame = 0; frame <= 15 * 30; frame++)
        {
            float t = frame / 30f;
            car.Rpm = t < 2 ? 900 : t < 4 ? 900 + (t - 2) * 2300 : t < 8 ? 2500 + (t - 4) * 1050
                : t < 11 ? 6700 - (t - 8) * 700 : t < 11.3f ? 4600 : 4200 + (t - 11.3f) * 650;
            car.Throttle = t < 2 || t >= 8 && t < 11.3f ? 0 : 1;
            car.Clutch = t < 4 || t >= 11 && t < 11.3f;
            car.Gear = t < 11.2f ? 3 : 4;
            car.Vel = new Vector3(0, 0, t < 4 ? 0 : -25);
            ghost.Record(t, car, 0, 0, 0);
        }
        ghost = ghost.CopyWithTime(15);
        string ghostPath = Path.Combine(output, "engine-driving-inputs.bin"); ghost.Save(ghostPath);
        var loaded = Ghost.Load(ghostPath, parameters, ground);
        float[] Render(Ghost source, float character, float engineLevel = 1, float engineGain = 1.5f, float rasp = 1, float aggression = .5f, float closedThrottle = 1f)
        {
            source.Rewind(); var puppet = new CarSim(parameters, ground);
            var sound = new EngineSound { EngineCharacterMix = character, EngineLevel = engineLevel,
                CombustionGain = engineGain, RallyRaspMix = rasp, EngineAggressionMix = aggression, ClosedThrottleRasp = closedThrottle };
            var data = new float[rate * 15];
            for (int i = 0; i < data.Length; i++)
            {
                if (i % 735 == 0)
                {
                    source.Apply(i / (float)rate, puppet, 1f / 30);
                    sound.Rpm = puppet.Rpm; sound.Throttle = puppet.Throttle; sound.Speed = puppet.Speed;
                    sound.Gear = puppet.Gear; sound.Driven = puppet.Clutch || puppet.Neutral ? 0 : 1;
                    sound.Drivetrain(puppet.Gear, puppet.Clutch, puppet.Neutral, puppet.Throttle, puppet.Rpm, true);
                }
                data[i] = sound.NextSample();
            }
            sound.Free(); return data;
        }
        void Wav(string name, float[] data)
        {
            using var f = new BinaryWriter(File.Open(Path.Combine(output, name + ".wav"), FileMode.CreateNew));
            f.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); f.Write(36 + data.Length * 2);
            f.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); f.Write(16); f.Write((short)1); f.Write((short)1);
            f.Write(rate); f.Write(rate * 2); f.Write((short)2); f.Write((short)16);
            f.Write(System.Text.Encoding.ASCII.GetBytes("data")); f.Write(data.Length * 2);
            foreach (float v in data) f.Write((short)Math.Clamp((int)MathF.Round(v * 32767), -32767, 32767));
        }
        bool balanceReview = OS.GetCmdlineUserArgs().Contains("--engine-balance-review");
        bool raspReview = OS.GetCmdlineUserArgs().Contains("--engine-rasp-review");
        bool aggressionReview = OS.GetCmdlineUserArgs().Contains("--engine-aggression-review");
        bool closedReview = OS.GetCmdlineUserArgs().Contains("--engine-closed-throttle-review");
        var before = Render(ghost, balanceReview || raspReview || aggressionReview || closedReview ? 1 : 0,
            engineGain: raspReview || aggressionReview || closedReview ? 1.5f : 1,
            rasp: raspReview ? 0 : 1, aggression: aggressionReview ? 0 : .5f, closedThrottle: closedReview ? 0 : 1);
        var after = Render(ghost, 1);
        Wav("ennen", before); Wav("jalkeen", after);
        Wav("ennen-jalkeen", before.Concat(new float[rate]).Concat(after).ToArray());
        Check(after.All(float.IsFinite) && after.Max(v => Math.Abs(v)) < .98f,
            "engine character: idle, free rev, loaded pull, lift and shift retain headroom");
        double Energy(float[] data, int a, int b) => Enumerable.Range(a * rate, (b - a) * rate).Sum(i => (double)data[i] * data[i]);
        Check(Energy(after, 5, 7) > Energy(after, 0, 2) * 1.5,
            "engine character: loaded pull has more presence than idle without raising master gain");
        Check(loaded != null && after.SequenceEqual(Render(loaded, 1)),
            "engine character: actual ghost record/save/load preserves inputs and rendered voice");
        Check(loaded != null && after.SequenceEqual(Render(loaded, 1)),
            "engine character: rewind with a fresh voice reproduces the saved audition");
        bool seekInputs = loaded != null;
        var expectedCar = new CarSim(parameters, ground); var soughtCar = new CarSim(parameters, ground);
        foreach (float time in new[] { 12f, 1f, 8.5f, 3f, 11.15f, 0f })
        {
            ghost.Apply(time, expectedCar, 0); loaded?.Apply(time, soughtCar, 0);
            seekInputs &= expectedCar.Rpm == soughtCar.Rpm && expectedCar.Throttle == soughtCar.Throttle
                && expectedCar.Clutch == soughtCar.Clutch && expectedCar.Gear == soughtCar.Gear
                && expectedCar.Speed == soughtCar.Speed;
        }
        Check(seekInputs, "engine character: forward/backward seeks restore all voice-driving inputs");
        // The new layers must obey engine silence; drivetrain/road sounds stay independent.
        Check(Render(ghost, 0, 0).SequenceEqual(Render(ghost, 1, 0)),
            "engine character: new engine layers vanish at EngineLevel zero");
        if (closedReview)
        {
            double EdgeRatio(float[] data, int start, int end)
            {
                double edge = 0, energy = 0;
                for (int i = start * rate + 1; i < end * rate; i++)
                { double d = data[i] - data[i - 1]; edge += d * d; energy += data[i] * data[i]; }
                return edge / Math.Max(energy, 1e-12);
            }
            Check(EdgeRatio(after, 0, 2) > EdgeRatio(before, 0, 2) * 1.08,
                "closed throttle: idle has more audible edge relative to its level, not just higher gain");
            Check(Math.Abs(Energy(after, 6, 7) / Energy(before, 6, 7) - 1) < .01,
                "closed throttle: full-load engine balance remains within one percent");
            Check(Render(ghost, 1, 0, closedThrottle: 0).SequenceEqual(Render(ghost, 1, 0, closedThrottle: 1)),
                "closed throttle: stopped engine stays silent with the added rasp");
        }
        File.WriteAllText(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(new {
            rate, seconds = 15, before_peak = before.Max(v => Math.Abs(v)), after_peak = after.Max(v => Math.Abs(v)),
            before_rms = Math.Sqrt(Energy(before, 0, 15) / before.Length),
            after_rms = Math.Sqrt(Energy(after, 0, 15) / after.Length),
            source = "Synthetic driving inputs recorded through production Ghost; not a real track lap",
            whine_level_both = .05f, before_engine_gain = raspReview || aggressionReview || closedReview ? 1.5f : 1f, after_engine_gain = 1.5f,
            before_character_mix = balanceReview || raspReview || aggressionReview || closedReview ? 1 : 0, after_character_mix = 1,
            before_rasp_mix = raspReview ? 0 : 1, after_rasp_mix = 1,
            before_aggression_mix = aggressionReview ? 0 : .5f, after_aggression_mix = .5f
        }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"Engine character review: {output}; peak {after.Max(v => Math.Abs(v)):F4}");
    }
}
