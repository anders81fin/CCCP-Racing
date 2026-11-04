using System;
using Godot;

namespace CCCPRacing.Car;

/// <summary>Projectile contacts against the car's oriented body, including its roof and both ends.</summary>
public static class ProjectileHits
{
    static readonly Vector3 Min=new(-.95f,-.4f,-2.2f),Max=new(.95f,1.05f,2.2f);

    public static float Distance(CarSim car,Vector3 point)
    {
        Vector3 p=car.Rot.Transposed()*(point-car.Pos);
        return (p-p.Clamp(Min,Max)).Length();
    }

    public static bool Segment(CarSim car,Vector3 from,Vector3 to,out float fraction)
    {
        Basis inverse=car.Rot.Transposed();
        Vector3 start=inverse*(from-car.Pos),delta=inverse*(to-from);
        float enter=0,exit=1;
        fraction=0;
        for(int axis=0;axis<3;axis++)
        {
            if(Math.Abs(delta[axis])<.000001f)
            {
                if(start[axis]<Min[axis] || start[axis]>Max[axis]) return false;
                continue;
            }
            float a=(Min[axis]-start[axis])/delta[axis],b=(Max[axis]-start[axis])/delta[axis];
            enter=Math.Max(enter,Math.Min(a,b));exit=Math.Min(exit,Math.Max(a,b));
            if(enter>exit) return false;
        }
        fraction=enter;
        return true;
    }
}
