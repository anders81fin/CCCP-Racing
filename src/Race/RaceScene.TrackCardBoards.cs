using System;
using System.Linq;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// CHOOSE TRACK's L LEADERBOARDS (the pad's X) for SOCIALIST RALLY and STAKHANOV LAPS: the highlighted card's track and the
/// card's mode only, read only. The rally shows its four fuel boards; the Stakhanov laps browse one fuel's top ten at a time
/// (Left/Right). No other track or mode, no replays, no page fallback: an empty board stays empty. Esc, B or Enter closes it
/// back to the same card, track and mode; nothing is chosen, started, saved or switched on the car.
/// The unrestricted PARTY OFFICE board (EnterBoard, RaceScene.LeaderboardReplay.cs) is untouched by it.
/// </summary>
public partial class RaceScene
{
    bool _boardScoped;              // the open board came from the track card (only while _showBoard)
    int _boardScopeTrack;           // ...of this Tracks index (the highlighted card, not the built track)
    bool _boardScopeLaps;           // ...the Stakhanov laps' boards, else the rally's
    FuelGrade _boardScopeFuel;      // ...and the Stakhanov fuel browsed (never the car's or a saved one)

    bool BoardScoped => _showBoard && _boardScoped;

    /// <summary>The track card offers L LEADERBOARDS: a single-player mode's card (not the LAN's).</summary>
    bool TrackCardBoardsOffered => _card == TitleCard.Track && _trackFor is MenuItem.Race or MenuItem.Training;

    void OpenTrackCardBoards()
    {
        if (!TrackCardBoardsOffered) return;
        _boardScopeTrack = Math.Clamp(_cardSel, 0, Tracks.Length - 1);      // (a locked track's results may be read too)
        _boardScopeLaps = _trackFor == MenuItem.Training;
        _boardScopeFuel = FuelGrade.Diesel;      // every opening starts Diesel → Petrol → Jet Fuel → Sputnik; never selects the driving fuel
        _boardScoped = _showBoard = true;
        _overlayFrame = Engine.GetProcessFrames();
        _hud.Mode = HudMode.Board;
        if (CardDbg) GD.Print($"track card boards: {Tracks[_boardScopeTrack].id}, {(_boardScopeLaps ? "stakhanov " + _boardScopeFuel : "rally")}");
    }

    void CloseTrackCardBoards()
    {
        _showBoard = _boardScoped = false;
        _overlayExitFrame = Engine.GetProcessFrames();      // (the closing press is not the card's Enter/Esc: HandleInput waits)
        _hud.Mode = HudMode.Title;
    }

    /// <summary>The scoped board owns every raw event: pad buttons become keys through the common pad path, the rest is swallowed.</summary>
    bool TrackCardBoardsRawInput(InputEvent e)
    {
        if (!BoardScoped) return false;
        // The pad's presses and releases (a release lets go of its synthetic key) and the stick as a d-pad: A Enter, B Esc, d-pad arrows.
        if (e is InputEventJoypadButton or InputEventJoypadMotion) { PadRawInput(e); return true; }
        if (e is InputEventKey { Pressed: true } physical && physical.Device != PadKeyDevice) _padMode = false;
        if (Engine.GetProcessFrames() <= _overlayFrame) return true;
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return true;
        switch (k.Keycode)
        {
            case Key.Escape or Key.Backspace or Key.Enter or Key.KpEnter:
                CloseTrackCardBoards(); break;
            case Key.Left or Key.Right when _boardScopeLaps:
                _boardScopeFuel = (FuelGrade)(((int)_boardScopeFuel + (k.Keycode == Key.Left ? Training.LapsCount - 1 : 1)) % Training.LapsCount); break;
        }
        // No other track, mode, replay, mouse row or gameplay shortcut from here.
        return true;
    }

    /// <summary>The scoped board's HUD: every board field set from the highlighted track and the card's mode alone.</summary>
    void TrackCardBoardsHud()
    {
        string id = Tracks[_boardScopeTrack].id;
        bool built = id == _trackId;
        _hud.BoardScoped = true;
        _hud.BoardStakhanov = _boardScopeLaps;
        _hud.BoardSelected = -1;
        _hud.BoardReplayNotice = "";
        _hud.BoardTrack = TrackName(id);
        if (_boardScopeLaps)
        {
            int power = Training.PercentForKey((int)_boardScopeFuel + 1);
            var all = BoardFor(id, 1);
            var rows = all.Where(e => e.Power == power).Take(Leaderboard.StakhanovSize).ToArray();      // one fuel's own top ten: unknown legacy powers are in none
            int hl = built && _boardHighlights[1] >= 0 && _boardHighlights[1] < all.Count ? Array.IndexOf(rows, all[_boardHighlights[1]]) : -1;
            string fuel = FuelGrades.Names[(int)_boardScopeFuel];
            _hud.Board = rows;
            _hud.BoardHighlight = hl;
            _hud.BoardFuel = fuel;
            _hud.BoardSpeed = SpeedText("stakhanov", id);
            _hud.BoardFuels = new[] { new Hud.FuelBoard(fuel + " - TOP " + Leaderboard.StakhanovSize, rows, hl, _hud.BoardSpeed) };
        }
        else
        {
            _hud.Board = Array.Empty<Leaderboard.Entry>();
            _hud.BoardHighlight = -1;
            _hud.BoardFuel = "";
            _hud.BoardSpeed = "";
            _hud.BoardFuels = new[] { 2, 0, 3, 4 }.Select(f => new Hud.FuelBoard(
                f switch { 2 => "DIESEL", 3 => "JET FUEL", 4 => "SPUTNIK", _ => "PETROL" }, BoardFor(id, f),
                built ? _boardHighlights[f] : -1,
                SpeedText(f switch { 2 => "rally_diesel", 3 => "rally_rocket", 4 => "rally_sputnik", _ => "rally" }, id))).ToArray();
        }
    }
}
