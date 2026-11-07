using System;
using System.Collections.Generic;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// What burns every physics step of a vehicle the tape poses: the tanks' track dust and diesel smoke, the BTRs' dust, the snow the ploughs throw (docs/replay-tape.md, section 16).
/// Not an event per puff (a tank would write more than a thousand a lap): the replay's own emitters make the puffs from the pose the tape gives, with the live code's formulas and a
/// random generator of their own. The speed is the posed hull's movement between two steps (a seek's jump is not speed); a seek starts them afresh.
/// </summary>
public sealed class TapeEmitters
{
    public enum Kind { Tank, Btr, Plough, Soot }
    sealed class E { public Node3D Node; public Kind K; public Vector3 Prev; public bool Has; public float Dust, SprayL, SprayR, Soot; public Vector3 Pipe; public float Power, SpeedRef = 8f, FixedThrottle = -1f, MinSpeed; }
    readonly TyreEffects _fx;
    readonly TrackData _t;
    readonly List<E> _list = new();
    readonly RandomNumberGenerator _rng = new() { Seed = 1963 };
    /// <summary>Puffs made since the player exists, by kind (the tests count them).</summary>
    public readonly SortedDictionary<string, int> Made = new();

    public TapeEmitters(TyreEffects fx, TrackData t) { _fx = fx; _t = t; }
    public int Count => _list.Count;

    public void Add(Node3D node, Kind kind) { if (node != null) _list.Add(new E { Node = node, K = kind }); }
    /// <summary>The player's diesel smoke (TyreEffects.EngineSoot) from a covered vehicle's pipe: the same formula as its live code, from the pose. kind Soot: nothing else is made for it.</summary>
    public void AddSoot(Node3D node, Kind kind, Vector3 pipe, float power, float speedRef = 8f, float fixedThrottle = -1f, float minSpeed = 0f)
    { if (node != null) _list.Add(new E { Node = node, K = kind, Pipe = pipe, Power = power, SpeedRef = speedRef, FixedThrottle = fixedThrottle, MinSpeed = minSpeed }); }
    public void Clear() => _list.Clear();
    /// <summary>A seek: the hulls jumped, so the next step only learns where they are.</summary>
    public void Restart() { foreach (var e in _list) { e.Has = false; e.Dust = e.SprayL = e.SprayR = e.Soot = 0; } }

    void Note(string k) => Made[k] = Made.GetValueOrDefault(k) + 1;

    public void Tick(float dt)
    {
        if (dt <= 0 || _list.Count == 0) return;
        _fx.BeginReplay(0);
        try { foreach (var e in _list) Step(e, dt); }
        finally { _fx.EndReplay(); }
    }

    void Step(E e, float dt)
    {
        if (!GodotObject.IsInstanceValid(e.Node) || !e.Node.IsInsideTree() || !e.Node.IsVisibleInTree()) { e.Has = false; return; }
        var xf = e.Node.GlobalTransform; Vector3 pos = xf.Origin;
        if (!e.Has) { e.Prev = pos; e.Has = true; return; }
        Vector3 delta = pos - e.Prev; e.Prev = pos;
        delta.Y = 0;
        if (delta.Length() > 8f * dt + 2f) return;      // a jump (a wrap, a spawn), not driving
        float speed = delta.Length() / dt;
        switch (e.K)
        {
            case Kind.Tank:
            {
                Vector3 f3 = -xf.Basis.Z; var f = new Vector2(f3.X, f3.Z).Normalized();
                float sign = delta.Dot(f3) < 0 ? -1f : 1f;
                if (_rng.Randf() < dt * 30f * (speed / TankBattle.DustSpeedRef))
                {
                    Vector2 back = new Vector2(pos.X, pos.Z) - f * 3.2f * sign + new Vector2(-f.Y, f.X) * _rng.RandfRange(-1.6f, 1.6f);
                    TankBattle.DustFx(_fx, _t, back); Note("tank-dust");
                }
                // (the exhaust smoke is the soot of AddSoot, below)
                break;
            }
            case Kind.Btr:
                if (speed > .5f && (e.Dust -= dt) <= 0) { e.Dust = .35f; BtrPatrols.DustFx(_fx, pos + xf.Basis.Z * 3 + Vector3.Up * .3f); Note("btr-dust"); }
                break;
            case Kind.Soot: break;
            case Kind.Plough:
                if (speed > 1f)
                {
                    var v3 = delta / dt;
                    foreach (float s in new[] { -1f, 1f })
                    {
                        SnowPloughs.SprayWing(xf, s, out Vector3 wing, out Vector3 outward);
                        if (s < 0) _fx.PloughSpray(wing, outward, v3, speed, ref e.SprayL, dt); else _fx.PloughSpray(wing, outward, v3, speed, ref e.SprayR, dt);
                    }
                    Note("plough-spray");
                }
                break;
        }
        if (e.Power > 0f && speed >= e.MinSpeed)
        {
            Vector3 fwd = -xf.Basis.Z.Normalized();
            _fx.EngineSoot(xf * e.Pipe, -fwd, delta / dt, e.FixedThrottle >= 0f ? e.FixedThrottle : Mathf.Clamp(speed / e.SpeedRef, 0.25f, 1f), e.Power, ref e.Soot, dt);
            Note("soot");
        }
    }
}
