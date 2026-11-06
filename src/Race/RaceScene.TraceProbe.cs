using System;
using System.IO;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// Refactoring check (tools/trace/world-trace.sh): WORLDTRACE=file writes, each physics step, a hash of the
/// car and of every collision circle (the actors move theirs), and every 30 steps one of the scene's own
/// named nodes (shown, stopped); after WORLDTRACE_FRAMES steps it writes END and quits. Two builds that write
/// the same files drive the world bit for bit alike. Run with --fixed-fps 60: each frame then takes the
/// same number of physics steps (2 at 120 Hz, more while a school fast-forwards).
/// </summary>
public partial class RaceScene
{
    StreamWriter _trace;
    int _traceFrame, _traceEnd;

    void TraceTick()
    {
        if (_traceFrame == 0)
        {
            string path = OS.GetEnvironment("WORLDTRACE");
            if (path == "") { _traceFrame = -1; return; }
            _trace = new StreamWriter(path) { AutoFlush = true };    // a school ending with --school-quit leaves no time to close it
            _traceEnd = int.Parse(OS.GetEnvironment("WORLDTRACE_FRAMES") is var f && f != "" ? f : "3000");
        }
        if (_traceFrame < 0) return;
        ulong h = 1469598103934665603;
        void Mix(float v) { h = (h ^ (uint)BitConverter.SingleToInt32Bits(v)) * 1099511628211; }
        void V3(Vector3 v) { Mix(v.X); Mix(v.Y); Mix(v.Z); }
        V3(_car.Pos); V3(_car.Vel); V3(_car.AngVel); V3(_car.Rot.X); V3(_car.Rot.Z); Mix(_car.Rpm);
        foreach (var o in _track.Obstacles) { Mix(o.Center.X); Mix(o.Center.Y); Mix(o.Radius); Mix(o.Velocity.X); Mix(o.Velocity.Y); }
        if (_jetsAll != null) h = (h ^ _jetsAll.StateHash()) * 1099511628211;      // (the jets' own flights, not only their circles: a changed random draw shows at once)
        if (OS.GetEnvironment("WORLDTRACE_DETAIL") is var det && det != "" && Math.Abs(_traceFrame - int.Parse(det)) <= (OS.GetEnvironment("WORLDTRACE_SPAN") is var sp && sp != "" ? int.Parse(sp) : 3))
        {
            // Which collision circle (whose) or which car value first parts two runs.
            _trace.WriteLine($"  car {_car.Pos} {_car.Vel} {_car.AngVel} {_car.Rpm} ctl {_car.Throttle} {_car.Brake} {_car.SteerIn} {_car.SteerAngle} {_car.Handbrake} {_car.Gear} school {(InSchool ? _sc.Current.step.Id + " " + _sc.Phase : "-")}");
            foreach (var c in GetChildren()) _trace.WriteLine($"  node {c.Name} {c.GetClass()}");
            for (int i = 0; i < _track.Obstacles.Count; i++)
            {
                string owner = "static";
                foreach (var (who, j, _) in _worldObstacles) if (j == i) { owner = who.ToString(); break; }
                _trace.WriteLine($"  #{i} {owner} {_track.Obstacles[i]}");
            }
        }
        string nodes = "";
        if (_traceFrame % 30 == 0)
        {
            ulong n = h;
            int count = 0;
            foreach (var c in GetChildren())
            {
                if (c.Name.ToString().StartsWith('@')) continue;    // unnamed one-shots (sounds...) go when their audio ends: real time
                foreach (char ch in c.Name.ToString()) n = (n ^ ch) * 1099511628211;
                n = (n ^ (uint)c.ProcessMode) * 1099511628211;
                if (c is Node3D d) n = (n ^ (uint)(d.Visible ? 7 : 3)) * 1099511628211;
                count++;
            }
            nodes = $" nodes={count}:{n:x16}";
        }
        _trace.WriteLine($"{_traceFrame} {_state}{(Replaying ? "R" : "")} {h:x16} car=({_car.Pos.X:F3},{_car.Pos.Z:F3}){nodes}");
        if (++_traceFrame >= _traceEnd) { _trace.WriteLine("END"); _trace.Close(); _traceFrame = -1; CCCPRacing.Audio.AudioMix.Quit(this); }
    }
}
