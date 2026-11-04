using System;
using System.IO;
using System.Threading.Tasks;
using Godot;

namespace CCCPRacing.Net;

/// <summary>Observes one local LAN round. Host records authority; joined players record received complete packets.</summary>
public sealed class LanReplayCapture : IDisposable
{
    readonly LanSession _net;
    readonly Func<float> _clock;
    readonly LanReplayFile.Writer _writer;
    readonly long _localId;
    readonly Func<Func<byte[]>> _freezeOwn;
    public bool Recording=>!_stopped && Error==null;
    public bool LocalFinished {get;private set;}
    bool _stopped;
    public Task<string> Completion=>_writer.Completion;
    public string Error=>_writer.Error;
    public LanReplayCapture(LanSession net,Func<float> clock,string path,LanReplayFile.Header header,Func<Func<byte[]>> freezeOwn=null)
    {
        _net=net;_clock=clock;_localId=header.LocalId;_freezeOwn=freezeOwn;_writer=new(path,header);
        net.SnapIn+=Car;net.WorldIn+=World;net.MeshIn+=Mesh;net.EffectIn+=Effect;net.BrokeIn+=Break;
        net.ReplaySnapOut+=Car;net.ReplayWorldOut+=WorldOut;net.ReplayEffectOut+=Effect;net.ReplayBreakOut+=Break;
    }
    static byte[] Encode(Action<BinaryWriter> write) {using var m=new MemoryStream();using(var w=new BinaryWriter(m,System.Text.Encoding.UTF8,true)) write(w);return m.ToArray();}
    void Add(LanReplayFile.Kind k,byte[] b)=>_writer.Add(Math.Max(0,_clock()),k,b);
    void Car(long id,Snapshot s) {Add(LanReplayFile.Kind.Car,Encode(w=>{w.Write(id);s.Write(w);}));if(id==_localId && s.Finished) LocalFinished=true;}
    public void MarkGo()=>Add(LanReplayFile.Kind.Go,Array.Empty<byte>());
    public void WorldOut(byte[] b,bool mesh) {if(mesh) Mesh(b);else World(b);}
    void World(byte[] b)=>Add(LanReplayFile.Kind.World,b);
    void Mesh(byte[] b)=>Add(LanReplayFile.Kind.Mesh,b);
    static void V(BinaryWriter w,Vector3 p) {w.Write(p.X);w.Write(p.Y);w.Write(p.Z);}
    void Effect(Vector3 p,int kind)=>Add(LanReplayFile.Kind.Effect,Encode(w=>{V(w,p);w.Write(kind);}));
    void Break(int id,float hit,Vector3 vel,Vector3 pos)=>Add(LanReplayFile.Kind.Break,Encode(w=>{w.Write(id);w.Write(hit);V(w,vel);V(w,pos);}));
    public void Abort(string reason) {_writer.Abort(reason);Dispose();}
    public void Dispose()
    {
        if(_stopped) return;_stopped=true;
        _net.SnapIn-=Car;_net.WorldIn-=World;_net.MeshIn-=Mesh;_net.EffectIn-=Effect;_net.BrokeIn-=Break;
        _net.ReplaySnapOut-=Car;_net.ReplayWorldOut-=WorldOut;_net.ReplayEffectOut-=Effect;_net.ReplayBreakOut-=Break;
        _writer.Finish(Error==null?_freezeOwn?.Invoke():null);
    }
}
