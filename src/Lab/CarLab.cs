using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Godot;
using CCCPRacing.Car;

namespace CCCPRacing.Lab;

/// <summary>
/// The car as a LAB target: every float and Vector3 parameter of <see cref="CarParams"/> (the list and descriptions come from
/// <see cref="CarParamDocs"/>, generated from the class) as a slider on the live parameters. The simulation reads them every step,
/// so a change shows in the next one; only the wheels' places are worked out once and have a refresh.
/// </summary>
public sealed class CarLab
{
    public const string Section = "car";
    public CarParams Target { get; }
    public IReadOnlyList<LabParam> Params => _params;
    public IReadOnlyList<string> Groups { get; }
    readonly List<LabParam> _params = new();

    /// <summary>Parameters that must stay above zero (they divide, or a zero is not a car): their range starts at a tenth of the default.</summary>
    static readonly HashSet<string> Positive = new()
    {
        "Mass", "Inertia", "WheelRadius", "RestLength", "MaxTravel", "SpringK", "PeakSlip", "DriveWheelInertia", "EngineInertia",
        "IdleRpm", "PeakRpm", "Redline", "ShiftUp", "ShiftDown", "FinalDrive", "TankLitres", "ReverseGear", "Efficiency", "OverdriveGearing",
        "ShiftTime", "ClutchDumpTime", "LimiterOn", "LimiterHold", "LimiterCut", "ReboundKnee", "MaxSteer", "SteerRate", "PadSteerRate",
    };
    static readonly HashSet<string> Geometry = new() { "FrontZ", "RearZ", "HalfTrack", "AttachY", "RestLength" };

    /// <param name="geometryChanged">Called after a wheel-place parameter moved (CarSim.RefreshGeometry).</param>
    public CarLab(CarParams target, Action geometryChanged = null)
    {
        Target = target;
        var props = typeof(CarParams).GetProperties(BindingFlags.Public | BindingFlags.Instance).ToDictionary(p => p.Name);
        var groups = new List<string>();
        foreach (var (group, name, kind, doc) in CarParamDocs.Entries)
        {
            if (!props.TryGetValue(name, out var prop)) continue;      // (the lab-tests name the mismatch)
            if (!groups.Contains(group)) groups.Add(group);
            Action changed = Geometry.Contains(name) ? geometryChanged : null;
            if (kind == "float")
                Add(group, name, name, doc, (float)prop.GetValue(target), () => (float)prop.GetValue(target), v => prop.SetValue(target, v), Positive.Contains(name), changed);
            else
                for (int i = 0; i < 3; i++)
                {
                    int axis = i;
                    var vec = (Vector3)prop.GetValue(target);
                    Add(group, name + "." + axis, name + " " + "XYZ"[axis], doc, vec[axis],
                        () => ((Vector3)prop.GetValue(target))[axis],
                        v => { var cur = (Vector3)prop.GetValue(target); cur[axis] = v; prop.SetValue(target, cur); },
                        Positive.Contains(name), changed);
                }
        }
        Groups = groups;
    }

    void Add(string group, string name, string label, string doc, float def, Func<float> get, Action<float> set, bool positive, Action changed)
    {
        float lo, hi;
        if (def == 0f) { lo = -1f; hi = 1f; }
        else
        {
            float a = def * 3f, b = positive ? def * 0.1f : 0f;
            lo = MathF.Min(a, b); hi = MathF.Max(a, b);
        }
        _params.Add(new LabParam { Group = group, Name = name, Label = label.ToUpperInvariant(), Doc = doc, Default = def, Min = lo, Max = hi, Get = get, Set = set, Changed = changed });
    }

    public IEnumerable<LabParam> InGroup(string group) => _params.Where(p => p.Group == group);
    public int ChangedCount => _params.Count(p => p.IsChanged);

    public void ResetAll() { foreach (var p in _params) p.Reset(); }

    /// <summary>What differs from the defaults, as text a person can read and copy into CarParams.cs.</summary>
    public string Diff()
    {
        var sb = new StringBuilder();
        foreach (var p in _params.Where(p => p.IsChanged))
            sb.Append(p.Name).Append(' ').Append(p.DefaultText).Append(" -> ").Append(p.Text).Append('\n');
        return sb.Length == 0 ? "no changes from the defaults\n" : sb.ToString();
    }

    public void Save(ConfigFile cfg)
    {
        if (cfg.HasSection(Section)) cfg.EraseSection(Section);
        foreach (var p in _params.Where(p => p.IsChanged)) cfg.SetValue(Section, p.Name, p.Value);
    }

    /// <summary>Puts the saved values on the parameters (unknown or non-finite ones are ignored); returns how many were applied.</summary>
    public int Load(ConfigFile cfg)
    {
        int n = 0;
        if (!cfg.HasSection(Section)) return 0;
        foreach (var p in _params)
            if (cfg.HasSectionKey(Section, p.Name) && cfg.GetValue(Section, p.Name).VariantType == Variant.Type.Float)
            { p.Assign((float)cfg.GetValue(Section, p.Name).AsDouble()); n++; }
        return n;
    }
}
