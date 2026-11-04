using System;
using Godot;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    int _tapeShootJetInstance = -1, _tapeShootJetLogSecond = -1;
    bool _tapeShootJetAirborne;

    bool SiberiaTapeJetCamera(float dt)
    {
        Node3D jet = null;
        int second = (int)_replayClock;
        if (_tapeSink != null)
        {
            foreach (var pair in _tapeSink.Live)
            {
                var node = pair.Value;
                if (!node.HasMeta(WorldTapeConst.KindMeta) || node.GetMeta(WorldTapeConst.KindMeta).AsString() != "jet") continue;
                var p = node.GlobalPosition;
                var fx = node.GetNodeOrNull<Track.JetReplayFx>("TapeState");
                if (second != _tapeShootJetLogSecond)
                    GD.Print(FormattableString.Invariant($"JET SHOT t={_replayClock:F2} id={pair.Key} pos={p.X:F2},{p.Y:F2},{p.Z:F2} burn={fx?.Sound.Afterburner ?? 0:F2}"));
                if (_tapeShootJetInstance < 0 && (fx?.Sound.Afterburner ?? 0) > .5f
                    && p.Z < 75 && p.X > 15 && p.X < 310
                    && p.Y < _track.MeshHeight(p.X, p.Z) + 20)
                {
                    _tapeShootJetInstance = pair.Key;
                    GD.Print($"JET SHOT selected id={pair.Key} t={_replayClock:F2}");
                }
                if (pair.Key == _tapeShootJetInstance) jet = node;
            }
        }
        _tapeShootJetLogSecond = second;
        if (jet == null) return false;

        float u = Mathf.Clamp((_replayClock - Math.Max(0, _tapeReplayStart)) / Math.Max(1, _tapeReplayQuit), 0, 1);
        float pullback = Mathf.Clamp((u - .3f) / .7f, 0, 1);
        float reveal = pullback * pullback * (3 - 2 * pullback);
        var pos = jet.GlobalPosition;
        if (pos.Y - _track.MeshHeight(pos.X, pos.Z) > 12) _tapeShootJetAirborne = true;
        if (_tapeShootJetAirborne)
        {
            // Cut to a vertical aircraft follow. Limit the ground footprint to
            // the actual terrain, including its lowest river surface, so the
            // finite world's outer edge cannot enter the frame.
            var overhead = pos + Vector3.Up * Mathf.Lerp(55, 80, reveal);
            float groundHeight = overhead.Y + 4;
            float marginX = Math.Max(2, Math.Min(pos.X - 8, _track.WorldSize - 8 - pos.X));
            float marginZ = Math.Max(2, Math.Min(pos.Z - 8, _track.WorldSize - 8 - pos.Z));
            float tangent = Math.Min(marginX / (groundHeight * (16f / 9f)), marginZ / groundHeight);
            _cam.Fov = Math.Min(60, Mathf.RadToDeg(2 * MathF.Atan(tangent)));
            _cam.Near = .2f; _cam.Far = 900;
            _cam.Pose(overhead, pos + Vector3.Forward * .01f, pos, dt);
            _cam.GlobalTransform = new Transform3D(Basis.LookingAt(Vector3.Down, Vector3.Forward), overhead);
            return true;
        }
        var forward = -jet.GlobalBasis.Z;
        forward.Y = 0;
        forward = forward.Normalized();
        var side = forward.Cross(Vector3.Up);
        var eye = pos - forward * 22 - side * 8 + Vector3.Up * 4;
        eye.Y = Math.Max(eye.Y, _track.MeshHeight(eye.X, eye.Z) + 4);
        var target = pos + forward * 5 + Vector3.Up;
        _cam.Fov = 58; _cam.Near = .2f; _cam.Far = 900;
        _cam.Pose(eye, target, pos, dt);
        return true;
    }
}
