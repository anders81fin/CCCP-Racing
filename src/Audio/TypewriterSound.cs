using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>Mechanical menu sounds, driven by the letters actually drawn on screen.</summary>
public partial class TypewriterSound : Node
{
    AudioStreamPlayer _key;
    Dictionary<string, int> _previous = new(), _visible = new();
    int _strike;

    public override void _Ready()
    {
        // UI goes straight to Master: title replay mutes the world's Sfx bus.
        AddChild(_key = new AudioStreamPlayer { Stream = MakeSound(), VolumeDb = -10, MaxPolyphony = 4 });
    }

    public void BeginFrame() => _visible.Clear();
    public void Letters(string text, int count) => _visible[text] = count;

    public void EndFrame(string context)
    {
        if (context == null)
        {
            _previous.Clear();
            _key.Stop();
            return;
        }
        bool strike = false;
        foreach (var (text, count) in _visible)
        {
            int before = _previous.TryGetValue(text, out int old) && old <= count ? old : 0;
            // A fully revealed background heading is not a new typing animation.
            if (before == 0 && count > 3) continue;
            for (int i = before; i < count; i++)
                if (!char.IsWhiteSpace(text[i])) { strike = true; break; }
        }
        if (strike)
        {
            _key.PitchScale = 0.94f + (_strike++ % 5) * 0.03f;
            _key.Play();
        }
        (_previous, _visible) = (_visible, _previous);
    }

    static AudioStreamWav MakeSound()
    {
        int rate = 44100;
        float duration = 0.075f;
        var data = new byte[(int)(rate * duration) * 2];
        var random = new Random(1936);
        for (int i = 0; i < data.Length / 2; i++)
        {
            float t = (float)i / rate;
            float noise = (float)random.NextDouble() * 2 - 1;
            // The reference key has a hard broadband attack and a short, bright steel resonance.
            float sound = noise * MathF.Exp(-t * 420) * 0.78f
                + MathF.Sin(MathF.Tau * 1760 * t) * MathF.Exp(-t * 105) * 0.10f
                + MathF.Sin(MathF.Tau * 3480 * t) * MathF.Exp(-t * 125) * 0.16f
                + MathF.Sin(MathF.Tau * 5750 * t) * MathF.Exp(-t * 155) * 0.22f
                + MathF.Sin(MathF.Tau * 5940 * t) * MathF.Exp(-t * 175) * 0.14f
                + MathF.Sin(MathF.Tau * 9450 * t) * MathF.Exp(-t * 230) * 0.07f;
            if (t >= 0.011f)
                sound += noise * MathF.Exp(-(t - 0.011f) * 155) * 0.25f
                    + MathF.Sin(MathF.Tau * 5570 * (t - 0.011f)) * MathF.Exp(-(t - 0.011f) * 185) * 0.08f;
            // A tiny attack avoids a hard discontinuity at the sample boundary.
            short sample = (short)(Math.Clamp(sound * Math.Min(t * 4000, 1), -1, 1) * short.MaxValue);
            data[2 * i] = (byte)(sample & 255);
            data[2 * i + 1] = (byte)((sample >> 8) & 255);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Data = data };
    }
}
