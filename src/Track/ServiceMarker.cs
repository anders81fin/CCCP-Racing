using System;
using Godot;

namespace CCCPRacing.Track;

/// <summary>A camera-facing service icon with a soft pulse, occluded by world geometry.</summary>
public partial class ServiceMarker : Node3D
{
    readonly Sprite3D _icon, _halo;
    float _time;

    public ServiceMarker() : this(MechanicMarker.IconPath) { }

    public ServiceMarker(string iconPath)
    {
        Name = "ServiceMarker";
        var texture = GD.Load<Texture2D>(iconPath);
        _halo = Sprite(texture, 0.085f, 0);
        _halo.Modulate = new Color(1f, 0.77f, 0.35f, 0.16f);
        AddChild(_halo);
        _icon = Sprite(texture, 0.065f, 1);
        AddChild(_icon);
    }

    static Sprite3D Sprite(Texture2D texture, float pixels, int priority) => new()
    {
        Texture = texture, PixelSize = pixels,
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        Shaded = false, NoDepthTest = false, RenderPriority = priority,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };

    public override void _Process(double delta)
    {
        _time += (float)delta;
        float bob = MathF.Sin(_time * 1.6f) * 0.18f;
        _icon.Position = _halo.Position = new Vector3(0, bob, 0);
        _halo.Modulate = new Color(1f, 0.77f, 0.35f, 0.13f + 0.035f * MathF.Sin(_time * 1.6f));
    }
}
