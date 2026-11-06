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

/// <summary>Real audio owners: scene replacement releases native playbacks; application exit drains the mixer.</summary>
public partial class AudioGeneratorLifecycleTest : Node
{
    int _failed;
    static readonly System.Collections.Generic.Dictionary<ulong, string> Owners = new();
    void Check(bool ok, string what) { GD.Print($"AUDIO LIFECYCLE {(ok ? "PASS" : "FAIL")}: {what}"); if (!ok) _failed++; }
    Node3D MakeScene()
    {
        var world = new Node3D(); AddChild(world);
        GetViewport().AudioListenerEnable3D = true;
        var camera = new Camera3D(); world.AddChild(camera); camera.MakeCurrent();
        var listener = new AudioListener3D(); world.AddChild(listener); listener.MakeCurrent();
        var target = new Node3D(); world.AddChild(target);
        var sounds = new Node[] {
            new EngineSound(), new HornSound(), new DieselSound(), new RotorSound(), new JetSound(),
            new SirenSound(), new FireSound(), new MissileSound(), new MissileWarning(), new GurgleSound(),
            new GeigerSound(), new DashChime(), new PaceBlip(), new EarRingSound(), new BeaconBeep(),
            new RadioMusic { TrackPath = "" }, new ClangSound(), new ExplosionSound(), Weather.Create(target)
        };
        foreach (var sound in sounds) { world.AddChild(sound); if (sound is RadioMusic radio) radio.Play(); }
        return world;
    }
    static ulong[] PlaybackIds(Node root)
    {
        var ids = new System.Collections.Generic.List<ulong>();
        void Visit(Node node)
        {
            if (node is AudioStreamPlayer p && p.HasStreamPlayback())
            { ulong id = p.GetStreamPlayback().GetInstanceId(); ids.Add(id); Owners[id] = node.GetParent().GetType().Name + "/" + node.GetType().Name; }
            if (node is AudioStreamPlayer3D p3 && p3.HasStreamPlayback())
            { ulong id = p3.GetStreamPlayback().GetInstanceId(); ids.Add(id); Owners[id] = node.GetType().Name; }
            foreach (var child in node.GetChildren()) Visit(child);
        }
        Visit(root); return ids.ToArray();
    }
    static System.Collections.Generic.Dictionary<ulong, int> FreeFrames(Node root)
    {
        var frames = new System.Collections.Generic.Dictionary<ulong, int>();
        void Visit(Node node)
        {
            AudioStreamPlayback playback = node is AudioStreamPlayer p && p.HasStreamPlayback() ? p.GetStreamPlayback()
                : node is AudioStreamPlayer3D p3 && p3.HasStreamPlayback() ? p3.GetStreamPlayback() : null;
            if (playback is AudioStreamGeneratorPlayback generator) frames[generator.GetInstanceId()] = generator.GetFramesAvailable();
            foreach (var child in node.GetChildren()) Visit(child);
        }
        Visit(root); return frames;
    }
    public override async void _Ready()
    {
        for (int cycle = 1; cycle <= 3; cycle++)
        {
            var world = MakeScene();
            var capacity = FreeFrames(world);
            await ToSignal(GetTree().CreateTimer(.12), SceneTreeTimer.SignalName.Timeout);
            var ids = PlaybackIds(world);
            Check(ids.Length == 19, $"scene {cycle}: all 19 production generator owners have live playbacks");
            foreach (var engine in world.GetChildren().OfType<EngineSound>()) engine.ResetPlaybackBuffer();
            foreach (var gurgle in world.GetChildren().OfType<GurgleSound>()) gurgle.ClearReplayAudio();
            foreach (var rotor in world.GetChildren().OfType<RotorSound>()) rotor.ResetState();
            var reset = PlaybackIds(world);
            var oldBuffers = ids.Except(reset).ToArray();
            var cleared = FreeFrames(world);
            var targets = ids.Where(id => Owners[id].StartsWith("EngineSound/") || Owners[id].StartsWith("GurgleSound/") || Owners[id] == "RotorSound").ToArray();
            Check(reset.SequenceEqual(ids) && targets.Length == 3 && targets.All(id => cleared[id] == capacity[id]), $"scene {cycle}: seek clears the engine, pouring and rotor PCM queues without allocating replacement native voices");
            world.Free();
            ids = ids.Concat(reset).Distinct().ToArray();
            ulong deadline = Time.GetTicksMsec() + 1000;
            ulong firstFrame = Engine.GetProcessFrames();
            while (ids.Any(GodotObject.IsInstanceIdValid) && Time.GetTicksMsec() < deadline)
                // Let the complete main loop run, including AudioServer's deferred list cleanup.
                await ToSignal(GetTree().CreateTimer(.02), SceneTreeTimer.SignalName.Timeout);
            Check(!ids.Any(GodotObject.IsInstanceIdValid), $"scene {cycle}: every native playback retired after scene removal ({Engine.GetProcessFrames()-firstFrame} frames); retained [{string.Join(", ", ids.Where(GodotObject.IsInstanceIdValid).Select(id => Owners[id] + (oldBuffers.Contains(id) ? " (old buffer)" : " (current buffer)")))}]");
        }
        var final = MakeScene();
        await ToSignal(GetTree().CreateTimer(.12), SceneTreeTimer.SignalName.Timeout);
        Check(PlaybackIds(final).Length == 19, "application quit starts with all 19 generators still active");
        int exit = 0;
        foreach (var arg in OS.GetCmdlineUserArgs()) if (arg.StartsWith("--audio-lifecycle-exit=")) exit = int.Parse(arg.Split('=')[1]);
        // Repeated requests share one shutdown and preserve an error/cancellation status.
        AudioMix.Quit(this);
        AudioMix.Quit(this, _failed > 0 ? 1 : exit);
    }
}
