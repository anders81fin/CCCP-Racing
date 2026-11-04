using CCCPRacing.UI;

namespace CCCPRacing.Race;

/// <summary>The Socialist Rally's target card (docs: owner's request 2026-10-27): the production norms in a compact top-right card. The Socialist Rally only (no Stakhanov laps, school, LAN, viewer or title) and only while the left aid shows.</summary>
public partial class RaceScene
{
    void NormCardHud()
    {
        bool show = !ViewerMode && _run == RunMode.Rally && !InSchool && !InLanRace && _norms.Enabled && _hud.PlanLaps > 0 && !(_norms.Finished && !_norms.Fulfilled);
        if (!show) { if (_hud != null) _hud.NormCard = null; return; }
        _hud.NormCard = NormCardModel.Build(NormPlan.Ladder, _norms.Allowance, _norms.PassedTargets, _norms.PassedTimes, _norms.Step, _norms.Fulfilled, NormPlan.BronzeAt, NormPlan.SilverAt);
    }
}
