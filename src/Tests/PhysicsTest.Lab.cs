using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CCCPRacing.Car;
using CCCPRacing.Lab;
using Godot;

namespace CCCPRacing.Tests;
public partial class PhysicsTest
{
    /// <summary>A short drive on flat ground: accelerate, turn with the handbrake, brake. The numbers that tell two parameter sets apart.</summary>
    float[] LabDrive(CarParams p, Action<CarSim> beforeDriving = null)
    {
        var car = new CarSim(p, new FlatGround()) { Pos = new Vector3(0, .8f, 0), WorldMin = -2000, WorldMax = 2000 };
        beforeDriving?.Invoke(car);
        for (int i = 0; i < 240 * 14; i++)
        {
            float t = i / 240f;
            if (t < 5f) car.SetControls(1, 0, 0, false, false);
            else if (t < 9f) car.SetControls(0.6f, 0, 0.7f, t > 7f && t < 8f, false);
            else car.SetControls(0, 1, -0.3f, false, false);
            car.Step(Dt);
        }
        return new[] { car.Pos.X, car.Pos.Z, car.Vel.X, car.Vel.Z, car.Rpm, car.AngVel.Y, car.Rot.GetEuler().Y };
    }

    static bool LabSame(float[] a, float[] b) { for (int i = 0; i < a.Length; i++) if (MathF.Abs(a[i] - b[i]) > 1e-4f * MathF.Max(1f, MathF.Abs(a[i]))) return false; return true; }

    void LabTests()
    {
        // The table is generated from CarParams.cs: it must name every float and Vector3 parameter, and only those.
        var exported = typeof(CarParams).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<ExportAttribute>() != null && (p.PropertyType == typeof(float) || p.PropertyType == typeof(Vector3)))
            .Select(p => p.Name).ToList();
        var tabled = CarParamDocs.Entries.Select(e => e.Name).ToList();
        Check(exported.OrderBy(x => x).SequenceEqual(tabled.OrderBy(x => x)), $"lab: CarParamDocs lists exactly the float/Vector3 parameters ({exported.Count} in the class, {tabled.Count} in the table; run tools/lab/gen_car_docs.py if they differ: {string.Join(",", exported.Except(tabled).Concat(tabled.Except(exported)))})");
        Check(tabled.Distinct().Count() == tabled.Count, "lab: no parameter is listed twice");
        Check(CarParamDocs.Entries.Count(e => e.Doc.Length > 0) > CarParamDocs.Entries.Length * 0.5, $"lab: about half or more of the parameters carry a description ({CarParamDocs.Entries.Count(e => e.Doc.Length > 0)} of {CarParamDocs.Entries.Length})");

        var p0 = new CarParams();
        var lab = new CarLab(p0);
        Check(lab.Params.Count == exported.Count + 2 * CarParamDocs.Entries.Count(e => e.Kind == "Vector3"), $"lab: every float is a slider and every Vector3 three ({lab.Params.Count})");
        Check(lab.Params.All(p => p.Min < p.Max && p.Min <= p.Default && p.Default <= p.Max), "lab: every default lies inside its range");
        Check(lab.Params.Where(p => p.Name.StartsWith("Mass") || p.Name.StartsWith("WheelRadius")).All(p => p.Min > 0), "lab: mass and wheel radius cannot reach zero");
        Check(lab.ChangedCount == 0 && lab.Diff().StartsWith("no changes"), "lab: a new lab changes nothing");

        // Moving a slider changes the live parameters; reset puts them back.
        var drag = lab.Params.First(p => p.Name == "Drag");
        float d0 = p0.Drag;
        drag.Nudge(+1, LabStep.Coarse);
        Check(p0.Drag > d0 && drag.IsChanged && lab.ChangedCount == 1, $"lab: a nudge moves the car's own parameter ({d0:F3} -> {p0.Drag:F3})");
        Check(lab.Diff().Contains("Drag") && lab.Diff().Contains("->"), "lab: the diff names what changed");
        for (int i = 0; i < 500; i++) drag.Nudge(+1, LabStep.Coarse);
        Check(MathF.Abs(p0.Drag - drag.Max) < 1e-5f, "lab: a value stops at the end of its range");
        drag.Reset();
        Check(p0.Drag == d0 && lab.ChangedCount == 0, "lab: reset gives back the exact default");
        var inertiaY = lab.Params.First(p => p.Name == "Inertia.1");
        var inertia0 = p0.Inertia;
        inertiaY.Nudge(+1, LabStep.Normal);
        Check(p0.Inertia.Y > inertia0.Y && p0.Inertia.X == inertia0.X && p0.Inertia.Z == inertia0.Z, "lab: a Vector3 parameter moves one component at a time");
        lab.ResetAll();
        Check(p0.Inertia == inertia0 && lab.ChangedCount == 0, "lab: reset all restores the vector too");

        // The same car with the lab built and untouched drives exactly as one without it; after moving and resetting everything it does too.
        var plain = LabDrive(new CarParams());
        var labbed = new CarParams(); var labbedLab = new CarLab(labbed);
        Check(LabSame(plain, LabDrive(labbed)), "lab: an untouched lab does not change the drive");
        foreach (var p in labbedLab.Params) p.Nudge(+1, LabStep.Coarse);
        labbedLab.ResetAll();
        Check(LabSame(plain, LabDrive(labbed)), "lab: after moving and resetting everything the drive is exactly the original");

        // Which parameters change the drive (the rest are for other systems: fuel, or only used in situations this drive does not make).
        var inert = new List<string>();
        foreach (var info in lab.Params.Where(p => !p.Name.StartsWith("Inertia.") || p.Name == "Inertia.1"))
        {
            var cp = new CarParams(); var cl = new CarLab(cp, null);
            var param = cl.Params.First(x => x.Name == info.Name);
            var geometry = new HashSet<string> { "FrontZ", "RearZ", "HalfTrack", "AttachY", "RestLength" };
            CarSim carRef = null;
            var cl2 = new CarLab(cp, () => carRef?.RefreshGeometry());
            param = cl2.Params.First(x => x.Name == info.Name);
            float[] changed = LabDrive(cp, car => { carRef = car; param.Assign(param.Default + (param.Max - param.Default) * 0.6f); if (geometry.Contains(info.Name)) car.RefreshGeometry(); });
            if (LabSame(plain, changed)) inert.Add(info.Name);
        }
        GD.Print($"lab: {inert.Count} of {lab.Params.Count} parameters leave this drive unchanged: {string.Join(", ", inert)}");
        Check(inert.Count < lab.Params.Count * 0.4f, $"lab: most parameters change the drive ({lab.Params.Count - inert.Count} of {lab.Params.Count} do)");
        foreach (var must in new[] { "Mass", "Drag", "FrontGrip", "RearGrip", "SpringK", "MaxSteer", "BrakeForce", "HandbrakeRotate", "MaxTorque", "FinalDrive" })
            Check(!inert.Contains(must), $"lab: {must} changes the drive");

        // The diesel preset names real parameters, inside their slider ranges, and describes a diesel: a lower redline, the peak torque low, more of it.
        {
            var dcp = new CarParams(); var dlab = new CarLab(dcp);
            bool all = true, inRange = true;
            foreach (var (name, value) in CCCPRacing.Race.RaceScene.DieselPreset)
            {
                var p = dlab.Params.FirstOrDefault(x => x.Name == name);
                all &= p != null; inRange &= p != null && value >= p.Min && value <= p.Max;
            }
            Check(all && inRange, "lab: the diesel preset names real parameters, inside their ranges");
            float V(string n) => CCCPRacing.Race.RaceScene.DieselPreset.First(x => x.Name == n).Value;
            Check(V("Redline") < dcp.Redline && V("PeakRpm") < dcp.PeakRpm && V("MaxTorque") > dcp.MaxTorque && V("FinalDrive") < dcp.FinalDrive && V("ShiftUp") < V("Redline"),
                "lab: the diesel preset has the lower redline, the lower torque peak, more torque and the taller gearing");
        }

        // The wheels' places follow a geometry parameter once the car is told.
        var gp = new CarParams(); CarSim gcar = null;
        var glab = new CarLab(gp, () => gcar.RefreshGeometry());
        gcar = new CarSim(gp, new FlatGround());
        float track0 = gcar.Wheels[1].Local.X;
        glab.Params.First(p => p.Name == "HalfTrack").Nudge(+1, LabStep.Coarse);
        Check(gcar.Wheels[1].Local.X > track0 + 0.01f && gcar.Wheels[0].Local.X < -track0 - 0.01f, "lab: widening the track moves the wheels at once");
        glab.ResetAll();
        Check(MathF.Abs(gcar.Wheels[1].Local.X - track0) < 1e-6f, "lab: resetting the track puts the wheels back");

        // Saved values: a round trip through a file, unknown or damaged entries ignored.
        string dir = ProjectSettings.GlobalizePath("user://lab-test/"); Directory.CreateDirectory(dir);
        string path = dir + "lab.cfg";
        var sp = new CarParams(); var slab = new CarLab(sp);
        slab.Params.First(p => p.Name == "Mass").Assign(1234f);
        slab.Params.First(p => p.Name == "Inertia.2").Assign(777f);
        var cfg = new ConfigFile(); slab.Save(cfg); cfg.SetValue(CarLab.Section, "NoSuchParameter", 5f); cfg.SetValue(CarLab.Section, "Drag", "text"); cfg.Save(path);
        var lp = new CarParams(); var llab = new CarLab(lp);
        var cfg2 = new ConfigFile(); cfg2.Load(path);
        int applied = llab.Load(cfg2);
        Check(applied == 2 && lp.Mass == 1234f && lp.Inertia.Z == 777f && lp.Drag == new CarParams().Drag, $"lab: saved values load, unknown and damaged ones are ignored ({applied} applied)");
        Check(llab.ChangedCount == 2, "lab: only what differs from the defaults is saved");
        File.Delete(path);
    }
}
