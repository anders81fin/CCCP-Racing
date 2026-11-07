using Godot;

namespace CCCPRacing.UI;

public partial class Hud
{
    /// <summary>VISUALS of the Party Meeting: map, comments, the camera, edges, softening, back.</summary>
    public bool VisualOpen;
    public int VisualSelection;
    public VisualRowId[] VisualRowIds = { VisualRowId.Comments, VisualRowId.LapInfo, VisualRowId.Map, VisualRowId.CarInstruments, VisualRowId.Rendering, VisualRowId.Edges, VisualRowId.Softening, VisualRowId.Back };
    /// <summary>RENDERING: PIXEL (the low-res picture blown up, the default) or NATIVE (the 3D at the window's own resolution, PixelFrame.Native).</summary>
    public string RenderingName = "PIXEL";
    /// <summary>The chosen gameplay camera's name (null or empty: no camera row).</summary>
    public string VisualCameraName;
    /// <summary>HUD visibility choices (VISUALS): visibility only, all on by default; the warnings, prompts, pause menu and map never depend on them.</summary>
    public bool CarInstrumentsOn = true, LapInfoOn = true, NormsCardOn = true;

    public void SetVisualOpen(bool open)
    {
        VisualOpen = open;
        _menuTextClock = 0;
        if (open) VisualSelection = 0;
    }

    internal Rect2 VisualRect
    {
        get
        {
            float width = Mathf.Min(280, Screen.X - 12), height = 38 + VisualRowIds.Length * 16 + 20;
            return new Rect2(Mathf.Floor((Screen.X - width) / 2), Mathf.Floor((Screen.Y - height) / 2), width, height);
        }
    }

    public int VisualRowAt(Vector2 pos)
    {
        var r = VisualRect;
        if (pos.X < r.Position.X + 4 || pos.X >= r.End.X - 4) return -1;
        int row = (int)Mathf.Floor((pos.Y - r.Position.Y - 36) / 16);
        return row >= 0 && row < VisualRowIds.Length ? row : -1;
    }

    /// <summary>A row's left label and its right-aligned value (null: none).</summary>
    internal void VisualRowTexts(VisualRowId id, out string label, out string value)
    {
        value = null;
        switch (id)
        {
            case VisualRowId.Map: label = "MAP"; value = MapOn ? "ON" : "OFF"; break;
            case VisualRowId.Comments: label = "COMMENTS"; value = Comments ? "ON" : "OFF"; break;
            case VisualRowId.Camera: label = "CAMERA"; value = "< " + VisualCameraName + " >"; break;
            case VisualRowId.Rendering: label = "RENDERING"; value = "< " + RenderingName + " >"; break;
            case VisualRowId.Edges: label = "EDGES"; value = "< " + AntialiasingName + " >"; break;
            case VisualRowId.Softening: label = "SOFTENING"; value = "< " + SofteningName + " >"; break;
            case VisualRowId.CarInstruments: label = "CAR INSTRUMENTS"; value = CarInstrumentsOn ? "ON" : "OFF"; break;
            case VisualRowId.LapInfo: label = "LAP INFO"; value = LapInfoOn ? "ON" : "OFF"; break;
            case VisualRowId.NormsCard: label = "NORMS INFO"; value = NormsCardOn ? "ON" : "OFF"; break;
            default: label = P("ESC  BACK", "B    BACK"); break;
        }
    }

    internal bool? VisualToggleValue(VisualRowId id) => id switch
    {
        VisualRowId.Map => MapOn,
        VisualRowId.Comments => Comments,
        VisualRowId.Softening => SofteningName == "ON",
        VisualRowId.CarInstruments => CarInstrumentsOn,
        VisualRowId.LapInfo => LapInfoOn,
        VisualRowId.NormsCard => NormsCardOn,
        _ => null,
    };

    internal bool? VisualSwitchValueAt(int row, Vector2 p) => row >= 0 && row < VisualRowIds.Length && VisualToggleValue(VisualRowIds[row]) != null
        ? OnOffValueAt(OnOffRowRect(VisualRect, VisualRect.Position.Y + 38 + row * 16), p) : null;

    internal void DrawVisual()
    {
        if (CameraHintOpen) { DrawCameraHint(); return; }
        var r = VisualRect;
        Panel(r, 1f);
        DrawHeading("VISUALS", Screen.X / 2, r.Position.Y + 8, _menuTextClock,
            PixelFont.Measure("VISUALS", 2) <= r.Size.X - 12 ? 2 : 1);
        float x = r.Position.X + (r.Size.X < 240 ? 10 : 18);
        for (int i = 0; i < VisualRowIds.Length; i++)
        {
            float y = r.Position.Y + 38 + i * 16;
            bool selected = i == VisualSelection;
            if (selected) DrawRect(new Rect2(r.Position.X + 4, y - 2, r.Size.X - 8, 12), new Color(Red, 0.55f));
            Color c = selected ? Gold : Paper;
            VisualRowTexts(VisualRowIds[i], out string label, out string value);
            PixelFont.Draw(this, label, new Vector2(x, y), c);
            if (VisualToggleValue(VisualRowIds[i]) is bool on) DrawOnOffSwitch(OnOffRowRect(r, y).Position, on, selected);
            else if (value != null) PixelFont.Draw(this, value, new Vector2(r.End.X - 12 - PixelFont.Measure(value), y), c);
        }
        string hint = P("UP/DOWN CHOOSE  LEFT/RIGHT CHANGE", "D-PAD CHOOSE / CHANGE  A SELECT");
        if (PixelFont.Measure(hint) > r.Size.X - 12) hint = P("UP/DOWN SELECT  < > CHANGE", "D-PAD SELECT / CHANGE");
        PixelFont.DrawCentered(this, hint, Screen.X / 2, r.End.Y - 12, Grey);
    }
}
