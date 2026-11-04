using System;
using CCCPRacing.Camera;
using CCCPRacing.Track;
using System.Linq;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void GameplayCameraTest()
    {
        bool visible = true, aheadVisible = true, smooth = true, terrainClear = true;
        foreach (float aspect in new[] { 16f / 9f, 4f / 3f, 9f / 16f })
        foreach (float speed in new[] { 0f, 5f, 30f, 110f })
        {
            var camera = new GameplayCinematicCamera();
            Vector3 car = Vector3.Zero, previousOffset = Vector3.Zero;
            for (int tick = 0; tick < 360; tick++)
            {
                // Drive, turn, then reverse with an unchanged car heading; no road data is supplied.
                float angle = tick < 120 ? 0 : tick < 240 ? (tick - 120) * 0.01f : 1.2f;
                Vector3 heading = new(MathF.Sin(angle), 0, -MathF.Cos(angle));
                Vector3 velocity = heading * (tick >= 240 ? -speed : speed);
                car += velocity / 60f;
                var frame = camera.Frame(car, velocity, heading, 1f / 60f, (_, _) => 0f);
                Vector3 offset = frame.eye - car;
                if (tick > 0) smooth &= offset.DistanceTo(previousOffset) < 1.2f;
                previousOffset = offset;
                Basis view = Basis.LookingAt(frame.target - frame.eye, Vector3.Up).Inverse();
                bool Fits(Vector3 point, float margin)
                {
                    Vector3 p = view * (point - frame.eye);
                    float tan = MathF.Tan(Mathf.DegToRad(30));
                    return p.Z < 0 && Math.Abs(p.X) / (-p.Z * tan * aspect) < margin
                        && Math.Abs(p.Y) / (-p.Z * tan) < margin;
                }
                visible &= Fits(car + Vector3.Up * 0.8f, 0.85f);
                // The first 12 metres of the actual path remain visible even while reversing.
                aheadVisible &= Fits(car + velocity.LimitLength(12f), 0.98f);
            }
            var paused = camera.Frame(car, Vector3.Zero, Vector3.Back, 0, (_, _) => 0f);
            var repeated = camera.Frame(car, Vector3.Zero, Vector3.Back, 0, (_, _) => 0f);
            smooth &= paused == repeated;
        }
        Check(visible, "gameplay camera: car stays framed through turns, reverse, 396 km/h and narrow windows");
        Check(aheadVisible, "gameplay camera: actual path ahead stays visible through forward/reverse transitions");
        Check(smooth, "gameplay camera: no cuts during turns/reverse; zero-delta framing is stationary");

        // Dense square loop: the bottom straight points +X; check centre, verge and off-road boundary.
        var road = new TrackData {
            Center = Enumerable.Range(0, 80).Select(i => i < 20 ? new Vector2(i, 0)
                : i < 40 ? new Vector2(20, i - 20) : i < 60 ? new Vector2(60 - i, 20)
                : new Vector2(0, 80 - i)).ToArray(),
            HalfWidth = Enumerable.Repeat(2f, 80).ToArray()
        };
        var roadDirection = GameplayCinematicCamera.RoadForward(road, new Vector3(10.5f, 0, 2f));
        Check(roadDirection == Vector3.Right
            && GameplayCinematicCamera.RoadForward(road, new Vector3(10.5f, 0, 2.01f)) == null,
            "gameplay camera: road alignment includes the verge boundary, releases outside it between samples");
        Check(GameplayCinematicCamera.RoadForward(road, new Vector3(0, 0, 0)).HasValue,
            "gameplay camera: road alignment wraps across the lap seam");
        var locked = new GameplayCinematicCamera();
        var baseline = locked.Frame(Vector3.Zero, Vector3.Right * 20, Vector3.Right, 1f / 60f, (_, _) => 0, roadDirection);
        bool steady = true;
        for (int tick = 0; tick < 360; tick++)
        {
            float angle = MathF.Sin(tick * 0.15f) * 1.1f;
            Vector3 heading = new(MathF.Cos(angle), 0, MathF.Sin(angle));
            Vector3 velocity = heading * (tick < 240 ? 20 : -20);
            var frame = locked.Frame(Vector3.Zero, velocity, heading, 1f / 60f, (_, _) => 0, roadDirection);
            steady &= frame.eye.DistanceTo(baseline.eye) < 0.001f && frame.target.DistanceTo(baseline.target) < 0.001f;
        }
        Check(steady, "gameplay camera: steering, slides and reversing do not swing the on-road eye or look target");
        for (int tick = 0; tick < 240; tick++)
            locked.Frame(Vector3.Zero, Vector3.Back * 20, Vector3.Back, 1f / 60f, (_, _) => 0);
        var offRoad = locked.Frame(Vector3.Zero, Vector3.Back * 20, Vector3.Back, 1f / 60f, (_, _) => 0);
        Check(offRoad.eye.Z < -20 && offRoad.target.Z > 6,
            "gameplay camera: after leaving the road, the chase follows the car's actual motion again");

        bool bonnetClear = true, bonnetFacesForward = true;
        foreach (float yaw in new[] { 0f, 1.5f, -2f })
        foreach (float pitch in new[] { -0.2f, 0f, 0.2f })
        {
            var body = Basis.FromEuler(new Vector3(pitch, yaw, 0.08f));
            var position = new Vector3(42, 5, 80);
            var bonnet = GameplayBonnetCamera.Frame(position, body);
            var localEye = body.Inverse() * (bonnet.eye - position);
            bonnetClear &= localEye.Z < -1.946f && localEye.Z > -2.03f && localEye.Y > 0.30f;
            bonnetFacesForward &= (bonnet.target - bonnet.eye).Normalized().Dot(-body.Z) > 0.99f;
        }
        Check(bonnetClear && bonnetFacesForward,
            "bonnet camera: sits at the bonnet's front edge (no bonnet in frame) and faces forward through yaw/pitch/roll");

        var hills = new GameplayCinematicCamera();
        float Ground(float x, float z) => z > 5 && z < 18 ? 13f : 0f;
        var hill = hills.Frame(Vector3.Zero, Vector3.Forward * 20f, Vector3.Forward, 1f / 60f, Ground);
        Vector3 anchor = Vector3.Up;
        for (int i = 2; i <= 16; i++)
        {
            Vector3 sample = anchor.Lerp(hill.eye, i / 16f);
            terrainClear &= sample.Y >= Ground(sample.X, sample.Z) + 1.499f;
        }
        Check(terrainClear, "gameplay camera: eye and sightline rise clear of intervening terrain");
        var teleported = hills.Frame(new(1000, 0, 1000), Vector3.Back * 20f, Vector3.Back, 1f / 60f, (_, _) => 0f);
        var fresh = new GameplayCinematicCamera().Frame(new(1000, 0, 1000), Vector3.Back * 20f, Vector3.Back, 1f / 60f, (_, _) => 0f);
        Check(teleported == fresh, "gameplay camera: rescue/teleport discards the old dolly state");
    }
}
