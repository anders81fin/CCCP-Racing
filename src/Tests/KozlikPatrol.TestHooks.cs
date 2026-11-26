using Godot;

namespace CCCPRacing.Track;

/// <summary>Test hooks of the duty officer's UAZ (this file is in src/Tests: it is not in the release build).</summary>
public partial class KozlikPatrol
{
    /// <summary>On the road at road metre `s`, `lat` metres off the centre line, at `speed` and chasing (as after the ambush's dash), closing in on the car at `ramDelta` m/s faster than it goes.</summary>
    internal void TestStartChaseOnRoad(float s, float lat, float speed, float ramDelta)
    {
        int n = _t.Center.Length;
        _s = ((s % n) + n) % n; _lat = lat; _speed = speed; _ramDelta = ramDelta;
        int i = (int)_s % n; float f = _s - Mathf.Floor(_s);
        Vector2 c = _t.Center[i].Lerp(_t.Center[(i + 1) % n], f), d = _t.CenterDir(i);
        Position2D = c + new Vector2(-d.Y, d.X) * lat; Forward2D = d; _velocity = d * speed;
        _cur = null; _path = null; _waiting = false; _reverse = false; _chaseOut = false; _kissing = true; _steer = 0;
        _chaseT = _caughtT = 0; _ram = null; LastRam = null; _serialUnknown = true; _carK = -1;
        Siren(true);
        SetMode(Mode.Chase);
    }

    /// <summary>Stopped behind the car for the autograph at road metre `s`, `lat` off the centre line (as after a fine whose shove has died away).</summary>
    internal void TestStartAutographOnRoad(float s, float lat)
    {
        TestStartChaseOnRoad(s, lat, 0f, _ramDelta);
        _speed = 0f; _velocity = Vector2.Zero; _kissing = false; _askT = 0; _keepGap = 0f; BackOffRails = false;
        SetMode(Mode.Autograph);
    }

    internal float TestSteer => _steer;
    internal float TestLat => _lat;
    internal Vector2 TestGuide => _guidePosition;
    internal float TestGap => _gapToCar;
    internal float TestS => _s;
    internal bool TestAutograph => _mode == Mode.Autograph;
    internal int[] TestObstacleIndices => _obs;
}
