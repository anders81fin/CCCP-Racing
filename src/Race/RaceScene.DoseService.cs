using System;
using System.Collections.Generic;
using CCCPRacing.Track;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    List<Vector2> _doseServicePath;
    int _doseServiceNext;
    float _doseServiceYaw, _doseServiceSpeed, _doseServiceStuck;
    bool _doseServiceWasWashing;
    enum DoseServicePhase { None, Braking, Approach, Scan, Order, Wash }
    DoseServicePhase _doseServicePhase;
    Vector2 _inspectorPosition, _inspectionCentre, _inspectionEntry;
    float _inspectionAngle, _inspectionTime, _inspectorStride;
    bool _inspectorReturning, _doseServiceNeutral;
    const float InspectionRadius = 3.5f, ScanTime = 8f, OrderTime = 1.1f;
    bool DoseServiceActive => _doseServicePhase != DoseServicePhase.None;

    void BeginDoseService()
    {
        _doseServicePhase = DoseServicePhase.Braking;
        _doseServiceNeutral = _car.Neutral;
        _hud.WrongWay = false;
        _wrongWay = 0f;
        if (DoseDebug) GD.Print($"dose service: braking at lap {_timer.LapsDone}, speed {_car.Speed * 3.6f:F1} km/h");
    }

    void BeginInspection()
    {
        _doseServicePhase = DoseServicePhase.Approach;
        _inspectionCentre = new Vector2(_car.Pos.X, _car.Pos.Z);
        _inspectorPosition = _dosimetrist?.Home ?? _inspectionCentre + Vector2.Right * InspectionRadius;
        Vector2 side = (_inspectorPosition - _inspectionCentre).Normalized();
        if (side == Vector2.Zero) side = Vector2.Right;
        _inspectionAngle = side.Angle();
        _inspectionEntry = _inspectionCentre + side * InspectionRadius;
        _inspectorStride = _inspectionTime = 0f;
        if (DoseDebug) GD.Print($"dose service: stopped at {_inspectionCentre}, speed {_car.Speed:F2} m/s");
    }

    void BuildDoseServiceRoute()
    {
        var road = _track.Center;
        var start = new Vector2(_car.Pos.X, _car.Pos.Z);
        int from = _track.NearestCenter(start, out _);
        int south = _track.NearestCenter(new Vector2(_rad.WashX - 22f, _rad.WashZ + 17f), out _);
        int north = _track.NearestCenter(new Vector2(_rad.WashX - 22f, _rad.WashZ - 14f), out _);
        _doseServicePath = new List<Vector2>();
        void Add(Vector2 point)
        {
            if (_doseServicePath.Count == 0 || _doseServicePath[^1].DistanceSquaredTo(point) > .1f)
                _doseServicePath.Add(point);
        }

        for (int i = from; i != south; i = (i + road.Length - 1) % road.Length) Add(road[i]);
        Add(road[south]);
        // The two paved approaches meet the open ends of the wash at different heights on the map.
        Add(new Vector2(_rad.WashX - 4f, _rad.WashZ + 17f));
        Add(new Vector2(_rad.WashX, _rad.WashZ + 12f));
        Add(new Vector2(_rad.WashX, _rad.WashZ - 11f));
        Add(new Vector2(_rad.WashX - 5f, _rad.WashZ - 14f));
        Add(road[north]);
        int end = _track.NearestCenter(new Vector2(_track.StartPos.X, _track.StartPos.Z), out _);
        for (int i = (north + 1) % road.Length; i != (end + 1) % road.Length; i = (i + 1) % road.Length) Add(road[i]);
        Add(new Vector2(_track.StartPos.X, _track.StartPos.Z));
        _doseServiceNext = 0;
        _doseServiceYaw = TrackData.YawFor(new Vector2(_car.Forward.X, _car.Forward.Z));
        _doseServiceSpeed = 0f; _doseServiceStuck = 0f;
        _doseServiceWasWashing = false;
        _doseServicePhase = DoseServicePhase.Wash;
        _inspectorReturning = _dosimetrist != null;
        _hud.Alert("AUTOPILOT • TO THE WASH", "CONTROL RETURNS AT THE START LINE", 4f, Hud.Gold, urgent: true);
        if (DoseDebug) GD.Print($"dose service: autopilot at lap {_timer.LapsDone}, route {_doseServicePath.Count} points");
    }

    void DoseServiceStep(float dt)
    {
        PreStepWorldTick(dt);
        _maxImpact = 0f;
        _impactMaterial = ImpactMaterial.Unknown;
        _impactBroke = false;
        _car.SetControls(false, false, 0f, false);
        _car.Brake = 1f;
        _car.Neutral = true;

        CarStepResult step = default;
        if (_doseServicePhase == DoseServicePhase.Braking)
        {
            step = StepCar(dt);
            if (_car.Speed < .2f && _car.Up.Y > .45f) BeginInspection();
            if (_rescue.Active)
            {
                _doseServicePhase = DoseServicePhase.None;
                _car.Neutral = _doseServiceNeutral;
            }
        }
        else
        {
            _car.AngVel = _car.Vel = Vector3.Zero;
            if (_doseServicePhase == DoseServicePhase.Wash) MoveDoseServiceCar(dt);
            else UpdateDoseInspection(dt);
        }

        if (_inspectorReturning) ReturnDoseInspector(dt);
        TickActors(dt, replay: false, afterCar: true);
        _fx.Emit(_car, dt);
        LapClockAndRecord(dt, step, doseService: true);
    }

    void UpdateDoseInspection(float dt)
    {
        if (_doseServicePhase == DoseServicePhase.Approach)
        {
            WalkDoseInspector(_inspectionEntry, dt);
            if (_inspectorPosition.DistanceSquaredTo(_inspectionEntry) < .001f)
                _doseServicePhase = DoseServicePhase.Scan;
            return;
        }

        _inspectionTime += dt;
        if (_doseServicePhase == DoseServicePhase.Scan)
        {
            float progress = Math.Min(1f, _inspectionTime / ScanTime);
            float angle = _inspectionAngle + Mathf.Tau * progress;
            _inspectorPosition = _inspectionCentre + Vector2.FromAngle(angle) * InspectionRadius;
            _inspectorStride += Mathf.Tau * InspectionRadius / ScanTime * dt;
            _dosimetrist?.PoseServiceWalk(_inspectorPosition, _inspectionCentre - _inspectorPosition,
                _inspectorStride, measuring: true);
            if (progress == 1f)
            {
                _doseServicePhase = DoseServicePhase.Order;
                _inspectionTime = 0f;
                if (DoseDebug) GD.Print($"dose service: full inspection, walked {_inspectorStride:F1} m");
                _hud.Alert("GO TO THE WASH", "THE DOSIMETRIST HAS FINISHED THE CHECK", 2f, Hud.Red, urgent: true);
            }
            return;
        }

        _dosimetrist?.PoseServiceWalk(_inspectorPosition, _inspectionCentre - _inspectorPosition, 0f,
            ordering: true);
        if (_inspectionTime >= OrderTime) BuildDoseServiceRoute();
    }

    void WalkDoseInspector(Vector2 target, float dt)
    {
        Vector2 direction = target - _inspectorPosition;
        Vector2 next = _inspectorPosition.MoveToward(target, 2.6f * dt);
        _inspectorStride += next.DistanceTo(_inspectorPosition);
        _inspectorPosition = next;
        _dosimetrist?.PoseServiceWalk(next, direction, _inspectorStride);
    }

    void ReturnDoseInspector(float dt)
    {
        if (_dosimetrist == null) { _inspectorReturning = false; return; }
        WalkDoseInspector(_dosimetrist.Home, dt);
        if (_inspectorPosition.DistanceSquaredTo(_dosimetrist.Home) >= .001f) return;
        _inspectorReturning = false;
        _dosimetrist.EndServiceWalk();
        if (DoseDebug) GD.Print("dose service: inspector returned to post");
    }

    void MoveDoseServiceCar(float dt)
    {
        Vector2 point = new(_car.Pos.X, _car.Pos.Z);
        Vector2 heading = new(_car.Forward.X, _car.Forward.Z);
        heading = heading.LengthSquared() > 1e-6f ? heading.Normalized() : Vector2.FromAngle(0);
        // The waypoint is reached when the car is near it, or has gone past it (so a wide curve never circles a point it cannot hit).
        for (;;)
        {
            Vector2 toWaypoint = _doseServicePath[_doseServiceNext] - point;
            bool last = _doseServiceNext == _doseServicePath.Count - 1;
            float reach = last ? 1.0f : 3.2f + .4f * _doseServiceSpeed;      // (a point inside the car's own turning circle is skipped: it could never be reached)
            if (toWaypoint.Length() >= reach && !(toWaypoint.Dot(heading) < 0f && toWaypoint.Length() < reach * 2.5f && _doseServiceNext > 0)) break;
            if (last) { FinishDoseService(); return; }
            _doseServiceNext++;
            _doseServiceStuck = 0f;
        }
        // The car drives like the player's: pure pursuit of a point ahead on the route, the front wheels steered (SteerAngle, slewed), the heading turned by the bicycle model, the speed eased in curves.
        // Lookahead along the route's points from the car.
        float lookahead = 2.6f + .55f * _doseServiceSpeed;
        Vector2 from = point, target = _doseServicePath[_doseServiceNext];
        float left = lookahead;
        for (int i = _doseServiceNext; i < _doseServicePath.Count; i++)
        {
            Vector2 to = _doseServicePath[i];
            float seg = from.DistanceTo(to);
            target = to;
            if (seg >= left) { target = from.Lerp(to, left / Math.Max(seg, 1e-4f)); break; }
            left -= seg; from = to;
        }
        Vector2 toTarget = target - point;
        float alpha = toTarget.LengthSquared() > 1e-4f ? heading.AngleTo(toTarget) : 0f;      // (+ = to the right, the same sign as SteerAngle)
        float wheelbase = _params.RearZ - _params.FrontZ;
        float maxSteer = _params.MaxSteer * .7f;
        float steerWanted = Math.Clamp(MathF.Atan2(2f * wheelbase * MathF.Sin(alpha), Math.Max(toTarget.Length(), lookahead * .5f)), -maxSteer, maxSteer);
        _car.SteerAngle = Mathf.MoveToward(_car.SteerAngle, steerWanted, 2.4f * dt);
        bool washing = InWash(point);
        if (washing != _doseServiceWasWashing && DoseDebug)
            GD.Print($"dose service: wash {(washing ? "entered" : "left")} at {point}, dose {_dose:F2}");
        if (_doseServiceWasWashing && !washing) _dose = 0f;
        float desired = washing ? 3f : 9f;
        desired *= 1f - .6f * Math.Abs(_car.SteerAngle) / maxSteer;      // (a bend is taken slower)
        if (_doseServiceNext >= _doseServicePath.Count - 2) desired = Math.Min(desired, 3f);
        _doseServiceSpeed = Mathf.MoveToward(_doseServiceSpeed, Math.Max(desired, 1.2f), 5f * dt);
        heading = heading.Rotated(_doseServiceSpeed * dt * MathF.Tan(_car.SteerAngle) / Math.Max(wheelbase, 1f));
        // A safety net: a car that circles without getting on (a waypoint it cannot reach) is turned towards the route at a fixed rate, as the old autopilot did.
        _doseServiceStuck += dt;
        if (_doseServiceStuck > 7f && toTarget.LengthSquared() > 1e-4f)
            heading = heading.Rotated(Mathf.Clamp(heading.AngleTo(toTarget), -2f * dt, 2f * dt));
        _doseServiceYaw = TrackData.YawFor(heading);
        _car.Rot = new Basis(Vector3.Up, _doseServiceYaw);
        Vector2 next = point + heading * (_doseServiceSpeed * dt);
        Vector3 position = new(next.X, _track.MeshHeight(next.X, next.Y) + .55f, next.Y);
        _car.Vel = (position - _car.Pos) / dt;
        _car.Pos = position;
        foreach (var wheel in _car.Wheels)
        {
            wheel.SpinSpeed = _doseServiceSpeed / _params.WheelRadius;
            wheel.SpinAngle += wheel.SpinSpeed * dt;
        }
        if (washing)
        {
            _dose = Math.Max(0f, _dose - Math.Max(WashRate, _rad.Limit / 3f) * dt);
            ZoneDosimetrist.WashSprayFx(_fx, _rng, _car.Pos, dt);
        }
        _dosimetrist?.SetWash(washing, _car.Pos);
        _doseServiceWasWashing = _washing = washing;
        if (DoseDebug && Engine.GetProcessFrames() % 20 == 0) GD.Print($"dose service drive: ({point.X:F1},{point.Y:F1}) yaw {Mathf.RadToDeg(_doseServiceYaw):F0} steer {Mathf.RadToDeg(_car.SteerAngle):F0} speed {_doseServiceSpeed:F1} wp {_doseServiceNext}/{_doseServicePath.Count}");
    }

    /// <summary>The route's end: the car is put on the start line, straight, the wheels straight and still, and the driver has the car again.</summary>
    void FinishDoseService()
    {
        _car.Pos = _track.StartPos;
        _car.Rot = new Basis(Vector3.Up, _track.StartYaw);
        _car.SteerAngle = 0f;
        foreach (var wheel in _car.Wheels) wheel.SpinSpeed = 0f;
        _dosimetrist?.SetWash(false, _car.Pos);
        _washing = false;
        _doseServicePath = null;
        _doseServicePhase = DoseServicePhase.None;
        _car.Neutral = _doseServiceNeutral;
        _inspectorReturning = false;
        _dosimetrist?.EndServiceWalk();
        if (DoseDebug) GD.Print($"dose service: complete at lap {_timer.LapsDone}, dose {_dose:F2}, next lap clock {_lapClock:F2}");
        _hud.Alert("CHECKED AND WASHED", "DRIVE ON, TOVARISH", 2.5f, Hud.Paper, urgent: true);
    }
}
