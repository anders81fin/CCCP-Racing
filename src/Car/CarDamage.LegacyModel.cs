using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;

namespace CCCPRacing.Car;
public partial class CarDamage
{
    const string LegacyRestMapPath = "res://assets/cars/pyatyorka2105-damage-map.json";
    sealed record RestMap(string SourceSignature, string TargetSignature, int[] SourceCounts, int[][] TargetIndices, int[] SourceSurface = null);
    static RestMap[] _restMaps;
    static bool _restMapRead;
    readonly Dictionary<Geometry, Geometry> _legacyShapes = new(ReferenceEqualityComparer.Instance);
    readonly Queue<Geometry> _legacyShapeOrder = new();
    bool _legacyModelReported;
    internal int LegacyShapeCacheCount => _legacyShapes.Count;

    // This is an explicit migration of one authored rest model, not permission to
    // accept arbitrary old/malformed geometry. Four converted shapes bound memory.
    Geometry RetargetLegacyModel(Geometry source)
    {
        if (source == null || source.Signature == Signature) return source;
        if (_legacyShapes.TryGetValue(source, out var found)) return found;
        if (!_restMapRead)
        {
            _restMapRead = true;
            try
            {
                if (Godot.FileAccess.FileExists(LegacyRestMapPath))
                    _restMaps = JsonSerializer.Deserialize<RestMap[]>(Godot.FileAccess.GetFileAsString(LegacyRestMapPath));
            }
            catch (Exception e) { GD.PushWarning("Car tape rest map unavailable: " + e.Message); }
        }
        // One map per known older rest model (each older body maps to the current one); the one whose source is this recording's shape applies.
        var map = _restMaps?.FirstOrDefault(m => ulong.TryParse(m.SourceSignature, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong o) && o == source.Signature);
        if (map == null || !ulong.TryParse(map.SourceSignature, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong old)
            || !ulong.TryParse(map.TargetSignature, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong target)
            || source.Signature != old || Signature != target || map.SourceCounts == null || map.TargetIndices == null) return null;
        var surfaces = AllSurfs().ToArray();
        if (surfaces.Length != map.TargetIndices.Length) return null;
        // The surface of the old model each new surface takes its offsets from (-1: none, a part the old model did not have); by default the same index.
        int[] from = map.SourceSurface ?? Enumerable.Range(0, surfaces.Length).ToArray();
        if (from.Length != surfaces.Length) return null;
        try
        {
            using var input = new BinaryReader(new MemoryStream(source.Data, false));
            using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
            writer.Write(input.ReadBytes(13)); // enabled, dents, glass, crack: verified by Compatible below
            var offs = new Vector3[map.SourceCounts.Length][];
            for (int s = 0; s < offs.Length; s++)
            {
                int count = input.ReadInt32();
                if (count != map.SourceCounts[s] || count < 0 || count > 200000) return null;
                offs[s] = new Vector3[count];
                for (int i = 0; i < count; i++) { offs[s][i] = ReadVector(input); if (!offs[s][i].IsFinite()) return null; }
            }
            for (int s = 0; s < surfaces.Length; s++)
            {
                var indices = map.TargetIndices[s]; int src = from[s];
                if (indices == null || indices.Length != surfaces[s].Orig.Count || src >= offs.Length || src < -1) return null;
                writer.Write(indices.Length);
                foreach (int i in indices)
                {
                    if (i < -1 || src >= 0 && i >= offs[src].Length) return null;
                    Write(writer, src < 0 || i < 0 ? Vector3.Zero : offs[src][i]);
                }
            }
            // Named detachable-part damage/state is unchanged by this body-only repair; parts the old model did not have (the rear wings) start undamaged.
            int named = 0;
            var tail = input.ReadBytes(checked((int)(input.BaseStream.Length - input.BaseStream.Position)));
            using (var t = new BinaryReader(new MemoryStream(tail, false)))
                while (t.BaseStream.Position < t.BaseStream.Length)
                {
                    if (named >= _parts.Count || t.ReadString() != _parts[named].Name) return null;
                    t.ReadSingle(); t.ReadByte(); t.ReadSingle(); named++;
                }
            writer.Write(tail);
            for (int i = named; i < _parts.Count; i++) { writer.Write(_parts[i].Name); writer.Write(0f); writer.Write((byte)0); writer.Write(0f); }
            var migrated = new Geometry(target, output.ToArray());
            if (!Compatible(migrated)) return null;
            if (_legacyShapeOrder.Count >= 4) _legacyShapes.Remove(_legacyShapeOrder.Dequeue());
            _legacyShapes[source] = migrated; _legacyShapeOrder.Enqueue(source);
            if (!_legacyModelReported)
            {
                _legacyModelReported = true;
                GD.Print("Car tape: known legacy damage retargeted to repaired body model; original recording unchanged.");
            }
            return migrated;
        }
        catch (Exception e) when (e is IOException or ArgumentException or OverflowException) { return null; }
    }
}
