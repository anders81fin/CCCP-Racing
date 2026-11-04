using System;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The pad's rumble: short kicks (crashes, potholes, shells, a babushka, a landing) and a light
/// continuous buzz (gravel and grass under the wheels, the rev limiter), mixed so a kick is never
/// overwritten by the buzz. The motors get short pulses that are renewed while something lasts, so
/// a frozen game never leaves a pad shaking; nothing is ever heavy for long (kicks are capped at
/// 0.45 s, the buzz at a quarter of the weak motor). On/off is saved in settings.cfg ([input] rumble).
/// RUMBLEDBG prints every kick and every change of the buzz.
/// </summary>
public class Rumble
{
    public const float MaxKick = 0.45f, MaxBuzzWeak = 0.25f, MaxBuzzStrong = 0.08f;
    const float Pulse = 0.2f;

    /// <summary>The pad that shakes (-1: none).</summary>
    public int Device;
    public bool Enabled { get; private set; } = true;
    public static readonly bool Debug = OS.HasEnvironment("RUMBLEDBG");

    string _settingsPath;
    double _kickEnd;
    float _kickWeak, _kickStrong;
    float _buzzWeak, _buzzStrong;
    string _buzzCause = "", _dbgBuzz = "";
    float _sentWeak, _sentStrong;
    double _sentEnd;

    static double Now => Time.GetTicksMsec() / 1000.0;

    /// <summary>A short kick: weak (high, buzzy) and strong (low, heavy) motor 0..1 for this long (capped).</summary>
    public void Kick(float weak, float strong, float duration, string cause)
    {
        if (!Enabled) return;
        weak = Math.Clamp(weak, 0f, 1f);
        strong = Math.Clamp(strong, 0f, 1f);
        duration = Math.Clamp(duration, 0.03f, MaxKick);
        if (Debug) GD.Print($"rumble weak={weak:F2} strong={strong:F2} dur={duration:F2} cause={cause}");
        double now = Now;
        if (now < _kickEnd)
        {
            // Two kicks at once: the harder of each motor, the later end.
            _kickWeak = Math.Max(_kickWeak, weak);
            _kickStrong = Math.Max(_kickStrong, strong);
            _kickEnd = Math.Max(_kickEnd, now + duration);
        }
        else { _kickWeak = weak; _kickStrong = strong; _kickEnd = now + duration; }
        Send(true);
    }

    /// <summary>The continuous buzz for this frame (0 stops it); capped light.</summary>
    public void Buzz(float weak, float strong, string cause)
    {
        _buzzWeak = Math.Clamp(weak, 0f, MaxBuzzWeak);
        _buzzStrong = Math.Clamp(strong, 0f, MaxBuzzStrong);
        _buzzCause = cause;
    }

    /// <summary>Per frame: keeps the motors on what the kicks and the buzz ask for.</summary>
    public void Update()
    {
        if (!Enabled) return;
        if (Debug)
        {
            string b = _buzzWeak + _buzzStrong <= 0 ? "" : $"{_buzzCause} {Math.Round(_buzzWeak * 20) / 20:F2}/{Math.Round(_buzzStrong * 20) / 20:F2}";
            if (b != _dbgBuzz) GD.Print(b == "" ? "rumble buzz off" : $"rumble buzz weak={_buzzWeak:F2} strong={_buzzStrong:F2} cause={_buzzCause}");
            _dbgBuzz = b;
        }
        Send(false);
    }

    void Send(bool force)
    {
        if (Device < 0) return;
        double now = Now;
        bool kick = now < _kickEnd;
        float weak = Math.Max(kick ? _kickWeak : 0, _buzzWeak), strong = Math.Max(kick ? _kickStrong : 0, _buzzStrong);
        if (weak <= 0.005f && strong <= 0.005f)
        {
            if (_sentWeak > 0 || _sentStrong > 0) Input.StopJoyVibration(Device);
            _sentWeak = _sentStrong = 0;
            return;
        }
        // A kick runs to its end; the buzz goes in short pulses, renewed before each runs out.
        float dur = kick && _kickWeak + _kickStrong > 0 ? (float)(_kickEnd - now) : Pulse;
        bool changed = Math.Abs(weak - _sentWeak) > 0.02f || Math.Abs(strong - _sentStrong) > 0.02f;
        if (!force && !changed && now < _sentEnd - 0.05) return;
        Input.StartJoyVibration(Device, weak, strong, dur);
        _sentWeak = weak;
        _sentStrong = strong;
        _sentEnd = now + dur;
    }

    /// <summary>Everything off at once (pause, title, gulag, leaving a run).</summary>
    public void Stop(string why = null)
    {
        _kickEnd = 0;
        _kickWeak = _kickStrong = _buzzWeak = _buzzStrong = 0;
        if (_sentWeak <= 0 && _sentStrong <= 0) return;
        if (Debug) GD.Print($"rumble stop ({why})");
        if (Device >= 0) Input.StopJoyVibration(Device);
        _sentWeak = _sentStrong = 0;
        _dbgBuzz = "";
    }

    public void SetEnabled(bool on)
    {
        if (!on) Stop("rumble off");
        Enabled = on;
        Save();
    }

    /// <summary>Reads [input] rumble from settings.cfg in the data directory (default on).</summary>
    public void Load(string dir)
    {
        _settingsPath = dir + "settings.cfg";
        var cfg = new ConfigFile();
        if (cfg.Load(_settingsPath) == Error.Ok)
        {
            var v = cfg.GetValue("input", "rumble", true);
            if (v.VariantType == Variant.Type.Bool) Enabled = (bool)v;
        }
    }

    void Save()
    {
        if (_settingsPath == null) return;
        var cfg = new ConfigFile();
        cfg.Load(_settingsPath);      // keep the radio and the KGB file
        cfg.SetValue("input", "rumble", Enabled);
        cfg.Save(_settingsPath);
    }
}
