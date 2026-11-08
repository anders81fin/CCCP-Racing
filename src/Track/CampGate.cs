using System;
using System.Collections.Generic;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// The camp's barrier boom at the middle-pad gate: closed by default. Honk within earshot of the guard hut, standing or still rolling, and the guard raises it
/// (it comes down again by itself five seconds later); a car that hits it hard enough breaks it and drives on through.
/// The barrier is a row of solid circles across the gate mouth (never any other part of the arch).
/// </summary>
public partial class CampGate : Node3D
{
    /// <summary>Impact speed (m/s, into the boom) at which it snaps (about 29 km/h): a gentle touch just bounces off.</summary>
    public const float BreakSpeed = 8f;
    /// <summary>Speed the car loses going through a broken boom (m/s).</summary>
    public const float PloughLoss = 3f;
    const float HornReach = 45f, OpenTime = 1.5f, HoldTime = 5f;      // (HornReach: the guard hears the horn this far from the gate, whatever the car's speed)
    static readonly Obstacle Away = new(new Vector2(-1e4f, -1e4f), .1f);
    public enum State { Closed, Opening, Open, Closing, Broken }
    public State Now { get; private set; } = State.Closed;
    /// <summary>The guard raised the boom (for the HUD).</summary>
    public event Action Opened;

    TrackData _track;
    Node3D _boom;
    Vector3 _hinge;
    float _angle, _hold, _swing;
    int _first, _count;
    readonly List<Obstacle> _saved = new();
    bool _solid = true;
    Vector2 Centre => new(_hinge.X + 6f, _hinge.Z);

    public void Init(TrackData track)
    {
        _track = track;
        if (track.Id != "bonus") return;
        // The arch is built by BonusFieldCamp: pillars at +-GateHalfWidth from the anchor, the hinge housing on the west one.
        Vector2 anchor = new(136, 104);
        float x = anchor.X - BonusFieldCamp.GateHalfWidth, z = anchor.Y + BonusFieldCamp.GateZ + .7f;
        _hinge = new Vector3(x + .3f, track.MeshHeight(x, z) + 1.4f, z);
        _boom = new Node3D { Position = _hinge, Name = "BarrierBoom" };
        AddChild(_boom);
        BuildBoom();
        Tape.Version(this, 1); Tape.Part(this, "root"); Tape.Part(_boom, "boom");      // the world tape: the boom is the only moving part
        _first = track.Obstacles.Count;
        for (float cx = _hinge.X + .6f; cx < anchor.X + BonusFieldCamp.GateHalfWidth - .6f; cx += 1.1f)
            track.Obstacles.Add(new Obstacle(new Vector2(cx, _hinge.Z), .55f, default, false, BreakSpeed, true, PloughLoss));
        _count = track.Obstacles.Count - _first;
    }

    void BuildBoom()
    {
        Color red = new(.75f, .12f, .06f), white = new(.84f, .82f, .72f), steel = new(.22f, .23f, .2f);
        const float Length = 11.2f;
        for (int i = 0; i < 14; i++)
            _boom.AddChild(Props.Part(Props.Box(Length / 14f + .02f, .22f, .22f), i % 2 == 0 ? red : white, new Vector3(.3f + (i + .5f) * Length / 14f, 0, 0)));
        _boom.AddChild(Props.Part(Props.Box(.9f, .45f, .4f), steel, new Vector3(-.5f, 0, 0)));            // counterweight
        _boom.AddChild(Props.Part(Props.Cyl(.25f, .6f, 8), steel, new Vector3(0, 0, 0), new Vector3(90, 0, 0)));
        DrunkTractor.BatchStatic(_boom, null);
        // The rest at the east pillar: a post the closed boom lies in.
        AddChild(Props.Part(Props.Box(.25f, 1.55f, .3f), steel, new Vector3(_hinge.X + Length + .5f, _track.MeshHeight(_hinge.X + Length + .5f, _hinge.Z) + .78f, _hinge.Z)));
    }

    /// <summary>For Breakables.AddKnock: a hard hit on any circle of the boom breaks it.</summary>
    public IEnumerable<(int, Func<Vector3, bool>)> Knocks()
    {
        for (int k = 0; k < _count; k++) yield return (_first + k, Break);
    }
    bool Break(Vector3 vel)
    {
        if (Now == State.Broken || !_solid) return false;
        Now = State.Broken;
        SetSolid(false);
        _swing = vel.Z < 0 ? 1f : -1f;
        return true;
    }

    void SetSolid(bool solid)
    {
        if (_solid == solid) return;
        _solid = solid;
        if (!solid)
        {
            _saved.Clear();
            for (int k = 0; k < _count; k++) { _saved.Add(_track.Obstacles[_first + k]); _track.Obstacles[_first + k] = Away; }
        }
        else
            for (int k = 0; k < _count && k < _saved.Count; k++) _track.Obstacles[_first + k] = _saved[k];
    }

    public void Reset()
    {
        if (_boom == null) return;
        Now = State.Closed;
        _angle = 0;
        _hold = 0;
        SetSolid(true);
        Apply();
    }

    /// <summary>Per tick: the car (null: nobody to open it for) and whether the horn is down.</summary>
    public void Tick(float dt, CarSim car, bool honking)
    {
        if (_boom == null || dt <= 0) return;
        Vector2 car2 = car == null ? new Vector2(-1e4f, -1e4f) : new Vector2(car.Pos.X, car.Pos.Z);
        float distance = car2.DistanceTo(Centre);
        switch (Now)
        {
            case State.Closed:
                if (honking && car != null && distance < HornReach) { Now = State.Opening; Opened?.Invoke(); }
                break;
            case State.Opening:
                _angle = Math.Min(1f, _angle + dt / OpenTime);
                if (_angle > .45f) SetSolid(false);
                if (_angle >= 1f) { Now = State.Open; _hold = HoldTime; }
                break;
            case State.Open:
                // Comes down by itself HoldTime seconds after it is fully up.
                _hold -= dt;
                if (_hold <= 0) Now = State.Closing;
                break;
            case State.Closing:
                // A car in the mouth stops it coming down on the roof.
                if (distance < 5f) { Now = State.Opening; break; }
                _angle = Math.Max(0f, _angle - dt / OpenTime * .8f);
                if (_angle < .45f) SetSolid(true);
                if (_angle <= 0f) Now = State.Closed;
                break;
            case State.Broken:
                _angle = Math.Min(1f, _angle + dt * 2f);
                break;
        }
        Apply();
    }

    void Apply()
    {
        if (Now == State.Broken)
        {
            // Snapped off its hinge and lying across the verge.
            _boom.Rotation = new Vector3(0, _swing * 1.1f * Mathf.SmoothStep(0, 1, _angle), -.18f * _angle);
            return;
        }
        _boom.Rotation = new Vector3(0, 0, Mathf.DegToRad(78f) * Mathf.SmoothStep(0, 1, _angle));
    }
}
