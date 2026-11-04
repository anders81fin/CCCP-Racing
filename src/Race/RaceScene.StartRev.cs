using System;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    const float StartRevDuration = 4.2f;
    float _startRevLeft;
    bool _startClutchAccepted;
    bool StartRevving => _state == State.Countdown && _startRevLeft > 0;
    bool StartClutchWaiting => _state == State.Countdown && !StartRevving && !_showKeys
        && !InSchool && !InLanRace && !_autodrive && !_padAuto && _clutchKickAt <= 0 && !_startClutchAccepted;

    /// <summary>Hand over once per start. A clutch already held needs no extra press;
    /// after acceptance, an early release remains a real possible false start.</summary>
    bool WaitForStartClutch()
    {
        if (!StartClutchWaiting) return false;
        if (!_driver.Read().Clutch) return true;
        _startClutchAccepted = true;
        _car.Clutch = true;
        _car.Neutral = false;
        return false;
    }

    /// <summary>Two throttle blips before the lights: clutch down, limiter, then exhaust crackle.</summary>
    bool StartRevControls(float dt)
    {
        if (!StartRevving) return false;
        float elapsed = StartRevDuration - _startRevLeft;
        bool throttle = !_showKeys && (elapsed < 1.25f || elapsed >= 2.1f && elapsed < 3.35f);
        _car.Neutral = true;
        _car.Clutch = true;
        _car.SetControls(throttle, false, 0, true);
        _car.Brake = 1;
        if (!_showKeys)
        {
            _startRevLeft = Math.Max(0, _startRevLeft - dt);
            // A slight mechanical tremor; exhaust pops provide their own stronger jolts.
            if (_car.Limiter) _cam.Shake(0.025f);
        }
        return true;
    }
}
