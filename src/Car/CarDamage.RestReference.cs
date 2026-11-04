using System.Linq;

namespace CCCPRacing.Car;
public partial class CarDamage
{
    // Developer model-migration reference: the same production rest mesh used by tape signatures.
    internal object RestReference() => new
    {
        Signature = Signature.ToString("x16"),
        Surfaces = AllSurfs().Select(s => new {
            Material = s.Mat?.ResourceName,
            Positions = s.Orig.Select(p => new[] { p.X, p.Y, p.Z }).ToArray(),
            Normals = s.Normal.Select(p => new[] { p.X, p.Y, p.Z }).ToArray(),
        }).ToArray(),
    };
}
