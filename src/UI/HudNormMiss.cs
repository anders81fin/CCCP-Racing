using System;
using System.Globalization;
using Godot;

namespace CCCPRacing.UI;

public partial class Hud
{
    float _normMissAge = 10;
    string _normMissTitle, _normMissComment, _normMissStamp, _normMissLeft;
    public bool NormMissActive => _normMissForCard || _normMissAge < (_normMissFinal ? 2.8f : 2.2f);
    bool _normMissForCard, _normMissClose;
    public bool NormMissCard { get; private set; }
    float _normMissCardStarted;
    public string NormMissComment => _normMissComment;
    public string NormMissLaps => _normMissLeft;
    int _normMissCount;
    bool _normMissFinal;
    // The stamp card never covers the car: it sits above or below it, on the side with more room, kept for the whole showing.
    bool _normMissPlaced, _normMissBelow;
    float _normMissTop;
    const float NormMissFull = 62, NormMissCompact = 62, NormMissGap = 30;
    static readonly string[] CommitteeComments =
    {
        "THE COMMITTEE EXPECTED MORE.",
        "YOUR EXPLANATION HAS BEEN FILED.",
        "THE STOPWATCH IS NOT NEGOTIABLE.",
        "PLEASE REVISE YOUR ENTHUSIASM.",
    };

    public void MissNorm(float time, float norm, bool ranked, int left, bool inCard = false)
    {
        ResetNormFeedback();
        _medalCheer?.Stop();
        bool valid = ranked && float.IsFinite(time) && time > 0;
        float excess = valid ? Math.Max(0, time - norm) : 0;
        bool close = valid && excess < 1;
        _normMissFinal = left <= 0;
        _normMissForCard = inCard;
        _normMissClose = close;
        _normMissAge = inCard ? 10 : 0;
        _normMissPlaced = false;
        _normMissTitle = valid ? "NORM MISSED +" + excess.ToString("0.00", CultureInfo.InvariantCulture) + " S"
            : "LAP NOT COUNTED";
        _normMissStamp = _normMissFinal ? "PLAN REJECTED" : close ? "ALMOST, COMRADE" : "BELOW EXPECTATIONS";
        _normMissComment = _normMissFinal ? "THE TRUCK IS WAITING."
            : !valid ? "THE COMMITTEE REQUIRES A VALID LAP."
            : close ? "THE COMMITTEE NOTICED THE EFFORT."
            : CommitteeComments[_normMissCount++ % CommitteeComments.Length];
        _normMissLeft = left <= 0 ? "NO LAPS LEFT" : left == 1 ? "LAST LAP!" : $"{left} LAPS LEFT";
        _normMissLeft += " / TARGET " + Race.LapTimer.Format(norm);
        if (!inCard) _normMissSound?.React(close, _normMissFinal);
    }

    public void BeginNormMissCard()
    {
        NormMissCard = true;
        _normMissCardStarted = _clock;
        _normMissSound?.React(_normMissClose, _normMissFinal);
    }

    public void EndNormMissCard()
    {
        NormMissCard = _normMissForCard = false;
        _normMissAge = 10;
    }

    void DrawNormMiss()
    {
        float duration = _normMissFinal ? 2.8f : 2.2f;
        if (NormMissCard || _normMissForCard || _normMissAge >= duration || Clean || TrackPreview || School || Mode != HudMode.Race) return;
        float age = _normMissAge;
        float alpha = 1 - Mathf.SmoothStep(duration - 0.35f, duration, age);
        float cx = Screen.X / 2;
        // The room above and below the car, between the top row of panels and the bottom gauges.
        float roomTop = 36, roomBottom = Screen.Y - 42;
        bool compact = false;
        float top;
        if (CarOnScreen is Vector2 car)
        {
            float above = car.Y - NormMissGap - roomTop, below = roomBottom - (car.Y + NormMissGap);
            if (!_normMissPlaced)
            {
                _normMissPlaced = true;
                _normMissBelow = below > above;
            }
            float room = _normMissBelow ? below : above;
            compact = room < NormMissFull;
            float h = compact ? NormMissCompact : NormMissFull;
            float rectTop = _normMissBelow ? car.Y + NormMissGap : car.Y - NormMissGap - h;
            rectTop = Math.Clamp(rectTop, roomTop, Math.Max(roomTop, roomBottom - h));
            float want = rectTop + 5;
            _normMissTop = age < 0.05f || float.IsNaN(_normMissTop) ? want : Mathf.Lerp(_normMissTop, want, 1f - MathF.Exp(-10f * (float)GetProcessDeltaTime()));
            top = _normMissTop;
        }
        else top = Math.Max(76, Screen.Y * 0.22f);
        string shortText=_normMissTitle=="LAP NOT COUNTED"?"LAP NOT COUNTED":_normMissTitle.Replace("NORM MISSED ","");
        shortText += " / " + (_normMissLeft.Contains(" / ")?_normMissLeft.Split(" / ")[0]:_normMissLeft);
        int scale = PixelFont.Measure(shortText, 2) <= Screen.X - 20 ? 2 : 1;
        PixelFont.DrawCentered(this, shortText, cx, top, new Color(Gold, alpha), scale, HeadingShadow);
        DrawNormMissStamp(new Vector2(cx, top + 32), age, alpha, 2);

    }

    void DrawNormMissStamp(Vector2 centre, float age, float alpha = 1, float size = 1)
    {
        float stampAge = Math.Max(0, age - 0.07f);
        float recoil = MathF.Exp(-stampAge * 20) * MathF.Sin(stampAge * 65) * 3;
        float stampScale = size * (1 + MathF.Exp(-stampAge * 24) * 0.4f);
        DrawSetTransform(centre + new Vector2(recoil, 0), -0.055f, Vector2.One * stampScale);
        float stampWidth = PixelFont.Measure(_normMissStamp) + 14;
        DrawRect(new Rect2(-stampWidth / 2, -6, stampWidth, 19), new Color(Red, alpha), false, 2);
        PixelFont.DrawCentered(this, _normMissStamp, 0, 0, new Color(Red.Lightened(0.2f), alpha));
        DrawSetTransform(Vector2.Zero);
    }
}
