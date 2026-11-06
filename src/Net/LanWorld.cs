using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.IO.Compression;
using CCCPRacing.Track;
using CCCPRacing.Audio;
using Godot;
namespace CCCPRacing.Net;

/// <summary>Host-owned procedural actor geometry and collision circles. Meshes travel once;
/// transforms travel independently of car/input packets. Clients never tick the original AI.</summary>
public sealed class LanWorld
{
    readonly Node3D _root;
    readonly Dictionary<ulong,int> _nodeIds=new(), _meshIds=new();
    readonly Dictionary<int,Mesh> _meshes=new();
    readonly Dictionary<int,GeometryInstance3D.ShadowCastingSetting> _shadows=new();
    readonly Dictionary<int,MeshInstance3D> _replicas=new();
    readonly Dictionary<int,Transform3D> _targets=new();
    readonly Dictionary<int,AudioStreamPlayer3D> _sounds=new();
    readonly Dictionary<int,Light3D> _lights=new();
    readonly Dictionary<int,RotatingBeacon> _beacons=new();
    /// <summary>Trailer tags appended after the original prototype/state layout. Older readers stop before them, and recordings
    /// without them decode with the original rules, so stored LAN replays stay readable in both directions.</summary>
    const int MaterialTrailer=0x314D574C, BeaconTrailer=0x3142574C;   // "LWM1", "LWB1"
    /// <summary>Test seam: write the pre-trailer layout, as builds before the trailers did.</summary>
    internal bool LegacyLayout;
    public int ReplicaBeaconCount=>_beacons.Count;
    public int ReplicaLightCount=>_lights.Count;
    public int ReplicaCount => _replicas.Count;
    internal int AudibleReplicaCount => _audible.Count;
    internal int VisibleReplicaCount => _replicas.Values.Count(n=>n.Visible);
    int _nextNode=1, _nextMesh=1;
    readonly long _prototypeBudget;
    long _prototypeBytes;
    readonly Dictionary<int,int> _prototypeSizes=new();
    bool _paused;
    readonly HashSet<int> _audible=new();
    public void SetPaused(bool paused) {_paused=paused;foreach(var (id,a) in _sounds) a.StreamPaused=paused || !_audible.Contains(id); }
    public LanWorld(Node parent,long prototypeBudget=0) { _prototypeBudget=prototypeBudget;_root=new Node3D {Name="LanWorld"}; parent.AddChild(_root); }
    public void Free() {SetPaused(true);_root.Hide();_root.QueueFree();_meshes.Clear();_replicas.Clear();_targets.Clear();_lights.Clear();_beacons.Clear();_sounds.Clear();_audible.Clear();}
    /// <summary>Drop posed instances on seek, retaining immutable decoded geometry for this recording.</summary>
    public void ResetPlaybackState()
    {
        SetPaused(true);
        foreach(Node child in _root.GetChildren()) {if(child is Node3D n) n.Hide();child.QueueFree();}
        _replicas.Clear();_targets.Clear();_sounds.Clear();_lights.Clear();_beacons.Clear();_audible.Clear();
    }
    static byte[] Pack(byte[] b) { using var m=new MemoryStream(); using(var z=new BrotliStream(m,CompressionLevel.Fastest,true)) z.Write(b); return m.ToArray(); }
    static byte[] Unpack(byte[] b)
    {
        using var z=new BrotliStream(new MemoryStream(b),CompressionMode.Decompress);
        using var m=new MemoryStream(); var buffer=new byte[8192]; int n;
        while((n=z.Read(buffer))>0) { if(m.Length+n>32*1024*1024) throw new InvalidDataException("world limit"); m.Write(buffer,0,n); }
        return m.ToArray();
    }
    static IEnumerable<MeshInstance3D> Leaves(Node node)
    {
        // A rotating beacon is rebuilt by each client (its beam is a camera-facing shader, its pulse follows the viewer).
        if(node is GroundImpactEffects or RotatingBeacon) yield break;
        if(node is MeshInstance3D m && m.Mesh!=null) yield return m;
        foreach(Node c in node.GetChildren()) foreach(var leaf in Leaves(c)) yield return leaf;
    }
    static IEnumerable<Light3D> Lights(Node node)
    {
        if(node is RotatingBeacon) yield break;
        if(node is SpotLight3D or OmniLight3D) yield return (Light3D)node;
        foreach(Node c in node.GetChildren()) foreach(var light in Lights(c)) yield return light;
    }
    static IEnumerable<AudioStreamPlayer3D> Sounds(Node node) {
        if(node is DieselSound or RotorSound or JetSound or HindGunSound) yield return (AudioStreamPlayer3D)node;
        foreach(Node c in node.GetChildren()) foreach(var a in Sounds(c)) yield return a;
    }
    static IEnumerable<RotatingBeacon> Beacons(Node node)
    {
        if(node is RotatingBeacon b) {yield return b;yield break;}
        foreach(Node c in node.GetChildren()) foreach(var beacon in Beacons(c)) yield return beacon;
    }
    static void V(BinaryWriter w,Vector3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
    static Vector3 V(BinaryReader r)=>new(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
    public void Capture(IEnumerable<Node3D> roots, IReadOnlyList<Obstacle> obstacles, IEnumerable<int> slots, Action<byte[],bool> send)
    {
        var leaves=new List<MeshInstance3D>(); var unique=new HashSet<ulong>();
        foreach(var root in roots) if(root!=null && GodotObject.IsInstanceValid(root))
            foreach(var leaf in Leaves(root)) if(unique.Add(leaf.GetInstanceId())) leaves.Add(leaf);
        using var stream=new MemoryStream(); using var w=new BinaryWriter(stream);
        w.Write(leaves.Count);
        foreach(var leaf in leaves)
        {
            if(!_nodeIds.TryGetValue(leaf.GetInstanceId(),out int id)) _nodeIds[leaf.GetInstanceId()]=id=_nextNode++;
            Mesh mesh=leaf.Mesh;
            // Material overrides are part of the prototype identity.
            ulong key=mesh.GetInstanceId() ^ ((leaf.MaterialOverride?.GetInstanceId() ?? 0)*1099511628211UL);
            if(!_meshIds.TryGetValue(key,out int mid))
            {
                _meshIds[key]=mid=_nextMesh++;
                using var ms=new MemoryStream(); using var mw=new BinaryWriter(ms);
                mw.Write(mid); mw.Write(mesh.GetSurfaceCount());
                var surfaceMaterials=new BaseMaterial3D[mesh.GetSurfaceCount()];
                for(int s=0;s<mesh.GetSurfaceCount();s++)
                {
                    byte[] arrays=GD.VarToBytes(mesh.SurfaceGetArrays(s)); mw.Write(arrays.Length); mw.Write(arrays);
                    mw.Write((int)(mesh is ArrayMesh arrayMesh ? arrayMesh.SurfaceGetPrimitiveType(s) : Mesh.PrimitiveType.Triangles));
                    var mat=(leaf.MaterialOverride ?? leaf.GetSurfaceOverrideMaterial(s) ?? mesh.SurfaceGetMaterial(s)) as BaseMaterial3D;
                    surfaceMaterials[s]=mat;
                    Color color=mat?.AlbedoColor ?? Colors.White;
                    mw.Write(color.R);mw.Write(color.G);mw.Write(color.B);mw.Write(color.A);
                    mw.Write(mat?.VertexColorUseAsAlbedo ?? false);
                    mw.Write((int)(mat?.ShadingMode ?? BaseMaterial3D.ShadingModeEnum.PerPixel));
                    mw.Write((int)(mat?.CullMode ?? BaseMaterial3D.CullModeEnum.Back));
                    byte[] texture=mat?.AlbedoTexture?.GetImage()?.SavePngToBuffer() ?? Array.Empty<byte>();
                    mw.Write(texture.Length);mw.Write(texture);
                    bool keep=mat?.HasMeta(NightGlow.KeepMeta) ?? false;mw.Write(keep);
                    if(keep) mw.Write(mat.GetMeta(NightGlow.KeepMeta).AsSingle());
                    bool emission=mat?.EmissionEnabled ?? false;mw.Write(emission);
                    if(emission) {
                        mw.Write(mat.Emission.R);mw.Write(mat.Emission.G);mw.Write(mat.Emission.B);
                        bool night=mat.HasMeta(NightGlow.EnergyMeta);mw.Write(night);
                        mw.Write(night?mat.GetMeta(NightGlow.EnergyMeta).AsSingle():mat.EmissionEnergyMultiplier);
                    }
                }
                if(!LegacyLayout) WriteMaterialTrailer(mw,leaf,surfaceMaterials);
                send(Pack(ms.ToArray()),true);
            }
            w.Write(id); w.Write(mid); w.Write(leaf.IsVisibleInTree());
            var t=leaf.GlobalTransform; V(w,t.Basis.X);V(w,t.Basis.Y);V(w,t.Basis.Z);V(w,t.Origin);
        }
        var indices=new List<int>(slots); w.Write(indices.Count);
        foreach(int i in indices) { var o=obstacles[i]; w.Write(i); w.Write(o.Center.X);w.Write(o.Center.Y);w.Write(o.Radius);w.Write(o.Velocity.X);w.Write(o.Velocity.Y); }
        var sounds=new List<AudioStreamPlayer3D>();var heard=new HashSet<ulong>();
        foreach(var root in roots) if(root!=null) foreach(var a in Sounds(root)) if(!a.StreamPaused && a.IsVisibleInTree() && heard.Add(a.GetInstanceId())) sounds.Add(a);
        w.Write(sounds.Count);
        foreach(var a in sounds) {
            if(!_nodeIds.TryGetValue(a.GetInstanceId(),out int id)) _nodeIds[a.GetInstanceId()]=id=_nextNode++;
            w.Write(id);V(w,a.GlobalPosition);
            if(a is DieselSound diesel) {w.Write((byte)0);w.Write(diesel.Speed);w.Write((float)((diesel.Dozing?1:0)|((int)diesel.Voice<<1)));w.Write(diesel.BaseRate);w.Write(diesel.RatePerSpeed);w.Write(diesel.Body);w.Write(diesel.Loudness);w.Write(diesel.Reach);}
            else { w.Write((byte)(a is RotorSound?1:a is JetSound?2:3));
                w.Write(a is RotorSound rotor?rotor.Level:a is JetSound jet?jet.Level:a is HindGunSound gun&&gun.Firing?1f:0);
                w.Write(a is JetSound j?j.Afterburner:0); }
        }
        var lights=new List<Light3D>();var lit=new HashSet<ulong>();
        foreach(var root in roots) if(root!=null) foreach(var light in Lights(root)) if(lit.Add(light.GetInstanceId())) lights.Add(light);
        w.Write(lights.Count);
        foreach(var light in lights) {
            if(!_nodeIds.TryGetValue(light.GetInstanceId(),out int id)) _nodeIds[light.GetInstanceId()]=id=_nextNode++;
            w.Write(id);w.Write((byte)(light is SpotLight3D?0:1));w.Write(light.IsVisibleInTree());
            var pose=light.GlobalTransform;V(w,pose.Basis.X);V(w,pose.Basis.Y);V(w,pose.Basis.Z);V(w,pose.Origin);
            w.Write(light.LightColor.R);w.Write(light.LightColor.G);w.Write(light.LightColor.B);w.Write(light.LightEnergy);
            if(light is SpotLight3D spot) {w.Write(spot.SpotRange);w.Write(spot.SpotAttenuation);w.Write(spot.SpotAngle);w.Write(spot.SpotAngleAttenuation);}
            else {var omni=(OmniLight3D)light;w.Write(omni.OmniRange);w.Write(omni.OmniAttenuation);}
        }
        if(!LegacyLayout)
        {
            var beacons=new List<RotatingBeacon>();var swept=new HashSet<ulong>();
            foreach(var root in roots) if(root!=null && GodotObject.IsInstanceValid(root))
                foreach(var b in Beacons(root)) if(swept.Add(b.GetInstanceId())) beacons.Add(b);
            w.Write(BeaconTrailer);w.Write(beacons.Count);
            foreach(var b in beacons) {
                if(!_nodeIds.TryGetValue(b.GetInstanceId(),out int id)) _nodeIds[b.GetInstanceId()]=id=_nextNode++;
                w.Write(id);w.Write(b.IsVisibleInTree());w.Write(b.Phase);
                var pose=b.GlobalTransform;V(w,pose.Basis.X);V(w,pose.Basis.Y);V(w,pose.Basis.Z);V(w,pose.Origin);
            }
        }
        send(Pack(stream.ToArray()),false);
    }
    /// <summary>The render state the original layout lacked: an alpha texture on an opaque-looking colour (the jet crash's scorch) was
    /// drawn as a solid slab, and additive, billboard, fog and depth settings were lost.</summary>
    static void WriteMaterialTrailer(BinaryWriter mw,MeshInstance3D leaf,BaseMaterial3D[] materials)
    {
        mw.Write(MaterialTrailer);mw.Write((int)leaf.CastShadow);mw.Write(materials.Length);
        foreach(var mat in materials)
        {
            mw.Write(mat!=null);
            if(mat==null) continue;
            mw.Write((int)mat.Transparency);mw.Write(mat.AlphaScissorThreshold);mw.Write((int)mat.BlendMode);mw.Write((int)mat.DepthDrawMode);
            mw.Write(mat.NoDepthTest);mw.Write(mat.DisableFog);mw.Write((int)mat.BillboardMode);mw.Write(mat.BillboardKeepScale);mw.Write((int)mat.TextureFilter);
        }
    }
    static bool Trailer(BinaryReader r,int tag)
    {
        if(r.BaseStream.Length-r.BaseStream.Position<4) return false;
        if(r.ReadInt32()==tag) return true;
        r.BaseStream.Position-=4;return false;
    }
    static T Enum<T>(BinaryReader r) where T:struct,System.Enum
    {
        var v=(T)System.Enum.ToObject(typeof(T),r.ReadInt32());if(!System.Enum.IsDefined(v)) throw new InvalidDataException("world material state");
        return v;
    }
    public void MeshIn(byte[] bytes)
    {
        byte[] raw=Unpack(bytes);
        using var r=new BinaryReader(new MemoryStream(raw));
        int id=r.ReadInt32(), n=r.ReadInt32();
        long total=_prototypeBytes-(_prototypeSizes.TryGetValue(id,out int previous)?previous:0)+raw.LongLength;
        if(_prototypeBudget>0 && total>_prototypeBudget) throw new InvalidDataException("LAN replay geometry memory limit");
        _prototypeBytes=total;_prototypeSizes[id]=raw.Length; if(n<0||n>256) throw new InvalidDataException("mesh surfaces");
        var mesh=new ArrayMesh();var materials=new StandardMaterial3D[n];
        for(int s=0;s<n;s++)
        {
            int size=r.ReadInt32(); if(size<0||size>24*1024*1024) throw new InvalidDataException("mesh arrays");
            var arrays=GD.BytesToVar(r.ReadBytes(size)).AsGodotArray();
            var primitive=(Mesh.PrimitiveType)r.ReadInt32();
            var mat=new StandardMaterial3D { AlbedoColor=new Color(r.ReadSingle(),r.ReadSingle(),r.ReadSingle(),r.ReadSingle()),VertexColorUseAsAlbedo=r.ReadBoolean(),ShadingMode=(BaseMaterial3D.ShadingModeEnum)r.ReadInt32(),CullMode=(BaseMaterial3D.CullModeEnum)r.ReadInt32() };
            if(mat.AlbedoColor.A<.99f) mat.Transparency=BaseMaterial3D.TransparencyEnum.Alpha;
            size=r.ReadInt32(); if(size<0||size>8*1024*1024) throw new InvalidDataException("texture");
            if(size>0) {var im=new Image();im.LoadPngFromBuffer(r.ReadBytes(size)); mat.AlbedoTexture=ImageTexture.CreateFromImage(im);}
            if(r.ReadBoolean()) mat.SetMeta(NightGlow.KeepMeta,r.ReadSingle());
            if(r.ReadBoolean()) {
                mat.EmissionEnabled=true;mat.Emission=new(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
                bool night=r.ReadBoolean();float energy=r.ReadSingle();mat.EmissionEnergyMultiplier=energy*(night?NightGlow.Level:1);
                if(night) {mat.SetMeta(NightGlow.EnergyMeta,energy);NightGlow.Glows.Add((mat,energy));}
            }
            mesh.AddSurfaceFromArrays(primitive,arrays);mesh.SurfaceSetMaterial(s,mat);materials[s]=mat;
        }
        if(Trailer(r,MaterialTrailer))
        {
            var shadow=Enum<GeometryInstance3D.ShadowCastingSetting>(r);
            if(r.ReadInt32()!=n) throw new InvalidDataException("world material count");
            foreach(var mat in materials)
            {
                if(!r.ReadBoolean()) continue;
                mat.Transparency=Enum<BaseMaterial3D.TransparencyEnum>(r);mat.AlphaScissorThreshold=r.ReadSingle();
                mat.BlendMode=Enum<BaseMaterial3D.BlendModeEnum>(r);mat.DepthDrawMode=Enum<BaseMaterial3D.DepthDrawModeEnum>(r);
                mat.NoDepthTest=r.ReadBoolean();mat.DisableFog=r.ReadBoolean();
                mat.BillboardMode=Enum<BaseMaterial3D.BillboardModeEnum>(r);mat.BillboardKeepScale=r.ReadBoolean();
                mat.TextureFilter=Enum<BaseMaterial3D.TextureFilterEnum>(r);
            }
            _shadows[id]=shadow;
        }
        else _shadows.Remove(id);
        _meshes[id]=mesh;
    }
    public void StateIn(byte[] bytes, IList<Obstacle> obstacles)
    {
        using var r=new BinaryReader(new MemoryStream(Unpack(bytes)));
        int n=r.ReadInt32(); if(n<0||n>20000) throw new InvalidDataException("world nodes");
        var seen=new HashSet<int>();
        for(int i=0;i<n;i++)
        {
            int id=r.ReadInt32(), mid=r.ReadInt32();bool visible=r.ReadBoolean();
            var t=new Transform3D(new Basis(V(r),V(r),V(r)),V(r));seen.Add(id);
            if(!_replicas.TryGetValue(id,out var node)) { if(!_meshes.TryGetValue(mid,out var mesh)) continue;
                node=new MeshInstance3D {Mesh=mesh};
                if(_shadows.TryGetValue(mid,out var shadow)) node.CastShadow=shadow;
                _root.AddChild(node);node.GlobalTransform=t;_replicas[id]=node; }
            node.Visible=visible;_targets[id]=t;
        }
        foreach(var (id,node) in _replicas) if(!seen.Contains(id)) node.Visible=false;
        n=r.ReadInt32(); if(n<0||n>20000) throw new InvalidDataException("world obstacles");
        for(int k=0;k<n;k++) {int i=r.ReadInt32();var c=new Vector2(r.ReadSingle(),r.ReadSingle());float radius=r.ReadSingle();var v=new Vector2(r.ReadSingle(),r.ReadSingle());
            if(i>=0&&i<obstacles.Count&&c.IsFinite()&&v.IsFinite()&&float.IsFinite(radius)) obstacles[i]=obstacles[i] with {Center=c,Radius=radius,Velocity=v}; }
        n=r.ReadInt32();if(n<0||n>512) throw new InvalidDataException("world sounds");
        var heard=new HashSet<int>();
        for(int k=0;k<n;k++) {
            int id=r.ReadInt32();var pos=V(r);int type=r.ReadByte();float level=r.ReadSingle(),extra=r.ReadSingle();
            if(!_sounds.TryGetValue(id,out var audio)) {
                audio=type switch {0=>new DieselSound(),1=>new RotorSound(),2=>new JetSound(),_=>new HindGunSound()};
                _root.AddChild(audio);_sounds[id]=audio;
            }
            audio.GlobalPosition=pos;heard.Add(id);audio.StreamPaused=_paused;
            if(audio is DieselSound d) {d.Speed=level;int engineFlags=(int)extra;d.Dozing=(engineFlags&1)!=0;d.Voice=(DieselSound.EngineVoice)(engineFlags>>1);d.BaseRate=r.ReadSingle();d.RatePerSpeed=r.ReadSingle();d.Body=r.ReadSingle();d.Loudness=r.ReadSingle();d.Reach=r.ReadSingle();d.VolumeDb=d.Loudness;d.MaxDistance=d.Reach;}
            else if(audio is RotorSound rotor) rotor.Level=level;
            else if(audio is JetSound jet) {jet.Level=level;jet.Afterburner=extra;}
            else if(audio is HindGunSound gun) gun.Firing=level>0;
        }
        foreach(var (id,a) in _sounds) if(!heard.Contains(id)) a.StreamPaused=true;
        _audible.Clear();_audible.UnionWith(heard);
        n=r.ReadInt32();if(n<0||n>512) throw new InvalidDataException("world lights");
        var lit=new HashSet<int>();
        for(int i=0;i<n;i++) {
            int id=r.ReadInt32();byte type=r.ReadByte();if(type>1) throw new InvalidDataException("world light type");
            bool visible=r.ReadBoolean();var pose=new Transform3D(new Basis(V(r),V(r),V(r)),V(r));
            if(!_lights.TryGetValue(id,out var light)) {
                light=type==0?new SpotLight3D():new OmniLight3D();light.ShadowEnabled=false;light.LightSpecular=0;
                _root.AddChild(light);light.GlobalTransform=pose;_lights[id]=light;
            }
            light.Visible=visible;light.LightColor=new(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());light.LightEnergy=r.ReadSingle();
            if(light is SpotLight3D spot) {spot.SpotRange=r.ReadSingle();spot.SpotAttenuation=r.ReadSingle();spot.SpotAngle=r.ReadSingle();spot.SpotAngleAttenuation=r.ReadSingle();}
            else {var omni=(OmniLight3D)light;omni.OmniRange=r.ReadSingle();omni.OmniAttenuation=r.ReadSingle();}
            _targets[id]=pose;lit.Add(id);
        }
        foreach(var (id,light) in _lights) if(!lit.Contains(id)) light.Visible=false;
        var beaconsSeen=new HashSet<int>();
        if(Trailer(r,BeaconTrailer))
        {
            n=r.ReadInt32();if(n<0||n>512) throw new InvalidDataException("world beacons");
            for(int i=0;i<n;i++) {
                int id=r.ReadInt32();bool visible=r.ReadBoolean();float phase=r.ReadSingle();
                var pose=new Transform3D(new Basis(V(r),V(r),V(r)),V(r));
                if(!float.IsFinite(phase) || !pose.Origin.IsFinite()) throw new InvalidDataException("world beacon pose");
                if(!_beacons.TryGetValue(id,out var beacon)) {
                    beacon=RotatingBeacon.Replica(phase);_root.AddChild(beacon);beacon.GlobalTransform=pose;_beacons[id]=beacon;
                }
                beacon.Visible=visible;_targets[id]=pose;beaconsSeen.Add(id);
            }
        }
        foreach(var (id,beacon) in _beacons) if(!beaconsSeen.Contains(id)) beacon.Visible=false;
    }
    Node3D Target(int id)=>_replicas.TryGetValue(id,out var m)?m:_lights.TryGetValue(id,out var l)?l:_beacons.TryGetValue(id,out var b)?b:null;
    public bool AtTargets()=>_targets.All(k=>{Node3D n=Target(k.Key);return n==null || n.GlobalTransform.IsEqualApprox(k.Value);});
    public void Snap() {foreach(var (id,t) in _targets) {Node3D n=Target(id);if(n!=null) n.GlobalTransform=t;}}
    public void Frame(float dt)
    {
        float blend=1-MathF.Exp(-dt*35);
        foreach(var (id,t) in _targets) {
            Node3D n=Target(id);
            if(n==null) continue;
            // Scaling and rotor poses may not form a rotation basis; direct basis avoids invalid quaternion interpolation.
            var old=n.GlobalTransform;
            n.GlobalTransform=new Transform3D(new Basis(old.Basis.X.Lerp(t.Basis.X,blend),old.Basis.Y.Lerp(t.Basis.Y,blend),old.Basis.Z.Lerp(t.Basis.Z,blend)),old.Origin.DistanceSquaredTo(t.Origin)>100?t.Origin:old.Origin.Lerp(t.Origin,blend));
        }
    }
}
