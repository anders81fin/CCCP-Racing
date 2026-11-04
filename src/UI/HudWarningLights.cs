using System;
using Godot;

namespace CCCPRacing.UI;

/// <summary>
/// The dashboard's warning lights, at the top of the screen in the middle where the eye is: the engine, the fuel pump and (ATOMGRAD-4) the radiation trefoil, each a plate with a big
/// pixel symbol. A symbol in yellow is a warning, in red an alarm. Engine: yellow at 50 % or less, red at 25 % or less. Fuel: yellow at 30 % or less, red at
/// 15 % or less. Dose: yellow from 70 % of the limit, red over it. Yellow breathes slowly; red blinks fast, with a red glow round the plate; a dead engine or an empty tank stays on, steady red (nothing is
/// going to get better). Drawn with the car's gauges (HUD VISUALS: instruments).
/// </summary>
public partial class Hud
{
    // An engine (intake, block, cylinders, pulley, exhaust, oil pan), a petrol pump with its hose and the radiation trefoil. Each cell is drawn two pixels square.
    // The engine's outline on a fine grid (one pixel a cell, a stroke two pixels thick): a T-shaped intake on top, the block with its stepped end, the bar at the front.
    static readonly string[] EngineMark =
    {
        "...........#####################.................",
        "...........#####################.................",
        "...................##............................",
        "...................##............................",
        "...................##............................",
        "..............###################................",
        "............#####################................",
        "...........####................###...............",
        "##.......####...................##...............",
        "##......####....................##...............",
        "##....####......................##...............",
        "##...####.......................##....#########..",
        "##...##.........................#####.##########.",
        "##...##.........................#####.##......##.",
        "##...##............................##.##......###",
        "##...##............................#####.......##",
        "##...##............................#####.......##",
        "##...##........................................##",
        "#######........................................##",
        "#######........................................##",
        "##...##........................................##",
        "##...##........................................##",
        "##...##........................................##",
        "##...##........................................##",
        "##...##........................................##",
        "##...##........................................##",
        "##...#######...................................##",
        "##...########......................######.....###",
        "...........####....................######.....##.",
        "............####...................##..##.....##.",
        "..............####.................##..#########.",
        "...............####................##..########..",
        ".................####################............",
        "..................###################............",
    };

    // The petrol pump: the body with its window and wide foot, the hose looping down from the nozzle's arm.
    static readonly string[] PumpMark =
    {
        "..##############.........",
        "..##############...###...",
        "..##############..######.",
        "..###........###..##.###.",
        "..###........###.###..##.",
        "..###........###.##...###",
        "..###........######....##",
        "..###........#####.....##",
        "..###........###.......##",
        "..###........###.......##",
        "..###........###.......##",
        "..##############.......##",
        "..##############.......##",
        "..##############.......##",
        "..##############.......##",
        "..##############......###",
        "..##############......##.",
        "..##############..######.",
        "..##############..#####..",
        "..##############.........",
        "##################.......",
        "##################.......",
        "##################.......",
        "##################.......",
    };

    // The radiation trefoil in its ring: a hub and three blades of 60 degrees, one up and two below.
    static readonly string[] TrefoilMark =
    {
        "..........#######..........",
        "........###########........",
        "......###.........###......",
        ".....##....#####....##.....",
        "....##...#########...##....",
        "...##....#########....##...",
        "..##.....#########.....##..",
        "..#.......#######.......#..",
        ".##........#####........##.",
        ".#.........#...#.........#.",
        "##.......................##",
        "##..........###..........##",
        "##.........#####.........##",
        "##.######..#####..######.##",
        "##.######..#####..######.##",
        "##.#######..###..#######.##",
        "##..######.......######..##",
        ".#..#######.....#######..#.",
        ".##..######.....######..##.",
        "..#..#####.......#####..#..",
        "..##..###.........###..##..",
        "...##..##.........##..##...",
        "....##...............##....",
        ".....##.............##.....",
        "......###.........###......",
        "........###########........",
        "..........#######..........",
    };

    /// <summary>How big the symbols are, against the drawings above (they were 40 % bigger).</summary>
    const float Shrink = 0.6f;
    static readonly string[] EngineSmall = Resample(EngineMark, Shrink), PumpSmall = Resample(PumpMark, Shrink), TrefoilSmall = Resample(TrefoilMark, Shrink);

    /// <summary>A mark drawn smaller: each new cell is lit by how much of the old cells under it are (a third is enough, so the thin lines keep their line).</summary>
    static string[] Resample(string[] mark, float k)
    {
        int w = mark[0].Length, h = mark.Length, nw = Math.Max(1, (int)MathF.Round(w * k)), nh = Math.Max(1, (int)MathF.Round(h * k));
        var rows = new string[nh];
        for (int y = 0; y < nh; y++)
        {
            var row = new char[nw];
            for (int x = 0; x < nw; x++)
            {
                float x0 = x / k, x1 = (x + 1) / k, y0 = y / k, y1 = (y + 1) / k, lit = 0, all = 0;
                for (int sy = (int)y0; sy < Math.Min(h, (int)MathF.Ceiling(y1)); sy++)
                    for (int sx = (int)x0; sx < Math.Min(w, (int)MathF.Ceiling(x1)); sx++)
                    {
                        float cover = Math.Max(0f, Math.Min(sx + 1, x1) - Math.Max(sx, x0)) * Math.Max(0f, Math.Min(sy + 1, y1) - Math.Max(sy, y0));
                        all += cover;
                        if (mark[sy][sx] == '#') lit += cover;
                    }
                row[x] = all > 0 && lit / all >= 0.34f ? '#' : '.';
            }
            rows[y] = new string(row);
        }
        return rows;
    }

    /// <summary>The state of each dashboard light (the same order as the chime hears them): the engine, the fuel and the dose.</summary>
    public int[] WarningLevels() => new[]
    {
        LightLevel(EngineHealth, 0.5f, 0.25f), LightLevel(Fuel, 0.3f, 0.15f), DoseLightLevel(Dose),
    };

    /// <summary>The dose is a share of the limit and grows: yellow from 70 %, red over the limit.</summary>
    static int DoseLightLevel(float dose) => dose < 0 ? 0 : dose >= 1f ? 2 : dose >= 0.7f ? 1 : 0;

    /// <summary>0 off, 1 warning (yellow), 2 alarm (red, blinking), 3 dead or empty (red, steady).</summary>
    static int LightLevel(float fraction, float warnAt, float alarmAt) => fraction < 0 ? 0 : fraction <= 0 ? 3 : fraction <= alarmAt ? 2 : fraction <= warnAt ? 1 : 0;

    void DrawWarningLights()
    {
        var levels = WarningLevels();
        int e = levels[0], f = levels[1], d = levels[2];
        if (e == 0 && f == 0 && d == 0) return;
        const int Pad = 3, Gap = 4, Top = 4;
        var lights = new System.Collections.Generic.List<(string[] mark, int level, int cell)>();
        if (e > 0) lights.Add((EngineSmall, e, 1));
        if (f > 0) lights.Add((PumpSmall, f, 1));
        if (d > 0) lights.Add((TrefoilSmall, d, 1));
        float total = -Gap;
        float tallest = 0;
        foreach (var (mark, _, cell) in lights) { total += mark[0].Length * cell + Pad * 2 + Gap; tallest = Math.Max(tallest, mark.Length * cell); }
        float x = Mathf.Floor(Screen.X / 2 - total / 2);
        foreach (var (mark, level, cell) in lights)
        {
            float w = mark[0].Length * cell + Pad * 2, h = tallest + Pad * 2;      // (a common height, so the two plates stand level)
            var plate = new Rect2(x, Top, w, h);
            bool red = level >= 2;
            // Yellow breathes (about 0.8 Hz); red blinks about three times a second; dead or empty stays on.
            float on = level == 1 ? 0.7f + 0.3f * (0.5f + 0.5f * MathF.Sin(_clock * 5f))
                : level == 2 ? ((int)(_clock * 6) % 2 == 0 ? 1f : 0.28f) : 1f;
            Color c = red ? new Color(Red.R * 1.15f, Red.G * 1.1f, Red.B * 1.1f) : Gold;
            if (red)
                for (int g = 3; g >= 1; g--)
                    DrawRect(plate.Grow(g * 2), new Color(Red, 0.05f * on * (4 - g)), true);
            DrawRect(plate, new Color(0.04f, 0.02f, 0.02f, 0.82f));
            DrawRect(plate, new Color(c, 0.35f + 0.65f * on), false, 1f);
            float ox = plate.Position.X + Pad, oy = plate.Position.Y + Pad + (tallest - mark.Length * cell) / 2f;
            Color fill = new(c, 0.25f + 0.75f * on);
            for (int r = 0; r < mark.Length; r++)
                for (int k = 0; k < mark[r].Length; k++)
                    if (mark[r][k] == '#') DrawRect(new Rect2(ox + k * cell, oy + r * cell, cell, cell), fill);
            x += w + Gap;
        }
    }
}
