using System;
using Godot;
namespace CCCPRacing.Car;

/// <summary>Equal and opposite impulses between the cars' three hull circles, including yaw inertia.</summary>
public static class CarContacts
{
    static float InvInertia(CarSim c, Vector3 r, Vector3 n)
    {
        var t=c.Rot.Transposed() * r.Cross(n);
        return t.X*t.X/c.P.Inertia.X+t.Y*t.Y/c.P.Inertia.Y+t.Z*t.Z/c.P.Inertia.Z;
    }
    static void Impulse(CarSim c, Vector3 r, Vector3 j)
    {
        c.Vel+=j/c.P.Mass;
        var t=c.Rot.Transposed()*r.Cross(j);
        c.AngVel+=c.Rot*new Vector3(t.X/c.P.Inertia.X,t.Y/c.P.Inertia.Y,t.Z/c.P.Inertia.Z);
    }
    public static float Resolve(CarSim a, CarSim b) => Resolve(a,b,false);

    /// <summary>Predict against a scratch remote pose. Only the local car is repositioned; no network puppet or host state is changed.</summary>
    public static float Predict(CarSim local, CarSim remote) => Resolve(local,remote,true);

    public static int Steps(float dt, float maxSpeed, int minimum=2)
        => Math.Clamp((int)Math.Ceiling(2*Math.Max(0,maxSpeed)*dt/.25f),minimum,16);

    static float Resolve(CarSim a, CarSim b, bool prediction)
    {
        if (Math.Abs(a.Pos.Y-b.Pos.Y)>1.45f || a.Pos.DistanceSquaredTo(b.Pos)>36) return 0;
        float hit=0;
        foreach (float za in CarSim.CollisionCircleZ) foreach (float zb in CarSim.CollisionCircleZ)
        {
            Vector3 pa=a.Pos+a.Rot*new Vector3(0,0,za), pb=b.Pos+b.Rot*new Vector3(0,0,zb);
            var d=pa-pb; d.Y=0; float len=d.Length(), depth=2*CarSim.CollisionRadius-len;
            if (depth<=0) continue;
            var n=len>.001f?d/len:Vector3.Right;
            var point=(pa+pb)*.5f; var ra=point-a.Pos; var rb=point-b.Pos;
            float inv=1/a.P.Mass+1/b.P.Mass;
            // Remove penetration now, like traffic contacts. A predicted remote pose stays fixed;
            // its velocity below is scratch state used only to compute this local response.
            if(prediction) a.Pos+=n*depth;
            else {var correction=n*(depth/inv);a.Pos+=correction/a.P.Mass;b.Pos-=correction/b.P.Mass;}
            var relative=a.Vel+a.AngVel.Cross(ra)-b.Vel-b.AngVel.Cross(rb);
            float closing=relative.Dot(n);
            if (closing>=0) continue;
            float j=-1.12f*closing/(inv+InvInertia(a,ra,n)+InvInertia(b,rb,n));
            Impulse(a,ra,n*j); Impulse(b,rb,-n*j);
            var tangent=relative-n*closing;
            if (tangent.LengthSquared()>.0001f)
            {
                tangent=tangent.Normalized();
                float f=Math.Min(j*.3f, relative.Dot(tangent)/(inv+InvInertia(a,ra,tangent)+InvInertia(b,rb,tangent)));
                Impulse(a,ra,-tangent*f); Impulse(b,rb,tangent*f);
            }
            hit=Math.Max(hit,-closing);
            a.NetworkImpact(-closing,point,n); b.NetworkImpact(-closing,point,-n);
        }
        return hit;
    }
}
