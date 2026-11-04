using System;
using System.Collections.Generic;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The co-driver's pace notes: the track's bends are found once from the centre line (how fast the heading turns over
/// 18 m), and as the car comes up to one it is announced once a lap: which way and how sharp, at about three
/// seconds' distance. Gentle bends are not called. Plain logic: RaceScene shows the text and plays the blips (PaceBlip).
/// </summary>
public class PaceNotes
{
    /// <summary>A bend: where it starts (road metre), +1 right / -1 left, and 2 MEDIUM, 3 SHARP, 4 HAIRPIN.</summary>
    public readonly record struct Bend(int Start, int Sign, int Severity);

    public static readonly string[] Names = { "", "EASY", "MEDIUM", "SHARP", "HAIRPIN" };

    const int Span = 18, MergeGap = 10;
    /// <summary>Radians turned over <see cref="Span"/> m: where a bend starts, and the limits of MEDIUM, SHARP and HAIRPIN.</summary>
    const float Start = 0.30f, Medium = 0.65f, Sharp = 1.0f, Hairpin = 1.4f;

    TrackData _for;
    List<Bend> _bends = new();
    bool[] _said = Array.Empty<bool>();
    int _idx = -1;

    public IReadOnlyList<Bend> Bends => _bends;

    /// <summary>The bends of a track, in road order (x right, z down on the map: a growing heading is a right turn).</summary>
    public static List<Bend> Find(TrackData t)
    {
        int n = t.Center.Length;
        var turn = new float[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 a = t.CenterDir(i), b = t.CenterDir((i + Span) % n);
            turn[i] = Mathf.Wrap(MathF.Atan2(b.Y, b.X) - MathF.Atan2(a.Y, a.X), -MathF.PI, MathF.PI);
        }
        var found = new List<Bend>();
        int k = 0;
        // Start somewhere calm so a bend is not cut in two by the loop's seam.
        int from = 0;
        for (int i = 0; i < n; i++) if (MathF.Abs(turn[i]) < 0.05f) { from = i; break; }
        while (k < n)
        {
            int i = (from + k) % n;
            if (MathF.Abs(turn[i]) < Start) { k++; continue; }
            int sign = Math.Sign(turn[i]), start = i, last = k;
            float peak = 0;
            // The bend goes on while the heading keeps turning the same way (short gaps are closed).
            for (int j = k; j < n && j - last <= MergeGap; j++)
            {
                float v = turn[(from + j) % n];
                if (Math.Sign(v) == sign && MathF.Abs(v) >= Start) { last = j; peak = Math.Max(peak, MathF.Abs(v)); }
            }
            int sev = peak >= Hairpin ? 4 : peak >= Sharp ? 3 : peak >= Medium ? 2 : 1;
            if (sev >= 2) found.Add(new Bend(start, sign, sev));
            k = last + 1;
        }
        found.Sort((a, b) => a.Start.CompareTo(b.Start));
        return found;
    }

    /// <summary>Per physics tick. True when a bend should be called now.</summary>
    public bool Tick(TrackData t, Vector2 pos, float speed, out Bend bend)
    {
        bend = default;
        if (t == null || t.Center.Length < 100) return false;
        if (t != _for)
        {
            _for = t;
            _bends = Find(t);
            _said = new bool[_bends.Count];
            _idx = -1;
            if (OS.HasEnvironment("PACEDBG"))
                foreach (var b in _bends) GD.Print($"pace: metre {b.Start} {(b.Sign > 0 ? "RIGHT" : "LEFT")} {Names[b.Severity]}");
        }
        int n = t.Center.Length;
        _idx = Locate(t, pos);
        float ahead = Math.Clamp(speed * 3.2f, 50f, 150f);
        for (int b = 0; b < _bends.Count; b++)
        {
            int dist = (_bends[b].Start - _idx + n) % n;
            if (dist > n / 2) { _said[b] = false; continue; }      // behind us: ready for the next lap
            if (_said[b] || dist > ahead || dist < 8) continue;
            _said[b] = true;
            bend = _bends[b];
            return true;
        }
        return false;
    }

    /// <summary>A restart or a new lap's line: every bend can be called again.</summary>
    public void Reset() { Array.Clear(_said); _idx = -1; }

    // The road metre: near the last one (cheap), the whole loop after a jump.
    int Locate(TrackData t, Vector2 pos)
    {
        int n = t.Center.Length;
        if (_idx >= 0)
        {
            int best = -1;
            float bestD = 30f * 30f;
            for (int d = -10; d <= 60; d++)
            {
                int i = (_idx + d + n) % n;
                float dd = t.Center[i].DistanceSquaredTo(pos);
                if (dd < bestD) { bestD = dd; best = i; }
            }
            if (best >= 0) return best;
        }
        return t.NearestCenter(pos, out _);
    }
}
