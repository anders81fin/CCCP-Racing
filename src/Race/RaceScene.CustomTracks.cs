using System;
using System.Collections.Generic;
using System.Linq;
using CCCPRacing.Track;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// SINGLE PLAYER > CUSTOM TRACKS: the player's own tracks (data in user://tracks/NAME/track.json, see docs/public/custom-tracks.md) as a vertical list. Choosing one starts free laps on it at once
/// (DIESEL, daylight: a custom track has no medals, norms or fuel, so no fuel or time card comes first). A track that does not validate stays in the list with ERROR and does not start; one that
/// validates but does not build goes back to this list with the reason (HandleCustomLoadFailure). Leaving the laps returns here, on the track just driven.
/// </summary>
public partial class RaceScene
{
    List<TrackSource.Entry> _customList = new();
    int _customSel;
    /// <summary>Names of custom tracks by id for TrackName (the list's, and a track built from the command line).</summary>
    static readonly Dictionary<string, string> CustomNames = new();
    /// <summary>A custom track that did not build: its id and the reason, shown on the list after the world was rebuilt on the first track.</summary>
    static (string id, string reason)? _customLoadError;

    static string CustomName(string id) => CustomNames.TryGetValue(id, out var n) ? n : id.ToUpperInvariant();

    /// <summary>The card: scans the folder every time (an agent may have written a track meanwhile), on the given track if it is there.</summary>
    void OpenCustomTracks(string selectId = null)
    {
        try { DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(TrackSource.CustomRoot)); } catch { }      // (the folder exists for the player to put tracks in)
        _customList = TrackSource.Scan();
        foreach (var e in _customList) if (e.Error == null) CustomNames[e.Id] = e.Name;
        _customSel = selectId != null && _customList.FindIndex(e => e.Id == selectId) is int at and >= 0 ? at : Math.Clamp(_customSel, 0, Math.Max(0, _customList.Count - 1));
        OpenCard(TitleCard.Custom);
    }

    /// <summary>The best DIESEL lap of a custom track from its Stakhanov board, as m:ss.cc, or "".</summary>
    string CustomBest(string id)
    {
        try
        {
            var times = BoardFor(id, 1).Where(e => Training.GradeOf(e.Power) == FuelGrade.Diesel).Select(e => e.Time).ToList();
            if (times.Count == 0) return "";
            float t = times.Min();
            return LapTimer.Format(t);
        }
        catch { return ""; }
    }

    /// <summary>What the card says about the selected track (at most 42 characters).</summary>
    string CustomSubLine()
    {
        if (_customList.Count == 0) return null;
        var e = _customList[Math.Clamp(_customSel, 0, _customList.Count - 1)];
        if (e.Error != null) return CustomText("ERROR: " + e.Error, 42);
        string best = CustomBest(e.Id);
        return best == "" ? "NO LAP YET - FREE LAPS, NO MEDALS" : "BEST LAP " + best + " - FREE LAPS, NO MEDALS";
    }

    /// <summary>Text for the pixel font: only characters it draws, cut to a length with two dots.</summary>
    internal static string CustomText(string text, int max)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in (text ?? "").Replace('\n', ' ')) sb.Append(PixelFont.CanDraw(c) ? c : ' ');
        string t = sb.ToString().Trim().ToUpperInvariant();
        return t.Length <= max ? t : t[..(max - 2)] + "..";
    }

    /// <summary>The card's data for the Hud (RaceScene.TitleCards.cs CardHud).</summary>
    void CustomCardHud()
    {
        _hud.CustomRows = _customList.Select(e => CustomText(e.Name, TrackValidator.MaxNameLength)).ToArray();
        _hud.CustomBad = _customList.Select(e => e.Error != null).ToArray();
        _hud.CustomTimes = _customList.Select(e => e.Error != null ? "" : CustomBest(e.Id)).ToArray();
        _hud.CustomSelection = Math.Clamp(_customSel, 0, Math.Max(0, _customList.Count - 1));
        _hud.CustomEmpty = new[] { "NO CUSTOM TRACKS YET", "PUT A TRACK FOLDER IN THE GAME'S", "TRACKS FOLDER. O OPENS IT.", "SEE CUSTOM-TRACKS.MD" };
        _hud.CardSub = CustomSubLine();
    }

    /// <summary>The card's keys: Up/Down choose (the d-pad too), Enter or A starts, O opens the folder, Esc or B back to the menu.</summary>
    bool CustomCardInput()
    {
        int n = _customList.Count;
        if (Input.IsActionJustPressed("accelerate") && n > 0) _customSel = Math.Max(0, _customSel - 1);
        else if (Input.IsActionJustPressed("brake") && n > 0) _customSel = Math.Min(n - 1, _customSel + 1);
        else if (Input.IsActionJustPressed("open_tracks_folder")) OpenCustomFolder();
        else if (Input.IsActionJustPressed("pause")) CardBack();
        else if (Input.IsActionJustPressed("start") && !Input.IsKeyPressed(Key.Alt)) StartCustomChoice();
        _cardSel = _customSel;
        return true;
    }

    /// <summary>The folder of the player's tracks in the system's file manager (the path is in the guide, and in the log).</summary>
    void OpenCustomFolder()
    {
        string abs = ProjectSettings.GlobalizePath(TrackSource.CustomRoot);
        try { DirAccess.MakeDirRecursiveAbsolute(abs); } catch { }
        GD.Print("Custom tracks folder: " + abs);
        if (DisplayServer.GetName() != "headless") OS.ShellShowInFileManager(abs);
    }

    /// <summary>Enter on a row: a track that validates starts free laps on it; one that does not says why and stays.</summary>
    void StartCustomChoice()
    {
        if (_customList.Count == 0) return;
        var e = _customList[Math.Clamp(_customSel, 0, _customList.Count - 1)];
        if (e.Error != null) { _hud.MenuNote("CANNOT START THIS TRACK", CustomText(e.Error, 42), 4f); return; }
        if (TrackLoadingScreen.Active != null || _replayViewer is { State: not ReplayViewerState.Closed }) return;      // (StartRun's own guard: a refused start leaves no pick behind)
        _card = TitleCard.None;
        _overlayExitFrame = Engine.GetProcessFrames();
        _trackFor = MenuItem.Training;
        _chosenRunTrack = e.Id;
        _lapsFuelPick = FuelGrade.Diesel;
        _lapsPhase = DayNight.Phase.Day;
        _lapsFuelStart = _lapsFuelLast = _lapsFuelPick;
        _lapsFuelFirst = _lapsFuelPicked = false;
        StartRun(MenuItem.Training);
    }

    /// <summary>After laps on a custom track: the CUSTOM TRACKS list, on the track just driven.</summary>
    void ReturnToCustomList(string id)
    {
        EnterTitle();
        OpenMenu(MenuItem.Single, MenuItem.Custom);
        AfterTitleCeremonies(() => OpenCustomTracks(id));
    }

    /// <summary>
    /// A custom track passed the checks but did not build (_Ready's catch): back to the first track and, when that world is up, the list with the reason. The failure stays on record until that
    /// list is shown, so a track named on the command line (--track=custom-x) is not chosen again by TrackToBuild: the game does not rebuild a failing track in a loop.
    /// </summary>
    void HandleCustomLoadFailure(Exception e)
    {
        string id = _trackId;
        GD.PushWarning($"Custom track '{id}' could not be loaded: {e.GetType().Name}: {e.Message}");      // (a warning: the player's file is the problem, not the game; the stack follows for the author of the game)
        GD.Print(e.StackTrace);
        _customLoadError = (id, e is System.IO.InvalidDataException ? e.Message : $"{e.GetType().Name}: {e.Message}");
        SetProcess(false); SetPhysicsProcess(false);      // (this half-built scene must not run a frame more before the reload: its car and timers do not exist)
        TrackLoadingScreen.Active?.Cancel();
        _loadingRunMode = null; _resume = null; _previewResume = -1;
        _trackId = Tracks[0].id;
        GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
    }

    /// <summary>On the world that came back after a failed custom track: the list opens with a red note (true: shown).</summary>
    bool ShowCustomLoadError()
    {
        if (_customLoadError is not var (id, reason)) return false;
        _customLoadError = null;
        _showLog = false;
        _hud.Mode = HudMode.Title;
        OpenMenu(MenuItem.Single, MenuItem.Custom);
        OpenCustomTracks(id);
        string why = reason.Contains(": ") ? reason[(reason.IndexOf(": ", StringComparison.Ordinal) + 2)..] : reason;
        _hud.MenuNote("COULD NOT LOAD THIS TRACK", CustomText(why, 42), 6f);
        return true;
    }
}
