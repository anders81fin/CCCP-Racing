using System;
using System.Collections.Generic;
using System.Linq;

namespace CCCPRacing.Race;

/// <summary>
/// The SPUTNIK test pilots' programme (the owner's order of 2026-11-22): six permanent recognitions for the best rally medals a profile has won on the SPUTNIK fuel, track by track (a gold counts as a silver).
/// The three plan tracks are ILYICH, ATOMGRAD and SIBERIA; the fourth is the bonus track POLIGON 17. Pure rules, no game state: the campaign's Sputnik medals go in, the recognitions earned come out.
/// </summary>
public enum SputnikAward { TestPilot, Third, Second, First, Champion, Chief }

public sealed class SputnikStanding
{
    public static readonly string[] MainTracks = { "ilyich", "atomgrad", "siberia" };
    public const string Poligon = "bonus";
    public static readonly string[] AllTracks = { "ilyich", "atomgrad", "siberia", Poligon };

    /// <summary>The Sputnik medal of each track in AllTracks order.</summary>
    public readonly NormPlan.Medal[] Medal = new NormPlan.Medal[4];
    public readonly bool[] Earned = new bool[6];

    public bool Has(SputnikAward a) => Earned[(int)a];
    public bool Any => Earned.Any(e => e);

    NormPlan.Medal Of(string track) => Medal[Array.IndexOf(AllTracks, track)];
    public bool SilverOn(string track) => Of(track) >= NormPlan.Medal.Silver;
    public bool GoldOn(string track) => Of(track) >= NormPlan.Medal.Gold;
    public int Golds => Medal.Count(m => m >= NormPlan.Medal.Gold);

    /// <summary>Tracks with a gold (their names go into the second class's and the first class's letters), in track order.</summary>
    public IEnumerable<string> GoldTracks => AllTracks.Where(GoldOn);
    /// <summary>Tracks still on silver (the first class's letter names it: what is left for the chief test pilot).</summary>
    public IEnumerable<string> SilverOnlyTracks => AllTracks.Where(t => Of(t) == NormPlan.Medal.Silver);

    public static SputnikStanding From(Func<string, NormPlan.Medal> sputnikMedal)
    {
        var s = new SputnikStanding();
        for (int i = 0; i < 4; i++) s.Medal[i] = sputnikMedal(AllTracks[i]);
        bool main3 = MainTracks.All(s.SilverOn), all4 = main3 && s.SilverOn(Poligon);
        s.Earned[(int)SputnikAward.TestPilot] = main3;
        s.Earned[(int)SputnikAward.Third] = all4;
        s.Earned[(int)SputnikAward.Second] = all4 && s.Golds >= 2;
        s.Earned[(int)SputnikAward.First] = all4 && s.Golds >= 3;
        s.Earned[(int)SputnikAward.Champion] = MainTracks.All(s.GoldOn);      // independent of Poligon and of the order
        s.Earned[(int)SputnikAward.Chief] = AllTracks.All(s.GoldOn);
        return s;
    }

    /// <summary>The recognition the ceremony is about when several are new at once: the chief test pilot, then the first class, the champion, the second and third class, the test pilot.</summary>
    public static readonly SputnikAward[] Rank = { SputnikAward.Chief, SputnikAward.First, SputnikAward.Champion, SputnikAward.Second, SputnikAward.Third, SputnikAward.TestPilot };

    public static SputnikAward? MainOf(IEnumerable<SputnikAward> awards)
    {
        var set = awards.ToHashSet();
        foreach (var a in Rank) if (set.Contains(a)) return a;
        return null;
    }

    public static string Title(SputnikAward a) => a switch
    {
        SputnikAward.TestPilot => "SPUTNIK TEST PILOT",
        SputnikAward.Third => "ORDER OF SPUTNIK - THIRD CLASS",
        SputnikAward.Second => "ORDER OF SPUTNIK - SECOND CLASS",
        SputnikAward.First => "ORDER OF SPUTNIK - FIRST CLASS",
        SputnikAward.Champion => "SPUTNIK RALLY CHAMPION",
        _ => "CHIEF TEST PILOT",
    };

    /// <summary>The gold stars of the order's ribbon: one for the third class, two for the second, three for the first, four for the chief test pilot (who wears them on the car).</summary>
    public static int Stars(SputnikAward a) => a switch { SputnikAward.Third => 1, SputnikAward.Second => 2, SputnikAward.First => 3, SputnikAward.Chief => 4, _ => 0 };
}
