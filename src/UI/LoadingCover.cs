using System;
using Godot;

namespace CCCPRacing.UI;

/// <summary>
/// The loading picture at launch, a nod to the C64's tape loaders: the game's cover
/// in C64 multicolor style, gradually blending into the modern cover over flickering loading stripes
/// while the world is built underneath. The modern cover stays behind the TEST VERSION notice
/// after a short hold (the card is up 2.6 s after launch once the world is built), then fades when the notice is dismissed. Only at a plain launch (tests and captures never
/// see it), once a session: a track change rebuilds the scene without it.
/// </summary>
public partial class LoadingCover : Control
{
    const float RetroHoldTime = 0.4f, TransitionTime = 1.8f, ModernHoldTime = 0.4f;     // a brisk blend, begun once the first frames' hitches are over
    const float MinTime = RetroHoldTime + TransitionTime + ModernHoldTime;
    const float FadeTime = 0.35f, StripeRate = 1f / 25f;
    static bool _shown;
    // The loader's stripes in the game's colours: deep red and maroon, close in brightness so the
    // flicker stays gentle on the eyes, now and then a gold line (the HUD's red and gold).
    static readonly Color Maroon = new(0.30f, 0.04f, 0.03f), Crimson = new(0.52f, 0.07f, 0.05f);

    const float PictureHeight = 238f / PixelFrame.TargetHeight;     // of the screen's height: a border of stripes all round
    readonly Texture2D _picture = GD.Load<Texture2D>("res://assets/ui/loading_cover.jpg");
    readonly Texture2D _retro = GD.Load<Texture2D>("res://assets/ui/loading_cover_c64.png");
    TextureRect _retroPicture, _modernPicture;
    readonly RandomNumberGenerator _rng = new();      // the stripes only, never the game's
    float _t, _stripeT, _fade = -1f;
    bool _loaded, _noticeReady;

    /// <summary>Raised when the picture has faded: the game takes over.</summary>
    public event Action Gone;

    /// <summary>The modern cover has been fully visible for a moment (ModernHoldTime) and the world is built; show the launch notice over it.</summary>
    public event Action ReadyForNotice;

    /// <summary>A plain launch that has not shown it yet (--cover shows it with other flags too).</summary>
    public static bool Wanted
    {
        get
        {
            var args = OS.GetCmdlineUserArgs();
            return !_shown && DisplayServer.GetName() != "headless" && (args.Length == 0 || Array.IndexOf(args, "--cover") >= 0);
        }
    }

    public override void _Ready()
    {
        _shown = true;
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _retroPicture = AddPicture(_retro, TextureFilterEnum.Nearest);
        _modernPicture = AddPicture(_picture, TextureFilterEnum.LinearWithMipmaps);
        _modernPicture.Modulate = new Color(1, 1, 1, 0);
        Resized += LayoutPictures;
        LayoutPictures();
    }

    TextureRect AddPicture(Texture2D texture, TextureFilterEnum filter)
    {
        var picture = new TextureRect
        {
            Texture = texture,
            TextureFilter = filter,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(picture);
        return picture;
    }

    void LayoutPictures()
    {
        Vector2 size = GetViewportRect().Size;
        Vector2 pic = _picture.GetSize() * (MathF.Round(size.Y * PictureHeight) / _picture.GetHeight());
        var at = new Vector2(MathF.Floor((size.X - pic.X) / 2), MathF.Floor((size.Y - pic.Y) / 2));
        // Identical bounds prevent the picture from jumping during the blend.
        foreach (var picture in new[] { _retroPicture, _modernPicture })
        {
            picture.Position = at;
            picture.Size = pic;
        }
    }

    /// <summary>The world is built underneath.</summary>
    public void Loaded() => _loaded = true;

    /// <summary>Input before the notice is swallowed so the modern image gets its full three seconds.</summary>
    public void Press(InputEvent e)
    {
        bool press = e is InputEventKey { Pressed: true, Echo: false } or InputEventJoypadButton { Pressed: true } or InputEventMouseButton { Pressed: true };
        if (press && _noticeReady) Dismiss();
    }

    /// <summary>The launch notice has closed, or was disabled: fade the cover into the game.</summary>
    public void Dismiss()
    {
        if (_loaded && _fade < 0) _fade = 0;
    }

    public override void _Process(double delta)
    {
        // World construction blocks rendering, and the world's first frames hitch: never let a long
        // frame jump the blend (a twentieth of a second at most per frame).
        float dt = Math.Min((float)delta, 0.05f);
        _t += dt;
        _stripeT += dt;
        float blend = Mathf.SmoothStep(0f, 1f, Math.Clamp((_t - RetroHoldTime) / TransitionTime, 0f, 1f));
        _modernPicture.Modulate = new Color(1, 1, 1, blend);
        if (_loaded && !_noticeReady && _t >= MinTime)
        {
            _noticeReady = true;
            if (ReadyForNotice != null) ReadyForNotice.Invoke();
            else Dismiss();
        }
        if (_fade >= 0)
        {
            _fade += dt;
            Modulate = new Color(1, 1, 1, 1f - Math.Clamp(_fade / FadeTime, 0f, 1f));
            if (_fade >= FadeTime)
            {
                Gone?.Invoke();
                QueueFree();
                return;
            }
        }
        if (_stripeT >= StripeRate) { _stripeT = 0; QueueRedraw(); }
    }

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        int scale = Math.Max(1, (int)MathF.Round(size.Y / PixelFrame.TargetHeight));
        // The border: crimson and maroon bands of a few lines each, new every frame, a rare gold line.
        bool light = _rng.Randf() < 0.5f;
        for (float y = 0; y < size.Y; light = !light)
        {
            bool gold = _rng.Randf() < 0.04f;
            float h = (gold ? 1 : _rng.RandiRange(1, 7)) * scale;
            DrawRect(new Rect2(0, y, size.X, h), gold ? Hud.Gold.Darkened(0.25f) : light ? Crimson : Maroon);
            y += h;
        }
    }
}
