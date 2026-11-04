using System;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>Three short positional warning tones before a live-fire run.</summary>
public partial class ExerciseWarningSound : AudioStreamPlayer3D
{
    static AudioStreamWav _tone;
    float _age;
    public override void _Ready()
    {
        if(_tone==null)
        {
            const int rate=22050;var data=new byte[rate*2];
            for(int i=0;i<rate;i++)
            {
                float t=i/(float)rate,phase=t%.33f;
                float envelope=phase<.19f?Math.Min(1,phase/.015f)*Math.Min(1,(.19f-phase)/.02f):0;
                short sample=(short)(MathF.Sin(t*MathF.Tau*880)*envelope*17000);
                data[i*2]=(byte)sample;data[i*2+1]=(byte)(sample>>8);
            }
            _tone=new AudioStreamWav {Format=AudioStreamWav.FormatEnum.Format16Bits,MixRate=rate,Data=data};
        }
        Stream=_tone;UnitSize=18;MaxDistance=260;VolumeDb=0;Bus=AudioMix.BusFor(AudioMix.Warning);Play();
    }
    public override void _Process(double delta) {if((_age+=(float)delta)>1.5f) QueueFree();}
}
