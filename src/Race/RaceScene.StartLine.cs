using Godot;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    bool _falseStart;

    void CheckFalseStart(Vector3 previous)
    {
        if (_state != State.Countdown || StartRevving || _showKeys || InSchool || _falseStart) return;
        // Project the front of the body onto the start-line normal. The bumper, not
        // the centre of the car, must remain behind the line until the green signal.
        Vector2 dir = _track.CenterDir(0), line = _track.Center[0];
        Vector2 nose = new(_car.Forward.X * 2.11f, _car.Forward.Z * 2.11f);
        Vector2 a = new(previous.X, previous.Z);
        Vector2 b = new Vector2(_car.Pos.X, _car.Pos.Z) + nose;
        float before = (a - line).Dot(dir), after = (b - line).Dot(dir);
        if (before < 0f && after >= 0f)
        {
            _falseStart = true;
        }
    }
}
