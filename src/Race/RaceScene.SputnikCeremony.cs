using System;
using System.Collections.Generic;
using System.Linq;
using CCCPRacing.Track;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The SPUTNIK test pilots' programme's ceremonies (SputnikProgram.cs, the owner's order of 2026-11-22), over the title after the run that earned them: one ceremony for everything new at once, the greatest recognition on the card
/// (HudSputnik.cs), the grant letter listing all that was granted, and for the chief test pilot the pit mechanic painting the name and four stars on the car (this file's paint scene; CarVisual.Livery.cs draws the paint). Medals already on file
/// from before the programme count: the ceremony comes at the first title. Each recognition is shown once (campaign.cfg [sputnik_presented]); the painted name (campaign.cfg [sputnik_livery]) is kept as it was painted.
/// </summary>
public partial class RaceScene
{
    enum SpStage { None, Card, Letter, Paint }
    SpStage _spStage;
    SputnikAward _spMain;
    SputnikAward[] _spFresh = Array.Empty<SputnikAward>();
    DrunkMen.Figure _spMech;
    Node3D _spBrush;
    string _spName = "";

    bool SputnikPaintActive => _spStage == SpStage.Paint && _state == State.Title;

    static string SputnikTrackName(string id) => Tracks.First(t => t.id == id).name;

    void BeginSputnikCeremony()
    {
        if (_spStage != SpStage.None || _fuelCeremony != null || ViewerMode || _previewMovie || InstructionTrack != null || _campaign == null || _state != State.Title) return;
        var standing = _campaign.SputnikStanding();
        var fresh = Enum.GetValues<SputnikAward>().Where(a => standing.Has(a) && !_campaign.SputnikPresented(a)).ToArray();
        if (fresh.Length == 0) return;
        _spFresh = fresh;
        _spMain = SputnikStanding.MainOf(fresh).Value;
        var old = Enum.GetValues<SputnikAward>().Where(a => standing.Has(a) && _campaign.SputnikPresented(a)).ToArray();
        _spName = DriverName;
        _spStage = SpStage.Card;
        _hud.SpBegin(_spMain, fresh, old, _spName, standing.Medal, SputnikStanding.AllTracks.Select(SputnikTrackName).ToArray());
        _overlayExitFrame = Engine.GetProcessFrames();
        if (OS.HasEnvironment("SPUTNIKDBG")) GD.Print($"SPUTNIK ceremony: main {_spMain}, new [{string.Join(",", fresh)}], old [{string.Join(",", old)}], name '{_spName}'");
    }

    void SputnikCeremonyFrame(float dt)
    {
        if (_spStage == SpStage.None) return;
        if (_state != State.Title) { SputnikCeremonyClose(false); return; }
        dt = Math.Min(dt, 0.1f);
        if (_spStage == SpStage.Paint) { SputnikPaintFrame(dt); return; }
        _hud.SpTick(dt);
    }

    bool SputnikCeremonyInput(InputEvent e)
    {
        if (!SputnikCeremonyActive) return false;
        bool press = e is InputEventKey { Pressed: true, Echo: false } or InputEventMouseButton { Pressed: true } or InputEventJoypadButton { Pressed: true };
        if (!press) return true;
        if (_spStage == SpStage.Paint)
        {
            if (_hud.SpPaintDone && _hud.SpPaintAge > _hud.SpPaintDoneAt + 1.2f) SputnikCeremonyClose(true);
            return true;
        }
        if (!_hud.SpCanClose) return true;
        if (_spStage == SpStage.Card && _spFresh.Length > 1) { _spStage = SpStage.Letter; _hud.SpPage1(); return true; }
        if (_spMain == SputnikAward.Chief && _spFresh.Contains(SputnikAward.Chief)) { SputnikPaintStart(); return true; }
        SputnikCeremonyClose(true);
        return true;
    }

    /// <summary>The ceremony is over (or the title went away under it): everything shown is marked as presented, the paint kept, the scene taken down.</summary>
    void SputnikCeremonyClose(bool mark)
    {
        if (mark)
        {
            foreach (var a in _spFresh) _campaign.PresentSputnik(a);
            if (_spFresh.Contains(SputnikAward.Chief) && _campaign.SputnikPaintedName.Length == 0) _campaign.PaintSputnikName(_spName);
        }
        else if (_spStage == SpStage.Paint && _hud.SpPaintDone) { foreach (var a in _spFresh) _campaign.PresentSputnik(a); _campaign.PaintSputnikName(_spName); }
        SputnikMechRemove();
        _liveryPaint = null;
        _spStage = SpStage.None;
        _hud.SpEnd();
        _overlayExitFrame = Engine.GetProcessFrames();
        if (mark) BeginSputnikCeremony();
    }

    // ------------------------------------------------------------------ the paint scene

    void SputnikPaintStart()
    {
        _spStage = SpStage.Paint;
        _hud.SpPaintOn = true; _hud.SpPaintAge = 0; _hud.SpPaintReveal = 0; _hud.SpPaintDone = false; _hud.SpPaintDoneAt = PaintDoneAt;
        _liveryPaint = (_spName.Length == 0 ? "PILOT" : _spName, 4, 0f);
        SputnikMechBuild();
        _overlayExitFrame = Engine.GetProcessFrames();
    }

    const float PaintFrom = 1.6f, PaintTo = 8.6f, PaintDoneAt = 9.4f;

    void SputnikMechBuild()
    {
        SputnikMechRemove();
        _spMech = DrunkMen.BuildFigure(new Color(0.18f, 0.24f, 0.42f));      // a blue overall
        _spMech.Bottle.Visible = false;
        _spBrush = new Node3D { Position = new Vector3(0, -0.55f, -0.08f) };
        _spBrush.AddChild(Props.Part(Props.Box(0.035f, 0.30f, 0.035f), new Color(0.55f, 0.4f, 0.2f), new Vector3(0, 0.05f, 0)));
        _spBrush.AddChild(Props.Part(Props.Box(0.07f, 0.09f, 0.04f), new Color(0.80f, 0.06f, 0.07f), new Vector3(0, -0.14f, 0)));
        _spMech.ArmR.AddChild(_spBrush);
        AddChild(_spMech.Root);
    }

    void SputnikMechRemove()
    {
        if (_spMech != null && IsInstanceValid(_spMech.Root)) _spMech.Root.QueueFree();
        _spMech = null; _spBrush = null;
    }

    float _spPaintT => _hud.SpPaintAge;

    void SputnikPaintFrame(float dt)
    {
        _hud.SpPaintAge += dt;
        float t = _hud.SpPaintAge, reveal = Math.Clamp((t - PaintFrom) / (PaintTo - PaintFrom), 0f, 1f);
        _hud.SpPaintReveal = reveal;
        bool wasDone = _hud.SpPaintDone;
        _hud.SpPaintDone = t >= PaintDoneAt;
        if (!wasDone && _hud.SpPaintDone) _hud.LapsRewardClang(true);
        _liveryPaint = (_spName.Length == 0 ? "PILOT" : _spName, 4, reveal);
        if (_spMech == null) return;
        // the mechanic bends over the car's right side and sweeps the brush from the tail towards the nose as the paint goes on; once it is dry he steps back and lowers the brush
        float walk = Mathf.SmoothStep(0, 1, Math.Clamp((t - 0.2f) / 1.3f, 0f, 1f));
        float along = Mathf.Lerp(1.05f, -0.95f, reveal);
        float back = _hud.SpPaintDone ? Math.Clamp((t - PaintDoneAt) / 2.4f, 0f, 1f) : 0f;
        Vector3 local = new(1.42f + 1.6f * back, -0.52f, Mathf.Lerp(2.9f, along, walk) + 4.4f * back);      // (when the paint is dry he steps off towards the tail, out of the picture)
        Vector3 pos = _car.Pos + _car.Rot * local;
        float ground = _track.MeshHeight(pos.X, pos.Z);
        pos.Y = ground;
        var facing = _car.Rot * Basis.FromEuler(new Vector3(0, Mathf.Pi / 2f, 0));      // (he looks at the car's right side)
        _spMech.Root.GlobalTransform = new Transform3D(facing.Orthonormalized().Scaled(Vector3.One * 1.0f), pos);
        float paintingNow = reveal > 0 && reveal < 1 ? 1f : 0f;
        float stroke = MathF.Sin(t * 9f) * 0.25f * paintingNow;
        _spMech.Body.Rotation = Vector3.Zero;      // (he stands upright: a lean put the head through the car)
        _spMech.ArmR.Rotation = new Vector3(Mathf.DegToRad(72f) * paintingNow + stroke, 0, 0);      // (a positive turn swings the hanging arm forward, to the car)
        _spMech.Head.Rotation = new Vector3(Mathf.DegToRad(-10f) * paintingNow, MathF.Sin(t * 1.5f) * 0.2f * (1 - paintingNow), 0);
        float step = MathF.Sin(t * 7f) * 0.5f * (1f - walk);
        _spMech.LegL.Rotation = new Vector3(step, 0, 0); _spMech.LegR.Rotation = new Vector3(-step, 0, 0);
    }

    void SputnikPaintCamera(float dt)
    {
        float t = _hud.SpPaintAge, k = Math.Clamp(t / PaintDoneAt, 0f, 1f), e = Mathf.SmoothStep(0, 1, k);
        // while he paints: from the front and the right, a little above, and nearer; when it is dry the mechanic walks off to the tail and the lens comes to the side of the car,
        // close, to read the name and the stars, then circles slowly
        Vector3 eyeA, lookA = new(0.9f, 0.1f, 0);
        float angA = Mathf.DegToRad(Mathf.Lerp(36f, 58f, e)), distA = Mathf.Lerp(7.2f, 6.2f, e), hA = Mathf.Lerp(2.2f, 1.7f, e);
        eyeA = new Vector3(MathF.Sin(angA) * distA, hA, -MathF.Cos(angA) * distA);
        float a = Math.Max(0f, t - PaintDoneAt), angB = Mathf.DegToRad(66f + a * 13f), distB = 4.6f + Math.Min(a, 6f) * 0.2f;
        var eyeB = new Vector3(MathF.Sin(angB) * distB, 1.0f + Math.Min(a, 6f) * 0.05f, -MathF.Cos(angB) * distB);
        var lookB = new Vector3(0.9f, 0.1f, -0.1f);
        float w = Mathf.SmoothStep(0, 1, Math.Clamp((t - (PaintDoneAt - 0.8f)) / 1.8f, 0f, 1f));
        ShowCarLens(eyeA.Lerp(eyeB, w), lookA.Lerp(lookB, w), Mathf.Lerp(42f, 38f, w), dt);
    }
}
