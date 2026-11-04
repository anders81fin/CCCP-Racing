using System;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The driver's name is the personnel file's: asked once, when a new file is opened in FILES
/// (YOUR NAME, TOVARISH?, RaceScene.Files.cs: Enter opens the file, Esc back to the files), never
/// before a run. A top-10 lap goes on the board of honour under it at once, the race going on.
/// Kept in the file's settings.cfg ([driver] name) and as the boards' last name.
/// </summary>
public partial class RaceScene
{
    bool _askName;
    ulong _askFrame;

    /// <summary>The name a top-10 lap goes on the board under.</summary>
    string DriverName => _board.LastName.Length > 0 ? _board.LastName : Leaderboard.DefaultName;

    /// <summary>After the boards are read, and on a change of file: the file's driver is the name on every board
    /// (a file without one: no name, it is asked).</summary>
    void LoadDriverName()
    {
        var cfg = new ConfigFile();
        string name = cfg.Load(FileDir + "settings.cfg") == Error.Ok ? Leaderboard.CleanName(cfg.GetValue("driver", "name", "").AsString()) : "";
        foreach (var b in _boards) b.LastName = name;
    }

    /// <summary>The name field's typing: allowed characters, Backspace; Enter opens the new file, Esc back to the files.</summary>
    void NameAskKey(InputEventKey k)
    {
        if (Engine.GetProcessFrames() <= _askFrame) return;      // the Enter that chose the mode
        switch (k.Keycode)
        {
            case Key.Enter or Key.KpEnter:
                if (k.Echo || k.AltPressed) return;
                string name = Leaderboard.CleanName(_entryName);
                if (name.Length == 0) name = Leaderboard.DefaultName;
                UseFile(_askFile);      // the new personnel file: in use from now, under this name
                foreach (var b in _boards) b.LastName = name;
                string path = FileDir + "settings.cfg";
                var cfg = new ConfigFile();
                cfg.Load(path);       // keep the other sections
                cfg.SetValue("driver", "name", name);
                cfg.Save(path);
                _askName = false;
                _overlayExitFrame = Engine.GetProcessFrames();
                FileCreated();
                _askFile = 0;
                return;
            case Key.Escape:
                if (k.Echo) return;
                _askName = false;
                _askFile = 0;
                _overlayExitFrame = Engine.GetProcessFrames();
                return;
            case Key.Backspace:
                if (_entryName.Length > 0) _entryName = _entryName[..^1];
                return;
        }
        if (k.CtrlPressed || k.AltPressed || k.MetaPressed || k.Unicode <= 0 || k.Unicode > char.MaxValue) return;
        char c = char.ToUpperInvariant((char)k.Unicode);
        if (_entryName.Length >= Leaderboard.MaxName || !Leaderboard.IsNameChar(c)) return;
        if (c == ' ' && (_entryName.Length == 0 || _entryName[^1] == ' ')) return;
        _entryName += c;
    }

    /// <summary>A top-10 lap: on the board under the driver's name at once (the race goes on).</summary>
    void BoardLap(float time, bool record)
    {
        string name = DriverName;
        int power = StakhanovRules ? _lapPeak : 0;
        int rank = _board.Insert(name, time, null, power, _lapPad, _lapNight, _lapBoost, _lastBoardRecordingId);
        _board.Save();
        _records.WriteSignature();
        _boardHighlights[RecordSet] = rank - 1;
        string lap = $"{name}  {LapTimer.Format(time)}" + (power > 0 ? $"  {power}%" : "");
        if (record)
            _hud.Show(Slogans.Pick(Slogans.Record), lap + "  -  PLACE #1 ON THE BOARD", 3.5f, Hud.Gold, plain: "RECORD LAP", plainSub: lap + "  -  PLACE #1 ON THE BOARD");
        else
            _hud.Show($"PLACE #{rank} ON THE BOARD OF HONOUR", lap, 3f, Hud.Gold);
    }
}
