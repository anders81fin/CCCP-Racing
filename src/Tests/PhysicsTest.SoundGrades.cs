using Godot;
using CCCPRacing.Audio;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>SOUNDGRADES: the three fuels' voices at the same revs, offline to /tmp WAVs (a 4 s pull in 2nd, a lift, then 2 s of the booster).</summary>
    void SoundGradesRender()
    {
        string dir = OS.GetEnvironment("SOUNDGRADES");
        foreach (int grade in new[] { 0, 1, 2 })
        {
            var snd = new EngineSound { Grade = grade, Volume = 1f };
            const int Rate = 22050;
            var pcm = new System.Collections.Generic.List<short>();
            for (int i = 0; i < Rate * 9; i++)
            {
                float t = i / (float)Rate;
                // 0-1.5 s idle, 1.5-5.5 s full throttle rising 2500 -> 6800 rpm, 5.5-6.5 s lift (coasting down), 6.5-9 s steady 5500 rpm throttle 0.8 (booster on from 7 s)
                float rpm, thr;
                if (t < 1.5f) { rpm = 1200; thr = 0; }
                else if (t < 5.5f) { rpm = 2500 + (t - 1.5f) / 4f * 4300; thr = 1; }
                else if (t < 6.5f) { rpm = 6800 - (t - 5.5f) * 2000; thr = System.Math.Max(0f, 1f - (t - 5.5f) / 0.12f); }      // (the pedal comes up in 0.12 s: the lift-off the game's keys and pad make)
                else { rpm = 5500; thr = 0.8f; }
                snd.Rpm = rpm; snd.Throttle = thr; snd.Driven = t > 1.5f ? 1 : 0; snd.Speed = t > 1.5f ? 20 : 0;
                snd.Boosted = t > 7f;
                float x = snd.NextSample();
                pcm.Add((short)(System.Math.Clamp(x, -1f, 1f) * 32000));
            }
            var f = FileAccess.Open($"{dir}/grade{grade}.wav", FileAccess.ModeFlags.Write);
            f.Store32(0x46464952); f.Store32((uint)(36 + pcm.Count * 2)); f.Store32(0x45564157); f.Store32(0x20746d66); f.Store32(16); f.Store16(1); f.Store16(1);
            f.Store32(Rate); f.Store32(Rate * 2); f.Store16(2); f.Store16(16); f.Store32(0x61746164); f.Store32((uint)(pcm.Count * 2));
            foreach (short s in pcm) f.Store16((ushort)s);
            f.Close();
        }
    }
}
