using System;
using Godot;

namespace CCCPRacing.UI;

/// <summary>
/// The CUSTOM TRACKS card over the title: the player's own tracks as a vertical list (six rows at a time), each with the best lap or ERROR, a line under the list for the selected
/// one, and the keys. Set by RaceScene.CustomTracks.cs; drawn in place of the horizontal choice card (HudSchool.DrawCard) while CustomRows is not null.
/// </summary>
public partial class Hud
{
    public const string CustomLabel = "CUSTOM TRACKS", CustomShort = "CUSTOM";
    public const int CustomVisible = 6;

    /// <summary>One row per track (null: this card is not up). The name, the time or "" and whether the track cannot be started.</summary>
    public string[] CustomRows, CustomTimes;
    public bool[] CustomBad;
    public int CustomSelection;
    /// <summary>The lines shown when there are no tracks.</summary>
    public string[] CustomEmpty = Array.Empty<string>();
    /// <summary>The keys line the card drew last (for the windowed tests).</summary>
    public string CustomKeysDrawn { get; private set; }

    /// <summary>The keys under the list (the card test measures it).</summary>
    internal string CustomKeysLine(bool empty) => empty ? P("O OPENS THE FOLDER   ESC BACK", "B BACK") : P("UP/DOWN CHOOSE   ENTER GO   O FOLDER   ESC BACK", "D-PAD CHOOSE   A GO   B BACK");

    void DrawCustomCard()
    {
        var s = Screen;
        float cx = s.X / 2;
        float top = Mathf.Floor(_cardTop), room = s.Y - 14 - top;
        bool empty = CustomRows.Length == 0;
        int titleScale = room >= 120 ? 2 : 1;
        float headH = titleScale == 2 ? 28 : 17, keysH = 14, subH = 14;
        int visible = Math.Max(1, Math.Min(Math.Min(CustomRows.Length, CustomVisible), (int)((room - headH - keysH - subH - 6) / 11)));
        float bodyH = empty ? CustomEmpty.Length * 10 + 6 : visible * 11 + subH;
        float h = Math.Min(room, headH + bodyH + keysH + 4);
        string keys = CustomKeysLine(empty);
        float w = Math.Min(s.X - 8, Math.Max(300, Math.Max(PixelFont.Measure(keys), PixelFont.Measure(CardSub ?? "")) + 24));
        var r = new Rect2(Mathf.Floor(cx - w / 2), top, w, h);
        Panel(r, 0.94f);
        DrawHeading(CardTitle, cx, r.Position.Y + (titleScale == 2 ? 9 : 5), _cardTextClock, titleScale);
        float y0 = r.Position.Y + headH + 4;
        if (empty)
        {
            for (int i = 0; i < CustomEmpty.Length; i++) PixelFont.DrawCentered(this, CustomEmpty[i], cx, y0 + i * 10, i == 0 ? Gold : Grey);
        }
        else
        {
            int first = Math.Clamp(CustomSelection - visible / 2, 0, CustomRows.Length - visible);
            for (int i = first; i < first + visible; i++)
            {
                float y = y0 + (i - first) * 11;
                bool sel = i == CustomSelection, bad = CustomBad[i];
                if (sel) DrawRect(new Rect2(r.Position.X + 4, y - 2, r.Size.X - 8, 11), new Color(Red, 0.7f));
                PixelFont.Draw(this, CustomRows[i], new Vector2(r.Position.X + 12, y), bad ? new Color(0.95f, 0.45f, 0.4f) : sel ? Gold : Paper);
                string t = bad ? "ERROR" : CustomTimes[i];
                if (t.Length > 0) PixelFont.Draw(this, t, new Vector2(r.End.X - 12 - PixelFont.Measure(t), y), bad ? new Color(0.95f, 0.45f, 0.4f) : sel ? Gold : Grey);
            }
            if (CustomRows.Length > visible)
            {
                string pos = $"{CustomSelection + 1}/{CustomRows.Length}";
                PixelFont.Draw(this, pos, new Vector2(r.End.X - 8 - PixelFont.Measure(pos), r.Position.Y + 5), Grey);
            }
            if (CardSub != null) PixelFont.DrawCentered(this, CardSub, cx, y0 + visible * 11 + 3, IsBad(CustomSelection) ? new Color(0.95f, 0.45f, 0.4f) : Grey);
        }
        PixelFont.DrawCentered(this, keys, cx, r.End.Y - 12, Grey);
        CustomKeysDrawn = keys;
        DrawMenuNote(cx, Mathf.Floor(r.Position.Y + r.Size.Y / 2 - 14));      // (a note over the middle of the card: the title's own draw is under it)

        bool IsBad(int i) => i >= 0 && i < CustomBad.Length && CustomBad[i];
    }
}
