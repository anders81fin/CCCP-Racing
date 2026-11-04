using System;
using Godot;

namespace CCCPRacing.UI;

/// <summary>
/// Game pad bits of the HUD: the prompts follow the last input (Pad: the pad's buttons by their
/// Xbox names), the letter-wheel text fields, and the small keyboard / pad marks that tell how a
/// lap on the boards and in the LAN results was driven.
/// </summary>
public partial class Hud
{
    /// <summary>The last input came from a pad: prompts and the key card name its buttons.</summary>
    public bool Pad;
    /// <summary>A pad is plugged in (the pause menu offers the rumble).</summary>
    public bool PadConnected;
    public bool Rumble = true;
    /// <summary>The pause menu's camera mode; null where the camera is not the player's.</summary>
    public string GameplayCameraName;
    /// <summary>The pause came from the driving pad being unplugged.</summary>
    public bool PadLost;

    /// <summary>The keys' prompt, or the pad's after pad input.</summary>
    string P(string keys, string pad) => Pad ? pad : keys;

    string AnyKey => Pad ? "PRESS ANY BUTTON, TOVARISH" : "PRESS ANY KEY, TOVARISH";

    /// <summary>A letter-wheel field's buttons (RaceScene.Pad.cs).</summary>
    const string WheelKeys = "UP DOWN LETTER  A NEXT  B DELETE  START OK";

    public const int InputMarkW = 11;

    static readonly string[] PadMark =
    {
        ".#########.",
        "##.#####.##",
        "#...###.#.#",
        "##.#####.##",
        "###.....###",
        "##.......##",
    };

    static readonly string[] KeysMark =
    {
        "###########",
        "#.........#",
        "#.#.#.#.#.#",
        "#.........#",
        "#..#####..#",
        "###########",
    };

    /// <summary>A tiny pad (d-pad, buttons, grips) or keyboard (keys, space bar), 11x6, top-left at a text line's top.</summary>
    void DrawInputMark(Vector2 at, bool pad, Color c)
    {
        var rows = pad ? PadMark : KeysMark;
        at = new Vector2(Mathf.Floor(at.X), Mathf.Floor(at.Y) + 1);
        for (int r = 0; r < rows.Length; r++)
            for (int x = 0; x < rows[r].Length; x++)
                if (rows[r][x] == '#') DrawRect(new Rect2(at.X + x, at.Y + r, 1, 1), c);
    }

    /// <summary>
    /// A name or IP field at double size. Keys: a blinking block after the text. Pad: the letter
    /// the wheel turns (the last) lit, with arrows over and under it; an empty field blinks a block.
    /// </summary>
    void DrawTextField(string text, Rect2 f, int max)
    {
        float tx = f.Position.X + 8, ty = f.Position.Y + 4;
        if (Pad && text.Length > 0)
        {
            float lx = tx + (text.Length - 1) * 12;
            PixelFont.Draw(this, text[..^1], new Vector2(tx, ty), Gold, 2);
            DrawRect(new Rect2(lx - 2, ty - 2, 14, 18), new Color(Red, 0.8f));
            PixelFont.Draw(this, text[^1].ToString(), new Vector2(lx, ty), Paper, 2);
            // Pixel arrows just outside the field: up over the letter, down under it.
            float mx = lx + 4;
            for (int k = 0; k < 3; k++)
            {
                DrawRect(new Rect2(mx - k, f.Position.Y - 5 + k, 1 + 2 * k, 1), Gold);
                DrawRect(new Rect2(mx - k, f.End.Y + 4 - k, 1 + 2 * k, 1), Gold);
            }
            return;
        }
        PixelFont.Draw(this, text, new Vector2(tx, ty), Gold, 2);
        if ((int)(_clock * 3) % 2 == 0 && text.Length < max)
            DrawRect(new Rect2(tx + text.Length * 12, ty, 10, 14), Paper);
    }
}
