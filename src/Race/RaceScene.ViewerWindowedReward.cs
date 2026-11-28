using System;
using System.Collections;
using System.Linq;
using Godot;

namespace CCCPRacing.Race;

// Windowed scenario "reward-look" (--viewer-windowed-only=rewardlook): the medal ceremony on the track card (a bronze on the first track opens the next one: the star, the track and medal under it, and the
// "STAKHANOV LAPS AUTHORIZED" stamp typing across the track names) on a fresh temporary profile. The mode's hint card is not drawn over the star's place, and the stamp's bar stays inside the card's frame.
public partial class RaceScene
{
    IEnumerable VwRewardLook()
    {
        foreach (var x in VwWaitReady()) yield return x;
        for (int i = 0; i < 600 && !(_state == State.Title && _card == TitleCard.None && !_showLog && !_askName); i++) yield return null;
        for (int i = 0; i < 90; i++) yield return null;
        _campaign.Award("ilyich", NormPlan.Medal.Bronze, FuelGrade.Diesel);
        _menuParent = MenuItem.Single; _menu = MenuItem.Training;
        OpenTrackCard(MenuItem.Training);
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(_lapsRewardIndex >= 0 && _card == TitleCard.Track, $"a bronze on the first track opens the ceremony on the track card (reward index {_lapsRewardIndex})");
        foreach (float at in new[] { 0.3f, 1.2f, 1.8f, 2.3f })
        {
            for (int i = 0; i < 600 && _lapsRewardAge < at; i++) yield return null;
            VwGrab(GetViewport(), $"rewardlook-{at:0.0}"); VwLog("shot", ("name", $"rewardlook-{at:0.0}"), ("age", _lapsRewardAge));
            yield return null;
        }
        VwCheck(_hud.LapsRewardSource != null, "the ceremony is on");
        // the rally's: a silver on the first track opens the SOCIALIST RALLY on the next (rays round the star, a wider stamp)
        for (int i = 0; i < 600 && _lapsRewardIndex >= 0; i++) yield return null;
        _lapsRewardIndex = -1; _card = TitleCard.None; for (int i = 0; i < 10; i++) yield return null;
        _campaign.Award("ilyich", NormPlan.Medal.Silver, _rallyFuel);
        _menuParent = MenuItem.Single; _menu = MenuItem.Race;
        OpenTrackCard(MenuItem.Race);
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(_lapsRewardIndex >= 0 && _hud.LapsRewardSource != null, $"a silver opens the rally's ceremony (reward index {_lapsRewardIndex})");
        foreach (float at in new[] { 0.3f, 1.3f, 2.0f })
        {
            for (int i = 0; i < 600 && _lapsRewardAge < at; i++) yield return null;
            VwGrab(GetViewport(), $"rewardlook-silver-{at:0.0}"); VwLog("shot", ("name", $"rewardlook-silver-{at:0.0}"), ("age", _lapsRewardAge));
            yield return null;
        }
        // a gold opens Poligon 17 (its own, longer ceremony)
        for (int i = 0; i < 600 && _lapsRewardIndex >= 0; i++) yield return null;
        _lapsRewardIndex = -1; _card = TitleCard.None; for (int i = 0; i < 10; i++) yield return null;
        _campaign.Award("ilyich", NormPlan.Medal.Gold, _rallyFuel);
        _menuParent = MenuItem.Single; _menu = MenuItem.Training;
        OpenTrackCard(MenuItem.Training);
        for (int i = 0; i < 10; i++) yield return null;
        VwCheck(_lapsRewardIndex >= 0 && _hud.LapsRewardSource != null, $"a gold opens Poligon 17's ceremony (reward index {_lapsRewardIndex})");
        foreach (float at in new[] { 0.4f, 1.5f, 2.6f, 3.4f })
        {
            for (int i = 0; i < 900 && _lapsRewardAge < at; i++) yield return null;
            VwGrab(GetViewport(), $"rewardlook-gold-{at:0.0}"); VwLog("shot", ("name", $"rewardlook-gold-{at:0.0}"), ("age", _lapsRewardAge));
            yield return null;
        }
    }
}
