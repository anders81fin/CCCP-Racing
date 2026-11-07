using System;
using CCCPRacing.Audio;
using CCCPRacing.Track;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    // The demonstration owns another world; the paused race stays intact, including its actors.
    string InstructionTrack;
    SchoolCourse _instructionCourse;
    int _instructionChapter;
    Action InstructionFinished;
    RaceScene _instructionPlayer;
    SubViewport _instructionViewport;
    TextureRect _instructionImage;
    Action _restoreInstructionWorld;
    readonly System.Collections.Generic.List<Action> _resumeRaceAudio = new();

    void PauseInput(bool alt)
    {
        SyncPauseReplayRow();
        SyncPauseHud();
        if (_hud.StakhanovBoardOpen) return;
        if (_labOpen) return;      // the LAB panel takes the menu's keys (RaceScene.Lab.cs)
        if (RallyExitInput() || VisualInput(alt) || MixerInput(alt) || SettingsInput(alt) || ReplayMenuInput(alt)) return;
        if (_hud.PauseNormsOpen)
        {
            if (Input.IsActionJustPressed("pause") || Input.IsActionJustPressed("start") || Input.IsActionJustPressed("keys"))
                ClosePauseNorms();
            return;
        }
        int count = PauseRowsNow.Count;
        _hud.PauseSelection = Math.Clamp(_hud.PauseSelection, 0, count - 1);
        if (Input.IsActionJustPressed("accelerate")) _hud.PauseSelection = (_hud.PauseSelection + count - 1) % count;
        else if (Input.IsActionJustPressed("brake")) _hud.PauseSelection = (_hud.PauseSelection + 1) % count;
        else if (Input.IsActionJustPressed("start") && !alt) PauseChoose(_hud.PauseSelection);
        else if (Input.IsActionJustPressed("rescue")) PlanRestart();
        else if (Input.IsActionJustPressed("pause")) PauseChooseId(PauseRowId.Continue);
        else if (Input.IsActionJustPressed("keys")) ShowKeys();
        else if (Input.IsActionJustPressed("instructions")) EnterLessons();
        else if (Input.IsActionJustPressed("rumble")) ToggleRumble();
    }

    /// <summary>What the menu's screens read from the scene, every frame (also while a settings screen is open: they return before the main menu's own input).</summary>
    void SyncPauseHud()
    {
        _hud.PauseHasNorms = _norms.Enabled;
        _hud.PauseRowIds = PauseRowsNow.ToArray();
        _hud.RadioOn = _carRadio.On;
        _hud.RadioVolume = _carRadio.Volume;
        _hud.RadioStation = _carRadio.StationNumber;
        _hud.RadioStationCount = _carRadio.StationCount;
        _hud.AntialiasingName = AaNames[_aaMode];
        _hud.SofteningName = SoftNames[_softLevel];
        _hud.RenderingName = RenderingName;
        SyncVisualHud();
        SyncSettingsHud();
    }

    /// <summary>
    /// The Party Meeting's rows, top to bottom: the single list the draw (Hud.PauseLines), the mouse hit test and the actions (PauseChoose) all read. REPLAY first where it applies (a single-player drive
    /// that can be resumed), then NORMS AND MEDALS, DRIVING SCHOOL, SETTINGS (sounds, visuals, rumble, keys), then CONTINUE, RESTART RACE and EXIT RACE. A row is found by its id, never by a position.
    /// </summary>
    internal static System.Collections.Generic.List<PauseRowId> PauseRowList(bool norms, bool replay, bool leaderboards = false)
    {
        var l = new System.Collections.Generic.List<PauseRowId>();
        if (replay) l.Add(PauseRowId.Replay);
        if (leaderboards) l.Add(PauseRowId.Leaderboards);
        if (norms) l.Add(PauseRowId.Norms);
        l.Add(PauseRowId.Instructions); l.Add(PauseRowId.Settings);
        l.Add(PauseRowId.Continue); l.Add(PauseRowId.Restart); l.Add(PauseRowId.Exit);
        return l;
    }
    System.Collections.Generic.List<PauseRowId> PauseRowsNow => PauseRowList(_norms.Enabled, ReplayRowShown, StakhanovBoardAvailable);
    int PauseRowIndex(PauseRowId id) => PauseRowsNow.IndexOf(id);
    /// <summary>Where the cursor starts when the meeting opens: NORMS AND MEDALS when there are norms, else CONTINUE (what row 0 was before REPLAY took the top).</summary>
    int PauseStartRow => PauseRowIndex(_norms.Enabled ? PauseRowId.Norms : PauseRowId.Continue);

    // Edge smoothing of the 3D view: off, or MSAA at 2x/4x/8x; chosen in the pause menu, remembered in settings.cfg.
    static readonly string[] AaNames = { "OFF", "MSAA 2X", "MSAA 4X", "MSAA 8X" };
    static int _aaMode;

    // Softening: a screen-space blur of the 3D picture's contrasty edges only (the HUD is drawn above it).
    static readonly string[] SoftNames = { "OFF", "ON" };
    static int _softLevel;
    CanvasLayer _softLayer;
    ShaderMaterial _softMaterial;

    const string SoftenShader = @"
shader_type canvas_item;
uniform sampler2D screen_tex : hint_screen_texture, filter_nearest;
uniform float strength = 0.5;
float luma(vec3 c) { return dot(c, vec3(0.299, 0.587, 0.114)); }
void fragment() {
	vec2 px = SCREEN_PIXEL_SIZE;
	vec3 c = texture(screen_tex, SCREEN_UV).rgb;
	vec3 n = texture(screen_tex, SCREEN_UV + vec2(0.0, -px.y)).rgb;
	vec3 s = texture(screen_tex, SCREEN_UV + vec2(0.0, px.y)).rgb;
	vec3 e = texture(screen_tex, SCREEN_UV + vec2(px.x, 0.0)).rgb;
	vec3 w = texture(screen_tex, SCREEN_UV + vec2(-px.x, 0.0)).rgb;
	float lo = min(luma(c), min(min(luma(n), luma(s)), min(luma(e), luma(w))));
	float hi = max(luma(c), max(max(luma(n), luma(s)), max(luma(e), luma(w))));
	// only a strong step in brightness is an edge; the pixel textures' fine grain is left alone
	float edge = smoothstep(0.10, 0.30, hi - lo);
	vec3 blurred = (c * 2.0 + n + s + e + w) / 6.0;
	COLOR = vec4(mix(c, blurred, edge * strength), 1.0);
}
";

    void ApplySoftening()
    {
        if (InstructionTrack != null && LanPlaybackSource==null) return;
        if (_softLevel == 0) { if (_softLayer != null) _softLayer.Visible = false; return; }
        if (_softLayer == null)
        {
            _softMaterial = new ShaderMaterial { Shader = new Shader { Code = SoftenShader } };
            _softLayer = new CanvasLayer { Layer = 1 };
            var rect = new ColorRect { Material = _softMaterial, MouseFilter = Control.MouseFilterEnum.Ignore };
            _softLayer.AddChild(rect);
            AddChild(_softLayer);
            rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        }
        _softMaterial.SetShaderParameter("strength", 0.375f);
        _softLayer.Visible = true;
    }

    void StepSoftening(int step)
    {
        _softLevel = (_softLevel + step + SoftNames.Length) % SoftNames.Length;
        ApplySoftening();
        string path = _records.Dir + "settings.cfg";
        var cfg = new ConfigFile();
        cfg.Load(path);
        cfg.SetValue("video", "softening", _softLevel);
        cfg.Save(path);
    }

    // RENDERING: PIXEL (default) draws the 3D into the low-res picture and blows it up; NATIVE draws it at the window's own resolution, 1:1 (PixelFrame.Native).
    // The HUD keeps its logical 480x270-style size either way. Remembered in settings.cfg [video] rendering (0 pixel, 1 native).
    static string RenderingName => PixelFrame.Native ? "NATIVE" : "PIXEL";

    void StepRendering()
    {
        PixelFrame.Native = !PixelFrame.Native;
        _hud.RenderingName = RenderingName;
        string path = _records.Dir + "settings.cfg";
        var cfg = new ConfigFile();
        cfg.Load(path);       // keep the other sections
        cfg.SetValue("video", "rendering", PixelFrame.Native ? 1 : 0);
        cfg.Save(path);
    }

    void LoadAntialiasing(ConfigFile cfg)
    {
        var rendering = cfg.GetValue("video", "rendering", 0);
        PixelFrame.Native = rendering.VariantType == Variant.Type.Int && (int)rendering == 1;
        var soft = cfg.GetValue("video", "softening", 0);
        _softLevel = soft.VariantType == Variant.Type.Int && (int)soft > 0 ? 1 : 0;      // the earlier steps all mean on
        ApplySoftening();
        var v = cfg.GetValue("video", "antialiasing", 0);
        _aaMode = v.VariantType == Variant.Type.Int ? Math.Clamp((int)v, 0, AaNames.Length - 1) : 0;
        ApplyAntialiasing(GetViewport());
    }

    static void ApplyAntialiasing(Viewport vp)
    {
        vp.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;      // FXAA blurs the whole picture, the pixel textures too
        vp.Msaa3D = _aaMode switch
        {
            1 => Viewport.Msaa.Msaa2X, 2 => Viewport.Msaa.Msaa4X, 3 => Viewport.Msaa.Msaa8X, _ => Viewport.Msaa.Disabled,
        };
    }

    void StepAntialiasing(int step)
    {
        _aaMode = (_aaMode + step + AaNames.Length) % AaNames.Length;
        ApplyAntialiasing(GetViewport());
        string path = _records.Dir + "settings.cfg";
        var cfg = new ConfigFile();
        cfg.Load(path);       // keep the other sections
        cfg.SetValue("video", "antialiasing", _aaMode);
        cfg.Save(path);
    }

    void OpenPauseNorms(bool beforeStart = false)
    {
        _hud.PauseNormTimes = Array.ConvertAll(NormPlan.Ladder, t => t + _norms.Allowance);
        _hud.PauseNormPassedTimes = (float[])_norms.PassedTimes.Clone();
        _hud.PauseNormBeaten = _norms.Fulfilled ? NormPlan.Steps : _norms.Step;
        for (int i = 0; i < _hud.PauseNormBeaten; i++)
            if (_norms.PassedTargets[i] > 0) _hud.PauseNormTimes[i] = _norms.PassedTargets[i];
        _hud.PauseNormFinished = _norms.Finished;
        _hud.PauseNormsStart = beforeStart;
        _hud.SetPauseNorms(true);
        if (beforeStart)
        {
            _state = State.Paused;
            _hud.Mode = HudMode.Paused;
            _hud.Frozen = true;
        }
    }

    void ClosePauseNorms()
    {
        _hud.SetPauseNorms(false);
        _overlayExitFrame = Engine.GetProcessFrames();
        if (!_hud.PauseNormsStart) return;
        _hud.PauseNormsStart = false;
        _state = State.Countdown;
        _hud.Mode = HudMode.Countdown;
        _hud.Frozen = false;
    }

    void PauseChoose(int selected)
    {
        var rows = PauseRowsNow;
        if (selected < 0 || selected >= rows.Count) return;
        PauseChooseId(rows[selected]);
    }

    void PauseChooseId(PauseRowId id)
    {
        switch (id)
        {
            case PauseRowId.Replay: OpenReplayList(); break;
            case PauseRowId.Leaderboards: OpenStakhanovBoard(); break;
            case PauseRowId.Norms: OpenPauseNorms(); break;
            case PauseRowId.Continue:
                _state = _countdown > 0 ? State.Countdown : State.Racing;
                _hud.Mode = _state == State.Countdown ? HudMode.Countdown : HudMode.Race;
                break;
            case PauseRowId.Restart: PlanRestart(); break;
            case PauseRowId.Exit:
                if (InSchool) _sc.End(false);
                else if (_lastPlayed == MenuItem.Race) AskLeaveRally();
                else EnterTitle();
                break;
            case PauseRowId.Instructions: EnterLessons(); break;
            case PauseRowId.Settings: OpenSettings(); break;
        }
    }

    void OpenInstruction(int course, string track, int chapter)
    {
        string raceTrack = _trackId;
        var radars = RadarHead.All.ToArray();
        var glows = NightGlow.Glows.ToArray();
        var lamps = NightGlow.Lamps.ToArray();
        float wetness = Surfaces.Wetness;
        _restoreInstructionWorld = () =>
        {
            _trackId = raceTrack;
            RadarHead.All.Clear(); RadarHead.All.AddRange(radars);
            NightGlow.Glows.Clear(); NightGlow.Glows.AddRange(glows);
            NightGlow.Lamps.Clear(); NightGlow.Lamps.AddRange(lamps);
            Surfaces.Wetness = wetness;
            NormPlan.UseTrack(TrackDef.Load($"res://tracks/{raceTrack}.json").Norms);
            NormPlan.UseFuel(_run == RunMode.Rally ? _rallyFuel : FuelGrade.Petrol);
            AudioMix.MuteSfx(false);
        };
        foreach (var node in FindChildren("*", "", true, false))
        {
            if (node is AudioStreamPlayer audio)
            {
                bool paused = audio.StreamPaused;
                audio.StreamPaused = true;
                _resumeRaceAudio.Add(() => audio.StreamPaused = paused);
            }
            else if (node is AudioStreamPlayer3D spatial)
            {
                bool paused = spatial.StreamPaused;
                spatial.StreamPaused = true;
                _resumeRaceAudio.Add(() => spatial.StreamPaused = paused);
            }
        }
        ((CanvasLayer)_hud.GetParent()).Visible = false;
        ReleasePadKeys();
        ProcessMode = ProcessModeEnum.Disabled;
        Visible = false;
        _instructionViewport = new SubViewport
        {
            OwnWorld3D = true, HandleInputLocally = false, AudioListenerEnable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Nearest,
        };
        PixelFrame.Mirror(_instructionViewport, GetViewport());      // (PIXEL: the visible rect's size, as before; NATIVE: the parent's framebuffer)
        ApplyAntialiasing(_instructionViewport);
        GetParent().AddChild(_instructionViewport);
        _instructionImage = new TextureRect
        {
            Texture = _instructionViewport.GetTexture(), Size = _instructionViewport.Size,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        GetParent().AddChild(_instructionImage);
        _instructionImage.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _instructionPlayer = new RaceScene
        {
            InstructionTrack = track, _instructionCourse = (SchoolCourse)course,
            _instructionChapter = chapter, InstructionFinished = () => CallDeferred(nameof(CloseInstruction)),
            _dataDir = _records.Dir, _noRecord = true, _padMode = _padMode,
        };
        _instructionViewport.AddChild(_instructionPlayer);
    }

    void CloseInstruction()
    {
        Input.Singleton.JoyConnectionChanged -= _instructionPlayer.OnJoyConnection;
        _instructionViewport.Free();
        _instructionImage.Free();
        _instructionPlayer = null;
        _instructionViewport = null;
        _instructionImage = null;
        _restoreInstructionWorld();
        _restoreInstructionWorld = null;
        foreach (var resume in _resumeRaceAudio) resume();
        _resumeRaceAudio.Clear();
        ((CanvasLayer)_hud.GetParent()).Visible = true;
        ProcessMode = ProcessModeEnum.Inherit;
        Visible = true;
        _showLessons = false;
        _hud.Mode = HudMode.Paused;
        _overlayExitFrame = Engine.GetProcessFrames();
    }
}
