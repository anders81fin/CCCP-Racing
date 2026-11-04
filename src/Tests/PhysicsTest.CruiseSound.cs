using System;
using System.IO;
using CCCPRacing.Audio;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>
    /// CRUISEWAV=dir: a steady drive at 80 km/h in fourth, rendered offline on each ground (EngineSound.NextSample): 2 s on the throttle,
    /// 2 s with the foot off (engine braking), then 1 s at a standstill. dir/cruise_&lt;ground&gt;.wav with everything, and
    /// dir/cruise_&lt;ground&gt;_engine.wav with the engine alone (no speed); the RMS of each stretch is printed.
    /// </summary>
    void CruiseWav(string dir)
    {
        Directory.CreateDirectory(dir);
        const int rate = 22050;
        foreach (var g in new[] { SurfaceType.Concrete, SurfaceType.Gravel, SurfaceType.Snow, SurfaceType.Mud })
            foreach (bool full in new[] { true, false })
            {
                var snd = new EngineSound { Ground = g, Gear = 4 };
                var pcm = new short[rate * 5];
                double[] sq = new double[3]; int[] n = new int[3];
                for (int i = 0; i < pcm.Length; i++)
                {
                    float t = i / (float)rate;
                    bool stopped = t >= 4f, lift = t >= 2f && !stopped;
                    snd.Speed = full && !stopped ? 22f : 0f;
                    snd.Rpm = stopped ? 900f : lift ? 4200f - 500f * (t - 2f) : 4200f;
                    snd.Throttle = stopped ? 0f : lift ? 0f : 0.7f;
                    snd.Driven = stopped ? 0f : 1f;
                    float s = snd.NextSample();
                    pcm[i] = (short)(s * 32000f);
                    int seg = stopped ? 2 : lift ? 1 : 0;
                    sq[seg] += s * s; n[seg]++;
                }
                snd.Free();
                string path = Path.Combine(dir, $"cruise_{g.ToString().ToLowerInvariant()}{(full ? "" : "_engine")}.wav");
                using var w = new BinaryWriter(File.Create(path));
                w.Write("RIFF"u8.ToArray()); w.Write(36 + pcm.Length * 2); w.Write("WAVEfmt "u8.ToArray());
                w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write("data"u8.ToArray()); w.Write(pcm.Length * 2);
                foreach (short x in pcm) w.Write(x);
                GD.Print($"cruise sound: {Path.GetFileName(path)} rms throttle {Math.Sqrt(sq[0] / n[0]):F3} lift {Math.Sqrt(sq[1] / n[1]):F3} stopped {Math.Sqrt(sq[2] / n[2]):F3}");
            }
    }
}
