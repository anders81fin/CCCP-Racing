using System;
using System.Collections.Generic;
using System.Linq;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// Development only (not compiled into the release): --track-chain=atomgrad,siberia,... (with --unlock-all) drives the track changes of a play session through the game's own menu functions, in one process, so that the audio
/// and the loading of repeated changes can be watched (the Deck's freeze on a track change, the 0.916 audio findings): the run on the track the game was started on (--start --track=...) goes on for a few seconds,
/// ReturnRallyToTrackSelect (the way back after a run), the fuel card, the track card on the next track, the loading card's key, and again; then the window is asked to close, as the window manager does
/// (--track-chain-quit=wm) or the game quits through AudioMix.Quit. Each arrival prints a CHAIN line with the track, the objects the engine holds and the generator players in the tree.
/// </summary>
public partial class RaceScene
{
    static string[] _chain;
    static int _chainNext, _chainArrivals;
    static bool _chainWmQuit;
    float _chainWait, _chainRaceSeconds;
    bool _chainArrived, _chainDone, _chainCountdownSeen;
    static Node _chainPresser;      // (in the tree's root: it outlives the scene reloads and keeps processing under the loading card)

    sealed partial class ChainKeyPresser : Node
    {
        float _wait; bool _seen;
        public override void _Process(double delta)
        {
            if (TrackLoadingScreen.Active is { IsReady: true } screen)
            {
                if (!_seen) { _seen = true; GD.Print($"CHAIN card ready at {Time.GetTicksMsec() / 1000.0:F1} s"); }
                _wait += (float)delta;
                if (_wait > 1.0f) { GD.Print($"CHAIN key pressed at {Time.GetTicksMsec() / 1000.0:F1} s"); _wait = 0; _seen = false; screen.Press(new InputEventKey { Pressed = true, Keycode = Key.Enter, PhysicalKeycode = Key.Enter }); }
            }
            else { _wait = 0; _seen = false; }
        }
    }

    partial void TrackChainTick(float dt)
    {
        if (_chain == null)
        {
            var arg = Array.Find(CmdArgs(), a => a.StartsWith("--track-chain="));
            _chain = arg == null ? Array.Empty<string>() : arg["--track-chain=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries);
            _chainWmQuit = Array.Exists(CmdArgs(), a => a == "--track-chain-quit=wm");
            var secs = Array.Find(CmdArgs(), a => a.StartsWith("--track-chain-seconds="));
            _chainRaceSeconds = secs == null ? 6f : float.Parse(secs["--track-chain-seconds=".Length..], System.Globalization.CultureInfo.InvariantCulture);
        }
        if (_chain.Length == 0 || ViewerMode) return;
        if (_chainRaceSeconds == 0) _chainRaceSeconds = 6f;
        // the loading card waits for a key: ChainKeyPresser presses it (this scene does not process while the card is up)
        if (!GodotObject.IsInstanceValid(_chainPresser)) { _chainPresser = new ChainKeyPresser { Name = "ChainKeyPresser", ProcessMode = ProcessModeEnum.Always }; GetTree().Root.AddChild(_chainPresser); }
        if (TrackLoadingScreen.Active != null) return;
        if (_state == State.Countdown && !_chainCountdownSeen) { _chainCountdownSeen = true; GD.Print($"CHAIN countdown begins at {Time.GetTicksMsec() / 1000.0:F1} s"); }
        if (_state == State.Racing && _runClock > 1f && !_chainArrived)
        {
            _chainArrived = true; _chainArrivals++;
            int generators = GetTree().Root.FindChildren("*", "", true, false).Count(n => n is AudioStreamPlayer { Stream: AudioStreamGenerator } or AudioStreamPlayer3D { Stream: AudioStreamGenerator });
            GD.Print($"CHAIN arrival {_chainArrivals} at {Time.GetTicksMsec() / 1000.0:F1} s: track {_trackId}, objects {Performance.GetMonitor(Performance.Monitor.ObjectCount):F0}, generator players {generators}, managed {GC.GetTotalMemory(false) / 1048576} MB");
        }
        if (_state == State.Racing && _chainArrived && !_chainDone && _runClock > _chainRaceSeconds)
        {
            if (_chainNext < _chain.Length)
            {
                GD.Print($"CHAIN leaving {_trackId} for {_chain[_chainNext]}");
                ReturnRallyToTrackSelect();
            }
            else
            {
                _chainDone = true;
                GD.Print($"CHAIN done after {_chainArrivals} arrivals; closing ({(_chainWmQuit ? "window manager's request" : "AudioMix.Quit")})");
                if (_chainWmQuit) GetTree().Root.PropagateNotification((int)Node.NotificationWMCloseRequest);
                else CCCPRacing.Audio.AudioMix.Quit(this);
            }
            return;
        }
        if (_state != State.Title || _chainNext >= _chain.Length) return;
        // the fuel card (the fuel first), then the track card: both answered
        _chainWait += dt;
        if (_chainWait < 0.6f) return;
        _chainWait = 0;
        if (_card == TitleCard.RallyFuel) { ChooseTitleCard(); return; }
        if (_card == TitleCard.Track)
        {
            int index = Array.FindIndex(Tracks, t => t.id == _chain[_chainNext]);
            if (index < 0) { GD.Print($"CHAIN: unknown track {_chain[_chainNext]}"); _chainNext++; return; }
            _cardSel = index; _chainNext++; _chainArrived = false;
            ChooseTitleCard();
        }
    }
}
