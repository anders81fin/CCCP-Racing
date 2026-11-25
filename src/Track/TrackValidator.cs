using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Track;

/// <summary>One finding about a track file: where it is (a JSON path such as "props[3].type") and what is wrong.</summary>
public sealed class TrackIssue
{
    public string Path, Message;
    public bool Error;
    public override string ToString() => $"{(Error ? "ERROR" : "WARNING")} {Path}: {Message}";
}

/// <summary>What the validator found and measured. Ok means no errors (warnings alone do not stop a track).</summary>
public sealed class TrackReport
{
    public readonly List<TrackIssue> Issues = new();
    public string Name = "", Signature = "";
    public int Points, Props;
    public float RoadLength;           // metres: the built road's length (from the build), else the control polygon's estimate
    public double BuildSeconds;
    public bool Built;
    public bool Ok => !Issues.Any(i => i.Error);
    public void Error(string path, string message) => Issues.Add(new TrackIssue { Path = path, Message = message, Error = true });
    public void Warn(string path, string message) => Issues.Add(new TrackIssue { Path = path, Message = message, Error = false });
    public string FirstError() => Issues.FirstOrDefault(i => i.Error)?.Message ?? "";
}

/// <summary>
/// Checks a custom track's JSON before the game builds it: it must parse, use only the fields and the building types a custom track may use, stay inside the map and inside the
/// size and count budgets (the loading time and the Steam Deck's memory), and build. Written for an AI agent to read: every finding names the JSON path, says what is wrong and,
/// for a misspelt field or type, what was probably meant. TrackSource.Scan runs it without the build (cheap, for the menu); the command line (--validate-track) runs it with the build.
/// </summary>
public static class TrackValidator
{
    public const int MaxBytes = 512 * 1024, MinPoints = 6, MaxPoints = 400, MaxProps = 1500, MaxFences = 60, MaxFeatures = 80, MaxNameLength = 24, MaxTextLength = 40;
    public const int MinSize = 64, MaxSize = 256;
    const float Margin = 14f;      // metres from the map's edge the road and the props keep

    /// <summary>The building and scenery types a custom track may place (TrackDef.Props type). The campaign's one-off set pieces (the reactor, the airbase, the dacha, the camp) need
    /// their own track's data and are not offered.</summary>
    public static readonly string[] AllowedProps =
    {
        "birch", "spruce", "pine", "deadtree", "rock", "stump", "hay", "izba", "barn", "bigbarn", "silo", "church", "well", "statue", "billboard", "pole", "streetlamp",
        "watertower", "henhouse", "stonepost", "flagpole", "busstop", "ruinbarn", "lorrywreck", "windsock", "tractor", "garden", "wheatfield",
    };
    public static readonly string[] FeatureTypes = { "crest", "dip", "jump" };
    public static readonly string[] FenceTypes = { "wood", "stone" };

    /// <summary>The top-level fields a custom track may set (camelCase, as in the file). The campaign tracks use more (water, snowbanks, the camp, the radiation, the airbase, the norms, the
    /// gearbox...): each needs its own code, scenery or rules, so it is not offered to a custom track (yet). A list, not a ban list: a field added to TrackDef later stays closed until it is
    /// tested and listed here.</summary>
    public static readonly string[] AllowedFields =
    {
        "name", "size", "tileSize", "seed", "hillAmp", "hillScale", "meadow", "birches", "bushes", "tufts", "babushkas", "ground", "season", "roadSmoothing", "edgeRim",
        "points", "surfaceSpans", "widthSpans", "roadCharacter", "props", "features", "fences", "fields", "paths", "smooth", "belts", "scatter", "hills", "plateaus",
    };

    public static TrackReport Validate(string json, bool build = true)
    {
        var r = new TrackReport();
        if (json == null) { r.Error("", "the file could not be read"); return r; }
        if (json.Length > MaxBytes) { r.Error("", $"the file is {json.Length / 1024} kB: at most {MaxBytes / 1024} kB"); return r; }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
        catch (JsonException e) { r.Error("", $"not valid JSON at line {e.LineNumber + 1}, column {e.BytePositionInLine + 1}: {e.Message.Split(" LineNumber:")[0].Trim()}"); return r; }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object) { r.Error("", "the file must be one JSON object { ... }"); return r; }
            Walk(doc.RootElement, typeof(TrackDef), "", r);
            foreach (var p in doc.RootElement.EnumerateObject())
                if (!string.Equals(p.Name, "id", StringComparison.OrdinalIgnoreCase) && typeof(TrackDef).GetProperty(p.Name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase) != null
                        && !AllowedFields.Contains(Camel(p.Name)) && !AllowedFields.Any(f => string.Equals(f, p.Name, StringComparison.OrdinalIgnoreCase))
                        && p.Value.ValueKind != JsonValueKind.Null && !(p.Value.ValueKind == JsonValueKind.Array && p.Value.GetArrayLength() == 0))
                    r.Error(p.Name, "not available in custom tracks (only the campaign tracks use it): remove it");
        }
        TrackDef def;
        try { def = TrackDef.Parse(json); }
        catch (JsonException e) { r.Error(e.Path ?? "", "a value has the wrong type or form: " + e.Message); return r; }
        catch (Exception e) { r.Error("", $"the file could not be read ({e.GetType().Name}: {e.Message})"); return r; }
        if (def == null) { r.Error("", "the file is empty"); return r; }
        r.Name = (def.Name ?? "").Trim();
        if (NullShapes(def, r)) return r;
        try { Check(def, r); }
        catch (Exception e) { r.Error("", $"the file could not be checked, a value is missing or of the wrong form ({e.GetType().Name}: {e.Message})"); return r; }
        if (build && r.Ok) Build(json, r);
        return r;
    }

    /// <summary>JSON null where a list or an item belongs ("props": null, "points": [null]) is refused: the checks and the builder expect lists of objects. True when something was found.</summary>
    static bool NullShapes(TrackDef d, TrackReport r)
    {
        bool found = false;
        foreach (var p in typeof(TrackDef).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (!p.PropertyType.IsGenericType || p.PropertyType.GetGenericTypeDefinition() != typeof(List<>) || !p.CanRead) continue;
            string name = Camel(p.Name);
            if (p.GetValue(d) is not System.Collections.IList list) { r.Error(name, "must be a list [ ... ], not null"); found = true; continue; }
            for (int i = 0; i < list.Count; i++)
                if (list[i] == null) { r.Error($"{name}[{i}]", "must be an object, not null"); found = true; }
        }
        return found;
    }

    /// <summary>Fields in the JSON that TrackDef does not have (a typo is otherwise silently ignored), found by walking the JSON beside TrackDef's own properties.</summary>
    static void Walk(JsonElement e, Type t, string path, TrackReport r)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        if (e.ValueKind == JsonValueKind.Object && t.IsClass && t != typeof(string) && !typeof(System.Collections.IDictionary).IsAssignableFrom(t))
        {
            var props = t.GetProperties().Where(p => p.CanWrite).ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in e.EnumerateObject())
            {
                string at = path == "" ? kv.Name : path + "." + kv.Name;
                if (!props.TryGetValue(kv.Name, out var p)) { r.Error(at, "unknown field" + DidYouMean(kv.Name, props.Keys.Select(Camel))); continue; }
                Walk(kv.Value, p.PropertyType, at, r);
            }
        }
        else if (e.ValueKind == JsonValueKind.Array && t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
        {
            int i = 0;
            foreach (var x in e.EnumerateArray()) Walk(x, t.GetGenericArguments()[0], $"{path}[{i++}]", r);
        }
    }

    static string Camel(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];

    /// <summary>" (did you mean 'x'?)" for the closest known name, else "".</summary>
    public static string DidYouMean(string wrong, IEnumerable<string> known)
    {
        string best = null; int bestD = int.MaxValue;
        foreach (string k in known)
        {
            int d = Distance(wrong.ToLowerInvariant(), k.ToLowerInvariant());
            if (d < bestD) { bestD = d; best = k; }
        }
        return best != null && bestD <= Math.Max(2, wrong.Length / 3) ? $" (did you mean '{best}'?)" : "";
    }

    static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

    static string SurfaceNames => string.Join(", ", Surfaces.Info.Select(i => i.Name));
    static bool KnownSurface(string s) => s != null && Surfaces.Info.Any(i => i.Name == s);

    static void Range(TrackReport r, string path, float v, float lo, float hi)
    {
        if (!Finite(v) || v < lo || v > hi) r.Error(path, $"{v.ToString(CultureInfo.InvariantCulture)} is outside {lo.ToString(CultureInfo.InvariantCulture)} to {hi.ToString(CultureInfo.InvariantCulture)}");
    }

    static void Check(TrackDef d, TrackReport r)
    {
        // ---- name: drawn on the track card in the game's pixel font
        if (string.IsNullOrWhiteSpace(d.Name) || d.Name == "UNNAMED KOLKHOZ") r.Error("name", "give the track a name (at most " + MaxNameLength + " characters)");
        else
        {
            if (d.Name.Length > MaxNameLength) r.Error("name", $"{d.Name.Length} characters: at most {MaxNameLength}");
            foreach (char c in d.Name) if (!PixelFont.CanDraw(c)) { r.Error("name", $"the character '{c}' cannot be drawn: use letters, digits, space and . , : ! ? - + = / % \" ( ) < > # *"); break; }
        }
        if (d.Size < MinSize || d.Size > MaxSize) r.Error("size", $"{d.Size} tiles: between {MinSize} and {MaxSize}");
        Range(r, "tileSize", d.TileSize, 1f, 4f);
        Range(r, "hillAmp", d.HillAmp, 0f, 30f);
        Range(r, "hillScale", d.HillScale, 20f, 300f);
        if (d.Birches < 0 || d.Birches > 400) r.Error("birches", $"{d.Birches}: between 0 and 400");
        if (d.Bushes < 0 || d.Bushes > 800) r.Error("bushes", $"{d.Bushes}: between 0 and 800");
        if (d.Tufts < 0 || d.Tufts > 2000) r.Error("tufts", $"{d.Tufts}: between 0 and 2000");
        if (d.Babushkas < 0 || d.Babushkas > 20) r.Error("babushkas", $"{d.Babushkas}: between 0 and 20");
        if (d.Ground != "grass" && d.Ground != "deepsnow") r.Error("ground", $"'{d.Ground}': \"grass\" or \"deepsnow\"");
        if (d.Season != "summer" && d.Season != "autumn") r.Error("season", $"'{d.Season}': \"summer\" or \"autumn\"");
        float extent = d.Size * d.TileSize;

        // ---- the road: a closed loop through the control points
        r.Points = d.Points.Count;
        if (d.Points.Count < MinPoints) r.Error("points", $"{d.Points.Count} control points: at least {MinPoints} (the road is a closed loop through them, do not repeat the first point at the end)");
        if (d.Points.Count > MaxPoints) r.Error("points", $"{d.Points.Count} control points: at most {MaxPoints}");
        float poly = 0;
        for (int i = 0; i < d.Points.Count && i < MaxPoints; i++)
        {
            var p = d.Points[i]; string at = $"points[{i}]";
            if (!Finite(p.X) || !Finite(p.Z) || p.X < Margin || p.Z < Margin || p.X > extent - Margin || p.Z > extent - Margin)
                r.Error(at, $"x {p.X}, z {p.Z} is outside the map: both must be between {Margin} and {extent - Margin} metres (the map is {extent} m square: size x tileSize)");
            Range(r, at + ".width", p.Width, 4f, 24f);
            Range(r, at + ".lift", p.Lift, -6f, 6f);
            if (!KnownSurface(p.Surface)) r.Error(at + ".surface", $"unknown surface '{p.Surface}'{DidYouMean(p.Surface ?? "", Surfaces.Info.Select(s => s.Name))}: one of {SurfaceNames}");
            var q = d.Points[(i + 1) % d.Points.Count];
            float seg = new Vector2(q.X - p.X, q.Z - p.Z).Length();
            poly += seg;
            if (seg < 2f) r.Error(at, $"closer than 2 m to the next point ({seg:0.0} m): the road cannot turn that sharply");
            else if (seg > 160f) r.Warn(at, $"{seg:0} m to the next point: add points between, a long span smooths into an unexpected curve");
        }
        r.RoadLength = poly;
        if (d.Points.Count >= MinPoints && poly < 600f) r.Error("points", $"the road is about {poly:0} m: at least 600 m for a lap worth driving");
        if (d.Points.Count >= MinPoints && poly > 9000f) r.Warn("points", $"the road is about {poly:0} m: long laps load slowly on a Steam Deck");
        CrossingWarnings(d, r);
        foreach (var s in d.SurfaceSpans.Select((s, i) => (s, i)))
        {
            if (!KnownSurface(s.s.Surface)) r.Error($"surfaceSpans[{s.i}].surface", $"unknown surface '{s.s.Surface}'");
            if (!(s.s.From < s.s.To) || s.s.From < 0) r.Error($"surfaceSpans[{s.i}]", "from must be below to, and not negative");
        }

        // ---- the scenery
        r.Props = d.Props.Count;
        if (d.Props.Count > MaxProps) r.Error("props", $"{d.Props.Count} props: at most {MaxProps} (the loading time on a Steam Deck)");
        for (int i = 0; i < d.Props.Count && i < MaxProps; i++)
        {
            var p = d.Props[i]; string at = $"props[{i}]";
            if (!AllowedProps.Contains(p.Type)) r.Error(at + ".type", $"'{p.Type}' is not a prop a custom track may place{DidYouMean(p.Type ?? "", AllowedProps)}: one of {string.Join(", ", AllowedProps)}");
            if (!Finite(p.X) || !Finite(p.Z) || p.X < 2 || p.Z < 2 || p.X > extent - 2 || p.Z > extent - 2) r.Error(at, $"x {p.X}, z {p.Z} is outside the map (0 to {extent})");
            Range(r, at + ".rot", p.Rot, -720f, 720f);
            if (p.Text != null && (p.Text.Length > MaxTextLength || p.Text.Any(c => !PixelFont.CanDraw(c)))) r.Error(at + ".text", $"at most {MaxTextLength} characters the pixel font can draw");
        }
        if (d.Features.Count > MaxFeatures) r.Error("features", $"{d.Features.Count}: at most {MaxFeatures}");
        for (int i = 0; i < d.Features.Count && i < MaxFeatures; i++)
        {
            var f = d.Features[i]; string at = $"features[{i}]";
            if (!FeatureTypes.Contains(f.Type)) r.Error(at + ".type", $"'{f.Type}'{DidYouMean(f.Type ?? "", FeatureTypes)}: one of {string.Join(", ", FeatureTypes)}");
            if (!Finite(f.At) || f.At < 0 || f.At > poly * 1.1f) r.Error(at + ".at", $"{f.At} m is not on the road (0 to about {poly:0} m)");
            Range(r, at + ".length", f.Length, 4f, 120f);
            Range(r, at + ".height", f.Height, 0f, 4f);
            Range(r, at + ".drop", f.Drop, 0f, 20f);
            Range(r, at + ".terrainWidth", f.TerrainWidth, 0f, 60f);
        }
        if (d.Fences.Count > MaxFences) r.Error("fences", $"{d.Fences.Count}: at most {MaxFences}");
        for (int i = 0; i < d.Fences.Count && i < MaxFences; i++)
        {
            var f = d.Fences[i]; string at = $"fences[{i}]";
            if (!FenceTypes.Contains(f.Type)) r.Error(at + ".type", $"'{f.Type}': one of {string.Join(", ", FenceTypes)}");
            if (f.Side != "left" && f.Side != "right") r.Error(at + ".side", $"'{f.Side}': \"left\" or \"right\"");
            if (!(f.From < f.To) || f.From < 0 || f.To > poly * 1.1f) r.Error(at, $"from {f.From} to {f.To}: from must be below to, both on the road (0 to about {poly:0} m)");
            Range(r, at + ".offset", f.Offset, 0f, 30f);
            Range(r, at + ".nearOffset", f.NearOffset, 0f, 30f);
            Range(r, at + ".nearEase", f.NearEase, 0f, 60f);
            Range(r, at + ".levelEase", f.LevelEase, 0f, 20f);
            Range(r, at + ".levelInside", f.LevelInside, -3f, 3f);
            Range(r, at + ".nearFrom", f.NearFrom, 0f, poly * 1.1f);
            Range(r, at + ".nearTo", f.NearTo, 0f, poly * 1.1f);
        }
        foreach (var (f, i) in d.Fields.Select((f, i) => (f, i)))
        {
            if (!KnownSurface(f.Surface)) r.Error($"fields[{i}].surface", $"unknown surface '{f.Surface}'");
            if (!Finite(f.X0 + f.Z0 + f.X1 + f.Z1) || Math.Min(Math.Min(f.X0, f.X1), Math.Min(f.Z0, f.Z1)) < 0 || Math.Max(Math.Max(f.X0, f.X1), Math.Max(f.Z0, f.Z1)) > extent) r.Error($"fields[{i}]", $"the rectangle must lie inside the map (0 to {extent})");
        }
        foreach (var (p, i) in d.Paths.Select((p, i) => (p, i)))
        {
            if (!KnownSurface(p.Surface)) r.Error($"paths[{i}].surface", $"unknown surface '{p.Surface}'");
            if (p.Points.Any(q => q == null || q.Length != 2 || !Finite(q[0]) || !Finite(q[1]) || q[0] < 0 || q[1] < 0 || q[0] > extent || q[1] > extent)) r.Error($"paths[{i}].points", $"each point is [x, z] inside the map (0 to {extent})");
        }
        if (d.Hills.Count > 30) r.Error("hills", $"{d.Hills.Count}: at most 30");
        foreach (var (h, i) in d.Hills.Select((h, i) => (h, i)))
        {
            string at = $"hills[{i}]";
            if (!Finite(h.X + h.Z) || h.X < 0 || h.Z < 0 || h.X > extent || h.Z > extent) r.Error(at, $"x {h.X}, z {h.Z} is outside the map (0 to {extent})");
            Range(r, at + ".height", h.Height, -30f, 30f);
            Range(r, at + ".radius", h.Radius, 10f, 300f);
        }
        if (d.Plateaus.Count > 10) r.Error("plateaus", $"{d.Plateaus.Count}: at most 10");
        foreach (var (pl, i) in d.Plateaus.Select((pl, i) => (pl, i)))
        {
            string at = $"plateaus[{i}]";
            if (!Finite(pl.X0 + pl.Z0 + pl.X1 + pl.Z1) || Math.Min(Math.Min(pl.X0, pl.X1), Math.Min(pl.Z0, pl.Z1)) < 0 || Math.Max(Math.Max(pl.X0, pl.X1), Math.Max(pl.Z0, pl.Z1)) > extent) r.Error(at, $"the rectangle must lie inside the map (0 to {extent})");
            Range(r, at + ".height", pl.Height, -20f, 40f);
            Range(r, at + ".blend", pl.Blend, 0f, 100f);
        }
        if (d.WidthSpans.Count > 30) r.Error("widthSpans", $"{d.WidthSpans.Count}: at most 30");
        foreach (var (w, i) in d.WidthSpans.Select((w, i) => (w, i)))
        {
            Range(r, $"widthSpans[{i}].width", w.Width, 4f, 24f);
            Range(r, $"widthSpans[{i}].taper", w.Taper, 0f, 60f);
            if (!(w.From < w.To) || w.From < 0 || w.To > poly * 1.1f) r.Error($"widthSpans[{i}]", $"from {w.From} to {w.To}: from must be below to, both on the road (0 to about {poly:0} m)");
        }
        CheckGround(d, r, extent);
        foreach (var (pa, i) in d.Paths.Select((pa, i) => (pa, i)))
        {
            if (pa.Points.Count > 200) r.Error($"paths[{i}].points", $"{pa.Points.Count} points: at most 200");
            Range(r, $"paths[{i}].width", pa.Width, 0.5f, 12f);
        }
        if (d.Paths.Count > 40) r.Error("paths", $"{d.Paths.Count}: at most 40");
        if (d.Fields.Count > 40) r.Error("fields", $"{d.Fields.Count}: at most 40");
        if (d.Smooth.Count > 10) r.Error("smooth", $"{d.Smooth.Count}: at most 10");
        if (d.EdgeRim is float rim) Range(r, "edgeRim", rim, 0f, 40f);
        if (d.RoadSmoothing < 0 || d.RoadSmoothing > 30) r.Error("roadSmoothing", $"{d.RoadSmoothing}: between 0 and 30");
        if (d.Scatter.Count > 20) r.Error("scatter", $"{d.Scatter.Count} scatter groups: at most 20");
        if (d.Belts.Count > 40) r.Error("belts", $"{d.Belts.Count} belts: at most 40");
    }

    public static readonly string[] BeltKinds = { "birch", "spruce", "pine", "deadtree" };
    public const int MaxScatterTotal = 2000, MaxBeltTrees = 2000;

    /// <summary>Scatter groups, tree belts, smoothing and meadow: every number that sets how much the build places or how long it loops is bounded (a belt with spacing 0 would never finish).</summary>
    static void CheckGround(TrackDef d, TrackReport r, float extent)
    {
        bool Inside(float v) => Finite(v) && v >= 0 && v <= extent;
        int scatterTotal = 0;
        foreach (var (sc, i) in d.Scatter.Select((sc, i) => (sc, i)))
        {
            string at = $"scatter[{i}]";
            if (!AllowedProps.Contains(sc.Type)) r.Error(at + ".type", $"'{sc.Type}' is not a prop a custom track may scatter{DidYouMean(sc.Type ?? "", AllowedProps)}: one of {string.Join(", ", AllowedProps)}");
            if (sc.Count < 0 || sc.Count > 1000) r.Error(at + ".count", $"{sc.Count}: between 0 and 1000");
            else scatterTotal += sc.Count;
            Range(r, at + ".minSize", sc.MinSize, 0.3f, 3f);
            Range(r, at + ".maxSize", sc.MaxSize, 0.3f, 3f);
            if (Finite(sc.MinSize) && Finite(sc.MaxSize) && sc.MinSize > sc.MaxSize) r.Error(at, "minSize is larger than maxSize");
            Range(r, at + ".roadGap", sc.RoadGap, 0f, 40f);
            Range(r, at + ".propGap", sc.PropGap, 0f, 40f);
            Range(r, at + ".clump", sc.Clump, 0f, 1f);
            Range(r, at + ".clumpScale", sc.ClumpScale, 5f, 200f);
        }
        if (scatterTotal > MaxScatterTotal) r.Error("scatter", $"{scatterTotal} scattered props in all: at most {MaxScatterTotal}");
        double beltTrees = 0;
        foreach (var (b, i) in d.Belts.Select((b, i) => (b, i)))
        {
            string at = $"belts[{i}]";
            if (!Inside(b.X0) || !Inside(b.Z0) || !Inside(b.X1) || !Inside(b.Z1)) { r.Error(at, $"both ends must lie inside the map (0 to {extent})"); continue; }
            if (b.Rows < 1 || b.Rows > 6) r.Error(at + ".rows", $"{b.Rows}: between 1 and 6");
            Range(r, at + ".spacing", b.Spacing, 1f, 50f);
            Range(r, at + ".rowGap", b.RowGap, 0.5f, 20f);
            Range(r, at + ".gap", b.Gap, 0f, 50f);
            Range(r, at + ".minSize", b.MinSize, 0.3f, 3f);
            Range(r, at + ".maxSize", b.MaxSize, 0.3f, 3f);
            if (Finite(b.MinSize) && Finite(b.MaxSize) && b.MinSize > b.MaxSize) r.Error(at, "minSize is larger than maxSize");
            if (b.Kinds == null || b.Kinds.Count == 0 || b.Kinds.Count > 12) r.Error(at + ".kinds", "a list of 1 to 12 tree types");
            else foreach (string k in b.Kinds) if (!BeltKinds.Contains(k)) { r.Error(at + ".kinds", $"'{k}'{DidYouMean(k ?? "", BeltKinds)}: one of {string.Join(", ", BeltKinds)}"); break; }
            float len = new Vector2(b.X1 - b.X0, b.Z1 - b.Z0).Length();
            if (Finite(b.Spacing) && b.Spacing >= 1f && b.Rows >= 1) beltTrees += (double)b.Rows * (len / b.Spacing + 1);
        }
        if (beltTrees > MaxBeltTrees) r.Error("belts", $"about {beltTrees:0} trees in all the belts: at most {MaxBeltTrees} (fewer rows, wider spacing or shorter belts)");
        foreach (var (sm, i) in d.Smooth.Select((sm, i) => (sm, i)))
        {
            string at = $"smooth[{i}]";
            if (!Inside(sm.X0) || !Inside(sm.Z0) || !Inside(sm.X1) || !Inside(sm.Z1)) r.Error(at, $"the rectangle must lie inside the map (0 to {extent})");
            if (sm.Passes < 0 || sm.Passes > 500) r.Error(at + ".passes", $"{sm.Passes}: between 0 and 500");
        }
        if (d.Meadow is { } m)
        {
            Range(r, "meadow.baseHeight", m.BaseHeight, -10f, 40f);
            Range(r, "meadow.amplitude", m.Amplitude, 0f, 30f);
            Range(r, "meadow.scale", m.Scale, 10f, 300f);
        }
        if (d.RoadCharacter is { } rc)
        {
            Range(r, "roadCharacter.edgeInset", rc.EdgeInset, 0f, 3f);
            if (rc.Pinches.Count > 30) r.Error("roadCharacter.pinches", $"{rc.Pinches.Count}: at most 30");
            foreach (var (pi, i) in rc.Pinches.Select((pi, i) => (pi, i)))
            {
                Range(r, $"roadCharacter.pinches[{i}].reduction", pi.Reduction, 0f, 0.6f);
                Range(r, $"roadCharacter.pinches[{i}].taper", pi.Taper, 0f, 60f);
            }
        }
    }

    /// <summary>The road crossing itself (a figure of eight) is not forbidden, but the controls of a car and the track's lap logic expect a loop; the control polygon is checked, a rough test.</summary>
    static void CrossingWarnings(TrackDef d, TrackReport r)
    {
        int n = d.Points.Count;
        if (n < 4 || n > MaxPoints) return;
        Vector2 P(int i) => new(d.Points[i % n].X, d.Points[i % n].Z);
        static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        int found = 0;
        for (int i = 0; i < n && found < 3; i++)
            for (int j = i + 2; j < n && found < 3; j++)
            {
                if (i == 0 && j == n - 1) continue;      // (neighbours through the closing segment)
                Vector2 a = P(i), b = P(i + 1), c = P(j), e = P(j + 1);
                if (Cross(a, b, c) * Cross(a, b, e) < 0 && Cross(c, e, a) * Cross(c, e, b) < 0)
                { r.Warn($"points[{i}]", $"the road crosses itself near x {a.X:0}, z {a.Y:0}: a loop that does not cross is safer to drive"); found++; }
            }
    }

    /// <summary>With the road built, its real length is known (the control polygon's was an estimate): a bump or a fence beyond the end of the road is an error.</summary>
    static void RoadLengthChecks(TrackDef d, float length, TrackReport r)
    {
        for (int i = 0; i < d.Features.Count; i++)
            if (d.Features[i].At > length) r.Error($"features[{i}].at", $"{d.Features[i].At:0} m is past the end of the road, which is {length:0} m long (the start line is 0)");
        for (int i = 0; i < d.Fences.Count; i++)
            if (d.Fences[i].To > length) r.Error($"fences[{i}].to", $"{d.Fences[i].To:0} m is past the end of the road, which is {length:0} m long");
        for (int i = 0; i < d.WidthSpans.Count; i++)
            if (d.WidthSpans[i].To > length) r.Error($"widthSpans[{i}].to", $"{d.WidthSpans[i].To:0} m is past the end of the road, which is {length:0} m long");
        for (int i = 0; i < d.SurfaceSpans.Count; i++)
            if (d.SurfaceSpans[i].To > length) r.Error($"surfaceSpans[{i}].to", $"{d.SurfaceSpans[i].To:0} m is past the end of the road, which is {length:0} m long");
    }

    /// <summary>The build itself: the terrain and road, the signature, the props. A track that does not build is an error with the engine's reason.</summary>
    static void Build(string json, TrackReport r)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Node3D props = null;
        try
        {
            var def = TrackDef.Parse(json);
            TrackSource.Normalize(def, TrackSource.CustomPrefix + "check");
            var t = TrackBuilder.Build(def);
            if (t == null) { r.Error("", "the track did not build"); return; }
            r.RoadLength = t.Center.Length;
            r.Signature = TrackSignature.Compute(t);
            RoadLengthChecks(def, t.Center.Length, r);
            props = Props.Build(t);
            r.Built = true;
        }
        catch (Exception e) { r.Error("", $"the track did not build: {e.GetType().Name}: {e.Message}"); }
        finally { if (props != null && GodotObject.IsInstanceValid(props)) props.Free(); r.BuildSeconds = sw.Elapsed.TotalSeconds; }
        if (r.Built && r.BuildSeconds > 20) r.Warn("", $"the build took {r.BuildSeconds:0} s here: smaller maps and fewer props load faster, a Steam Deck is slower than this machine");
    }
}
