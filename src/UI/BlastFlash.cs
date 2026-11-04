using Godot;

namespace CCCPRacing.UI;

/// <summary>A pressure-wave white-out: a full-screen flash that snaps in and fades, for a gun going off right next to the car.</summary>
public partial class BlastFlash : CanvasLayer
{
    public float Strength = .5f;
    float _t;
    ColorRect _rect;
    public override void _Ready()
    {
        Layer = 90;
        _rect = new ColorRect { Color = new Color(1f, .96f, .86f, Strength), MouseFilter = Control.MouseFilterEnum.Ignore };
        _rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_rect);
    }
    public override void _Process(double delta)
    {
        _t += (float)delta;
        float a = Strength * Mathf.Clamp(1f - _t / .45f, 0f, 1f);
        _rect.Color = new Color(1f, .96f, .86f, a * a);
        if (_t >= .45f) QueueFree();
    }
}
