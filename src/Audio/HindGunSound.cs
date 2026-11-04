using System;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>Spatial, repeating rotary machine-gun report; one shared PCM loop for the fleet.</summary>
public partial class HindGunSound : AudioStreamPlayer3D
{
    static AudioStreamWav _loop;
    static int _players;
    bool _firing;
    public bool Firing { get => _firing; set { _firing = value; StreamPaused = !value; } }
    public override void _Ready()
    {
        _players++;
        Stream = _loop ??= MakeLoop();
        Bus = AudioMix.BusFor(AudioMix.Jet);
        UnitSize = 120; MaxDistance = 700; MaxDb = 0; VolumeDb = -2;
        AttenuationFilterDb = -2;
        Play(); StreamPaused = true;
    }
    public override void _ExitTree()
    {
        Stop(); Stream = null;
        if (--_players == 0)
        {
            _loop?.Dispose();
            _loop = null;
        }
    }
    static AudioStreamWav MakeLoop()
    {
        const int rate = 22050, count = rate;
        var bytes = new byte[count * 2];
        var rng = new Random(1981); float low = 0;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)rate, phase = t * (1f / CCCPRacing.Track.HindSorties.BurstInterval) % 1;
            float noise = (float)rng.NextDouble() * 2 - 1;
            low += (noise - low) * .12f;
            // One report per simulated round: fast metallic attack, low pressure and rotor-cannon rasp.
            float age = phase * CCCPRacing.Track.HindSorties.BurstInterval;
            float crack = noise * 1.15f * MathF.Exp(-age * 850f);
            float body = MathF.Sin(age * MathF.Tau * (125f - age * 900f)) * .70f * MathF.Exp(-age * 95f);
            float rasp = low * 2.1f * MathF.Exp(-age * 65f);
            float value = MathF.Tanh((crack + body + rasp) * 1.6f) * .83f;
            short sample = (short)(Math.Clamp(value, -1, 1) * 29000);
            bytes[i * 2] = (byte)(sample & 255); bytes[i * 2 + 1] = (byte)((sample >> 8) & 255);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate,
            Data = bytes, LoopMode = AudioStreamWav.LoopModeEnum.Forward, LoopBegin = 0, LoopEnd = count };
    }
}
