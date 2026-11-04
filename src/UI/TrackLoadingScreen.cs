using System;
using CCCPRacing.Audio;
using System.Threading.Tasks;
using Godot;

namespace CCCPRacing.UI;

/// <summary>A pixel-aligned loading card that survives rebuilding the current scene.</summary>
public partial class TrackLoadingScreen : CanvasLayer
{
    public static TrackLoadingScreen Active { get; private set; }
    SubViewport _viewport;
    TextureRect _image;
    TextureRect _background;
    ImageTexture _menuTexture;
    Hud _hud;
    Action _continue;
    ulong _readyFrame;
    RadioMusic _music;
    float _progress;

    /// <summary>The bar (0..1): only grows, stays below 1 until the world is ready (Loaded sets 1).</summary>
    public float Progress => _progress;
    /// <summary>The world is built and drawn once; a press now continues.</summary>
    public bool IsReady => _continue != null;

    /// <summary>Build-stage progress from the scene that builds for this card (RaceScene.LoadProgress.cs). Ignored for a LAN card, after ready and when lower than before.</summary>
    public void ReportBuild(float fraction)
    {
        if (IsLan || _hud == null || _continue != null || _hud.LoadingTrackReady || !float.IsFinite(fraction)) return;
        _hud.LoadingBarShown = true;      // (the bar is the normal card's only: a LAN card never gets here)
        _progress = Math.Max(_progress, Math.Clamp(fraction, 0f, Race.LoadProgress.Cap));
        _hud.LoadingProgress = _progress;
    }

    /// <summary>The title music carries on over the card (the scene that played it is replaced while the world builds).</summary>
    public bool HasMusic => _music != null;
    public void AdoptMusic(RadioMusic from)
    {
        if (from == null || !from.Playing || _music != null) return;
        _music = from.Continuation(this);
        from.CutNow();
    }

    public static TrackLoadingScreen Show(SceneTree tree, bool pad)
    {
        var screen = new TrackLoadingScreen
        {
            Layer = 100,
            // Keep the menu visible even when its scene is freed during a track change.
            _menuTexture = ImageTexture.CreateFromImage(tree.Root.GetTexture().GetImage()),
        };
        Active = screen;
        tree.Root.AddChild(screen);
        screen._hud.Pad = pad;
        return screen;
    }

    public override void _Ready()
    {
        _background = new TextureRect
        {
            Texture = _menuTexture, TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(_background);
        _viewport = new SubViewport
        {
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            HandleInputLocally = false,
            CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Nearest,
        };
        AddChild(_viewport);
        _hud = new Hud { LoadingTrack = true };
        _viewport.AddChild(_hud);
        _image = new TextureRect
        {
            Texture = _viewport.GetTexture(), TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        AddChild(_image);
        GetTree().Root.SizeChanged += Relayout;
        Relayout();
    }

    void Relayout()
    {
        Vector2 s = GetTree().Root.GetVisibleRect().Size;
        _background.Size = s;
        int scale = Math.Max(1, (int)MathF.Round(s.Y / PixelFrame.TargetHeight));
        var size = new Vector2I((int)MathF.Ceiling(s.X / scale), (int)MathF.Ceiling(s.Y / scale));
        _viewport.Size = size;
        _image.Size = size * scale;
    }

    public async Task PresentLoadingHeading()
    {
        // The synchronous build cannot animate a heading. Finish revealing it before that work begins.
        do
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        } while (!_hud.LoadingTrackHeadingComplete);
    }

    public async void Loaded(Action resume)
    {
        // Let the rebuilt world's first frame render before offering to continue.
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _continue = resume;
        if (!IsLan) { _progress = 1f; _hud.LoadingProgress = 1f; _hud.LoadingBarShown = true; }
        _readyFrame = Engine.GetProcessFrames();
        _hud.LoadingTrackReady = true;
    }

    public void Press(InputEvent e)
    {
        if(PressLan(e)) return;
        if (e is InputEventJoypadButton { Pressed: true }) _hud.Pad = true;
        else if (e is InputEventKey { Pressed: true }) _hud.Pad = false;
        bool pressed = e is InputEventKey { Pressed: true, Echo: false }
            or InputEventMouseButton { Pressed: true }
            or InputEventJoypadButton { Pressed: true };
        if (!pressed || _continue == null || Engine.GetProcessFrames() <= _readyFrame) return;
        var resume = _continue;
        _continue = null;
        Cancel();
        resume();
    }

    public void Cancel()
    {
        if (Active == this) Active = null;
        Hide();
        // The music fades out as the run starts; the card (and its player) goes once it is silent.
        if (_music != null) { _music.Stop(); GetTree().CreateTimer(1.6).Timeout += QueueFree; }
        else QueueFree();
    }

    public override void _ExitTree()
    {
        GetTree().Root.SizeChanged -= Relayout;
        if (Active == this) Active = null;
    }
}
