using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>The title's keys and pad buttons: Enter starts the selected menu item, Left/Right move along the row.</summary>
public partial class RaceScene
{
    void TitleStart() => MenuEnter();

    void TitleSide(int step) => MoveMenu(step);

    /// <summary>The title takes the pad's buttons itself (no overlay, card or reward in the way): the same as the keys, without the key press in between.</summary>
    bool TitleDirect => _state == State.Title && _card == TitleCard.None && _lapsRewardIndex < 0 && !PresentationActive && !_previewMovie && !TitleSettingsOpen && !_titleMenuHidden
        && Engine.GetProcessFrames() > _overlayExitFrame + 1;

    /// <summary>SETTINGS opened from the title's top row (or one of its screens): it takes the keys and the pad's buttons, the menu waits under it.</summary>
    bool TitleSettingsOpen => _state == State.Title && (_hud.SettingsOpen || _hud.VisualOpen || _hud.MixerOpen);

    // The main menu can be hidden (the owner's order, 2026-11-19): Space or the pad's RB hides it to watch the background video, and pressing again (Space, Esc, Enter, or any pad button) brings it back. A hint stays at the
    // bottom centre, always. The menu and its rows, the cards of a mode (fuel, track, time of day, saved rally) and SETTINGS with its screens (SOUNDS, VISUALS) hide; a tour, the name entry, the boards, a LAN page and the recordings do not.
    bool _titleMenuHidden;

    bool TitleMenuToggleAllowed => _state == State.Title && !_tourOpen && !_carTourOpen && !_askName && !_showBoard && !_showLog && !_watching && !_recordingsOpen && !_showFiles && !_showLessons
        && !_showKeys && _lapsRewardIndex < 0 && !PresentationActive && !_previewMovie && _lanPage == LanPage.None && _resignOut < 0 && !_keepNameOpen && !_hud.ReplayListOpen && Engine.GetProcessFrames() > _overlayExitFrame + 1;

    void SetTitleMenuHidden(bool hidden)
    {
        _titleMenuHidden = hidden;
        _overlayExitFrame = Engine.GetProcessFrames();      // (the press that toggled it does nothing else)
    }

    /// <summary>The key that hides or shows the main menu, read before the cards take the keys (so it works over a card too): true when it was the key's.</summary>
    bool TitleHideInput()
    {
        if (_state != State.Title) return false;
        if (_titleMenuHidden)      // hidden: the music and the background video go on; Space, Esc or Enter (any pad button) bring it back, nothing else does anything
        {
            if (Input.IsActionJustPressed("handbrake") || Input.IsActionJustPressed("pause") || Input.IsActionJustPressed("start")) SetTitleMenuHidden(false);
            return true;
        }
        if (TitleMenuToggleAllowed && Input.IsActionJustPressed("handbrake")) { SetTitleMenuHidden(true); return true; }
        return false;
    }
}
