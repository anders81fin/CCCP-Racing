using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace CCCPRacing.Net;

/// <summary>
/// What a comrade has opened: the tracks (ids) and the fuels (one bit per <see cref="Race.FuelGrade"/>). Every comrade sends it with his hello; the host offers the secret ballot
/// exactly what the comrade with the most opened tracks and fuels has opened (a tie goes to the lowest slot, the host first).
/// </summary>
public sealed record LanUnlocks(IReadOnlyList<string> Tracks, int Fuels)
{
    /// <summary>Every fuel the game has.</summary>
    public const int AllFuels = (1 << Race.FuelGrades.Count) - 1;
    /// <summary>A fresh game, or a comrade whose hello said nothing: the first track and the diesel.</summary>
    public static readonly LanUnlocks Minimum = new(new[] { "ilyich" }, 1);
    /// <summary>Opened tracks and fuels together: the number that decides whose set the ballot offers.</summary>
    public int Score => Tracks.Count + BitOperations.PopCount((uint)(Fuels & AllFuels));
    public bool HasFuel(int grade) => grade is >= 0 and < Race.FuelGrades.Count && (Fuels >> grade & 1) != 0;
    /// <summary>The set with the highest score; the first of equals wins (the caller orders by slot).</summary>
    public static LanUnlocks Best(IEnumerable<LanUnlocks> all)
    {
        LanUnlocks best = null;
        foreach (var u in all) if (best == null || u.Score > best.Score) best = u;
        return best ?? Minimum;
    }
    public string WriteTracks() => string.Join(",", Tracks);
    /// <summary>A hello's tracks field: known ids only, at least the first track.</summary>
    public static LanUnlocks Read(string tracks, int fuels)
    {
        var list = (tracks ?? "").Split(',', System.StringSplitOptions.RemoveEmptyEntries).Where(Track.TrackIds.Known).Distinct().Take(16).ToList();
        if (list.Count == 0) list.Add("ilyich");
        int mask = fuels & AllFuels;
        return new LanUnlocks(list, mask == 0 ? 1 : mask);
    }
}
