using System;
using Godot;

namespace CCCPRacing.UI;

/// <summary>
/// The menus' headings type themselves out, once, when their panel opens (or its heading changes):
/// the KGB ARCHIVE, the mode's hint card, DRIVING SCHOOL, the BOARD OF HONOUR, the title's cards
/// before a run and the name question. All at one rate, with a cursor; the lines under a heading
/// are there at once. PARTY MEETING and loading-card headings type too; the TEST VERSION card and notes do not.
/// </summary>
public partial class Hud
{
    const float HeadingRate = 28f;      // letters a second
    static readonly Color HeadingShadow = new(0.35f, 0, 0);
    HudMode _typewriterMode = (HudMode)(-1);
    float _menuTextClock, _hintTextClock, _cardTextClock, _nameTextClock;
    string _hintTitle, _cardTitleSeen;
    bool _nameSeen;
    string _loadingHeadingSeen;
    float _loadingHeadingClock;
    ulong _loadingHeadingStartMs;
    string _previewCardHeadingSeen;
    float _previewCardHeadingClock;

    void TickMenuText(float dt)
    {
        if (_typewriterMode != Mode)
        {
            _typewriterMode = Mode;
            _menuTextClock = _hintTextClock = 0;
            _hintTitle = null;
        }
        // A slow world-building frame must not consume the whole reveal.
        dt = Math.Min(dt, 0.1f);
        string previewHeading = TrackPreview && PreviewCardAlpha > 0 && !PreviewMovie
            ? string.Join("\n", PreviewCardBig) : null;
        if (previewHeading != _previewCardHeadingSeen)
        {
            _previewCardHeadingSeen = previewHeading;
            _previewCardHeadingClock = 0;
        }
        else _previewCardHeadingClock += dt;
        string loadingHeading = LoadingTrack ? LoadingTrackHeading : null;
        if (loadingHeading != _loadingHeadingSeen)
        {
            _loadingHeadingSeen = loadingHeading;
            _loadingHeadingClock = 0;
            _loadingHeadingStartMs = Time.GetTicksMsec();
        }
        // Godot can cap Process delta during the world build. Keep the loading card's
        // repeat/pause tied to real time so a short full-line pause does not stretch.
        else if (LoadingTrack) _loadingHeadingClock = (Time.GetTicksMsec() - _loadingHeadingStartMs) / 1000f;
        else _loadingHeadingClock += dt;
        _menuTextClock += dt;
        _hintTextClock += dt;
        _cardTextClock += dt;
        _nameTextClock += dt;
        if (CardTitle != _cardTitleSeen) { _cardTitleSeen = CardTitle; _cardTextClock = 0; }
        if (NameAsk != _nameSeen) { _nameSeen = NameAsk; _nameTextClock = 0; }
        if (Mode != HudMode.Title || MenuHint == null)
        {
            _hintTitle = null;
            _hintTextClock = 0;
        }
    }

    /// <summary>A panel's heading (gold, its shadow) typing out from elapsed seconds after the panel opened.</summary>
    void DrawHeading(string text, float cx, float y, float elapsed, int scale = 2) =>
        DrawTypewriter(text, cx, y, Gold, elapsed, HeadingRate, scale, HeadingShadow);

    void DrawTypewriter(string text, float cx, float y, Color color, float elapsed,
        float rate = 28f, int scale = 1, Color? shadow = null, bool cursor = true)
    {
        if (elapsed < 0) return;
        int count = Math.Clamp((int)(elapsed * rate), 0, text.Length);
        _typewriterSound.Letters(text, count);
        // Anchor against the complete line so new letters never move existing ones.
        float x = Mathf.Floor(cx - PixelFont.Measure(text, scale) * 0.5f);
        PixelFont.Draw(this, text[..count], new Vector2(x, y), color, scale, shadow);
        if (cursor && count < text.Length)
            DrawRect(new Rect2(x + count * PixelFont.Advance * scale, y + 5 * scale,
                PixelFont.GlyphW * scale, 2 * scale), color);
    }
}
