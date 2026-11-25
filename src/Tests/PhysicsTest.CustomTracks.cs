using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

/// <summary>
/// Custom tracks (a player's own track as data in user://tracks/NAME/track.json): the loader's ids and paths, the validator's findings (what an AI agent reads), every prop a custom
/// track may place building, the folder scan the menu will use, and the real game driving the example track one lap (--custom-track-tests, also in the full suite).
/// </summary>
public partial class PhysicsTest
{
    const string OvalJson = "docs/public/examples/oval/track.json";

    static string OvalText() => File.ReadAllText(ProjectSettings.GlobalizePath("res://" + OvalJson));

    /// <summary>The oval with one top-level field replaced or added (a JSON text edit: the examples are plain JSON with comments).</summary>
    static string WithField(string json, string name, string valueJson)
    {
        int open = json.IndexOf('{');
        return json[..(open + 1)] + $"\n  \"{name}\": {valueJson}," + json[(open + 1)..];
    }

    /// <summary>The oval with "props": null (the props array is the file's last field) and with "points": null.</summary>
    static string PropsNull() => OvalText().Split("\"props\"")[0] + "\"props\": null }";
    static string PointsNull() => OvalText().Split("\"points\"")[0] + "\"points\": null, \"props\"" + OvalText().Split("\"props\"")[1];

    static bool HasError(TrackReport r, string path, string fragment) => r.Issues.Any(i => i.Error && i.Path == path && i.Message.Contains(fragment));

    void CustomTracksTest()
    {
        GD.Print("Custom track tests");
        // ---- ids and paths
        Check(TrackSource.PathOf("ilyich") == "res://tracks/ilyich.json" && TrackSource.PathOf("bonus") == "res://tracks/bonus.json", "custom tracks: a campaign track's file is where it was");
        Check(TrackSource.PathOf("custom-my-oval") == "user://tracks/my-oval/track.json" && TrackSource.IsCustom("custom-x") && !TrackSource.IsCustom("custom-") && !TrackSource.IsCustom("ilyich"), "custom tracks: custom-NAME is user://tracks/NAME/track.json");
        Check(TrackSource.PathOf("custom-../etc") == null && TrackSource.PathOf("custom-A") == null && TrackSource.PathOf("custom-a b") == null && TrackSource.PathOf("custom-" + new string('a', 33)) == null, "custom tracks: a folder name with dots, capitals, spaces or more than 32 characters is no track");
        Check(!TrackIds.Known("custom-oval"), "custom tracks: the LAN's list of known tracks does not include a custom one");
        var norm = new TrackDef { Id = "x", Fuel = true, Norms = new NormsDef() };
        TrackSource.Normalize(norm, "custom-t");
        Check(norm.Id == "custom-t" && !norm.Fuel && norm.Norms == null, "custom tracks: a custom track takes its own id, no fuel and the default norms");

        // ---- the example validates and builds, and builds the same twice
        var oval = TrackValidator.Validate(OvalText());
        Check(oval.Ok && oval.Built && oval.Points == 18 && oval.RoadLength > 500 && oval.Signature.Length == 16 && TrackSignature.LooksValid(oval.Signature), $"custom tracks: the oval example is accepted and builds ({oval.Points} points, {oval.RoadLength:0} m, {oval.Signature}, {string.Join("; ", oval.Issues)})");
        Check(oval.Issues.Count == 0, "custom tracks: the oval example has no warnings either: " + string.Join("; ", oval.Issues));
        var again = TrackValidator.Validate(OvalText());
        Check(again.Signature == oval.Signature, "custom tracks: the same file gives the same signature (records belong to it)");
        string edited = OvalText().Replace("\"hillAmp\": 3.0", "\"hillAmp\": 4.0");
        Check(TrackValidator.Validate(edited).Signature != oval.Signature, "custom tracks: an edited track has another signature (the old records no longer fit it)");

        // ---- the richer example, the variants of the ground, and the reference file
        var village = TrackValidator.Validate(File.ReadAllText(ProjectSettings.GlobalizePath("res://docs/public/examples/village/track.json")));
        Check(village.Ok && village.Built && village.Issues.Count == 0 && village.Props > 100, $"custom tracks: the village example (autumn, jump, fence, field, path, hill) is accepted and builds ({village.Props} props, {village.RoadLength:0} m): {string.Join("; ", village.Issues)}");
        NightGlow.Glows.Clear();
        var snow = TrackValidator.Validate(OvalText().Replace("\"hillAmp\": 3.0", "\"hillAmp\": 3.0, \"ground\": \"deepsnow\""));
        Check(snow.Ok && snow.Built, "custom tracks: the oval on deep snow builds: " + string.Join("; ", snow.Issues));
        NightGlow.Glows.Clear();
        string refFile = ProjectSettings.GlobalizePath("res://docs/public/custom-tracks-reference.md");
        Check(File.Exists(refFile) && File.ReadAllText(refFile).ReplaceLineEndings("\n") == TrackSchema.Markdown().ReplaceLineEndings("\n"), "custom tracks: docs/public/custom-tracks-reference.md is the game's own --track-schema output (regenerate: --track-schema=FILE)");
        var campaignField = TrackValidator.Validate(OvalText().Replace("\"birches\": 60", "\"birches\": 60, \"water\": { \"ponds\": [] }, \"gulag\": { }"));
        Check(HasError(campaignField, "gulag", "not available in custom tracks"), "custom tracks: a campaign field such as the camp is refused (the list of allowed fields, not a ban list)");
        Check(HasError(TrackValidator.Validate(OvalText().Replace("\"babushkas\": 2", "\"babushkas\": 2, \"hills\": [ { \"x\": 100, \"z\": 100, \"height\": 90, \"radius\": 50 } ]"), build: false), "hills[0].height", "outside"), "custom tracks: a hill too high is refused");

        // the guide says: the start line is at the first control point and the car drives towards the second
        {
            var def = TrackDef.Parse(OvalText()); TrackSource.Normalize(def, "custom-oval");
            var t = TrackBuilder.Build(def);
            var p0 = new Vector2(def.Points[0].X, def.Points[0].Z); var toNext = (new Vector2(def.Points[1].X, def.Points[1].Z) - p0).Normalized();
            Check(t.Center[0].DistanceTo(p0) < 8f && t.CenterDir(0).Dot(toNext) > 0.5f, $"custom tracks: the start line is at the first control point, heading for the second (off by {t.Center[0].DistanceTo(p0):0.0} m, direction {t.CenterDir(0).Dot(toNext):0.00})");
        }

        // ---- what an agent is told
        var bad = TrackValidator.Validate("{ \"name\": \"X\", ");
        Check(!bad.Ok && bad.Issues[0].Message.Contains("not valid JSON at line"), "custom tracks: broken JSON says where: " + bad.FirstError());
        Check(!TrackValidator.Validate("[]").Ok && !TrackValidator.Validate("").Ok, "custom tracks: a file that is not one object is refused");
        var typo = TrackValidator.Validate(WithField(OvalText(), "propz", "[]"));
        Check(HasError(typo, "propz", "did you mean 'props'"), "custom tracks: a misspelt field is an error with the likely name: " + string.Join("; ", typo.Issues.Select(i => i.Message)));
        var prop = TrackValidator.Validate(OvalText().Replace("\"type\": \"barn\"", "\"type\": \"barnn\""));
        Check(HasError(prop, "props[0].type", "did you mean 'barn'"), "custom tracks: a misspelt prop type names the right one");
        var camp = TrackValidator.Validate(OvalText().Replace("\"birches\": 60", "\"birches\": 60, \"radiation\": { \"background\": 1 }"));
        Check(HasError(camp, "radiation", "not available in custom tracks"), "custom tracks: a campaign-only field is refused");
        var out1 = TrackValidator.Validate(OvalText().Replace("\"x\": 278", "\"x\": 900"));
        Check(out1.Issues.Any(i => i.Error && i.Path.StartsWith("points[") && i.Message.Contains("outside the map")), "custom tracks: a control point outside the map is refused: " + string.Join("; ", out1.Issues.Take(2)));
        var few = TrackValidator.Validate(WithField(OvalText().Split("\"points\"")[0] + "\"points\": [ { \"x\": 50, \"z\": 50 }, { \"x\": 200, \"z\": 60 }, { \"x\": 120, \"z\": 200 } ] }", "size", "160"));
        Check(HasError(few, "points", "at least 6"), "custom tracks: too few control points");
        Check(!TrackValidator.Validate(OvalText().Replace("OVAL EXAMPLE", "THIS NAME IS FAR TOO LONG TO FIT")).Ok && !TrackValidator.Validate(OvalText().Replace("OVAL EXAMPLE", "OVAL ~ EXAMPLE")).Ok, "custom tracks: a name too long or with a character the pixel font cannot draw is refused");
        Check(!TrackValidator.Validate(OvalText().Replace("\"surface\": \"dirt\"", "\"surface\": \"sandd\"")).Ok, "custom tracks: an unknown surface is refused");
        var many = new StringBuilder();
        for (int i = 0; i <= TrackValidator.MaxProps; i++) many.Append(i > 0 ? "," : "").Append("{ \"type\": \"rock\", \"x\": 30, \"z\": 30 }");
        var big = TrackValidator.Validate(OvalText().Split("\"props\"")[0] + "\"props\": [" + many + "] }", build: false);
        Check(HasError(big, "props", "at most"), "custom tracks: more props than the budget is refused");
        Check(!TrackValidator.Validate(new string(' ', TrackValidator.MaxBytes + 1)).Ok, "custom tracks: a file over the size limit is refused");
        // a figure of eight is a warning, not an error
        string eight = OvalText().Split("\"points\"")[0] + "\"points\": [ {\"x\":60,\"z\":60},{\"x\":160,\"z\":60},{\"x\":260,\"z\":60},{\"x\":60,\"z\":260},{\"x\":160,\"z\":260},{\"x\":260,\"z\":260} ] }";
        var cross = TrackValidator.Validate(eight, build: false);
        Check(cross.Ok && cross.Issues.Any(i => !i.Error && i.Message.Contains("crosses itself")), "custom tracks: a crossing road is a warning only: " + string.Join("; ", cross.Issues));

        // ---- every prop a custom track may place builds on a custom track
        var sb = new StringBuilder();
        for (int i = 0; i < TrackValidator.AllowedProps.Length; i++)
            sb.Append(i > 0 ? ",\n" : "").Append($"    {{ \"type\": \"{TrackValidator.AllowedProps[i]}\", \"x\": {40 + (i % 7) * 34}, \"z\": {40 + (i / 7) * 34}, \"rot\": {i * 20} }}");
        var all = TrackValidator.Validate(OvalText().Split("\"props\"")[0] + "\"props\": [\n" + sb + "\n  ] }");
        Check(all.Ok && all.Built, "custom tracks: all " + TrackValidator.AllowedProps.Length + " allowed props build together: " + string.Join("; ", all.Issues));
        NightGlow.Glows.Clear();

        // ---- the scan the menu uses (a scratch folder in the user data)
        string root = $"user://scratch-tracks-{System.Environment.ProcessId}/";
        try
        {
            string abs = ProjectSettings.GlobalizePath(root);
            foreach (var (folder, text) in new[] { ("good-one", OvalText()), ("broken", "{ nope"), ("Bad Name", OvalText()) })
            { Directory.CreateDirectory(Path.Combine(abs, folder)); File.WriteAllText(Path.Combine(abs, folder, "track.json"), text); }
            Directory.CreateDirectory(Path.Combine(abs, "empty-folder"));
            var list = TrackSource.Scan(root);
            var byFolder = list.ToDictionary(e => e.Folder);
            Check(list.Count == 3 && !byFolder.ContainsKey("empty-folder"), $"custom tracks: the scan lists the folders that hold a track.json ({string.Join(", ", list.Select(e => e.Folder))})");
            Check(byFolder["good-one"].Error == null && byFolder["good-one"].Name == "OVAL EXAMPLE" && byFolder["good-one"].Id == "custom-good-one", "custom tracks: a good track is listed with its own name and id");
            Check(byFolder["broken"].Error != null && byFolder["Bad Name"].Error != null, "custom tracks: a broken track and a badly named folder stay listed, with the reason");
            Check(list[0].Folder == "Bad Name" || list.Select(e => e.Folder).SequenceEqual(list.Select(e => e.Folder).OrderBy(f => f, StringComparer.Ordinal)), "custom tracks: the list is in folder order");
        }
        finally { try { Directory.Delete(ProjectSettings.GlobalizePath(root), true); } catch { } }
        Check(TrackSource.Scan("user://there-is-no-such-folder/").Count == 0, "custom tracks: no folder, no tracks");
        // a track whose name the card cannot draw is listed under its folder's name, never the raw text
        string scratch2 = $"user://scratch-names-{System.Environment.ProcessId}/";
        try
        {
            string abs2 = ProjectSettings.GlobalizePath(scratch2);
            foreach (var (folder, nm) in new[] { ("long-name", new string('X', 200)), ("odd-chars", "ODD ~ NAME"), ("fine", "FINE NAME") })
            { Directory.CreateDirectory(Path.Combine(abs2, folder)); File.WriteAllText(Path.Combine(abs2, folder, "track.json"), OvalText().Replace("OVAL EXAMPLE", nm)); }
            var names = TrackSource.Scan(scratch2).ToDictionary(e => e.Folder);
            Check(names["long-name"].Name == "LONG-NAME" && names["long-name"].Error != null && names["odd-chars"].Name == "ODD-CHARS" && names["odd-chars"].Error != null && names["fine"].Name == "FINE NAME" && names["fine"].Error == null,
                $"custom tracks: a name too long or undrawable is replaced by the folder's name in the list ({string.Join(" | ", names.Values.Select(e => e.Name))})");
        }
        finally { try { Directory.Delete(ProjectSettings.GlobalizePath(scratch2), true); } catch { } }
        // the pause menu's resume and the car tour's card do not read a custom track's file again
        Check(TrackSource.NormsOf("custom-no-such-track") == null && TrackSource.NormsOf("ilyich") != null, "custom tracks: NormsOf reads a campaign track's norms and gives a custom track none without reading its file");

        // ---- a null where a list or an item belongs: errors, never an exception (the menu's scan reads every folder)
        foreach (var (what, json) in new[]
        {
            ("props null", PropsNull()), ("points null", PointsNull()),
            ("features null", WithField(OvalText(), "features", "null")), ("a null prop", OvalText().Replace("\"props\": [", "\"props\": [ null,")), ("a null point", OvalText().Replace("\"points\": [", "\"points\": [ null,")),
            ("pinches null", WithField(OvalText(), "roadCharacter", "{ \"from\": 10, \"to\": 200, \"pinches\": null }")), ("paths with a null list", WithField(OvalText(), "paths", "[ { \"points\": null } ]")),
        })
        {
            TrackReport rep = null; string thrown = null;
            try { rep = TrackValidator.Validate(json, build: true); } catch (Exception e) { thrown = e.GetType().Name; }
            Check(thrown == null && rep != null && !rep.Ok, $"custom tracks: {what} is an error in the report, not an exception ({thrown ?? string.Join("; ", rep.Issues.Take(1))})");
        }
        {
            var nulled = TrackValidator.Validate(WithField(OvalText(), "features", "null"), build: false);
            Check(HasError(nulled, "features", "not null"), "custom tracks: a null list says which field: " + string.Join("; ", nulled.Issues.Take(1)));
            Check(HasError(TrackValidator.Validate(PropsNull(), build: false), "props", "not null") && HasError(TrackValidator.Validate(PointsNull(), build: false), "points", "not null"), "custom tracks: props and points set to null each get their own message");
            var nullItem = TrackValidator.Validate(OvalText().Replace("\"props\": [", "\"props\": [ null,"), build: false);
            Check(HasError(nullItem, "props[0]", "not null"), "custom tracks: a null item says which one: " + string.Join("; ", nullItem.Issues.Take(1)));
        }

        // ---- the numbers that set how much the build places or how long it loops: refused up front (a belt with spacing 0 never ends), and the check itself stays quick
        {
            string Belt(string fields) => WithField(OvalText(), "belts", "[ { \"x0\": 30, \"z0\": 30, \"x1\": 130, \"z1\": 30" + fields + " } ]");
            foreach (var (what, json, path, fragment) in new[]
            {
                ("belt spacing 0", Belt(", \"spacing\": 0"), "belts[0].spacing", "outside"), ("belt spacing negative", Belt(", \"spacing\": -2"), "belts[0].spacing", "outside"),
                ("belt kinds empty", Belt(", \"kinds\": []"), "belts[0].kinds", "1 to 12"), ("belt kind unknown", Belt(", \"kinds\": [ \"reactor\" ]"), "belts[0].kinds", "one of"),
                ("belt rows 99", Belt(", \"rows\": 99"), "belts[0].rows", "between 1 and 6"), ("belt outside the map", WithField(OvalText(), "belts", "[ { \"x0\": 30, \"z0\": 30, \"x1\": 9000, \"z1\": 30 } ]"), "belts[0]", "inside the map"),
                ("belt minSize above maxSize", Belt(", \"minSize\": 2, \"maxSize\": 1"), "belts[0]", "larger than"),
                ("belts too many trees", WithField(OvalText(), "belts", "[ { \"x0\": 20, \"z0\": 20, \"x1\": 300, \"z1\": 20, \"rows\": 6, \"spacing\": 1 }, { \"x0\": 20, \"z0\": 40, \"x1\": 300, \"z1\": 40, \"rows\": 6, \"spacing\": 1 } ]"), "belts", "trees in all"),
                ("scatter count huge", WithField(OvalText(), "scatter", "[ { \"type\": \"rock\", \"count\": 100000000 } ]"), "scatter[0].count", "between 0 and 1000"),
                ("scatter total", WithField(OvalText(), "scatter", "[ { \"type\": \"rock\", \"count\": 1000 }, { \"type\": \"stump\", \"count\": 1000 }, { \"type\": \"rock\", \"count\": 1000 } ]"), "scatter", "in all"),
                ("scatter type", WithField(OvalText(), "scatter", "[ { \"type\": \"reactor\", \"count\": 10 } ]"), "scatter[0].type", "not a prop a custom track may scatter"),
                ("scatter sizes", WithField(OvalText(), "scatter", "[ { \"type\": \"rock\", \"count\": 10, \"minSize\": 0, \"maxSize\": 99 } ]"), "scatter[0].minSize", "outside"),
                ("smooth passes huge", WithField(OvalText(), "smooth", "[ { \"x0\": 20, \"z0\": 20, \"x1\": 100, \"z1\": 100, \"passes\": 2000000000 } ]"), "smooth[0].passes", "between 0 and 500"),
                ("smooth outside", WithField(OvalText(), "smooth", "[ { \"x0\": 20, \"z0\": 20, \"x1\": 5000, \"z1\": 100 } ]"), "smooth[0]", "inside the map"),
                ("meadow amplitude", WithField(OvalText(), "meadow", "{ \"amplitude\": 900 }"), "meadow.amplitude", "outside"),
                ("pinch reduction", WithField(OvalText(), "roadCharacter", "{ \"from\": 10, \"to\": 200, \"pinches\": [ { \"from\": 20, \"to\": 60, \"reduction\": 5 } ] }"), "roadCharacter.pinches[0].reduction", "outside"),
            })
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var rep = TrackValidator.Validate(json, build: true);      // (the build is not reached: an error stops it first)
                Check(sw.Elapsed.TotalSeconds < 10 && HasError(rep, path, fragment) && !rep.Built, $"custom tracks: {what} is refused at once ({sw.Elapsed.TotalSeconds:0.0} s): {string.Join("; ", rep.Issues.Take(2).Select(i => i.Path + " " + i.Message))}");
            }
            // sane values in the same fields are accepted and build
            string sane = OvalText().Split("\"props\"")[0]
                + "\"belts\": [ { \"x0\": 30, \"z0\": 40, \"x1\": 130, \"z1\": 40, \"rows\": 2, \"spacing\": 4, \"kinds\": [ \"birch\", \"spruce\" ] } ],"
                + "\"scatter\": [ { \"type\": \"rock\", \"count\": 60 }, { \"type\": \"stump\", \"count\": 30 } ],"
                + "\"smooth\": [ { \"x0\": 40, \"z0\": 240, \"x1\": 120, \"z1\": 300, \"passes\": 50 } ],"
                + "\"meadow\": { \"baseHeight\": 4, \"amplitude\": 5, \"scale\": 55 }, \"props\": [ ] }";
            var saneReport = TrackValidator.Validate(sane);
            Check(saneReport.Ok && saneReport.Built, "custom tracks: sane belts, scatter, smooth and meadow are accepted and build: " + string.Join("; ", saneReport.Issues.Take(2).Select(i => i.Path + " " + i.Message)));
            NightGlow.Glows.Clear();
        }
        // the ranges of the nested numbers, the same narrowest width for points and width spans, and the real road length once the road is built
        {
            string Fence(string fields) => WithField(OvalText(), "fences", "[ { \"type\": \"wood\", \"from\": 10, \"to\": 100, \"side\": \"left\"" + fields + " } ]");
            foreach (var (what, json, path) in new[]
            {
                ("fence offset 1e9", Fence(", \"offset\": 1000000000"), "fences[0].offset"), ("fence nearOffset", Fence(", \"nearOffset\": 500"), "fences[0].nearOffset"),
                ("fence levelEase", Fence(", \"levelEase\": 900"), "fences[0].levelEase"), ("fence nearFrom", Fence(", \"nearFrom\": 90000, \"nearTo\": 90100"), "fences[0].nearFrom"),
                ("feature terrainWidth 5000", WithField(OvalText(), "features", "[ { \"type\": \"crest\", \"at\": 100, \"terrainWidth\": 5000 } ]"), "features[0].terrainWidth"),
                ("prop rot 1e9", OvalText().Replace("\"type\": \"barn\", \"x\": 160, \"z\": 160, \"rot\": 0", "\"type\": \"barn\", \"x\": 160, \"z\": 160, \"rot\": 1000000000"), "props[0].rot"),
            })
            {
                var rep = TrackValidator.Validate(json, build: false);
                Check(HasError(rep, path, "outside"), $"custom tracks: {what} is refused: " + string.Join("; ", rep.Issues.Take(2).Select(i => i.Path + " " + i.Message)));
            }
            Check(TrackValidator.Validate(WithField(OvalText(), "widthSpans", "[ { \"from\": 100, \"to\": 200, \"width\": 5, \"taper\": 10 } ]"), build: false).Ok, "custom tracks: a width span can narrow the road to 5 m, as a control point can");
            var estimate = TrackValidator.Validate(OvalText(), build: false).RoadLength;      // (the control polygon's length, the real road is shorter)
            string past = WithField(OvalText(), "features", $"[ {{ \"type\": \"crest\", \"at\": {estimate * 1.08f:0}, \"length\": 20 }} ]");
            var before = TrackValidator.Validate(past, build: false); var after = TrackValidator.Validate(past, build: true);
            Check(before.Ok && !after.Ok && HasError(after, "features[0].at", "past the end of the road"), $"custom tracks: a bump past the built road's end passes the estimate ({estimate:0} m) but is refused once the road is built ({after.RoadLength:0} m): " + string.Join("; ", after.Issues.Take(1).Select(i => i.Path + " " + i.Message)));
            NightGlow.Glows.Clear();
        }

        // a file is read once: what is validated is what is parsed (FromText), and a text that fails never becomes a track
        Check(TrackSource.FromText(OvalText(), "custom-t").Id == "custom-t", "custom tracks: FromText gives the normalised track of a good text");
        bool refused = false; try { TrackSource.FromText(WithField(OvalText(), "gulag", "{ }"), "custom-t"); } catch (System.IO.InvalidDataException) { refused = true; }
        bool refusedNull = false; try { TrackSource.FromText(null, "custom-t"); } catch (System.IO.InvalidDataException) { refusedNull = true; }
        Check(refused && refusedNull, "custom tracks: FromText refuses a text with a campaign field and a missing file");
        Check(!TrackValidator.Validate("{ \"size\": 99999999999999999999 }", build: false).Ok, "custom tracks: a number too large for its field is an error, not an exception");

        // ---- Load checks a custom track on every read; ids that do not belong in a path are no ids
        Check(TrackSource.PathOf("../x") == null && TrackSource.PathOf("ilyich/../x") == null && TrackSource.PathOf("") == null && TrackSource.PathOf(null) == null && TrackSource.PathOf("a b") == null, "custom tracks: an id with a slash, dots, a space or nothing in it has no file");
        Check(TrackSource.PathOf("ilyich") != null && TrackSource.PathOf("atomgrad") != null && TrackSource.PathOf("siberia") != null && TrackSource.PathOf("bonus") != null, "custom tracks: the campaign ids still have their files");
        bool threw = false; try { TrackSource.Load("../x"); } catch (ArgumentException) { threw = true; } catch (Exception) { }
        Check(threw, "custom tracks: loading an id that is no track is an ArgumentException");
        string loadName = $"zz-load-test-{System.Environment.ProcessId}", loadDir = ProjectSettings.GlobalizePath($"user://tracks/{loadName}");
        try
        {
            Directory.CreateDirectory(loadDir);
            File.WriteAllText(Path.Combine(loadDir, "track.json"), OvalText());
            var loaded = TrackSource.Load("custom-" + loadName);
            Check(loaded.Id == "custom-" + loadName && !loaded.Fuel && loaded.Norms == null && loaded.Points.Count == 18, "custom tracks: Load reads a good track from the user data, normalised");
            File.WriteAllText(Path.Combine(loadDir, "track.json"), WithField(OvalText(), "gulag", "{ }"));      // (edited after the menu listed it)
            string reason = null; try { TrackSource.Load("custom-" + loadName); } catch (System.IO.InvalidDataException e) { reason = e.Message; }
            Check(reason != null && reason.Contains("not available in custom tracks"), "custom tracks: Load refuses a track edited into an invalid one: " + reason);
            File.WriteAllText(Path.Combine(loadDir, "track.json"), "{ \"props\": null }");
            reason = null; try { TrackSource.Load("custom-" + loadName); } catch (System.IO.InvalidDataException e) { reason = e.Message; } catch (Exception e) { reason = "WRONG " + e.GetType().Name; }
            Check(reason != null && !reason.StartsWith("WRONG"), "custom tracks: Load of a track with a null list is an InvalidDataException, not a crash: " + reason);
            // the name a player types is the player's folder first, then a folder of that name where the command runs
            string stray = Path.Combine(Directory.GetCurrentDirectory(), loadName);
            try
            {
                File.WriteAllText(Path.Combine(loadDir, "track.json"), OvalText());
                Directory.CreateDirectory(stray); File.WriteAllText(Path.Combine(stray, "track.json"), "{ \"name\": \"STRAY\" }");
                Check(TrackCheck.Resolve(loadName) == Path.Combine(loadDir, "track.json"), "custom tracks: --validate-track=NAME finds the player's folder before a folder of that name in the working directory");
            }
            finally { try { Directory.Delete(stray, true); } catch { } }
        }
        finally { try { Directory.Delete(loadDir, true); } catch { } }

        // ---- the CUSTOM TRACKS card's texts: keys lines fit the narrowest screen, text is cut to what the font draws
        {
            var hud = new CCCPRacing.UI.Hud();
            foreach (bool pad in new[] { false, true })
                foreach (bool emptyList in new[] { false, true })
                {
                    hud.Pad = pad;
                    string keys = hud.CustomKeysLine(emptyList);
                    Check(CCCPRacing.UI.PixelFont.Measure(keys) <= 408 && keys.All(CCCPRacing.UI.PixelFont.CanDraw), $"custom tracks: the card's keys line fits and is drawable ({(pad ? "pad" : "keys")}, {(emptyList ? "empty list" : "list")}: '{keys}')");
                }
            hud.Free();
            string cut = CCCPRacing.Race.RaceScene.CustomText("error: not valid json at [line 1] \"x\" ~ ^ and a long long tail that never ends", 42);
            Check(cut.Length == 42 && cut.EndsWith("..") && cut.All(CCCPRacing.UI.PixelFont.CanDraw) && cut == cut.ToUpperInvariant(), $"custom tracks: a message for the card is upper case, drawable and cut to 42 with two dots ('{cut}')");
            Check(CCCPRacing.Race.RaceScene.CustomText("short", 42) == "SHORT" && CCCPRacing.Race.RaceScene.CustomText(null, 42) == "", "custom tracks: a short message is only upper-cased, null is empty");
        }

        // ---- the command line check: the exit code
        Check(TrackCheck.Run(ProjectSettings.GlobalizePath("res://" + OvalJson)) == 0 && TrackCheck.Run("/tmp/no-such-track-file-xyz") == 1, "custom tracks: --validate-track exits 0 for a good track and 1 for a missing one");
        string badFile = Path.Combine(Path.GetTempPath(), $"cccp-bad-track-{System.Environment.ProcessId}.json");
        File.WriteAllText(badFile, "{ \"name\": \"X\" }");
        try { Check(TrackCheck.Run(badFile) == 1, "custom tracks: --validate-track exits 1 for a track with errors"); } finally { File.Delete(badFile); }
        GD.Print($"Custom track tests: {(_failures == 0 ? "all ok" : _failures + " FAIL")}");
    }

    /// <summary>--validate-track in a real game process: the output, the RESULT line and the exit code (the hook in RaceScene._Ready, the quit), for a good track, a track with errors and one with a null list.</summary>
    void CustomTrackCliTest()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"cccp-custom-cli-{System.Environment.ProcessId}");
        try
        {
            Directory.CreateDirectory(dir);
            string good = Path.Combine(dir, "good.json"), errors = Path.Combine(dir, "errors.json"), nulled = Path.Combine(dir, "nulled.json");
            File.WriteAllText(good, OvalText());
            File.WriteAllText(errors, WithField(OvalText(), "propz", "[]"));
            File.WriteAllText(nulled, PropsNull());
            string locked = Path.Combine(dir, "locked.json");
            File.WriteAllText(locked, OvalText()); File.SetUnixFileMode(locked, UnixFileMode.None);      // (a file the check cannot read: it must still end with a RESULT line and exit 1)
            foreach (var (file, wantExit, wantResult, wantLine) in new[] { (good, 0, "RESULT: OK", "OK: 'OVAL EXAMPLE'"), (errors, 1, "RESULT: FAILED", "unknown field (did you mean 'props'?)"), (nulled, 1, "RESULT: FAILED", "must be a list"), (locked, 1, "RESULT: FAILED", "could not be completed") })
            {
                var psi = new System.Diagnostics.ProcessStartInfo(OS.GetExecutablePath()) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var a in new[] { "--headless", "--path", TapeProjectDir, "--", "--validate-track=" + file }) psi.ArgumentList.Add(a);
                psi.Environment["XDG_DATA_HOME"] = dir + ".xdg";
                using var p = System.Diagnostics.Process.Start(psi);
                var outTask = p.StandardOutput.ReadToEndAsync(); var errTask = p.StandardError.ReadToEndAsync();
                bool done = p.WaitForExit(90000); if (!done) { try { p.Kill(true); } catch { } }
                string text = outTask.Result + errTask.Result;
                string name = Path.GetFileName(file);
                Check(done && p.ExitCode == wantExit && text.Contains(wantResult) && text.Contains(wantLine) && !text.Contains("NullReferenceException"), $"custom tracks: the command line check of {name} exits {wantExit} and says '{wantResult}' ({(done ? "exit " + p.ExitCode : "timed out")}, {(text.Contains("NullReferenceException") ? "an exception" : "no exception")})");
            }
        }
        finally { foreach (var d in new[] { dir, dir + ".xdg" }) try { Directory.Delete(d, true); } catch { } }
    }

    /// <summary>A track named on the command line that validates but fails to build: the game falls back to the first track ONCE, and does not rebuild the failing one in a loop (the override of --track= is held off
    /// until the list has been shown). The child runs for a while, then is stopped; its log must name the failure once.</summary>
    void CustomTrackFailureLoopTest()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"cccp-custom-loop-{System.Environment.ProcessId}");
        string tracks = dir + ".xdg/godot/app_userdata/CCCP Racing/tracks/fails";
        try
        {
            Directory.CreateDirectory(tracks); Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(tracks, "track.json"), OvalText());
            var psi = new System.Diagnostics.ProcessStartInfo(OS.GetExecutablePath()) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in new[] { "--headless", "--fixed-fps", "60", "--path", TapeProjectDir, "--", "--track=custom-fails", "--vw-fail-build=custom-fails", "--no-intro", "--no-bundled-title", "--data-dir=" + dir }) psi.ArgumentList.Add(a);
            psi.Environment["XDG_DATA_HOME"] = dir + ".xdg";
            using var p = System.Diagnostics.Process.Start(psi);
            var outTask = p.StandardOutput.ReadToEndAsync(); var errTask = p.StandardError.ReadToEndAsync();
            bool exited = p.WaitForExit(25000); if (!exited) { try { p.Kill(true); } catch { } p.WaitForExit(5000); }
            string text = outTask.Result + errTask.Result;
            int failures = text.Split('\n').Count(l => l.Contains("Custom track 'custom-fails' could not be loaded"));
            Check(failures == 1 && !exited, $"custom tracks: a track from the command line that fails to build is given up once, not rebuilt in a loop (failures logged {failures}, still running after 25 s: {!exited})");
            Check(!text.Contains("NullReferenceException") && !text.Contains("Unhandled exception"), "custom tracks: the failed build leaves no unhandled exception");
        }
        finally { foreach (var d in new[] { dir, dir + ".xdg" }) try { Directory.Delete(d, true); } catch { } }
    }

    /// <summary>Records of a custom track: a lap is recorded under the custom id; when the track file is edited (its shape changes) the old records are ARCHIVED (renamed *_v02), never deleted, and the
    /// new shape starts its own. Two children drive a lap each on the oval, the second after the file was edited.</summary>
    void CustomTrackRecordsTest()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"cccp-custom-rec-{System.Environment.ProcessId}");
        string tracks = dir + ".xdg/godot/app_userdata/CCCP Racing/tracks/oval";
        try
        {
            Directory.CreateDirectory(tracks); Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(tracks, "track.json"), OvalText());
            string[] args = { "--track=custom-oval", "--start", "--training", "--autodrive", "--tape-quit-laps=1" };
            var c1 = TapeGame(dir, args, 120);
            var before = Directory.GetFiles(dir).Select(Path.GetFileName).Where(f => f.Contains("custom-oval")).OrderBy(f => f).ToArray();
            Check(c1.Exit == 0 && !c1.TimedOut && before.Any(f => f.StartsWith("ghost_custom-oval")) && before.Any(f => f.StartsWith("track_custom-oval") && f.EndsWith(".sig")), $"custom tracks: a recorded lap leaves the ghost and the signature under the custom id ({string.Join(", ", before)})");
            string sigFile = Path.Combine(dir, "track_custom-oval.sig");
            if (!File.Exists(sigFile)) { Check(false, "custom tracks: the first lap left no signature file, so the records test cannot go on"); return; }
            string sig1 = File.ReadAllText(sigFile).Trim();
            var oldBytes = before.Where(f => f.StartsWith("ghost_") || f.StartsWith("tape_")).ToDictionary(f => f, f => File.ReadAllBytes(Path.Combine(dir, f)));
            string edited = OvalText().Replace("\"hillAmp\": 3.0", "\"hillAmp\": 4.0");
            Check(edited != OvalText() && oldBytes.Count >= 2, "custom tracks: the records test really edits the shape and has the first lap's files to compare");
            File.WriteAllText(Path.Combine(tracks, "track.json"), edited);      // the shape changes
            var c2 = TapeGame(dir, args, 120);
            var after = Directory.GetFiles(dir).Select(Path.GetFileName).Where(f => f.Contains("custom-oval")).OrderBy(f => f).ToArray();
            var ghosts = after.Where(f => f.StartsWith("ghost_custom-oval")).ToArray();
            Check(c2.Exit == 0 && !c2.TimedOut && ghosts.Length == 2 && ghosts.Count(f => f.Contains("_v02")) == 1, $"custom tracks: after the track is edited the old ghost is archived (_v02) and the new shape has its own ({string.Join(", ", ghosts)})");
            string sig2 = File.Exists(sigFile) ? File.ReadAllText(sigFile).Trim() : "(no signature file)";
            // nothing was lost: every ghost and tape of the first lap is still there, byte for byte, under its archive name
            bool kept = oldBytes.All(kv => after.Any(a => a.Contains("_v02") && a.Replace("_v02", "") == kv.Key && File.ReadAllBytes(Path.Combine(dir, a)).AsSpan().SequenceEqual(kv.Value)));
            Check(sig1 != sig2 && after.Any(f => f.StartsWith("track_custom-oval") && f.Contains("_v02")) && kept, $"custom tracks: the signature changed with the edit and the first lap's ghost and tape are kept byte for byte as *_v02 ({sig1} -> {sig2}, kept {kept})");
        }
        finally { foreach (var d in new[] { dir, dir + ".xdg" }) try { Directory.Delete(d, true); } catch { } }
    }

    /// <summary>The real game on the example track: it is put in the user data of a child process, which drives a lap with the autopilot (headless, fixed fps as the other scene tests).</summary>
    void CustomTrackDriveTest()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"cccp-custom-drive-{System.Environment.ProcessId}");
        string tracks = dir + ".xdg/godot/app_userdata/CCCP Racing/tracks/oval";
        try
        {
            Directory.CreateDirectory(tracks);
            File.WriteAllText(Path.Combine(tracks, "track.json"), OvalText());
            foreach (var mode in new[] { "--start", "--training" })      // (the rally start and the free laps the guide names: --start --training)
            {
                var c = TapeGame(dir, new[] { "--track=custom-oval", "--start", "--autodrive", "--no-record", "--tape-quit-laps=1" }.Concat(mode == "--training" ? new[] { "--training" } : Array.Empty<string>()).ToArray(), 120);
                Check(c.Exit == 0 && !c.TimedOut, $"custom tracks: the game drives a lap on the custom oval from the user data ({mode}: exit {c.Exit}, {c.Seconds:0} s, timed out {c.TimedOut})");
                Check(c.Lines.Any(l => l.Contains("laps 1")) && !c.Lines.Any(l => l.StartsWith("ERROR") || l.Contains("Unknown track")), $"custom tracks: the lap was completed and the log has no errors ({mode}): " + string.Join(" | ", c.Lines.Where(l => l.StartsWith("ERROR")).Take(2)));
            }
        }
        finally { foreach (var d in new[] { dir, dir + ".xdg" }) try { Directory.Delete(d, true); } catch { } }
    }
}
