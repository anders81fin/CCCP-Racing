using System;
using CCCPRacing.Camera;
using CCCPRacing.Track;
using System.Linq;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>The CINEMATIC view is a close chase camera: behind and a little above the car, a soft lag in turns, a roll into a corner, a pitch for braking and a launch, a bounded shake.</summary>
    void GameplayRearCameraTest()
    {
        var cam = new GameplayRearCamera();
        Vector3 car = Vector3.Zero, vel = new(0, 0, 25f), heading = new(0, 0, 1);
        (Vector3 eye, Vector3 target, float roll) f = default;
        for (int i = 0; i < 240; i++) { car += vel / 60f; f = cam.Frame(car, vel, heading, 1f / 60f, (_, _) => 0f); }
        Vector3 back = f.eye - car;
        Check(back.Z < -6f && back.Z > -10f && back.Y > 2f && back.Y < 4f && Math.Abs(back.X) < 0.3f, $"rear camera: a close chase straight behind the car and a little above it ({back.Z:F1} m back, {back.Y:F1} m up)");
        Check(Math.Abs(f.roll) < 0.01f, "rear camera: no roll on a straight");

        // A sudden turn of the car (a slide: the heading swings 70 degrees, the speed keeps its direction a moment): the camera lags behind it.
        float yaw = 70f * MathF.PI / 180f;
        heading = new Vector3(MathF.Sin(yaw), 0, MathF.Cos(yaw));
        Vector3 firstEye = default; float maxRoll = 0, maxStep = 0; Vector3 last = f.eye;
        for (int i = 0; i < 12; i++)
        {
            car += vel / 60f; f = cam.Frame(car, vel, heading, 1f / 60f, (_, _) => 0f);
            if (i == 0) firstEye = f.eye - car;
            maxStep = Math.Max(maxStep, f.eye.DistanceTo(last)); last = f.eye;
        }
        Vector3 shown = (f.target - f.eye).Normalized();
        Check(Math.Abs(firstEye.X) < 0.5f && MathF.Atan2(shown.X, shown.Z) < yaw * 0.5f, "rear camera: after a sudden turn of the car the view is still mostly where it was (the delay)");
        // A slide: the heading turns 50 degrees while the car goes on in a straight line. The camera swings round with it, but late and slowly (a rubber band): a good part of the way after a tenth
        // of a second, nearly all of it after a second and a half, and at most a little past the nose (the eye lags in its spring).
        {
            var slide = new GameplayRearCamera(); Vector3 cp = Vector3.Zero, cv = new Vector3(0, 0, 25f);
            for (int i = 0; i < 120; i++) { cp += cv / 60f; slide.Frame(cp, cv, new Vector3(0, 0, 1), 1f / 60f, (_, _) => 0f); }
            float nose = 50f * MathF.PI / 180f; Vector3 sh = new(MathF.Sin(nose), 0, MathF.Cos(nose));
            float ViewYaw((Vector3 eye, Vector3 target, float roll) g) => MathF.Atan2(g.target.X - g.eye.X, g.target.Z - g.eye.Z);
            float early = 0, mid = 0, late = 0;
            for (int i = 0; i < 240; i++)
            {
                cp += cv / 60f; var g = slide.Frame(cp, cv, sh, 1f / 60f, (_, _) => 0f);
                if (i == 5) early = ViewYaw(g); if (i == 90) mid = ViewYaw(g); if (i == 239) late = ViewYaw(g);
            }
            Check(early < nose * 0.3f && mid > nose * 0.6f && late > nose * 0.8f && late <= nose * 1.15f, $"rear camera: a slide turns the camera round hard, like a rubber band that is a little late ({early * 57.3f:F0}, {mid * 57.3f:F0}, {late * 57.3f:F0} degrees of {nose * 57.3f:F0})");
        }
        // Left turn: the velocity swings left, so the car is pulled to its left and the picture leans left (+ roll).
        var turn = new GameplayRearCamera();
        car = Vector3.Zero; vel = new Vector3(0, 0, 25f);
        Vector3 head = new(0, 0, 1);
        for (int i = 0; i < 120; i++) { car += vel / 60f; turn.Frame(car, vel, head, 1f / 60f, (_, _) => 0f); }
        for (int i = 0; i < 90; i++)
        {
            float a = i * 0.012f;      // curving to the car's left (towards -x with +z ahead)
            vel = new Vector3(-MathF.Sin(a) * 25f, 0, MathF.Cos(a) * 25f); head = vel.Normalized();
            car += vel / 60f; var g = turn.Frame(car, vel, head, 1f / 60f, (_, _) => 0f);
            maxRoll = Math.Max(maxRoll, Math.Abs(g.roll));
        }
        Check(maxRoll > 0.01f && maxRoll <= 0.0901f, $"rear camera: a corner rolls the picture, within bounds ({maxRoll * 57.3f:F1} degrees)");
        // Braking dips and launching lifts the look point.
        var brake = new GameplayRearCamera(); car = Vector3.Zero; vel = new Vector3(0, 0, 30f);
        for (int i = 0; i < 90; i++) { car += vel / 60f; brake.Frame(car, vel, new Vector3(0, 0, 1), 1f / 60f, (_, _) => 0f); }
        float steady = brake.Frame(car, vel, new Vector3(0, 0, 1), 1f / 60f, (_, _) => 0f).target.Y;
        for (int i = 0; i < 40; i++) { vel = new Vector3(0, 0, Math.Max(0f, 30f - i * 0.5f)); car += vel / 60f; brake.Frame(car, vel, new Vector3(0, 0, 1), 1f / 60f, (_, _) => 0f); }
        float braking = brake.Frame(car, vel, new Vector3(0, 0, 1), 1f / 60f, (_, _) => 0f).target.Y;
        Check(braking < steady - 0.01f, "rear camera: braking dips the look point");
        // The shake and the sway follow the speed: nearly nothing standing, the most at a high speed.
        float Jitter(float metresPerSecond)
        {
            var c = new GameplayRearCamera(); Vector3 pos = Vector3.Zero, v = new Vector3(0, 0, metresPerSecond);
            var ys = new System.Collections.Generic.List<float>(); var xs = new System.Collections.Generic.List<float>(); var aim = new System.Collections.Generic.List<float>();
            for (int i = 0; i < 360; i++)
            {
                pos += v / 60f;
                var g = c.Frame(pos, v, new Vector3(0, 0, 1), 1f / 60f, (_, _) => 0f);
                if (i >= 120) { ys.Add(g.eye.Y); xs.Add(g.eye.X); aim.Add(g.target.X - pos.X); }
            }
            float Dev(System.Collections.Generic.List<float> l) { float m = l.Average(); return MathF.Sqrt(l.Average(x => (x - m) * (x - m))); }
            return Dev(ys) + Dev(xs) + Dev(aim);
        }
        float stillJ = Jitter(0f), slowJ = Jitter(10f), fastJ = Jitter(45f);
        Check(stillJ < slowJ && slowJ < fastJ && fastJ > stillJ * 5f, $"rear camera: the shake and the sway grow with the speed (standing {stillJ:F4}, 10 m/s {slowJ:F4}, 45 m/s {fastJ:F4})");
        // Always finite, never under the ground, whatever the car does (spun on the spot, reversing, a jump of 100 m).
        bool sane = true;
        var wild = new GameplayRearCamera(); car = Vector3.Zero;
        for (int i = 0; i < 600; i++)
        {
            Vector3 v = i < 200 ? new Vector3(0, 0, -12) : i < 400 ? Vector3.Zero : new Vector3(40, 0, 0);
            if (i == 500) car += new Vector3(100, 0, 0);
            car += v / 60f;
            var w = wild.Frame(car, v, new Vector3(MathF.Sin(i * 0.1f), 0, MathF.Cos(i * 0.1f)), 1f / 60f, (x, z) => 1f, 0.05f);
            sane &= float.IsFinite(w.eye.X + w.eye.Y + w.eye.Z + w.target.X + w.target.Y + w.target.Z + w.roll) && w.eye.Y >= 1.8f && w.eye.DistanceTo(car) < 40f;
        }
        Check(sane, "rear camera: finite, over the ground and near the car through reversing, spinning, standing and a jump");
    }

    /// <summary>The BONNET view lives with the speed: its sway and shake grow from nothing standing to the most at a high speed, the lens stays on the bonnet, the roll leans into a corner.</summary>
    void GameplayBonnetFeelTest()
    {
        Basis body = Basis.Identity;
        float Wobble(float metresPerSecond)
        {
            var feel = new GameplayBonnetFeel(); Vector3 car = Vector3.Zero, v = new Vector3(0, 0, -metresPerSecond);
            var dx = new System.Collections.Generic.List<float>(); var dy = new System.Collections.Generic.List<float>();
            for (int i = 0; i < 360; i++)
            {
                car += v / 60f;
                var f = GameplayBonnetCamera.Frame(car, body);
                var r = feel.Apply(f.eye, f.target, body, v, 1f / 60f);
                if (i >= 120) { var d = (r.target - r.eye).Normalized(); dx.Add(d.X); dy.Add(d.Y); }
            }
            float Dev(System.Collections.Generic.List<float> l) { float m = l.Average(); return MathF.Sqrt(l.Average(x => (x - m) * (x - m))); }
            return Dev(dx) + Dev(dy);
        }
        float still = Wobble(0f), slow = Wobble(10f), fast = Wobble(45f);
        Check(still < slow && slow < fast && fast > still * 4f, $"bonnet camera: the sway and the shake grow with the speed (standing {still:F5}, 10 m/s {slow:F5}, 45 m/s {fast:F5})");
        // The lens stays within a few millimetres of the bonnet's edge, the roll bounded, the aim finite, a corner leans the picture to the left for a pull to the left.
        var turn = new GameplayBonnetFeel(); float maxMove = 0f, maxRoll = 0f; bool finite = true;
        Vector3 pos = Vector3.Zero; Vector3 vel = new Vector3(0, 0, -25f);
        for (int i = 0; i < 180; i++)
        {
            float a = i < 60 ? 0f : (i - 60) * 0.012f;      // curving to the left (towards -x with -z ahead)
            vel = new Vector3(-MathF.Sin(a) * 25f, 0, -MathF.Cos(a) * 25f);
            Basis b = Basis.FromEuler(new Vector3(0, a, 0));
            pos += vel / 60f;
            var f = GameplayBonnetCamera.Frame(pos, b);
            var r = turn.Apply(f.eye, f.target, b, vel, 1f / 60f);
            maxMove = Math.Max(maxMove, r.eye.DistanceTo(f.eye)); maxRoll = Math.Max(maxRoll, r.roll);
            finite &= float.IsFinite(r.eye.X + r.eye.Y + r.eye.Z + r.target.X + r.target.Y + r.target.Z + r.roll);
        }
        Check(finite && maxMove < 0.01f && maxRoll > 0.005f && maxRoll <= 0.0601f, $"bonnet camera: the lens stays on the bonnet (moved {maxMove * 1000f:F1} mm) and a corner leans the picture ({maxRoll * 57.3f:F1} degrees)");
    }

    void GameplayCameraTest()
    {
        GameplayRearCameraTest();
        GameplayBonnetFeelTest();
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
