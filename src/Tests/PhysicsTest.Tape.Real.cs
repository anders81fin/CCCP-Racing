using Godot;

namespace CCCPRacing.Tests;

// Where the real tape implementations plug into the tests. Each owner implements its own partial method in its own file
// (PhysicsTest.Tape.RealT1.cs ...), so parallel work never edits the same lines. An implementation installs itself by assigning
// the seams it owns in TapeSeams and adding their names to TapeSeams.Real; --tape-tests then runs against it instead of the
// reference model, and the mutation checks (which start from the installed baseline) run against it too.
//
//   T1: NewIndex, MarkPart, RegisterSpinner, RegisterSound, NewRecorder, UnregisteredSpinCount, EncodeTape, DecodeTape
//   T2: Fingerprint (the shared one), SavePair, LoadTape, CleanStale, RealOps
//   T3: NewPlayer
public partial class PhysicsTest
{
    partial void InstallRealTapeT1();
    partial void InstallRealTapeT2();
    partial void InstallRealTapeT3();

    void InstallRealTape()
    {
        InstallRealTapeT1();
        InstallRealTapeT2();
        InstallRealTapeT3();
        GD.Print($"tape: real seams: {(TapeSeams.Real.Count == 0 ? "none" : string.Join(", ", TapeSeams.Real))}");
    }
}
