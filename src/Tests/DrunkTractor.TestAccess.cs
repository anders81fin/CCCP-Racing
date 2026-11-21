using Godot;

namespace CCCPRacing.Track;

// Development only (src/Tests is not compiled into the release): the situation scenarios (RaceScene.ViewerWindowedSituations.cs) put the tractor where an encounter is wanted and let the real game decide the warning.
public partial class DrunkTractor
{
    /// <summary>Stand at <paramref name="pos"/> facing <paramref name="forward"/>, bound for <paramref name="goal"/> at about <paramref name="speed"/>; ploughing normally (the barn's departure is over).</summary>
    public void PlaceForTest(Vector2 pos, Vector2 forward, Vector2 goal, float speed)
    {
        _departureStage = 3; _napTimer = 0; _pos = pos; _goal = goal;
        _yaw = Mathf.Atan2(-forward.X, -forward.Y);
        _speed = speed; _targetSpeed = speed;
    }
}
