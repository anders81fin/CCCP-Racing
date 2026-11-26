namespace CCCPRacing.Audio;

/// <summary>
/// The SPUTNIK's anti-lag crackle: the gaps between the bangs while the pedal is up and the revs are high, in the five-cylinder's beat (four quick ones and a longer breath).
/// The scene schedules the bangs with it and the offline auditions and tests do the same, so the two cannot drift apart.
/// </summary>
public static class AntiLagPattern
{
    public static readonly float[] Beats = { 0.06f, 0.06f, 0.07f, 0.09f, 0.26f };

    /// <summary>The gap (s) after the bang of this beat: shorter the higher the revs (load 0..1), with a jitter around 1.</summary>
    public static float Gap(int beat, float load, float jitter) => Beats[beat] * (1.5f - 0.6f * load) * jitter;
}
