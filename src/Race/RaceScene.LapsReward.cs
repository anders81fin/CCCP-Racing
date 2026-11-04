using System;
using System.Linq;
using Godot;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    int _lapsRewardIndex = -1;
    float _lapsRewardAge;
    string _lapsRewardSource;
    MenuItem _rewardFor;
    // Halfway between the original speed and the previous one-third speed.
    const float LapsRewardSpeed = 2f / 3f;
    const float LapsRewardReveal = 2.5f;
    bool GoldTrackReward => _lapsRewardIndex >= 0 && Tracks[_lapsRewardIndex].requires == Bonus;

    void BeginLapsReward()
    {
        _lapsRewardIndex = -1;
        if (_previewMovie || _trackFor is not (MenuItem.Training or MenuItem.Race)) return;
        _rewardFor = _trackFor;
        bool rally = _rewardFor == MenuItem.Race;
        for (int i = 1; i < Tracks.Length; i++)
        {
            if (LockedByMedals(i, _rewardFor) || _campaign.LapsPresented(Tracks[i].id, rally)) continue;
            _lapsRewardIndex = _cardSel = i;
            _lapsRewardAge = 0;
            string source = Tracks[i].requires == Bonus
                ? Tracks.First(t => _campaign.Best(t.id) == NormPlan.Medal.Gold).id : Tracks[i].requires;
            _lapsRewardSource = TrackName(source);
            _hud.BeginLapsReward(Tracks[i].requires == Bonus ? NormPlan.Medal.Gold
                : rally ? NormPlan.Medal.Silver : NormPlan.Medal.Bronze, rally);
            break;
        }
    }

    void LapsRewardFrame(float dt)
    {
        if (_lapsRewardIndex < 0) return;
        if (_state != State.Title || _card != TitleCard.Track || _trackFor != _rewardFor)
        {
            _lapsRewardIndex = -1;
            return;
        }
        float before = _lapsRewardAge;
        _lapsRewardAge += Math.Min(dt, 0.1f) * LapsRewardSpeed;
        float reveal = GoldTrackReward ? 3.6f : LapsRewardReveal;
        if (before < reveal && _lapsRewardAge >= reveal)
        {
            _campaign.PresentLaps(Tracks[_lapsRewardIndex].id, _rewardFor == MenuItem.Race);
            _hud.LapsRewardClang(true);
        }
        if (_lapsRewardAge >= (GoldTrackReward ? 5.2f : 3.4f))
        {
            BeginLapsReward();
            _overlayExitFrame = Engine.GetProcessFrames();
        }
    }
}
