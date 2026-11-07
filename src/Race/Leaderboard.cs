using System;
using System.Collections.Generic;
using System.Linq;
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
    public const int StakhanovTotalSize = StakhanovSize * (Training.LapsCount + 1); // four fuels and an unclassified legacy bucket
    public int Capacity { get; }
    public bool FuelPartitions { get; }
    public int StorageCapacity => FuelPartitions ? Capacity * (Training.LapsCount + 1) : Capacity;
    public Leaderboard(int capacity = Size, bool fuelPartitions = false) { Capacity = Math.Clamp(capacity, 1, StakhanovSize); FuelPartitions = fuelPartitions; }
    static int FuelBucket(int power) => Training.GradeOf(power) is FuelGrade g ? (int)g : Training.LapsCount;
    void Trim()
    {
        var counts = new int[Training.LapsCount + 1];
        _entries.RemoveAll(e => ++counts[FuelPartitions ? FuelBucket(e.Power) : 0] > Capacity);
    }
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
    public static Leaderboard Load(string path, float seedRecord = -1, int capacity = Size, bool fuelPartitions = false)
    {
        var b = new Leaderboard(capacity, fuelPartitions) { Path = path };
        var cfg = new ConfigFile();
        if (FileAccess.FileExists(path) && cfg.Load(path) == Error.Ok)
        {
            b.LastName = CleanName(cfg.GetValue("driver", "last_name", "").AsString());
            for (int i = 0; i < (fuelPartitions ? StakhanovTotalSize : StakhanovSize); i++)
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
            var ordered = b._entries.OrderBy(e => e.Time).ToArray(); // stable: keep the saved ordering of ties
            b._entries.Clear(); b._entries.AddRange(ordered);
            b.Trim();
        }
        else if (seedRecord > 0)
            b._entries.Add(new Entry(DefaultName, seedRecord, ""));
        return b;
    }

    public void Save()
    {
        if (string.IsNullOrEmpty(Path)) return;
        if (!BackupLegacy()) return;
        var cfg = new ConfigFile();
        if (FuelPartitions) cfg.SetValue("board", "fuel_top10", 1);
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

    // Preserve the exact pre-migration board once. Old rows without a known
    // fuel remain unclassified; no missing historic results are invented.
    bool BackupLegacy()
    {
        if (!FuelPartitions || !FileAccess.FileExists(Path)) return true;
        var old = new ConfigFile();
        if (old.Load(Path) == Error.Ok && old.HasSectionKey("board", "fuel_top10")) return true;
        string real = ProjectSettings.GlobalizePath(Path), backup = real + ".before-fuel-top10";
        try
        {
            if (!System.IO.File.Exists(backup)) System.IO.File.Copy(real, backup, false);
            return true;
        }
        catch (Exception e) { GD.PrintErr("Leaderboard migration backup: " + e.Message); return false; }
    }

    /// <summary>1-based place a lap of this time would take (ties go after equal times), 0 if off the board.</summary>
    public int RankFor(float time, int power = 0)
    {
        if (time <= 0 || !float.IsFinite(time)) return 0;
        int i = 0;
        foreach (var e in _entries)
            if ((!FuelPartitions || FuelBucket(e.Power) == FuelBucket(power)) && e.Time <= time) i++;
        return i < Capacity ? i + 1 : 0;
    }

    public bool Qualifies(float time, int power = 0) => RankFor(time, power) > 0;

    /// <summary>Puts a lap on the board and drops any entry beyond its capacity; returns its 1-based place, 0 if it did not make it.</summary>
    /// <param name="power">Engine power in whole percent (Stakhanov laps), 0 for none.</param>
    /// <param name="pad">A game pad drove during the lap.</param>
    /// <param name="night">A rally lap started at dusk or at night.</param>
    /// <param name="boost">OCTANE BOOSTER ran during the lap.</param>
    public int Insert(string name, float time, string date = null, int power = 0, bool pad = false, bool night = false, bool boost = false, string recordingId = null)
    {
        int rank = RankFor(time, power);
        if (rank == 0) return 0;
        name = CleanName(name);
        if (name.Length == 0) name = DefaultName;
        int index = _entries.FindIndex(e => e.Time > time);
        _entries.Insert(index < 0 ? _entries.Count : index, new Entry(name, time, date ?? Time.GetDateStringFromSystem(), Math.Max(0, power), pad, night, boost, CleanRecordingId(recordingId)));
        Trim();
        return rank;
    }

    // ---- LAN board sharing (RaceScene.LanBoards.cs, LanBoardStore): idempotent merge of another board's rows.

    /// <summary>
    /// The identity of a result across machines: the cleaned name, the time to the millisecond (the saved text may not
    /// round-trip the float bit for bit), the date and the lap's markers. The recording is not part of it, so the same lap
    /// with and without its recording is one row.
    /// </summary>
    public static string RowKey(string name, float time, string date, int power, bool pad, bool night, bool boost)
        => $"{CleanName(name)}|{(long)Math.Round(time * 1000.0)}|{date ?? ""}|{Math.Max(0, power)}|{(pad ? 1 : 0)}{(night ? 1 : 0)}{(boost ? 1 : 0)}";
    public static string RowKey(Entry e) => RowKey(e.Name, e.Time, e.Date, e.Power, e.Pad, e.Night, e.Boost);

    /// <summary>The entry with this row key, or null.</summary>
    public Entry Find(string key) => _entries.Find(e => RowKey(e) == key);

    /// <summary>
    /// Puts another machine's result on the board unless it is already there (same <see cref="RowKey"/>): its own date is kept
    /// and it never brings a recording link (the recording is attached once it is here and verified). Equal times use a deterministic row-key order, so every machine retains the same tied podium.
    /// The capacity drops the slowest as always. Returns the 1-based place, 0 when it did not make the board,
    /// -1 when it was already on it.
    /// </summary>
    public int MergeRow(string name, float time, string date, int power, bool pad, bool night, bool boost)
    {
        name = CleanName(name);
        if (name.Length == 0 || time <= 0 || !float.IsFinite(time)) return 0;
        // A shared top-N must choose the same tied rows regardless of which machine supplied them first.
        int Compare(Entry a,Entry b) { int c=a.Time.CompareTo(b.Time); return c!=0?c:StringComparer.Ordinal.Compare(RowKey(a),RowKey(b)); }
        _entries.Sort(Compare);
        if (Find(RowKey(name, time, date, power, pad, night, boost)) != null) return -1;
        var incoming=new Entry(name,time,date ?? "",Math.Max(0,power),pad,night,boost);
        _entries.Add(incoming); _entries.Sort(Compare);
        Trim();
        if (!_entries.Contains(incoming)) return 0;
        return _entries.Where(e => !FuelPartitions || FuelBucket(e.Power) == FuelBucket(power)).ToList().IndexOf(incoming) + 1;
    }

    /// <summary>Puts back exactly these rows (a merge whose save failed is undone in memory).</summary>
    internal void ReplaceEntries(IEnumerable<Entry> rows) { _entries.Clear(); _entries.AddRange(rows); }

    /// <summary>Links a verified recording to the row with this key when it has none. False when the row is gone or already linked (an existing link is never replaced).</summary>
    public bool AttachRecording(string key, string recordingId)
    {
        string id = CleanRecordingId(recordingId);
        if (id == null) return false;
        int i = _entries.FindIndex(e => RowKey(e) == key);
        if (i < 0 || _entries[i].RecordingId != null) return false;
        _entries[i] = _entries[i] with { RecordingId = id };
        return true;
    }

    /// <summary>
    /// Save through a temporary file that is read back before it replaces the board (the old file stays whole if anything fails).
    /// Same contents as <see cref="Save"/>. Returns the error text, null on success.
    /// </summary>
    public string SaveAtomic()
    {
        if (string.IsNullOrEmpty(Path)) return "no board file";
        if (!BackupLegacy()) return "could not preserve the original Stakhanov board";
        string tmp = Path + ".lan.tmp";
        string realTmp = ProjectSettings.GlobalizePath(tmp), real = ProjectSettings.GlobalizePath(Path);
        try
        {
            var cfg = new ConfigFile();
            if (FuelPartitions) cfg.SetValue("board", "fuel_top10", 1);
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
            string fail = FailSaveForTest; FailSaveForTest = null;
            if (fail == "before-write") throw new System.IO.IOException("test: " + fail);
            var err = cfg.Save(tmp);
            if (err != Error.Ok) throw new System.IO.IOException($"could not write {tmp}: {err}");
            var back = Load(tmp, -1, Capacity, FuelPartitions);
            // (as a set: the load's sort may order equal times differently)
            static string Row(Entry e) => RowKey(e) + "|" + CleanRecordingId(e.RecordingId);
            var want = _entries.ConvertAll(Row); var got = new List<Entry>(back.Entries).ConvertAll(Row);
            want.Sort(StringComparer.Ordinal); got.Sort(StringComparer.Ordinal);
            bool same = want.Count == got.Count && want.SequenceEqual(got);
            if (!same) throw new System.IO.IOException("the written board does not read back");
            if (fail == "before-replace") throw new System.IO.IOException("test: " + fail);
            if (System.IO.File.Exists(real)) System.IO.File.Replace(realTmp, real, null);
            else System.IO.File.Move(realTmp, real);
            return null;
        }
        catch (Exception e)
        {
            try { if (System.IO.File.Exists(realTmp)) System.IO.File.Delete(realTmp); } catch { }
            GD.PrintErr($"Leaderboard: could not save {Path}: {e.Message}");
            return e.Message;
        }
    }

    /// <summary>Test seam for <see cref="SaveAtomic"/>: "before-write" or "before-replace" makes the next save fail there.</summary>
    internal static string FailSaveForTest;

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
