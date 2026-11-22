using System;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Car;

/// <summary>
/// The chief test pilot's livery (the SPUTNIK test pilots' programme, 2026-11-22): the driver's name in CCCP red under four gold stars on both sides of the car, painted by the pit mechanic in the ceremony and worn on the Sputnik after it
/// (and in the replays of those laps: LapMeta.Livery). Two flat quads just outside the doors' flush, a pixel texture from the game's own font; the quads are part of the visual and move with the body. Not on the ghost, which is only a translucent copy.
/// </summary>
public partial class CarVisual
{
    const int LiveryW = 160, LiveryH = 44;
    static readonly Color LiveryRed = new(0.80f, 0.06f, 0.07f), LiveryGold = new(1f, 0.78f, 0.12f), LiveryGoldEdge = new(0.55f, 0.36f, 0.02f);
    // the 9x9 star: '#' fill. The layout follows the body's door panel (the mesh of pyatyorka2105.glb, measured by rays along -X): the skin is flat at x 0.90 for y -0.30..0.20 over z -0.6..0.6 (the quads sit 12 mm off it, over the kit's slats at 0.91), the glass starts at y 0.37, the Sputnik kit's brake-cooling vents are the slats at |z| 0.28-0.55 below y 0.04, the door handles at y 0.17-0.23:
    // the name goes on the clear panel above the vents and under the handles (y 0.045-0.175) and the four stars under it (y -0.13..-0.06), in the gap between the two sets of vents (z -0.32..0.28, its middle -0.02).
    static readonly string[] StarRows =
    {
        "....#....", "....#....", "...###...", "#########", ".#######.", "..#####..", "..##.##..", ".##...##.", ".#.....#.",
    };
    MeshInstance3D _liveryR, _liveryL;
    ImageTexture _liveryTexture;
    Image _liveryImage;
    string _liveryName = "";
    int _liveryStars;
    float _liveryReveal = -1f;

    public string LiveryName => _liveryName;
    public int LiveryStars => _liveryStars;
    public bool HasLivery => _liveryR != null && _liveryR.Visible;

    /// <summary>Puts the livery on (stars 0..4 and a name) or takes it off (an empty name or no stars). reveal 0..1 paints it gradually: the stars one by one, then the name's letters from the left.</summary>
    public void SetLivery(string name, int stars, float reveal = 1f)
    {
        name = name ?? "";
        if (_ghost || (name.Length == 0 && stars <= 0)) { if (_liveryR != null) { _liveryR.Visible = false; _liveryL.Visible = false; } _liveryName = ""; _liveryStars = 0; _liveryReveal = -1; return; }
        reveal = Math.Clamp(reveal, 0f, 1f);
        if (_liveryR != null && _liveryName == name && _liveryStars == stars && Math.Abs(_liveryReveal - reveal) < 0.0005f && _liveryR.Visible) return;
        _liveryName = name; _liveryStars = Math.Clamp(stars, 0, 4); _liveryReveal = reveal;
        if (_liveryR == null)
        {
            _liveryImage = Image.CreateEmpty(LiveryW, LiveryH, false, Image.Format.Rgba8);
            _liveryTexture = ImageTexture.CreateFromImage(_liveryImage);
            var mat = new StandardMaterial3D
            {
                AlbedoTexture = _liveryTexture, Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor, AlphaScissorThreshold = 0.5f,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest, Roughness = 1f, CullMode = BaseMaterial3D.CullModeEnum.Back,
            };
            var quad = new QuadMesh { Size = new Vector2(1.5f, 1.5f * LiveryH / LiveryW) };      // 1.5 m of the door panel, 0.41 m tall: a pixel is 9.4 mm
            _liveryR = new MeshInstance3D { Name = "LiveryRight", Mesh = quad, MaterialOverride = mat, Position = new Vector3(0.912f, -0.012f, -0.02f), RotationDegrees = new Vector3(0, 90, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            _liveryL = new MeshInstance3D { Name = "LiveryLeft", Mesh = quad, MaterialOverride = mat, Position = new Vector3(-0.912f, -0.012f, -0.02f), RotationDegrees = new Vector3(0, -90, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(_liveryR); AddChild(_liveryL);
        }
        PaintLivery();
        _liveryR.Visible = _liveryL.Visible = true;
    }

    void PaintLivery()
    {
        var img = _liveryImage;
        img.Fill(new Color(0, 0, 0, 0));
        // stars: four slots across the top, revealed one by one over the first third of the paint
        int shown = _liveryReveal >= 1f ? _liveryStars : Math.Clamp((int)Math.Ceiling(_liveryReveal / 0.35f * _liveryStars - 0.0001f), 0, _liveryStars);
        const int starGap = 2, starW = 9;
        int totalW = _liveryStars * starW + (_liveryStars - 1) * starGap, x0 = (LiveryW - totalW) / 2;
        for (int s = 0; s < shown; s++)
            for (int y = 0; y < starW; y++)
                for (int x = 0; x < starW; x++)
                    if (StarRows[y][x] == '#') img.SetPixel(x0 + s * (starW + starGap) + x, 27 + y, LiveryGold);
        // the name: its letters from the left over the rest of the paint; twice the font where it fits
        string name = _liveryName;
        int chars = _liveryReveal >= 1f ? name.Length : Math.Clamp((int)Math.Ceiling((_liveryReveal - 0.35f) / 0.65f * name.Length - 0.0001f), 0, name.Length);
        if (chars > 0 && name.Length > 0)
        {
            int scale = PixelFont.Measure(name) * 2 <= LiveryW - 4 ? 2 : 1;
            var text = PixelFont.RenderImage(name, LiveryRed, new Color(0, 0, 0, 0), 0);
            int fullW = text.GetWidth(), visible = Math.Min(fullW, PixelFont.Measure(name[..chars]));
            var part = text.GetRegion(new Rect2I(0, 0, Math.Max(1, visible), text.GetHeight()));
            if (scale == 2) part.Resize(part.GetWidth() * 2, part.GetHeight() * 2, Image.Interpolation.Nearest);
            int nx = (LiveryW - fullW * scale) / 2, ny = 2;
            img.BlendRect(part, new Rect2I(0, 0, part.GetWidth(), part.GetHeight()), new Vector2I(nx, ny));
        }
        _liveryTexture.Update(img);
    }
}
