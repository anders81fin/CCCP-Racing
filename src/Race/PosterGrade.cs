using System;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The title menu's world as a faded Soviet poster: under half of its own colour left, the rest a
/// tritone in the game's colours, mixed halfway towards monochrome for a muted red tint,
/// the blacks a little lifted. A 3D colour table on the environment, so the 3D world only: the
/// banner, the menus and the HUD keep their colours. Amount 0 is the plain picture (the adjustment
/// off), 1 the full poster; the title fades between them.
/// </summary>
public static class PosterGrade
{
    const int Size = 17;                       // table points per channel (sampled with interpolation)
    const float ColourLeft = 0.45f, Tone = 0.55f, Lift = 0.04f;
    static readonly Vector3 Shadow = new(0.16f, 0.04f, 0.04f), Middle = new(0.66f, 0.27f, 0.14f), Highlight = new(0.97f, 0.89f, 0.72f);

    static ImageTexture3D _table;
    static float _tableAmount = -1f;

    /// <summary>The poster look on env at this amount (0..1); rebuilt only when the amount has moved.</summary>
    public static void Apply(Godot.Environment env, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        if (amount < 0.001f)
        {
            env.AdjustmentEnabled = false;     // in colour the picture is exactly as without it
            return;
        }
        float step = MathF.Round(amount * 40f) / 40f;      // 40 steps over a fade: few rebuilds
        if (_table == null || Math.Abs(step - _tableAmount) > 0.0001f)
        {
            _table = Build(step);
            _tableAmount = step;
        }
        env.AdjustmentEnabled = true;
        env.AdjustmentBrightness = 1f;
        env.AdjustmentContrast = 1f;
        env.AdjustmentSaturation = 1f;
        env.AdjustmentColorCorrection = _table;
    }

    static ImageTexture3D Build(float k)
    {
        var layers = new Godot.Collections.Array<Image>();
        float sat = Mathf.Lerp(1f, ColourLeft, k);
        for (int b = 0; b < Size; b++)
        {
            var img = Image.CreateEmpty(Size, Size, false, Image.Format.Rgb8);
            for (int g = 0; g < Size; g++)
            for (int r = 0; r < Size; r++)
            {
                var c = new Vector3(r, g, b) / (Size - 1);
                float lum = c.Dot(new Vector3(0.299f, 0.587f, 0.114f));
                Vector3 own = new Vector3(lum, lum, lum).Lerp(c, sat);
                Vector3 tone = lum < 0.5f ? Shadow.Lerp(Middle, lum / 0.5f) : Middle.Lerp(Highlight, (lum - 0.5f) / 0.5f);
                Vector3 o = own.Lerp(tone, Tone * k);
                o = o * (1f - Lift * k) + Vector3.One * (Lift * k);
                float grey = o.Dot(new Vector3(0.299f, 0.587f, 0.114f));
                o = o.Lerp(Vector3.One * grey, 0.5f * k);
                img.SetPixel(r, g, new Color(o.X, o.Y, o.Z));
            }
            layers.Add(img);
        }
        var tex = new ImageTexture3D();
        tex.Create(Image.Format.Rgb8, Size, Size, Size, false, layers);
        return tex;
    }
}
