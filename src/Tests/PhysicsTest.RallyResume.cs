using System.Linq;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void RallyResumeTest()
    {
        GD.Print("Rally resume: profile/track isolation and exact norm budget");
        NormPlan.UseTrack(null);
        string root = "user://test_rally_resume/";
        string dir = PlayerFiles.Dir(root, 1);
        PlayerFiles.Erase(root, 1);
        bool Same(RallyResume.Checkpoint a, RallyResume.Checkpoint b) => a != null && (a with { PassedTimes = b.PassedTimes, PassedTargets = b.PassedTargets }) == b;
        var point = new RallyResume.Checkpoint(3, 2, 9, 1, 123.5f, 9, 6) { PassedTimes = new[] { 74f, 71f, 69f }, PassedTargets = new[] { 75f, 72.5f, 70f } };
        Check(RallyResume.Save(dir, "ilyich", "layout-a", point) == Error.Ok, "checkpoint saves");
        Check(Same(RallyResume.Load(dir, "ilyich", "layout-a"), point), "checkpoint round trip keeps all fields");
        Check(RallyResume.Load(dir, "siberia", "layout-a") == null, "tracks have separate checkpoints");
        Check(RallyResume.Load(PlayerFiles.Dir(root, 2), "ilyich", "layout-a") == null, "profiles have separate checkpoints");
        Check(RallyResume.Load(dir, "ilyich", "layout-b") == null, "changed track or norms cannot resume an old run");
        var loaded = RallyResume.Load(dir, "ilyich", "layout-a");
        Check(loaded.PassedTimes.Take(3).SequenceEqual(point.PassedTimes), "passing lap times survive saving and loading");
        var plan = new NormPlan();
        plan.Restore(loaded.Step, loaded.Left, loaded.Used, loaded.PassedTimes, loaded.PassedTargets);
        Check(plan.PassedTimes.Take(3).SequenceEqual(point.PassedTimes), "restoring keeps each passing lap time");
        Check(plan.Enabled && plan.Step == 3 && plan.Left == 2 && plan.Used == 9 && !plan.Finished,
            "restoring does not replenish laps or reset progress");
        Check(plan.OnLap(999, true) == NormPlan.Outcome.Continue && plan.Left == 1,
            "restored lap budget still counts down");
        Check(plan.OnLap(999, true) == NormPlan.Outcome.OutOfLaps, "restored run can still fail normally");
        RallyResume.Save(dir, "siberia", "layout-a", point);
        Check(RallyResume.Clear(dir, "ilyich") == Error.Ok && RallyResume.Load(dir, "ilyich", "layout-a") == null
            && Same(RallyResume.Load(dir, "siberia", "layout-a"), point), "consuming one checkpoint leaves other tracks intact");
        RallyResume.Save(dir, "ilyich", "layout-a", point with { Left = 0 });
        Check(RallyResume.Load(dir, "ilyich", "layout-a") == null, "finished or corrupt budgets are rejected");
        plan.Reset(true);
        plan.OnLap(10, false);
        plan.OnLap(NormPlan.Ladder[0], true);
        Check(plan.PassedTimes[0] == 0, "void and equal-to-norm laps do not record passing times");
        Check(plan.OnLap(74, true) == NormPlan.Outcome.NewNorm && plan.PassedTimes[0] == 74,
            "the actual norm-beating lap is recorded");
        plan.OnLap(74, true);
        Check(plan.PassedTimes[0] == 74 && plan.PassedTimes[1] == 0, "a later failed lap does not overwrite history");
        plan.Reset(true);
        plan.Climate = () => 5f;
        plan.OnLap(79, true);
        plan.Climate = null;
        Check(plan.PassedTimes[0] == 79 && plan.PassedTargets[0] == 80,
            "passing time retains the climate-adjusted target actually beaten");
        plan.Reset(true, NormPlan.Steps - 1);
        Check(plan.PassedTimes.All(t => t == 0), "a new run clears passing times");
        Check(plan.OnLap(1, true) == NormPlan.Outcome.Success && plan.PassedTimes[^1] == 1,
            "the final gold norm also records its passing lap");
        RallyResume.Save(dir, "ilyich", "layout-a", point);
        var legacy = new ConfigFile();
        legacy.Load(dir + RallyResume.Filename);
        for (int i = 0; i < NormPlan.Steps; i++) legacy.EraseSectionKey("ilyich", $"passed_{i}");
        legacy.Save(dir + RallyResume.Filename);
        Check(RallyResume.Load(dir, "ilyich", "layout-a").PassedTimes.All(t => t == 0),
            "old checkpoints resume without invented passing times");
        PlayerFiles.Erase(root, 1);
        Check(RallyResume.Load(dir, "siberia", "layout-a") == null, "erasing a profile also erases its checkpoints");
    }
}
