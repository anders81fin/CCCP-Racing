using System;

namespace CCCPRacing.Race;

/// <summary>
/// The liaison officer's norms: a ladder of lap times, each with a window of five laps. A ranked
/// lap strictly under the current norm moves the driver up one step, however fast it was (the
/// norms are beaten one at a time), and the next norm gets five laps plus the ones the driver had
/// left over (at most MaxLaps in all); beating the last step, the
/// Stakhanovite norm, fulfils the plan. Every lap that does not beat the norm uses one lap of the
/// window, void laps included; a window used up means the gulag. Wrecks, seized engines, restart
/// requests and empty-tank tows take laps from the window too (Penalize). Every norm has the
/// climate's allowance on top (Climate; none by default). Training laps and the title demo have
/// no norms (disabled: every lap is Continue).
/// </summary>
public class NormPlan
{
    /// <summary>The bonus track's original ladder, the default for a track without its own norms.</summary>
    public static readonly float[] DefaultLadder = { 75f, 72.5f, 70f, 67.5f, 65f, 62.5f, 60f, 57.5f, 55f, 52.5f };
    /// <summary>The ladder in seconds of the track built (UseTrack) and of the fuel (UseFuel); the last step is the Stakhanovite norm.</summary>
    public static float[] Ladder { get; private set; } = DefaultLadder;
    /// <summary>The track's petrol ladder, before the fuel's factor.</summary>
    public static float[] BaseLadder { get; private set; } = DefaultLadder;
    /// <summary>The fuel's lap-time factor now (1: petrol): the norms and the climate's allowances are this much longer or shorter.</summary>
    public static float Factor { get; private set; } = 1f;
    static Track.NormsDef _def;
    /// <summary>The track's climate-allowance scale (tracks/ID.json norms "climate"; 1: the first track's seconds).</summary>
    public static float ClimateScale => _def?.Climate is > 0f and var c ? c : 1f;
    /// <summary>When a track says nothing: the lap-time factors of diesel and rocket fuel (the diesel's own engine and 200 % power for rocket fuel; the autopilot's laps gave 1.007-1.014 and 0.98-0.99 on the four tracks with the diesel engine of 2 November, 1.02-1.05 with the old half-power diesel and 0.96-0.98 before: the human pace gets more from the pickup).</summary>
    static readonly float[] DefaultFactors = { 1.02f, 1f, 0.97f, 0.95f };

    /// <summary>The fuel's factor on a track (diesel's and rocket fuel's are the track's own when it has them).</summary>
    public static float FactorFor(Track.NormsDef def, Race.FuelGrade fuel) => fuel switch
    {
        Race.FuelGrade.Diesel => def?.Diesel is > 0f and var d ? d : DefaultFactors[0],
        Race.FuelGrade.Rocket => def?.Rocket is > 0f and var r ? r : DefaultFactors[2],
        Race.FuelGrade.Sputnik => def?.Sputnik is > 0f and var sp ? sp : DefaultFactors[3],
        _ => 1f,
    };

    /// <summary>Seconds each better fuel takes off every norm when a track has only a diesel ladder: DIESEL is the reference, PETROL 1 s tighter, JET FUEL 2 s, SPUTNIK 3 s.</summary>
    public const float FuelStep = 1f;

    /// <summary>
    /// A track's ladder for a fuel: the fuel's own ladder when the track states one (tracks/ID.json: "ladder" is petrol's, then dieselLadder, rocketLadder, sputnikLadder; hand-tuned,
    /// each norm in the same ratio to the next lower fuel's as before the 8 November retune); a track with only a diesel ladder gets one second less per fuel step; a track
    /// without a diesel ladder falls back to the factors on the petrol ladder.
    /// </summary>
    public static float[] LadderFor(Track.NormsDef def, Race.FuelGrade fuel)
    {
        float[] baseLadder = def?.Ladder is { Length: > 1 } l ? l : DefaultLadder;
        float[] own = fuel switch { Race.FuelGrade.Diesel => def?.DieselLadder, Race.FuelGrade.Rocket => def?.RocketLadder, Race.FuelGrade.Sputnik => def?.SputnikLadder, _ => baseLadder };
        if (own != null && own.Length == baseLadder.Length && (fuel == Race.FuelGrade.Petrol ? def?.DieselLadder != null : true)) return own;
        if (def?.DieselLadder is { } diesel && diesel.Length == baseLadder.Length)
            return Array.ConvertAll(diesel, t => t - (int)fuel * FuelStep);
        float f = FactorFor(def, fuel);
        return f == 1f ? baseLadder : Array.ConvertAll(baseLadder, t => MathF.Round(t * f * 2f) / 2f);
    }

    /// <summary>The track built, at this fuel's pace (the norms and the climate's allowances).</summary>
    public static void UseFuel(Race.FuelGrade fuel)
    {
        Factor = FactorFor(_def, fuel);
        Ladder = LadderFor(_def, fuel);
    }
    /// <summary>Norms to beat for a bronze and a silver medal; gold is the whole plan.</summary>
    public static int BronzeAt { get; private set; } = 4;
    public static int SilverAt { get; private set; } = 7;

    public enum Medal { None, Bronze, Silver, Gold }

    /// <summary>The track's own norms and medals (tracks/ID.json "norms"); null: the default ladder.</summary>
    public static void UseTrack(Track.NormsDef def)
    {
        _def = def;
        BaseLadder = Ladder = def?.Ladder is { Length: > 1 } l ? l : DefaultLadder;
        Factor = 1f;
        BronzeAt = Math.Clamp(def?.Bronze ?? 4, 1, Ladder.Length - 1);
        SilverAt = Math.Clamp(def?.Silver ?? 7, BronzeAt, Ladder.Length - 1);
    }

    /// <summary>The track's norms as they were (StaticsSnapshot).</summary>
    public sealed class Statics
    {
        internal float[] Ladder, BaseLadder;
        internal float Factor;
        internal Track.NormsDef Def;
        internal int Bronze, Silver;
    }

    /// <summary>
    /// The norms of the track built, fuel included. Every world build sets them (UseTrack), so a session owner that lets a second world build
    /// (the replay viewer) takes this first and calls RestoreStatics once that world has left the tree.
    /// </summary>
    public static Statics StaticsSnapshot() => new() { Ladder = Ladder, BaseLadder = BaseLadder, Factor = Factor, Def = _def, Bronze = BronzeAt, Silver = SilverAt };

    public static void RestoreStatics(Statics s)
    {
        Ladder = s.Ladder; BaseLadder = s.BaseLadder; Factor = s.Factor; _def = s.Def; BronzeAt = s.Bronze; SilverAt = s.Silver;
    }

    /// <summary>The medal for this many norms beaten (gold: all of them, the plan fulfilled).</summary>
    public static Medal MedalFor(int beaten) =>
        beaten >= Ladder.Length ? Medal.Gold : beaten >= SilverAt ? Medal.Silver : beaten >= BronzeAt ? Medal.Bronze : Medal.None;
    public const int WindowLaps = 5;
    /// <summary>The most laps a norm can have: its five and the ones saved on the norms before it.</summary>
    public const int MaxLaps = 10;

    public enum Outcome { Continue, NewNorm, Success, OutOfLaps }

    public float[] PassedTimes { get; private set; } = Array.Empty<float>();

    public float[] PassedTargets { get; private set; } = Array.Empty<float>();

    public bool Enabled { get; private set; }
    /// <summary>The current norm, 0-based into Ladder.</summary>
    public int Step { get; private set; }
    /// <summary>The step the run started at (tests start it higher).</summary>
    public int StartStep { get; private set; }
    /// <summary>Laps left in the current norm's window (the lap being driven included).</summary>
    public int Left { get; private set; } = WindowLaps;
    /// <summary>Laps completed in the run (void ones included).</summary>
    public int Used { get; private set; }
    /// <summary>Steps the last NewNorm jumped (1: the next step, more: norm exceeded).</summary>
    /// <summary>The plan was fulfilled or a window ran out: later laps change nothing.</summary>
    public bool Finished { get; private set; }
    public bool Fulfilled { get; private set; }
    /// <summary>The climate's allowance in seconds on top of every norm (null: none).</summary>
    public Func<float> Climate;

    public static int Steps => Ladder.Length;
    public float Allowance => Climate?.Invoke() ?? 0f;
    /// <summary>The ladder time of the current norm, without the allowance.</summary>
    public float LadderTime => Ladder[Step];
    /// <summary>The effective norm: a ranked lap strictly under this moves the driver up.</summary>
    public float Norm => LadderTime + Allowance;
    /// <summary>The lap being driven is the window's last.</summary>
    public bool OnLastLap => Enabled && !Finished && Left == 1;

    /// <summary>A new run: startStep's norm (0-based) with a full window, or only lapsLeft of it (tests).</summary>
    public void Reset(bool enabled, int startStep = 0, int lapsLeft = -1)
    {
        Enabled = enabled;
        Step = StartStep = Math.Clamp(startStep, 0, Ladder.Length - 1);
        Left = lapsLeft > 0 ? lapsLeft : WindowLaps;
        PassedTimes = new float[Steps];
        PassedTargets = new float[Steps];
        Used = 0;
        Finished = Fulfilled = false;
    }

    /// <summary>Restore an unfinished, validated checkpoint without replenishing its lap budget.</summary>
    public void Restore(int step, int left, int used, float[] passedTimes = null, float[] passedTargets = null)
    {
        if (step < 0 || step >= Steps || left < 1 || left > MaxLaps || used < 0)
            throw new ArgumentOutOfRangeException(nameof(step));
        Reset(true, step, left);
        Used = used;
        for (int i = 0; i < step && i < (passedTargets?.Length ?? 0); i++)
            if (float.IsFinite(passedTargets[i]) && passedTargets[i] > 0) PassedTargets[i] = passedTargets[i];
        for (int i = 0; i < step && i < (passedTimes?.Length ?? 0); i++)
            if (float.IsFinite(passedTimes[i]) && passedTimes[i] > 0) PassedTimes[i] = passedTimes[i];
    }

    /// <summary>
    /// A wreck, a restart request or a tow: the window loses this many laps. True when that leaves
    /// no lap for the one being driven: the window has run out.
    /// </summary>
    public bool Penalize(int laps)
    {
        if (!Enabled || Finished) return false;
        Left = Math.Max(0, Left - laps);
        if (Left >= 1) return false;
        Finished = true;
        return true;
    }

    /// <summary>
    /// A lap reached the line. ranked: neither void nor unranked. A lap under the norm climbs one
    /// step (Success past the last), any other uses up a lap.
    /// </summary>
    public Outcome OnLap(float time, bool ranked)
    {
        if (!Enabled || Finished) return Outcome.Continue;
        Used++;
        float allow = Allowance;
        if (ranked && time > 0 && time < Ladder[Step] + allow)
        {
            PassedTimes[Step] = time;
            PassedTargets[Step] = Ladder[Step] + allow;
            if (Step + 1 >= Ladder.Length)
            {
                Finished = Fulfilled = true;
                return Outcome.Success;
            }
            Step++;
            Left = Math.Min(MaxLaps, WindowLaps + Left - 1);     // the laps saved come along, up to MaxLaps
            return Outcome.NewNorm;
        }
        if (--Left > 0) return Outcome.Continue;
        Left = 0;
        Finished = true;
        return Outcome.OutOfLaps;
    }
}
