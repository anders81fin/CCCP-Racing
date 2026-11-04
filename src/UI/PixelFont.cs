using System.Collections.Generic;
using Godot;

namespace CCCPRacing.UI;

/// <summary>
/// 5×7 bitmap font defined in code, with a handful of Cyrillic letters for faux-Russian.
/// Rendered from a small atlas texture so it stays pixel-exact in the low-res viewport.
/// </summary>
public static class PixelFont
{
    public const int GlyphW = 5, GlyphH = 7, Advance = 6, LineHeight = 9;

    static readonly Dictionary<char, string> Glyphs = new()
    {
        ['A'] = ".###. #...# #...# ##### #...# #...# #...#",
        ['B'] = "####. #...# #...# ####. #...# #...# ####.",
        ['C'] = ".###. #...# #.... #.... #.... #...# .###.",
        ['D'] = "####. #...# #...# #...# #...# #...# ####.",
        ['E'] = "##### #.... #.... ####. #.... #.... #####",
        ['F'] = "##### #.... #.... ####. #.... #.... #....",
        ['G'] = ".###. #...# #.... #.### #...# #...# .####",
        ['H'] = "#...# #...# #...# ##### #...# #...# #...#",
        ['I'] = ".###. ..#.. ..#.. ..#.. ..#.. ..#.. .###.",
        ['J'] = "..### ...#. ...#. ...#. #..#. #..#. .##..",
        ['K'] = "#...# #..#. #.#.. ##... #.#.. #..#. #...#",
        ['L'] = "#.... #.... #.... #.... #.... #.... #####",
        ['M'] = "#...# ##.## #.#.# #.#.# #...# #...# #...#",
        ['N'] = "#...# ##..# #.#.# #..## #...# #...# #...#",
        ['O'] = ".###. #...# #...# #...# #...# #...# .###.",
        ['P'] = "####. #...# #...# ####. #.... #.... #....",
        ['Q'] = ".###. #...# #...# #...# #.#.# #..#. .##.#",
        ['R'] = "####. #...# #...# ####. #.#.. #..#. #...#",
        ['S'] = ".#### #.... #.... .###. ....# ....# ####.",
        ['T'] = "##### ..#.. ..#.. ..#.. ..#.. ..#.. ..#..",
        ['U'] = "#...# #...# #...# #...# #...# #...# .###.",
        ['V'] = "#...# #...# #...# #...# #...# .#.#. ..#..",
        ['W'] = "#...# #...# #...# #.#.# #.#.# #.#.# .#.#.",
        ['X'] = "#...# #...# .#.#. ..#.. .#.#. #...# #...#",
        ['Y'] = "#...# #...# .#.#. ..#.. ..#.. ..#.. ..#..",
        ['Z'] = "##### ....# ...#. ..#.. .#... #.... #####",
        ['0'] = ".###. #...# #..## #.#.# ##..# #...# .###.",
        ['1'] = "..#.. .##.. ..#.. ..#.. ..#.. ..#.. .###.",
        ['2'] = ".###. #...# ....# ..##. .#... #.... #####",
        ['3'] = "####. ....# ....# .###. ....# ....# ####.",
        ['4'] = "...#. ..##. .#.#. #..#. ##### ...#. ...#.",
        ['5'] = "##### #.... ####. ....# ....# #...# .###.",
        ['6'] = ".###. #.... #.... ####. #...# #...# .###.",
        ['7'] = "##### ....# ...#. ..#.. .#... .#... .#...",
        ['8'] = ".###. #...# #...# .###. #...# #...# .###.",
        ['9'] = ".###. #...# #...# .#### ....# ....# .###.",
        [' '] = "..... ..... ..... ..... ..... ..... .....",
        ['.'] = "..... ..... ..... ..... ..... .##.. .##..",
        [','] = "..... ..... ..... ..... .##.. ..#.. .#...",
        [':'] = "..... .##.. .##.. ..... .##.. .##.. .....",
        ['!'] = "..#.. ..#.. ..#.. ..#.. ..#.. ..... ..#..",
        ['?'] = ".###. #...# ....# ..##. ..#.. ..... ..#..",
        ['-'] = "..... ..... ..... .###. ..... ..... .....",
        ['+'] = "..... ..#.. ..#.. ##### ..#.. ..#.. .....",
        ['='] = "..... ..... ##### ..... ##### ..... .....",
        ['/'] = "....# ...#. ...#. ..#.. .#... .#... #....",
        ['%'] = "##..# ##.#. ...#. ..#.. .#... .#.## #..##",
        ['\''] = "..#.. ..#.. .#... ..... ..... ..... .....",
        ['´'] = "...## ..##. .##.. ..... ..... ..... .....",
        ['©'] = ".###. #...# #.### #.#.# #.### #...# .###.",
        ['"'] = ".#.#. .#.#. ..... ..... ..... ..... .....",
        ['('] = "...#. ..#.. .#... .#... .#... ..#.. ...#.",
        [')'] = ".#... ..#.. ...#. ...#. ...#. ..#.. .#...",
        ['<'] = "...#. ..#.. .#... #.... .#... ..#.. ...#.",
        ['>'] = ".#... ..#.. ...#. ....# ...#. ..#.. .#...",
        ['#'] = ".#.#. .#.#. ##### .#.#. ##### .#.#. .#.#.",
        ['*'] = "..#.. #.#.# .###. ##### .###. #.#.# ..#..",
        // Cyrillic
        ['Я'] = ".#### #...# #...# .#### ..#.# .#..# #...#",
        ['И'] = "#...# #...# #..## #.#.# ##..# #...# #...#",
        ['Ж'] = "#.#.# #.#.# .###. ..#.. .###. #.#.# #.#.#",
        ['Д'] = ".###. .#.#. .#.#. .#.#. .#.#. ##### #...#",
        ['Б'] = "##### #.... #.... ####. #...# #...# ####.",
        ['Ф'] = "..#.. .###. #.#.# #.#.# #.#.# .###. ..#..",
        ['Л'] = "..### .#..# .#..# .#..# .#..# .#..# #...#",
        ['П'] = "##### #...# #...# #...# #...# #...# #...#",
        ['Ш'] = "#.#.# #.#.# #.#.# #.#.# #.#.# #.#.# #####",
        ['Г'] = "##### #.... #.... #.... #.... #.... #....",
        ['Ц'] = "#..#. #..#. #..#. #..#. #..#. ##### ....#",
        ['Ч'] = "#...# #...# #...# .#### ....# ....# ....#",
        ['Э'] = "####. ....# ....# .###. ....# ....# ####.",
        ['Ы'] = "#...# #...# #...# ##..# #.#.# #.#.# ##..#",
        ['З'] = "####. ....# ....# .###. ....# ....# ####.",
        ['Ь'] = "#.... #.... #.... ####. #...# #...# ####.",
        ['Й'] = ".#.#. #...# #..## #.#.# ##..# #...# #...#",
        // Finnish/Swedish letters for song titles.
        // Dots on the top row with a clear gap, the letter squeezed into the lower five rows.
        ['Ä'] = ".#.#. ..... .###. #...# ##### #...# #...#",
        ['Ö'] = ".#.#. ..... .###. #...# #...# #...# .###.",
        ['Å'] = "..#.. ..... .###. #...# ##### #...# #...#",
        ['Ü'] = ".#.#. ..... #...# #...# #...# #...# .###.",
    };

    static ImageTexture _texture;
    static readonly Dictionary<char, int> Index = new();

    public static ImageTexture Texture
    {
        get
        {
            if (_texture == null) Build();
            return _texture;
        }
    }

    /// <summary>Cyrillic letters that look exactly like Latin ones reuse the Latin glyph.</summary>
    static readonly Dictionary<char, char> Lookalikes = new()
    {
        ['А'] = 'A', ['В'] = 'B', ['Е'] = 'E', ['К'] = 'K', ['М'] = 'M', ['Н'] = 'H',
        ['О'] = 'O', ['Р'] = 'P', ['С'] = 'C', ['Т'] = 'T', ['Х'] = 'X', ['У'] = 'Y',
    };

    static char Resolve(char c)
    {
        c = char.ToUpperInvariant(c);
        return Lookalikes.TryGetValue(c, out char l) ? l : c;
    }

    /// <summary>Whether the font has a glyph for this character (after uppercasing and look-alikes).</summary>
    public static bool CanDraw(char c) => Glyphs.ContainsKey(Resolve(c));

    static void Build()
    {
        var img = Image.CreateEmpty(Glyphs.Count * GlyphW, GlyphH, false, Image.Format.Rgba8);
        int i = 0;
        foreach (var (ch, rows) in Glyphs)
        {
            Index[ch] = i;
            string[] r = rows.Split(' ');
            for (int y = 0; y < GlyphH; y++)
            for (int x = 0; x < GlyphW; x++)
                if (r[y][x] == '#') img.SetPixel(i * GlyphW + x, y, Colors.White);
            i++;
        }
        _texture = ImageTexture.CreateFromImage(img);
    }

    public static int Measure(string text, int scale = 1) =>
        text.Length == 0 ? 0 : (text.Length * Advance - 1) * scale;

    /// <summary>Draws text with a hard 1-pixel drop shadow, DOS style.</summary>
    public static void Draw(CanvasItem ci, string text, Vector2 pos, Color color, int scale = 1, Color? shadow = null)
    {
        if (_texture == null) Build();
        Color sh = shadow ?? new Color(0, 0, 0, 0.85f);
        if (sh.A > 0) DrawRaw(ci, text, pos + new Vector2(scale, scale), sh, scale);
        DrawRaw(ci, text, pos, color, scale);
    }

    public static void DrawCentered(CanvasItem ci, string text, float cx, float y, Color color, int scale = 1, Color? shadow = null) =>
        Draw(ci, text, new Vector2(Mathf.Floor(cx - Measure(text, scale) * 0.5f), y), color, scale, shadow);

    static void DrawRaw(CanvasItem ci, string text, Vector2 pos, Color color, int scale)
    {
        float x = pos.X;
        foreach (char c0 in text)
        {
            char c = Resolve(c0);
            if (Index.TryGetValue(c, out int gi) && c != ' ')
                ci.DrawTextureRectRegion(_texture,
                    new Rect2(x, pos.Y, GlyphW * scale, GlyphH * scale),
                    new Rect2(gi * GlyphW, 0, GlyphW, GlyphH), color);
            x += Advance * scale;
        }
    }

    /// <summary>Renders text into an image, e.g. for propaganda billboards.</summary>
    public static Image RenderImage(string text, Color fg, Color bg, int padding = 2)
    {
        if (_texture == null) Build();
        int w = Measure(text) + padding * 2, h = GlyphH + padding * 2;
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        img.Fill(bg);
        int x = padding;
        foreach (char c0 in text)
        {
            char c = Resolve(c0);
            if (Glyphs.TryGetValue(c, out string rows))
            {
                string[] r = rows.Split(' ');
                for (int yy = 0; yy < GlyphH; yy++)
                for (int xx = 0; xx < GlyphW; xx++)
                    if (r[yy][xx] == '#') img.SetPixel(x + xx, padding + yy, fg);
            }
            x += Advance;
        }
        return img;
    }

    /// <summary>Faux-Russian: flips a few Latin letters into their Cyrillic look-alikes.</summary>
    public static string Faux(string s) => s.Replace('R', 'Я').Replace('N', 'И');
}
