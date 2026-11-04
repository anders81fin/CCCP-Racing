using System;
using System.IO;
using System.Linq;
using System.Threading;
using CCCPRacing.Replay;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void LapArchiveDeleteTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "cccp-delete-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var rig = new LapRig(this, 2, 2);
        try
        {
            var ops = new LapFaultOps(); var a = new LapArchive(Path.Combine(root, "laps"), ops);
            string Add() { var g = rig.Drive(2, 30, 12); var s = a.Submit(LapCap(g, rig.Rec)); a.Flush(TimeSpan.FromSeconds(15)); return s.Id; }
            string memory = Add(); var snapshot = a.Open(memory); var bytes = snapshot.GhostBytes.ToArray();
            Check(a.Delete(memory).Result.Ok && a.List().Count == 0 && a.Open(memory) == null && a.Accounting().OwnedBytes == 0,
                "delete: THIS SESSION lap disappears and releases its memory");
            Check(snapshot.GhostBytes.Span.SequenceEqual(bytes), "delete: an already opened immutable source stays unchanged");
            string kept = Add(), other = Add();
            var k = a.Retain(kept, "DELETE ME").Result; var o = a.Retain(other, "NEIGHBOUR").Result;
            a.SaveEdits(kept, new LapEdits { Pinned = true }).Wait();
            a.SaveEdits(kept, new LapEdits { Pinned = false }).Wait();
            var neighbour = File.ReadAllBytes(Path.Combine(o.Dir, "ghost.bin"));
            using (var busy = new ManualResetEventSlim())
            using (var resume = new ManualResetEventSlim())
            {
                a.WorkerHook = s => { if (s.StartsWith("retain:") || s.StartsWith("edits-save:")) { busy.Set(); resume.Wait(TimeSpan.FromSeconds(10)); } };
                var saving = a.Retain(kept, "SAVING");
                try { Check(busy.Wait(TimeSpan.FromSeconds(5)) && !a.Delete(kept).Result.Ok, "delete: a KEEP/rename already in flight cannot be removed"); }
                finally { resume.Set(); }
                saving.Wait(); busy.Reset(); resume.Reset();
                var editing = a.SaveEdits(kept, new LapEdits { Pinned = true });
                try { Check(busy.Wait(TimeSpan.FromSeconds(5)) && !a.Delete(kept).Result.Ok, "delete: an edit write already in flight cannot be removed"); }
                finally { resume.Set(); }
                editing.Wait(); a.WorkerHook = null;
            }
            ops.FailStep = s => s == "lap-delete";
            Check(!a.Delete(kept).Result.Ok && a.Open(kept) != null && File.Exists(Path.Combine(k.Dir, "edits.json")),
                "delete: an IO refusal is reported and leaves the recording usable");
            ops.FailStep = null;
            Check(a.Delete(kept).Result.Ok && !Directory.Exists(k.Dir) && !a.List().Any(i => i.Id == kept),
                "delete: kept ghost, world tape, edits and backup disappear together");
            a.Refresh(); a.Flush(TimeSpan.FromSeconds(15));
            Check(a.List().Count == 1 && a.List()[0].Id == other && File.ReadAllBytes(Path.Combine(o.Dir, "ghost.bin")).SequenceEqual(neighbour),
                "delete: rescan does not resurrect the lap or change a neighbour");
            Check(!a.Delete("../outside").Result.Ok && !a.Delete(kept).Result.Ok, "delete: unknown/path-like IDs cannot remove anything");

            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            a.WorkerHook = s => { if (s.StartsWith("delete:")) { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); } };
            var deletion = a.Delete(other);
            try
            {
                Check(entered.Wait(TimeSpan.FromSeconds(5)), "delete: retained IO runs on the worker");
                Check(!a.Retain(other, "NO").Result.Ok && !a.SaveEdits(other, new LapEdits()).Result.Ok && a.Open(other) == null,
                    "delete: KEEP, edit writes and new opens are refused while deleting");
                a.Refresh();
            }
            finally { release.Set(); }
            Check(deletion.Result.Ok && a.Flush(TimeSpan.FromSeconds(15)) && a.List().Count == 0, "delete: queued refresh cannot resurrect a deleted entry");

            var pending = new LapArchive(Path.Combine(root, "pending"));
            using var started = new ManualResetEventSlim(); using var finish = new ManualResetEventSlim();
            pending.WorkerHook = s => { if (s.StartsWith("compress:")) { started.Set(); finish.Wait(TimeSpan.FromSeconds(10)); } };
            var sub = pending.Submit(LapCap(rig.Drive(2, 20, 8), rig.Rec));
            try
            {
                Check(started.Wait(TimeSpan.FromSeconds(5)) && pending.Delete(sub.Id).Result.Ok && pending.List().Count == 0,
                    "delete: a session lap can be removed while tape compression is in flight");
            }
            finally { finish.Set(); }
            pending.Flush(TimeSpan.FromSeconds(15));
            Check(pending.List().Count == 0 && pending.Accounting().OwnedBytes == 0 && pending.Accounting().InFlightBytes == 0,
                "delete: compression finishes without republishing the removed payload");

            if (!OperatingSystem.IsWindows())
            {
                string outside = Path.Combine(root, "outside"); Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside, "sentinel"), "keep");
                string linked = Add(); string dir = a.Retain(linked, "LINK TEST").Result.Dir;
                Directory.CreateSymbolicLink(Path.Combine(dir, "external"), outside);
                Check(a.Delete(linked).Result.Ok && File.ReadAllText(Path.Combine(outside, "sentinel")) == "keep",
                    "delete: an inner symbolic link is removed without following it outside the lap");
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
