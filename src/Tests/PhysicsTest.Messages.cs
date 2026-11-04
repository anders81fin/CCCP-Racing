using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>The message rule (UI/HudMessages.cs): critical at once, important waits or is dropped when urgent, jokes only on a quiet screen.</summary>
    void MessageRuleTest()
    {
        GD.Print("Messages: critical, important, nice to have");
        var hud = new Hud();
        void Tick(float s) { for (float t = 0; t < s; t += 0.1f) hud.TickMessagesForTest(0.1f); }

        hud.Show("JOKE", null, 2f, null, Prio.Nice);
        Check(hud.CenterText == "JOKE", "a joke shows on a quiet screen");
        hud.Show("NEWS", null, 2f);
        Check(hud.CenterText == "NEWS", "important news pushes the joke off");
        hud.Show("JOKE 2", null, 2f, null, Prio.Nice);
        Check(hud.CenterText == "NEWS", "no joke over the news");
        Tick(3f);
        hud.Show("JOKE 3", null, 2f, null, Prio.Nice);
        Check(hud.CenterText == null, $"no joke until the screen has been quiet {Hud.QuietGap:0} s");
        Tick(Hud.QuietGap);
        hud.Show("JOKE 4", null, 2f, null, Prio.Nice);
        Check(hud.CenterText == "JOKE 4", "after a quiet spell, a joke again");

        Tick(3f);
        hud.Alert("ALERT", null, 3f, Hud.Red, Prio.Important);
        hud.Show("WAITS", null, 2f);
        hud.Show("WARNING", null, 2f, null, Prio.Important, urgent: true);
        Check(hud.TopText == "ALERT" && hud.CenterText == null, "important news waits while other news is up; an urgent warning is dropped");
        Tick(3.2f);
        Check(hud.CenterText == "WAITS", "the waiting news shows once the screen is free");
        hud.Show("CRITICAL", null, 2f, null, Prio.Critical);
        Check(hud.CenterText == "CRITICAL", "a critical message shows at once");

        hud.Frozen = true;
        Tick(5f);
        Check(hud.CenterText == "CRITICAL", "frozen (pause, a card): nothing expires unseen");
        hud.ShowAlways("COMMENTS OFF", null, 1.5f);
        Tick(2f);
        Check(hud.CenterText == null, "a key's confirmation made while paused runs out anyway");
        hud.Frozen = false;

        hud.SetComments(false, confirm: false);
        Tick(Hud.QuietGap + 1f);
        hud.Show("JOKE 5", null, 2f, null, Prio.Nice);
        Check(hud.CenterText == null, "comments off (TAB): no jokes");
        hud.Show("NEWS 2", null, 2f);
        Check(hud.CenterText == "NEWS 2", "comments off: the news still shows");
        hud.SetComments(true, confirm: false);
        Tick(Hud.QuietGap + 3f);
        hud.Danger = true;
        hud.Show("JOKE 6", null, 2f, null, Prio.Nice);
        Check(hud.CenterText == null, "no jokes in a dangerous moment");
        hud.Danger = false;
        hud.ReserveStartMessages(8.5f);
        hud.Show("EARLY NEWS");
        hud.Show("EARLY JOKE", prio: Prio.Nice);
        Check(hud.CenterText == null && hud.TopText == null, "start clears stale messages and drops unrelated commentary");
        hud.Alert("START NORM", null, 4.5f, Hud.Red);
        Check(hud.TopText == "START NORM", "opening norm still shows during the reserved start");
        hud.Show("REAL DANGER", prio: Prio.Critical);
        Check(hud.CenterText == "REAL DANGER", "real dangers can interrupt the start");
        hud.Frozen = true;
        Tick(10f);
        hud.Frozen = false;
        hud.ClearMessages();
        hud.Show("PAUSED START NEWS");
        Check(hud.CenterText == null, "pause preserves the start reservation");
        Tick(9f);
        hud.Show("LATER NEWS");
        Check(hud.CenterText == "LATER NEWS", "normal commentary resumes after the opening");
        hud.Free();
    }
}
