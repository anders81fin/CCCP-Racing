using System;
using System.Collections.Generic;
using System.Linq;

namespace CCCPRacing.Net;

/// <summary>
/// The secret ballot for the power of a LAN race: every comrade votes a power (1-9 as in STAKHANOV
/// LAPS), the most votes wins, a tie goes to the lottery drum among the tied powers only. The host
/// draws; everything here is deterministic from the seed, so every screen spins the same drum.
/// </summary>
public static class LanBallot
{
    public static string Track(IEnumerable<string> votes,IReadOnlyList<string> options,int seed,out List<string> tied)
    {
        if(options.Count==0) throw new ArgumentException("no host tracks");
        var counts=options.ToDictionary(x=>x,_=>0);
        foreach(string vote in votes) counts[counts.ContainsKey(vote??"")?vote:options[0]]++;
        int max=counts.Values.Max();tied=options.Where(x=>counts[x]==max).ToList();
        return tied[Draw(tied.Count,unchecked((int)Mix((uint)seed^0x54524143u)))];
    }
    /// <summary>What a comrade who did not vote in time counts as.</summary>
    public const int NoVote = 100;
    public const float Step = 0.8f;          // s between two ballots shown
    public const float TallyHold = 1.4f;     // s the tally stays up before the drum or the result
    public const float DrumSpin = 3f, DrumHold = 1.2f;
    public const float AnnounceHold = 2.6f, GridHold = 2.6f;
    public const float Countdown = 3f;

    /// <summary>The powers with the most votes, lowest first (one: the winner; more: the drum). Counts per power.</summary>
    public static List<int> Tally(IEnumerable<int> votes, out SortedDictionary<int, int> counts)
    {
        counts = new SortedDictionary<int, int>();
        foreach (int v in votes) counts[v] = counts.TryGetValue(v, out int c) ? c + 1 : 1;
        if (counts.Count == 0) return new List<int> { NoVote };
        int max = counts.Values.Max();
        return counts.Where(kv => kv.Value == max).Select(kv => kv.Key).ToList();
    }

    /// <summary>The drum's pick among n tied options (index), equal chances, from the seed.</summary>
    public static int Draw(int n, int seed) => n <= 1 ? 0 : (int)(Mix((ulong)(uint)seed) % (ulong)n);

    /// <summary>Each rule is counted independently; ties use a separate deterministic draw.</summary>
    public static bool Rule(IEnumerable<bool> votes, int seed, int salt)
    {
        var tied = Tally(votes.Select(v => v ? 1 : 0), out _);
        return tied[Draw(tied.Count, unchecked((int)Mix((ulong)(uint)seed ^ (uint)salt)))] != 0;
    }

    /// <summary>Grid order drawn by lot: a Fisher-Yates shuffle of the ids from the seed.</summary>
    public static List<long> Shuffle(IEnumerable<long> ids, int seed)
    {
        var l = ids.ToList();
        ulong s = Mix((ulong)(uint)seed ^ 0x6772696400UL);
        for (int i = l.Count - 1; i > 0; i--)
        {
            s = Mix(s);
            int j = (int)(s % (ulong)(i + 1));
            (l[i], l[j]) = (l[j], l[i]);
        }
        return l;
    }

    /// <summary>
    /// The drum's reel position (in options) t seconds into the spin: several full turns, slowing
    /// (cubic ease-out) and stopping exactly on the winner after DrumSpin. Option shown: floor mod n.
    /// </summary>
    public static float DrumPosition(int n, int winner, int seed, float t)
    {
        if (n <= 1) return 0;
        int turns = Math.Max(3, 12 / n) + (int)(Mix((ulong)(uint)seed ^ 0xd2u) % 3);
        float total = turns * n + winner;
        float x = Math.Clamp(t / DrumSpin, 0f, 1f);
        float k = 1f - x;
        return total * (1f - k * k * k);
    }

    /// <summary>The option the drum shows at reel position p.</summary>
    public static int DrumIndex(int n, float p) => n <= 1 ? 0 : ((int)MathF.Floor(p) % n + n) % n;

    /// <summary>When each part of the count starts, seconds from the opening of the box.</summary>
    public readonly struct Timeline
    {
        public readonly float Tally, Drum, Announce, Grid, End;

        public Timeline(int ballots, bool drum)
        {
            Tally = Step * (ballots + 1);
            Drum = Tally + TallyHold;
            Announce = Drum + (drum ? DrumSpin + DrumHold : 0f);
            Grid = Announce + AnnounceHold;
            End = Grid + GridHold;
        }
    }

    /// <summary>SplitMix64: a well-mixed 64-bit hash, the same on every machine.</summary>
    public static ulong Mix(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
