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
        // Medal jumps (the owner's order, 2026-11-19): a checkpoint after a jump saves and resumes exactly, with no lap times invented for the norms the jump skipped.
        {
            plan.Reset(true);
            plan.OnLap(50f, true);      // (a gold-time lap from the start: the bronze, 4 norms)
            var jumpPoint = new RallyResume.Checkpoint(plan.Step, plan.Left, plan.Used, 0, 61.5f, plan.Used, 0) { PassedTimes = plan.PassedTimes, PassedTargets = plan.PassedTargets };
            Check(plan.Step == NormPlan.BronzeAt && plan.Used == 1, "a jump run: the bronze after one real lap");
            Check(RallyResume.Save(dir, "ilyich", "layout-a", jumpPoint) == Error.Ok, "the checkpoint after a jump saves");
            var back = RallyResume.Load(dir, "ilyich", "layout-a");
            Check(back != null && back.Step == NormPlan.BronzeAt && back.Used == 1 && back.PassedTimes[NormPlan.BronzeAt - 1] == 50f && back.PassedTimes.Take(NormPlan.BronzeAt - 1).All(t => t == 0)
                && back.PassedTargets.Take(NormPlan.BronzeAt - 1).All(t => t == 0), "it loads with the lap's time on the norm it landed on and none on the skipped ones");
            var resumed = new NormPlan();
            resumed.Restore(back.Step, back.Left, back.Used, back.PassedTimes, back.PassedTargets);
            Check(resumed.Step == NormPlan.BronzeAt && resumed.Used == 1 && resumed.Left == back.Left && NormPlan.MedalFor(resumed.Step) == NormPlan.Medal.Bronze, "the resumed run has its bronze, its one lap and its window");
            Check(resumed.OnLap(50f, true) == NormPlan.Outcome.NewNorm && resumed.Step == NormPlan.SilverAt && resumed.Used == 2 && resumed.PassedTimes.Count(t => t > 0) == 2,
                "and the next gold-time lap jumps to the silver: the bronze was earned in this run, saved with it (two real laps, two times)");
            // A checkpoint from before the jumps (every norm beaten by its own lap): resumes with all its times, and the jump rules apply to its next lap.
            var oldPoint = new RallyResume.Checkpoint(2, 7, 6, 0, 300f, 6, 0) { PassedTimes = new[] { 74f, 72f }, PassedTargets = new[] { 75f, 72.5f } };
            RallyResume.Save(dir, "ilyich", "layout-a", oldPoint);
            var old = RallyResume.Load(dir, "ilyich", "layout-a");
            var oldPlan = new NormPlan();
            oldPlan.Restore(old.Step, old.Left, old.Used, old.PassedTimes, old.PassedTargets);
            Check(oldPlan.Step == 2 && oldPlan.PassedTimes[0] == 74f && oldPlan.PassedTimes[1] == 72f && oldPlan.Used == 6, "an old checkpoint resumes with its own times and laps");
            Check(oldPlan.OnLap(60f, true) == NormPlan.Outcome.NewNorm && oldPlan.Step == NormPlan.BronzeAt && oldPlan.Jumped == 2 && oldPlan.PassedTimes[0] == 74f && oldPlan.PassedTimes[2] == 0 && oldPlan.PassedTimes[3] == 60f && oldPlan.Used == 7,
                "its next lap under the bronze time jumps to the bronze (norms 3-4): the old times stay, the skipped norm 3 has none, one real lap more");
            RallyResume.Clear(dir, "ilyich");
        }
        PlayerFiles.Erase(root, 1);
        Check(RallyResume.Load(dir, "siberia", "layout-a") == null, "erasing a profile also erases its checkpoints");
    }
}
