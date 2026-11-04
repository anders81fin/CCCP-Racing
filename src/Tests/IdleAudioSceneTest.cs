using System;
using System.IO;
using System.Linq;
using System.Reflection;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

/// <summary>Actual game start -> clutch waiting -> engine bus audio, including the live mixer/player.</summary>
public partial class IdleAudioSceneTest : Node
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    float _elapsed, _recorded; int _samples; double _energy; float _peak;
    AudioEffectCapture _capture; int _bus = -1;
    static object Read(object obj, string name) => obj.GetType().GetField(name, Private)?.GetValue(obj);
    static RaceScene Find(Node node)
    {
        if (node is RaceScene race) return race;
        foreach (var c in node.GetChildren()) { var found = Find(c); if (found != null) return found; }
        return null;
    }
    public override void _Ready() => GetTree().Root.CallDeferred(Node.MethodName.AddChild,
        GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate());

    public override void _Process(double delta)
    {
        _elapsed += (float)delta;
        if (_elapsed > 25) { GD.PushError("IDLE SCENE FAIL: game did not provide idle audio within 25 s"); GetTree().Quit(1); return; }
        var race = Find(GetTree().Root);
        if (race == null || Read(race, "_car") is not CarSim car || Read(race, "_snd") is not EngineSound sound) return;
        if (Read(race, "_state")?.ToString() != "Countdown" || (float)(Read(race, "_startRevLeft") ?? 1f) > 0) return;
        if (Math.Abs(sound.Rpm - 1200) > 1 || sound.Throttle > .01f) return;
        if (_capture == null)
        {
            _bus = AudioServer.GetBusIndex(AudioMix.Engine);
            _capture = new AudioEffectCapture { BufferLength = .5f };
            AudioServer.AddBusEffect(_bus, _capture);
            var player = (AudioStreamPlayer)Read(sound, "_player");
            GD.Print($"IDLE SCENE: car rpm={car.Rpm:F0}, N={car.Neutral}; sound rpm={sound.Rpm:F0}, level={sound.EngineLevel}, volume={sound.Volume}, gain={SoundBalance.EngineGain}, player playing={player.Playing}, paused={player.StreamPaused}, process={sound.CanProcess()}");
            foreach (string name in new[] { AudioMix.Engine, AudioMix.Sfx, "Master" })
            {
                int i = AudioServer.GetBusIndex(name);
                GD.Print($"IDLE BUS {name}: mute={AudioServer.IsBusMute(i)}, volume={AudioServer.GetBusVolumeDb(i)} dB");
            }
        }
        _recorded += (float)delta;
        int available = _capture.GetFramesAvailable();
        if (available > 0)
            foreach (var sample in _capture.GetBuffer(available))
            { _peak = Math.Max(_peak, Math.Abs(sample.X)); _energy += sample.X * sample.X; _samples++; }
        if (_recorded < 3) return;
        var hud = (CCCPRacing.UI.Hud)Read(race, "_hud");
        bool ok = hud.RpmFrac > 0 && MathF.Round(hud.RpmFrac * 14) >= 1 && _samples > 1000 && _energy / Math.Max(1, _samples) > .0001 && _peak < 2f;
        GD.Print($"IDLE SCENE {(ok ? "PASS" : "FAIL")}: captured engine bus before Master limiter {_samples} samples, peak {_peak:F5}, RMS {Math.Sqrt(_energy / Math.Max(1, _samples)):F5}; driver={AudioServer.GetDriverName()}");
        AudioServer.RemoveBusEffect(_bus, AudioServer.GetBusEffectCount(_bus) - 1);
        GetTree().Quit(ok ? 0 : 1);
    }
}
