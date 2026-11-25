using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace CCCPRacing.Track;

/// <summary>
/// The custom track reference, generated from TrackDef itself (--track-schema prints it; docs/public/custom-tracks-reference.md is that output, and a test keeps the two equal). The fields,
/// types and defaults cannot drift from the code; what a field means is in the guide (docs/public/custom-tracks.md).
/// </summary>
public static class TrackSchema
{
    static readonly Dictionary<string, string> Limits = new()
    {
        ["size"] = $"{TrackValidator.MinSize} to {TrackValidator.MaxSize}", ["tileSize"] = "1 to 4", ["hillAmp"] = "0 to 30", ["hillScale"] = "20 to 300",
        ["birches"] = "0 to 400", ["bushes"] = "0 to 800", ["tufts"] = "0 to 2000", ["babushkas"] = "0 to 20", ["ground"] = "\"grass\" or \"deepsnow\"", ["season"] = "\"summer\" or \"autumn\"",
        ["name"] = $"at most {TrackValidator.MaxNameLength} characters the pixel font can draw", ["points"] = $"{TrackValidator.MinPoints} to {TrackValidator.MaxPoints}, a closed loop",
        ["points.x"] = "14 to map size - 14", ["points.z"] = "14 to map size - 14", ["points.width"] = "4 to 24", ["points.lift"] = "-6 to 6", ["points.surface"] = "a surface",
        ["props"] = $"at most {TrackValidator.MaxProps}", ["props.type"] = "a prop type", ["props.text"] = $"at most {TrackValidator.MaxTextLength} characters",
        ["features"] = $"at most {TrackValidator.MaxFeatures}", ["features.type"] = "crest, dip or jump", ["features.length"] = "4 to 120", ["features.height"] = "0 to 4", ["features.drop"] = "0 to 20",
        ["fences"] = $"at most {TrackValidator.MaxFences}", ["fences.type"] = "wood or stone", ["fences.side"] = "left or right",
        ["surfaceSpans.surface"] = "a surface", ["fields.surface"] = "a surface", ["paths.surface"] = "a surface",
        ["scatter"] = "at most 20", ["belts"] = "at most 40", ["hills"] = "at most 30", ["hills.height"] = "-30 to 30", ["hills.radius"] = "10 to 300",
        ["plateaus"] = "at most 10", ["plateaus.height"] = "-20 to 40", ["plateaus.blend"] = "0 to 100", ["widthSpans"] = "at most 30", ["widthSpans.width"] = "4 to 24",
        ["scatter.type"] = "a prop type", ["scatter.count"] = "0 to 1000, at most 2000 in all", ["scatter.minSize"] = "0.3 to 3", ["scatter.maxSize"] = "0.3 to 3", ["scatter.roadGap"] = "0 to 40", ["scatter.propGap"] = "0 to 40",
        ["scatter.clump"] = "0 to 1", ["scatter.clumpScale"] = "5 to 200", ["belts.rows"] = "1 to 6", ["belts.spacing"] = "1 to 50", ["belts.rowGap"] = "0.5 to 20", ["belts.gap"] = "0 to 50", ["belts.minSize"] = "0.3 to 3",
        ["belts.maxSize"] = "0.3 to 3", ["belts.kinds"] = "1 to 12 of birch, spruce, pine, deadtree; at most 2000 trees in all", ["belts.x0"] = "inside the map", ["belts.z0"] = "inside the map", ["belts.x1"] = "inside the map", ["belts.z1"] = "inside the map",
        ["smooth.passes"] = "0 to 500", ["smooth.x0"] = "inside the map", ["smooth.z0"] = "inside the map", ["smooth.x1"] = "inside the map", ["smooth.z1"] = "inside the map", ["paths.width"] = "0.5 to 12", ["paths"] = "at most 40",
        ["fields"] = "at most 40", ["meadow.baseHeight"] = "-10 to 40", ["meadow.amplitude"] = "0 to 30", ["meadow.scale"] = "10 to 300", ["roadCharacter.edgeInset"] = "0 to 3", ["roadCharacter.pinches"] = "at most 30",
        ["widthSpans.taper"] = "0 to 60", ["props.rot"] = "-720 to 720", ["features.terrainWidth"] = "0 to 60", ["fences.offset"] = "0 to 30", ["fences.nearOffset"] = "0 to 30", ["fences.nearEase"] = "0 to 60",
        ["fences.levelEase"] = "0 to 20", ["fences.levelInside"] = "-3 to 3", ["fences.nearFrom"] = "on the road", ["fences.nearTo"] = "on the road", ["smooth"] = "at most 10", ["edgeRim"] = "0 to 40", ["roadSmoothing"] = "0 to 30",
    };

    static string TypeName(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        if (t == typeof(string)) return "text";
        if (t == typeof(bool)) return "true/false";
        if (t == typeof(int) || t == typeof(long)) return "integer";
        if (t == typeof(float) || t == typeof(double)) return "number";
        if (t.IsArray) return "list of " + TypeName(t.GetElementType());
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) return "list of " + TypeName(t.GetGenericArguments()[0]);
        return "object (" + t.Name.Replace("Def", "").ToLowerInvariant() + ")";
    }

    static string Default(object o, PropertyInfo p)
    {
        object v = o == null ? null : p.GetValue(o);
        return v switch
        {
            null => "none",
            string s => s.Length == 0 ? "\"\"" : "\"" + s + "\"",
            bool b => b ? "true" : "false",
            float f => f.ToString("0.###", CultureInfo.InvariantCulture),
            double d => d.ToString("0.###", CultureInfo.InvariantCulture),
            IList l => l.Count == 0 ? "empty" : l.Count + " items",
            IFormattable x => x.ToString(null, CultureInfo.InvariantCulture),
            _ => "see the object's fields",
        };
    }

    static string Camel(string s) => char.ToLowerInvariant(s[0]) + s[1..];

    static IEnumerable<PropertyInfo> Props(Type t) => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite).OrderBy(p => p.MetadataToken);

    static Type ElementOf(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) t = t.GetGenericArguments()[0];
        return t.IsClass && t != typeof(string) && !t.IsArray ? t : null;
    }

    static void Table(StringBuilder sb, string heading, Type type, string prefix, object instance, Func<PropertyInfo, bool> keep = null)
    {
        sb.Append($"\n### {heading}\n\n| field | type | default | limits |\n| --- | --- | --- | --- |\n");
        foreach (var p in Props(type))
        {
            if (keep != null && !keep(p)) continue;
            string key = prefix + Camel(p.Name);
            Limits.TryGetValue(key, out string limit);
            sb.Append($"| `{Camel(p.Name)}` | {TypeName(p.PropertyType)} | {Default(instance, p)} | {limit ?? ""} |\n");
        }
    }

    public static string Markdown()
    {
        var sb = new StringBuilder();
        var def = new TrackDef();
        sb.Append("# Custom track reference\n\n");
        sb.Append("Generated by `--track-schema` from the game's own track definition: do not edit by hand (a test keeps this file equal to the game's output). What the fields mean, and how to build a good track, is in [custom-tracks.md](custom-tracks.md).\n\n");
        sb.Append("A track is one JSON file; comments (`//`) and a trailing comma are allowed. Unknown or misspelt fields are errors.\n");
        Table(sb, "Track fields", typeof(TrackDef), "", def, p => TrackValidator.AllowedFields.Contains(Camel(p.Name)));
        sb.Append("\nThese are all the top-level fields a custom track may set. The campaign tracks use more (water, snow banks, the camp, the radiation, the airbase, the norms...); they are refused in a custom track. `id` is ignored: the game uses the folder name.\n");
        foreach (string f in TrackValidator.AllowedFields)
        {
            var p = typeof(TrackDef).GetProperty(f, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (p == null) continue;
            var el = ElementOf(p.PropertyType);
            if (el == null) continue;
            object inst; try { inst = Activator.CreateInstance(el); } catch { inst = null; }
            Table(sb, $"`{f}` items ({el.Name})", el, f + ".", inst);
        }
        sb.Append("\n### Prop types (`props[].type`)\n\n").Append(string.Join(", ", TrackValidator.AllowedProps.Select(t => $"`{t}`"))).Append("\n");
        sb.Append("\n### Surfaces (`points[].surface`, `surfaceSpans`, `fields`, `paths`)\n\n").Append(string.Join(", ", Surfaces.Info.Select(i => $"`{i.Name}`"))).Append("\n");
        sb.Append($"\n### Limits\n\nFile at most {TrackValidator.MaxBytes / 1024} kB; map size x tileSize is the square the road and props must stay inside; every x and z keeps {14} m from the map's edge (props 2 m).\n");
        return sb.ToString();
    }
}
