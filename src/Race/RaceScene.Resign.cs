using System;
using CCCPRacing.Audio;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// QUIT on the title (or Esc at its top, or in the archive at launch): the Party has received your resignation.
/// There is nothing to answer. The music fades out, a hammer is drawn back and a shot goes under the
/// window's words, the stain of it is left, and everything else fades to the dark; then the game ends.
/// </summary>
public partial class RaceScene
{
    float _resignOut = -1f;
    /// <summary>When the hammer starts (the music has faded by then, so the sounds are heard), and the quit after the stain.</summary>
    public const float ResignClick = 1.0f;
    const float ResignQuit = Hud.ResignBloodFadeAt + Hud.ResignBloodFadeLen + 0.05f;

    void AskResignation()
    {
        if (_resignOut >= 0) return;
        _resignOut = 0f;
        ResignHud();
    }

    /// <summary>The resignation is on its way: nothing more to say (called for every input event).</summary>
    bool ResignInput(InputEvent e) => _resignOut >= 0;

    /// <summary>The clock of the sequence: the music out, the hammer, then out (called every frame).</summary>
    void ResignTick(float dt)
    {
        if (_resignOut < 0) return;
        float before = _resignOut;
        _resignOut += dt;
        if (before == 0f) { _radio?.Stop(); AudioMix.MuteSfx(true); }
        if (before < ResignClick && _resignOut >= ResignClick) AddChild(new HammerSound());
        if (_resignOut >= ResignQuit) CCCPRacing.Audio.AudioMix.Quit(this);
        ResignHud();
    }

    void ResignHud() => _hud.ResignOut = _resignOut;
}
