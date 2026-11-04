using System;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.UI;

public partial class Hud
{
    public NormPlan.Medal RunMedal { get; private set; }
    float _medalAge = 10;
    string _medalUnlock;
    Audio.MedalCheer _medalCheer;

    public void ResetMedalCelebration()
    {
        RunMedal = NormPlan.Medal.None;
        _medalAge = 10;
        _medalCheer?.Stop();
        ResetNormFeedback();
    }

    public void CelebrateMedal(NormPlan.Medal medal, string unlock)
    {
        if (medal <= RunMedal) return;
        ResetNormFeedback();
        RunMedal = medal;
        _medalUnlock = unlock;
        _medalAge = 0;
        _medalCheer?.Play();
    }

    void TickMedal(float dt)
    {
        if (!Frozen) _medalAge += dt;
    }

    float _medalCornerY = -1;

    /// <summary>
    /// Where the earned medal's star rests, top right: under the Socialist Rally's NORMS card when it is up (its actual bottom, with a gap for the star and its landing thump; the card's height follows the progression row and the end
    /// state, and the star glides with it), else where it always was.
    /// </summary>
    /// <summary>The star's resting y on a screen of this size: under the card with 6 px of gap and room for the star's landing thump (radius 7.5, up to 1.6 times), or the old place.</summary>
    internal float MedalCornerTarget(Vector2 screen) => NormCardShown ? NormCardRect(screen).End.Y + 6 + 9 : 33;

    internal float MedalCornerY()
    {
        float target = MedalCornerTarget(Screen);
        if (_medalCornerY < 0 || !NormCardShown) _medalCornerY = target;
        else _medalCornerY = Mathf.Lerp(_medalCornerY, target, 0.25f);
        return _medalCornerY;
    }

    void DrawMedalCelebration()
    {
        if (RunMedal == NormPlan.Medal.None || Clean || TrackPreview || School) return;
        var colour = RunMedal switch { NormPlan.Medal.Bronze => Bronze, NormPlan.Medal.Silver => Silver, _ => Gold };
        Vector2 corner = new(Screen.X - 17, MedalCornerY()), stamp = new(Screen.X - 82, Screen.Y * 0.23f);
        float fly = Mathf.SmoothStep(2.6f, 3.5f, _medalAge);
        Vector2 at = stamp.Lerp(corner, fly);
        float thump = 1 + 0.6f * MathF.Exp(-_medalAge * 12) * MathF.Cos(_medalAge * 22);
        float radius = Mathf.Lerp(23, 7.5f, fly) * thump;
        if (_medalAge < 0.7f)
        {
            float flash = MathF.Exp(-_medalAge * 6);
            for (int i = 0; i < 12; i++)
            {
                float angle = i * MathF.Tau / 12;
                DrawColoredPolygon(new[] { at + Vector2.FromAngle(angle - 0.025f) * radius * 1.3f,
                    at + Vector2.FromAngle(angle) * radius * (1.7f + _medalAge * 2),
                    at + Vector2.FromAngle(angle + 0.025f) * radius * 1.3f }, new Color(colour, flash * 0.7f));
            }
        }
        DrawMedalConfetti(colour);
        DrawMedalSparks(at, radius);
        DrawMedalWreath(at, radius, colour);
        if (_medalAge < 3.0f)
        {
            float alpha = 1 - Mathf.SmoothStep(2.6f, 3.0f, _medalAge);
            float y = stamp.Y + 32;
            PixelFont.DrawCentered(this, Campaign.Name(RunMedal) + " STAR - URAA!!!", stamp.X, y,
                new Color(colour, alpha), 1, new Color(0.12f, 0.04f, 0.04f, alpha));
            float row = y + 13;
            void Lines(string text, Color tint)
            {
                if (string.IsNullOrEmpty(text)) return;
                string line = "";
                foreach (string word in text.Split(' '))
                {
                    string next = line.Length == 0 ? word : line + " " + word;
                    if (line.Length > 0 && PixelFont.Measure(next) > 150)
                    {
                        PixelFont.DrawCentered(this, line, stamp.X, row, new Color(tint, alpha));
                        row += 10; line = word;
                    }
                    else line = next;
                }
                PixelFont.DrawCentered(this, line, stamp.X, row, new Color(tint, alpha));
                row += 10;
            }
            Lines(_medalUnlock, colour);
        }
    }

    // Analytic particles use the award clock: pausing freezes them, and a new award
    // starts a fresh burst without accumulating nodes or affecting world physics.
    float MedalRandom(int index, int salt) => Track.Noise.Hash01(index, salt, 772 + (int)RunMedal * 31);

    void DrawMedalConfetti(Color medalColour)
    {
        if (_medalAge >= 3f) return;
        float fade = 1 - Mathf.SmoothStep(2.25f, 3f, _medalAge);
        int count = RunMedal switch { NormPlan.Medal.Bronze => 34, NormPlan.Medal.Silver => 58, _ => 88 };
        for (int i = 0; i < count; i++)
        {
            float t = _medalAge - MedalRandom(i, 1) * 0.24f;
            if (t < 0) continue;
            bool left = (i & 1) == 0;
            float side = left ? 1 : -1;
            // Most paper stays in the outer quarter; only a few pieces reach the inner verge.
            float reach = Screen.X * (i % 13 == 0 ? 0.36f : 0.05f + MedalRandom(i, 2) * 0.21f);
            float inward = reach * (1 - MathF.Exp(-t * 2.8f));
            float x = (left ? 0 : Screen.X) + side * inward
                + MathF.Sin(t * 4 + i) * (1 + MedalRandom(i, 3) * 3);
            float y = -7 + (12 + MedalRandom(i, 4) * 37) * t + 24 * t * t;
            if (y > Screen.Y + 8) continue;
            float angle = i + t * (3 + MedalRandom(i, 5) * 7);
            Vector2 axis = Vector2.FromAngle(angle), normal = new(-axis.Y, axis.X);
            float length = 1.3f + MedalRandom(i, 6) * 2.1f;
            float width = 0.45f + MathF.Abs(MathF.Sin(t * 9 + i)) * 0.7f;
            Vector2 at = new(x, y);
            Color colour = i % 3 == 0 ? Red : i % 3 == 1 ? medalColour : medalColour.Lightened(0.25f);
            DrawColoredPolygon(new[] { at - axis * length - normal * width, at + axis * length - normal * width,
                at + axis * length + normal * width, at - axis * length + normal * width }, new Color(colour, fade * 0.9f));
        }

        int ribbonsPerSide = (int)RunMedal + 1;
        for (int i = 0; i < ribbonsPerSide * 2; i++)
        {
            bool left = (i & 1) == 0;
            float t = _medalAge - 0.05f - MedalRandom(i, 11) * 0.16f;
            if (t <= 0) continue;
            float side = left ? 1 : -1;
            float x = (left ? 0 : Screen.X) + side * Screen.X * (0.035f + MedalRandom(i, 12) * 0.18f)
                * (1 - MathF.Exp(-t * 3));
            float head = -10 + (18 + MedalRandom(i, 13) * 15) * t + 27 * t * t;
            float length = (18 + MedalRandom(i, 14) * 18) * Math.Min(1, t * 5);
            var points = new Vector2[15];
            for (int k = 0; k < points.Length; k++)
            {
                float u = k / (float)(points.Length - 1);
                float coil = MathF.Sin(u * MathF.Tau * 1.8f - t * 7 + i);
                points[k] = new Vector2(x + coil * (2.5f + u * 2.5f), head - u * length);
            }
            Color colour = i % 3 == 0 ? Red : medalColour;
            DrawPolyline(points, new Color(0.15f, 0.05f, 0.04f, fade * 0.3f), 2.3f);
            DrawPolyline(points, new Color(colour, fade * 0.9f), 1.2f);
        }
    }

    void DrawMedalSparks(Vector2 centre, float radius)
    {
        if (_medalAge >= 0.8f) return;
        int count = 6 + (int)RunMedal * 5;
        float t = _medalAge, fade = 1 - Mathf.SmoothStep(0.25f, 0.8f, t);
        for (int i = 0; i < count; i++)
        {
            float angle = i * MathF.Tau / count + MedalRandom(i, 21) * 0.3f;
            Vector2 velocity = Vector2.FromAngle(angle) * (25 + MedalRandom(i, 22) * 38);
            Vector2 at = centre + Vector2.FromAngle(angle) * radius * 0.8f + velocity * t + Vector2.Down * (t * t * 24);
            Vector2 tail = at - velocity.Normalized() * (1.5f + t * 3);
            DrawLine(tail, at, new Color(Gold, fade * 0.8f), 1.2f);
            DrawRect(new Rect2(at - Vector2.One * 0.7f, Vector2.One * 1.4f), new Color(Paper, fade));
        }
    }

    void DrawMedalWreath(Vector2 centre, float radius, Color colour)
    {
        var dark = new Color(0.16f, 0.07f, 0.06f);
        // Symmetrical laurel branches; distinct leaves instead of a solid circular badge.
        foreach (float side in new[] { -1f, 1f })
        {
            Vector2 Point(float t) => centre + new Vector2(side * radius * (0.3f + MathF.Sin(t * 2.4f) * 0.95f), radius * (1.1f - t * 1.8f));
            for (int i = 0; i < 8; i++)
            {
                float t = i / 8f;
                Vector2 root = Point(t), next = Point((i + 1) / 8f);
                DrawLine(root, next, dark, Math.Max(1.5f, radius * 0.09f));
                DrawLine(root, next, colour.Darkened(0.18f), Math.Max(0.8f, radius * 0.04f));
                Vector2 tip = root + new Vector2(side * radius * 0.27f, -radius * 0.30f);
                Vector2 across = new(-side * radius * 0.10f, -radius * 0.10f);
                Vector2 mid = root.Lerp(tip, 0.5f);
                var leaf = new[] { root, mid + across, tip, mid - across };
                DrawColoredPolygon(leaf, i % 2 == 0 ? colour : colour.Darkened(0.16f));
            }
        }
        StarShape(centre + new Vector2(0, radius * 0.04f), radius * 1.06f, dark);
        StarShape(centre, radius, colour);
        // Raised facets give the Soviet star a stamped-metal look in all three medal colours.
        for (int i = 0; i < 5; i++)
        {
            float angle = -MathF.PI / 2 + i * MathF.Tau / 5;
            DrawColoredPolygon(new[] { centre, centre + Vector2.FromAngle(angle) * radius,
                centre + Vector2.FromAngle(angle + MathF.PI / 5) * radius * 0.4f }, colour.Lightened(0.23f));
        }
    }
}
