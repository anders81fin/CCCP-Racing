using System;
using System.Linq;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    static string WingsOf(CarVisual v) => string.Join(",", v.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>()
        .Where(m => m.Name.ToString().Contains("rear wing", StringComparison.OrdinalIgnoreCase)).Select(m => m.Name.ToString().Replace("rear wing ", "") + (m.IsVisibleInTree() ? "+" : "-")));

    /// <summary>Two cars of different fuels each keep their own rear wing (the model's meshes and materials are shared by every car).</summary>
    void CarWingShareTest()
    {
        var holder = new Node3D(); AddChild(holder);
        var a = CarVisual.Create(new CarParams(), Colors.Red); holder.AddChild(a);
        var b = CarVisual.Create(new CarParams(), Colors.Red); holder.AddChild(b);
        FuelGrades.ApplyVisual(a, FuelGrade.Diesel);
        FuelGrades.ApplyVisual(b, FuelGrade.Petrol);
        string wa = WingsOf(a), wb = WingsOf(b);
        GD.Print($"  wings: diesel car [{wa}] petrol car [{wb}]");
        Check(wa.Contains("duck+") && !wa.Contains("low+") && wb.Contains("low+") && !wb.Contains("duck+"), $"car wings: each car wears its own fuel's wing (diesel {wa}; petrol {wb})");
        // The paint materials are shared too: a car made while another wears the jet fuel's red must still have the factory paint.
        static System.Collections.Generic.Dictionary<string, Color> Paints(CarVisual v)
        {
            var d = new System.Collections.Generic.Dictionary<string, Color>();
            foreach (var mi in v.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
                for (int si = 0; mi.Mesh != null && si < mi.Mesh.GetSurfaceCount(); si++)
                    if (mi.Mesh.SurfaceGetMaterial(si) is StandardMaterial3D m && m.ResourceName.StartsWith("Paint_Olive")) d[m.ResourceName] = m.AlbedoColor;
            return d;
        }
        var factory = Paints(a);
        a.FuelPaint = Colors.Red;
        var c = CarVisual.Create(new CarParams(), Colors.Red); holder.AddChild(c);
        c.Repaint();
        var now = Paints(c);
        bool same = factory.Count > 0 && factory.All(kv => now.TryGetValue(kv.Key, out var col) && col.IsEqualApprox(kv.Value));
        Check(same, $"car paint: a car made while another one wears red has the factory paint ({factory.Count} paint materials)");
        a.FuelPaint = null;
        holder.Free();
    }
}
