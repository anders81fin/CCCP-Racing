using Godot;

namespace CCCPRacing.Race;

/// <summary>The title's keys and pad buttons: Enter starts the selected menu item, Left/Right move along the row.</summary>
public partial class RaceScene
{
    void TitleStart() => MenuEnter();

    void TitleSide(int step) => MoveMenu(step);

    /// <summary>The title takes the pad's buttons itself (no overlay, card or reward in the way): the same as the keys, without the key press in between.</summary>
    bool TitleDirect => _state == State.Title && _card == TitleCard.None && _lapsRewardIndex < 0 && !PresentationActive && !_previewMovie
        && Engine.GetProcessFrames() > _overlayExitFrame + 1;
}
