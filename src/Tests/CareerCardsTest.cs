using System;
using System.Collections;
using System.Reflection;
using CCCPRacing.Race;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Tests;

/// <summary>Run the real rally card flow against isolated career files; never touches player saves.</summary>
public partial class CareerCardsTest : Node
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    int _frames, _failures;
    object Read(RaceScene race, string name) => typeof(RaceScene).GetField(name, Private).GetValue(race);
    void Write(RaceScene race, string name, object value) => typeof(RaceScene).GetField(name, Private).SetValue(race, value);
    object Call(RaceScene race, string name, params object[] args) => typeof(RaceScene).GetMethod(name, Private).Invoke(race, args);
    void Check(bool ok, string message)
    {
        GD.Print($"[{(ok ? "ok" : "FAIL")}] {message}");
        if (!ok) _failures++;
    }
    RaceScene Find(Node node)
    {
        if (node is RaceScene race) return race;
        foreach (var child in node.GetChildren()) { var found = Find(child); if (found != null) return found; }
        return null;
    }
    public override void _Ready() => AddChild(GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate());
    public override void _Process(double delta)
    {
        if (++_frames < 4) return;
        var race = Find(this);
        if (race == null) { if (_frames > 120) GetTree().Quit(1); return; }
        SetProcess(false);
        race.ProcessMode = ProcessModeEnum.Disabled;
        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cccp-career-cards-" + Guid.NewGuid().ToString("N")) + "/";
        string one = root + "file1/", two = root + "file2/";
        DirAccess.MakeDirRecursiveAbsolute(one); DirAccess.MakeDirRecursiveAbsolute(two);
        var memory = new TutorialFile(one);
        Write(race, "_tutorial", memory);
        Write(race, "_countdown", -2f);
        Write(race, "_state", Enum.Parse(Read(race, "_state").GetType(), "Racing"));
        Call(race, "ResetCards");
        void Queue(string kind) => Call(race, "QueueCard", kind, kind.ToUpperInvariant(), Hud.Gold, "SHORT COMMENT", "SHORT HINT", new[] { "FIRST EXPLANATION" });
        foreach (string kind in new[] { "fuel", "engine", "headlights", "norm_beaten", "norm_missed", "last_chance", "revenge_jet", "revenge_tanks", "gai_chase", "gai_fine" })
        {
            string key = (string)typeof(RaceScene).GetMethod("CardKey", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { kind });
            Queue(kind);
            Check(!memory.SeenKey(key), kind + ": queuing does not consume the explanation");
            Call(race, "CardFrame", 0.01f);
            Check(Read(race, "_stopCard") != null && Read(race, "_state").ToString() == "Paused" && new TutorialFile(one).SeenKey(key), kind + ": first showing pauses and persists its flag");
            Call(race, "CloseCard");
            // A reload of the same career must retain the flag, independently of session caches.
            Write(race, "_tutorial", new TutorialFile(one));
            Queue(kind); Call(race, "CardFrame", 0.01f);
            Check(Read(race, "_stopCard") == null && Read(race, "_state").ToString() == "Racing", kind + ": subsequent showing is a comment without pausing");
            Check(!new TutorialFile(two).SeenKey(key), kind + ": another KGB file has independent flags");
        }
        Write(race, "_tutorial", new TutorialFile(two));
        Queue("norm_beaten"); Call(race, "CardFrame", 0.01f);
        Check(Read(race, "_stopCard") != null, "new KGB file gets its own first explanation");
        Call(race, "CloseCard");
        Queue("cancelled"); Queue("cancelled");
        Check(((ICollection)Read(race, "_stopQueue")).Count == 1, "pending explanations of the same kind coalesce");
        Call(race, "ResetCards");
        Check(!new TutorialFile(two).SeenKey("card_cancelled"), "discarding a queued explanation leaves it unseen");
        var legacy = new TutorialFile(two);
        legacy.MarkKey("gai_chase_siberia"); Write(race, "_tutorial", legacy);
        Queue("gai_chase"); Call(race, "CardFrame", 0.01f);
        Check(Read(race, "_stopCard") == null && new TutorialFile(two).SeenKey("card_gai_chase"), "old per-track radar flag migrates to a career-wide flag");
        GD.Print($"Career card tests: {_failures} failures; isolated files at {root}");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }
}
