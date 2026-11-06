using System;
using System.Collections.Generic;
using System.Linq;
using CCCPRacing.Car;
using CCCPRacing.Net;
using CCCPRacing.Track;
using Godot;
namespace CCCPRacing.Tests;

/// <summary>LAN client visuals on Siberia: the jet crash's scorch must stay a ragged see-through decal (it was a solid black slab),
/// and the snowploughs' rotating beacons must be rebuilt by the client (their beam is a camera-facing shader and was replicated as a
/// frozen opaque white strip). Uses the production actors, the production crash function and the production LanWorld codec.</summary>
public partial class PhysicsTest
{
    void LanVisualsTest()
    {
        LanVisualsTest("siberia");
        LanVisualsTest("atomgrad");
    }

    void LanVisualsTest(string trackId)
    {
        GD.Print($"LAN visual roundtrip: {trackId}");
        var def=TrackDef.Load($"res://tracks/{trackId}.json");var track=TrackBuilder.Build(def);
        var fx=new TyreEffects();AddChild(fx);fx.Init(track);
        var ploughs=new SnowPloughs {Name="SnowPloughs"};AddChild(ploughs);ploughs.Init(track,fx,def.Ploughs);
        var jets=new BalalaikaSorties {Name="BalalaikaSorties"};AddChild(jets);jets.Init(track,fx);
        var fxParent=new Node3D();AddChild(fxParent);

        // The crash site exactly as the live Explode makes it: scorch and wreck under the jets' actor root.
        Vector3 at=new(track.Center[40].X,track.MeshHeight(track.Center[40].X,track.Center[40].Y),track.Center[40].Y);
        BalalaikaSorties.CrashFx(null,new RandomNumberGenerator {Seed=7},fxParent,fxParent,at,out var scorch,out var wreck);
        jets.AddChild(scorch);jets.AddChild(wreck);
        var roots=new Node3D[]{ploughs,jets};
        var obstacles=new List<Obstacle>();

        bool Excluded(Node n) {for(var p=n;p!=null;p=p.GetParent()) if(p is RotatingBeacon or GroundImpactEffects) return true;return false;}
        var hostLeaves=roots.SelectMany(r=>r.FindChildren("*","MeshInstance3D",true,false)).Cast<MeshInstance3D>().Where(m=>m.Mesh!=null && !Excluded(m)).ToList();
        var hostBeacons=ploughs.FindChildren("*","",true,false).OfType<RotatingBeacon>().ToList();
        int hostLights=roots.SelectMany(r=>r.FindChildren("*","",true,false)).Count(n=>n is SpotLight3D or OmniLight3D && !Excluded(n));
        Check(hostBeacons.Count>0 && hostBeacons.Count==def.Ploughs.Count,"LAN visuals fixture: every Siberia plough carries a rotating beacon");

        // Sweep: nothing the client receives as a mesh may depend on a material the codec cannot carry (a shader is rebuilt locally or excluded).
        var unsupported=hostLeaves.SelectMany(m=>Enumerable.Range(0,m.Mesh.GetSurfaceCount()).Select(s=>(m,mat:m.MaterialOverride ?? m.GetSurfaceOverrideMaterial(s) ?? m.Mesh.SurfaceGetMaterial(s))))
            .Where(x=>x.mat!=null && x.mat is not BaseMaterial3D).Select(x=>x.m.GetPath().ToString()).Distinct().ToList();
        Check(unsupported.Count==0,$"LAN: replicated Siberia plough/jet/crash meshes use only codec-supported materials ({string.Join(", ",unsupported.Take(3))})");

        LanWorld Receive(bool legacy,out Node3D client)
        {
            client=new Node3D();AddChild(client);
            var sender=new LanWorld(fxParent) {LegacyLayout=legacy};var receiver=new LanWorld(client);
            sender.Capture(roots,obstacles,Array.Empty<int>(),(bytes,mesh)=>{if(mesh) receiver.MeshIn(bytes);else receiver.StateIn(bytes,obstacles);});
            receiver.Snap();sender.Free();
            return receiver;
        }
        static MeshInstance3D ScorchReplica(Node client)=>client.FindChildren("*","MeshInstance3D",true,false).Cast<MeshInstance3D>()
            .FirstOrDefault(m=>m.Mesh?.SurfaceGetMaterial(0) is StandardMaterial3D {AlbedoTexture: {} t} && t.GetWidth()==24);

        var world=Receive(false,out var client);
        var replica=ScorchReplica(client);
        var mat=replica?.Mesh.SurfaceGetMaterial(0) as StandardMaterial3D;
        var image=mat?.AlbedoTexture.GetImage();
        Check(mat!=null && mat.Transparency==BaseMaterial3D.TransparencyEnum.Alpha && mat.TextureFilter==BaseMaterial3D.TextureFilterEnum.Nearest
            && mat.ShadingMode==BaseMaterial3D.ShadingModeEnum.Unshaded && replica.CastShadow==GeometryInstance3D.ShadowCastingSetting.Off,
            $"LAN: the jet crash scorch keeps its alpha-blended, unshaded, unfiltered decal material on the client ({mat?.Transparency}, {mat?.TextureFilter}, {replica?.CastShadow})");
        Check(image!=null && image.GetPixel(0,0).A<.05f && image.GetPixel(12,12).A>.8f && replica.GlobalTransform.Origin.DistanceTo(scorch.GlobalPosition)<.001f,
            "LAN: the scorch's ragged edge is see-through and its burnt centre opaque, at the host's crash position");
        Check(client.FindChildren("*","MeshInstance3D",true,false).Cast<MeshInstance3D>().Count(m=>m.Mesh!=null && !Excluded(m))==hostLeaves.Count && world.ReplicaCount==hostLeaves.Count,
            $"LAN: every plough, jet and wreck mesh is replicated and the beacon's beam and halo are not ({world.ReplicaCount} of {hostLeaves.Count})");

        // Beacons: one local rebuild per host beacon, posed like it, with the real beam shader and its own lamps.
        var replicaBeacons=client.FindChildren("*","",true,false).OfType<RotatingBeacon>().ToList();
        bool posed=hostBeacons.All(h=>replicaBeacons.Any(r=>r.GlobalTransform.IsEqualApprox(h.GlobalTransform) && r.Phase==h.Phase && r.Visible));
        bool shader=replicaBeacons.All(r=>r.FindChildren("*","MeshInstance3D",true,false).Cast<MeshInstance3D>()
            .Any(m=>m.MaterialOverride is ShaderMaterial {Shader: {} s} && s.Code.Contains("blend_add") && s.Code.Contains("CAMERA_POSITION_WORLD")));
        Check(world.ReplicaBeaconCount==hostBeacons.Count && replicaBeacons.Count==hostBeacons.Count && posed && shader,
            $"LAN: each plough beacon is rebuilt on the client with its camera-facing additive beam, at the host pose and phase ({replicaBeacons.Count}/{hostBeacons.Count})");
        Check(world.ReplicaLightCount==hostLights,$"LAN: the beacon's sweeping lamps are not doubled as frozen host lights ({world.ReplicaLightCount} vs {hostLights})");

        // The beacon follows its truck, and hides with it.
        var model=(Node3D)hostBeacons[0].GetParent();
        world.Free();client.QueueFree();
        client=new Node3D();AddChild(client);
        var sender2=new LanWorld(fxParent);var receiver2=new LanWorld(client);
        void Deliver(byte[] b,bool m) {if(m) receiver2.MeshIn(b);else receiver2.StateIn(b,obstacles);}
        sender2.Capture(roots,obstacles,Array.Empty<int>(),Deliver);receiver2.Snap();
        model.GlobalPosition+=new Vector3(30,0,-12);model.RotateY(1.1f);
        sender2.Capture(roots,obstacles,Array.Empty<int>(),Deliver);
        for(int i=0;i<120;i++) receiver2.Frame(1/60f);
        var follower=client.FindChildren("*","",true,false).OfType<RotatingBeacon>().FirstOrDefault(r=>r.Phase==hostBeacons[0].Phase);
        Check(follower!=null && follower.GlobalPosition.DistanceTo(hostBeacons[0].GlobalPosition)<.01f && follower.GlobalTransform.Basis.X.DistanceTo(hostBeacons[0].GlobalTransform.Basis.X)<.01f,
            "LAN: a replicated beacon follows its moving, turning plough");
        model.Visible=false;
        sender2.Capture(roots,obstacles,Array.Empty<int>(),Deliver);
        Check(follower!=null && !follower.Visible,"LAN: a hidden plough hides its replicated beacon");
        model.Visible=true;
        // LAN replay seek: posed instances are dropped and rebuilt from the next state, decoded geometry kept.
        receiver2.ResetPlaybackState();
        sender2.Capture(roots,obstacles,Array.Empty<int>(),Deliver);
        Check(receiver2.ReplicaBeaconCount==hostBeacons.Count && receiver2.ReplicaCount==hostLeaves.Count,"LAN replay seek rebuilds the beacons and replicas without duplicates");
        sender2.Free();receiver2.Free();client.QueueFree();

        // Recordings made before the trailers: still decoded, with the old look (no exception, no beacons, opaque scorch).
        var legacy=Receive(true,out var oldClient);
        var oldMat=ScorchReplica(oldClient)?.Mesh.SurfaceGetMaterial(0) as StandardMaterial3D;
        Check(legacy.ReplicaCount==hostLeaves.Count && legacy.ReplicaBeaconCount==0 && oldMat!=null && oldMat.Transparency==BaseMaterial3D.TransparencyEnum.Disabled,
            "LAN: a legacy world stream (no material/beacon trailers) still decodes, with its original opaque scorch and no beacons");
        legacy.Free();oldClient.QueueFree();

        ploughs.QueueFree();jets.QueueFree();fxParent.QueueFree();fx.QueueFree();
    }
}
