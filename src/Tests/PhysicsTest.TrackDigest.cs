using System;
using System.Linq;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>--track-digest: prints, per track, a hash of everything the track builder placed (props, obstacles) and the tree counts, so that a change to one track's trees can be shown to leave the others exactly as they were.</summary>
    void TrackDigest()
    {
        foreach (string id in new[] { "ilyich", "siberia", "atomgrad", "bonus" })
        {
            var t = TrackBuilder.Build(TrackDef.Load($"res://tracks/{id}.json"));
            ulong h = 14695981039346656037UL;
            void Mix(float v) { int b = BitConverter.SingleToInt32Bits(MathF.Round(v * 1000f) / 1000f); for (int k = 0; k < 4; k++) { h ^= (byte)(b >> (8 * k)); h *= 1099511628211UL; } }
            void MixS(string s) { foreach (char c in s) { h ^= c; h *= 1099511628211UL; } }
            foreach (var p in t.Props) { MixS(p.Type); Mix(p.Position.X); Mix(p.Position.Z); Mix(p.Rotation); Mix(p.Size); }
            ulong props = h; h = 14695981039346656037UL;
            foreach (var o in t.Obstacles) { Mix(o.Center.X); Mix(o.Center.Y); Mix(o.Radius); Mix(o.BreakAt); }
            ulong obstacles = h;
            var kinds = t.Props.Where(p => p.Type is "birch" or "pine" or "spruce" or "redpine" or "deadtree").GroupBy(p => p.Type).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}");
            GD.Print($"TRACKDIGEST {id} props={t.Props.Count}/{props:x16} obstacles={t.Obstacles.Count}/{obstacles:x16} trees={string.Join(",", kinds)} bushes={t.Bushes.Count} tufts={t.Tufts.Count}");
        }
    }
}
