using Godot;
using CCCPRacing.Race;

namespace CCCPRacing.UI;

public partial class Hud
{
    public bool PauseHasNorms, PauseNormsOpen, PauseNormFinished, PauseNormsStart, PauseNormsResumed;
    public int PauseNormBeaten;
    public float[] PauseNormPassedTimes = System.Array.Empty<float>();
    public float[] PauseNormTimes = System.Array.Empty<float>();

    public void SetPauseNorms(bool open)
    {
        PauseNormsOpen = open;
        _menuTextClock = 0;
    }

    // Shares the pause menu's exact row geometry, including controller-disconnected notice.
    public int PauseRowAt(Vector2 pos)
    {
        int count = PauseRowIds.Length;
        float h = 52 + count * 10 + (PadLost ? 12 : 0);
        float top = Mathf.Floor(Screen.Y / 2 - h / 2);
        if (pos.X < Screen.X / 2 - 106 || pos.X > Screen.X / 2 + 106) return -1;
        int row = (int)Mathf.Floor((pos.Y - top - 28 - (PadLost ? 12 : 0)) / 10);
        return row >= 0 && row < count ? row : -1;
    }

    void DrawPauseNorms()
    {
        float h = 88 + PauseNormTimes.Length * 12;
        var r = new Rect2(Mathf.Floor(Screen.X / 2 - 150), Mathf.Floor(Screen.Y / 2 - h / 2), 300, h);
        Panel(r, 1f);
        DrawHeading("NORMS AND MEDALS", Screen.X / 2, r.Position.Y + 8, _menuTextClock);
        PixelFont.DrawCentered(this, PauseNormsStart && PauseNormsResumed ? $"SAVED RALLY - {PlanLeft} LAPS LEFT" : "BEAT EACH TIME IN ORDER - RANKED LAPS", Screen.X / 2, r.Position.Y + 30, Paper);
        for (int i = 0; i < PauseNormTimes.Length; i++)
        {
            bool done = i < PauseNormBeaten;
            string status = done ? "X  PASSED" : i == PauseNormBeaten && !PauseNormFinished ? ">  CURRENT" : "-";
            if (done && i < PauseNormPassedTimes.Length && PauseNormPassedTimes[i] > 0)
                status += " " + LapTimer.Format(PauseNormPassedTimes[i]);
            string medal = i + 1 == PauseNormTimes.Length ? "GOLD" : i + 1 == NormPlan.SilverAt ? "SILVER" : i + 1 == NormPlan.BronzeAt ? "BRONZE" : "";
            float y = r.Position.Y + 46 + i * 12;
            PixelFont.Draw(this, $"{i + 1,2}  < {LapTimer.Format(PauseNormTimes[i])}", new Vector2(r.Position.X + 12, y), Paper);
            PixelFont.Draw(this, status, new Vector2(r.Position.X + 120, y), done ? new Color(0.5f, 0.85f, 0.4f) : Gold);
            PixelFont.Draw(this, medal, new Vector2(r.End.X - 50, y), Gold);
        }
        PixelFont.DrawCentered(this, "MEDALS AWARDED AFTER THE MARKED NORM", Screen.X / 2, r.End.Y - 26, Grey);
        PixelFont.DrawCentered(this, PauseNormsStart ? P("ENTER / ESC  START RACE", "A / B  START RACE") : P("ESC / ENTER  BACK", "B / A  BACK"), Screen.X / 2, r.End.Y - 12, Paper);
    }
}
