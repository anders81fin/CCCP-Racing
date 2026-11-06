using System;
using CCCPRacing.Audio;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.UI;

/// <summary>
/// Renders the game into a low-res SubViewport and blows it up with an integer scale.
/// The low-res size is derived from the window so the whole screen is used without borders
/// or stretching: 1920×1080 → 480×270 ×4, 1366×768 → 456×256 ×3, 2560×1440 → 512×288 ×5.
/// VISUALS > RENDERING NATIVE (<see cref="Native"/>) keeps that logical size for the 2D (HUD, menus, mouse, Camera3D projection: Size2DOverride with
/// stretch) but gives the SubViewport a physical framebuffer of logical × PixelScale, shown 1:1, so the 3D is drawn at the window's own resolution.
/// </summary>
public partial class PixelFrame : Control
{
    public const float TargetHeight = 270f;

    SubViewport _vp;
    TextureRect _rect;
    RaceScene _race;
    LoadingCover _cover;
    RadioMusic _startupMusic;
    SubViewport _overlay;
    TextureRect _overlayRect;
    bool _previewLayout, _fullLayout, _coverIntro, _coverPlanned;

    public int PixelScale { get; private set; } = 1;

    static bool _native;
    static PixelFrame _current;

    /// <summary>VISUALS > RENDERING: false PIXEL (the default, the low-res picture blown up), true NATIVE. Switching refits the frame at once; nested viewports follow next frame (<see cref="Mirror"/>).</summary>
    public static bool Native
    {
        get => _native;
        set { if (_native == value) return; _native = value; if (IsInstanceValid(_current)) _current.Relayout(); }
    }

    /// <summary>NATIVE in effect: never in the exporter child, whose movie keeps its fixed 480x270 (or debug full-res) pixel contract.</summary>
    public static bool NativeActive => _native && !ExportChild.Active;

    /// <summary>The physical pixels per logical pixel the game viewport renders at now (1 in PIXEL).</summary>
    public int RenderScale => NativeActive ? PixelScale : 1;

    /// <summary>
    /// Size a viewport for a logical (2D) size at k physical pixels per logical one. k = 1 is the plain PIXEL viewport (no override), exactly as before;
    /// k &gt; 1 a k-times framebuffer whose 2D canvas, visible rect and mouse stay in the logical pixels. Assigns only what changed.
    /// </summary>
    public static void Fit(SubViewport vp, Vector2I logical, int k)
    {
        k = Math.Max(1, k);
        var physical = logical * k;
        var over = k > 1 ? logical : Vector2I.Zero;
        if (vp.Size != physical) vp.Size = physical;
        if (vp.Size2DOverride != over) vp.Size2DOverride = over;
        if (vp.Size2DOverrideStretch != k > 1) vp.Size2DOverrideStretch = k > 1;
    }

    /// <summary>A nested full-screen viewport (replay viewer, demonstration) takes its parent's physical size and logical override, so it follows PIXEL/NATIVE and the window.</summary>
    public static void Mirror(SubViewport child, Viewport parent)
    {
        if (parent is SubViewport p)
        {
            if (child.Size != p.Size) child.Size = p.Size;
            if (child.Size2DOverride != p.Size2DOverride) child.Size2DOverride = p.Size2DOverride;
            if (child.Size2DOverrideStretch != p.Size2DOverrideStretch) child.Size2DOverrideStretch = p.Size2DOverrideStretch;
        }
        else Fit(child, (Vector2I)parent.GetVisibleRect().Size, 1);
    }

    public override void _Ready()
    {
        if (ExportChild.Active) CCCPRacing.Export.ExportWindowHider.EnsureCurrentProcessHidden();
        GetTree().AutoAcceptQuit=false;
        _current = this;
        _vp = new SubViewport
        {
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Msaa3D = Viewport.Msaa.Disabled,
            CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Nearest,
            HandleInputLocally = false,
            // The game camera lives in here, not in the root window: without this every
            // AudioStreamPlayer3D (jets, tractors, tanks, explosions) had no listener and was silent.
            AudioListenerEnable3D = true,
        };
        AddChild(_vp);

        _rect = new TextureRect
        {
            Texture = _vp.GetTexture(),
            TextureFilter = TextureFilterEnum.Nearest,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_rect);

        // Keep the menu at full resolution while the live world fits the preview window.
        _overlay = new SubViewport
        {
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            HandleInputLocally = false,
        };
        AddChild(_overlay);
        _overlayRect = new TextureRect
        {
            Texture = _overlay.GetTexture(), TextureFilter = TextureFilterEnum.Nearest,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_overlayRect);
        GetViewport().SizeChanged += Relayout;
        Relayout();
        UpdateMouse();
        if (LoadingCover.Wanted && !Array.Exists(OS.GetCmdlineUserArgs(),a=>a.StartsWith("--lan-replay=")))
        {
            // Keep Godot's boot splash up while the synchronous world build runs. The game cover
            // is added only when the world is ready, so its transition can animate from its first frame.
            _coverPlanned = true;
            AudioMix.Setup(null);
            AudioMix.MuteSfx(true);
            _startupMusic = new RadioMusic { Subtle = true, VolumeDb = 2f };
            // EnterTitle calls Play during BuildRace: initialize the shared player first.
            AddChild(_startupMusic);
            BuildRace();
            _cover = new LoadingCover();
            _cover.Gone += () => { _cover = null; _race.CoverSetsProcessMode(ProcessModeEnum.Inherit); };      // (a replay viewer open over the world keeps it frozen)
            _cover.ReadyForNotice += ShowCoverIntro;
            AddChild(_cover);
            _cover.Loaded();
            _startupMusic.Play(immediate: true);
        }
        else BuildRace();
    }

    void BuildRace()
    {
        string lanReplay=Array.Find(OS.GetCmdlineUserArgs(),a=>a.StartsWith("--lan-replay="));
        if(lanReplay!=null) {
            try {
                var source=new CCCPRacing.Net.LanReplayFile.Reader(lanReplay["--lan-replay=".Length..]);
                _race=RaceScene.CreateLanPlayback(source);
                _vp.AddChild(_race);return;
            } catch(Exception e) {GD.PushError("LAN replay could not open: "+e.Message);CCCPRacing.Audio.AudioMix.Quit(this, 2);return;}
        }
        _race = new RaceScene { StartupMusic = _startupMusic };
        if (_coverPlanned || _cover != null) _race.ProcessMode = ProcessModeEnum.Disabled;
        _vp.AddChild(_race);
    }

    public override void _ExitTree() { if (_current == this) _current = null; }

    public override void _Notification(int what)
    {
        if(what==NotificationWMCloseRequest && !(CCCPRacing.Net.LanSession.Current?.RequestWindowClose() ?? false)) RequestQuit();
    }

    public void RequestQuit(int exitCode = 0) => AudioMix.Quit(this, exitCode);

    void ShowCoverIntro()
    {
        if (!_race.StartupIntroVisible) { _cover.Dismiss(); return; }
        _coverIntro = true;
        // The actual launch HUD runs above the cover while the world stays paused.
        _race.PreviewHudLayer.Reparent(_overlay);
        MoveChild(_overlayRect, GetChildCount() - 1);
        Relayout();
    }

    void CloseCoverIntro()
    {
        _coverIntro = false;
        _race.PrepareStartupArchive();
        _race.PreviewHudLayer.Reparent(_race);
        Relayout();
        // Start the title replay beneath the cover's fade so no frozen menu/world frame is exposed.
        _race.CoverSetsProcessMode(ProcessModeEnum.Inherit);
        _cover.Dismiss();
    }

    void Relayout()
    {
        Vector2 s = GetViewportRect().Size;
        PixelScale = Math.Max(1, (int)MathF.Round(s.Y / TargetHeight));
        var size = new Vector2I((int)MathF.Ceiling(s.X / PixelScale), (int)MathF.Ceiling(s.Y / PixelScale));
        int k = RenderScale;
        Fit(_overlay, size, k);
        _overlayRect.Size = size * PixelScale;
        bool overlayVisible = _previewLayout || _coverIntro;
        _overlayRect.Visible = overlayVisible;
        _overlay.RenderTargetUpdateMode = overlayVisible ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        var video = _previewLayout && !_fullLayout ? Hud.PreviewRect(size) : new Rect2(Vector2.Zero, size);
        Fit(_vp, new Vector2I((int)video.Size.X, (int)video.Size.Y), k);      // (NATIVE: the TextureRect below shows the k-times texture 1:1)
        _rect.Position = video.Position * PixelScale;
        _rect.Size = video.Size * PixelScale;
    }

    public override void _Process(double delta)
    {
        if (ExportChild.Active) CCCPRacing.Export.ExportWindowHider.EnsureCurrentProcessHidden();
        UpdatePointers(GetGlobalTransformWithCanvas().AffineInverse() * GetViewport().GetMousePosition());      // also refresh after a resize/warp when the OS emits no motion event
        if (_race == null || _race.Booting) return;
        if (_coverIntro) return;     // keep the launch notice above the cover until it is dismissed
        if (_previewLayout == _race.PresentationActive && _fullLayout == _race.PresentationFullscreen) return;
        _previewLayout = _race.PresentationActive;
        _fullLayout = _race.PresentationFullscreen;
        Node hudParent = _previewLayout ? _overlay : _race;
        if (_race.PreviewHudLayer.GetParent() != hudParent) _race.PreviewHudLayer.Reparent(hudParent);
        _race.SetPreviewTexture(_previewLayout ? _vp.GetTexture() : null);
        Relayout();
    }

    public override void _Input(InputEvent e)
    {
        // RawInput is routed manually below, so the subviewports otherwise never learn the mouse position.
        // Their input coordinates remain logical in both PIXEL and NATIVE (the 2D override only stretches drawing).
        if (e is InputEventMouse mouse)
        {
            var at = GetGlobalTransformWithCanvas().AffineInverse() * mouse.Position;
            UpdatePointers(at);
        }
        if (e is InputEventKey { Pressed: true, Echo: false } k &&
            ((k.Keycode == Key.Enter && k.AltPressed) || k.Keycode == Key.F11))
        {
            var mode = DisplayServer.WindowGetMode();
            bool full = mode is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen;
            DisplayServer.WindowSetMode(full ? DisplayServer.WindowMode.Windowed : DisplayServer.WindowMode.ExclusiveFullscreen);
            UpdateMouse();
            GetViewport().SetInputAsHandled();
        }
        else if (TrackLoadingScreen.Active?.IsLan==true)
        {
            TrackLoadingScreen.Active.Press(e);GetViewport().SetInputAsHandled();
        }
        else if (_race?.LanLeaveActive==true)
        {
            _race.RawInput(e);
            GetViewport().SetInputAsHandled();
        }
        else if (TrackLoadingScreen.Active != null)
        {
            TrackLoadingScreen.Active.Press(e);
            GetViewport().SetInputAsHandled();
        }
        else if (_cover != null)
        {
            if (_coverIntro)
            {
                _race.RawInput(e);
                if (!_race.StartupIntroVisible) CloseCoverIntro();
            }
            else _cover.Press(e);
            GetViewport().SetInputAsHandled();
        }
        else _race?.RawInput(e);
    }

    void UpdatePointers(Vector2 at)
    {
        if (!IsInstanceValid(_rect) || !IsInstanceValid(_overlayRect)) return;
        UpdatePointer(_vp, (at - _rect.Position) / PixelScale);
        UpdatePointer(_overlay, (at - _overlayRect.Position) / PixelScale);
    }

    static void UpdatePointer(SubViewport viewport, Vector2 logicalPosition)
    {
        if (!IsInstanceValid(viewport) || viewport.GetMousePosition().IsEqualApprox(logicalPosition)) return;
        // Update cursor/hover without sending another click or relative camera movement, or consuming the window's event.
        bool local = viewport.HandleInputLocally;
        viewport.HandleInputLocally = true;
        try { viewport.PushInput(new InputEventMouseMotion { Position = logicalPosition, GlobalPosition = logicalPosition }, true); }
        finally { viewport.HandleInputLocally = local; }
    }

    static void UpdateMouse()
    {
        var mode = DisplayServer.WindowGetMode();
        bool full = mode is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen;
        Input.MouseMode = full ? Input.MouseModeEnum.Hidden : Input.MouseModeEnum.Visible;
    }
}

/// <summary>Registers input actions in code so project.godot stays readable.</summary>
public static class InputSetup
{
    public static void Ensure()
    {
        Add("accelerate", Key.Up, Key.W);
        Add("brake", Key.Down, Key.S);
        Add("steer_left", Key.Left, Key.A);
        Add("steer_right", Key.Right, Key.D);
        Add("handbrake", Key.Space);
        Add("clutch", Key.Shift);          // both Shift keys: the key location is left unspecified
        Add("horn", Key.Ctrl);             // both Ctrl keys
        Add("rescue", Key.R);
        Add("pause", Key.Escape);
        Add("start", Key.Enter, Key.KpEnter);
        Add("debug", Key.F1);
        Add("keys", Key.K);                // the key card
        Add("instructions", Key.I);        // pause menu: the driving school's instructions
        Add("tour", Key.T);                // CHOOSE TRACK: the track's scenic tour
        Add("comments", Key.Tab);          // centre-screen comments on/off
        Add("camera", Key.C);              // top-down / cinematic / bonnet
        Add("minimap", Key.M);             // the minimap on/off
        Add("rumble", Key.V);              // pause menu: the pad's rumble on/off
        Add("radio_toggle", Key.Backspace);
        // Finnish layout: + is the US - position.
        Add("radio_volume_down", Key.Key0, Key.KpSubtract);
        Add("radio_volume_up", Key.Minus, Key.KpAdd);
    }

    static void Add(string action, params Key[] keys)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);
        foreach (var k in keys)
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = k, Device = -1 });   // any device: the keys a pad presses (RaceScene.Pad.cs) too
    }
}
