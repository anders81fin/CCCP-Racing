using System;
using Godot;

namespace CCCPRacing.UI;

/// <summary>
/// The BALANCED LEARNING CURVE VERSION card at launch: version and a development notice, over the modern loading cover; any
/// other key closes it (the changelog is the title menu's CHANGELOG).
/// </summary>
public partial class Hud
{
    static readonly string[] IntroNotice =
    {
        "THE GAME IS STILL IN DEVELOPMENT. EXPECT ROUGH EDGES.",
    };

    void DrawIntro()
    {
        float w = Math.Min(Screen.X - 16, 440);
        int cols = (int)((w - 24) / PixelFont.Advance), rows = 0;
        foreach (string line in IntroNotice) rows += System.Linq.Enumerable.Count(Wrap(line, cols));
        var r = LogPanel(38 + rows * 10 + 22);
        float cx = Screen.X / 2;
        PixelFont.DrawCentered(this, "BALANCED LEARNING CURVE VERSION", cx, r.Position.Y + 7, Gold, 2, new Color(0.35f, 0, 0));
        string version = "V. " + ProjectSettings.GetSetting("application/config/version").AsString();
        PixelFont.DrawCentered(this, version, cx, r.Position.Y + 25, Grey);

        float y = r.Position.Y + 38;
        foreach (string line in IntroNotice)
            foreach (string row in Wrap(line, cols))
            {
                PixelFont.DrawCentered(this, row, cx, y, Paper);
                y += 10;
            }

        if ((int)(_clock * 2) % 2 == 0)
            PixelFont.DrawCentered(this, AnyKey, cx, r.End.Y - 12, Gold);
    }
}
