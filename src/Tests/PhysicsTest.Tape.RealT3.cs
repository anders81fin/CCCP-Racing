using CCCPRacing.Race;

namespace CCCPRacing.Tests;

// T3: the real player. It runs after T1's install, so it replaces the temporary bridge to the reference player.
public partial class PhysicsTest
{
    partial void InstallRealTapeT3()
    {
        TapeSeams.NewPlayer = (tape, index, sink) => new WorldTapePlayer(tape, index, sink);
        TapeSeams.Real.Add("NewPlayer");
    }
}
