using System;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// The dacha's security men at work (PartyDacha.Guards, in a KnockGroup):
/// - the gate man stands his post, turning to watch a car that comes near;
/// - the pond man walks the north shore abreast of the rowing boat, eyes on the official guest;
/// - the garden man walks the west fence to and fro, stopping at each end to look about.
/// They walk with a leg swing and stop wherever the car knocked them over until they are up again.
/// </summary>
public partial class DachaPatrol : Node
{
    const float WatchCar = 30f, Stride = 2.3f;
    // The pond man's shore: a circle round the pond, east of the footbridge, inside the estate.
    const float ShoreR = 19.2f, ShoreX0 = 223.3f, ShoreX1 = 233f, ShoreSpeed = 1.3f;
    // The garden man's beat along the inside of the west fence.
    static readonly Vector2 BeatA = new(205.5f, 167.5f), BeatB = new(205.5f, 183.5f);
    const float BeatSpeed = 1.05f, BeatPause = 3.5f;

    readonly TrackData _t;
    readonly KnockGroup _guards;
    PartyRowboat _boat;
    readonly Vector2[] _at = new Vector2[3];
    readonly float[] _yaw = new float[3], _gait = new float[3], _restYaw = new float[3];
    readonly Node3D[,] _legs = new Node3D[3, 2];
    float _clock, _wait;
    bool _towardB = true;

    public DachaPatrol(TrackData t, KnockGroup guards)
    {
        _t = t;
        _guards = guards;
        Name = "DachaPatrol";
        for (int i = 0; i < Math.Min(3, guards.Count); i++)
        {
            var f = guards.Figure(i);
            _at[i] = new Vector2(f.Position.X, f.Position.Z);
            _yaw[i] = _restYaw[i] = f.Rotation.Y;
            // Hip pivots for the two leg blocks, so a stride swings them from the hip.
            int leg = 0;
            foreach (var child in f.GetChildren())
                if (child is MeshInstance3D m && MathF.Abs(m.Position.Y - 0.39f) < 0.01f && MathF.Abs(MathF.Abs(m.Position.X) - 0.17f) < 0.01f && leg < 2)
                {
                    var hip = new Node3D { Position = new Vector3(m.Position.X, 0.58f, 0) };
                    f.RemoveChild(m);
                    f.AddChild(hip);
                    m.Position = new Vector3(0, 0.39f - 0.58f, m.Position.Z);
                    hip.AddChild(m);
                    _legs[i, leg++] = hip;
                }
        }
    }

    /// <summary>The boat of this patrol's own world (a second world, the replay viewer's, has its own pond).</summary>
    public override void _Ready() => _boat = WorldScope.Find(this, "PartyOfficialRowing") as PartyRowboat;

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (dt <= 0 || _guards.Count < 3) return;
        _clock += dt;
        var car = new Vector2(SeeThrough.Car.X, SeeThrough.Car.Z);

        // The gate: his post; he turns to keep a car near him in view.
        {
            float want = _restYaw[0];
            if (car.DistanceTo(_at[0]) < WatchCar)
                want = _restYaw[0] + Math.Clamp(Mathf.Wrap(TrackData.YawFor(car - _at[0]) - _restYaw[0], -MathF.PI, MathF.PI), -1.2f, 1.2f);
            Step(0, _at[0], want, 0, dt);
        }

        // The pond: abreast of the boat along the shore, facing it whenever he stands.
        {
            Vector2 boat = _boat != null ? new Vector2(_boat.BoatPosition.X, _boat.BoatPosition.Z) : _at[1];
            float x = Mathf.MoveToward(_at[1].X, Math.Clamp(boat.X + 3f, ShoreX0, ShoreX1), ShoreSpeed * dt);
            var pos = Shore(x);
            float moved = pos.DistanceTo(_at[1]);
            float want = moved > 0.002f ? TrackData.YawFor(pos - _at[1]) : TrackData.YawFor(boat - pos);
            Step(1, pos, want, moved, dt);
        }

        // The garden: along the fence, a pause and a look round at each end.
        {
            Vector2 to = _towardB ? BeatB : BeatA, pos = _at[2];
            float moved = 0, want;
            if (_wait > 0)
            {
                _wait -= dt;
                want = TrackData.YawFor(to - pos) + MathF.Sin(_clock * 0.9f) * 1.1f;    // looking about
            }
            else
            {
                pos = pos.MoveToward(to, BeatSpeed * dt);
                moved = pos.DistanceTo(_at[2]);
                want = TrackData.YawFor(to - pos);
                if (pos.DistanceTo(to) < 0.01f) { _towardB = !_towardB; _wait = BeatPause; }
            }
            Step(2, pos, want, moved, dt);
        }
    }

    static Vector2 Shore(float x)
    {
        var c = PartyRowboat.PondCentre;
        float dx = x - c.X;
        return new Vector2(x, c.Y - MathF.Sqrt(Math.Max(0, ShoreR * ShoreR - dx * dx)));
    }

    /// <summary>Guard i to pos (if standing), turned smoothly towards want, legs striding by the distance walked.</summary>
    void Step(int i, Vector2 pos, float want, float moved, float dt)
    {
        if (_guards.IsDown(i)) return;
        _at[i] = pos;
        _yaw[i] += Math.Clamp(Mathf.Wrap(want - _yaw[i], -MathF.PI, MathF.PI), -2.5f * dt, 2.5f * dt);
        // The gait: the swing advances with the ground covered and settles when he stops.
        float swing = 0;
        if (moved > 0.0005f) { _gait[i] += moved / Stride * MathF.Tau; swing = MathF.Sin(_gait[i]) * 0.42f; }
        else _gait[i] = MathF.Round(_gait[i] / MathF.PI) * MathF.PI;
        for (int k = 0; k < 2; k++)
            if (_legs[i, k] != null)
            {
                float cur = _legs[i, k].Rotation.X, target = k == 0 ? swing : -swing;
                _legs[i, k].Rotation = new Vector3(Mathf.Lerp(cur, target, Math.Min(1f, dt * 12f)), 0, 0);
            }
        float bob = moved > 0.0005f ? MathF.Abs(MathF.Cos(_gait[i])) * 0.035f : 0;
        var origin = new Vector3(pos.X, _t.MeshHeight(pos.X, pos.Y) + bob, pos.Y);
        var scale = _guards.Figure(i).Scale;
        _guards.Move(i, new Transform3D(new Basis(Vector3.Up, _yaw[i]).Scaled(scale), origin));
    }
}
