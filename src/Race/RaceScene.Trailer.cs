using System;
using System.Linq;
using CCCPRacing.Audio;
using Godot;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    bool _trailerCapture;

    // Capture existing lesson demonstrations at normal speed, without teaching cards.
    // --trailer-shots=kick,flick,hairpin,... --track=ilyich|siberia --write-movie FILE
    // --trailer-sfx also separates the world sound from the music for editing.
    void StartTrailerCapture()
    {
        var args = CmdArgs();
        string shots = Array.Find(args, a => a.StartsWith("--trailer-shots="));
        string trailerV2 = Array.Find(args, a => a.StartsWith("--trailer-v2="));
        if (trailerV2 != null)
        {
            string take = trailerV2.Split('=')[1];
            if ((take is "kolkhoz" or "farm" && _trackId != "ilyich") || (take == "siberia" && _trackId != "siberia")
                || take is not ("kolkhoz" or "farm" or "siberia"))
            {
                GD.PushError("--trailer-v2=kolkhoz|farm|siberia needs the corresponding --track=ilyich|siberia");
                GetTree().Quit(1);
                return;
            }
            shots = "--trailer-shots=" + (take == "siberia" ? "snow_hairpin" : "hairpin");
        }
        bool drone = Array.Exists(args, a => a == "--drone-tour");
        if (drone)
        {
            if (_trackId is not ("ilyich" or "siberia" or "atomgrad" or "bonus")) { GD.PushError("--drone-tour needs --track=ilyich, siberia, atomgrad or bonus"); GetTree().Quit(1); return; }
            shots = "--trailer-shots=" + (_trackId == "siberia" ? "snow_hairpin" : "hairpin");
        }
        if (Array.Exists(args, a => a == "--cinematic-tour"))
        {
            var tour = TrackTour.For(_trackId, _track.Center.Length);
            if (tour == null) { GD.PushError("--cinematic-tour needs ilyich or siberia"); GetTree().Quit(1); return; }
            _cinematicTour = true;
            shots = "--trailer-shots=" + string.Join(',', tour.Parts.Select(p => p.Id));
        }
        if (shots != null)
        {
            var available = SchoolScript.Driving.Concat(_trackId == "siberia"
                ? SchoolScript.Siberia : SchoolScript.Ilyich).ToArray();
            var names = shots["--trailer-shots=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (names.Length == 0 || names.Any(id => !available.Any(x => x.step.Id == id)))
            {
                GD.PushError("Unknown or empty --trailer-shots selection");
                GetTree().Quit(1);
                return;
            }
            _trailerCapture = true;
            _hud.Clean = true;
            _scMarkNode.Visible = false;
            EnterSchool(SchoolCourse.Rally, true);
            _sc.Steps = drone ? SchoolScript.DroneFor(_trackId) : names.Select(id => available.First(x => x.step.Id == id)).ToArray();
            _sc.Step = 0;
            if (!drone) _sc.BeginShow(false);
            if (_cinematicTour) _hud.Visible = false;
            ConfigureMarketingShot(args);
            if (trailerV2 != null) ConfigureTrailerV2();
            if (drone) ConfigureDroneTour();
            TrailerShotLog("start");
        }
        if (Array.Exists(args, a => a == "--trailer-sfx"))
        {
            foreach (string bus in new[] { RadioMusic.BusName, RadioMusic.SubtleBusName })
            {
                int index = AudioServer.GetBusIndex(bus);
                if (index >= 0) AudioServer.SetBusMute(index, true);
            }
        }
    }

    // Opt-in still photography of the real world and car physics. No gameplay changes.
    void ConfigureMarketingShot(string[] args)
    {
        string option = Array.Find(args, a => a.StartsWith("--marketing-shot="));
        if (option == null) return;
        string shot = option.Split('=')[1];
        _cam.ShakeEnabled = false;
        float turn = -1;
        _sc.Beats = new() { B("", () =>
        {
            ScFresh();
            ApplyWorld((shot == "village" ? Actors.None : Actors.Drunks) | Actors.Details | (shot == "plough" ? Actors.Ploughs : Actors.None)
                | (shot == "mig" ? Actors.Jets : Actors.None));
            int at = shot switch { "church" => 754, "barn" => _track.Center.Length - 22,
                "village" => 850, "ice" => 1060, "plough" => 838, _ => 665 };
            ScRoll(at, shot == "plough" ? 3.3f : shot == "mig" ? -8f : 0f,
                shot == "plough" ? 22 : shot == "mig" ? 0 : 55);
            _scLightClock = shot switch { "village" => 3.4f, "ice" => 4.3f, "mig" => 8f, _ => 1f };
            if (shot == "plough") _ploughs.Reset();
            if (shot == "mig") { _jets.Ploughs = null; _jets.CueSchoolSortie(); }
            _scScenic = () => shot switch
            {
                "church" => (Ground(new Vector2(65, 193)), new Vector3(14, 34, 30)),
                "barn" => (Ground(new Vector2(91, 61)), new Vector3(30, 30, 32)),
                "village" => (Ground(new Vector2(36, 150)), new Vector3(15, 34, 33)),
                "ice" => (Ground(new Vector2(196, 238)), new Vector3(14, 32, 26)),
                "plough" => ((_car.Pos + Ground(SiberiaNearestPlough() ?? CarXZ)) * 0.5f, new Vector3(20, 31, 25)),
                _ => ((_car.Pos + (_jets.JetPosition(_jets.NextJet) ?? _car.Pos)) * 0.5f, new Vector3(0, 35, 34)),
            };
        }, (t, dt) =>
        {
            if (shot is "church" or "ice")
            {
                int entry = shot == "church" ? 816 : 1110;
                SlideDrive("hairpin", entry, shot == "church" ? 55 : 48, t, ref turn);
                if (turn >= 0 && t - turn < 0.8f)
                {
                    float into = MathF.Sign(_track.CenterDir(entry).AngleTo(_track.CenterDir(entry + 25)));
                    _car.SetControls(t - turn > 0.45f ? 1 : 0, 0, into, t - turn < 0.55f, false);
                }
            }
            else if (shot == "mig") ScStop();
            else if (shot == "plough")
                ScCruise(22, SchoolScript.SlideSteer(_car, _track, ref _scRoad, 3.3f));
            else _ap.Drive(_car, dt);
            return t > (shot == "mig" ? 25 : 10);
        }, max: 30) };
        _sc.BeatIndex = -1;
        _sc.NextBeat();
    }

    void TrailerShotLog(string state) =>
        GD.Print($"TRAILER {state} {_sc.Current.step.Id} frame={Engine.GetProcessFrames()} time={_sc.Total:F3}");

    void TrailerShotDone()
    {
        TrailerShotLog("end");
        if (_sc.Step + 1 == _sc.Steps.Length)
        {
            _sc.Course = null;
            GetTree().Quit();
            return;
        }
        _sc.Step++;
        _sc.BeginShow(false);
        TrailerShotLog("start");
    }
}
