using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace CCCPRacing.Track;

/// <summary>Track description as authored in tracks/*.json.</summary>
public class TrackDef
{
    public string Id { get; set; } = "track";
    public string Name { get; set; } = "UNNAMED KOLKHOZ";
    /// <summary>The five-speed rally box fitted for this track; null uses the default Ilyich ratios.</summary>
    public GearboxDef Gearbox { get; set; }
    public int Size { get; set; } = 160;          // tiles per side
    public float TileSize { get; set; } = 2f;     // metres per tile
    public int Seed { get; set; } = 1917;
    /// <summary>Start on this metre of the authored loop, travelling back towards metre zero.</summary>
    public int ReverseFrom { get; set; } = -1;
    public float HillAmp { get; set; } = 5f;
    public float HillScale { get; set; } = 70f;
    /// <summary>Shared rolling meadow profile inside and beyond the driving grid; null keeps the old terrain.</summary>
    public MeadowDef Meadow { get; set; }
    /// <summary>The height of the landscape beyond the map edge (WorldBackdrop): base height and swing of its rolling noise, so the ground around a low track does not climb into a bowl. null: the old 7 m +- 6 m (winter +- 9 m).</summary>
    public BackdropDef Backdrop { get; set; }
    public int Birches { get; set; } = 150;
    /// <summary>The ground off the road: "grass" (the kolkhoz, with mud patches) or "deepsnow" (winter: a snow verge, black ice on the road).</summary>
    public string Ground { get; set; } = "grass";
    /// <summary>"autumn": olive-brown grass strewn with leaves, yellow birches, rusty bushes, straw tufts (on a "grass" ground). Else summer.</summary>
    public string Season { get; set; } = "summer";
    /// <summary>"rain": the rainy autumn's weather round the day (Race.Weather); null: none.</summary>
    public string Weather { get; set; }
    /// <summary>A stand of rust-red pines (ATOMGRAD-4's red forest) round a centre; null: none.</summary>
    public RedForestDef RedForest { get; set; }
    /// <summary>The rally's fuel (tank, cans, golden OCTANE BOOSTER, depot, tow): false on ILYICH'S LAMP, the first track (it comes in from SIBERIA on).</summary>
    public bool Fuel { get; set; } = true;
    /// <summary>Where the fuel depot stands (road metre, side, distance off the centre line); null: 28 m before the line on the left.</summary>
    public DepotDef Depot { get; set; }
    /// <summary>The zone's radiation (ATOMGRAD-4, Race.RaceScene.Dose): background, hot spots, the limit, the wash; null: none.</summary>
    public RadiationDef Radiation { get; set; }
    /// <summary>The zone's life (Track.ZoneLife): the catfish, the horses, the lunokhod, the train; null: none.</summary>
    public ZoneDef Zone { get; set; }
    public List<TrackPoint> Points { get; set; } = new();
    public List<SurfaceSpan> SurfaceSpans { get; set; } = new();
    public MaintenanceHallDef MaintenanceHall {get;set;}
    public List<WidthSpan> WidthSpans { get; set; } = new();
    public RoadCharacterDef RoadCharacter { get; set; } = null;
    public List<PropDef> Props { get; set; } = new();
    public List<FieldDef> Fields { get; set; } = new();
    public List<FeatureDef> Features { get; set; } = new();
    public List<FenceDef> Fences { get; set; } = new();
    public List<PadDef> Pads { get; set; } = new();
    public RunwayDef Runway { get; set; }
    public List<BusStopDef> BusStops { get; set; } = new();
    public TankRangeDef TankRange { get; set; }
    public List<BlightDef> Blights { get; set; } = new();
    /// <summary>Large-scale landforms added on top of the noise hills.</summary>
    public List<HillDef> Hills { get; set; } = new();
    public List<PlateauDef> Plateaus { get; set; } = new();
    /// <summary>Height the map edge rises to (hides the end of the world); null: 16 m above the noise.</summary>
    public float? EdgeRim { get; set; }
    /// <summary>The rim the ROAD's heights are graded with (null: EdgeRim). Lowering EdgeRim where the road runs within the edge margin would change the road's heights and with them the track signature (the records); set this to the old rim to keep both and let only the ground around the road follow the new rim.</summary>
    public float? RoadEdgeRim { get; set; }
    /// <summary>How many bushes to scatter over the grass.</summary>
    public int Bushes { get; set; } = 350;
    /// <summary>How many grass tufts to scatter.</summary>
    public int Tufts { get; set; } = 900;
    /// <summary>How many babushkas walk the verges.</summary>
    public int Babushkas { get; set; } = 8;
    /// <summary>How strongly the road ignores the hills (box-blur radius in metres, 3 passes).</summary>
    public int RoadSmoothing { get; set; } = 7;
    /// <summary>The plan: a lap under this many seconds within the lap budget clears the track.</summary>
    public float TargetLap { get; set; } = 50f;
    /// <summary>Ponds and a river with its ford (TrackBuilder.CarveWater); null: a dry track.</summary>
    public WaterDef Water { get; set; }
    /// <summary>The GAI speed trap at the end of the longest straight (Race: the speed records); null: none.</summary>
    public SpeedTrapDef SpeedTrap { get; set; }
    /// <summary>Ploughed snow banks along both sides of a winter road (null: none).</summary>
    public SnowbankDef Snowbanks { get; set; }
    /// <summary>The labour camp the road runs through, metres along the road (null: none).</summary>
    public GulagDef Gulag { get; set; }
    /// <summary>Stacks of logs by the road: metres along it, side ("left"/"right" of the driving direction), how far out (0: just behind the bank).</summary>
    public List<LogPileDef> LogPiles { get; set; } = new();
    /// <summary>Snowploughs round the lap: start metre, and whether it runs against the race.</summary>
    public List<PloughDef> Ploughs { get; set; } = new();
    /// <summary>A labour detail marching round the lap from the camp (null: none).</summary>
    public ColumnDef Column { get; set; }
    /// <summary>The track's norm ladder and medals (Race.NormPlan.UseTrack); null: the bonus track's default ladder.</summary>
    public NormsDef Norms { get; set; }
    /// <summary>A drive-through barn over the start/finish line instead of the banner gate; null: the gate.</summary>
    public StartBarnDef StartBarn { get; set; }

    public static TrackDef Load(string resPath)
    {
        string json = FileAccess.GetFileAsString(resPath);
        return JsonSerializer.Deserialize<TrackDef>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
    }
}

/// <summary>Centre-line control point. Values apply to the segment starting here.</summary>
/// <summary>Repaints the road surface between two centre-line distances (metres), without
/// touching the road's shape: e.g. to shorten a mud stretch that comes from the control points.</summary>
public class SurfaceSpan
{
    public float From { get; set; }
    public float To { get; set; }
    public string Surface { get; set; } = "gravel";
}

/// <summary>Short passing place with smooth entry and exit tapers, in authored road metres.</summary>
public class WidthSpan
{
    public float From { get; set; }
    public float To { get; set; }
    public float Width { get; set; } = 11.5f;
    public float Taper { get; set; } = 18f;
}

public class TrackPoint
{
    public float X { get; set; }
    public float Z { get; set; }
    public float Width { get; set; } = 8f;
    public string Surface { get; set; } = "concrete";
    public float Lift { get; set; }               // extra road height in metres (crests, jumps)
}

public class PropDef
{
    public string Type { get; set; } = "birch";
    public float X { get; set; }
    public float Z { get; set; }
    public float Rot { get; set; }                // degrees
    public string Text { get; set; }
}

/// <summary>
/// A shaped bump in the road at a distance along the centre line (metres from the start line).
/// crest: rounded hump of Height over Length. dip: the same downwards.
/// jump: kicker rising Height over Length (steepening to the lip), then falling back over Drop.
/// level: from At for Length metres the road is levelled to its average height (runways).
/// </summary>
public class FeatureDef
{
    public string Type { get; set; } = "crest";
    public float At { get; set; }
    public float Length { get; set; } = 25f;
    public float Height { get; set; } = 1f;
    public float Drop { get; set; } = 6f;
    /// <summary>Carry this road feature into surrounding terrain, fading out at this distance from the centreline.</summary>
    public float TerrainWidth { get; set; }
}

/// <summary>
/// A fence along the road from/to a distance (metres from the start line), beside the ditch.
/// Side is relative to the direction of travel: "left" or "right". Type: "wood" or "stone".
/// </summary>
public class FenceDef
{
    public string Type { get; set; } = "wood";
    public float From { get; set; }
    public float To { get; set; }
    public string Side { get; set; } = "right";
    /// <summary>Distance beyond the road edge (the ditch ends ~5 m out).</summary>
    public float Offset { get; set; } = 5.6f;
    /// <summary>Optional stretch [NearFrom, NearTo] (metres) at NearOffset instead, eased over NearEase metres either side.</summary>
    public float NearFrom { get; set; }
    public float NearTo { get; set; }
    public float NearOffset { get; set; }
    public float NearEase { get; set; } = 8f;

    /// <summary>The offset at road metre m of this fence.</summary>
    public float OffsetAt(float m)
    {
        if (NearTo <= NearFrom) return Offset;
        float into = Mathf.Min(m - (NearFrom - NearEase), (NearTo + NearEase) - m) / NearEase;
        return Mathf.Lerp(Offset, NearOffset, Mathf.Clamp(into, 0f, 1f));
    }
}

/// <summary>Area where the tanks exercise (world rectangle), how many there are and their firing
/// positions [x, z]: side A inside the road's bend, side B across the road from it.</summary>
public class TankRangeDef
{
    public float X0 { get; set; }
    public float Z0 { get; set; }
    public float X1 { get; set; }
    public float Z1 { get; set; }
    public int Tanks { get; set; } = 2;
    public float[][] PositionsA { get; set; }
    public float[][] PositionsB { get; set; }
}

/// <summary>A bus stop at a road distance, on the given side of the direction of travel.</summary>
public class BusStopDef
{
    public float At { get; set; }
    public string Side { get; set; } = "left";
    public int Drunks { get; set; } = 3;
}

/// <summary>Highway strip markings between two road distances; Numbers = designators at From/To ends.</summary>
public class RunwayDef
{
    public float From { get; set; }
    public float To { get; set; }
    public string[] Numbers { get; set; }
    /// <summary>Optional aircraft takeoff endpoints, authored separately so circuit reversal does not reverse takeoff.</summary>
    public float? TakeoffFrom { get; set; }
    public float? TakeoffTo { get; set; }
    /// <summary>The jets taxi to a holding point beside the strip and wait there while a snowplough is on it or this many metres (or less) of road from either end; 0: no holding point.</summary>
    public float ClearBehind { get; set; }
}

/// <summary>A flattened rectangle (airfield apron, hangar floor) with its own surface.
/// Height defaults to the road height nearest the pad's centre.</summary>
public class PadDef
{
    public float X0 { get; set; }
    public float Z0 { get; set; }
    public float X1 { get; set; }
    public float Z1 { get; set; }
    public string Surface { get; set; } = "runway";
    public float? Height { get; set; }
    /// <summary>Flatten to the ground height at the pad's centre instead of the road height.</summary>
    public bool Ground { get; set; }
    /// <summary>Pave through the roadside verge up to the road edge, preserving the racing surface itself.</summary>
    public bool ConnectRoad { get; set; }
}

/// <summary>Circle of dead vegetation (around the radar): withered ground, dead trees and bushes.</summary>
public class BlightDef
{
    public float X { get; set; }
    public float Z { get; set; }
    public float R { get; set; } = 20f;
}

/// <summary>Rectangular off-road area with its own surface (ploughed field, mud pit).</summary>
public class FieldDef
{
    public string Surface { get; set; } = "field";
    public float X0 { get; set; }
    public float Z0 { get; set; }
    public float X1 { get; set; }
    public float Z1 { get; set; }
}

/// <summary>Snow banks: crest height above the road (m), gap from the road's edge to the inner foot (m), open stretches.</summary>
public class SnowbankDef
{
    public float Height { get; set; } = 1.2f;
    public float Gap { get; set; } = 0.6f;
    public List<SnowbankGap> Except { get; set; } = new();
    /// <summary>Stretches where the plough pushed the bank further out (By metres more gap), eased in and out.</summary>
    public List<SnowbankGap> Widen { get; set; } = new();
}

public class ColumnDef
{
    public float Start { get; set; }
    public int Prisoners { get; set; } = 8;
}

public class PloughDef
{
    public float Start { get; set; }
    public bool Against { get; set; }
    /// <summary>"plough" (a snowplough) or "mixer" (ATOMGRAD-4's concrete mixers, bound for the sarcophagus).</summary>
    public string Kind { get; set; } = "plough";
}

public class LogPileDef
{
    public float At { get; set; }
    public string Side { get; set; } = "right";
    public float Out { get; set; }
}

/// <summary>The camp's stretch of road: a gate at From and at To (metres along the road).</summary>
public class GulagDef
{
    public float From { get; set; }
    public float To { get; set; }
}

/// <summary>No bank from..to (metres along the road) on "left", "right" or "both" sides.</summary>
public class SnowbankGap
{
    public float From { get; set; }
    public float To { get; set; }
    public string Side { get; set; } = "both";
    /// <summary>Widen only: how much further out the bank stands (m).</summary>
    public float By { get; set; } = 3f;
}

/// <summary>The GAI post: road metre of the radar, which side of the road, the posted limit (km/h).</summary>
public class SpeedTrapDef
{
    public float At { get; set; }
    public string Side { get; set; } = "right";
    public float Limit { get; set; } = 90f;
}

/// <summary>The rally's norms on a track: the ladder (seconds, the last one is the plan) and how
/// many norms beaten earn a bronze and a silver medal (gold: the whole plan).</summary>
public class NormsDef
{
    public float[] Ladder { get; set; }
    public int Bronze { get; set; } = 4;
    public int Silver { get; set; } = 7;
    /// <summary>Lap-time factors of the DIESEL and JET FUEL rallies against the petrol ladder (0: the default factor);
    /// measured with the autopilot at 50 and 200 % power (Race.FuelGrades).</summary>
    public float Diesel { get; set; }
    public float Rocket { get; set; }
    /// <summary>Lap-time factor of the SPUTNIK rally (0: the default factor).</summary>
    public float Sputnik { get; set; }
    /// <summary>The track's climate allowance against the first track's (0: 1): the allowances follow the lap time (Kolkhoz night +2 s, dusk +1 s at 1).</summary>
    public float Climate { get; set; }
    /// <summary>A fuel's own ladder (seconds, as many steps as Ladder) instead of the factor's: set when its norms are tuned by hand.</summary>
    public float[] DieselLadder { get; set; }
    public float[] RocketLadder { get; set; }
    public float[] SputnikLadder { get; set; }
}

/// <summary>The start/finish barn (Village.StartBarn): its length along the road, how far the line is
/// from its entrance, and the room between the road's edge and each wall.</summary>
public class StartBarnDef
{
    public float Length { get; set; } = 22f;
    public float Before { get; set; } = 17f;
    public float Room { get; set; } = 3.2f;
}

/// <summary>
/// Ponds and rivers at one water level: the road's height where the river crosses it (the ford).
/// A river that runs from edge to edge crosses a closed lap an even number of times, so a ford
/// river should start in a pond inside the lap. Depths are below the water line.
/// </summary>
public class WaterDef
{
    public List<PondDef> Ponds { get; set; } = new();
    public List<RiverDef> Rivers { get; set; } = new();
    /// <summary>Ford depth round the outside of the road's bend (the gravel bar, longer) and on the inside (the short, deep line).</summary>
    public float FordShallow { get; set; } = 0.1f;
    public float FordDeep { get; set; } = 0.5f;
    /// <summary>Winter: the rivers are frozen (TrackBuilder.CarveFrozen): glare ice sunk between banks, falling downstream, the road on it where it runs on it. No ponds.</summary>
    public bool Frozen { get; set; }
    /// <summary>Broad winter river valley below the surrounding terrain; zero preserves the original river grading.</summary>
    public float ValleyDepth { get; set; }
    /// <summary>Horizontal run of each valley bank beyond the ice edge, in metres.</summary>
    public float ValleyBankWidth { get; set; } = 28f;
}

public class PondDef
{
    public float X { get; set; }
    public float Z { get; set; }
    public float R { get; set; } = 15f;
    public float Depth { get; set; } = 1.2f;
}

public class RiverDef
{
    /// <summary>Centre line, [x, z] pairs, or [x, z, width] where the river is wider or narrower than Width there
    /// (the width runs smoothly from point to point). Optional fourth value is independent
    /// bank variation in metres; optional fifth value insets only the positive-cross bank
    /// of the authored 2D centre line. Omitted values retain the original banks.</summary>
    public float[][] Points { get; set; }
    public float Width { get; set; } = 10f;
    public float Depth { get; set; } = 1f;

    /// <summary>The width at point i.</summary>
    public float WidthAt(int i) => Points[i].Length > 2 ? Points[i][2] : Width;
    float BankVariationAt(int i) => Points[i].Length > 3 ? System.Math.Max(0, Points[i][3]) : 0;
    float PositiveBankInsetAt(int i) => Points[i].Length > 4 ? System.Math.Max(0, Points[i][4]) : 0;
    /// <summary>Half the width u (0..1) of the way along segment i.</summary>
    public float HalfWidth(int i, float u) => Godot.Mathf.Lerp(WidthAt(i), WidthAt(i + 1), u) * 0.5f;
    /// <summary>The width at the first (end 0) or last (end 1) point.</summary>
    public float EndWidth(int end) => WidthAt(end == 0 ? 0 : Points.Length - 1);
    /// <summary>The widest point (bounds).</summary>
    public float MaxWidth
    {
        get { float w = 0; for (int i = 0; i < Points.Length; i++) w = System.Math.Max(w, WidthAt(i) + 2 * BankVariationAt(i)); return w; }
    }

    /// <summary>Signed distance from p to the river's edge (negative inside), the width as it is along the line.</summary>
    public float EdgeDistance(Godot.Vector2 p)
    {
        float best = float.MaxValue, variation = 0, side = 0, bankInset = 0;
        for (int i = 0; i + 1 < Points.Length; i++)
        {
            var a = new Godot.Vector2(Points[i][0], Points[i][1]);
            var b = new Godot.Vector2(Points[i + 1][0], Points[i + 1][1]);
            Godot.Vector2 ab = b - a;
            float len2 = ab.LengthSquared();
            if (len2 < 0.0001f) continue;
            float u = System.Math.Clamp((p - a).Dot(ab) / len2, 0f, 1f);
            float distance = p.DistanceTo(a + ab * u) - HalfWidth(i, u);
            if (distance >= best) continue;
            best = distance;
            variation = Godot.Mathf.Lerp(BankVariationAt(i), BankVariationAt(i + 1), u);
            side = ab.Cross(p - a) >= 0 ? 0 : 2.3f;
            bankInset = Godot.Mathf.Lerp(PositiveBankInsetAt(i), PositiveBankInsetAt(i + 1), u);
        }
        if (variation > 0)
        {
            float station = (p.X + p.Y) * .7071068f;
            // Different phases/scales on each bank: one bank need not straighten
            // merely because the opposite side bends or the authored width changes.
            float wave = .65f * System.MathF.Sin(station / 2.7f + side)
                + .35f * System.MathF.Sin(station / 5.3f + side * 1.7f);
            best -= variation * wave;
        }
        if (side == 0) best += bankInset;
        return best;
    }
}

/// <summary>A level district with softly blended slopes outside its rectangular bounds.</summary>
public class PlateauDef
{
    public float X0 { get; set; }
    public float Z0 { get; set; }
    public float X1 { get; set; }
    public float Z1 { get; set; }
    public float Height { get; set; }
    public float Blend { get; set; } = 30f;
}

/// <summary>
/// A broad landform on top of the noise: a round hill around (X, Z), or a ridge along the segment
/// (X, Z)-(X1, Z1). Height at the crest, smoothly falling to nothing Radius metres away.
/// </summary>
public class HillDef
{
    public float X { get; set; }
    public float Z { get; set; }
    public float? X1 { get; set; }
    public float? Z1 { get; set; }
    public float Height { get; set; } = 10f;
    public float Radius { get; set; } = 80f;
    /// <summary>Falloff on the side facing away from the map centre (the ridge's back); null: Radius.</summary>
    public float? BackRadius { get; set; }
    /// <summary>0..1: how much the noise hills are smoothed away under this one (fully at the crest).</summary>
    public float Calm { get; set; }
}

/// <summary>The red forest: Count rust-red pines within R metres of (X, Z), never on the road.</summary>
public class RedForestDef
{
    public float X { get; set; }
    public float Z { get; set; }
    public float R { get; set; } = 35f;
    public int Count { get; set; } = 60;
}

/// <summary>
/// The zone's radiation: a background dose rate (mSv/s) everywhere, hot spots that add their Rate
/// at the middle, falling to nothing at R, OffRoad times as much off the hard road (the dust and the
/// mud hold it), the limit (mSv) the dosimetrist stops a car at, the wash station's bay (X, Z, along
/// its yaw) where a car is washed clean.
/// </summary>
/// <summary>The fuel depot's place: its stopping zone off road metre At, on Side ("left" or "right" of the race direction), Off metres from the centre line.</summary>
public class DepotDef
{
    public int At { get; set; }
    public string Side { get; set; } = "left";
    public float Off { get; set; } = 10.5f;
}

public class RadiationDef
{
    public float Background { get; set; } = 0.02f;
    public float OffRoad { get; set; } = 3f;
    public float Limit { get; set; } = 20f;
    public List<HotSpotDef> HotSpots { get; set; } = new();
    public float WashX { get; set; }
    public float WashZ { get; set; }
}

public class HotSpotDef
{
    public float X { get; set; }
    public float Z { get; set; }
    public float R { get; set; } = 30f;
    public float Rate { get; set; } = 0.5f;
}

/// <summary>ATOMGRAD-4's life (Track.ZoneLife); any part may be left out.</summary>
public class ZoneDef
{
    public CatfishDef Catfish { get; set; }
    public HorsesDef Horses { get; set; }
    public RobotDef Robot { get; set; }
    public TrainDef Train { get; set; }
}

/// <summary>The catfish jumps out of the channel between X0 and X1 at Z.</summary>
public class CatfishDef { public float X0 { get; set; } public float X1 { get; set; } public float Z { get; set; } }
/// <summary>Count horses grazing round (X, Z).</summary>
public class HorsesDef { public float X { get; set; } public float Z { get; set; } public int Count { get; set; } = 4; }
/// <summary>The lunokhod crosses back and forth between (X0, Z0) and (X1, Z1).</summary>
public class RobotDef { public float X0 { get; set; } public float Z0 { get; set; } public float X1 { get; set; } public float Z1 { get; set; } }
/// <summary>The train on the rails along Z at X: from South (its nose, off the map's south edge) up to End and back; Cross: the level crossing's Z.</summary>
public class TrainDef { public float X { get; set; } public float End { get; set; } public float Cross { get; set; } public float South { get; set; } = 340f; }

public class MaintenanceHallDef { public float X {get;set;} public float Z {get;set;} public float Width {get;set;}=18; public float Length {get;set;}=26; }

/// <summary>Rolling height of the landscape beyond the map edge: BaseHeight + Amplitude * noise (noise about -1..1). Set it near the track's own ground height plus the edge rim.</summary>
public class BackdropDef
{
    public float BaseHeight { get; set; } = 7;
    public float Amplitude { get; set; } = 6;
}

/// <summary>Broad low-frequency meadow rises, shared by playable ground and backdrop.</summary>
public class MeadowDef
{
    public float BaseHeight {get;set;}=4;
    public float Amplitude {get;set;}=5;
    public float Scale {get;set;}=55;
    public int Seed {get;set;}=1922;
    public float HeightAt(float x,float z)
    {
        float scale=System.Math.Max(16,Scale);
        return BaseHeight+Amplitude*(.75f*Noise.Value(x/scale,z/scale,Seed+71)
            +.25f*Noise.Value(x/(scale*1.9f),z/(scale*1.9f),Seed+181));
    }
}

/// <summary>Optional worn road edges and broad, authored narrowing zones; distances follow the centreline.</summary>
public class RoadCharacterDef
{
    public float From { get; set; }
    public float To { get; set; }
    public float EdgeInset { get; set; } = .45f;
    public int Seed { get; set; } = 1922;
    public List<RoadPinchDef> Pinches { get; set; } = new();
}
public class RoadPinchDef
{
    public float From { get; set; }
    public float To { get; set; }
    public float Reduction { get; set; } = .16f;
    public float Taper { get; set; } = 18;
}
