using System;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>Low rotor blade pulses with turbine whine; positional attenuation belongs to Godot.</summary>
public partial class RotorSound : AudioStreamPlayer3D
{
    public float Level = .2f;
    AudioStreamGeneratorPlayback _playback;
    float _phase, _whine, _whine2, _noise, _turbineNoise, _level;
    Doppler _doppler;
    readonly Random _random = new(1924);
    public override void _Ready()
    {
        Stream = new AudioStreamGenerator { MixRate=22050, BufferLength=.2f };
        UnitSize=30; MaxDistance=700; MaxDb=0; VolumeDb=-6; AttenuationFilterDb=-12;
        Bus=AudioMix.BusFor(AudioMix.Jet);
        Play(); _playback=GetStreamPlayback() as AudioStreamGeneratorPlayback;
    }
    /// <summary>Back to a freshly made sound's state (a replay that keeps its aircraft across a seek): silent, smoothing and phases at zero, queued audio dropped.</summary>
    public void ResetState()
    {
        Level = 0; _level = _phase = _whine = _whine2 = _noise = _turbineNoise = 0;
        _doppler = default;
        // (the generator refuses ClearBuffer while its playback is active: stop the player, start a new playback with an empty buffer, keep the pause)
        if (!IsInsideTree()) { _playback = null; return; }      // (the next _Ready starts a clean playback)
        bool paused = StreamPaused;
        Stop(); Play(); _playback = GetStreamPlayback() as AudioStreamGeneratorPlayback;
        StreamPaused = paused;
    }
    public override void _Process(double delta)
    {
        if (_playback == null || StreamPaused) return;
        var viewport=GetViewport();
        Vector3 ear=viewport.GetAudioListener3D()?.GlobalPosition??viewport.GetCamera3D()?.GlobalPosition??GlobalPosition;
        float pitch=_doppler.Update(GlobalPosition,ear,(float)delta);
        int frames=_playback.GetFramesAvailable();
        for(int i=0;i<frames;i++)
        {
            _level+= (Level-_level)*.0004f;
            _phase=Mathf.PosMod(_phase+(3f+17f*_level/.9f)*pitch/22050f,1);
            _whine=Mathf.PosMod(_whine+(900f+1800f*_level/.9f)*pitch/22050f,1);
            _whine2=Mathf.PosMod(_whine2+(940f+1840f*_level/.9f)*pitch/22050f,1);
            float noise=(float)_random.NextDouble()*2-1;
            _noise+=(noise-_noise)*.08f;
            _turbineNoise+=(noise-_turbineNoise)*.32f;
            float blade=MathF.Pow(Math.Max(0,MathF.Sin(_phase*MathF.Tau)),8);
            float rotor=_noise*(.8f+blade*5)+MathF.Sin(_phase*MathF.Tau)*.22f
                +MathF.Sin(_phase*MathF.Tau*2)*.18f+MathF.Sin(_phase*MathF.Tau*3)*.13f;
            // Twin turbines keep their bright, continuous whine and gas hiss beneath the blade slap.
            float turbines=MathF.Sin(_whine*MathF.Tau)*.055f+MathF.Sin(_whine2*MathF.Tau)*.035f
                +(_turbineNoise-_noise)*.65f+(noise-_turbineNoise)*.08f;
            float signal=(rotor+turbines)*_level;
            float sample=MathF.Tanh(signal);
            _playback.PushFrame(new Vector2(sample,sample));
        }
    }
    public override void _ExitTree() { _playback?.Dispose(); _playback=null; }
}
