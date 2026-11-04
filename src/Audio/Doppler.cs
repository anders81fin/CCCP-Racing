using System;
using Godot;

namespace CCCPRacing.Audio;

/// <summary>
/// The pitch of a passing sound source. The synthesised voices cannot use the player's pitch scale (a generator
/// ignores it), so each one asks here how much to stretch its own pitch: above 1 closing in, below 1 going away.
/// The closing speed is the distance's rate of change, smoothed, so a tractor going past bends down and the
/// car's own speed counts as well. Exaggerated a little (the speed of sound is 280 m/s), as a game should.
/// </summary>
public struct Doppler
{
    const float SpeedOfSound = 280f, MaxClosing = 70f;
    float _dist, _closing;
    bool _have;

    /// <summary>The pitch ratio now; call once a frame with the source's and the listener's positions.</summary>
    public float Update(Vector3 source, Vector3 ear, float dt)
    {
        dt = Math.Max(dt, 1e-4f);
        float d = source.DistanceTo(ear);
        if (_have) _closing += ((_dist - d) / dt - _closing) * Math.Min(1f, dt * 8f);
        _dist = d;
        _have = true;
        // Right next to the listener the sign flips in no time: the shift fades away inside 4 m.
        float near = Math.Clamp((d - 1.5f) / 4f, 0f, 1f);
        float c = Math.Clamp(_closing, -MaxClosing, MaxClosing) * near;
        return SpeedOfSound / (SpeedOfSound - c);
    }
}
