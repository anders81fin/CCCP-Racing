using System;
using System.Collections.Generic;
using CCCPRacing.Net;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>--lan-board-ui-probe: isolated lobby checks through production keys, pad routing and mouse hit regions.</summary>
public partial class RaceScene
{
    bool _lanBoardProbe;
    int _lanBoardProbeStep;
    double _lanBoardProbeAt;
    IEnumerator<int> _lanBoardProbeRun;

    void LanBoardsProbeTick()
    {
        if (!_lanBoardProbe || !_net.Active || !_net.InLobby || _net.State != LanSession.Phase.Lobby) return;
        if (_lanBoardProbeRun == null && (_lanPage != LanPage.Lobby || string.IsNullOrEmpty(_dataDir)))
        {
            if (string.IsNullOrEmpty(_dataDir)) { GD.PrintErr("LANBOARDUITEST: FAIL needs an isolated --data-dir"); _lanBoardProbe = false; }
            return;
        }
        if (LanSession.Now < _lanBoardProbeAt) return;
        _lanBoardProbeAt = LanSession.Now + .08;
        try
        {
            _lanBoardProbeRun ??= LanBoardsProbeRun().GetEnumerator();
            if (!_lanBoardProbeRun.MoveNext()) { _lanBoardProbeRun.Dispose(); _lanBoardProbeRun = null; _lanBoardProbe = false; }
        }
        catch (Exception e)
        {
            GD.PrintErr($"LANBOARDUITEST: FAIL step {_lanBoardProbeStep}: {e.Message}");
            _lanBoardProbeRun?.Dispose(); _lanBoardProbeRun = null; _lanBoardProbe = false;
        }
    }

    IEnumerable<int> LanBoardsProbeRun()
    {
        void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
        void Step(int n) { _lanBoardProbeStep = n; GD.Print($"LANBOARDUITEST: step {n}"); }
        void Key(Key key, bool echo = false) => RawInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true, Echo = echo });
        IEnumerable<int> Pad(JoyButton b)
        {
            PadButton(b, true, false); yield return 0; // ParseInputEvent's synthetic key runs on the next input cycle.
            PadButton(b, false, false); yield return 0;
        }
        Vector2 AskRow(int row) { var s = _hud.GetViewportRect().Size; return new Vector2(s.X / 2, Mathf.Floor((s.Y - 140) / 2) + (row == 0 ? 89 : 103)); }
        Vector2 ShareLine() { var s = _hud.GetViewportRect().Size; return new Vector2(Mathf.Floor(s.X / 2 - Math.Min(290, s.X - 8) / 2) + 6, _hud.LanBoardsShareLineY + 3); }

        Step(0);
        Check(!_net.BoardShareMine && !_hud.LanBoards.On, "a new session did not start OFF");
        Key(Godot.Key.B);
        Check(_lanPage == LanPage.Boards && !_lanBoardAskYes, "B did not open the consent question on NO");
        Key(Godot.Key.Enter);
        Check(_lanPage == LanPage.Lobby && !_net.BoardShareMine, "Enter on NO did not stay OFF");
        yield return 0;

        Step(1);
        foreach (var x in Pad(JoyButton.Y)) yield return x;
        Check(_lanPage == LanPage.Boards, "pad Y did not open the question");
        foreach (var x in Pad(JoyButton.DpadUp)) yield return x;
        Check(_lanBoardAskYes, "the d-pad did not select YES");
        foreach (var x in Pad(JoyButton.B)) yield return x;
        Check(_lanPage == LanPage.Lobby && !_net.BoardShareMine, "pad B did not back out without sharing");

        Step(2);
        Key(Godot.Key.B); Key(Godot.Key.W);
        Check(_lanPage == LanPage.Boards && _lanBoardAskYes, "W did not select YES");
        Key(Godot.Key.Enter);
        Check(_lanPage == LanPage.Lobby && _net.BoardShareMine, "YES did not turn sharing ON");
        yield return 0;

        Step(3);
        Check(_hud.LanBoards.On && _hud.LanBoardsShareLineY > 0, "the lobby does not show ON");
        Key(Godot.Key.B);
        Check(!_net.BoardShareMine && _lanPage == LanPage.Lobby, "B did not withdraw consent");
        yield return 0;

        Step(4);
        LanBoardsMouseAt(ShareLine(), true, false);
        Check(_lanPage == LanPage.Boards && !_lanBoardAskYes, "the label click did not open the question");
        yield return 0; // the next HUD frame exposes the question's mouse hit regions
        LanBoardsMouseAt(AskRow(0), false, true);
        Check(_lanBoardAskYes, "hovering YES did not select it");
        LanBoardsMouseAt(AskRow(1), false, true);
        Check(!_lanBoardAskYes, "hovering NO did not select it");
        LanBoardsMouseAt(new Vector2(2, 2), true, false);
        Check(_lanPage == LanPage.Boards, "an outside click answered the question");
        LanBoardsMouseAt(AskRow(0), true, false);
        Check(_lanPage == LanPage.Lobby && _net.BoardShareMine, "clicking YES did not share");
        yield return 0;

        Step(5);
        while (_net.BoardBusyLocal) yield return 0;
        double before = LanSession.Now;
        foreach (var x in Pad(JoyButton.X)) yield return x;
        Check(_net.BoardShareMine && _net.BoardNoteAt >= before, "pad X did not resync");

        Step(6);
        Key(Godot.Key.Down);
        int laps = _net.Laps;
        Key(Godot.Key.Left);
        Check(!_net.BoardShareMine && _lanPage == LanPage.Lobby, "Left did not set sync OFF");
        _net.BoardFixtureState(LanSession.Phase.Race);
        try { Key(Godot.Key.Right); Check(!_net.BoardShareMine, "sync could be enabled outside the lobby phase"); }
        finally { _net.BoardFixtureState(LanSession.Phase.Lobby); }
        Key(Godot.Key.Right); Key(Godot.Key.Right, true);
        Check(_net.BoardShareMine && _net.Laps == laps, "Right or its repeat changed laps instead of sync");
        foreach (var x in Pad(JoyButton.DpadLeft)) yield return x;
        Check(!_net.BoardShareMine && _net.Laps == laps, "pad left did not set sync OFF independently");
        foreach (var x in Pad(JoyButton.DpadRight)) yield return x;
        Check(_net.BoardShareMine && _net.Laps == laps, "pad right did not set sync ON independently");

        Step(7);
        var off = _hud.LanBoardsSwitchRect;
        LanBoardsMouseAt(new Vector2(off.Position.X + off.Size.X / 4, off.GetCenter().Y), true, false);
        Check(!_net.BoardShareMine && _lanPage == LanPage.Lobby, "clicking OFF did not set sync OFF");
        yield return 0;

        Step(8);
        var on = _hud.LanBoardsSwitchRect;
        Check(on == off, "the switch moved when its state changed");
        LanBoardsMouseAt(new Vector2(on.Position.X + on.Size.X * .75f, on.GetCenter().Y), true, false);
        Check(_net.BoardShareMine && _lanPage == LanPage.Lobby, "clicking ON did not set sync ON");
        yield return 0;

        Step(9);
        if (_net.IsHost)
        {
            int original = _net.Laps;
            Key(Godot.Key.Up);
            bool reduce = original == LanLapChoices[^1];
            Key(reduce ? Godot.Key.Left : Godot.Key.Right);
            Check(_net.Laps != original && _net.BoardShareMine, "the host LAPS row no longer changes laps independently");
            Key(reduce ? Godot.Key.Right : Godot.Key.Left);
            Check(_net.Laps == original && _net.BoardShareMine, "restoring laps changed sync");
        }
        GD.Print("LANBOARDUITEST: PASS (consent shortcuts, resync, keyboard/pad OFF/ON, held-arrow isolation, mouse switch, fixed layout, independent host laps and race-phase guard) - left on YES");
    }
}
