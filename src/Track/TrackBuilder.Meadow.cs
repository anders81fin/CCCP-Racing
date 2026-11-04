using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;
public static partial class TrackBuilder
{
    // After road grading, before pads/water: remove the artificial rim only away
    // from protected driving/landmark zones, replacing it with the shared meadow.
    static IEnumerable<int> ShapeMeadowSteps(TrackDef def, TrackData t, CenterGrid grid)
    {
        if(def.Meadow==null)yield break;
        var clock=System.Diagnostics.Stopwatch.StartNew();int nv=t.N+1;
        var meadowWeight=new float[nv*nv];
        float RectDistance(Vector2 p,float x0,float z0,float x1,float z1)
        {
            float dx=Math.Max(0,Math.Max(x0-p.X,p.X-x1));
            float dz=Math.Max(0,Math.Max(z0-p.Y,p.Y-z1));
            return MathF.Sqrt(dx*dx+dz*dz);
        }
        for(int iz=0;iz<nv;iz++)
        {
            if(clock.ElapsedMilliseconds>10){yield return 0;clock.Restart();}
            for(int ix=0;ix<nv;ix++)
            {
                var p=new Vector2(ix*t.Tile,iz*t.Tile);
                int ci=grid.Nearest(p,48,out float road);
                float weight=ci<0?1:SmoothStep(t.HalfWidth[ci]+6,t.HalfWidth[ci]+36,road);
                if(weight<=0)continue;
                if(def.Water!=null)weight=Math.Min(weight,SmoothStep(6,22,WaterDistance(def.Water,p,out _)));
                foreach(var pad in def.Pads)
                    weight=Math.Min(weight,SmoothStep(6,16,RectDistance(p,pad.X0,pad.Z0,pad.X1,pad.Z1)));
                foreach(var prop in def.Props)
                {
                    if(prop.Type=="dormitory")
                    {
                        float length=float.TryParse(prop.Text,System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,out float value)?value:84;
                        float angle=Mathf.DegToRad(prop.Rot);
                        var delta=p-new Vector2(prop.X,prop.Z);
                        var local=new Vector2(delta.Dot(new Vector2(MathF.Cos(angle),-MathF.Sin(angle))),
                            delta.Dot(new Vector2(MathF.Sin(angle),MathF.Cos(angle))));
                        weight=Math.Min(weight,SmoothStep(6,18,RectDistance(local,-2,-6,length+2,9)));
                    }
                    else if(prop.Type is "barn" or "bigbarn" or "church" or "izba")
                        weight=Math.Min(weight,SmoothStep(16,30,p.DistanceTo(new Vector2(prop.X,prop.Z))));
                }
                if(weight<=0)continue;
                float hills=HillsHeight(def,p.X,p.Y,t.WorldSize,out float calm);
                float natural=def.HillAmp*Noise.Fbm(p.X/def.HillScale,p.Y/def.HillScale,def.Seed,4)*(1-calm)+hills;
                float target=Math.Max(natural,def.Meadow.HeightAt(p.X,p.Y))+t.RoadLandformHeight(ci,road);
                int index=iz*nv+ix;
                meadowWeight[index]=weight;
                t.Heights[index]=Mathf.Lerp(t.Heights[index],target,weight);
            }
        }
        // Ease the transition from preserved roads/landmarks into the new ground.
        // Use a frozen source per pass so no scan direction introduces ridges.
        // Protected vertices are never touched; water and foundations are graded later.
        var source=new float[t.Heights.Length];
        for(int pass=0;pass<24;pass++)
        {
            Array.Copy(t.Heights,source,source.Length);
            for(int iz=1;iz<t.N;iz++)
            {
                if(clock.ElapsedMilliseconds>10){yield return 0;clock.Restart();}
                for(int ix=1;ix<t.N;ix++)
                {
                    int k=iz*nv+ix;float weight=meadowWeight[k];
                    if(weight<=0)continue;
                    float average=(source[k-1]+source[k+1]+source[k-nv]+source[k+nv])*.25f;
                    t.Heights[k]=Mathf.Lerp(source[k],average,.75f*weight);
                }
            }
        }
    }
}
