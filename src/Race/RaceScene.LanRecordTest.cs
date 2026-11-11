using System;
using System.IO;
using CCCPRacing.Net;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// --lan-record-probe (with --data-dir and --lan-name): from the title, drives RECORD LAN RACES? through the production
/// LAN menu input (keys, pad, mouse) and checks it: HOST GAME and JOIN GAME ask while unanswered and open no session,
/// Esc/B back out saving nothing, an answer is saved beside the file's other settings and continues to the chosen
/// route, an answered machine is not asked again, and the menu row shows and changes the answer.
/// Refuses to run without an isolated --data-dir. Prints LANRECORDTEST: PASS/FAIL; with --lan-shots, screenshots.
/// </summary>
public partial class RaceScene
{
    int _lanRecordProbeStep;
    double _lanRecordProbeWait;

    partial void LanRecordProbeTick()
    {
        if (!_lanRecordProbe) return;
        string path = LanRecordPath;
        void Assert(bool ok, string why)
        {
            if (ok) return;
            GD.PrintErr($"LANRECORDTEST: FAIL step {_lanRecordProbeStep} {why}");
            _lanRecordProbe = false; GetTree().Quit(4); throw new InvalidOperationException(why);
        }
        void Key(Key key) => RawInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        void Pad(JoyButton b) { PadButton(b, true, false); PadButton(b, false, false); }
        bool? Saved() => LanRecordChoice.Load(path);
        Vector2 Row(int row)
        {
            float top = Mathf.Floor((_hud.GetViewportRect().Size.Y - 122) / 2);      // HudLanRecord.cs: the rows at +70 and +84
            return new Vector2(_hud.GetViewportRect().Size.X / 2, top + (row == 0 ? 71 : 85));
        }
        void Shot(string tag) { LanShotIn(.25, tag); _lanRecordProbeWait = _clock + .6; }
        if (_lanRecordProbeWait > 0) { if (_clock < _lanRecordProbeWait || _lanShotQueue.Count > 0) return; _lanRecordProbeWait = 0; }
        if (_lanRecordProbeStep == 0)
        {
            Assert(!string.IsNullOrEmpty(_dataDir), "needs an isolated --data-dir (never the player's own settings)");
            if (_state != State.Title || _booting || CardShown || _showLog || _clock < 1) return;
            var cfg = new ConfigFile(); cfg.Load(path); cfg.SetValue("probe", "sentinel", "kept"); cfg.Save(path);
            Assert(Saved() == null && _lanRecordSaved == null && !LanRecordOn, "a fresh machine is not unanswered/off");
            EnterLanMenu();
            Assert(_lanPage == LanPage.Menu, "the LAN menu did not open (pass --lan-name)");
            GD.Print($"LANRECORDTEST: settings {ProjectSettings.GlobalizePath(path)}");
            _lanRecordProbeStep = 1; return;
        }
        GD.Print($"LANRECORDTEST: step {_lanRecordProbeStep}");
        switch (_lanRecordProbeStep)
        {
            case 1:
                Assert(_hud.Lan.Items.Count == 4 && _hud.Lan.Items[3] == "RECORD LAN RACES: ASK", "the menu row is '" + string.Join("|", _hud.Lan.Items) + "'");
                Key(Godot.Key.Enter);      // HOST GAME
                Assert(_lanPage == LanPage.Record && _lanRecordNext == LanRecordNext.Host && _lanRecordSel, "HOST GAME did not ask first with YES selected");
                Assert(!_net.Active, "HOST GAME opened a session before the answer");
                Shot("record-question-host"); _lanRecordProbeStep = 2; break;
            case 2:
                Key(Godot.Key.Escape);
                Assert(_lanPage == LanPage.Menu && _lanSel == 0 && !_net.Active && Saved() == null, "Esc did not back out to HOST GAME with nothing saved");
                Key(Godot.Key.Down); Key(Godot.Key.Enter);      // JOIN GAME
                Assert(_lanPage == LanPage.Record && _lanRecordNext == LanRecordNext.Join, "JOIN GAME did not ask first");
                Key(Godot.Key.S); Assert(!_lanRecordSel, "S did not select NO");
                Key(Godot.Key.W); Assert(_lanRecordSel, "W did not select YES");
                Pad(JoyButton.DpadDown); _lanRecordProbeStep = 3; break;
            case 3:
                Assert(!_lanRecordSel, "the pad's d-pad did not select NO");
                Pad(JoyButton.DpadUp); _lanRecordProbeStep = 4; break;
            case 4:
                Assert(_lanRecordSel && _hud.Lan.Page == LanPage.Record, "the pad's d-pad did not select YES");
                LanRecordMouseAt(Row(1), false, true); Assert(!_lanRecordSel, "hovering NO did not select it");
                LanRecordMouseAt(Row(0), false, true); Assert(_lanRecordSel, "hovering YES did not select it");
                LanRecordMouseAt(new Vector2(4, 4), true, false); Assert(_lanPage == LanPage.Record, "a click outside the rows answered");
                RawInput(new InputEventMouseMotion()); Assert(_lanPage == LanPage.Record, "a real mouse event left the question");
                Pad(JoyButton.B); _lanRecordProbeStep = 5; break;
            case 5:
                Assert(_lanPage == LanPage.Menu && _lanSel == 1 && Saved() == null, "the pad's B did not back out to JOIN GAME with nothing saved");
                Key(Godot.Key.Enter);
                Assert(_lanPage == LanPage.Record, "JOIN GAME did not ask again while unanswered");
                _lanRecordProbeStep = 6; break;
            case 6:
                LanRecordMouseAt(Row(1), true, false);      // click NO
                Assert(_lanPage == LanPage.Join && Saved() == false && _lanRecordSaved == false && !LanRecordOn, "clicking NO did not save NO and go on to JOIN GAME");
                var cfg = new ConfigFile(); cfg.Load(path);
                Assert(cfg.GetValue("probe", "sentinel", "").AsString() == "kept", "the answer lost the file's other settings");
                Key(Godot.Key.Escape);
                Assert(_lanPage == LanPage.Menu, "Esc on JOIN GAME did not return to the menu");
                Key(Godot.Key.Down); Key(Godot.Key.Enter);
                Assert(_lanPage == LanPage.Join, "an answered machine was asked again before JOIN GAME");
                Key(Godot.Key.Escape); Key(Godot.Key.Up);      // HOST GAME -> wraps to RECORD LAN RACES
                Assert(_lanPage == LanPage.Menu && _lanSel == LanRecordMenuRow, "Up from HOST GAME did not reach the RECORD row");
                Shot("menu-record-no"); _lanRecordProbeStep = 7; break;
            case 7:
                Assert(_hud.Lan.Items[3] == "RECORD LAN RACES: NO", "the menu row is '" + _hud.Lan.Items[3] + "'");
                Key(Godot.Key.Left);
                Assert(Saved() == true && LanRecordOn && _lanPage == LanPage.Menu, "Left on the row did not save YES");
                Shot("menu-record-yes"); _lanRecordProbeStep = 8; break;
            case 8:
                Assert(_hud.Lan.Items[3] == "RECORD LAN RACES: YES", "the menu row is '" + _hud.Lan.Items[3] + "'");
                Key(Godot.Key.Right); Assert(Saved() == false, "Right on the row did not save NO");
                Key(Godot.Key.Enter);
                Assert(_lanPage == LanPage.Record && !_lanRecordSel && _lanRecordNext == LanRecordNext.Menu, "Enter on the row did not open the question on the saved NO");
                Shot("record-question-menu"); _lanRecordProbeStep = 9; break;
            case 9:
                Key(Godot.Key.Escape);
                Assert(_lanPage == LanPage.Menu && _lanSel == LanRecordMenuRow && Saved() == false, "Esc from the menu's question changed the answer");
                Key(Godot.Key.Enter); Key(Godot.Key.Up); Key(Godot.Key.Enter);
                Assert(_lanPage == LanPage.Menu && _lanSel == LanRecordMenuRow && Saved() == true && !_net.Active, "YES from the menu's question was not saved (or opened a session)");
                LoadLanRecord(); Assert(_lanRecordSaved == true && LanRecordOn, "the saved YES does not read back");
                GD.Print("LANRECORDTEST: PASS (host/join ask while unanswered; Esc/B save nothing; keys, pad and mouse; saved beside other settings; not asked again; menu row shows and changes it)");
                _lanRecordProbe = false; GetTree().Quit(0); break;
        }
    }
}
