using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using CCCPRacing.Car;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Race
{
    public partial class Ghost
    {
        internal CarDamage.State LegacyReviewState(int frame) => _damage[frame];
    }
}
namespace CCCPRacing.Tests
{
    public partial class PhysicsTest
    {
        static (byte[] Header, Vector3[][] Offsets, byte[] Parts) ReadReviewGeometry(CarDamage.Geometry geometry, int surfaces)
        {
            using var r = new BinaryReader(new MemoryStream(geometry.Data));
            var header = r.ReadBytes(13); var offsets = new Vector3[surfaces][];
            for (int s = 0; s < surfaces; s++)
            {
                int count = r.ReadInt32(); offsets[s] = new Vector3[count];
                for (int i = 0; i < count; i++) offsets[s][i] = new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            }
            return (header, offsets, r.ReadBytes((int)(r.BaseStream.Length - r.BaseStream.Position)));
        }
        void LegacyDamageModelTest(string path)
        {
            byte[] original = File.ReadAllBytes(path);
            var parameters = new CarParams(); var ground = new FlatGround();
            var ghost = Ghost.Load(path, parameters, ground);
            var visual = CarVisual.Create(parameters, Colors.Red); AddChild(visual);
            var fx = new TyreEffects(); fx.Init(TrackBuilder.Build(TrackDef.Load("res://tracks/ilyich.json"))); AddChild(fx);
            Check(ghost?.HasDamage == true && visual.Damage != null, "legacy model: real old damaged driving recording loads");
            if (ghost?.HasDamage != true) return;
            using var map = JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://assets/cars/pyatyorka2105-damage-map.json"));
            string sourceSig = ghost.LegacyReviewState(0).Shape.Signature.ToString("x16");
            var entry = map.RootElement.EnumerateArray().First(m => m.GetProperty("SourceSignature").GetString() == sourceSig);
            int[] fromSurface = entry.TryGetProperty("SourceSurface", out var ss) ? ss.EnumerateArray().Select(v => v.GetInt32()).ToArray() : null;
            int oldSurfaces = entry.GetProperty("SourceCounts").GetArrayLength();
            var indices = entry.GetProperty("TargetIndices").EnumerateArray().Select(a => a.EnumerateArray().Select(v => v.GetInt32()).ToArray()).ToArray();
            bool applied = true, offsets = true, state = true, poses = true;
            foreach (int frame in new[] { 0, 10, 25, 87, 155, 209, 40, 0, 155, 120, 209 })
            {
                var old = ghost.LegacyReviewState(frame);
                applied &= ghost.ApplyDamage(frame / 30f + .00001f, visual.Damage, fx);
                var current = visual.Damage.CaptureTapeState();
                var a = ReadReviewGeometry(old.Shape, oldSurfaces); var b = ReadReviewGeometry(current.Shape, indices.Length);
                // The old part records stay as they were; the parts the old model did not have (the rear wings) follow them undamaged.
                state &= a.Header.SequenceEqual(b.Header) && b.Parts.Take(a.Parts.Length).SequenceEqual(a.Parts);
                for (int s = 0; s < indices.Length; s++)
                {
                    int src = fromSurface?[s] ?? s;
                    for (int i = 0; i < indices[s].Length; i++)
                        offsets &= b.Offsets[s][i] == (src < 0 || indices[s][i] < 0 ? Vector3.Zero : a.Offsets[src][indices[s][i]]);
                }
                for (int i = 0; i < old.Parts.Length; i++)
                    poses &= old.Parts[i].Detached == current.Parts[i].Detached
                        && old.Parts[i].Pose.Origin.DistanceTo(current.Parts[i].Pose.Origin) < .001f;
            }
            Check(applied, "legacy model: production replay/seek/loop accepts known legacy damage on repaired model");
            Check(offsets, "legacy model: actual rebuilt offset stream preserves all mapped original offsets; new surfaces have zero old offsets");
            Check(state, "legacy model: enabled/dents/glass/crack and named bumper/lamp damage states remain byte-identical");
            Check(poses, "legacy model: original detachable part poses survive retarget, backward seek and loop");
            Check(visual.Damage.LegacyShapeCacheCount is > 0 and <= 4, "legacy model: converted geometry cache stays bounded across seeks");
            visual.Damage.ReleaseTape(fx);
            Check(!visual.Damage.TapeDriven && !visual.Damage.IsDamaged && visual.Damage.HeadlightWorks(true), "legacy model: exit restores clean repaired model and working lamps");
            var bad = ghost.LegacyReviewState(40); bad = bad with { Shape = bad.Shape with { Signature = 123 } };
            Check(!visual.Damage.ApplyTapeState(bad, bad, 0) && !visual.Damage.IsDamaged, "legacy model: unrelated signature is still rejected safely");
            Check(ghost.ToBytes().SequenceEqual(original) && File.ReadAllBytes(path).SequenceEqual(original), "legacy model: original loaded and on-disk recording are unchanged");
            visual.Free(); fx.Free();
        }
    }
}
