using System;
using System.IO;
using CCCPRacing.Audio;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>
    /// SKIDWAV=dir: a hard stop from 70 km/h rendered offline on each ground (EngineSound.NextSample),
    /// the wheels locked for its first 1.5 s: dir/brake_&lt;ground&gt;.wav with the engine, and
    /// dir/brake_&lt;ground&gt;_tyres.wav with the tyres and the brakes alone.
    /// </summary>
    void SkidWav(string dir)
    {
        Directory.CreateDirectory(dir);
        var grounds = new[]
        {
            SurfaceType.Concrete, SurfaceType.Gravel, SurfaceType.Dirt, SurfaceType.Mud,
            SurfaceType.Grass, SurfaceType.Snow, SurfaceType.Ice, SurfaceType.Puddle,
        };
        const int rate = 22050;
        foreach (var g in grounds)
            foreach (bool engine in new[] { true, false })
            {
                var snd = new EngineSound { Ground = g, EngineLevel = engine ? 1f : 0f };
                var pcm = new short[(int)(rate * 3.2f)];
                for (int i = 0; i < pcm.Length; i++)
                {
                    float t = i / (float)rate, u = Math.Clamp((t - 0.3f) / 2.5f, 0f, 1f);
                    bool braking = t >= 0.3f && u < 1f;
                    float v = 19.4f * (1f - u);                                  // m/s, down to a stop
                    snd.Rpm = 900f + 2100f * (1f - u);
                    snd.Throttle = t < 0.3f ? 0.3f : 0f;
                    snd.BrakeSqueal = braking ? Math.Clamp((v - 1f) / 9f, 0f, 1f) : 0f;
                    snd.Slide = braking && t < 1.8f ? Math.Clamp(v / 8f, 0.3f, 0.9f) : 0f;
                    pcm[i] = (short)(snd.NextSample() * 32000f);
                }
                snd.Free();
                string path = Path.Combine(dir, $"brake_{g.ToString().ToLowerInvariant()}{(engine ? "" : "_tyres")}.wav");
                using var w = new BinaryWriter(File.Create(path));
                w.Write("RIFF"u8.ToArray()); w.Write(36 + pcm.Length * 2); w.Write("WAVEfmt "u8.ToArray());
                w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write("data"u8.ToArray()); w.Write(pcm.Length * 2);
                foreach (short x in pcm) w.Write(x);
                GD.Print($"skid sound: {path}");
            }
    }
}
