using System;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>
/// Master-bus safety and mix groups. <see cref="Setup"/> puts a look-ahead limiter on Master so a
/// shell landing next to the car or a jet crash on top of the engine can never clip. With
/// CCCP_MIX_DEBUG set, each sound group plays on its own bus (feeding Master) so it can be
/// recorded alone with --record-bus=NAME, or all at once with CCCP_MIX_REC=PREFIX (writes
/// PREFIX_Engine.wav ... when the scene exits).
/// </summary>
public static class AudioMix
{
    static AudioShutdown _shutdown;
    public static bool Closing => GodotObject.IsInstanceValid(_shutdown);

    internal readonly struct DriverLock : IDisposable
    {
        public void Dispose() => AudioServer.Unlock();
    }
    internal static DriverLock LockDriver() { AudioServer.Lock(); return new DriverLock(); }

    /// <summary>Retire audio while the mixer still runs, then close with the requested exit status.</summary>
    public static void Quit(Node owner, int exitCode = 0)
    {
        if (GodotObject.IsInstanceValid(_shutdown))
        {
            if (exitCode != 0) _shutdown.ExitCode = exitCode;
            return;
        }
        _shutdown = new AudioShutdown { ExitCode = exitCode, ProcessMode = Node.ProcessModeEnum.Always };
        owner.GetTree().Root.CallDeferred(Node.MethodName.AddChild, _shutdown);
    }

    /// <summary>Mix groups (bus names in debug mode).</summary>
    public const string Engine = "Engine", Diesel = "Diesel", Boom = "Boom", Jet = "Jet", Warning = "Warning";
    /// <summary>Limiter ceiling on Master (dBFS).</summary>
    public const float CeilingDb = -1f;

    internal static readonly bool Debug = OS.GetEnvironment("CCCP_MIX_DEBUG") != "";

    /// <summary>Adds the Master limiter once (idempotent; call before any recorder is added).</summary>
    public static void Setup(Node host)
    {
        string rec = OS.GetEnvironment("CCCP_MIX_REC");
        if (Debug && rec != "" && host != null) host.AddChild(new GroupRecorder(rec));
        int master = AudioServer.GetBusIndex("Master");
        for (int i = 0; i < AudioServer.GetBusEffectCount(master); i++)
            if (AudioServer.GetBusEffect(master, i) is AudioEffectHardLimiter) return;
        // Soft-ish: 1 dB below full scale, quick but not pumping release.
        AudioServer.AddBusEffect(master, new AudioEffectHardLimiter { CeilingDb = CeilingDb, PreGainDb = 0f, Release = 0.2f }, 0);
    }

    /// <summary>All sound effects (not the music or the radios) go through this bus into Master,
    /// so the title screen can silence the replay's engine, jets and tanks under its music.</summary>
    public const string Sfx = "Sfx";

    /// <summary>Bus for a mix group: the group's own bus, feeding Sfx (so a group can be trimmed or ducked and recorded alone).</summary>
    public static string BusFor(string group)
    {
        EnsureBus(Sfx, "Master");
        EnsureBus(group, Sfx);
        return group;
    }

    // A group's level is its trim (the mixer, SoundBalance) plus its duck (a crash).
    static readonly System.Collections.Generic.Dictionary<string, float> Trims = new();

    /// <summary>The group's standing level in dB (the SOUND BALANCE mixer).</summary>
    public static void SetTrim(string group, float db)
    {
        Trims[group] = db;
        Apply(group);
    }

    static void Apply(string group)
    {
        int i = AudioServer.GetBusIndex(group);
        if (i < 0) return;
        float duck = Ducked.TryGetValue(group, out var d) ? d.db : 0f;
        AudioServer.SetBusVolumeDb(i, Math.Max(-80f, (Trims.TryGetValue(group, out var t) ? t : 0f) + duck));
    }

    // Ducking: a group is pushed down by some dB and eases back over its time (Tick).
    static readonly System.Collections.Generic.Dictionary<string, (float db, float perSecond)> Ducked = new();

    /// <summary>Pushes a group down by this many dB, easing back over the given seconds: a hard hit takes the machinery and the weather under it.</summary>
    public static void Duck(string group, float db, float seconds)
    {
        int i = AudioServer.GetBusIndex(group);
        if (i < 0) return;
        float now = Ducked.TryGetValue(group, out var d) ? d.db : 0f;
        float target = Math.Min(now, -Math.Abs(db));
        Ducked[group] = (target, Math.Abs(target) / Math.Max(0.05f, seconds));
        Apply(group);
    }

    /// <summary>Per frame: the ducked groups ease back to 0 dB.</summary>
    public static void Tick(float dt)
    {
        if (Ducked.Count == 0) return;
        foreach (var group in new System.Collections.Generic.List<string>(Ducked.Keys))
        {
            var (db, perSecond) = Ducked[group];
            db = Math.Min(0f, db + perSecond * dt);
            if (db >= 0f) Ducked.Remove(group); else Ducked[group] = (db, perSecond);
            Apply(group);
        }
    }

    /// <summary>Mutes or unmutes every sound effect at once (the title screen plays music only).</summary>
    public static bool SfxMuted => AudioServer.IsBusMute(EnsureBus(Sfx, "Master"));

    public static void MuteSfx(bool mute)
    {
        int bus = EnsureBus(Sfx, "Master");
        AudioServer.SetBusMute(bus, mute);
        AudioServer.SetBusVolumeDb(bus, 0f);      // (a level given by Backdrop never outlives the title)
    }

    /// <summary>The title's background lap is heard at this level (dB below normal): the sound effects open, under the music.</summary>
    public static void Backdrop(float db)
    {
        int bus = EnsureBus(Sfx, "Master");
        AudioServer.SetBusMute(bus, false);
        AudioServer.SetBusVolumeDb(bus, db);
    }

    static int EnsureBus(string name, string send)
    {
        int i = AudioServer.GetBusIndex(name);
        if (i >= 0) return i;
        AudioServer.AddBus();
        i = AudioServer.BusCount - 1;
        AudioServer.SetBusName(i, name);
        AudioServer.SetBusSend(i, send);
        if (Trims.TryGetValue(name, out var trim)) AudioServer.SetBusVolumeDb(i, trim);
        return i;
    }
}

/// <summary>AudioServer retires stopped voices on its mix thread, before native application teardown.</summary>
internal sealed partial class AudioShutdown : Node
{
    internal int ExitCode;
    readonly System.Collections.Generic.List<ulong> _playbacks = new();
    double _elapsed, _lastMix;
    int _mixes;
    bool _started;

    public override void _Ready()
    {
        var tree = GetTree();
        // Freeze producers, including always-processing viewers/exporters, before disposing their cached playback wrappers.
        foreach (var child in tree.Root.GetChildren()) if (child != this) child.ProcessMode = ProcessModeEnum.Disabled;
        using (AudioMix.LockDriver()) StopAudio(tree.Root);
        _lastMix = Time.GetTicksUsec() / 1e6 - AudioServer.GetTimeSinceLastMix();
        _started = true;
        if (_playbacks.Count == 0) tree.Quit(ExitCode);
    }

    void StopAudio(Node node)
    {
        AudioStreamPlayback playback = null;
        AudioStreamGenerator generator = null;
        if (node is AudioStreamPlayer player)
        {
            generator = player.Stream as AudioStreamGenerator;
            if (player.HasStreamPlayback()) playback = player.GetStreamPlayback();
            player.StreamPaused = false; player.Stop(); player.Stream = null;
        }
        else if (node is AudioStreamPlayer3D spatial)
        {
            generator = spatial.Stream as AudioStreamGenerator;
            if (spatial.HasStreamPlayback()) playback = spatial.GetStreamPlayback();
            spatial.StreamPaused = false; spatial.Stop(); spatial.Stream = null;
        }
        if (playback != null) { _playbacks.Add(playback.GetInstanceId()); playback.Stop(); playback.Dispose(); }
        generator?.Dispose();
        foreach (var child in node.GetChildren()) StopAudio(child);
    }

    public override void _Process(double delta)
    {
        if (!_started) return;
        _elapsed += delta;
        // A deferred child may have entered the tree after the initial sweep.
        int before = _playbacks.Count;
        using (AudioMix.LockDriver()) StopAudio(GetTree().Root);
        if (_playbacks.Count != before) _mixes = 0;
        double mixedAt = Time.GetTicksUsec() / 1e6 - AudioServer.GetTimeSinceLastMix();
        if (mixedAt - _lastMix > .001) { _lastMix = mixedAt; _mixes++; }
        if (_mixes < 2 || _elapsed < .05)
        {
            if (_elapsed < 1) return;
        }
        int alive = _playbacks.FindAll(GodotObject.IsInstanceIdValid).Count;
        if (alive > 0 && _elapsed < 1) return;
        if (alive > 0) GD.PushWarning($"Audio shutdown: {alive} playback references did not retire before timeout");
        if (OS.HasEnvironment("AUDIO_SHUTDOWN_DEBUG")) GD.Print($"AUDIO SHUTDOWN: {_playbacks.Count} playbacks, {alive} retained, {_mixes} mixes, exit {ExitCode}");
        GetTree().Quit(ExitCode);
    }
}
