using System;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>A short spatial ground strike, shared PCM so a strafing run needs no per-round generator.</summary>
public partial class CannonStrikeSound : AudioStreamPlayer3D
{
    static AudioStreamWav _sample;
    static int _players;
    public override void _Ready()
    {
        _players++;
        Stream=_sample??=MakeSample();Bus=AudioMix.BusFor(AudioMix.Boom);
        UnitSize=18;MaxDistance=130;MaxDb=-5;VolumeDb=-5;
        Finished+=QueueFree;Play();
    }
    public override void _ExitTree()
    {
        Stop();Stream=null;
        if(--_players==0) {_sample?.Dispose();_sample=null;}
    }
    static AudioStreamWav MakeSample()
    {
        const int rate=22050,count=7717;
        var data=new byte[count*2];var rng=new Random(24072);float low=0;
        for(int i=0;i<count;i++)
        {
            float t=i/(float)rate,noise=(float)rng.NextDouble()*2-1;
            low+=(noise-low)*.18f;
            float hit=noise*MathF.Exp(-t*500)*.85f+low*MathF.Exp(-t*28)*1.3f
                +MathF.Sin(t*MathF.Tau*145)*MathF.Exp(-t*65)*.35f;
            short pcm=(short)(MathF.Tanh(hit)*24000);
            data[i*2]=(byte)pcm;data[i*2+1]=(byte)(pcm>>8);
        }
        return new AudioStreamWav {Format=AudioStreamWav.FormatEnum.Format16Bits,MixRate=rate,Data=data};
    }
}
