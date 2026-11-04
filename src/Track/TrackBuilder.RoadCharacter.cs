using System;
using Godot;

namespace CCCPRacing.Track;

public static partial class TrackBuilder
{
    static void ShapeRoadEdges(TrackDef def, TrackData t)
    {
        if(def.RoadCharacter is not {} shape) return;
        int count=t.Center.Length;
        t.RoadLeftInset=new float[count];
        t.RoadRightInset=new float[count];
        for(int i=0;i<count;i++)
        {
            float protection=SmoothStep(shape.From,shape.From+18,i)
                *(1-SmoothStep(shape.To-18,shape.To,i));
            // The bus stopping place remains a full-width passing space.
            foreach(var stop in def.BusStops)
                protection*=SmoothStep(12,24,Math.Abs(i-stop.At));
            float reduction=0;
            foreach(var pinch in shape.Pinches)
            {
                float weight=SmoothStep(pinch.From,pinch.From+pinch.Taper,i)
                    *(1-SmoothStep(pinch.To-pinch.Taper,pinch.To,i));
                reduction=Math.Max(reduction,Math.Clamp(pinch.Reduction,0,.2f)*weight);
            }
            float original=t.HalfWidth[i];
            t.HalfWidth[i]=Math.Max(Math.Min(3.6f,original),original*(1-reduction*protection));
            // Independent, nonperiodic wear on the two edges. The nominal road remains
            // the conservative clearance used to place traffic, fences and buildings.
            float amplitude=Math.Min(Math.Clamp(shape.EdgeInset,0,.6f)*protection,
                Math.Max(0,t.HalfWidth[i]-original*.8f));
            t.RoadLeftInset[i]=amplitude*(.6f+.4f*Noise.Value(i/17f,3.7f,shape.Seed+219));
            t.RoadRightInset[i]=amplitude*(.6f+.4f*Noise.Value(i/23f,8.2f,shape.Seed+541));
        }
    }
}
