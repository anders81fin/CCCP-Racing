using System;
using System.Collections;
using System.Linq;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

// Windowed scenario "session-best" (--viewer-windowed-only=sessionbest): the Socialist Rally's top-left BEST is this session's best valid lap, not the leaderboard record, through the production lap completion
// (LapTimer.CompleteLapForTest, which fires the same events a crossed line does), and the earned medal's star under the target card.
public partial class RaceScene
{
    IEnumerable VwSessionBest()
    {
        foreach (var x in VwWaitReady()) yield return x;
        foreach (var x in VrUntil(() => _state == State.Racing && _lapClock > 3f, 60 * 40, "the race is running")) yield return x;
        string Top() => LapTimer.Format(_hud.BestIsSession ? _hud.SessionBestLap : _hud.BestLap);
        IEnumerable Settle() { for (int i = 0; i < 8; i++) yield return null; }
        // the leaderboard record, far faster than the first laps below
        _timer.BestLap = 40f; _bestLaps[RecordSet] = 40f;
        foreach (var x in Settle()) yield return x;
        VwCheck(_hud.BestIsSession && _hud.SessionBestLap < 0 && Top() == "--:--.--" && MathF.Abs(_hud.BestLap - 40f) < 1e-4f, $"before the first valid lap BEST is empty ({Top()}) while the record stays {_hud.BestLap:0.00}");
        foreach (var x in VrShot("sessionbest-1-empty")) yield return x;
        void Lap(float t) => _timer.CompleteLapForTest(t);
        Lap(70f); foreach (var x in Settle()) yield return x;
        VwCheck(MathF.Abs(_hud.SessionBestLap - 70f) < 1e-4f && MathF.Abs(_hud.BestLap - 40f) < 1e-4f, $"a lap slower than the record is the session best (BEST {Top()}, record {_hud.BestLap:0.00})");
        Lap(75f); foreach (var x in Settle()) yield return x;
        VwCheck(MathF.Abs(_hud.SessionBestLap - 70f) < 1e-4f, "a slower lap leaves it");
        Lap(65f); foreach (var x in Settle()) yield return x;
        VwCheck(MathF.Abs(_hud.SessionBestLap - 65f) < 1e-4f && MathF.Abs(_hud.BestLap - 40f) < 1e-4f && _norms.Step >= 1, $"a better lap improves it ({Top()}); the norms moved on (step {_norms.Step}) without resetting it");
        foreach (var x in VrShot("sessionbest-2-after-laps")) yield return x;
        _timer.Void(); Lap(50f); foreach (var x in Settle()) yield return x;
        VwCheck(MathF.Abs(_hud.SessionBestLap - 65f) < 1e-4f, "a void lap makes no best");
        _timer.Unranked = true; Lap(55f); _timer.Unranked = false; foreach (var x in Settle()) yield return x;
        VwCheck(MathF.Abs(_hud.SessionBestLap - 65f) < 1e-4f, "an unranked lap makes no best");
        // a pause and the replay viewer leave it alone
        foreach (var x in VrTap(Key.Escape)) yield return x;
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(_state == State.Paused && MathF.Abs(_hud.SessionBestLap - 65f) < 1e-4f, "a pause leaves it");
        foreach (var x in VrTap(Key.Escape)) yield return x;
        // a record lap: the session best follows, the record logic is the old one (timer record, ghost, board untouched by this change)
        Lap(30f); foreach (var x in Settle()) yield return x;
        VwCheck(MathF.Abs(_hud.SessionBestLap - 30f) < 1e-4f && MathF.Abs(_timer.BestLap - 30f) < 1e-4f && MathF.Abs(_bestLaps[RecordSet] - 30f) < 1e-4f, $"a lap faster than the record is both: session best {Top()}, record {_timer.BestLap:0.00}");
        foreach (var x in VrShot("sessionbest-3-record-lap")) yield return x;
        // the replay viewer's round trip
        var lap = VwFixtureLap(VlArg("--vw-lap") ?? _trackId);
        ReplayViewerSession s = null;
        foreach (var x in VwOpenPlaying(lap, "session-best", r => s = r)) yield return x;
        if (s != null) { s.Close(); foreach (var x in VrUntil(() => ReplayViewer == null, 60 * 60, "the viewer closed")) yield return x; }
        for (int i = 0; i < 20; i++) yield return null;
        VwCheck(MathF.Abs(_hud.SessionBestLap - 30f) < 1e-4f, "a viewer visit leaves it");
        // the other modes show the record as before
        var keep = _run; _run = RunMode.Training; foreach (var x in Settle()) yield return x;
        VwCheck(!_hud.BestIsSession, "Stakhanov laps keep the record on the BEST line");
        _run = keep; foreach (var x in Settle()) yield return x;
        // the earned medal's star under the card, for each medal
        foreach (var medal in new[] { NormPlan.Medal.Bronze, NormPlan.Medal.Silver, NormPlan.Medal.Gold })
        {
            _hud.ResetMedalCelebration();
            _hud.CelebrateMedal(medal, "");
            for (int i = 0; i < 60 * 4; i++) yield return null;      // (the celebration flies to its corner in 3.5 s)
            var card = _hud.NormCardRect(GetViewport().GetVisibleRect().Size);
            float y = _hud.MedalCornerY();
            VwCheck(y - 7.5f * 1.6f >= card.End.Y + 1f, $"{medal}: the star rests at y {y:0.#}, under the card (bottom {card.End.Y})");
            foreach (var x in VrShot($"sessionbest-star-{medal.ToString().ToLowerInvariant()}")) yield return x;
        }
        _hud.ResetMedalCelebration();
        // a restart or a new run opens a new session
        EnterCountdown(); foreach (var x in Settle()) yield return x;
        VwCheck(_hud.SessionBestLap < 0 && Top() == "--:--.--" && MathF.Abs(_hud.BestLap - 30f) < 1e-4f, $"a restart or new run empties the session best ({Top()}); the record stays {_hud.BestLap:0.00}");
    }
}
