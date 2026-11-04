using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using Godot;

namespace CCCPRacing.Race;

// The tape file (docs/replay-tape.md, section 8). Header: magic, format version, track, set, lap time, sample count, ghost
// fingerprint and every actor's TapeActorStructure, written plainly with BinaryWriter. Body (WorldTape.Payload): produced by
// the recorder's Finish (the only compression pass, off the hot path) and carried verbatim by Encode and Decode:
//
//   "CCB1", f32 position scale (1024: 1/1024 m), then framed chunks (varint raw length, varint compressed length, Brotli):
//   one meta chunk (counts, string table, part table, sound key table, events, keyframes), then the sample chunks (90 tape
//   samples each) and the sound chunks (90 ten-Hz ticks each).
//   Sample record: varint changed-part count; per part: zig-zag varint part number delta, flags, then only what changed as zig-zag
//   varint deltas of the part's previous state: position (3 x int, 1/1024 m), rotation (smallest three, see TapeWire) and the
//   unwrapped spinner angle (1/2048 rad). Sound tick: varint count; per change: varint sound key, byte.

/// <summary>A part's state at one instant as the file holds it (rotation with a spinner's spin removed, see section 4.4).</summary>
public readonly record struct TapePartState(bool Visible, Vector3 Pos, Quaternion Rot, bool Spin, float Angle);

public sealed class TapeBodyKey
{
    public int Sample;
    public (int Part, TapePartState State)[] Poses = Array.Empty<(int, TapePartState)>();
    public Dictionary<TapeSoundKey, byte> Sounds = new();
    public List<TapeEvent> LiveInstances = new(), LiveEffects = new();
    public List<int> Broken = new();
}

/// <summary>A tape body fully decoded (<see cref="WorldTapeCodec.ReadBody"/>): what the player works from.</summary>
public sealed class TapeBody
{
    public float PosScale = 1024f;
    public List<TapePartId> Parts = new();
    public List<(int Part, TapePartState State)[]> Samples = new();
    public List<TapeBodyKey> Keys = new();
    public List<(TapeSoundKey Key, byte Value)[]> Sounds = new();     // one entry per 10 Hz tick, changes only
    public List<TapeEvent> Events = new();
}

/// <summary>A part's quantised state: what the recorder diffs and what the stream carries.</summary>
struct TapePartQ
{
    public bool Has, Vis;
    public int X, Y, Z, RI, RA, RB, RC, Ang;
}

sealed class TapeBuf
{
    public byte[] B; public int N;
    public TapeBuf(int cap) { B = new byte[cap]; }
    public void Reserve(int cap) { if (cap > B.Length) Array.Resize(ref B, cap); }
    void Room(int more) { if (N + more > B.Length) Array.Resize(ref B, Math.Max(B.Length * 2, N + more)); }
    public void U8(int v) { Room(1); B[N++] = (byte)v; }
    public void Var(uint v) { Room(5); while (v >= 0x80) { B[N++] = (byte)(v | 0x80); v >>= 7; } B[N++] = (byte)v; }
    public void Zig(int v) => Var((uint)((v << 1) ^ (v >> 31)));
    public void F32(float f) { Room(4); BitConverter.TryWriteBytes(new Span<byte>(B, N, 4), f); N += 4; }
    public void Bytes(ReadOnlySpan<byte> s) { Room(s.Length); s.CopyTo(new Span<byte>(B, N, s.Length)); N += s.Length; }
    public void Str(string s) { var b = Encoding.UTF8.GetBytes(s); Var((uint)b.Length); Bytes(b); }
    public ReadOnlySpan<byte> Span => new(B, 0, N);
}

sealed class TapeRd
{
    readonly byte[] _b; int _p; readonly int _end;
    public TapeRd(byte[] b, int p, int end) { _b = b; _p = p; _end = end; }
    public int Pos => _p;
    public bool Done => _p >= _end;
    public int U8() { if (_p >= _end) throw new EndOfStreamException(); return _b[_p++]; }
    public uint Var() { uint v = 0; for (int s = 0; s < 35; s += 7) { int b = U8(); v |= (uint)(b & 0x7F) << s; if (b < 0x80) return v; } throw new InvalidDataException("varint"); }
    public int Zig() { uint v = Var(); return (int)(v >> 1) ^ -(int)(v & 1); }
    public int Count() { uint v = Var(); if (v > (uint)(_end - _p + 1) * 8u + 1024u) throw new InvalidDataException("count"); return (int)v; }
    public float F32() { if (_p + 4 > _end) throw new EndOfStreamException(); float f = BitConverter.ToSingle(_b, _p); _p += 4; return f; }
    public string Str() { int n = Count(); if (_p + n > _end) throw new EndOfStreamException(); var s = Encoding.UTF8.GetString(_b, _p, n); _p += n; return s; }
    public byte[] Raw(int n) { if (n < 0 || _p + n > _end) throw new EndOfStreamException(); var r = new byte[n]; Buffer.BlockCopy(_b, _p, r, 0, n); _p += n; return r; }
}

/// <summary>Quantisation and the byte layout of one part's state.</summary>
static class TapeWire
{
    public const float PosScale = 1024f;
    public const float AngleScale = 2048f;
    public const float RotScale = 46340f;                 // 32767 / sqrt(1/2): the three smallest components of a unit quaternion are within +-0.7071
    public const int ChunkSamples = 90;

    /// <summary>Smallest-three: the largest component (index RI, made positive by the sign of the whole quaternion) is dropped.</summary>
    public static void QuatQ(Quaternion q, out int ri, out int a, out int b, out int c)
    {
        float x = q.X, y = q.Y, z = q.Z, w = q.W;
        float ax = MathF.Abs(x), ay = MathF.Abs(y), az = MathF.Abs(z), aw = MathF.Abs(w);
        ri = 0; float m = ax;
        if (ay > m) { ri = 1; m = ay; }
        if (az > m) { ri = 2; m = az; }
        if (aw > m) { ri = 3; m = aw; }
        float sign = (ri switch { 0 => x, 1 => y, 2 => z, _ => w }) < 0 ? -1f : 1f;
        float s = sign * RotScale;
        switch (ri)
        {
            case 0: a = R(y * s); b = R(z * s); c = R(w * s); break;
            case 1: a = R(x * s); b = R(z * s); c = R(w * s); break;
            case 2: a = R(x * s); b = R(y * s); c = R(w * s); break;
            default: a = R(x * s); b = R(y * s); c = R(z * s); break;
        }
    }

    static int R(float v) => (int)MathF.Round(v);

    public static Quaternion QuatD(int ri, int a, int b, int c)
    {
        float fa = a / RotScale, fb = b / RotScale, fc = c / RotScale;
        float d = MathF.Sqrt(MathF.Max(0f, 1f - fa * fa - fb * fb - fc * fc));
        return ri switch { 0 => new Quaternion(d, fa, fb, fc), 1 => new Quaternion(fa, d, fb, fc), 2 => new Quaternion(fa, fb, d, fc), _ => new Quaternion(fa, fb, fc, d) };
    }

    /// <summary>A sample record entry: what changed from <paramref name="p"/> (the part's previous state) to <paramref name="c"/>.</summary>
    public static void WriteEntry(TapeBuf b, int partDelta, in TapePartQ p, in TapePartQ c, bool spinner)
    {
        bool first = !p.Has;
        bool pos = first || p.X != c.X || p.Y != c.Y || p.Z != c.Z;
        bool rotAbs = first || p.RI != c.RI;
        bool rot = rotAbs || p.RA != c.RA || p.RB != c.RB || p.RC != c.RC;
        bool ang = spinner && (first || p.Ang != c.Ang);
        b.Zig(partDelta);
        b.U8((c.Vis ? 1 : 0) | (pos ? 2 : 0) | (rot ? 4 : 0) | (ang ? 8 : 0) | (rotAbs ? 16 : 0) | (c.RI << 5));
        if (pos) { b.Zig(c.X - p.X); b.Zig(c.Y - p.Y); b.Zig(c.Z - p.Z); }
        if (rot)
        {
            if (rotAbs) { b.Zig(c.RA); b.Zig(c.RB); b.Zig(c.RC); }
            else { b.Zig(c.RA - p.RA); b.Zig(c.RB - p.RB); b.Zig(c.RC - p.RC); }
        }
        if (ang) b.Zig(c.Ang - p.Ang);
    }

    public static void ReadEntry(TapeRd r, ref TapePartQ s, bool spinner)
    {
        int fl = r.U8();
        if ((fl & 2) != 0) { s.X += r.Zig(); s.Y += r.Zig(); s.Z += r.Zig(); }
        if ((fl & 4) != 0)
        {
            if ((fl & 16) != 0) { s.RI = (fl >> 5) & 3; s.RA = r.Zig(); s.RB = r.Zig(); s.RC = r.Zig(); }
            else { s.RA += r.Zig(); s.RB += r.Zig(); s.RC += r.Zig(); }
        }
        if (spinner && (fl & 8) != 0) s.Ang += r.Zig();
        s.Vis = (fl & 1) != 0; s.Has = true;
    }

    public static void WritePose(TapeBuf b, in TapePartQ c, bool spinner)
    {
        b.U8(c.Vis ? 1 : 0); b.Zig(c.X); b.Zig(c.Y); b.Zig(c.Z); b.U8(c.RI); b.Zig(c.RA); b.Zig(c.RB); b.Zig(c.RC);
        if (spinner) b.Zig(c.Ang);
    }

    public static TapePartQ ReadPose(TapeRd r, bool spinner)
    {
        var s = new TapePartQ { Has = true, Vis = r.U8() != 0, X = r.Zig(), Y = r.Zig(), Z = r.Zig(), RI = r.U8() & 3, RA = r.Zig(), RB = r.Zig(), RC = r.Zig() };
        if (spinner) s.Ang = r.Zig();
        return s;
    }

    public static TapePartState State(in TapePartQ q, bool spinner, float posScale)
    {
        var rot = QuatD(q.RI, q.RA, q.RB, q.RC);
        return new TapePartState(q.Vis, new Vector3(q.X / posScale, q.Y / posScale, q.Z / posScale), rot, spinner, q.Ang / AngleScale);
    }

    // ---- events ----

    public static void WriteEvent(TapeBuf b, in TapeEvent e, Dictionary<string, int> strings, List<string> table)
    {
        int S(string s) { if (s == null) return 0; if (!strings.TryGetValue(s, out int i)) { i = table.Count; table.Add(s); strings[s] = i; } return i + 1; }
        bool pos = e.Pos != default, dir = e.Dir != default, vel = e.Velocity != default, other = e.Gravity != 0 || e.Param != 0 || e.Life != 0;
        b.F32(e.Time); b.U8((int)e.Type);
        b.U8((e.Source != null ? 1 : 0) | (e.Kind != null ? 2 : 0) | (e.Parent != null ? 4 : 0) | (e.Instance != 0 ? 8 : 0) | (pos ? 16 : 0) | (dir ? 32 : 0) | (vel ? 64 : 0) | (other ? 128 : 0));
        if (e.Source != null) b.Var((uint)S(e.Source)); if (e.Kind != null) b.Var((uint)S(e.Kind)); if (e.Parent != null) b.Var((uint)S(e.Parent));
        if (e.Instance != 0) b.Zig(e.Instance);
        b.U8((int)e.Effect); b.U8((int)e.Projectile);
        if (pos) V(b, e.Pos); if (dir) V(b, e.Dir); if (vel) V(b, e.Velocity);
        if (other) { b.F32(e.Gravity); b.F32(e.Param); b.F32(e.Life); }
    }

    static void V(TapeBuf b, Vector3 v) { b.F32(v.X); b.F32(v.Y); b.F32(v.Z); }
    static Vector3 V(TapeRd r) => new(r.F32(), r.F32(), r.F32());

    public static TapeEvent ReadEvent(TapeRd r, string[] strings)
    {
        float time = r.F32(); var type = (TapeEventType)r.U8(); int m = r.U8();
        string src = null, kind = null, parent = null;
        if ((m & 1) != 0) src = strings[r.Var() - 1]; if ((m & 2) != 0) kind = strings[r.Var() - 1]; if ((m & 4) != 0) parent = strings[r.Var() - 1];
        int inst = (m & 8) != 0 ? r.Zig() : 0;
        var fx = (TapeEffect)r.U8(); var pr = (TapeProjectile)r.U8();
        Vector3 pos = (m & 16) != 0 ? V(r) : default, dir = (m & 32) != 0 ? V(r) : default, vel = (m & 64) != 0 ? V(r) : default;
        float g = 0, par = 0, life = 0;
        if ((m & 128) != 0) { g = r.F32(); par = r.F32(); life = r.F32(); }
        return new TapeEvent(time, type, src, kind, parent, inst, fx, pr, pos, dir, vel, g, par, life);
    }

    // ---- framing ----

    public static void Chunk(TapeBuf dst, ReadOnlySpan<byte> raw)
    {
        var tmp = ArrayPool<byte>.Shared.Rent(BrotliEncoder.GetMaxCompressedLength(Math.Max(1, raw.Length)));
        try
        {
            if (!BrotliEncoder.TryCompress(raw, tmp, out int n, 3, 22)) throw new InvalidOperationException("brotli");
            dst.Var((uint)raw.Length); dst.Var((uint)n); dst.Bytes(new ReadOnlySpan<byte>(tmp, 0, n));
        }
        finally { ArrayPool<byte>.Shared.Return(tmp); }
    }

    public static TapeRd Unchunk(TapeRd r)
    {
        int raw = r.Count(), comp = r.Count();
        if (raw > 256 * 1024 * 1024) throw new InvalidDataException("chunk");
        var packed = r.Raw(comp); var plain = new byte[raw];
        if (!BrotliDecoder.TryDecompress(packed, plain, out int n) || n != raw) throw new InvalidDataException("brotli");
        return new TapeRd(plain, 0, raw);
    }
}

/// <summary>Encodes and decodes the tape file. Decode never throws: a bad file is null.</summary>
public static class WorldTapeCodec
{
    public static readonly byte[] BodyMagic = { (byte)'C', (byte)'C', (byte)'B', (byte)'1' };

    static void T(BinaryWriter w, Transform3D t) { var b = t.Basis; foreach (var v in new[] { b.Column0, b.Column1, b.Column2, t.Origin }) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); } }
    static Transform3D T(BinaryReader r) { Vector3 V() => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); var a = V(); var b = V(); var c = V(); return new Transform3D(new Basis(a, b, c), V()); }

    /// <summary>The whole file: header (section 8) and the body as the recorder compressed it.</summary>
    public static byte[] Encode(WorldTape t)
    {
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, true))
        {
            w.Write(WorldTapeConst.Magic); w.Write(WorldTapeConst.FormatVersion); w.Write(t.TrackId ?? ""); w.Write(t.Set); w.Write(t.LapTime); w.Write(t.SampleCount); w.Write(t.GhostFingerprint);
            w.Write(t.ActorStructure.Count);
            foreach (var (key, st) in t.ActorStructure)
            {
                w.Write(key); w.Write(st.Version); w.Write(st.Hash);
                w.Write(st.ParentPaths.Count); foreach (var (k, v) in st.ParentPaths) { w.Write(k); w.Write(v); }
                w.Write(st.RestPoses.Count); foreach (var (k, v) in st.RestPoses) { w.Write(k); T(w, v); }
                w.Write(st.RestInRoot.Count); foreach (var (k, v) in st.RestInRoot) { w.Write(k); T(w, v); }
            }
            w.Write(t.Payload.Length); w.Write(t.Payload);
        }
        return ms.ToArray();
    }

    sealed record Stamp(byte[] Payload, int Samples);
    static readonly ConditionalWeakTable<WorldTape, Stamp> Playable = new();      // what was validated (a tape edited since is checked again)

    /// <summary>
    /// True when the body can be played and agrees with the header (section 4.3): it reads to the end, its sample count is the header's, and its
    /// keyframes are the ones the recorder writes (sample 0, then every <see cref="WorldTapeConst.KeyframeSamples"/>). Silent: the loader logs the one line.
    /// </summary>
    public static bool Validate(WorldTape t)
    {
        if (t == null) return false;
        if (Playable.TryGetValue(t, out var seen) && ReferenceEquals(seen.Payload, t.Payload) && seen.Samples == t.SampleCount) return true;
        var body = ReadBody(t.Payload);
        int keys = Math.Max(1, (t.SampleCount + WorldTapeConst.KeyframeSamples - 1) / WorldTapeConst.KeyframeSamples);
        bool ok = body != null && t.SampleCount >= 0 && body.Samples.Count == t.SampleCount && body.Keys.Count == keys;
        for (int i = 0; ok && i < keys; i++) ok = body.Keys[i].Sample == i * WorldTapeConst.KeyframeSamples;
        if (!ok) return false;
        Playable.AddOrUpdate(t, new Stamp(t.Payload, t.SampleCount));
        return true;
    }

    /// <summary>The tape of a file, or null when it is not one (magic, version, truncation, a body that cannot be played or disagrees with the header, any exception).</summary>
    public static WorldTape Decode(byte[] bytes)
    {
        try
        {
            using var r = new BinaryReader(new MemoryStream(bytes));
            if (r.ReadUInt32() != WorldTapeConst.Magic || r.ReadInt32() != WorldTapeConst.FormatVersion) return null;
            var t = new WorldTape { TrackId = r.ReadString(), Set = r.ReadInt32(), LapTime = r.ReadSingle(), SampleCount = r.ReadInt32(), GhostFingerprint = r.ReadUInt64() };
            for (int n = r.ReadInt32(), i = 0; i < n; i++)
            {
                var key = r.ReadString(); var st = new TapeActorStructure { Version = r.ReadInt32(), Hash = r.ReadUInt32() };
                for (int m = r.ReadInt32(), j = 0; j < m; j++) st.ParentPaths[r.ReadString()] = r.ReadString();
                for (int m = r.ReadInt32(), j = 0; j < m; j++) st.RestPoses[r.ReadString()] = T(r);
                for (int m = r.ReadInt32(), j = 0; j < m; j++) st.RestInRoot[r.ReadString()] = T(r);
                t.ActorStructure[key] = st;
            }
            int len = r.ReadInt32();
            if (len < 0 || len > bytes.Length) return null;
            t.Payload = r.ReadBytes(len);
            return t.Payload.Length == len && Validate(t) ? t : null;
        }
        catch { return null; }
    }

    /// <summary>The body of a tape fully decoded (what the player works from), or null when the payload is not a body of this format.</summary>
    public static TapeBody ReadBody(byte[] payload)
    {
        try
        {
            if (payload == null || payload.Length < 8 || payload[0] != BodyMagic[0] || payload[1] != BodyMagic[1] || payload[2] != BodyMagic[2] || payload[3] != BodyMagic[3]) return null;
            var top = new TapeRd(payload, 4, payload.Length);
            var body = new TapeBody { PosScale = top.F32() };
            float ps = body.PosScale;
            var meta = TapeWire.Unchunk(top);
            int samples = meta.Count(), ticks = meta.Count();
            var strings = new string[meta.Count()]; for (int i = 0; i < strings.Length; i++) strings[i] = meta.Str();
            int np = meta.Count(); var spinner = new bool[np];
            for (int i = 0; i < np; i++) { body.Parts.Add(new TapePartId(strings[meta.Var()], strings[meta.Var()])); spinner[i] = meta.U8() != 0; }
            int nk = meta.Count(); var skPart = new int[nk]; var skCh = new TapeSoundChannel[nk];
            for (int i = 0; i < nk; i++) { skPart[i] = (int)meta.Var(); skCh[i] = (TapeSoundChannel)meta.U8(); }
            for (int n = meta.Count(), i = 0; i < n; i++) body.Events.Add(TapeWire.ReadEvent(meta, strings));
            for (int n = meta.Count(), i = 0; i < n; i++)
            {
                var key = new TapeBodyKey { Sample = (int)meta.Var() };
                key.Poses = new (int, TapePartState)[meta.Count()];
                for (int j = 0; j < key.Poses.Length; j++) { int p = (int)meta.Var(); key.Poses[j] = (p, TapeWire.State(TapeWire.ReadPose(meta, spinner[p]), spinner[p], ps)); }
                for (int m = meta.Count(), j = 0; j < m; j++) { int sk = (int)meta.Var(); key.Sounds[new TapeSoundKey(body.Parts[skPart[sk]], skCh[sk])] = (byte)meta.U8(); }
                for (int m = meta.Count(), j = 0; j < m; j++) key.LiveInstances.Add(body.Events[(int)meta.Var()]);
                for (int m = meta.Count(), j = 0; j < m; j++) key.LiveEffects.Add(body.Events[(int)meta.Var()]);
                for (int m = meta.Count(), j = 0; j < m; j++) key.Broken.Add(meta.Zig());
                body.Keys.Add(key);
            }
            var state = new TapePartQ[np];
            for (int nc = top.Count(), c = 0; c < nc; c++)
            {
                var rd = TapeWire.Unchunk(top);
                while (!rd.Done && body.Samples.Count < samples)
                {
                    var list = new (int, TapePartState)[rd.Count()]; int prev = 0;
                    for (int j = 0; j < list.Length; j++)
                    {
                        int p = prev + rd.Zig(); prev = p;
                        TapeWire.ReadEntry(rd, ref state[p], spinner[p]);
                        list[j] = (p, TapeWire.State(state[p], spinner[p], ps));
                    }
                    body.Samples.Add(list);
                }
            }
            for (int nc = top.Count(), c = 0; c < nc; c++)
            {
                var rd = TapeWire.Unchunk(top);
                while (!rd.Done && body.Sounds.Count < ticks)
                {
                    var list = new (TapeSoundKey, byte)[rd.Count()];
                    for (int j = 0; j < list.Length; j++) { int sk = (int)rd.Var(); list[j] = (new TapeSoundKey(body.Parts[skPart[sk]], skCh[sk]), (byte)rd.U8()); }
                    body.Sounds.Add(list);
                }
            }
            return body.Samples.Count == samples && body.Sounds.Count == ticks ? body : null;
        }
        catch { return null; }
    }
}
