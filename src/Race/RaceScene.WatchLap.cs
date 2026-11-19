using System;
using System.Linq;
using System.Threading.Tasks;
using CCCPRacing.Replay;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// WATCH LAP (the owner's order, 2026-11-19). The lap that just ended in a single-player Socialist Rally or Stakhanov drive is remembered by its archive id (and the archive that holds it: a normal lap's or
/// a record lap's), and three ways open exactly that lap in the replay viewer (the AUTO camera): the key F3 (while driving, at a result card, at the end of a rally), the result card's WATCH LAP, and
/// the Party Meeting's WATCH LAST LAP. Never an older lap: when the last lap's recording is still being prepared (or could not be saved) its state is shown and nothing else opens. The viewer suspends the
/// live scene as it is (the car, the clock, the norm, the attempts) and gives it back, to the driving, the card or the Party Meeting's row it was opened from.
/// </summary>
public partial class RaceScene
{
    enum WatchFrom { Racing, PartyMeeting, Card }

    string _lastLapId;
    int _lastLapNumber;
    float _lastLapSeconds;
    LapKind _lastLapKind;
    Task<(LapSource Src, string Notice, LapEntryInfo Info)> _watchTask;
    LapArchive _watchArchive;
    string _watchId, _watchNotice = "";
    int _watchNumber;
    WatchFrom _watchFrom;
    ulong _watchNoticeUntil;
    float _watchHintClock = 99f;
    const float WatchHintSeconds = 9f;

    /// <summary>The lap that just ended: the hint after it, and the target of F3 and WATCH LAST LAP.</summary>
    void NoteLastLap(string id, int number, float seconds, LapKind kind)
    {
        _lastLapId = id; _lastLapNumber = number; _lastLapSeconds = seconds; _lastLapKind = kind;
        _watchHintClock = 0f;
        _watchNotice = "";
    }

    /// <summary>A lap exists that can be asked for: the single-player drives with an archive (the same eligibility as the Party Meeting's laps), not the school, LAN or a viewer.</summary>
    bool WatchAvailable => _lastLapId != null && ReplayRowShown && _replayViewer == null && _watchTask == null && _sessionLapArchives.ContainsKey(_lastLapId);

    /// <summary>The hint line after a lap: F3 for the keyboard, the Party Meeting's route for a pad (null: none now).</summary>
    string WatchHintText => _lastLapId == null ? null : _padMode ? "START > WATCH LAST LAP" : $"F3  WATCH LAP {_lastLapNumber}";

    /// <summary>"PREPARING", "NOT SAVED", "OLD VERSION" for a lap that cannot be opened (yet); empty when it can.</summary>
    static string WatchStateText(LapEntryInfo i) => i.Openable ? "" : i.State switch
    {
        LapEntryState.Pending => "PREPARING",
        LapEntryState.Incompatible => "OLD VERSION",
        LapEntryState.Failed => "NOT SAVED",
        _ => "",
    };

    /// <summary>WATCH LAST LAP's label tail: a notice from the last try, else the LAST lap's own state from the meeting's listing (never an older lap's).</summary>
    string WatchStateNow()
    {
        if (_watchNotice != "" && Time.GetTicksMsec() < _watchNoticeUntil) return _watchNotice;
        if (_lastLapId == null) return "";
        var info = _replayInfos.FirstOrDefault(i => i.Id == _lastLapId);
        if (info == null) return _replayKnownLaps < 0 ? "" : "PREPARING";
        return WatchStateText(info);
    }

    void WatchNotice(string text, bool error)
    {
        _watchNotice = text; _watchNoticeUntil = Time.GetTicksMsec() + 3500;
        if (_watchFrom != WatchFrom.PartyMeeting || _state != State.Paused) _hud.Show($"LAP {_watchNumber}", text, 3f, error ? Hud.Red : Hud.Paper, Prio.Critical);
    }

    /// <summary>Opens the last lap in the viewer (the exact id, in its own archive). The listing and the opening run on a worker; the result is polled every frame (WatchTick).</summary>
    void WatchLastLap(WatchFrom from)
    {
        if (!WatchAvailable) return;
        _watchId = _lastLapId; _watchNumber = _lastLapNumber; _watchFrom = from;
        _watchArchive = _sessionLapArchives[_watchId];
        var archive = _watchArchive; string id = _watchId;
        _watchTask = Task.Run(() =>
        {
            var info = archive.List().FirstOrDefault(i => i.Id == id);
            if (info == null) return ((LapSource)null, "NOT IN THE ARCHIVE", (LapEntryInfo)null);
            if (!info.Openable) return (null, WatchStateText(info) is { Length: > 0 } t ? t : "CANNOT BE OPENED", info);
            var src = archive.Open(id, out string reason);
            return (src, src == null ? ("CANNOT OPEN: " + (reason ?? "UNKNOWN")).ToUpperInvariant() : "", info);
        });
        _watchNotice = "OPENING..."; _watchNoticeUntil = Time.GetTicksMsec() + 3500;
    }

    /// <summary>Every frame: the opening's result (the viewer starts after this frame, never inside it), and the hint's clock (it runs while driving).</summary>
    void WatchTick(float dt)
    {
        if (_state == State.Racing && !FateHasCar && !PlanHasCar) _watchHintClock += dt;
        // the hint after a lap (a few seconds, fading), the stop card's and the end screens' offer
        bool offer = _lastLapId != null && ReplayRowShown && _sessionLapArchives.ContainsKey(_lastLapId);
        _hud.WatchHint = offer && _state == State.Racing && _watchHintClock < WatchHintSeconds && !PlanHasCar && !FateHasCar ? WatchHintText : null;
        _hud.WatchHintAlpha = Math.Clamp((WatchHintSeconds - _watchHintClock) / 2f, 0f, 1f);
        string end = offer && !_padMode ? $"F3  WATCH LAP {_lastLapNumber}" : null;
        _hud.StopCardWatch = _stopCard != null ? (offer ? (_padMode ? "THEN START > WATCH LAST LAP" : end) : null) : null;
        _hud.EndWatchHint = _planScreen || _gulag ? end : null;
        if (_watchTask is not { IsCompleted: true } t) return;
        _watchTask = null;
        if (t.IsFaulted) { WatchNotice("COULD NOT OPEN THE LAP", true); GD.PushWarning("Watch lap: " + t.Exception?.GetBaseException().Message); return; }
        var (src, notice, info) = t.Result;
        if (src == null) { WatchNotice(notice, true); return; }
        _watchNotice = "";
        var archive = _watchArchive; string id = _watchId; int number = _watchNumber;
        Callable.From(() => StartWatch(src, info, archive, number)).CallDeferred();
    }

    void StartWatch(LapSource src, LapEntryInfo info, LapArchive archive, int number)
    {
        if (!IsInsideTree() || _replayViewer != null) return;
        var session = OpenReplayViewer(src, out string error);
        if (session == null) { WatchNotice(("CANNOT WATCH: " + error).ToUpperInvariant(), true); return; }
        session.SetEditContext(archive, info);      // (RaceScene.ReplayViewerEdits.cs: KEEP and the edits as from the list)
        session.SetTitle(info != null ? RowOf(info).Title + (info.Meta.Kind == LapKind.Record ? " RECORD" : info.Meta.Kind == LapKind.Void ? " VOID" : "") : $"LAP {number}");
        session.Restored += () =>
        {
            if (!IsInstanceValid(this)) return;
            _overlayExitFrame = Engine.GetProcessFrames();      // (the key that closed the viewer does nothing else)
            if (session.EndNotice != "") WatchNotice(session.EndNotice, session.EndedByError);
        };
    }

    /// <summary>The key F3 (and the same from a card or the end of the rally): while driving and in the Party Meeting's main list; the cards and the result screens call WatchLastLap themselves.</summary>
    void WatchKey()
    {
        if (!WatchAvailable) return;
        if (_state == State.Racing) { WatchLastLap(WatchFrom.Racing); return; }
        bool menu = _state == State.Paused && _hud.Mode == HudMode.Paused && !_hud.SettingsOpen && !_hud.VisualOpen && !_hud.MixerOpen && !_hud.ReplayListOpen && !_hud.PauseNormsOpen && !_hud.RallyExitOpen && !_hud.StakhanovBoardOpen && !_labOpen;
        if (menu) WatchLastLap(WatchFrom.PartyMeeting);
    }
}
