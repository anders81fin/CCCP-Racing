using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace CCCPRacing.Race;

// Movie capture only: existing camera rigs share one uninterrupted tape/audio clock.
public partial class RaceScene
{
    string _tapeCameraSequencePath;
    TapeCameraSequenceFile _tapeCameraSequence;
    int _tapeSequenceLast = -1, _tapeSequencePoseTick = -1;
    bool? _tapeSequenceGoldenSaved;

    sealed class TapeCameraSequenceFile
    {
        [JsonPropertyName("shots")] public TapeCameraSequenceShot[] Shots { get; set; }
    }
    sealed class TapeCameraSequenceShot
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("start")] public float Start { get; set; }
        [JsonPropertyName("end")] public float End { get; set; }
        [JsonPropertyName("camera")] public string Camera { get; set; }
        [JsonPropertyName("eye")] public float[] Eye { get; set; }
        [JsonPropertyName("eye_end")] public float[] EyeEnd { get; set; }
        [JsonPropertyName("look")] public float[] Look { get; set; }
        [JsonPropertyName("fov")] public float? Fov { get; set; }
        [JsonPropertyName("golden")] public bool? Golden { get; set; }
        [JsonPropertyName("jet_instance")] public int? JetInstance { get; set; }
        [JsonPropertyName("missile_instance")] public int? MissileInstance { get; set; }
        [JsonPropertyName("hind_instance")] public int? HindInstance { get; set; }
        [JsonPropertyName("hind_target_instance")] public int? HindTargetInstance { get; set; }
        [JsonPropertyName("collision_time")] public float? CollisionTime { get; set; }
        [JsonPropertyName("detach_time")] public float? DetachTime { get; set; }
        [JsonPropertyName("collision_point")] public float[] CollisionPoint { get; set; }
        [JsonPropertyName("opening_eye_offset")] public float[] OpeningEyeOffset { get; set; }
        [JsonPropertyName("collision_eye_offset")] public float[] CollisionEyeOffset { get; set; }
        [JsonPropertyName("pullout_eye_offset")] public float[] PulloutEyeOffset { get; set; }
        [JsonPropertyName("hind_reveal_seconds")] public float? HindRevealSeconds { get; set; }
    }
    static Vector3? SequenceEye(float[] v) => v == null ? null : new Vector3(v[0], v[1], v[2]);

    bool TapeSequenceCamera(float dt)
    {
        if (_tapeCameraSequence == null)
        {
            try
            {
                var sequence = JsonSerializer.Deserialize<TapeCameraSequenceFile>(File.ReadAllText(_tapeCameraSequencePath));
                if (sequence?.Shots == null || sequence.Shots.Length is < 1 or > 32) throw new InvalidDataException("empty/oversized camera sequence");
                float previous = sequence.Shots[0].Start;
                if (Math.Abs(previous - _tapeReplayStart) > 1f / 30 + .005f) throw new InvalidDataException("sequence must start at the captured span");
                foreach (var shot in sequence.Shots)
                {
                    if (!float.IsFinite(shot.Start) || !float.IsFinite(shot.End) || shot.Start < 0
                        || shot.End <= shot.Start || shot.End > TitleGhost.Duration + .001f || Math.Abs(shot.Start - previous) > .002f)
                        throw new InvalidDataException("camera shots must be contiguous and inside the tape");
                    if (shot.Camera is not ("trailer-bonnet" or "trailer-fender" or "trailer-jet-wing" or "trailer-buk-wing" or "trailer-hind-mounted" or "trailer-hind-collision" or "trailer-mounted-fixed" or "trailer-front" or "trailer-rear" or "trailer-side" or "trailer-world" or "trailer-crane" or "trailer-overhead" or "ilyich-sweep" or "trailer-slurry-cab" or "trailer-slurry-pass" or "trailer-kozlik-track" or "default-top-down" or "trailer-tractor-pass" or "trailer-kozlik-pass" or "trailer-water" or "trailer-bank" or "trailer-fixed"))
                        throw new InvalidDataException("unsupported camera rig");
                    foreach (var eye in new[] { shot.Eye, shot.EyeEnd, shot.Look })
                        if (eye != null && (eye.Length != 3 || eye.Any(v => !float.IsFinite(v)))) throw new InvalidDataException("invalid camera position");
                    foreach (var eyeOffset in new[] { shot.OpeningEyeOffset, shot.CollisionEyeOffset, shot.PulloutEyeOffset })
                        if (eyeOffset != null && (eyeOffset.Length != 3 || eyeOffset.Any(v => !float.IsFinite(v)))) throw new InvalidDataException("invalid Hind collision camera offset");
                    if (shot.Fov is float fov && (!float.IsFinite(fov) || fov < 20 || fov > 90)) throw new InvalidDataException("invalid lens angle");
                    if (shot.Camera == "trailer-buk-wing" && shot.MissileInstance is not > 0) throw new InvalidDataException("BUK missile wing camera requires a recorded missile_instance");
                    if (shot.Camera == "trailer-hind-mounted" && (shot.HindInstance is not > 0 || shot.HindTargetInstance is not > 0 || shot.HindInstance == shot.HindTargetInstance))
                        throw new InvalidDataException("Hind mounted camera requires distinct recorded hind_instance and hind_target_instance IDs");
                    if (shot.Camera == "trailer-hind-collision" && (shot.HindInstance is not > 0 || shot.HindTargetInstance is not > 0 || shot.HindInstance == shot.HindTargetInstance
                        || shot.CollisionTime is not float ct || !float.IsFinite(ct) || ct <= shot.Start || ct >= shot.End
                        || shot.DetachTime is not float dt0 || !float.IsFinite(dt0) || dt0 < ct || dt0 >= shot.End
                        || shot.CollisionPoint == null || shot.CollisionPoint.Length != 3 || shot.CollisionPoint.Any(v => !float.IsFinite(v))))
                        throw new InvalidDataException("Hind collision camera requires distinct taped Hind IDs, collision/detach times, and a finite recorded collision point");
                    if (shot.HindRevealSeconds is float revealSeconds
                        && (!float.IsFinite(revealSeconds) || revealSeconds < .2f || revealSeconds > 5f))
                        throw new InvalidDataException("hind_reveal_seconds must be between 0.2 and 5 seconds");
                    if (shot.Camera == "trailer-mounted-fixed" && shot.Eye == null) throw new InvalidDataException("fixed mounted camera requires an explicit eye");
                    if (shot.Camera == "trailer-mounted-fixed" && shot.EyeEnd != null
                        && shot.Eye.Zip(shot.EyeEnd, (a, b) => Math.Abs(a - b)).Any(d => d > .0001f))
                        throw new InvalidDataException("fixed mounted camera cannot move its eye");
                    previous = shot.End;
                }
                _tapeCameraSequence = sequence;
            }
            catch (Exception e) when (e is IOException or InvalidDataException or JsonException or ArgumentException or NotSupportedException)
            { GD.PushError("Tape camera sequence: " + e.Message); GetTree().Quit(1); return true; }
        }
        int index = 0;
        while (index + 1 < _tapeCameraSequence.Shots.Length && _replayClock >= _tapeCameraSequence.Shots[index].End - .0001f) index++;
        var s = _tapeCameraSequence.Shots[index];
        if (_tapeSequenceLast != index)
        {
            _tapeSequenceLast = index;
            GD.Print($"tape camera sequence: {s.Id} {s.Camera} at {_replayClock:F3}");
            if (s.Eye is { Length: 3 } explicitEye)
            {
                float eyeGround = _track.MeshHeight(explicitEye[0], explicitEye[2]);
                Vector2 eyeXZ = new(explicitEye[0], explicitEye[2]);
                string nearest = string.Join(",", _track.Obstacles.Select((o, i) => (i, o.Center, o.Radius, gap: o.Center.DistanceTo(eyeXZ) - o.Radius))
                    .Where(o => o.Center.X > -1e3f).OrderBy(o => o.gap).Take(5)
                    .Select(o => FormattableString.Invariant($"#{o.i}@({o.Center.X:F1},{o.Center.Y:F1})r{o.Radius:F1}/gap{o.gap:F1}")));
                GD.Print(FormattableString.Invariant($"tape camera eye probe: {s.Id} {s.Camera} input=({explicitEye[0]:F2},{explicitEye[1]:F2},{explicitEye[2]:F2}) terrain={eyeGround:F2} water={_track.WaterLevel:F2} camera={_cam.GlobalPosition} nearest=[{nearest}]"));
            }
            if (s.Camera == "default-top-down") _cam.SnapDriving(_car.Pos, _car.Vel);
            if (s.Camera == "trailer-jet-wing")
            { _tapeWingJetId = s.JetInstance ?? -1; GD.Print($"tape wing camera: requested recorded jet {_tapeWingJetId} at {_replayClock:F3}"); }
            if (s.Camera == "trailer-buk-wing")
            { _tapeWingMissileId = s.MissileInstance ?? -1; GD.Print($"tape BUK wing camera: requested recorded missile {_tapeWingMissileId} at {_replayClock:F3}"); }
            if (s.Camera == "trailer-hind-mounted")
            { _tapeWingHindId = s.HindInstance ?? -1; _tapeHindTargetId = s.HindTargetInstance ?? -1; GD.Print($"tape Hind camera: mounted on recorded Hind {_tapeWingHindId}, aiming at recorded Hind {_tapeHindTargetId} at {_replayClock:F3}"); }
            if (s.Camera == "trailer-hind-collision")
            {
                _tapeWingHindId = s.HindInstance ?? -1; _tapeHindTargetId = s.HindTargetInstance ?? -1;
                _tapeHindCollisionTime = s.CollisionTime; _tapeHindDetachTime = s.DetachTime;
                _tapeHindCollisionPoint = SequenceEye(s.CollisionPoint);
                _tapeHindOpeningEyeOffset = SequenceEye(s.OpeningEyeOffset);
                _tapeHindCollisionEyeOffset = SequenceEye(s.CollisionEyeOffset);
                _tapeHindPulloutEyeOffset = SequenceEye(s.PulloutEyeOffset);
                _tapeHindRevealSeconds = s.HindRevealSeconds;
                GD.Print($"tape Hind collision camera: recorded Hinds {_tapeWingHindId}/{_tapeHindTargetId}, collision {_tapeHindCollisionTime:F3}, detach {_tapeHindDetachTime:F3}, at {_replayClock:F3}");
            }
        }
        var oldRig = _tapeShootTrailerDetail; var oldDetail = _tapeShootIlyichDetail;
        var oldEye = _tapeTrailerEye; var oldEnd = _tapeTrailerEyeEnd; var oldLook = _tapeTrailerLook;
        var oldCollisionTime = _tapeHindCollisionTime; var oldDetachTime = _tapeHindDetachTime; var oldCollisionPoint = _tapeHindCollisionPoint;
        var oldOpeningEyeOffset = _tapeHindOpeningEyeOffset; var oldCollisionEyeOffset = _tapeHindCollisionEyeOffset; var oldPulloutEyeOffset = _tapeHindPulloutEyeOffset;
        var oldHindRevealSeconds = _tapeHindRevealSeconds;
        try
        {
            _tapeTrailerEye = SequenceEye(s.Eye); _tapeTrailerEyeEnd = SequenceEye(s.EyeEnd); _tapeTrailerLook = SequenceEye(s.Look);
            bool posed;
            if (s.Camera == "default-top-down")
            {
                _cam.Fov = _replayOldFov; _cam.Near = _replayOldNear; _cam.Far = _replayOldFar;
                CameraFeel(dt, replayDriving: true);
                _cam.Follow(_car.Pos, _car.Vel, dt);
                posed = true;
            }
            else if (s.Camera == "ilyich-sweep")
            { _tapeShootIlyichDetail = s.Camera; posed = IlyichTapeDetailCamera(dt); }
            else
            { _tapeShootTrailerDetail = s.Camera; posed = TrailerTapeDetailCamera(dt, s.Start, s.End - s.Start); }
            if (s.Fov is float fov) _cam.Fov = fov;
            // Explicit legacy presentation restoration, never inferred from speed.
            if (s.Golden is bool golden)
            {
                _tapeSequenceGoldenSaved ??= _carVis.Golden;
                _carVis.Golden = golden;
            }
            int poseTick = (int)(_replayClock * 2);
            if (CmdArgs().Contains("--tape-camera-frame-log") && poseTick != _tapeSequencePoseTick)
            {
                _tapeSequencePoseTick = poseTick;
                GD.Print($"camera frame {s.Id} t={_replayClock:F3} car={_car.Pos} eye={_cam.GlobalPosition} screen={_cam.UnprojectPosition(_car.Pos)} behind={_cam.IsPositionBehind(_car.Pos)} fov={_cam.Fov}");
            }
            return posed;
        }
        finally
        {
            _tapeShootTrailerDetail = oldRig; _tapeShootIlyichDetail = oldDetail;
            _tapeTrailerEye = oldEye; _tapeTrailerEyeEnd = oldEnd; _tapeTrailerLook = oldLook;
            _tapeHindCollisionTime = oldCollisionTime; _tapeHindDetachTime = oldDetachTime; _tapeHindCollisionPoint = oldCollisionPoint;
            _tapeHindOpeningEyeOffset = oldOpeningEyeOffset; _tapeHindCollisionEyeOffset = oldCollisionEyeOffset; _tapeHindPulloutEyeOffset = oldPulloutEyeOffset;
            _tapeHindRevealSeconds = oldHindRevealSeconds;
        }
    }
}
