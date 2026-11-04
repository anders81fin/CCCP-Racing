using CCCPRacing.Race;

namespace CCCPRacing.Tests;

// T2: the storage seams. The byte encoding stays the codec's (EncodeTape/DecodeTape seams are read at call time).
public partial class PhysicsTest
{
    partial void InstallRealTapeT2()
    {
        var store = new WorldTapeStorage(t => TapeSeams.EncodeTape(t), b => TapeSeams.DecodeTape(b));
        TapeSeams.SavePair = store.SavePair;
        TapeSeams.LoadTape = store.Load;
        TapeSeams.CleanStale = WorldTapeFile.CleanStale;
        TapeSeams.RealOps = () => WorldTapeRealOps.Instance;
        foreach (var n in new[] { "SavePair", "LoadTape", "CleanStale", "RealOps" }) TapeSeams.Real.Add(n);
        TapeStorageTests();
    }
}
