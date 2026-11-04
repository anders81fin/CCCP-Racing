using System;
using CCCPRacing.Export;
using CCCPRacing.Replay;
using Godot;

namespace CCCPRacing.Tests;
public partial class PhysicsTest
{
    void FreeCameraExportContractTests()
    {
        var shot = new LapFreeCamera { X = 40, Y = 20, Z = 10, OffsetX = 15, OffsetY = 20, OffsetZ = 8,
            Pitch = -.5f, Yaw = .8f, Follow = true, Look = true, Fov = 55, Near = .2f, Far = 800 };
        var request = new ExportRequest { InClock = 10, OutClock = 14, CameraKeys = new() {
            new(9f, LapEdits.Free, shot), new(12f, LapEdits.Free, shot with { Follow = false, Look = false, X = -10 }) } };
        Check(request.ValidateCameras(66f) == null && request.HasFreeCameraKeys, "free export: a complete key before IN is accepted");
        var plan = new ExportPlan { Request = request };
        var again = ExportPlan.FromJson(plan.ToJson());
        Check(again.Version == ExportPlan.CurrentVersion && again.Request.CameraKeys[0].Free == shot && again.Request.CameraKeys[1].Free?.Follow == false,
            "free export: parent/child JSON preserves position, aim, flags and lens");
        var old = ExportPlan.FromJson("{\"Version\":1,\"Request\":{\"InClock\":0,\"OutClock\":1,\"CameraKeys\":[{\"T\":0,\"Mode\":\"TopDown\"}]}}");
        Check(old.Version == 1 && old.Request.ValidateCameras(10f) == null && !old.Request.HasFreeCameraKeys,
            "free export: old preset-only plans remain readable");
        Check(ExportPlan.FromJson("{}").Version == 1, "free export: a missing plan version retains legacy semantics");
        bool Refused(string json) { try { ExportPlan.FromJson(json); return false; } catch { return true; } }
        Check(Refused((plan with { Version = 1 }).ToJson()) && Refused((plan with { Version = 999 }).ToJson()),
            "free export: free payload in legacy plan and future plan version are refused");
        Check((request with { DefaultCamera = LapEdits.Free }).ValidateCameras(66f) != null, "free export: unbound free default refused");
        Check((request with { CameraKeys = new() { new(9f, LapEdits.Free) } }).ValidateCameras(66f) != null,
            "free export: missing free-camera payload refused");
        Check((request with { CameraKeys = new() { new(9f, LapEdits.TopDown, shot) } }).ValidateCameras(66f) != null,
            "free export: preset cannot silently hide a free pose");
        foreach (var bad in new[] { shot with { OffsetX = float.NaN }, shot with { Near = -1 }, shot with { Fov = 180 } })
            Check((request with { CameraKeys = new() { new(9f, LapEdits.Free, bad) } }).ValidateCameras(66f) != null,
                "free export: invalid pose/lens refused before launching renderer");
        Check((request with { CameraKeys = new() { new(12f, LapEdits.Free, shot), new(9f, LapEdits.Free, shot) } }).ValidateCameras(66f) != null
            && (request with { CameraKeys = new() { new(9f, LapEdits.Free, shot), new(9f, LapEdits.Auto) } }).ValidateCameras(66f) != null,
            "free export: unsorted and duplicate camera times refused");
        Check((request with { CameraKeys = new() { new(float.NaN, LapEdits.Free, shot) } }).ValidateCameras(66f) != null
            && (request with { CameraKeys = new() { new(80f, LapEdits.Free, shot) } }).ValidateCameras(66f) != null,
            "free export: non-finite and out-of-recording times refused");
        Check((request with { CameraKeys = new() { new(0f, LapEdits.TopDown), new(12f, LapEdits.Free, shot), new(20f, LapEdits.Auto) } }).ValidateCameras(66f) == null,
            "free export: mixed free and preset shots share the request timeline");
    }
}
