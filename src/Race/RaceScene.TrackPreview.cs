using System;
using CCCPRacing.Audio;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The tracks' scenic tours (THE KOLKHOZ, WELCOME TO SIBERIA): opened from CHOOSE TRACK with T
/// (a pad: Y) on a track that has one, full screen, the world rebuilt on that track first if need be.
/// Esc goes back to the card, Enter races there; at its end it comes back to the card. --tour-movie
/// records one for the trailer.
/// </summary>
public partial class RaceScene
{
    static int _previewResume = -1;
    static MenuItem _previewResumeFor = MenuItem.Race;
    static bool _tourResume;           // the rebuilt scene opens the tour at once
    bool _tourOpen;                    // the player opened the highlighted track's tour
    bool _preview, _previewFocus, _previewFull, _previewReload;
    bool _previewTitle, _previewReloadQueued;
    ulong _previewReloadFrame;
    float _previewTellFrom = -1;
    bool _previewMovie, _previewMovieDone;
    public bool TrackPreviewActive => _preview && _state == State.Title && _card == TitleCard.Track;

    /// <summary>The track has a scenic tour (TrackPreview steps).</summary>
    static bool HasTour(string id) => id is "ilyich" or "siberia" || DroneFilm.For(id) is { Ready: true };

    /// <summary>T / Y on the track card: the highlighted track's tour, full screen.</summary>
    void OpenTour()
    {
        if (!HasTour(Tracks[_cardSel].id)) return;
        _tourOpen = true;
        _previewFocus = _previewFull = true;
    }

    /// <summary>The tour's keys: Esc back to the card, Enter races on this track.</summary>
    bool TourInput()
    {
        if (!_tourOpen) return false;
        if (_previewReload) return true;
        if (_droneWait) { if (Input.IsActionJustPressed("pause")) CloseTour(); return true; }      // any other key starts the flight (RawInput)
        if (Engine.GetProcessFrames() <= _droneReleaseFrame) return true;      // (the key that started the flight is not Enter: race)
        if (Input.IsActionJustPressed("pause")) CloseTour();
        else if (Input.IsActionJustPressed("start") && !Input.IsKeyPressed(Key.Alt))
        {
            CloseTour();
            StopTrackPreview();
            _card = TitleCard.None;
            _overlayExitFrame = Engine.GetProcessFrames();
            TrackChosen(_cardSel);
        }
        return true;
    }

    void CloseTour()
    {
        _tourOpen = _previewFocus = _previewFull = false;
        _overlayExitFrame = Engine.GetProcessFrames();
    }
    public bool TrackPreviewFullscreen => TrackPreviewActive && _previewFull;
    public Node PreviewHudLayer => _hud.GetParent();
    public void SetPreviewTexture(Texture2D texture) => _hud.PreviewTexture = texture;

    // --tour-movie with --track=ilyich|siberia and Godot's --write-movie:
    // record one complete menu tour, including its opening music, without menu controls.
    void StartTrackPreviewMovie()
    {
        if (!Array.Exists(CmdArgs(), a => a == "--tour-movie")) return;
        if (!HasTour(_trackId))
        {
            GD.PushError("--tour-movie requires a track with a tour (ilyich, siberia, atomgrad, bonus)");
            CCCPRacing.Audio.AudioMix.Quit(this, 1);
            return;
        }
        _previewMovie = true;
        EnterTitle();
        OpenMenu(MenuItem.Single, MenuItem.Race);
        OpenTrackCard(MenuItem.Race);
        _cardSel = Array.FindIndex(Tracks, t => t.id == _trackId);
        _previewFocus = _previewFull = true;
        _hud.PreviewMovie = true;
        TrackPreviewFrame(0);
    }

    void TrackPreviewFrame(float dt)
    {
        if (_previewMovieDone) return;
        // A tour the player opened from the card (T / Y), or the --tour-movie capture.
        bool wanted = (_tourOpen || _previewMovie) && _state == State.Title && _card == TitleCard.Track;
        if (!wanted)
        {
            StopTrackPreview();
            _hud.TrackPreview = false;
            return;
        }
        if (_previewReload)
        {
            // Render the loading card before rebuilding the world synchronously.
            if (!_previewReloadQueued && Engine.GetProcessFrames() >= _previewReloadFrame + 3)
            {
                _previewReloadQueued = true;
                GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
            }
            return;
        }
        if (Tracks[_cardSel].id != _trackId)
        {
            _previewReload = true;
            _previewResume = _cardSel;
            _previewResumeFor = _trackFor;
            _tourResume = _tourOpen;
            _trackId = Tracks[_cardSel].id;
            _preview = true;
            _hud.TrackPreview = true;
            _hud.PreviewLoading = TrackName(_trackId);
            _hud.PreviewFullscreen = _previewFull;      // LOADING full screen, as the tour will be
            _previewReloadFrame = Engine.GetProcessFrames();
            return;
        }
        if (!_preview) BeginTrackPreview();
        DroneCardTick(dt);
        _sc.Total += dt;
        EnvironmentMusic(dt, false);
        _hud.TrackPreview = true;
        _hud.PreviewFullscreen = _previewFull;
        _hud.PreviewFocused = _previewFocus;
        _hud.PreviewCardAlpha = DroneCardAlpha;
        _hud.PreviewCardWaiting = _droneWait;
        _hud.PreviewTitle = _previewTitle ? (_trackId == "ilyich" ? "THE KOLKHOZ" : _trackId == "siberia" ? "WELCOME TO SIBERIA" : TrackName(_trackId)) : null;
        // The officer's card already presents these lines; keep only distinct scene captions.
        _hud.PreviewCaption = _previewTitle || (_sc.Steps.Length > 0
            && Array.IndexOf(_sc.Current.step.Tell, _sc.Caption) >= 0) ? null : _sc.Caption;
        // Restore the original two-line introduction cards over the moving video.
        if (!_previewTitle && _sc.Steps.Length > 0 && !string.IsNullOrEmpty(_sc.Caption) && _previewTellFrom < 0)
            _previewTellFrom = _sc.Total;
        _hud.PreviewTell = _previewTellFrom >= 0 && _sc.Total < _previewTellFrom + 6f
            ? _sc.Current.step.Tell : null;
        _hud.PreviewArrow = _sc.Arrow?.Invoke() is Vector3 at && !_cam.IsPositionBehind(at)
            ? _cam.UnprojectPosition(at) : null;
    }

    void StopTrackPreview()
    {
        if (!_preview) return;
        _preview = _previewFocus = _previewFull = false;
        _droneWait = false;
        StopDroneTour();
        EndEnvironmentTour();
        _scLightClock = null;
        ApplyWorld(Actors.All);
        _car.Neutral = false;
        _radio.Play();
        AudioMix.MuteSfx(true);
        _hud.TrackPreview = false;
    }

    void BeginTrackPreview()
    {
        _preview = true;
        if (SchoolDbg) GD.Print($"preview: {_trackId} starts");
        _previewTellFrom = -1;
        bool film = DroneFilm.For(_trackId) is { Ready: true };      // a track with a baked drone film flies it (RaceScene.DroneTour.cs)
        _sc.BeginPreview(film ? SchoolScript.DroneFor(_trackId)
            : _trackId == "ilyich" ? SchoolScript.IlyichEnvironment
            : _trackId == "siberia" ? SchoolScript.SiberiaEnvironment : Array.Empty<(int, SchoolStep)>());
        _previewTitle = !film;      // the film opens with its own black card (DroneCardAlpha)
        _radio.Stop();
        AudioMix.MuteSfx(false);
        if (film)
        {
            StartDroneCardWait();      // the black card waits for a key (or a minute); then the world is built and the flight starts
            if (_previewMovie) ReleaseDroneCard();      // (a --tour-movie has no keys)
        }
        else if (_sc.Steps.Length > 0) BeginEnvironmentTour();
    }

    void TrackPreviewDrive(float dt)
    {
        if (!TrackPreviewActive || _previewReload || _previewMovieDone) return;
        if (_droneWait) return;      // the card is up: nothing runs yet
        if (!_tourOpen && !_previewMovie) return;      // the tour has been closed (it ends a frame later)
        _sc.Sim += dt;
        if (_sc.Steps.Length == 0)
        {
            _previewTitle = _sc.Sim < SchoolRunner.LessonTime;
            return;
        }
        if (_previewTitle)
        {
            if (_sc.Sim < SchoolRunner.LessonTime) return;
            _previewTitle = false;
            _sc.PreviewFirst();
            PreviewLog();
            return;
        }
        if (_sc.PreviewBeatOver(dt)) PreviewNextBeat();
    }

    void PreviewNextBeat()
    {
        switch (_sc.PreviewNext())
        {
            case SchoolRunner.PreviewMove.End when _previewMovie:
                _previewMovieDone = true;
                GD.Print($"Tour movie complete: {_trackId}, {_sc.Total:F3} seconds");
                CCCPRacing.Audio.AudioMix.Quit(this);
                return;
            case SchoolRunner.PreviewMove.End:
                CloseTour();      // over: back to the track card
                return;
            case SchoolRunner.PreviewMove.Step:
                _previewTellFrom = -1;
                break;
        }
        PreviewLog();
    }

    void PreviewLog()
    {
        if (SchoolDbg) GD.Print($"preview: {_sc.Current.step.Id} beat {_sc.BeatIndex + 1}: {_sc.Caption}");
    }
}
