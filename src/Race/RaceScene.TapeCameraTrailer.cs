using System;
using System.Globalization;
using System.Linq;
using Godot;
using CCCPRacing.Camera;

namespace CCCPRacing.Race;

// Capture-only rigs for the Atomgrad/Poligon films. Their poses are pure functions
// of the selected tape span; they never move the recorded actors or change time.
public partial class RaceScene
{
    string _tapeShootTrailerDetail;
    int _tapeWingJetId = -1;
    int _tapeWingMissileId = -1;
    int _tapeWingHindId = -1, _tapeHindTargetId = -1;
    float? _tapeHindCollisionTime, _tapeHindDetachTime;
    Vector3? _tapeHindCollisionPoint;
    Vector3? _tapeHindOpeningEyeOffset, _tapeHindCollisionEyeOffset, _tapeHindPulloutEyeOffset;
    float? _tapeHindRevealSeconds;
    Vector3? _tapeTrailerEye, _tapeTrailerEyeEnd, _tapeTrailerLook;

    static Vector3 TrailerTapeVector(string text)
    {
        string[] p = text.Split(',');
        if (p.Length != 3) throw new ArgumentException("Tape camera needs x,y,z");
        return new(float.Parse(p[0], CultureInfo.InvariantCulture),
            float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture));
    }

    // Capture drone inertia is reconstructed from the tape's recent history.
    // No frame accumulator: seek/repeated capture yields the same camera pose.
    Vector3 TapeDroneAnchor(float responseSeconds)
    {
        Vector3 anchor = Vector3.Zero;
        float total = 0;
        for (int i = 0; i <= 48; i++)
        {
            float age = i / 60f;
            float weight = MathF.Exp(-age / responseSeconds);
            anchor += TitleGhost.Sample(Math.Max(0, _replayClock - age)).Origin * weight;
            total += weight;
        }
        return anchor / total;
    }

    static bool IsRecordedHind(Node3D node) => node != null && node.HasMeta(WorldTapeConst.KindMeta)
        && node.GetMeta(WorldTapeConst.KindMeta).AsString().StartsWith("hind/", StringComparison.Ordinal);

    bool TrailerTapeDetailCamera(float dt, float? shotStart = null, float? shotDuration = null)
    {
        _cam.Far = 700;
        if (_tapeShootTrailerDetail == "trailer-fixed")
        {
            Vector3 fixedEye = _tapeTrailerEye ?? new Vector3(275, 5, 246);
            fixedEye.Y = Math.Max(fixedEye.Y, _track.MeshHeight(fixedEye.X, fixedEye.Z) + 1.5f);
            Vector3 fixedLook = _tapeTrailerLook ?? new Vector3(232, 2, 277);
            _cam.Fov = 64; _cam.Near = .08f;
            _cam.Pose(fixedEye, fixedLook, _car.Pos, dt);
            return true;
        }
        if (_tapeShootTrailerDetail is "trailer-water" or "trailer-bank")
        {
            Vector3 lens = _tapeTrailerEye ?? new Vector3(250, .18f, 269);
            // Both lenses stay at one world point. Y is height above the water
            // or bank, respectively; only the aim follows the taped car.
            lens.Y += _tapeShootTrailerDetail == "trailer-bank" ? _track.MeshHeight(lens.X, lens.Z) : _track.WaterLevel;
            _cam.Fov = 54; _cam.Near = .06f;
            _cam.Pose(lens, _car.Pos + Vector3.Up * .45f, _car.Pos, dt);
            return true;
        }
        if (_tapeShootTrailerDetail is "trailer-tractor-pass" or "trailer-kozlik-pass" or "trailer-slurry-pass")
        {
            bool kozlik = _tapeShootTrailerDetail == "trailer-kozlik-pass";
            bool slurry = _tapeShootTrailerDetail == "trailer-slurry-pass";
            Node3D subject = slurry ? _slurryAll?.Model : kozlik ? _kozlikAll?.Body : _tractorAll?.Model;
            string actor = slurry ? "Slurry:SlurryTractor" : kozlik ? "Kozlik:Kozlik" : "Tractor:DrunkTractor";
            if (subject == null || _tapePlayer?.Covers(actor) != true)
            {
                GD.PushError("Tape camera sequence: tractor pass requires a compatible recorded tractor");
                GetTree().Quit(1);
                return true;
            }
            Vector3 tractor = subject.GlobalPosition + Vector3.Up;
            Vector3 center = (_car.Pos + tractor) * .5f + Vector3.Up * .5f;
            // Follow the encounter midpoint with a fixed camera arm. Changing
            // arm length with vehicle separation creates an unrequested zoom.
            Vector3 offset = slurry ? new Vector3(10, 18, 8) : new Vector3(0, 18, 21);
            Vector3 passEye = center + offset;
            _cam.Fov = slurry ? 50 : 55; _cam.Near = .2f;
            _cam.Pose(passEye, center, _car.Pos, dt);
            return true;
        }
        if (_tapeShootTrailerDetail == "trailer-kozlik-track")
        {
            if (_kozlikAll?.Body == null || _tapePlayer?.Covers("Kozlik:Kozlik") != true)
            {
                GD.PushError("Tape camera sequence: Kozlik camera requires a compatible recorded Kozlik");
                GetTree().Quit(1);
                return true;
            }
            // Passenger-side windscreen mount clears the opaque glazing and bear.
            // Both the mount and subject use recorded transforms; no actor is moved.
            Vector3 lens = _kozlikAll.Body.GlobalTransform * (_tapeTrailerEye ?? new Vector3(1.05f, 2.2f, -.7f));
            Vector3 aim = _car.Pos + Vector3.Up * .5f;
            if (lens.DistanceSquaredTo(aim) < .01f) aim -= _car.Rot.Z;
            _cam.Fov = 70; _cam.Near = .06f;
            _cam.Pose(lens, aim, _car.Pos, dt);
            return true;
        }
        if (_tapeShootTrailerDetail == "trailer-slurry-cab")
        {
            if (_slurryAll?.Model == null || _tapePlayer?.Covers("Slurry:SlurryTractor") != true)
            {
                GD.PushError("Tape camera sequence: slurry cab requires a compatible recorded tractor");
                GetTree().Quit(1);
                return true;
            }
            // The model's transform is restored by the world tape. Never use the
            // live tractor simulation's Position2D or move/hide any actor parts.
            // For this rig eye/eye_end are tractor-local lens/look coordinates.
            var cab = _slurryAll.Model.GlobalTransform;
            Vector3 cabEye = cab * (_tapeTrailerEye ?? new Vector3(.30f, 2.80f, .65f));
            Vector3 cabLook = cab * (_tapeTrailerEyeEnd ?? new Vector3(0, 1.8f, -7));
            // The model's glazing is opaque scenery. The capture lens clips
            // the nearest pane while retaining the dashboard, hood and frame.
            _cam.Fov = 82; _cam.Near = .55f;
            _cam.Pose(cabEye, cabLook, _car.Pos, dt);
            _cam.GlobalTransform = new Transform3D(Basis.LookingAt(cabLook - cabEye, cab.Basis.Y), cabEye);
            return true;
        }
        if (_tapeShootTrailerDetail == "trailer-jet-wing")
        {
            Node3D jet = null;
            float nearest = float.PositiveInfinity;
            if (_tapeSink != null)
                foreach (var pair in _tapeSink.Live)
                {
                    var node = pair.Value;
                    if (!node.HasMeta(WorldTapeConst.KindMeta)
                        || node.GetMeta(WorldTapeConst.KindMeta).AsString() != "jet") continue;
                    if (_tapeWingJetId >= 0)
                    {
                        if (pair.Key == _tapeWingJetId) jet = node;
                        continue;
                    }
                    float distance = node.GlobalPosition.DistanceSquaredTo(_car.Pos);
                    if (distance < nearest) { nearest = distance; jet = node; }
                }
            if (jet == null)
            {
                GD.PushError("Tape camera sequence: wing camera requires its recorded MiG instance");
                GetTree().Quit(1);
                return true;
            }
            if (_tapeWingJetId < 0)
                foreach (var pair in _tapeSink.Live)
                    if (pair.Value == jet)
                    {
                        _tapeWingJetId = pair.Key;
                        GD.Print($"tape wing camera: recorded jet {_tapeWingJetId} at {_replayClock:F3}");
                        break;
                    }
            Vector3 local = _tapeTrailerEye ?? new Vector3(3.5f, 1.85f, 3.3f);
            Vector3 lens = jet.GlobalTransform * local;
            Vector3 aim = _car.Pos + Vector3.Up * .65f;
            _cam.Fov = 64; _cam.Near = .06f;
            _cam.Pose(lens, aim, _car.Pos, dt);
            return true;
        }
        if (_tapeShootTrailerDetail == "trailer-buk-wing")
        {
            // Capture-only BUK missile companion view. Resolve only the requested
            // tape instance; never substitute another missile or move/simulate it.
            Node3D missile = null;
            if (_tapeWingMissileId > 0 && _tapeSink?.Live.TryGetValue(_tapeWingMissileId, out var recorded) == true
                && recorded != null && recorded.HasMeta(WorldTapeConst.KindMeta)
                && recorded.GetMeta(WorldTapeConst.KindMeta).AsString() == "buk-missile") missile = recorded;
            if (missile == null)
            {
                string live = _tapeSink == null ? "<no sink>" : string.Join(",", _tapeSink.Live.OrderBy(p => p.Key).Select(p =>
                    $"{p.Key}:{(p.Value != null && p.Value.HasMeta(WorldTapeConst.KindMeta) ? p.Value.GetMeta(WorldTapeConst.KindMeta).AsString() : "<unmarked>")}"));
                string requestedKind = _tapeSink?.Live.TryGetValue(_tapeWingMissileId, out var requested) == true
                    ? (requested != null && requested.HasMeta(WorldTapeConst.KindMeta) ? requested.GetMeta(WorldTapeConst.KindMeta).AsString() : "<unmarked>")
                    : "<absent>";
                string tapeLive = _tapePlayer == null ? "<no player>" : string.Join(",", _tapePlayer.LiveInstances.OrderBy(p => p.Key).Select(p => $"{p.Key}@{p.Value:F3}"));
                GD.PushError($"Tape camera sequence: BUK wing camera requires recorded buk-missile instance {_tapeWingMissileId}; sink live [{live}], requested kind {requestedKind}, tape live [{tapeLive}]");
                GetTree().Quit(1);
                return true;
            }
            // Local X/Y place the camera clear of the missile body; local Z keeps
            // it beside the missile. The gaze tracks the recorded player car.
            Vector3 lens = missile.GlobalTransform * (_tapeTrailerEye ?? new Vector3(4f, 1.6f, 0.4f));
            Vector3 aim = _car.Pos + Vector3.Up * .65f;
            _cam.Fov = 64; _cam.Near = .06f;
            _cam.Pose(lens, aim, _car.Pos, dt);
            return true;
        }
        if (_tapeShootTrailerDetail == "trailer-hind-mounted")
        {
            // Capture-only viewpoint attached to one recorded Hind and aimed at
            // a second explicit Hind instance. No actor is moved or simulated.
            Node3D cameraHind = null, targetHind = null;
            if (_tapeSink != null && _tapeWingHindId > 0 && _tapeHindTargetId > 0
                && _tapeSink.Live.TryGetValue(_tapeWingHindId, out var mounted)
                && _tapeSink.Live.TryGetValue(_tapeHindTargetId, out var targetNode))
            {
                if (IsRecordedHind(mounted)) cameraHind = mounted;
                if (IsRecordedHind(targetNode)) targetHind = targetNode;
            }
            if (cameraHind == null || targetHind == null)
            {
                GD.PushError($"Tape camera sequence: mounted Hind view requires recorded Hind instances {_tapeWingHindId} and {_tapeHindTargetId}");
                GetTree().Quit(1);
                return true;
            }
            // This outboard, elevated local mount clears the 8.5 m rotor disk.
            // Aim at the second taped aircraft so their approach/contact stays
            // framed even as both recorded poses change or enter the fall state.
            Vector3 lens = cameraHind.GlobalTransform * (_tapeTrailerEye ?? new Vector3(10f, 9f, 0f));
            Vector3 aim = targetHind.GlobalPosition + Vector3.Up * 1.5f;
            _cam.Fov = 64; _cam.Near = .06f;
            _cam.Pose(lens, aim, _car.Pos, dt);
            return true;
        }
        if (_tapeShootTrailerDetail == "trailer-hind-collision")
        {
            Node3D mountedHind = null, targetHind = null;
            if (_tapeSink != null && _tapeWingHindId > 0 && _tapeHindTargetId > 0
                && _tapeSink.Live.TryGetValue(_tapeWingHindId, out var mounted)
                && _tapeSink.Live.TryGetValue(_tapeHindTargetId, out var targetNode))
            {
                if (IsRecordedHind(mounted)) mountedHind = mounted;
                if (IsRecordedHind(targetNode)) targetHind = targetNode;
            }
            if (mountedHind == null || targetHind == null || _tapeHindCollisionTime is not float impact
                || _tapeHindDetachTime is not float detach || _tapeHindCollisionPoint is not Vector3 collisionPoint)
            {
                GD.PushError($"Tape camera sequence: Hind collision flythrough requires taped Hinds {_tapeWingHindId}/{_tapeHindTargetId} and its recorded collision path");
                GetTree().Quit(1);
                return true;
            }

            // Match clip 13's current trailer-crane landscape pose exactly at
            // the first native frame, then fly toward the pair's taped midpoint.
            // The approach ends before contact; a short release blends from the
            // live midpoint to the recorded collision point. The later pullout
            // is driven by that collision point and the player car, not by a
            // tumbling Hind.
            float collisionStart = Math.Max(0, shotStart ?? _tapeReplayStart);
            Vector3 carStart = TitleGhost.Sample(collisionStart).Origin;
            Vector3 collisionHeading = TitleGhost.Sample(Math.Min(TitleGhost.Duration, collisionStart + .9f)).Origin - carStart;
            collisionHeading.Y = 0;
            collisionHeading = collisionHeading.LengthSquared() > .1f ? collisionHeading.Normalized() : -_car.Rot.Z;
            Vector3 collisionSide = collisionHeading.Cross(Vector3.Up);
            Vector3 collisionStartEye = carStart + collisionSide * 28 + collisionHeading * -5 + Vector3.Up * 15
                + (_tapeHindOpeningEyeOffset ?? Vector3.Zero);
            Vector3 collisionStartAim = carStart + Vector3.Up * .8f;
            // This is the same terrain-clearance pass as the existing crane rig.
            for (int i = 2; i <= 20; i++)
            {
                float along = i / 20f;
                Vector3 p = collisionStartAim.Lerp(collisionStartEye, along);
                collisionStartEye.Y += Math.Max(0, (_track.MeshHeight(p.X, p.Z) + 2 - p.Y) / along);
            }

            Vector3 pairCenter = (mountedHind.GlobalPosition + targetHind.GlobalPosition) * .5f;
            Vector3 collisionOffset = _tapeHindCollisionEyeOffset ?? new Vector3(18f, 14f, 24f);
            float approachEnd = Math.Max(collisionStart + .05f, Math.Min(impact - .15f, collisionStart + 2.7f));
            float approachU = Mathf.Clamp((_replayClock - collisionStart) / Math.Max(.01f, approachEnd - collisionStart), 0, 1);
            approachU = approachU * approachU * (3 - 2 * approachU);
            Vector3 collisionEye = collisionStartEye.Lerp(pairCenter + collisionOffset, approachU);
            Vector3 collisionAim = collisionStartAim.Lerp(pairCenter + Vector3.Up * 2f, approachU);

            if (_replayClock >= approachEnd)
            {
                collisionEye = pairCenter + collisionOffset;
                collisionAim = pairCenter + Vector3.Up * 2f;
            }
            if (_replayClock >= impact)
            {
                float releaseU = Mathf.Clamp((_replayClock - impact) / Math.Max(.01f, detach - impact), 0, 1);
                releaseU = releaseU * releaseU * (3 - 2 * releaseU);
                Vector3 releasedAnchor = pairCenter.Lerp(collisionPoint, releaseU);
                collisionEye = releasedAnchor + collisionOffset;
                collisionAim = releasedAnchor + Vector3.Up * 2f;

                if (_replayClock >= detach)
                {
                    float pullU = Mathf.Clamp((_replayClock - detach) / Math.Max(.01f, collisionStart + (shotDuration ?? 13f) - detach), 0, 1);
                    // Ease out quickly after detaching so the player car enters
                    // the wide composition soon after the collision.
                    pullU = pullU * (2 - pullU);
                    float lookU = pullU;
                    if (_tapeHindRevealSeconds is float revealSeconds)
                    {
                        lookU = Mathf.Clamp((_replayClock - detach) / Math.Max(.01f, revealSeconds), 0, 1);
                        lookU = lookU * (2 - lookU);
                    }
                    Vector3 composition = (collisionPoint + _car.Pos) * .5f;
                    Vector3 pullEye = composition + (_tapeHindPulloutEyeOffset ?? new Vector3(35f, 75f, 35f));
                    Vector3 pullAim = composition + Vector3.Up * 3f;
                    Vector3 detachedEye = collisionPoint + collisionOffset;
                    Vector3 detachedAim = collisionPoint + Vector3.Up * 2f;
                    collisionEye = detachedEye.Lerp(pullEye, pullU);
                    collisionAim = detachedAim.Lerp(pullAim, lookU);
                }
            }

            _cam.Fov = 60; _cam.Near = .2f; // same fixed lens as the original clip13 crane; never interpolate/zoom
            _cam.Pose(collisionEye, collisionAim, _car.Pos, dt);
            return true;
        }
        if (_tapeShootTrailerDetail == "trailer-fender")
        {
            // Rigid GoPro arm outside the front fender, ahead of the damaged
            // bonnet silhouette. The body pose comes directly from the tape.
            Vector3 lens = _car.Pos + _car.Rot * new Vector3(1.25f, .72f, -1.45f);
            Vector3 aim = lens - _car.Rot.Z * 40f - _car.Rot.Y * .5f;
            _cam.Fov = 75; _cam.Near = .06f;
            _cam.Pose(lens, aim, _car.Pos, dt);
            return true;
        }
        if (_tapeShootTrailerDetail == "trailer-bonnet")
        {
            var view = GameplayBonnetCamera.Frame(_car.Pos, _car.Rot);
            _cam.Fov = 75; _cam.Near = .06f;
            _cam.Pose(view.eye, view.target, _car.Pos, dt);
            return true;
        }
        float start = Math.Max(0, shotStart ?? _tapeReplayStart);
        float u = Mathf.Clamp((_replayClock - start) / Math.Max(1, shotDuration ?? _tapeReplayQuit), 0, 1);
        u = u * u * (3 - 2 * u);
        Vector3 origin = TitleGhost.Sample(start).Origin;
        Vector3 heading = TitleGhost.Sample(Math.Min(TitleGhost.Duration, start + .9f)).Origin - origin;
        heading.Y = 0;
        heading = heading.LengthSquared() > .1f ? heading.Normalized() : -_car.Rot.Z;
        Vector3 side = heading.Cross(Vector3.Up);
        Vector3 target = _car.Pos + Vector3.Up * .8f, eye;
        Vector3 drone = TapeDroneAnchor(.24f);
        _cam.Fov = 60; _cam.Near = .2f;
        if (_tapeShootTrailerDetail == "trailer-overhead")
        {
            eye = (_tapeTrailerEye ?? origin + Vector3.Up * 48);
            _cam.Fov = 57;
            _cam.Pose(eye, new Vector3(eye.X, eye.Y - 30, eye.Z - .01f), _car.Pos, dt);
            _cam.GlobalTransform = new Transform3D(Basis.LookingAt(Vector3.Down, Vector3.Forward), eye);
            return true;
        }
        if (_tapeShootTrailerDetail == "trailer-mounted-fixed")
        {
            if (_tapeTrailerEye is not Vector3 fixedEye)
            {
                GD.PushError("Tape camera sequence: fixed mounted camera requires an explicit eye");
                GetTree().Quit(1);
                return true;
            }
            // Fixed world-space security/landscape mount: never follow, lerp or
            // lift the eye over terrain. Caller composes a safe visible point.
            Vector3 fixedLook = _tapeTrailerLook ?? _car.Pos + Vector3.Up * .8f;
            _cam.Fov = 56; _cam.Near = .2f;
            _cam.Pose(fixedEye, fixedLook, _car.Pos, dt);
            return true;
        }
        if (_tapeShootTrailerDetail == "trailer-world")
        {
            eye = (_tapeTrailerEye ?? origin + new Vector3(24, 25, 28))
                .Lerp(_tapeTrailerEyeEnd ?? origin + new Vector3(10, 32, 22), u);
            target = _tapeTrailerLook ?? target;
            _cam.Fov = 56;
        }
        else if (_tapeShootTrailerDetail == "trailer-rear")
        {
            // Capture-only drone follows the recorded path, including slides.
            // Symmetric tape samples soften steering without seek-dependent state.
            Vector3 travel = TitleGhost.Sample(Math.Min(TitleGhost.Duration, _replayClock + .35f)).Origin
                - TitleGhost.Sample(Math.Max(0, _replayClock - .35f)).Origin;
            travel.Y = 0;
            travel = travel.LengthSquared() > .1f ? travel.Normalized() : heading;
            // Optional rear-rig offsets: side, height, and forward distance.
            Vector3 mount = _tapeTrailerEye ?? new Vector3(3, 10, -17);
            eye = drone + travel * mount.Z + travel.Cross(Vector3.Up) * mount.X + Vector3.Up * mount.Y;
            target = TapeDroneAnchor(.09f) + Vector3.Up * .8f + travel * 5;
            _cam.Fov = 58;
        }
        else if (_tapeShootTrailerDetail == "trailer-front")
        {
            eye = drone + Vector3.Up * .8f + heading * 11 + side * 3 + Vector3.Up * 3;
            target = TapeDroneAnchor(.09f) + Vector3.Up * .8f;
            _cam.Fov = 64;
        }
        else if (_tapeShootTrailerDetail == "trailer-side")
        {
            // Optional capture mount: lateral offset, height, forward distance.
            // Higher Siberia lenses clear the roadside conifers; the original
            // mount remains the default for existing camera sequences.
            Vector3 mount = _tapeTrailerEye ?? new Vector3(13, 10, -7);
            eye = drone + Vector3.Up * .8f + side * mount.X + heading * mount.Z + Vector3.Up * mount.Y;
            target = TapeDroneAnchor(.09f) + Vector3.Up * .8f + heading * 2;
            _cam.Fov = 62;
        }
        else
        {
            eye = origin + side * Mathf.Lerp(28, 20, u) + heading * Mathf.Lerp(-5, 15, u)
                + Vector3.Up * Mathf.Lerp(15, 34, u);
        }
        for (int i = 2; i <= 20; i++)
        {
            float along = i / 20f;
            Vector3 p = target.Lerp(eye, along);
            eye.Y += Math.Max(0, (_track.MeshHeight(p.X, p.Z) + 2 - p.Y) / along);
        }
        _cam.Pose(eye, target, _car.Pos, dt);
        return true;
    }
}
