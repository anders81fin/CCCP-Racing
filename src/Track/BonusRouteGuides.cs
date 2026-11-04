using System;
using Godot;

namespace CCCPRacing.Track;

/// <summary>Low roadside reflectors and outside-bend chevrons distinguish the raceway from camp access.</summary>
public static class BonusRouteGuides
{
    public static Node3D Build(TrackData track)
    {
        var root=new Node3D {Name="PoligonRouteGuides"};
        if(track.Id!="bonus") return root;
        bool Clear(Vector2 p)
        {
            int road=track.NearestCenter(p,out float distance);
            if(distance<track.HalfWidth[road]+.9f || BonusAirbase.ServiceDistance(p)<3) return false;
            foreach(var obstacle in track.Obstacles)
                if(p.DistanceTo(obstacle.Center)<obstacle.Radius+.6f) return false;
            return true;
        }
        for(int i=0;i<track.Center.Length;i+=18)
            foreach(float sign in new[]{-1f,1f})
            {
                Vector2 side=track.CenterDir(i).Orthogonal()*sign;
                Vector2 p=track.Center[i]+side*(track.HalfWidth[i]+1.4f);
                if(!Clear(p)) continue;
                Vector3 at=new(p.X,track.MeshHeight(p.X,p.Y),p.Y);
                root.AddChild(Props.Part(Props.Box(.14f,.8f,.14f),new Color(.73f,.72f,.60f),at+Vector3.Up*.38f));
                root.AddChild(Props.Part(Props.Box(.16f,.18f,.16f),new Color(.14f,.16f,.12f),at+Vector3.Up*.48f));
                root.AddChild(NightGlow.Part(Props.Box(.17f,.13f,.17f),new(.9f,.72f,.25f),new(1,.76f,.3f),1.2f,at+Vector3.Up*.72f));
            }
        foreach(Vector2 bend in new[]{new Vector2(48,250),new Vector2(100,212),new Vector2(192,150),new Vector2(137,228),new Vector2(265,95),new Vector2(145,60)})
        {
            int i=track.NearestCenter(bend,out _),n=track.Center.Length;
            Vector2 before=track.CenterDir((i-10+n)%n),after=track.CenterDir((i+10)%n);
            float turn=Math.Sign(before.Cross(after));if(turn==0) continue;
            Vector2 p=track.Center[i]-track.CenterDir(i).Orthogonal()*turn*(track.HalfWidth[i]+2.1f);
            if(!Clear(p)) continue;
            var sign=new Node3D {Position=new Vector3(p.X,track.MeshHeight(p.X,p.Y),p.Y),Rotation=new Vector3(0,TrackData.YawFor(before),0)};
            root.AddChild(sign);
            foreach(float x in new[]{-.8f,.8f}) sign.AddChild(Props.Part(Props.Box(.07f,1.5f,.07f),new(.25f,.27f,.22f),new(x,.75f,0)));
            sign.AddChild(Props.Part(Props.Box(2.25f,.65f,.09f),new(.12f,.14f,.11f),new(0,1.4f,0)));
            foreach(float x in new[]{-.66f,0,.66f}) foreach(float half in new[]{-1f,1f})
            {
                var arrow=NightGlow.Part(Props.Box(.12f,.38f,.045f),new(.92f,.79f,.37f),new(1,.83f,.4f),.9f,new(x,1.4f+half*.14f,.07f));
                arrow.Rotation=new Vector3(0,0,turn*half*.65f);sign.AddChild(arrow);
            }
        }
        return root;
    }
}
