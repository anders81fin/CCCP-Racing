using System;
using System.Linq;
using CCCPRacing.Audio;
using Godot;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    bool _tourCapture;

    // The tour capture mode: the shared entry of the scenic tours: --drone-tour and --cinematic-tour run their parts as
    // school shows at normal speed, without teaching cards. --trailer-sfx also separates the world sound
    // from the music for editing. (The trailer-only options --trailer-shots, --trailer-v2 and
    // --marketing-shot were removed 2026-11-11: the trailers are finished.)
    void StartTourCapture()
    {
        var args = CmdArgs();
        string[] names = null;
        bool drone = Array.Exists(args, a => a == "--drone-tour");
        if (drone)
        {
            if (_trackId is not ("ilyich" or "siberia" or "atomgrad" or "bonus")) { GD.PushError("--drone-tour needs --track=ilyich, siberia, atomgrad or bonus"); CCCPRacing.Audio.AudioMix.Quit(this, 1); return; }
            names = new[] { _trackId == "siberia" ? "snow_hairpin" : "hairpin" };
        }
        if (Array.Exists(args, a => a == "--cinematic-tour"))
        {
            var tour = TrackTour.For(_trackId, _track.Center.Length);
            if (tour == null) { GD.PushError("--cinematic-tour needs ilyich or siberia"); CCCPRacing.Audio.AudioMix.Quit(this, 1); return; }
            _cinematicTour = true;
            names = tour.Parts.Select(p => p.Id).ToArray();
        }
        if (names != null)
        {
            var available = SchoolScript.Driving.Concat(_trackId == "siberia"
                ? SchoolScript.Siberia : SchoolScript.Ilyich).ToArray();
            if (names.Length == 0 || names.Any(id => !available.Any(x => x.step.Id == id)))
            {
                GD.PushError("Unknown or empty tour part selection");
                CCCPRacing.Audio.AudioMix.Quit(this, 1);
                return;
            }
            _tourCapture = true;
            _hud.Clean = true;
            _scMarkNode.Visible = false;
            EnterSchool(SchoolCourse.Rally, true);
            _sc.Steps = drone ? SchoolScript.DroneFor(_trackId) : names.Select(id => available.First(x => x.step.Id == id)).ToArray();
            _sc.Step = 0;
            if (!drone) _sc.BeginShow(false);
            if (_cinematicTour) _hud.Visible = false;
            if (drone) ConfigureDroneTour();
            TourShotLog("start");
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

    void TourShotLog(string state) =>
        GD.Print($"TOURSHOT {state} {_sc.Current.step.Id} frame={Engine.GetProcessFrames()} time={_sc.Total:F3}");

    void TourShotDone()
    {
        TourShotLog("end");
        if (_sc.Step + 1 == _sc.Steps.Length)
        {
            _sc.Course = null;
            CCCPRacing.Audio.AudioMix.Quit(this);
            return;
        }
        _sc.Step++;
        _sc.BeginShow(false);
        TourShotLog("start");
    }
}
