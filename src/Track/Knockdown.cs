using System;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// A person (or a road sign) the car knocks over instead of stopping against like a wall: it tips
/// away from the car, lies a while and gets up again (a sign stays down until the run restarts).
/// Its obstacle is the actor's own, out of the way from the hit until it stands again, so the car
/// ploughs on (Breakables.AddKnock, the obstacle's Loss: what the car loses going through).
/// The owner multiplies Tilt into its root's basis (the pivot at the feet).
/// </summary>
public class Knockdown
{
    const float FallTime = 0.35f, LieTime = 2.6f, RiseTime = 0.8f;
    static readonly Obstacle Away = new(new Vector2(-1e4f, -1e4f), 0.1f);
    readonly bool _stays;
    float _t = -1f;
    Vector3 _axis = Vector3.Right;
    TrackData _track;
    int _obstacle = -1;
    Obstacle _orig;
    bool _moved;

    public Knockdown(bool stays = false) { _stays = stays; }

    /// <summary>The owner's obstacle for it (in t.Obstacles): moved out of the way while it is down.</summary>
    public void Bind(TrackData t, int obstacle)
    {
        _track = t;
        _obstacle = obstacle;
    }

    /// <summary>Knocked and not yet up again: the obstacle stays out of the way.</summary>
    public bool Down => _t >= 0f;

    /// <summary>The car hit it going this way: it starts to fall (false if already down).</summary>
    public bool Hit(Vector3 carVel)
    {
        if (_t >= 0f) return false;
        var d = new Vector2(carVel.X, carVel.Z);
        d = d.LengthSquared() > 0.01f ? d.Normalized() : Vector2.Up;
        _axis = new Vector3(d.Y, 0, -d.X);      // up x travel: the top goes the car's way
        _t = 0f;
        if (_track != null && _obstacle >= 0)
        {
            _orig = _track.Obstacles[_obstacle];
            _track.Obstacles[_obstacle] = Away;
            _moved = true;
        }
        return true;
    }

    public void Tick(float dt)
    {
        if (_t < 0f) return;
        _t += dt;
        if (!_stays && _t > FallTime + LieTime + RiseTime) { _t = -1f; PutBack(); }
    }

    /// <summary>A new run: standing, its obstacle where it was.</summary>
    public void Reset()
    {
        _t = -1f;
        PutBack();
    }

    void PutBack()
    {
        if (!_moved) return;
        _track.Obstacles[_obstacle] = _orig;
        _moved = false;
    }

    /// <summary>The tilt now (identity standing).</summary>
    public Basis Tilt
    {
        get
        {
            if (_t < 0f) return Basis.Identity;
            float k = _t < FallTime ? Mathf.SmoothStep(0f, 1f, _t / FallTime)
                : _stays || _t < FallTime + LieTime ? 1f
                : 1f - Mathf.SmoothStep(0f, 1f, (_t - FallTime - LieTime) / RiseTime);
            return new Basis(_axis, k * MathF.PI * 0.5f);
        }
    }
}
