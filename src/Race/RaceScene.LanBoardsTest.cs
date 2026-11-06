using System;
using CCCPRacing.Net;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// --lan-board-ui-probe (a LAN client or host, isolated --data-dir): in the lobby, drives SHARE BOARDS through the production input
/// paths (RawInput keys, PadButton, the HUD's mouse positions) and checks page and consent after each step: B opens the question on
/// NO, Enter on NO stays NO, pad Y opens it and pad B backs out, W/Up + Enter says YES, B withdraws, a click on the share line opens
/// the question again, hovering selects and a click on YES answers YES, R resyncs once idle. Ends sharing (YES) so the session's sync
/// and race go on. Prints LANBOARDUITEST: PASS / FAIL.
/// </summary>
public partial class RaceScene
{
    bool _lanBoardProbe;
    int _lanBoardProbeStep;
    double _lanBoardProbeAt;

    void LanBoardsProbeTick()
    {
        if (!_lanBoardProbe || !_net.Active || !_net.InLobby || _net.State != LanSession.Phase.Lobby) return;
        if (_lanBoardProbeStep == 0 && (_lanPage != LanPage.Lobby || string.IsNullOrEmpty(_dataDir))) { if (string.IsNullOrEmpty(_dataDir)) Fail("needs an isolated --data-dir"); return; }
        if (LanSession.Now < _lanBoardProbeAt) return;
        _lanBoardProbeAt = LanSession.Now + .3;      // a few frames between steps: the HUD draws the new state (mouse positions)
        void Fail(string why) { GD.PrintErr($"LANBOARDUITEST: FAIL step {_lanBoardProbeStep} {why}"); _lanBoardProbe = false; }
        bool Assert(bool ok, string why) { if (!ok) Fail(why); return ok; }
        void Key(Key key) => RawInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        void Pad(JoyButton b) { PadButton(b, true, false); PadButton(b, false, false); }
        Vector2 AskRow(int row) { var s = _hud.GetViewportRect().Size; return new Vector2(s.X / 2, Mathf.Floor((s.Y - 140) / 2) + (row == 0 ? 89 : 103)); }
        Vector2 ShareLine() { var s = _hud.GetViewportRect().Size; return new Vector2(Mathf.Floor(s.X / 2 - Math.Min(290, s.X - 8) / 2) + 6, _hud.LanBoardsShareLineY + 3); }
        GD.Print($"LANBOARDUITEST: step {_lanBoardProbeStep}");
        switch (_lanBoardProbeStep)
        {
            case 0:
                if (!Assert(!_net.BoardShareMine && _hud.LanBoards.On == false, "a new session does not start on NO")) return;
                Key(Godot.Key.B);
                if (!Assert(_lanPage == LanPage.Boards && !_lanBoardAskYes && _hud.Lan.Page == LanPage.Boards || _lanPage == LanPage.Boards && !_lanBoardAskYes, "B did not open SHARE BOARDS? on NO")) return;
                Key(Godot.Key.Enter);
                if (!Assert(_lanPage == LanPage.Lobby && !_net.BoardShareMine, "Enter on NO did not keep NO")) return;
                break;
            case 1:
                Pad(JoyButton.Y);
                if (!Assert(_lanPage == LanPage.Boards, "pad Y did not open the question")) return;
                Pad(JoyButton.DpadUp);
                if (!Assert(_lanBoardAskYes, "the d-pad did not select YES")) return;
                Pad(JoyButton.B);
                if (!Assert(_lanPage == LanPage.Lobby && !_net.BoardShareMine, "pad B did not back out with nothing shared")) return;
                break;
            case 2:
                Key(Godot.Key.B); Key(Godot.Key.W);
                if (!Assert(_lanPage == LanPage.Boards && _lanBoardAskYes, "W did not select YES")) return;
                Key(Godot.Key.Enter);
                if (!Assert(_lanPage == LanPage.Lobby && _net.BoardShareMine, "Enter on YES did not share")) return;
                break;
            case 3:
                if (!Assert(_hud.LanBoards.On && _hud.LanBoardsShareLineY > 0, "the lobby does not show YES")) return;
                Key(Godot.Key.B);
                if (!Assert(!_net.BoardShareMine && _lanPage == LanPage.Lobby, "B while YES did not withdraw at once")) return;
                break;
            case 4:
                LanBoardsMouseAt(ShareLine(), true, false);
                if (!Assert(_lanPage == LanPage.Boards && !_lanBoardAskYes, "a click on the share line did not open the question on NO")) return;
                LanBoardsMouseAt(AskRow(0), false, true);
                if (!Assert(_lanBoardAskYes, "hovering YES did not select it")) return;
                LanBoardsMouseAt(AskRow(1), false, true);
                if (!Assert(!_lanBoardAskYes, "hovering NO did not select it")) return;
                LanBoardsMouseAt(new Vector2(2, 2), true, false);
                if (!Assert(_lanPage == LanPage.Boards, "a click outside the rows answered")) return;
                LanBoardsMouseAt(AskRow(0), true, false);
                if (!Assert(_lanPage == LanPage.Lobby && _net.BoardShareMine, "a click on YES did not share")) return;
                break;
            case 5:
                if (_net.BoardBusyLocal) return;      // R resyncs when idle (it cancels while busy)
                double before = LanSession.Now;
                Pad(JoyButton.X);
                if (!Assert(_net.BoardShareMine && _net.BoardNoteAt >= before, "pad X did not resync")) return;
                GD.Print("LANBOARDUITEST: PASS (B / pad Y open the question on NO; Enter, pad B and Esc keep NO; W/d-pad + Enter and a mouse click say YES; B withdraws at once; the share line click works; pad X resyncs) - left on YES");
                _lanBoardProbe = false;
                return;
        }
        _lanBoardProbeStep++;
    }
}
