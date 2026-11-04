using System;
using Godot;

namespace CCCPRacing.UI;

public partial class Hud
{
    Audio.MedalCheer _normCheer;
    Audio.NormMissSound _normMissSound;
    float _normAge = 10f;
    public bool NormConfettiActive => _normAge < 1.8f;

    public void ReactToNorm(bool beaten)
    {
        ResetNormFeedback();
        _medalCheer?.Stop();
        if (beaten)
        {
            _normAge = 0;
            _normCheer?.Play();
        }
    }

    void ResetNormFeedback()
    {
        _normAge = 10f;
        _normCheer?.Stop();
        _normMissSound?.Stop();
        _normMissAge = 10;
        NormMissCard = _normMissForCard = false;
    }

    void TickNormFeedback(float dt)
    {
        if (!Frozen) { _normAge += dt; _normMissAge += dt; }
    }

    // A small paper burst at the lens edges; no stamp, text or obstruction over the car.
    void DrawNormConfetti()
    {
        if (!NormConfettiActive || Clean || TrackPreview || School) return;
        float fade = 1 - Mathf.SmoothStep(1.2f, 1.8f, _normAge);
        float Random(int i, int salt) => Track.Noise.Hash01(i, salt, 1957);
        for (int i = 0; i < 28; i++)
        {
            float t = _normAge - Random(i, 1) * 0.12f;
            if (t <= 0) continue;
            bool left = (i & 1) == 0;
            float reach = Screen.X * (0.04f + Random(i, 2) * 0.16f) * (1 - MathF.Exp(-t * 4));
            var at = new Vector2((left ? reach : Screen.X - reach) + MathF.Sin(t * 6 + i) * 2,
                -5 + (20 + Random(i, 3) * 24) * t + 30 * t * t);
            Vector2 axis = Vector2.FromAngle(i + t * (5 + Random(i, 4) * 5));
            Vector2 normal = new(-axis.Y, axis.X);
            float length = 1.2f + Random(i, 5) * 1.3f;
            float width = 0.4f + MathF.Abs(MathF.Sin(t * 10 + i)) * 0.5f;
            Color colour = i % 3 == 0 ? Red : i % 3 == 1 ? Gold : Paper;
            DrawColoredPolygon(new[] { at - axis * length - normal * width, at + axis * length - normal * width,
                at + axis * length + normal * width, at - axis * length + normal * width }, new Color(colour, fade));
        }
    }
}
