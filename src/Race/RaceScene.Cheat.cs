using System;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The Konami code on the title (up up down down left right left right B A, with keys or the pad) opens every fuel and every track, for good; the same code closes them again.
/// Only the access changes: no medal, record, ghost or save is written or forged, and the real medals still open things as they always did. --unlock-all does the same for one
/// run and writes nothing (the tests and a quick look at everything).
/// </summary>
public partial class RaceScene
{
    bool? _cheatSaved;
    int _cheatPos;
    ulong _cheatLastMs;
    string _cheatLastToken;
    ulong _cheatTokenMs;
    int _cheatLastDevice;

    static readonly string[] CheatCode = { "UP", "UP", "DOWN", "DOWN", "LEFT", "RIGHT", "LEFT", "RIGHT", "B", "A" };

    /// <summary>Everything is open: the code was entered (saved in settings.cfg, [cheats] unlock_all) or --unlock-all was given.</summary>
    public bool CheatUnlock
    {
        get
        {
            if (Array.Exists(CmdArgs(), a => a == "--unlock-all")) return true;
            if (_cheatSaved == null)
            {
                var cfg = new ConfigFile();
                _cheatSaved = _records != null && cfg.Load(_records.Dir + "settings.cfg") == Error.Ok && (bool)cfg.GetValue("cheats", "unlock_all", false);
            }
            return _cheatSaved == true;
        }
    }

    void SetCheatUnlock(bool on)
    {
        var cfg = new ConfigFile();
        cfg.Load(_records.Dir + "settings.cfg");      // keep the other sections
        cfg.SetValue("cheats", "unlock_all", on);
        cfg.Save(_records.Dir + "settings.cfg");
        _cheatSaved = on;
        _hud.MenuNote(on ? "THE POLITBURO HAS REVIEWED YOUR FILE" : "YOUR FILE IS BACK IN THE ARCHIVE",
            on ? "EVERY FUEL AND EVERY ROAD IS OPEN, COMRADE" : "THE FUELS AND THE ROADS OPEN WITH MEDALS AGAIN", 4f);
    }

    /// <summary>--cheat-test: enters the code with keys and with the pad, prints what opens and closes, and puts the user's settings back as they were (the tests read the lines).</summary>
    void CheatSelfTestFromArgs()
    {
        if (!Array.Exists(CmdArgs(), a => a == "--cheat-test")) return;
        string path = ProjectSettings.GlobalizePath(_records.Dir + "settings.cfg");      // (a real path: System.IO does not know user://)
        byte[] backup = System.IO.File.Exists(path) ? System.IO.File.ReadAllBytes(path) : null;
        bool Closed() => FuelLocked(FuelGrade.Sputnik) && Locked(Tracks.Length - 1, MenuItem.Race);
        InputEvent K(Key k) => new InputEventKey { Pressed = true, Keycode = k };
        InputEvent P(JoyButton b) => new InputEventJoypadButton { Pressed = true, ButtonIndex = b };
        string[] state = new string[6];
        state[0] = $"start closed={Closed()} unlock={CheatUnlock}";
        foreach (var k in new[] { Key.Up, Key.Up, Key.Down, Key.Down, Key.Left, Key.Right, Key.Left, Key.Right, Key.B, Key.A }) CheatCodeInput(K(k));
        state[1] = $"after keys unlock={CheatUnlock} sputnikLocked={FuelLocked(FuelGrade.Sputnik)} jetLocked={FuelLocked(FuelGrade.Rocket)} lastTrackLocked={Locked(Tracks.Length - 1, MenuItem.Race)} saved={(System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path).Contains("unlock_all=true") : false)}";
        bool swallowed = false;
        foreach (var b in new[] { JoyButton.DpadUp, JoyButton.DpadUp, JoyButton.DpadDown, JoyButton.DpadDown, JoyButton.DpadLeft, JoyButton.DpadRight, JoyButton.DpadLeft, JoyButton.DpadRight, JoyButton.B, JoyButton.A }) swallowed = CheatCodeInput(P(b));
        state[2] = $"after pad unlock={CheatUnlock} closed={Closed()} lastPadAswallowed={swallowed}";
        // A doubled pad (two devices report every press) must still work.
        InputEvent PD(JoyButton b, int d) => new InputEventJoypadButton { Pressed = true, ButtonIndex = b, Device = d };
        bool before = CheatUnlock;
        foreach (var b in new[] { JoyButton.DpadUp, JoyButton.DpadUp, JoyButton.DpadDown, JoyButton.DpadDown, JoyButton.DpadLeft, JoyButton.DpadRight, JoyButton.DpadLeft, JoyButton.DpadRight, JoyButton.B, JoyButton.A }) { CheatCodeInput(PD(b, 0)); CheatCodeInput(PD(b, 1)); }
        state[4] = $"doubled pad toggled={CheatUnlock != before}";
        before = CheatUnlock;
        CheatCodeInput(K(Key.Up)); CheatCodeInput(K(Key.Up)); CheatCodeInput(K(Key.Left)); CheatCodeInput(K(Key.B)); CheatCodeInput(K(Key.A));      // (a wrong sequence does nothing)
        state[3] = $"wrong sequence unchanged={CheatUnlock == before}";
        foreach (var s in state) if (s != null) GD.Print("CHEATTEST: " + s);
        if (backup != null) System.IO.File.WriteAllBytes(path, backup); else if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        _cheatSaved = null;
        GD.Print($"CHEATTEST: settings restored, unlock={CheatUnlock}");
        GetTree().Quit();
    }

    /// <summary>The code's keys, watched on the title only (not in a card, a name field, the recordings or the viewer). True: the event is swallowed (the pad's last A must not also open a menu row).</summary>
    bool CheatCodeInput(InputEvent e)
    {
        if (_state != State.Title || _card != TitleCard.None || _carTourOpen || _askName || _hud.NameAsk || _hud.ReplayListOpen
            || _replayViewer is { State: not ReplayViewerState.Closed } || TrackLoadingScreen.Active != null) { _cheatPos = 0; return false; }
        string token = e switch
        {
            InputEventKey { Pressed: true, Echo: false } k => k.Keycode switch
            { Key.Up => "UP", Key.Down => "DOWN", Key.Left => "LEFT", Key.Right => "RIGHT", Key.B => "B", Key.A => "A", _ => "?" },
            InputEventJoypadButton { Pressed: true } b => b.ButtonIndex switch
            { JoyButton.DpadUp => "UP", JoyButton.DpadDown => "DOWN", JoyButton.DpadLeft => "LEFT", JoyButton.DpadRight => "RIGHT", JoyButton.B => "B", JoyButton.A => "A", _ => "?" },
            _ => null,
        };
        if (token == null) return false;
        ulong now = Time.GetTicksMsec();
        if (OS.HasEnvironment("CHEATDBG")) GD.Print($"CHEAT token {token} pos {_cheatPos} dev {(e as InputEventJoypadButton)?.Device}");
        // One press can arrive twice (a Steam Deck has its own pad and Steam Input's virtual one, both reporting): the same token from another device within 40 ms is the same press.
        int device = e is InputEventJoypadButton jd ? jd.Device : -1;
        if (token == _cheatLastToken && device != _cheatLastDevice && now - _cheatTokenMs < 40) return false;
        _cheatLastToken = token; _cheatTokenMs = now; _cheatLastDevice = device;
        if (now - _cheatLastMs > 3000) _cheatPos = 0;      // (the code is entered in one go)
        _cheatLastMs = now;
        if (token == CheatCode[_cheatPos]) _cheatPos++;
        else _cheatPos = token == CheatCode[0] ? 1 : 0;
        if (_cheatPos < CheatCode.Length) return false;
        _cheatPos = 0;
        SetCheatUnlock(!CheatUnlock);
        return e is InputEventJoypadButton;
    }
}
