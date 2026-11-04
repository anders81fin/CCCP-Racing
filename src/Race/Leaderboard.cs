using System;
using System.Collections.Generic;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The kolkhoz board of honour: the fastest ranked laps (three for rally, ten for Stakhanov) with their drivers' names, kept in
/// a small ConfigFile next to the ghost. A missing or broken file is simply an empty board.
/// Each run has its own board; a Stakhanov lap also keeps the power it was driven at. A lap driven
/// with a game pad says input="pad"; no input key is a keyboard lap (all laps from before the pads were).
/// A rally lap started at dusk or at night says night=true; no night key is a day lap. A rally lap
/// with OCTANE BOOSTER (the golden can's 350 %) running at any time says boost=true.
/// </summary>
public class Leaderboard
{
    public const int Size = 3;
    public const int StakhanovSize = 10;
    public int Capacity { get; }
    public Leaderboard(int capacity = Size) { Capacity = Math.Clamp(capacity, 1, StakhanovSize); }
    public const int MaxName = 12;
    public const string DefaultName = "TOVARISH";

    /// <summary>Power: engine power in whole percent for a Stakhanov lap, 0 for none (the rally).
    /// Pad: a game pad drove during the lap, else the keyboard. Night: the lap started at dusk or at night. Boost: OCTANE BOOSTER ran during it.</summary>
    public record Entry(string Name, float Time, string Date, int Power = 0, bool Pad = false, bool Night = false, bool Boost = false, string RecordingId = null);

    readonly List<Entry> _entries = new();
    public IReadOnlyList<Entry> Entries => _entries;
    /// <summary>The name typed last time, offered again in the name entry.</summary>
    public string LastName = "";
    public string Path;

    /// <summary>
    /// Loads the board. With no readable file, an existing record lap (seedRecord &gt; 0) is put
    /// on it as TOVARISH with no date, so the record made before the board existed is not lost.
    /// </summary>
    public static Leaderboard Load(string path, float seedRecord = -1, int capacity = Size)
    {
        var b = new Leaderboard(capacity) { Path = path };
        var cfg = new ConfigFile();
        if (FileAccess.FileExists(path) && cfg.Load(path) == Error.Ok)
        {
            b.LastName = CleanName(cfg.GetValue("driver", "last_name", "").AsString());
            for (int i = 0; i < StakhanovSize; i++)
            {
                string sec = $"entry_{i}";
                if (!cfg.HasSection(sec)) continue;
                float time = cfg.GetValue(sec, "time", -1.0).AsSingle();
                if (time <= 0 || !float.IsFinite(time)) continue;
                string name = CleanName(cfg.GetValue(sec, "name", DefaultName).AsString());
                b._entries.Add(new Entry(name.Length > 0 ? name : DefaultName, time, cfg.GetValue(sec, "date", "").AsString(),
                    Math.Max(0, cfg.GetValue(sec, "power", 0).AsInt32()), cfg.GetValue(sec, "input", "kb").AsString() == "pad",
                    cfg.GetValue(sec, "night", false).AsBool(), cfg.GetValue(sec, "boost", false).AsBool(), CleanRecordingId(cfg.GetValue(sec, "recording", "").AsString())));
            }
            b._entries.Sort((x, y) => x.Time.CompareTo(y.Time));
            if (b._entries.Count > b.Capacity) b._entries.RemoveRange(b.Capacity, b._entries.Count - b.Capacity);
        }
        else if (seedRecord > 0)
            b._entries.Add(new Entry(DefaultName, seedRecord, ""));
        return b;
    }

    public void Save()
    {
        if (string.IsNullOrEmpty(Path)) return;
        var cfg = new ConfigFile();
        cfg.SetValue("driver", "last_name", LastName);
        for (int i = 0; i < _entries.Count; i++)
        {
            cfg.SetValue($"entry_{i}", "name", _entries[i].Name);
            cfg.SetValue($"entry_{i}", "time", _entries[i].Time);
            cfg.SetValue($"entry_{i}", "date", _entries[i].Date);
            if (CleanRecordingId(_entries[i].RecordingId) is string recording) cfg.SetValue($"entry_{i}", "recording", recording);
            if (_entries[i].Power > 0) cfg.SetValue($"entry_{i}", "power", _entries[i].Power);
            if (_entries[i].Pad) cfg.SetValue($"entry_{i}", "input", "pad");
            if (_entries[i].Night) cfg.SetValue($"entry_{i}", "night", true);
            if (_entries[i].Boost) cfg.SetValue($"entry_{i}", "boost", true);
        }
        var err = cfg.Save(Path);
        if (err != Error.Ok) GD.PrintErr($"Leaderboard: could not save {Path}: {err}");
    }

    /// <summary>1-based place a lap of this time would take (ties go after equal times), 0 if off the board.</summary>
    public int RankFor(float time)
    {
        if (time <= 0 || !float.IsFinite(time)) return 0;
        int i = 0;
        while (i < _entries.Count && _entries[i].Time <= time) i++;
        return i < Capacity ? i + 1 : 0;
    }

    public bool Qualifies(float time) => RankFor(time) > 0;

    /// <summary>Puts a lap on the board and drops any entry beyond its capacity; returns its 1-based place, 0 if it did not make it.</summary>
    /// <param name="power">Engine power in whole percent (Stakhanov laps), 0 for none.</param>
    /// <param name="pad">A game pad drove during the lap.</param>
    /// <param name="night">A rally lap started at dusk or at night.</param>
    /// <param name="boost">OCTANE BOOSTER ran during the lap.</param>
    public int Insert(string name, float time, string date = null, int power = 0, bool pad = false, bool night = false, bool boost = false, string recordingId = null)
    {
        int rank = RankFor(time);
        if (rank == 0) return 0;
        name = CleanName(name);
        if (name.Length == 0) name = DefaultName;
        _entries.Insert(rank - 1, new Entry(name, time, date ?? Time.GetDateStringFromSystem(), Math.Max(0, power), pad, night, boost, CleanRecordingId(recordingId)));
        if (_entries.Count > Capacity) _entries.RemoveRange(Capacity, _entries.Count - Capacity);
        return rank;
    }

    public static string CleanRecordingId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 128 || !id.StartsWith("lap-", StringComparison.Ordinal)) return null;
        foreach (char c in id) if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')) return null;
        return id;
    }

    /// <summary>Letters (that the pixel font can draw), digits, space and hyphen.</summary>
    public static bool IsNameChar(char c) =>
        (c is ' ' or '-' || char.IsLetterOrDigit(c)) && PixelFont.CanDraw(c);

    /// <summary>Uppercase, allowed characters only, single spaces, at most MaxName long.</summary>
    public static string CleanName(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c0 in s ?? "")
        {
            char c = char.ToUpperInvariant(c0);
            if (!IsNameChar(c) || (c == ' ' && (sb.Length == 0 || sb[^1] == ' '))) continue;
            sb.Append(c);
        }
        string r = sb.ToString().Trim();
        return r.Length > MaxName ? r[..MaxName].TrimEnd() : r;
    }
}
