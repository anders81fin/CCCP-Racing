using Godot;

namespace CCCPRacing.Audio;

/// <summary>Test: records every mix group bus and saves the WAVs when the scene exits.</summary>
internal sealed partial class GroupRecorder : Node
{
    readonly string _prefix;
    readonly System.Collections.Generic.List<(string, AudioEffectRecord)> _recs = new();

    public GroupRecorder() { }
    public GroupRecorder(string prefix) => _prefix = prefix;

    public override void _Ready()
    {
        foreach (string g in new[] { AudioMix.Engine, AudioMix.Diesel, AudioMix.Boom, AudioMix.Jet, AudioMix.Warning })
        {
            var r = new AudioEffectRecord { Format = AudioStreamWav.FormatEnum.Format16Bits };
            AudioServer.AddBusEffect(AudioServer.GetBusIndex(AudioMix.BusFor(g)), r);
            r.SetRecordingActive(true);
            _recs.Add((g, r));
        }
    }

    public override void _ExitTree()
    {
        foreach (var (g, r) in _recs)
        {
            r.SetRecordingActive(false);
            r.GetRecording()?.SaveToWav($"{_prefix}_{g}.wav");
        }
        _recs.Clear();
    }
}
