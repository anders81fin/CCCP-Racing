using System;
using Godot;

namespace CCCPRacing.UI;

public partial class Hud
{
    AudioStreamPlayer _goldArrival, _goldRelease;

    void GoldRewardFanfare(bool release)
    {
        if (_goldArrival == null)
        {
            AddChild(_goldArrival = new AudioStreamPlayer { Stream = MakeGoldFanfare(false), VolumeDb = -9 });
            AddChild(_goldRelease = new AudioStreamPlayer { Stream = MakeGoldFanfare(true), VolumeDb = -8 });
        }
        _goldArrival.Stop();
        _goldRelease.Stop();
        (release ? _goldRelease : _goldArrival).Play();
    }

    // Original brass fanfare: an ascending three-note call, then a sustained major chord on opening.
    static AudioStreamWav MakeGoldFanfare(bool release)
    {
        const int rate = 22050;
        float duration = release ? 1.8f : 1.4f;
        var data = new byte[(int)(rate * duration) * 2];
        float[] pitches = release ? new[] { 261.63f, 329.63f, 392f, 523.25f }
            : new[] { 261.63f, 329.63f, 392f };
        for (int i = 0; i < data.Length / 2; i++)
        {
            float t = (float)i / rate, sample = 0;
            for (int n = 0; n < pitches.Length; n++)
            {
                float local = t - (release ? n * 0.025f : n * 0.28f);
                float length = release ? 1.5f : n == 2 ? 0.65f : 0.25f;
                if (local < 0 || local >= length) continue;
                float envelope = Math.Min(1, local / 0.025f) * Math.Clamp((length - local) / 0.22f, 0, 1);
                float vibrato = 0.003f * MathF.Sin(local * MathF.Tau * 5);
                for (int h = 1; h <= 7; h++)
                    sample += MathF.Sin(MathF.Tau * pitches[n] * local * h + vibrato * h)
                        / MathF.Pow(h, 1.25f) * envelope * (release ? 0.13f : 0.32f);
            }
            if (release)
                sample += MathF.Sin(MathF.Tau * 92 * t) * MathF.Exp(-t * 10) * Math.Min(1, t * 1000) * 0.35f;
            short value = (short)(Math.Clamp(sample, -0.95f, 0.95f) * short.MaxValue);
            data[2 * i] = (byte)(value & 255);
            data[2 * i + 1] = (byte)((value >> 8) & 255);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Data = data };
    }

    void DrawGoldTrackReward()
    {
        float age = LapsRewardAge, cx = Screen.X / 2;
        float reveal = Math.Max(0, age - 3.6f);
        bool opened = age >= 3.6f;
        var panel = new Rect2(8, Math.Max(5, Screen.Y * 0.12f), Screen.X - 16, Screen.Y * 0.77f);
        DrawRect(new Rect2(Vector2.Zero, Screen), new Color(0.02f, 0.01f, 0, 0.65f));
        Panel(panel, 0.98f);
        DrawRect(panel, Gold, false, 2);
        DrawRect(new Rect2(panel.Position + Vector2.One * 5, panel.Size - Vector2.One * 10), new Color(Gold, 0.4f), false, 1);
        float top = panel.Position.Y;
        PixelFont.DrawCentered(this, "ORDER OF THE GOLD STAR", cx, top + 13, Gold,
            PixelFont.Measure("ORDER OF THE GOLD STAR", 2) < panel.Size.X - 20 ? 2 : 1, HeadingShadow);
        Vector2 centre = new(cx, top + panel.Size.Y * 0.36f);
        float radius = Math.Min(30, panel.Size.Y * 0.15f);
        float arrival = 1 + 0.5f * MathF.Exp(-age * 5) * MathF.Cos(age * 13);
        for (int i = 0; i < 24; i++)
        {
            float angle = i * MathF.Tau / 24 + age * 0.08f;
            float length = radius * (i % 2 == 0 ? 2.1f : 1.7f);
            DrawColoredPolygon(new[] { centre + Vector2.FromAngle(angle - 0.025f) * radius * 1.2f,
                centre + Vector2.FromAngle(angle) * length,
                centre + Vector2.FromAngle(angle + 0.025f) * radius * 1.2f }, new Color(Gold, 0.35f));
        }
        DrawArc(centre, radius * 1.4f, 0, MathF.Tau, 64, new Color(Gold, 0.65f), 1);
        DrawMedalWreath(centre, radius * arrival, Gold);
        PixelFont.DrawCentered(this, LapsRewardSource + " / SOCIALIST RALLY / GOLD", cx,
            top + panel.Size.Y * 0.61f, Gold);
        float textY = top + panel.Size.Y * 0.70f;
        if (!opened)
            DrawTypewriter("BONUS TRACK AUTHORIZED", cx, textY, Gold, age - 0.9f, RewardTypewriterRate, 1, HeadingShadow);
        else PixelFont.DrawCentered(this, "BONUS TRACK UNLOCKED!", cx, textY, Gold, 1, HeadingShadow);
        PixelFont.DrawCentered(this, "POLIGON 17", cx, top + panel.Size.Y * 0.79f, opened ? Gold : Grey, 2, HeadingShadow);
        PixelFont.DrawCentered(this, _lapsRewardRally ? "SOCIALIST RALLY" : "STAKHANOV LAPS", cx,
            panel.End.Y - 15, Paper);
        // Two opposing gold showers travel along the panel edges; the text stays unobscured.
        if (opened)
        {
            for (int i = 0; i < 64; i++)
            {
                float hash = Track.Noise.Hash01(i, 1, 1980);
                float t = reveal - hash * 0.2f;
                if (t < 0) continue;
                bool left = (i & 1) == 0;
                float x = (left ? panel.Position.X + 12 : panel.End.X - 12)
                    + (left ? 1 : -1) * (8 + hash * 26) * t;
                float y = panel.End.Y - 20 - (50 + hash * 70) * t + 40 * t * t;
                float fade = 1 - Mathf.SmoothStep(1.1f, 1.6f, t);
                Vector2 at = new(x, y), direction = Vector2.FromAngle(i + t * 5);
                DrawLine(at, at + direction * 4, new Color(i % 3 == 0 ? Paper : Gold, fade), 2);
            }
            float ring = radius * (1.5f + reveal * 3);
            DrawArc(centre, ring, 0, MathF.Tau, 80, new Color(Gold, Math.Max(0, 1 - reveal * 2)), 2);
        }
    }
}
