using System;
using System.Linq;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void PartyDachaTest()
    {
        GD.Print("Party dacha: road access, dry buildings, a rowing official confined to the pond");
        var t = TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"));
        int dacha = t.Props.FindIndex(p => p.Type == "partydacha");
        Check(dacha >= 0 && t.PowerAnchors.ContainsKey(dacha), "the log palace has a connected electricity service");
        bool levelGrounds = true;
        foreach (var point in new[] { PartyDacha.Site, PartyDacha.Gate, PartyDacha.Car, PartyDacha.FinnishCar,
            new Vector2(202, 164), new Vector2(220, 186), new Vector2(207, 170), new Vector2(233, 187) })
            levelGrounds &= Math.Abs(t.MeshHeight(point.X, point.Y) - t.WaterLevel) < 0.02f;
        Check(levelGrounds, "the building, courtyard, gate and dry bank share the pond's water level");
        Check(dacha >= 0 && Math.Abs(t.Props[dacha].Position.Y - t.WaterLevel) < 0.02f,
            "the dacha foundation sits on the lakeside ground without an elevated basement");
        bool clear = true, gravel = true;
        foreach (var p in PartyDacha.Drive)
        {
            foreach (var o in t.Obstacles)
                if (o.Center.DistanceTo(p) < o.Radius + 1.2f)
                { GD.Print($"  driveway blocked at {p} by {o.Center}, r={o.Radius:F2}"); clear = false; }
        }
        for (int i = 0; i + 1 < PartyDacha.Drive.Length; i++)
            for (float f = 0; f <= 1; f += 0.1f)
            {
                var p = PartyDacha.Drive[i].Lerp(PartyDacha.Drive[i + 1], f);
                gravel &= t.Sample(p.X, p.Y).Surface == SurfaceType.Gravel;
                foreach (var o in t.Obstacles)
                    clear &= o.Center.DistanceTo(p) > o.Radius + 1.2f;
            }
        int road = t.NearestCenter(PartyDacha.Drive[0], out float d);
        Check(clear && gravel && d < t.HalfWidth[road], "an unblocked gravel driveway reaches the actual race road through the gate");
        bool dry = true;
        foreach (var solid in PartyDacha.SolidFootprints())
            dry &= t.Sample(solid.At.X, solid.At.Y).Surface is not (SurfaceType.Water or SurfaceType.Shallow);
        Check(dry, "palace, car, gateposts and fences stand on dry land");
        bool afloat = true;
        for (int second = 0; second < 200; second++)
        {
            var p = PartyRowboat.PositionAt(second);
            afloat &= p.DistanceTo(PartyRowboat.PondCentre) + 2.95f * PartyRowboat.FigureScale < 18f;
            afloat &= t.Sample(p.X, p.Y).Surface == SurfaceType.Water;
        }
        Check(afloat, "the boat and its oars remain in the middle of the pond throughout the circuit");
        bool duckClear = true;
        for (int second = 0; second < 280; second++)
        {
            var duck = PartyPondDuck.PositionAt(second);
            duckClear &= duck.DistanceTo(PartyRowboat.PondCentre) + PartyPondDuck.Radius < 18f;
            duckClear &= duck.DistanceTo(PartyRowboat.PositionAt(second)) > 6.8f;
            duckClear &= t.Sample(duck.X, duck.Y).Surface == SurfaceType.Water;
        }
        Check(duckClear, "the mallard stays in water, clear of the rowing boat and north-shore pier");
        bool misses = true, waterOnly = true, clearBoat = true;
        for (int second = 0; second < 1400; second++)
            for (int side = 0; side < 2; side++)
            {
                var impact = PartyRowboat.MissAt(second, side);
                // Allow another metre beyond the actual duck model for the pellet splash.
                misses &= impact.DistanceTo(PartyPondDuck.PositionAt(second)) > PartyPondDuck.Radius + 1f;
                waterOnly &= t.Sample(impact.X, impact.Y).Surface == SurfaceType.Water;
                clearBoat &= impact.DistanceTo(PartyRowboat.PositionAt(second)) > 4.5f;
            }
        Check(misses && waterOnly && clearBoat, "drunken shots always miss the mallard and boat, splashing in the pond throughout both orbits");
        bool audible = true;
        foreach (var point in t.Center)
            audible &= point.DistanceTo(PartyRowboat.PondCentre) + 5f < CCCPRacing.Audio.ShotgunSound.Reach * 0.65f;
        Check(audible, "the shotgun report carries to the whole first track, within its distance fade");
        bool privateGrounds = true;
        foreach (var p in t.Props)
            if (p.Type is "birch" or "pine" or "spruce")
                privateGrounds &= !PartyDacha.Reserved(new Vector2(p.Position.X, p.Position.Z));
        Check(privateGrounds, "random trees do not grow through the estate or its driveway");
        var buildingOrigin=new Vector3(PartyDacha.Site.X,t.WaterLevel,PartyDacha.Site.Y);
        var building = PartyDacha.Building(t,buildingOrigin);
        var mesh=building.GetChildren().OfType<MeshInstance3D>().Single();
        var vertices=Enumerable.Range(0,mesh.Mesh.GetSurfaceCount())
            .SelectMany(i=>mesh.Mesh.SurfaceGetArrays(i)[(int)Mesh.ArrayType.Vertex].AsVector3Array()).ToArray();
        Check(vertices.Any(p=>Math.Abs(p.X)<.9f && p.Y>5.65f && p.Y<6.55f && p.Z>6.4f),
            "upper balcony has the matching glazed double doorway");
        bool clearBalcony=true,footings=true;
        foreach(float x in new[] {-9.3f,-5.7f,5.7f,9.3f})
            clearBalcony &= !vertices.Any(p=>Math.Abs(p.X-x)<.3f && Math.Abs(p.Z-9.15f)<.3f && p.Y>4.52f && p.Y<5.8f);
        Check(clearBalcony,"balcony has no free-standing outer pillar stumps or floating caps");
        var supports=new[] {new Vector2(-9.3f,9.15f),new Vector2(-5.7f,9.15f),new Vector2(-2.15f,9.15f),
            new Vector2(2.15f,9.15f),new Vector2(5.7f,9.15f),new Vector2(9.3f,9.15f),new Vector2(-8.8f,6.4f),new Vector2(8.8f,6.4f)};
        foreach(var support in supports)
        {
            var corner=new Vector2(support.X-.34f,support.Y-.34f);
            float ground=t.MeshHeight(buildingOrigin.X+corner.X,buildingOrigin.Z+corner.Y)-buildingOrigin.Y;
            footings &= vertices.Any(p=>Math.Abs(p.X-corner.X)<.001f && Math.Abs(p.Z-corner.Y)<.001f && p.Y<=ground-.075f);
            footings &= vertices.Any(p=>Math.Abs(p.X-corner.X)<.001f && Math.Abs(p.Z-corner.Y)<.001f && Math.Abs(p.Y-.885f)<.001f);
        }
        Check(footings,"eight stone footings extend into actual ground and meet the terrace underside");
        var boat = PartyRowboat.Build(t);
        var grounds = PartyDacha.Grounds(t);
        Check(building.GetChildCount() == 2 && grounds.GetChildCount() == 2, "palace and estate details use batched meshes");
        var lighting = building.GetNode<Node3D>("DachaLighting");
        Check(!lighting.GetChildren().OfType<MeshInstance3D>().Any(m=>m.Position.Y>6.6f && m.Position.Z>8),
            "upper lamps are wall-mounted rather than floating over the balcony railing");
        int lamps = 0, windows = 0;
        foreach (var child in lighting.GetChildren())
        {
            if (child is Light3D l && l.HasMeta("night_energy")) lamps++;
            if (child is MeshInstance3D m && m.MaterialOverride is StandardMaterial3D mat && mat.EmissionEnabled) windows++;
        }
        Check(lamps >= 5 && windows >= 24, "dacha lighting survives mesh batching with night-controlled lamps and glowing windows");
        building.Free(); boat.Free(); grounds.Free();
    }
}
