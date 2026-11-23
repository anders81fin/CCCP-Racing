using System;
using CCCPRacing.Audio;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The main menu's background music (the owner's order, 2026-11-19): SETTINGS > SOUNDS opened from the main menu shows RADIO as a passive ON (it is the car's radio, switched in a run) and a STATION row of six: the
/// radio's stations, the last of which, the default, is the menu's own theme song "HE KATSOIVAT LANTEEN" (the car's radio has it as its sixth station too). Stepping the row tunes the menu's music at once, and the choice is kept (settings.cfg [radio] title_station,
/// by the song's name) for the next launch. The car radio's own stations and station choice are untouched.
/// </summary>
public partial class RaceScene
{
    const string TitleThemePath = "res://assets/music/he_katsoivat_lanteen.mp3", TitleThemeName = "HE KATSOIVAT LANTEEN";

    /// <summary>The main menu's SOUNDS: the screen was opened from the title.</summary>
    bool TitleMixer => _state == State.Title;

    string _titleStationName;      // the song the menu plays (or is to play) by name; null: not read yet
    string _titleTuned = TitleThemeName;      // what the menu's player has been tuned to (it starts on the theme)

    // The menu's music level (RADIO VOLUME in the main menu's SOUNDS): 0 (mute) to 10, the default 5 the level the menu always had (+2 dB), 2.5 dB a step; kept in settings.cfg [radio] title_volume.
    const int TitleVolumeDefault = 5;
    int _titleVolume = -1;

    static float TitleVolumeDb(int v) => v <= 0 ? -80f : 7f + (v - TitleVolumeDefault) * 2.5f;      // (2 -> 7 dB, the same two notches)

    int TitleVolume
    {
        get
        {
            if (_titleVolume >= 0) return _titleVolume;
            int v = TitleVolumeDefault;
            if (_records != null && !ViewerMode)
            {
                var cfg = new ConfigFile();
                if (cfg.Load(_records.Dir + "settings.cfg") == Error.Ok && cfg.GetValue("radio", "title_volume", TitleVolumeDefault) is { VariantType: Variant.Type.Int } val && (int)val >= 0 && (int)val <= CarRadio.MaxVolume) v = (int)val;
            }
            return _titleVolume = v;
        }
    }

    void SetTitleVolume(int v)
    {
        _titleVolume = Math.Clamp(v, 0, CarRadio.MaxVolume);
        if (_records != null && !ViewerMode)
        {
            string path = _records.Dir + "settings.cfg";
            var cfg = new ConfigFile();
            cfg.Load(path);      // keep the other sections
            cfg.SetValue("radio", "title_volume", _titleVolume);
            cfg.Save(path);
        }
        if (_radio != null) _radio.VolumeDb = TitleVolumeDb(_titleVolume);      // (live: the player reads it every frame)
    }

    int TitleStationCount => Math.Max(1, _carRadio.StationCount);      // (the radio's stations: the theme is the last)

    string TitleStationNameAt(int i) => i >= 0 && i < _carRadio.StationCount ? Ascii(_carRadio.StationName(i)) : TitleThemeName;

    static string Ascii(string s) => s.ToUpperInvariant().Replace("Ä", "A").Replace("Ö", "O").Replace("Å", "A");

    int TitleStationIndex
    {
        get
        {
            _titleStationName ??= ReadTitleStation();
            for (int i = 0; i < TitleStationCount; i++) if (TitleStationNameAt(i) == _titleStationName) return i;
            return TitleStationCount - 1;      // an unknown or removed song: the theme
        }
    }

    string ReadTitleStation()
    {
        if (_records == null || ViewerMode) return TitleThemeName;
        var cfg = new ConfigFile();
        if (cfg.Load(_records.Dir + "settings.cfg") != Error.Ok) return TitleThemeName;
        var v = cfg.GetValue("radio", "title_station", TitleThemeName);
        return v.VariantType == Variant.Type.String ? (string)v : TitleThemeName;
    }

    void StepTitleStation(int step)
    {
        int n = TitleStationCount, i = ((TitleStationIndex + step) % n + n) % n;
        _titleStationName = TitleStationNameAt(i);
        if (_records != null && !ViewerMode)
        {
            string path = _records.Dir + "settings.cfg";
            var cfg = new ConfigFile();
            cfg.Load(path);      // keep the other sections
            cfg.SetValue("radio", "title_station", _titleStationName);
            cfg.Save(path);
        }
        TitleStationApply();
    }

    /// <summary>The menu's player on the chosen song: tuned when it differs from what plays (a station change, a return to the title after a run).</summary>
    void TitleStationApply()
    {
        if (_radio == null || _carRadio == null || ViewerMode) return;
        _radio.VolumeDb = TitleVolumeDb(TitleVolume);      // the menu's music level as set in SOUNDS
        int i = TitleStationIndex;
        string name = TitleStationNameAt(i);
        if (name == _titleTuned) return;
        var stream = i < _carRadio.StationCount ? _carRadio.StationStream(i) : ResourceLoader.Load<AudioStream>(TitleThemePath);      // (no stations: the theme itself)
        if (stream == null) return;
        _radio.Tune(stream, name);
        _titleTuned = name;
    }

    /// <summary>What the main menu's SOUNDS shows: the number, the count and the song of the menu's station.</summary>
    void SyncTitleMixerHud()
    {
        _hud.MixerTitleMode = TitleMixer;
        if (!TitleMixer) return;
        int i = TitleStationIndex;
        _hud.TitleStationNumber = i + 1; _hud.TitleStationCount = TitleStationCount; _hud.TitleStationName = TitleStationNameAt(i);
    }
}
