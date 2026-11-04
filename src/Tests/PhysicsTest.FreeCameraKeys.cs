using System;
using System.IO;
using System.Linq;
using CCCPRacing.Camera;
using CCCPRacing.Replay;
using Godot;

namespace CCCPRacing.Tests;
public partial class PhysicsTest
{
    void FreeCameraKeyTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "cccp-free-key-" + Guid.NewGuid().ToString("N"));
        using var rig = new LapRig(this, 3, 2);
        try
        {
            var fx = EditsFixture(rig, root); EditsRetain(fx); string original = fx.PairHash();
            var camera = new ReplayFreeCamera(new Transform3D(Basis.FromEuler(new Vector3(-.5f, .8f, 0)), new Vector3(40, 30, 20)), new Vector3(4, 3, 2));
            camera.SetFollow(true, new Vector3(4, 3, 2)); camera.SetLook(true, new Vector3(4, 3, 2));
            var shot = camera.Capture(new Vector3(4, 3, 2), 50, .2f, 800);
            var choices = new LapEdits { In = .5f, Out = 3.5f, CameraKeys = new() { new() { T = .5f, Mode = LapEdits.Free, Free = shot }, new() { T = 2f, Mode = LapEdits.TopDown } } };
            var saved = fx.A.SaveEdits(fx.Id, choices).Result;
            var fresh = new LapArchive(root); var loaded = fresh.LoadEdits(fx.Id).Result;
            Check(saved.Ok && loaded.Kind == LapEditsLoadKind.Ok && loaded.Edits.Version == LapEdits.CurrentVersion && loaded.Edits.CameraKeys[0].Free == shot && loaded.Edits.CameraKeys[1].Free == null,
                "free keys: archive save and fresh load preserve free pose, flags, lens and mixed preset keys");
            Check(fresh.List().Single().HasCameraKeys && fx.PairHash() == original, "free keys: list markers work and original ghost/tape/meta remain byte-identical");
            var exportSource = new CCCPRacing.Race.RaceScene.SpExportSource(fx.A.Open(fx.Id), "free-key test", 4f);
            var request = new CCCPRacing.Export.ExportRequest { InClock = 0, OutClock = 1 };
            Check(CCCPRacing.Export.ExportJob.Validate(request, exportSource) == null
                && CCCPRacing.Export.ExportJob.Validate(request with { CameraKeys = new() { new(.5f, LapEdits.Free, shot) } }, exportSource) == null
                && CCCPRacing.Export.ExportJob.Validate(request with { CameraKeys = new() { new(.5f, LapEdits.Free) } }, exportSource) != null,
                "free keys: export preflight accepts preset/free payloads and refuses missing free poses");
            var bound = loaded.Edits; string good = File.ReadAllText(fx.Edits);
            foreach (var bad in new[] { shot with { X = float.NaN }, shot with { OffsetY = float.PositiveInfinity }, shot with { Fov = 180 }, shot with { Near = 0 }, shot with { Far = .01f }, shot with { Pitch = 2 }, shot with { Yaw = 9 } })
            {
                var r = fx.A.SaveEdits(fx.Id, choices with { CameraKeys = new() { new() { T = 1f, Mode = LapEdits.Free, Free = bad } } }).Result;
                Check(!r.Ok && File.ReadAllText(fx.Edits) == good, "free keys: invalid pose/lens refused without overwriting saved edits (" + r.Message + ")");
            }
            Check((bound with { CameraKeys = new() { new() { T = 1f, Mode = LapEdits.Free } } }).Validate(4f) != null,
                "free keys: Free mode requires a pose");
            Check((bound with { CameraKeys = new() { new() { T = 1f, Mode = LapEdits.TopDown, Free = shot } } }).Validate(4f) != null,
                "free keys: preset mode cannot silently carry a free pose");
            Check((bound with { Playback = new() { Camera = LapEdits.Free } }).Validate(4f) != null,
                "free keys: free pose must be an explicit timed key, never an unbound playback default");
            Check((bound with { Version = 1 }).Validate(4f) != null, "free keys: free payload is not mislabelled as the old format");
            var animated = shot with { Motion = new LapCameraMotion { Tracking = LapCameraMotion.Helicopter, Rise = 18, Dolly = -5, EndFov = 40 } };
            var motionEdits = choices with { CameraKeys = new() { new() { T = 1f, Mode = LapEdits.Free, Free = animated } } };
            Check(fx.A.SaveEdits(fx.Id, motionEdits).Result.Ok && new LapArchive(root).LoadEdits(fx.Id).Result.Edits.CameraKeys[0].Free == animated,
                "motion archive: composed behavior survives save and fresh archive load");
            Check((bound with { Version = 2, CameraKeys = motionEdits.CameraKeys }).Validate(4f) != null,
                "motion archive: version-2 sidecar cannot silently carry animated shots");
            var legacy = bound with { Version = 1, CameraKeys = new() { new() { T = 1f, Mode = LapEdits.Auto } } };
            File.WriteAllText(fx.Edits, legacy.ToJson()); string legacyBytes = File.ReadAllText(fx.Edits);
            var legacyLoad = new LapArchive(root).LoadEdits(fx.Id).Result;
            Check(legacyLoad.Kind == LapEditsLoadKind.Ok && legacyLoad.Edits.Version == 1 && File.ReadAllText(fx.Edits) == legacyBytes,
                "free keys: version-1 edits load without rewriting or discarding their presets/trim");
            var upgraded = new LapArchive(root).SaveEdits(fx.Id, legacyLoad.Edits).Result;
            Check(upgraded.Ok && upgraded.Edits.Version == LapEdits.CurrentVersion && File.ReadAllText(fx.Edits + ".bak") == legacyBytes,
                "free keys: explicit save upgrades schema and preserves old edits as backup");
            File.WriteAllText(fx.Edits, "{\"Version\":999}");
            Check(new LapArchive(root).LoadEdits(fx.Id).Result.Kind == LapEditsLoadKind.NewerVersion, "free keys: unknown future edits remain visibly refused");
            var eval = new ReplayFreeCamera(Transform3D.Identity, Vector3.Zero);
            var target = new Vector3(-20, 6, 91); eval.Restore(shot, target); var pose = new Transform3D(eval.Basis, eval.Position);
            eval.Restore(shot, new Vector3(1500, -40, -22)); eval.Restore(shot, target);
            Check(new Transform3D(eval.Basis, eval.Position).IsEqualApprox(pose), "free keys: restoring follow+look pose is independent of seek history");
            var fixedShot = shot with { Follow = false, Look = false }; eval.Restore(fixedShot, target); pose = new Transform3D(eval.Basis, eval.Position);
            eval.Restore(fixedShot, Vector3.Zero);
            Check(new Transform3D(eval.Basis, eval.Position).IsEqualApprox(pose), "free keys: fixed position and manual aim ignore target changes");
            Check(fx.PairHash() == original, "free keys: source pair and metadata unchanged after failures, migration and reloads");
        }
        finally { Directory.Delete(root, true); }
    }
}
