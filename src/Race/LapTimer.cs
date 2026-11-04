using System;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// Hundreds-of-checkpoints lap validation in the spirit of Rally-Sport 1.27: the centre line is
/// split into gates every few metres and they must be passed in order.
/// </summary>
public class LapTimer
{
    public const int GateStep = 20;    // metres between gates
    const float Tolerance = 25f;       // how far off the road still counts as on track

    readonly TrackData _t;
    readonly int _gateCount;
    int _shortcutGate = -1;

    public int GateCount => _gateCount;
    public bool Running { get; private set; }
    public float LapTime { get; private set; }
    public float LastLap { get; private set; } = -1;
    public float BestLap { get; set; } = -1;
    public int LapsDone { get; private set; }
    public int NextGate { get; private set; } = 1;
    public int Progress { get; private set; }
    /// <summary>The current lap was voided (rescue): it runs to the line but cannot count.</summary>
    public bool LapVoid { get; private set; }
    /// <summary>The last completed lap was void; LastLap still holds its time.</summary>
    public bool LastLapVoid { get; private set; }
    /// <summary>The current lap cannot be a record or ranked; set by the race (no run uses it now: each has its own records).</summary>
    public bool Unranked { get; set; }
    /// <summary>The last completed lap was neither void nor unranked: it may go on the leaderboard.</summary>
    public bool LastLapRanked { get; private set; }

    /// <summary>(lap time, is new record)</summary>
    public event Action<float, bool> LapCompleted;
    /// <summary>A void lap reached the line: (lap time). Not a record, BestLap untouched.</summary>
    public event Action<float> VoidLapCompleted;
    public event Action ShortcutDetected;

    public LapTimer(TrackData t)
    {
        _t = t;
        _gateCount = t.Center.Length / GateStep;
    }

    /// <summary>Starts a new run from lap 1 (keepLaps: a restart request, the laps done so far still count); BestLap is kept.</summary>
    public void Start(bool keepLaps = false)
    {
        _hasPrev = _followed = _lineCrossed = false;
        Running = true;
        if (!keepLaps) LapsDone = 0;
        LapTime = 0;
        NextGate = 1;
        _shortcutGate = -1;
        LapVoid = false;
    }

    public void Stop() => Running = false;
    /// <summary>A server rescue moved the car; the teleport itself cannot cross the finish line.</summary>
    public void ResetMovementHistory() {_hasPrev=false;_lineCrossed=false;}
    public void CopyNetwork(int laps,int gate,int progress,float time,float last,float best)
    {
        LapsDone=laps;NextGate=gate;Progress=progress;LapTime=time;LastLap=last;BestLap=best;
    }

    public void RestoreCompletedLaps(int laps) => LapsDone = Math.Max(0, laps);

    /// <summary>Voids the lap in progress; the clock keeps running and the next lap is valid again.</summary>
    public void Void()
    {
        if (Running) LapVoid = true;
    }

    public void AddPenalty(float seconds) => LapTime += seconds;

    /// <summary>
    /// The car was put down just short of the line (towed to the fuel depot): a void lap in
    /// progress ends at the next crossing instead of a lap later. Valid laps are never skipped.
    /// </summary>
    public void SkipToLine()
    {
        if (!Running || !LapVoid) return;
        NextGate = _gateCount;
        _shortcutGate = -1;
    }

    /// <summary>
    /// The car was put down at road metre m out on the lap (towed to a depot there): a void lap in
    /// progress goes on from there to the line. Valid laps are never moved.
    /// </summary>
    public void PutAt(int m)
    {
        if (!Running || !LapVoid) return;
        int n = _t.Center.Length;
        m = (m % n + n) % n;
        NextGate = Math.Clamp(m / GateStep + 1, 1, _gateCount);
        Progress = m;
        _followed = true;
        _hasPrev = false;
        _shortcutGate = -1;
    }

    /// <summary>
    /// The road metre nearest to pos, followed from the last one (Progress) so that a hairpin's other
    /// leg or a nearby stretch never takes it over; far from where it was (a rescue, a tow, a reset)
    /// the whole road is searched again.
    /// </summary>
    public int Follow(Vector2 pos, out float d)
    {
        int n = _t.Center.Length, best = -1;
        float bestD = float.MaxValue;
        if (_followed)
            for (int a = -25; a <= 40; a++)
            {
                int i = ((Progress + a) % n + n) % n;
                float q = _t.Center[i].DistanceSquaredTo(pos);
                if (q < bestD) { bestD = q; best = i; }
            }
        d = MathF.Sqrt(bestD);
        if (best < 0 || d > _t.HalfWidth[best] + Tolerance) best = _t.NearestCenter(pos, out d);
        return best;
    }

    bool _followed, _lineCrossed;     // (a jump of 30 m counts as crossing: a test or a reset over the line)
    Vector2 _prevPos;
    bool _hasPrev;

    /// <summary>Where the rescue puts the car: the stretch it was last on (its progress), or the road nearest to pos before a lap has started.</summary>
    public int RescueMetre(Vector2 pos) => _followed ? Progress : _t.NearestCenter(pos, out _);

    /// <summary>The car went from a to b across the start/finish line between its posts, the right way.</summary>
    bool CrossedLine(Vector2 a, Vector2 b)
    {
        Vector2 c = _t.Center[0], dir = _t.CenterDir(0), side = new(-dir.Y, dir.X);
        float sa = (a - c).Dot(dir), sb = (b - c).Dot(dir);
        if (!(sa < 0f && sb >= 0f)) return false;
        float f = sa / (sa - sb);
        float off = MathF.Abs((a.Lerp(b, f) - c).Dot(side));
        if (off <= _t.LineHalf) return true;
        if (off < _t.LineHalf + 40f) LineMissed?.Invoke();       // wide of the posts: it does not count
        return false;
    }

    /// <summary>The car passed the line wide of its posts on the lap's last stretch: the lap has not ended.</summary>
    public event Action LineMissed;

    public void Update(float dt, Vector2 pos)
    {
        if (!Running) return;
        LapTime += dt;
        Vector2 prev = _hasPrev ? _prevPos : pos;
        _prevPos = pos;
        _hasPrev = true;

        // The line itself: crossed between its posts on the lap's last stretch (outside them it does not count).
        if (NextGate >= _gateCount && (CrossedLine(prev, pos) || prev.DistanceTo(pos) > 30f)) _lineCrossed = true;
        int idx = Follow(pos, out float d);
        if (d > _t.HalfWidth[idx] + Tolerance) return;
        // Cut across country to another stretch of the road: the car is not on its course there.
        // Its progress stays where it left the road (the rescue puts it back there); it comes
        // back only on its own stretch, or a little behind it.
        if (_followed)
        {
            int n0 = _t.Center.Length, jump = ((idx - Progress) % n0 + n0) % n0;
            if (jump > 45 && jump < n0 - 60) return;
        }
        Progress = idx;
        _followed = true;

        int n = _t.Center.Length;
        int target = NextGate < _gateCount ? NextGate * GateStep : 0;
        int ahead = ((idx - target) % n + n) % n;

        // Missing one gate on a wide line is forgiven; skipping several is a shortcut.
        if (ahead <= GateStep * 2 + 4)
        {
            if (NextGate < _gateCount)
                NextGate = Math.Min(_gateCount, NextGate + 1 + ahead / GateStep);
            else if (_lineCrossed)
                CompleteLap(1 + ahead / GateStep);
        }
        else if (ahead < GateStep * 10 && _shortcutGate != NextGate)
        {
            _shortcutGate = NextGate;
            ShortcutDetected?.Invoke();
        }
    }

    void CompleteLap(int nextGate)
    {
        float time = LapTime;
        bool wasVoid = LapVoid;
        bool record = !wasVoid && !Unranked && (BestLap < 0 || time < BestLap);
        LastLap = time;
        LastLapVoid = wasVoid;
        LastLapRanked = !wasVoid && !Unranked;
        if (record) BestLap = time;
        LapsDone++;
        LapTime = 0;
        NextGate = nextGate;
        _shortcutGate = -1;
        _lineCrossed = false;
        LapVoid = false;
        if (wasVoid) VoidLapCompleted?.Invoke(time);
        else LapCompleted?.Invoke(time, record);
    }

    /// <summary>Test: completes the lap in progress now with this time, as if the line was crossed.</summary>
    public void CompleteLapForTest(float time)
    {
        if (!Running) return;
        LapTime = time;
        CompleteLap(NextGate);
    }

    public static string Format(float t)
    {
        if (t < 0) return "--:--.--";
        int cs = (int)MathF.Round(t * 100);
        return $"{cs / 6000}:{cs / 100 % 60:00}.{cs % 100:00}";
    }
}
