using System.Linq;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The globals a nested viewer world may change that the live scene owns, captured before the viewer exists and put back after its deferred
/// teardown. Scoped fixes live where the global does (see phase2-statics-table.md); only the ones that cannot be scoped are here.
/// Surfaces.Wetness (Weather keeps its own), RadarHead readers (RadarHead.Heads, per world) and GulagTruck._onRoad are scoped, not captured.
/// </summary>
public sealed class ViewerGlobals
{
    string _trackId;
    RadarHead[] _radars;
    NightGlow.State _glow;
    NormPlan.Statics _norms;
    SeeThrough.FadeStatics _fade;

    public static ViewerGlobals Capture(RaceScene live) => new()
    {
        _trackId = live.TrackIdForRestore,
        _radars = RadarHead.All.ToArray(),      // (the legacy list RaceScene's build clears; readers use RadarHead.Heads)
        _glow = NightGlow.Snapshot(),
        _norms = NormPlan.StaticsSnapshot(),
        _fade = SeeThrough.Capture(),
    };

    /// <summary>After the nested scene left the tree: statics back, then the live night re-applies its tint and glow at once.</summary>
    public void Restore(RaceScene live)
    {
        live.TrackIdForRestore = _trackId;
        RadarHead.All.Clear(); RadarHead.All.AddRange(_radars);
        NightGlow.Restore(_glow);
        NormPlan.RestoreStatics(_norms);
        SeeThrough.Restore(_fade);
        live.DayNightNode?.ForceReapply();
    }
}
