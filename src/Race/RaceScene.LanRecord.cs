using CCCPRacing.Net;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// RECORD LAN RACES? (LanRecordChoice): before this machine first hosts or joins, the LAN menu asks YES / NO, and the
/// answer is kept in the machine's settings.cfg. The menu's RECORD LAN RACES row shows and changes it. With NO no LAN
/// recording, original-car ghost or journal is made and KEEP is never offered; YES is the KEEP flow as before.
/// Each machine decides for itself: nothing goes over the network. Esc (pad B) backs out to the LAN menu, saving nothing.
/// </summary>
public partial class RaceScene
{
    enum LanRecordNext { Menu, Host, Join }
    const int LanRecordMenuRow = 3;
    bool? _lanRecordSaved;       // [lan] record_races; null: never answered
    bool? _lanRecordFlag;        // --lan-record=yes|no (tests): this run only, never saved
    bool _lanRecordSel = LanRecordChoice.Suggested, _lanRecordInit;
    LanRecordNext _lanRecordNext;

    string LanRecordPath => _records.Dir + "settings.cfg";
    /// <summary>This machine records its LAN rounds: the test flag, else the answer; not answered: no.</summary>
    bool LanRecordOn => _lanRecordFlag ?? _lanRecordSaved ?? false;
    string LanRecordWhy => _lanRecordFlag.HasValue ? "test flag --lan-record" : _lanRecordSaved.HasValue ? "player choice" : "not answered yet";
    string LanRecordRowLabel => "RECORD LAN RACES: " + LanRecordChoice.Label(_lanRecordSaved);

    void LoadLanRecord()
    {
        _lanRecordSaved = LanRecordChoice.ForSession(LanRecordPath);
        _lanRecordFlag = InstructionTrack == null ? LanRecordChoice.Override(OS.GetCmdlineUserArgs()) : null;
        if (!_lanRecordInit) GD.Print($"LAN record: {(LanRecordOn ? "on" : "off")} ({LanRecordWhy}; saved {LanRecordChoice.Label(_lanRecordSaved)})");
        _lanRecordInit = true;
    }

    /// <summary>HOST GAME / JOIN GAME: never answered (and no test flag) - ask first. True: the question opened.</summary>
    bool LanRecordAskFirst(LanRecordNext next)
    {
        if (_lanRecordSaved.HasValue || _lanRecordFlag.HasValue) return false;
        OpenLanRecord(next);
        return true;
    }

    void OpenLanRecord(LanRecordNext next)
    {
        _lanRecordNext = next;
        _lanRecordSel = _lanRecordSaved ?? LanRecordChoice.Suggested;
        SetLanPage(LanPage.Record);
        GD.Print($"LAN record: question open (before {next}, cursor {(_lanRecordSel ? "YES" : "NO")})");
    }

    void AnswerLanRecord(bool on)
    {
        bool saved = LanRecordChoice.Apply(LanRecordPath, on);
        _lanRecordSaved = on;      // (an unwritable file: the answer holds for this run, and the note says so)
        GD.Print($"LAN record: answered {(on ? "YES" : "NO")} -> {(saved ? "saved in " + ProjectSettings.GlobalizePath(LanRecordPath) : "NOT SAVED (settings file unavailable)")}");
        var next = _lanRecordNext;
        _lanRecordNext = LanRecordNext.Menu;
        if (!saved) { LanRecordSaveFailed(on); return; }
        switch (next)
        {
            case LanRecordNext.Host: LanHost(); break;
            case LanRecordNext.Join: LanChooseTrack(false); break;      // same track-selection path as JOIN GAME with a saved answer
            default: SetLanPage(LanPage.Menu); _lanSel = LanRecordMenuRow; break;
        }
    }

    void CancelLanRecord()
    {
        int row = _lanRecordNext switch { LanRecordNext.Host => 0, LanRecordNext.Join => 1, _ => LanRecordMenuRow };
        _lanRecordNext = LanRecordNext.Menu;
        GD.Print("LAN record: question closed with Esc (nothing saved)");
        SetLanPage(LanPage.Menu);
        _lanSel = row;
    }

    /// <summary>The menu row's Left (YES) / Right (NO), as on the ballot: saved at once.</summary>
    void SetLanRecord(bool on)
    {
        _lanRecordNext = LanRecordNext.Menu;
        if (_lanRecordSaved == on) return;
        bool saved = LanRecordChoice.Apply(LanRecordPath, on);
        _lanRecordSaved = on;
        GD.Print($"LAN record: menu set {(on ? "YES" : "NO")}{(saved ? "" : " (NOT SAVED)")}");
        if (!saved) LanRecordSaveFailed(on);
    }

    void LanRecordSaveFailed(bool on) => LanMessage("SETTING NOT SAVED", $"RECORDING: {(on ? "YES" : "NO")} - THIS RUN ONLY", LanPage.Menu);

    /// <summary>The question's keys (pad: d-pad, A, B arrive as keys); Up/Left/W/A YES, Down/Right/S/D NO.</summary>
    void LanRecordKey(bool up, bool down, bool enter, bool esc)
    {
        if (esc) CancelLanRecord();
        else if (up) _lanRecordSel = true;
        else if (down) _lanRecordSel = false;
        else if (enter) AnswerLanRecord(_lanRecordSel);
    }

    /// <summary>The mouse on the question: hover selects, a left click answers. True: a mouse event (always taken).</summary>
    bool LanRecordMouse(InputEvent e)
    {
        if (e is not InputEventMouse) return false;
        // (PixelFrame forwards window events: the HUD's own mouse position is in its 480x270 pixels, as for KEEP)
        LanRecordMouseAt(_hud.GetLocalMousePosition(), e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }, e is InputEventMouseMotion);
        return true;
    }

    void LanRecordMouseAt(Vector2 at, bool click, bool hover)
    {
        int row = _hud.LanRecordRowAt(at);
        if (row < 0) return;
        if (hover) _lanRecordSel = row == 0;
        else if (click) AnswerLanRecord(row == 0);
    }
}
