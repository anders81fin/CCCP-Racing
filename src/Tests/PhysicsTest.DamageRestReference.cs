using System;
using System.IO;
using System.Text.Json;
using CCCPRacing.Car;
using Godot;

namespace CCCPRacing.Tests;
public partial class PhysicsTest
{
    void DamageRestReferenceTest(string path)
    {
        var car = CarVisual.Create(new CarParams(), Colors.Red); AddChild(car);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        File.WriteAllText(path, JsonSerializer.Serialize(car.Damage.RestReference()));
        GD.Print("Damage rest reference: " + Path.GetFullPath(path)); car.Free();
    }
}
