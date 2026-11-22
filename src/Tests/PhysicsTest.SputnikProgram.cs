using System;
using System.Collections.Generic;
using System.Linq;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    static SputnikStanding Sp(NormPlan.Medal ilyich, NormPlan.Medal atom, NormPlan.Medal siberia, NormPlan.Medal poligon)
    {
        var m = new Dictionary<string, NormPlan.Medal> { ["ilyich"] = ilyich, ["atomgrad"] = atom, ["siberia"] = siberia, ["bonus"] = poligon };
        return SputnikStanding.From(t => m[t]);
    }

    /// <summary>The SPUTNIK test pilots' programme: the rules of the six recognitions (silver and gold counting, the order of progress, the three gold champion before and after Poligon), the main recognition of several at once, the
    /// campaign's separation of the fuels, and the saved "presented" marks and the painted name surviving a restart.</summary>
    void SputnikProgramTest()
    {
        const NormPlan.Medal N = NormPlan.Medal.None, B = NormPlan.Medal.Bronze, S = NormPlan.Medal.Silver, G = NormPlan.Medal.Gold;
        bool[] Set(SputnikStanding s) => s.Earned;
        string Names(SputnikStanding s) => string.Join(",", Enum.GetValues<SputnikAward>().Where(s.Has));
        Check(!Sp(S, S, B, S).Any, "bronze on the third track: nothing yet");
        Check(Names(Sp(S, S, S, N)) == "TestPilot", $"silver on the three main tracks: the test pilot's licence only ({Names(Sp(S, S, S, N))})");
        Check(Names(Sp(G, S, S, N)) == "TestPilot", "a gold counts as a silver");
        Check(Names(Sp(S, S, S, S)) == "TestPilot,Third", $"silver on all four: the third class ({Names(Sp(S, S, S, S))})");
        Check(Names(Sp(G, G, S, S)) == "TestPilot,Third,Second", $"two golds and four silvers: the second class ({Names(Sp(G, G, S, S))})");
        Check(Names(Sp(G, S, G, G)) == "TestPilot,Third,Second,First", $"three golds, the fourth on silver or more: the first class ({Names(Sp(G, S, G, G))})");
        var firstWithSilver = Sp(G, G, S, G);
        Check(firstWithSilver.Has(SputnikAward.First) && firstWithSilver.SilverOnlyTracks.SequenceEqual(new[] { "siberia" }), "the first class names the track still on silver");
        Check(firstWithSilver.GoldTracks.SequenceEqual(new[] { "ilyich", "atomgrad", "bonus" }), "the gold tracks are listed in track order");
        Check(Names(Sp(G, G, G, N)) == "TestPilot,Champion", $"three main golds before Poligon: the champion (and the licence) ({Names(Sp(G, G, G, N))})");
        Check(Names(Sp(G, G, G, S)) == "TestPilot,Third,Second,First,Champion", $"... and Poligon's silver then makes the first class ({Names(Sp(G, G, G, S))})");
        Check(Names(Sp(G, G, G, G)) == "TestPilot,Third,Second,First,Champion,Chief", $"four golds: all six ({Names(Sp(G, G, G, G))})");
        Check(Names(Sp(G, G, S, G)) == "TestPilot,Third,Second,First" && !Sp(G, G, S, G).Has(SputnikAward.Champion), "the champion needs the three MAIN golds: Poligon's gold is not one of them");
        Check(Names(Sp(S, S, S, G)) == "TestPilot,Third", "Poligon's gold alone does not make a second class");
        // the third gold from Poligon: the champion is still missing, the first class there
        Check(Names(Sp(G, G, S, G)).EndsWith("First"), "the third gold from Poligon makes the first class, and no champion");
        // main recognition
        Check(SputnikStanding.MainOf(new[] { SputnikAward.TestPilot, SputnikAward.Third, SputnikAward.Second }) == SputnikAward.Second, "of several, the greatest is the main one");
        Check(SputnikStanding.MainOf(Enum.GetValues<SputnikAward>()) == SputnikAward.Chief, "the chief test pilot outranks all");
        Check(SputnikStanding.MainOf(new[] { SputnikAward.Champion, SputnikAward.Third }) == SputnikAward.Champion, "the champion outranks a third class");
        Check(SputnikStanding.MainOf(Array.Empty<SputnikAward>()) == null, "nothing new: no main one");
        Check(Enum.GetValues<SputnikAward>().Select(SputnikStanding.Stars).Max() == 4 && SputnikStanding.Stars(SputnikAward.Third) == 1 && SputnikStanding.Stars(SputnikAward.Second) == 2 && SputnikStanding.Stars(SputnikAward.First) == 3, "the order's stars: 1, 2, 3 and the chief's 4");
        // the campaign: only the Sputnik medals count, the other fuels do not
        string dir = "user://sputnik-test/"; DirAccess.MakeDirRecursiveAbsolute(dir);
        if (FileAccess.FileExists(dir + "campaign.cfg")) DirAccess.RemoveAbsolute(dir + "campaign.cfg");
        var c = new Campaign(dir);
        foreach (var t in SputnikStanding.AllTracks) { c.Award(t, G, FuelGrade.Rocket); c.Award(t, G, FuelGrade.Petrol); c.Award(t, G, FuelGrade.Diesel); }
        Check(!c.SputnikStanding().Any, "gold on every track on the other three fuels: nothing for the Sputnik programme");
        foreach (var t in SputnikStanding.MainTracks) c.Award(t, S, FuelGrade.Sputnik);
        Check(c.SputnikStanding().Has(SputnikAward.TestPilot) && !c.SputnikStanding().Has(SputnikAward.Third), "Sputnik silvers on the main tracks: the licence");
        Check(!c.SputnikPresented(SputnikAward.TestPilot), "earned but not presented yet");
        c.PresentSputnik(SputnikAward.TestPilot); c.PaintSputnikName("A VERY LONG NAME OF A COMRADE");
        var again = new Campaign(dir);      // a restart
        Check(again.SputnikPresented(SputnikAward.TestPilot) && !again.SputnikPresented(SputnikAward.Third) && again.SputnikStanding().Has(SputnikAward.TestPilot), "the presented marks and the medals are there after a restart");
        Check(again.SputnikPaintedName == "A VERY LONG NAME OF A COMRADE", "the painted name is kept as it was painted");
        // a medal earned before the programme is recognised (the standing is read from the medals themselves)
        again.Award("bonus", G, FuelGrade.Sputnik);
        Check(again.SputnikStanding().Has(SputnikAward.Third) && !again.SputnikPresented(SputnikAward.Third), "a medal already on file counts and has not been presented");
        DirAccess.RemoveAbsolute(dir + "campaign.cfg");
        // the livery in a lap's metadata: it survives its json, an old json without it parses as none, and a text from outside (an imported or LAN lap) is cleaned
        var meta = new CCCPRacing.Replay.LapMeta { Id = "x", TrackId = "ilyich", Livery = "ANTTI|4" };
        Check(CCCPRacing.Replay.LapMeta.TryParse(meta.ToJson(), out var back, out _) && back.Livery == "ANTTI|4", "LapMeta.Livery survives its json");
        string oldJson = System.Text.RegularExpressions.Regex.Replace(meta.ToJson(), "\"[Ll]ivery\"\\s*:\\s*\"[^\"]*\",?", "");
        Check(!oldJson.ToLower().Contains("livery") && CCCPRacing.Replay.LapMeta.TryParse(oldJson, out var old, out _) && old.Livery == "", "metadata from before the livery parses, and has none");
        var ok = RaceScene.ParseLiveryForTest("ANTTI|4");
        Check(ok != null && ok.Value.Name == "ANTTI" && ok.Value.Stars == 4, "a livery text is parsed");
        Check(RaceScene.ParseLiveryForTest("") == null && RaceScene.ParseLiveryForTest("X|9") == null && RaceScene.ParseLiveryForTest("X|0") == null && RaceScene.ParseLiveryForTest("nobar") == null, "an empty text, bad stars and a text without stars are refused");
        var odd = RaceScene.ParseLiveryForTest("<b>EVIL</b>|4");
        Check(odd == null || (!odd.Value.Name.Contains('<') && !odd.Value.Name.Contains('>')), "odd characters from an imported lap are cleaned");
    }
}
