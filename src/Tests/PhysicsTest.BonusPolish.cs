using System;
using System.Linq;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;
namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void BonusPolishTest()
    {
        var def=TrackDef.Load("res://tracks/bonus.json");var t=TrackBuilder.Build(def);
        var heights=(float[])t.Heights.Clone();var surfaces=(SurfaceType[])t.Surf.Clone();
        string signature=TrackSignature.Compute(t);
        var terrain=TerrainMesh.Build(t);int rockVertices=0;bool northOnly=true;
        foreach(var patch in new[]{terrain}.Concat(terrain.GetChildren().OfType<MeshInstance3D>().Where(n=>n.Name.ToString().StartsWith("TerrainChunk")))) {
            var a=patch.Mesh.SurfaceGetArrays(0);var v=a[(int)Mesh.ArrayType.Vertex].AsVector3Array();var uv=a[(int)Mesh.ArrayType.TexUV2].AsVector2Array();
            for(int i=0;i<v.Length;i++) if(uv[i].Y>.01f) {rockVertices++;northOnly&=(v[i]+patch.Position).Z<=112;}
        }
        Check(rockVertices>500 && northOnly,"Bonus: rock treatment follows the northern slopes only");
        Check(heights.SequenceEqual(t.Heights)&&surfaces.SequenceEqual(t.Surf)&&signature==TrackSignature.Compute(t),"Bonus: rock rendering leaves ground heights, grip and track signature unchanged");
        Check(!t.Props.Any(p=>p.Type=="hay"&&p.Position.Z<150),"Bonus: no hay bales remain near the landing pads");
        var left=BonusAirbase.Pads[0];int ix=(int)(left.X/t.Tile),iz=(int)(left.Y/t.Tile);
        Check(t.Surf[iz*t.N+ix]==SurfaceType.Concrete && def.Fields.All(f=>left.X<f.X0||left.X>f.X1||left.Y<f.Z0||left.Y>f.Z1),"Bonus: the left pad has concrete underneath and no agricultural field");
        Check(t.Obstacles.Any(o=>o.Center.DistanceTo(BonusAirbase.AbandonedCombine)<.01f&&o.Radius==3)&&BonusAirbase.Pads.All(p=>p.DistanceTo(BonusAirbase.AbandonedCombine)>40),"Bonus: combine and its collider move together away from the landing area");
        int trap=(int)def.SpeedTrap.At;
        // The track now runs the other way round: the GAI waits on the western straight, after the long climb up to it.
        Check(t.Center[trap].X<60 && t.CenterH[trap]-t.CenterH[Math.Max(0,trap-70)]>5,"Bonus: GAI waits after the long western climb");
        var troops=new Troopers();AddChild(troops);troops.Init(t,8);troops.Canisters=true;
        Check(troops.GetChildren().OfType<Node3D>().Count(n=>n.Name.ToString().StartsWith("SupplyTrooper"))==8,"Bonus: all eight roadside walkers use military supply models");
        Check(troops.MakeGolden(new RandomNumberGenerator {Seed=12})&&troops.GoldenOut,"Bonus: military supply troops retain the golden booster canister");
        var other=new Babushkas();AddChild(other);other.Init(TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json")),2);
        Check(!other.GetChildren().OfType<Node3D>().Any(n=>n.Name.ToString().StartsWith("SupplyTrooper")),"Ilyich: civilian babushkas remain unchanged");
        var point=new Vector3(105,t.MeshHeight(105,45),45);var fx=GroundImpactEffects.Create(t,point);AddChild(fx);
        var scar=fx.GetChildren().OfType<MeshInstance3D>().Single();var verts=scar.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        Check(verts.Length>2000&&verts.All(p=>Math.Abs(p.Y+point.Y-t.MeshHeight(point.X+p.X,point.Z+p.Z)-.025f)<.002f),"Blast scar is detailed and follows the real terrain without a floating square");
        for(int i=0;i<140;i++)fx._Process(.05);
        Check(((MultiMeshInstance3D)fx.GetChild(0)).Multimesh.VisibleInstanceCount==0,"Shell flash, earth fountain and dust finish within a bounded particle lifetime");
        Check(heights.SequenceEqual(t.Heights),"Blast detail never changes terrain geometry");
        fx.QueueFree();terrain.Free();troops.QueueFree();other.QueueFree();
    }
}
