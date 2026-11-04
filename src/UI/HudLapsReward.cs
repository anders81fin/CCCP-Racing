using System;
using Godot;
using CCCPRacing.Race;

namespace CCCPRacing.UI;

public partial class Hud
{
    // The ceremony clock runs at 2/3 speed; compensate so typing uses the normal real-time rate.
    const float RewardTypewriterRate = HeadingRate / (2f / 3f);
    public string LapsRewardSource;
    public float LapsRewardAge;
    NormPlan.Medal _lapsRewardMedal;
    bool _lapsRewardRally;
    AudioStreamPlayer _lapsRewardBell, _rallyRewardAccent;

    public void BeginLapsReward(NormPlan.Medal medal, bool rally)
    {
        _lapsRewardMedal = medal;
        _lapsRewardRally = rally;
        LapsRewardClang(false);
    }

    public void LapsRewardClang(bool release)
    {
        if (_lapsRewardMedal == NormPlan.Medal.Gold)
        {
            GoldRewardFanfare(release);
            return;
        }
        if (_lapsRewardBell == null)
        {
            const int rate = 22050;
            var data = new byte[rate * 2];
            for (int i = 0; i < rate; i++)
            {
                float t = (float)i / rate;
                float sample = (MathF.Sin(t * MathF.Tau * 1480) * MathF.Exp(-t * 6)
                    + 0.45f * MathF.Sin(t * MathF.Tau * 3920) * MathF.Exp(-t * 13)
                    + 0.2f * MathF.Sin(t * MathF.Tau * 6170) * MathF.Exp(-t * 25))
                    * Math.Min(1, t * 2000);
                short value = (short)(sample * 14000);
                data[i * 2] = (byte)(value & 255);
                data[i * 2 + 1] = (byte)((value >> 8) & 255);
            }
            AddChild(_lapsRewardBell = new AudioStreamPlayer
            {
                Stream = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Data = data },
                VolumeDb = -7,
            });
        }
        _lapsRewardBell.PitchScale = release ? 1.25f : 0.8f;
        _lapsRewardBell.Play();
        if (release && _lapsRewardRally)
        {
            if (_rallyRewardAccent == null)
                AddChild(_rallyRewardAccent = new AudioStreamPlayer { Stream = _lapsRewardBell.Stream, VolumeDb = -11 });
            _rallyRewardAccent.PitchScale = 1.875f;
            _rallyRewardAccent.Play();
        }
    }

    void DrawLapsReward(Rect2 card, float optionY, bool compact)
    {
        if (LapsRewardSource == null) return;
        if (_lapsRewardMedal == NormPlan.Medal.Gold)
        {
            DrawGoldTrackReward();
            return;
        }
        float age = LapsRewardAge, cx = Screen.X / 2;
        Color medal = _lapsRewardMedal == NormPlan.Medal.Gold ? Gold
            : _lapsRewardMedal == NormPlan.Medal.Silver ? Silver : Bronze;
        float radius = (_lapsRewardRally ? 21 : 17) * (1 + 0.65f * MathF.Exp(-age * 9) * MathF.Cos(age * 20));
        float starY = Math.Max(_lapsRewardRally ? 32 : 27, card.Position.Y - (_lapsRewardRally ? 44 : 38));
        if (_lapsRewardRally) DrawRallyRewardRays(new Vector2(cx, starY), age, radius);
        DrawMedalWreath(new Vector2(cx, starY), radius, medal);
        string earned = LapsRewardSource + " / SOCIALIST RALLY / " + Campaign.Name(_lapsRewardMedal);
        PixelFont.DrawCentered(this, earned, cx, starY + (_lapsRewardRally ? 28 : 23), medal);
        string text = _lapsRewardRally ? "SOCIALIST RALLY UNLOCKED" : "STAKHANOV LAPS AUTHORIZED";
        int scale = !compact && PixelFont.Measure(text, 2) < Screen.X - 24 ? 2 : 1;
        float y = card.Position.Y + optionY + (compact ? 4 : 3);
        // The authorization is stamped directly across the still grey track names.
        if (age < 2.5f)
        {
            DrawRect(new Rect2(4, y - 2, Screen.X - 8, 7 * scale + 4), new Color(0.08f, 0.07f, 0.06f, 0.9f));
            DrawTypewriter(text, cx, y, Gold, Math.Max(0, age - 0.5f), RewardTypewriterRate, scale, HeadingShadow);
        }
        else PixelFont.DrawCentered(this, _lapsRewardRally ? "RALLY UNLOCKED!" : "AUTHORIZED", cx, card.End.Y - 12, Gold);
        if (age >= 2.5f)
        {
            float flash = 1 - Mathf.Clamp((age - 2.5f) / 0.6f, 0, 1);
            float x = Mathf.Lerp(card.Position.X, card.End.X, (age - 2.5f) / 0.6f);
            DrawRect(new Rect2(x, card.Position.Y, 5, card.Size.Y), new Color(Gold, flash * 0.7f));
        }
    }

    void DrawRallyRewardRays(Vector2 centre, float age, float radius)
    {
        float pulse = 0.35f + 0.15f * MathF.Sin(age * 3);
        for (int i = 0; i < 12; i++)
        {
            float angle = i * MathF.Tau / 12 + age * 0.12f;
            Vector2 direction = Vector2.FromAngle(angle);
            DrawLine(centre + direction * radius * 1.35f,
                centre + direction * radius * 1.65f, new Color(Gold, pulse), 1.5f);
        }
        if (age < 2.5f) return;
        float burst = age - 2.5f;
        float fade = 1 - Mathf.Clamp(burst / 0.85f, 0, 1);
        for (int i = 0; i < 20; i++)
        {
            Vector2 direction = Vector2.FromAngle(i * MathF.Tau / 20);
            Vector2 at = centre + direction * (radius * 1.4f + burst * 55);
            DrawLine(at, at - direction * 4, new Color(i % 2 == 0 ? Silver : Gold, fade), 2);
        }
    }
}
