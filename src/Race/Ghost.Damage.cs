using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using CCCPRacing.Car;
using Godot;

namespace CCCPRacing.Race;

public partial class Ghost
{
    const uint MagicV4 = 0x34434343; // CCC4: V3 samples followed by compressed, versioned damage states
    readonly List<CarDamage.State> _damage = new();
    bool _damageWarning;
    public bool HasDamage => _damage.Count == Count && Count > 0;

    /// <summary>Absolute state, so a mid-lap start, backward seek and loop need no collision history.</summary>
    public bool ApplyDamage(float clock, CarDamage damage, TyreEffects fx = null)
    {
        if (!HasDamage || damage == null) return false;
        Index(clock, out int i, out int j, out float u);
        if (damage.ApplyTapeState(_damage[i], _damage[j], u, fx)) return true;
        damage.ReleaseTape(fx);
        if (!_damageWarning)
        {
            _damageWarning = true;
            GD.PushWarning("Car tape: damage model/version mismatch; recorded damage disabled. Car pose and world tape remain usable.");
        }
        return false;
    }

    void WriteDamage(System.IO.BinaryWriter output)
    {
        using var compressed = new MemoryStream();
        using (var zip = new BrotliStream(compressed, CompressionLevel.Fastest, true))
        using (var w = new BinaryWriter(zip))
        {
            w.Write(CarDamage.TapeVersion);
            var ids = new Dictionary<CarDamage.Geometry, int>();
            foreach (var state in _damage) if (!ids.ContainsKey(state.Shape)) ids.Add(state.Shape, ids.Count);
            w.Write(ids.Count);
            foreach (var shape in ids.Keys)
            { w.Write(shape.Signature); w.Write(shape.Data.Length); w.Write(shape.Data); }
            w.Write(_damage.Count);
            foreach (var state in _damage)
            {
                w.Write(ids[state.Shape]); w.Write(state.Parts.Length);
                foreach (var p in state.Parts) { w.Write(p.Detached); CarDamage.Write(w, p.Pose); }
                w.Write(state.ChipClock); w.Write(state.Chips.Length);
                foreach (var c in state.Chips)
                {
                    w.Write(c.Born); CarDamage.Write(w, c.Pos); CarDamage.Write(w, c.Vel);
                    w.Write(c.Color.R); w.Write(c.Color.G); w.Write(c.Color.B); w.Write(c.Color.A);
                    w.Write(c.Alpha); w.Write(c.Size0); w.Write(c.Size1); w.Write(c.Life);
                    w.Write(c.Gravity); w.Write(c.Drag); w.Write(c.Solid);
                }
            }
        }
        output.Write((int)compressed.Length); output.Write(compressed.ToArray());
    }

    bool ReadDamage(byte[] bytes)
    {
        try
        {
            using var zip = new BrotliStream(new MemoryStream(bytes, false), CompressionMode.Decompress);
            using var raw = new MemoryStream();
            byte[] buffer = new byte[8192]; int read;
            while ((read = zip.Read(buffer)) > 0)
            {
                if (raw.Length + read > 256 * 1024 * 1024) return false;
                raw.Write(buffer, 0, read);
            }
            raw.Position = 0;
            using var r = new BinaryReader(raw);
            if (r.ReadInt32() != CarDamage.TapeVersion) return false;
            int shapes = r.ReadInt32(); if (shapes < 1 || shapes > Count) return false;
            var geometry = new CarDamage.Geometry[shapes];
            for (int i = 0; i < shapes; i++)
            {
                ulong signature = r.ReadUInt64(); int length = r.ReadInt32();
                if (length < 1 || length > 8 * 1024 * 1024 || length > raw.Length - raw.Position) return false;
                geometry[i] = new(signature, r.ReadBytes(length));
            }
            if (r.ReadInt32() != Count) return false;
            for (int i = 0; i < Count; i++)
            {
                int id = r.ReadInt32(), count = r.ReadInt32();
                if (id < 0 || id >= shapes || count < 0 || count > 64) return false;
                var parts = new CarDamage.PartPose[count];
                for (int k = 0; k < count; k++)
                {
                    bool detached = r.ReadBoolean(); var pose = CarDamage.ReadTransform(r);
                    if (!pose.IsFinite()) return false;
                    parts[k] = new(detached, pose);
                }
                float clock = r.ReadSingle(); int chips = r.ReadInt32();
                if (!float.IsFinite(clock) || chips < 0 || chips > 2048) return false;
                var effects = new CarDamage.Chip[chips];
                for (int k = 0; k < chips; k++)
                {
                    effects[k] = new(r.ReadSingle(), CarDamage.ReadVector(r), CarDamage.ReadVector(r),
                        new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                        r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadBoolean());
                    var c = effects[k];
                    if (!float.IsFinite(c.Born) || !c.Pos.IsFinite() || !c.Vel.IsFinite()
                        || !float.IsFinite(c.Color.R) || !float.IsFinite(c.Color.G) || !float.IsFinite(c.Color.B) || !float.IsFinite(c.Color.A)
                        || !float.IsFinite(c.Alpha) || !float.IsFinite(c.Size0) || !float.IsFinite(c.Size1)
                        || !float.IsFinite(c.Life) || c.Life <= 0 || c.Life > 120
                        || !float.IsFinite(c.Gravity) || !float.IsFinite(c.Drag)) return false;
                }
                _damage.Add(new(geometry[id], parts, effects, clock));
            }
            return raw.Position == raw.Length;
        }
        catch (Exception e) when (e is IOException or ArgumentException or OverflowException) { return false; }
    }
}
