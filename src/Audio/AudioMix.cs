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

    public static void MuteSfx(bool mute) => AudioServer.SetBusMute(EnsureBus(Sfx, "Master"), mute);

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
