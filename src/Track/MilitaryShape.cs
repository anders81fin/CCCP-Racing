using System;
using Godot;

namespace CCCPRacing.Track;

/// <summary>Closed, faceted armoured shells with broad panels rather than spherical toy bodies.</summary>
public static class MilitaryShape
{
    public static MeshInstance3D Loft(Vector3[] centres, Vector2[] radii, Color paint, Func<Vector3,Color> paintAt=null)
    {
        var st=new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 Ring(int row,int corner)
        {
            float a=(corner+.5f)*MathF.Tau/8;
            return centres[row]+new Vector3(MathF.Cos(a)*radii[row].X,MathF.Sin(a)*radii[row].Y,0);
        }
        void Triangle(Vector3 a,Vector3 b,Vector3 c,Color colour)
        {
            st.SetColor(colour); st.SetNormal((b-a).Cross(c-a).Normalized());
            st.AddVertex(a);st.AddVertex(b);st.AddVertex(c);
        }
        for(int row=0;row<centres.Length-1;row++)
            for(int side=0;side<8;side++)
            {
                var a=Ring(row,side);var b=Ring(row,side+1);var c=Ring(row+1,side);var d=Ring(row+1,side+1);
                int divisions=paintAt==null?1:5;
                Vector3 Point(float u,float v)=>a.Lerp(b,u).Lerp(c.Lerp(d,u),v);
                for(int x=0;x<divisions;x++) for(int y=0;y<divisions;y++)
                {
                    var p=Point(x/(float)divisions,y/(float)divisions);
                    var q=Point((x+1)/(float)divisions,y/(float)divisions);
                    var r=Point(x/(float)divisions,(y+1)/(float)divisions);
                    var t=Point((x+1)/(float)divisions,(y+1)/(float)divisions);
                    Triangle(p,q,r,paintAt?.Invoke((p+q+r)/3)??paint);
                    Triangle(q,t,r,paintAt?.Invoke((q+t+r)/3)??paint);
                }
            }
        for(int side=0;side<8;side++)
        {
            Triangle(centres[0],Ring(0,side+1),Ring(0,side),paint);
            int end=centres.Length-1;Triangle(centres[end],Ring(end,side),Ring(end,side+1),paint);
        }
        st.SetMaterial(new StandardMaterial3D {VertexColorUseAsAlbedo=true,Roughness=.9f});
        return new MeshInstance3D {Mesh=st.Commit()};
    }
}
