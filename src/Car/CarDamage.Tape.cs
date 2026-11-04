using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Car;

public partial class CarDamage
{
    // The player's geometry is carried by the car tape, alongside its 30 Hz pose.
    // Version 1: original vertex signature + lossless offsets; named detachable parts.
    public const int TapeVersion = 1;
    public bool TapeDriven { get; private set; }
    Geometry _tapeGeometry, _appliedGeometry;
    ulong? _tapeSignature;
    float _chipClock;
    bool _capturedEnabled, _liveEnabled;
    readonly List<Chip> _chips = new();

    public sealed record Geometry(ulong Signature, byte[] Data);
    public sealed record PartPose(bool Detached, Transform3D Pose);
    public sealed record Chip(float Born, Vector3 Pos, Vector3 Vel, Color Color,
        float Alpha, float Size0, float Size1, float Life, float Gravity, float Drag, bool Solid);
    public sealed record State(Geometry Shape, PartPose[] Parts, Chip[] Chips, float ChipClock);

    void DamageChanged() => _tapeGeometry = null;

    ulong Signature
    {
        get
        {
            if (_tapeSignature is ulong signature) return signature;
            var h = TapeHash.Start().Add(TapeVersion).Add(_parts.Count);
            foreach (var p in _parts) foreach (char c in p.Name) h = h.Add(c);
            foreach (var s in AllSurfs())
            {
                h = h.Add(s.Orig.Count);
                foreach (var v in s.Orig) h = h.Add(v.X).Add(v.Y).Add(v.Z);
            }
            _tapeSignature = h.Value;
            return h.Value;
        }
    }

    public State CaptureTapeState()
    {
        if (_tapeGeometry == null || _capturedEnabled != Enabled)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(Enabled); w.Write(Dents); w.Write(_glass); w.Write(_crackStage);
            foreach (var s in AllSurfs())
            {
                w.Write(s.Off.Length);
                foreach (var v in s.Off) Write(w, v);
            }
            foreach (var p in _parts)
            { w.Write(p.Name); w.Write(p.Damage); w.Write((byte)p.State); w.Write(p.HangSide); }
            _tapeGeometry = new Geometry(Signature, ms.ToArray());
            _capturedEnabled = Enabled;
        }
        return new State(_tapeGeometry, _parts.Select(p => new PartPose(p.State == PartState.Off,
            p.State == PartState.Off ? p.Node.GlobalTransform : p.Node.Transform)).ToArray(),
            _chips.ToArray(), _chipClock);
    }

    // Validate the entire geometry before touching the live mesh (a model change is safe).
    bool Compatible(Geometry geometry)
    {
        if (geometry.Signature != Signature) return false;
        try
        {
            using var r = new BinaryReader(new MemoryStream(geometry.Data, false));
            r.ReadBoolean(); int dents = r.ReadInt32(); float glass = r.ReadSingle(); int crack = r.ReadInt32();
            if (dents < 0 || !float.IsFinite(glass) || glass < 0) return false;
            if (crack < 0 || crack > 2) return false;
            foreach (var s in AllSurfs())
            {
                if (r.ReadInt32() != s.Off.Length) return false;
                for (int i = 0; i < s.Off.Length; i++) if (!ReadVector(r).IsFinite()) return false;
            }
            foreach (var p in _parts)
            {
                if (r.ReadString() != p.Name) return false;
                float damage = r.ReadSingle();
                if (!float.IsFinite(damage) || damage < 0 || r.ReadByte() > 2 || !float.IsFinite(r.ReadSingle())) return false;
            }
            return r.BaseStream.Position == r.BaseStream.Length;
        }
        catch (Exception e) when (e is IOException or ArgumentException or FormatException or OverflowException) { return false; }
    }

    public bool ApplyTapeState(State a, State b, float u, TyreEffects fx = null)
    {
        if (a == null || b == null || !PartCountFits(a) || !PartCountFits(b)) return false;
        if (a.Shape.Signature != Signature)
        {
            var migrated = RetargetLegacyModel(a.Shape);
            if (migrated == null) return false;
            a = a with { Shape = migrated };
        }
        if (!ReferenceEquals(_appliedGeometry, a.Shape))
        {
            if (!Compatible(a.Shape)) return false;
            using var r = new BinaryReader(new MemoryStream(a.Shape.Data, false));
            if (!TapeDriven) _liveEnabled = Enabled;
            Enabled = r.ReadBoolean(); Dents = r.ReadInt32(); _glass = r.ReadSingle(); _crackStage = r.ReadInt32();
            foreach (var s in AllSurfs())
            { r.ReadInt32(); for (int i = 0; i < s.Off.Length; i++) s.Off[i] = ReadVector(r); }
            foreach (var p in _parts)
            {
                r.ReadString(); p.Damage = r.ReadSingle(); p.State = (PartState)r.ReadByte(); p.HangSide = r.ReadSingle();
                p.Node.TopLevel = p.State == PartState.Off; p.Node.Visible = !p.Hidden;
                RebuildPart(p);
            }
            RebuildBody();
            _crack.Visible = _crackStage > 0;
            if (_crackStage > 0) ((StandardMaterial3D)_crack.MaterialOverride).AlbedoTexture = _crackTex[_crackStage - 1];
            _appliedGeometry = a.Shape; _tapeGeometry = a.Shape; _capturedEnabled = Enabled;
        }
        TapeDriven = true;
        for (int i = 0; i < _parts.Count; i++)
        {
            var p = _parts[i]; PartPose pa = PoseOf(a, i), pb = PoseOf(b, i); var pose = pa.Pose;
            // A detachment is a discrete change of coordinate space, never interpolate across it.
            if (pa.Detached && pb.Detached) pose = pose.InterpolateWith(pb.Pose, u);
            if (pa.Detached) p.Node.GlobalTransform = pose;
            else p.Node.Transform = pose;
        }
        if (fx != null)
        {
            fx.ClearDamageReplay();
            foreach (var chip in a.Chips)
            {
                float age = a.ChipClock - chip.Born + u / Ghost.SampleRate;
                if (age < 0 || age >= chip.Life + (chip.Solid ? 2.5f : 0)) continue;
                fx.BeginDamageReplay(age);
                fx.Burst(chip.Pos, chip.Vel, chip.Color, chip.Alpha, chip.Size0, chip.Size1,
                    chip.Life, chip.Gravity, chip.Drag, chip.Solid);
                fx.EndReplay();
            }
        }
        return true;
    }

    /// <summary>Recordings from before the rear wings (or before the duck-tail) have fewer parts, the first ones in the same order: the parts they lack sit at home.</summary>
    const int LegacyParts = 4;
    bool PartCountFits(State s) => s.Parts.Length <= _parts.Count && s.Parts.Length >= LegacyParts;
    PartPose PoseOf(State s, int i) => i < s.Parts.Length ? s.Parts[i] : new PartPose(false, new Transform3D(Basis.Identity, _parts[i].Centre));

    public void ReleaseTape(TyreEffects fx = null)
    {
        if (!TapeDriven) return;
        TapeDriven = false; Enabled = _liveEnabled; _appliedGeometry = null;
        Reset(); fx?.ClearDamageReplay();
    }

    void DamageBurst(TyreEffects fx, Vector3 pos, Vector3 vel, Color color, float alpha,
        float size0, float size1, float life, float gravity, float drag, bool solid)
    {
        _chips.Add(new Chip(_chipClock, pos, vel, color, alpha, size0, size1, life, gravity, drag, solid));
        fx?.Burst(pos, vel, color, alpha, size0, size1, life, gravity, drag, solid);
    }

    internal static void Write(BinaryWriter w, Vector3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
    internal static Vector3 ReadVector(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    internal static void Write(BinaryWriter w, Transform3D t) { Write(w, t.Basis.X); Write(w, t.Basis.Y); Write(w, t.Basis.Z); Write(w, t.Origin); }
    internal static Transform3D ReadTransform(BinaryReader r) => new(new Basis(ReadVector(r), ReadVector(r), ReadVector(r)), ReadVector(r));
}
