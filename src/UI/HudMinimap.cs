using System.Collections.Generic;
using Godot;

namespace CCCPRacing.UI;

/// <summary>
/// The minimap, bottom-left in a run (M, a pad's R3 or the pause menu: on and off): the whole track
/// north up, as the camera looks, the start line, the car as an arrow, the record lap's ghost and the
/// comrades of a LAN race as dots. No frame: a thin cream road with a dark edge and a soft shadow
/// (it reads over grass, snow and mud alike), smoothed lines. RaceScene fills the points in 0..1 of
/// the map's box (x east, y south).
/// </summary>
public partial class Hud
{
    public bool Minimap;
    /// <summary>The map is switched on (the pause menu's line says so).</summary>
    public bool MapOn = true;
    /// <summary>The road, 0..1 in the box (closed: the last point joins the first); null: none.</summary>
    public Vector2[] MinimapPath;
    /// <summary>The box's width over its height (the track's own proportions).</summary>
    public float MinimapAspect = 1f;
    public Vector2 MinimapCar, MinimapCarDir = Vector2.Down, MinimapStart, MinimapStartDir = Vector2.Right;
    public Vector2? MinimapGhost, MinimapMechanic, MinimapFuel, MinimapWash;
    Texture2D _mechanicIcon, _fuelIcon, _washIcon;
    public Color MinimapCarColor = Gold;
    public readonly List<(Vector2 position, Color color)> MinimapOthers = new();

    const float MapSize = 82f, MapPad = 6f;

    void DrawMinimap()
    {
        if (!Minimap || MinimapPath == null || MinimapPath.Length < 2) return;
        var s = Screen;
        float w = MinimapAspect >= 1f ? MapSize : MapSize * MinimapAspect, h = MinimapAspect >= 1f ? MapSize / MinimapAspect : MapSize;
        Vector2 o = new(MapPad, Mathf.Floor(s.Y - h - MapPad)), size = new(w, h);
        Vector2 P(Vector2 p) => o + p * size;

        var pts = new Vector2[MinimapPath.Length + 1];
        for (int i = 0; i < MinimapPath.Length; i++) pts[i] = P(MinimapPath[i]);
        pts[^1] = pts[0];
        var shadow = new Vector2[pts.Length];
        for (int i = 0; i < pts.Length; i++) shadow[i] = pts[i] + new Vector2(1.2f, 1.2f);
        DrawPolyline(shadow, new Color(0, 0, 0, 0.35f), 3.6f, true);              // a soft shadow
        DrawPolyline(pts, new Color(0.08f, 0.05f, 0.04f, 0.75f), 3.2f, true);     // the road's dark edge
        DrawPolyline(pts, new Color(Paper, 0.95f), 1.4f, true);                   // the road

        // The start line across the road: red with a gold tip.
        Vector2 st = P(MinimapStart), across = new Vector2(-MinimapStartDir.Y, MinimapStartDir.X);
        DrawLine(st - across * 3.5f, st + across * 3.5f, new Color(0.08f, 0.05f, 0.04f, 0.8f), 2.6f, true);
        DrawLine(st - across * 3f, st + across * 3f, Red, 1.4f, true);
        DrawCircle(st + across * 3f, 0.9f, Gold, true, -1f, true);

        if (MinimapFuel is Vector2 fuel)
        {
            _fuelIcon ??= GD.Load<Texture2D>(CCCPRacing.Track.FuelDepot.IconPath);
            Vector2 centre = P(fuel);
            DrawCircle(centre, 5.4f, new Color(0.12f, 0.06f, 0.08f, 0.90f));
            DrawTextureRect(_fuelIcon, new Rect2(centre - Vector2.One * 5, Vector2.One * 10), false);
        }

        if (MinimapMechanic is Vector2 mechanic)
        {
            _mechanicIcon ??= GD.Load<Texture2D>("res://assets/ui/mechanic-wrench.svg");
            Vector2 centre = P(mechanic);
            // A dark backing separates the gold wrench from the cream road and snow.
            DrawCircle(centre, 5.4f, new Color(0.12f, 0.06f, 0.08f, 0.90f));
            DrawTextureRect(_mechanicIcon, new Rect2(centre - Vector2.One * 5, Vector2.One * 10), false);
        }

        if (MinimapWash is Vector2 wash)
        {
            _washIcon ??= GD.Load<Texture2D>("res://assets/ui/wash.svg");
            Vector2 centre = P(wash);
            DrawCircle(centre, 5.4f, new Color(0.12f, 0.06f, 0.08f, 0.90f));
            DrawTextureRect(_washIcon, new Rect2(centre - Vector2.One * 5, Vector2.One * 10), false);
        }

        if (MinimapGhost is Vector2 g) DrawCircle(P(g), 1.8f, new Color(Grey, 0.85f), true, -1f, true);
        foreach (var (c,color) in MinimapOthers)
        {
            DrawCircle(P(c), 2.6f, new Color(0.08f, 0.05f, 0.04f, 0.7f), true, -1f, true);
            DrawCircle(P(c), 1.9f, color, true, -1f, true);
        }

        // The car: an arrow the way it points, gold with a dark outline.
        Vector2 at = P(MinimapCar), f = MinimapCarDir.LengthSquared() > 1e-6f ? MinimapCarDir.Normalized() : Vector2.Down, r = new(-f.Y, f.X);
        var tri = new[] { at + f * 4.5f, at - f * 2.8f + r * 3f, at - f * 1.6f, at - f * 2.8f - r * 3f };
        DrawColoredPolygon(tri, MinimapCarColor);
        DrawPolyline(new[] { tri[0], tri[1], tri[2], tri[3], tri[0] }, new Color(0.1f, 0.05f, 0.03f, 0.9f), 1.2f, true);
    }
}
