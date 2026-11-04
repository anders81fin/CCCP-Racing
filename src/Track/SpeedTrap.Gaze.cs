using System;
using System.Collections.Generic;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

public partial class SpeedTrap
{
    public const float SightDistance = 90f;
    const float NeckLimit = 1.3962634f; // 80 degrees; the body turns for a car farther behind.
    float _bodyYaw, _headYaw, _headPitch, _sightWait;
    bool _seesCar;
    List<Node> _sightNodes;
    readonly List<(int Index, Obstacle Original)> _tapeObstacles = new();

    public ITapeFxHost TapeFx;
    public bool TapeDriven { get; private set; }
    internal Node3D InspectorNode => _figure;
    internal Node3D HeadNode => _head;
    internal bool SeesCar => _seesCar;

    void ResetGaze()
    {
        _bodyYaw = _figureRest.Basis.Orthonormalized().GetEuler().Y;
        _headYaw = _headPitch = _sightWait = 0;
        _seesCar = false;
        _sightNodes = null;
        if (_head != null) _head.Rotation = Vector3.Zero;
    }

    bool CanSee(Vector3 car)
    {
        Vector3 eye = _figureRest.Origin + Vector3.Up * (1.55f * SizeScale);
        Vector3 aim = car + Vector3.Up * .65f;
        float distance = eye.DistanceTo(aim);
        if (distance > SightDistance || distance < .1f) return false;
        // The rendered terrain, not suspension micro-bumps, blocks his view.
        int steps = Math.Max(2, (int)MathF.Ceiling(distance / 2f));
        for (int i = 1; i < steps; i++)
        {
            Vector3 p = eye.Lerp(aim, i / (float)steps);
            if (_t.MeshHeight(p.X, p.Z) > p.Y - .1f) return false;
        }
        if (_sightNodes == null && IsInsideTree())
        {
            _sightNodes = new();
            var world = WorldScope.Of(this);      // the occluders of this world only (a second world, the replay viewer's, has its own)
            foreach (var node in GetTree().GetNodesInGroup("sight_occluder")) if (WorldScope.Of(node) == world) _sightNodes.Add(node);
        }
        if (_sightNodes != null)
            foreach (var node in _sightNodes)
            {
                if (!IsInstanceValid(node)) continue;
                if (node is SeeThrough building && building.BlocksSight(eye, aim)) return false;
                if (node is FadeUnder roof && roof.BlocksSight(eye, aim)) return false;
            }
        return true;
    }

    void GazeTick(float dt, Vector3? car)
    {
        if (_officerDown.Down) { _head.Rotation = Vector3.Zero; _headYaw = _headPitch = 0; _seesCar = false; return; }
        _sightWait -= dt;
        if (car == null) _seesCar = false;
        else if (_sightWait <= 0)
        {
            _seesCar = CanSee(car.Value);
            _sightWait = .1f;
        }
        float rest = _figureRest.Basis.Orthonormalized().GetEuler().Y;
        float body = rest, head = 0, pitch = 0;
        if (_seesCar && car is Vector3 target)
        {
            Vector3 delta = target + Vector3.Up * .65f - (_figureRest.Origin + Vector3.Up * (1.55f * SizeScale));
            float yaw = TrackData.YawFor(new Vector2(delta.X, delta.Z));
            float relative = Mathf.AngleDifference(rest, yaw);
            body = rest + MathF.Sign(relative) * Math.Max(0, MathF.Abs(relative) - NeckLimit * .8f);
            _bodyYaw = Mathf.RotateToward(_bodyYaw, body, 1.6f * dt);
            head = Math.Clamp(Mathf.AngleDifference(_bodyYaw, yaw), -NeckLimit, NeckLimit);
            pitch = Math.Clamp(MathF.Atan2(delta.Y, new Vector2(delta.X, delta.Z).Length()), -.35f, .35f);
        }
        else _bodyYaw = Mathf.RotateToward(_bodyYaw, body, 1.6f * dt);
        _headYaw = Mathf.RotateToward(_headYaw, head, 3.5f * dt);
        _headPitch = Mathf.MoveToward(_headPitch, pitch, 2f * dt);
        _head.Rotation = new Vector3(_headPitch, _headYaw, 0);
    }

    public void SetTapeDriven(bool on)
    {
        if (TapeDriven == on) return;
        if (!on)
        {
            foreach (var (index, obstacle) in _tapeObstacles) _t.Obstacles[index] = obstacle;
            _tapeObstacles.Clear();
            TapeDriven = false;
            Reset();
            return;
        }
        Reset();
        foreach (int index in new[] { _officerObstacle, _signObstacle })
        {
            _tapeObstacles.Add((index, _t.Obstacles[index]));
            _t.Obstacles[index] = new Obstacle(new Vector2(-1e4f, -1e4f), .1f);
        }
        TapeDriven = true;
    }

    public void RestoreTapeReading(TapeEvent? reading, float age)
    {
        _bubble = reading is TapeEvent e ? Math.Max(0, e.Life - age) : 0;
        _text = reading is TapeEvent value ? value.Param < 0 ? "???" : $"{value.Param:0} КМ/Ч" : null;
    }
}
