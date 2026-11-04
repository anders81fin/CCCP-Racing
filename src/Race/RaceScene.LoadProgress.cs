using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The normal single-player load's progress for the TrackLoadingScreen bar (table and curve: LoadProgress.cs).
/// The scene that builds for a card keeps its own reference to that card, taken when the build starts, so it never writes to a later or other static Active
/// (a LAN card and a replay viewer's build never get one: their progress is not this bar's).
/// </summary>
public partial class RaceScene
{
    TrackLoadingScreen _loadCard;
    int _loadSteps;
    /// <summary>Checkpoints passed by this scene's own (non-viewer) sliced build.</summary>
    public int LoadSteps => _loadSteps;

    void LoadStepsBegin()
    {
        _loadSteps = 0;
        _loadCard = !ViewerMode && TrackLoadingScreen.Active is { IsLan: false } card ? card : null;
        _loadCard?.ReportBuild(0f);
    }

    void LoadStep()
    {
        if (ViewerMode) return;
        _loadSteps++;
        if (_loadCard != null && IsInstanceValid(_loadCard) && TrackLoadingScreen.Active == _loadCard)
            _loadCard.ReportBuild(LoadProgress.Fraction(_track?.Id ?? _trackId, _loadSteps));
    }

    void LoadStepsEnd()
    {
        if (!ViewerMode && OS.HasEnvironment("LOADSTEPS")) GD.Print($"LOADSTEPS {_trackId} {_loadSteps}");
        _loadCard = null;      // (the ready state, 1.0, is the card's own: TrackLoadingScreen.Loaded)
    }
}
