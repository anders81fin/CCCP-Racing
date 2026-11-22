using System;
using System.Linq;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The Sputnik gallery (the owner's order of 2026-11-22): KGB FILES > PERSONNEL, G (a pad: X) on a driver's row opens what that driver has earned in the SPUTNIK test pilots' programme (SputnikProgram.cs), as four pages:
/// the licence, the order (the highest class earned), the champion's pennant and the chief test pilot's car (the world's own car at the start line with the livery, circled by the camera, the same "car on show" as the ceremonies).
/// Left/Right (the pad's d-pad) turn the page, Esc (B) goes back to the files. It reads that file's own campaign.cfg, so any driver's gallery can be opened, in use or not; nothing is written.
/// </summary>
public partial class RaceScene
{
    bool _galleryOpen;
    int _galleryFile, _galleryPage;
    SputnikStanding _galleryStanding;
    string _galleryName = "", _galleryPainted = "";

    /// <summary>The chief test pilot's car page is up: the 3D car is the screen.</summary>
    bool GalleryCarActive => _galleryOpen && _galleryPage == 3 && _galleryStanding != null && _galleryStanding.Has(SputnikAward.Chief) && _state == State.Title && _showFiles;

    void GalleryOpen(int file)
    {
        var dir = PlayerFiles.Dir(_records.Dir, file);
        var campaign = file == _file ? _campaign : new Campaign(dir);
        _galleryStanding = campaign.SputnikStanding();
        var summary = PlayerFiles.Read(_records.Dir, file);
        _galleryName = summary.Name.Length > 0 ? summary.Name : Leaderboard.DefaultName;
        _galleryPainted = campaign.SputnikPaintedName.Length > 0 ? campaign.SputnikPaintedName : _galleryName;
        _galleryFile = file; _galleryPage = 0; _galleryOpen = true;
        _hud.GalleryAge = 0;
        GalleryHud();
    }

    void GalleryClose()
    {
        _galleryOpen = false;
        _hud.GalleryOn = false;
        _overlayExitFrame = Engine.GetProcessFrames();
        FilesHud();
    }

    void GalleryHud()
    {
        var st = _galleryStanding;
        _hud.GalleryOn = true;
        _hud.GalleryPage = _galleryPage;
        _hud.GalleryEarned = new[] { st.Has(SputnikAward.TestPilot), st.Has(SputnikAward.Third), st.Has(SputnikAward.Champion), st.Has(SputnikAward.Chief) };
        _hud.GalleryOrderClass = st.Has(SputnikAward.First) ? SputnikAward.First : st.Has(SputnikAward.Second) ? SputnikAward.Second : SputnikAward.Third;
        _hud.SpName = _galleryName;
        _hud.SpMedals = st.Medal;
        _hud.SpTrackNames = SputnikStanding.AllTracks.Select(SputnikTrackName).ToArray();
        _hud.SpNew = Array.Empty<SputnikAward>();
        // what is still missing, in the locked pages' last line: the tracks without the silver / gold the page asks for
        int page = _galleryPage;
        var needed = page switch
        {
            0 => SputnikStanding.MainTracks.Where(t => !st.SilverOn(t)),
            1 => SputnikStanding.AllTracks.Where(t => !st.SilverOn(t)),
            2 => SputnikStanding.MainTracks.Where(t => !st.GoldOn(t)),
            _ => SputnikStanding.AllTracks.Where(t => !st.GoldOn(t)),
        };
        string verb = page is 0 or 1 ? "STILL WITHOUT A SILVER: " : "STILL WITHOUT A GOLD: ";
        _hud.GalleryTrackLine = verb + string.Join(", ", needed.Select(SputnikTrackName));
    }

    void GalleryFrame(float dt)
    {
        if (!_galleryOpen) return;
        if (!_showFiles) { _galleryOpen = false; _hud.GalleryOn = false; return; }
        _hud.GalleryAge += Math.Min(dt, 0.1f);
        _hud.GalleryPage = _galleryPage;
    }

    /// <summary>The gallery's keys while it is up (true: it has the key). Left/Right (A/D, the d-pad) turn the page, Esc/B close.</summary>
    bool GalleryInput(InputEvent e)
    {
        if (!_galleryOpen) return false;
        if (Engine.GetProcessFrames() <= _overlayFrame) return true;
        bool left = e is InputEventKey { Pressed: true, Echo: false } k1 && k1.Keycode is Key.Left or Key.A || e is InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.DpadLeft };
        bool right = e is InputEventKey { Pressed: true, Echo: false } k2 && k2.Keycode is Key.Right or Key.D || e is InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.DpadRight };
        bool back = e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } || e is InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.B };
        if (back) GalleryClose();
        else if (left || right)
        {
            _galleryPage = (_galleryPage + (right ? 1 : 3)) % 4;
            _hud.GalleryAge = 0;
            GalleryHud();
        }
        return true;
    }
}
