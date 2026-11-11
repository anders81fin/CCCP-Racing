using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// The hooks of the development-only test code (src/Tests, RaceScene.Viewer*Test/ViewerWindowed*/ViewerSpike/ViewerFreeze, RaceScene.Lan*Test).
/// A release build (Configuration ExportRelease, see CCCPRacing.csproj) does not compile those files: a partial method without an
/// implementation is removed together with its calls, so production code calls the hooks plainly and has no #if.
/// A new test hook that production code must call belongs here (declaration only); its body stays in the test file as "partial void ...".
/// </summary>
public partial class RaceScene
{
    partial void ViewerSpikeTick(float dt);
    partial void ViewerFreezeTick(float dt);
    partial void ViewerTestTick(float dt);
    partial void ViewerLifecycleTick(float dt);
    partial void ViewerFidelityTick(float dt);
    partial void LanBoardsProbeTick();
    partial void LanKeepProbeTick();
    partial void LanKeepProbeOff();
    partial void LanLeaveProbeTick();
    partial void LanRecordProbeTick();
    partial void LanExportUiProbe();
    partial void LanWorldCheck();

    // Set by the test flags (parsed in production code); read only by the test files.
    bool _lanBoardProbe, _lanLeaveProbe, _lanRecordProbe;
}
