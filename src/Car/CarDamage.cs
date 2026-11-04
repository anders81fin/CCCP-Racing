using System;
using System.Collections.Generic;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Car;

/// <summary>
/// Cosmetic crash damage on the player's Pyatyorka: the body dents where it was hit, the bumpers
/// hang crooked and then fall off, headlights pop out, the windscreen cracks. Works on a
/// per-instance copy of the glb body, subdivided so dents stay local; rebuilt only on a hit.
/// Handling is never touched. Lives in the car's frame (child of CarVisual).
/// </summary>
public partial class CarDamage : Node3D
{
    const float DentThreshold = 3.5f;      // m/s; a firm landing on the bumper corners stays clean
    const float MaxDent = 0.5f;            // cap per vertex so the car never turns inside out
    const float SubdivEdge = 0.3f;         // longest edge left after subdividing the body
    static readonly Color Paint = new(0.75f, 0.74f, 0.27f);
    static readonly Color Chrome = new(0.85f, 0.85f, 0.86f);
    static readonly Color Glass = new(0.85f, 0.92f, 1f);

    /// <summary>Triangle soup of one material: original (body-frame) positions plus dent offsets.</summary>
    class Surf
    {
        public Material Mat;
        public readonly List<Vector3> Orig = new();
        public readonly List<Vector3> Normal = new();     // original face normal, one per vertex
        public Vector3[] Off;
    }

    enum PartState { On, Loose, Off }

    /// <summary>A loose piece split out of the body mesh: bumpers and headlights.</summary>
    class Part
    {
        public string Name;
        public Aabb Box;                   // body-frame region its triangles lie in
        public string[] Materials;
        public readonly List<Surf> Surfs = new();
        public MeshInstance3D Node;
        public ArrayMesh Mesh;
        public Vector3 Centre, Size;
        public bool Fixed;                 // never hangs loose or falls off (the SPUTNIK's kit: slats, skirts, lamps, scoop; it only dents with the body)
        public float MinHitY = -9f;        // hits below this body-frame height do not touch it (the roof rack)
        public bool Hidden;                // a rear wing the car does not wear (the fuel picks one): not drawn, never hit
        public float Damage;               // 1 = hangs crooked, Detach = falls off
        public float Detach = 2f;
        public PartState State;
        public float HangSide;             // +1: the right end dropped (pivot on the left)
        // Debris once off.
        public Vector3 Pos, Vel, AngVel;
        public Basis Rot;
        public bool Resting;
    }

    MeshInstance3D _body;
    ArrayMesh _mesh;
    readonly List<Surf> _surfs = new();
    readonly List<Part> _parts = new();
    MeshInstance3D _crack;
    ImageTexture[] _crackTex;
    float _glass;
    int _crackStage;
    float _hitCd, _peak;
    readonly RandomNumberGenerator _rng = new() { Seed = 2105 };

    // Windscreen in the body frame, from build_pyatyorka.py ("windscreen" face, glass at u 0.08-0.92,
    // v 0.14-0.88) after its SX/SY/SZ scale, CG shift and Y-up conversion.
    static readonly Vector3 WsBL = new(-0.844f, 0.284f, -0.827f), WsBR = new(0.844f, 0.284f, -0.827f);
    static readonly Vector3 WsTL = new(-0.733f, 0.900f, -0.282f), WsTR = new(0.733f, 0.900f, -0.282f);

    /// <summary>Off in Stakhanov laps: hits leave no mark, the car stays as it left the factory.</summary>
    public bool Enabled = true;

    /// <summary>Total damage so far, for tests and the debug print.</summary>
    public int Dents { get; private set; }

    /// <summary>Anything to fix: dents, a cracked windscreen or a bumper or door hanging off.</summary>
    public bool IsDamaged
    {
        get
        {
            if (Dents > 0 || _glass > 0) return true;
            foreach (var p in _parts) if (!p.Hidden && (p.State != PartState.On || p.Damage > 0)) return true;
            return false;
        }
    }

    /// <summary>The headlight on this side is still in the car (a hard frontal hit pops it out; Reset puts it back).</summary>
    public bool HeadlightWorks(bool left)
    {
        foreach (var p in _parts)
            if (p.Name == (left ? "left headlight" : "right headlight")) return p.State != PartState.Off;
        return true;
    }

    public static CarDamage Create(MeshInstance3D body)
    {
        var d = new CarDamage { Name = "Damage", _body = body };
        d.Build();
        return d;
    }

    /// <summary>A single pistol impact breaks only the left lamp, without crash damage to the engine or body.</summary>
    public void ShootHeadlamp(CarSim car, TyreEffects fx)
    {
        if (TapeDriven) return;
        DamageChanged();
        Vector3 local = new(-0.585f, 0.03f, -1.955f);
        foreach (var p in _parts)
            if (p.Name == "left headlight" && p.State != PartState.Off) { p.Damage = p.Detach; Detach(p, local, car); }
        Vector3 hit = car.Pos + car.Rot * local;
        for (int i = 0; i < 14; i++)
            DamageBurst(fx, hit, car.Rot * new Vector3(_rng.RandfRange(-2, 2), _rng.RandfRange(1, 3), _rng.RandfRange(-3, -1)),
                Glass, 1, 0.12f, 0.07f, 1.1f, 14, 0.8f, true);
    }

    // ------------------------------------------------------------ build

    void Build()
    {
        string[] bumperMats = { "Chrome", "Black_Plastic" };
        // Bumper boxes: chrome bar, black under-strip and corner caps (build_pyatyorka.py bumper()).
        _parts.Add(new Part { Name = "front bumper", Materials = bumperMats, Box = new Aabb(new Vector3(-0.97f, -0.39f, -2.07f), new Vector3(1.94f, 0.235f, 0.235f)) });
        _parts.Add(new Part { Name = "rear bumper", Materials = bumperMats, Box = new Aabb(new Vector3(-0.97f, -0.39f, 1.835f), new Vector3(1.94f, 0.235f, 0.225f)), Detach = 2.2f });
        foreach (int s in new[] { -1, 1 })
            _parts.Add(new Part
            {
                Name = s < 0 ? "left headlight" : "right headlight", Materials = new[] { "Headlight" }, Detach = 1f,
                Box = new Aabb(new Vector3(s < 0 ? -0.72f : 0.45f, -0.1f, -1.98f), new Vector3(0.27f, 0.26f, 0.05f)),
            });

        // The rear wings, one part per height (build_pyatyorka.py): the car wears the one of its fuel (Wing), the others are hidden.
        for (int w = 0; w < WingNames.Length; w++)
            _parts.Add(new Part
            {
                Name = "rear wing " + WingNames[w].ToLowerInvariant(), Materials = new[] { "Wing_" + WingNames[w], "Wing_Emblem_" + WingNames[w] }, Detach = 2.4f,
                Box = new Aabb(new Vector3(-0.9f, 0.2f, 1.2f), new Vector3(1.8f, 0.8f, 1.1f)),
            });

        // The roof rack with the spare tyre (the SPUTNIK has none) and the SPUTNIK's kit (build_pyatyorka.py); Equip picks what the fuel's car wears.
        _parts.Add(new Part
        {
            Name = "roof rack", Materials = new[] { "Rubber_Seal", "Tyre", "Steel_Wheel", "Black_Plastic" }, MinHitY = 0.35f,
            Box = new Aabb(new Vector3(-0.95f, 0.895f, -0.55f), new Vector3(1.9f, 0.62f, 1.4f)),
        });
        _parts.Add(new Part
        {
            Name = "sputnik kit", Materials = new[] { "Kit_Dark", "Kit_Lamp", "Kit_Chrome", "Kit_Red" }, Fixed = true,
            Box = new Aabb(new Vector3(-1.2f, -0.6f, -2.3f), new Vector3(2.4f, 2f, 4.6f)),
        });
        // The twin exhaust pipes of every car but the SPUTNIK (it has one fat pipe in its kit).
        _parts.Add(new Part
        {
            Name = "twin exhaust", Materials = new[] { "Pipe_Chrome", "Pipe_Dark" }, Fixed = true,
            Box = new Aabb(new Vector3(0.2f, -0.5f, 1.7f), new Vector3(0.5f, 0.2f, 0.6f)),
        });

        var src = _body.Mesh;
        for (int si = 0; si < src.GetSurfaceCount(); si++)
        {
            var arrays = src.SurfaceGetArrays(si);
            var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var idx = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            Material mat = _body.GetSurfaceOverrideMaterial(si) ?? src.SurfaceGetMaterial(si);
            string mname = mat?.ResourceName ?? "";
            if (idx.Length == 0) { idx = new int[verts.Length]; for (int i = 0; i < idx.Length; i++) idx[i] = i; }

            // Split the loose parts out first (whole original faces), then subdivide each group.
            var groups = new Dictionary<Part, List<Tri>>();
            var rest = new List<Tri>();
            for (int t = 0; t + 2 < idx.Length; t += 3)
            {
                var tri = new Tri(verts[idx[t]], verts[idx[t + 1]], verts[idx[t + 2]],
                    (normals[idx[t]] + normals[idx[t + 1]] + normals[idx[t + 2]]).Normalized());
                var dst = rest;
                foreach (var p in _parts)
                    if (Array.IndexOf(p.Materials, mname) >= 0 && Inside(p.Box, tri.A) && Inside(p.Box, tri.B) && Inside(p.Box, tri.C))
                    {
                        if (!groups.TryGetValue(p, out dst)) groups[p] = dst = new List<Tri>();
                        break;
                    }
                dst.Add(tri);
            }
            bool fine = mname != "Gold";                    // the plate lettering is tiny already
            var body = ToSurf(fine ? Subdivide(rest) : rest, mat);
            foreach (var (p, list) in groups) p.Surfs.Add(ToSurf(fine ? Subdivide(list) : list, mat));
            _surfs.Add(body);
        }
        foreach (var s in AllSurfs()) s.Off = new Vector3[s.Orig.Count];

        _mesh = new ArrayMesh();
        _body.Mesh = _mesh;
        RebuildBody();

        foreach (var p in _parts)
        {
            var box = new Aabb();
            bool first = true;
            foreach (var s in p.Surfs)
                foreach (var v in s.Orig) { if (first) { box = new Aabb(v, Vector3.Zero); first = false; } else box = box.Expand(v); }
            p.Centre = box.GetCenter();
            p.Size = box.Size;
            p.Mesh = new ArrayMesh();
            p.Node = new MeshInstance3D { Name = p.Name, Mesh = p.Mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(p.Node);
            RebuildPart(p);
            if (OS.HasEnvironment("DAMAGEDBG")) GD.Print($"damage: {p.Name} {p.Surfs.Count} surfaces, centre {p.Centre}, size {p.Size}");
        }

        ApplyWing();
        ApplyEquip();
        _crackTex = new[] { ImageTexture.CreateFromImage(CrackImage(1)), ImageTexture.CreateFromImage(CrackImage(2)) };
        _crack = new MeshInstance3D
        {
            Name = "Cracks",
            Mesh = WindscreenQuad(),
            MaterialOverride = new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                AlbedoTexture = _crackTex[0],
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_crack);
        int n0 = 0;
        foreach (var s in AllSurfs()) n0 += s.Orig.Count / 3;
        if (OS.HasEnvironment("DAMAGEDBG")) GD.Print($"damage: body subdivided to {n0} triangles");
    }

    /// <summary>Triangles of the body proper (not of the loose parts) in materials that start with the prefix; the wings, the kit, the spare must all have moved into their parts (tests).</summary>
    internal int BodyTriangles(params string[] prefixes)
    {
        int n = 0;
        foreach (var s in _surfs)
            foreach (var pre in prefixes)
                if ((s.Mat?.ResourceName ?? "").StartsWith(pre)) n += s.Orig.Count / 3;
        return n;
    }

    public static readonly string[] WingNames = { "Low", "Mid", "High", "Duck" };
    int _wing;

    /// <summary>The rear wing the car wears: 0 low (PETROL), 1 middle (JET FUEL), 2 high (SPUTNIK), 3 the duck-tail lip of the DIESEL (-1: none). The other wings are hidden and take no damage.</summary>
    public int Wing
    {
        get => _wing;
        set { if (_wing == value) return; _wing = value; ApplyWing(); }
    }

    void ApplyWing()
    {
        for (int w = 0; w < WingNames.Length; w++)
            foreach (var p in _parts)
                if (p.Name == "rear wing " + WingNames[w].ToLowerInvariant() && p.Node != null)
                { p.Hidden = w != _wing; p.Node.Visible = !p.Hidden; }
    }

    bool _bumpers = true, _rack = true, _kit;

    /// <summary>The SPUTNIK's kit is on (its four auxiliary lamps light up after dark: CarLights).</summary>
    public bool KitWorn => _kit;

    /// <summary>What the car wears besides its wing: the bumpers (the JET FUEL and the SPUTNIK run without them), the roof rack with the spare tyre (not the SPUTNIK) and the SPUTNIK's kit. What is not worn is hidden and takes no damage.</summary>
    public void Equip(bool bumpers, bool roofRack, bool kit)
    {
        if (_bumpers == bumpers && _rack == roofRack && _kit == kit) return;
        _bumpers = bumpers; _rack = roofRack; _kit = kit;
        ApplyEquip();
    }

    void ApplyEquip()
    {
        foreach (var p in _parts)
        {
            bool? worn = p.Name switch { "front bumper" or "rear bumper" => _bumpers, "roof rack" => _rack, "sputnik kit" => _kit, "twin exhaust" => !_kit, _ => null };
            if (worn is bool w && p.Node != null) { p.Hidden = !w; p.Node.Visible = w; }
        }
    }

    static bool Inside(Aabb b, Vector3 p) =>
        p.X >= b.Position.X && p.Y >= b.Position.Y && p.Z >= b.Position.Z && p.X <= b.End.X && p.Y <= b.End.Y && p.Z <= b.End.Z;

    IEnumerable<Surf> AllSurfs()
    {
        foreach (var s in _surfs) yield return s;
        foreach (var p in _parts) foreach (var s in p.Surfs) yield return s;
    }

    readonly record struct Tri(Vector3 A, Vector3 B, Vector3 C, Vector3 N);

    static Surf ToSurf(List<Tri> tris, Material mat)
    {
        var s = new Surf { Mat = mat };
        foreach (var t in tris)
        {
            s.Orig.Add(t.A); s.Orig.Add(t.B); s.Orig.Add(t.C);
            s.Normal.Add(t.N); s.Normal.Add(t.N); s.Normal.Add(t.N);
        }
        return s;
    }

    /// <summary>
    /// Splits every edge longer than SubdivEdge at its midpoint until none is left. The decision
    /// depends on the edge alone, so faces sharing an edge split it alike and dents stay closed.
    /// </summary>
    static List<Tri> Subdivide(List<Tri> tris)
    {
        for (int pass = 0; pass < 8; pass++)
        {
            var next = new List<Tri>(tris.Count * 2);
            bool any = false;
            foreach (var t in tris)
            {
                Vector3 a = t.A, b = t.B, c = t.C, n = t.N;
                bool sab = a.DistanceTo(b) > SubdivEdge, sbc = b.DistanceTo(c) > SubdivEdge, sca = c.DistanceTo(a) > SubdivEdge;
                int k = (sab ? 1 : 0) + (sbc ? 1 : 0) + (sca ? 1 : 0);
                if (k == 0) { next.Add(t); continue; }
                any = true;
                if (k == 3)
                {
                    Vector3 mab = (a + b) * 0.5f, mbc = (b + c) * 0.5f, mca = (c + a) * 0.5f;
                    next.Add(new Tri(a, mab, mca, n)); next.Add(new Tri(mab, b, mbc, n));
                    next.Add(new Tri(mca, mbc, c, n)); next.Add(new Tri(mab, mbc, mca, n));
                }
                else if (k == 1)
                {
                    // Rotate (keeping the winding) so the split edge is ab.
                    if (sbc) (a, b, c) = (b, c, a);
                    else if (sca) (a, b, c) = (c, a, b);
                    Vector3 m = (a + b) * 0.5f;
                    next.Add(new Tri(a, m, c, n)); next.Add(new Tri(m, b, c, n));
                }
                else
                {
                    // Rotate so the split edges are ab and bc.
                    if (!sab) (a, b, c) = (b, c, a);
                    else if (!sbc) (a, b, c) = (c, a, b);
                    Vector3 m1 = (a + b) * 0.5f, m2 = (b + c) * 0.5f;
                    next.Add(new Tri(m1, b, m2, n)); next.Add(new Tri(a, m1, m2, n)); next.Add(new Tri(a, m2, c, n));
                }
            }
            tris = next;
            if (!any) break;
        }
        return tris;
    }

    static void AddSurf(ArrayMesh mesh, Surf s, Vector3 shift)
    {
        int n = s.Orig.Count;
        if (n == 0) return;
        var pos = new Vector3[n];
        var nrm = new Vector3[n];
        for (int i = 0; i < n; i++) pos[i] = s.Orig[i] + s.Off[i] - shift;
        for (int i = 0; i + 2 < n; i += 3)
        {
            // Flat facets: a crumpled panel catches the light differently from its neighbours.
            Vector3 f = (pos[i + 1] - pos[i]).Cross(pos[i + 2] - pos[i]);
            f = f.LengthSquared() > 1e-12f ? f.Normalized() : s.Normal[i];
            if (f.Dot(s.Normal[i]) < 0) f = -f;
            nrm[i] = nrm[i + 1] = nrm[i + 2] = f;
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = pos;
        arrays[(int)Mesh.ArrayType.Normal] = nrm;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, s.Mat);
    }

    void RebuildBody()
    {
        _mesh.ClearSurfaces();
        foreach (var s in _surfs) AddSurf(_mesh, s, Vector3.Zero);
    }

    /// <summary>The mechanic's hammer knocks the dents partly out: every offset keeps this fraction (0..1) of its depth.
    /// Hanging and missing parts, the windscreen and the dent count wait for the last blow (Reset).</summary>
    public void Straighten(float keep)
    {
        if (TapeDriven || Dents == 0 || keep >= 1f) return;
        DamageChanged();
        foreach (var s in _surfs) ScaleOff(s, keep);
        RebuildBody();
        foreach (var p in _parts)
        {
            if (p.State != PartState.On || p.Damage <= 0) continue;
            foreach (var s in p.Surfs) ScaleOff(s, keep);
            p.Damage *= keep;
            RebuildPart(p);
        }
    }

    static void ScaleOff(Surf s, float k)
    {
        for (int i = 0; i < s.Off.Length; i++) s.Off[i] *= k;
    }

    void RebuildPart(Part p)
    {
        p.Mesh.ClearSurfaces();
        foreach (var s in p.Surfs) AddSurf(p.Mesh, s, p.Centre);
        PlacePart(p);
    }

    /// <summary>Attached: at home, or hanging off one end, drooping and sticking out a little.</summary>
    void PlacePart(Part p)
    {
        if (p.State == PartState.Off) return;
        if (p.State == PartState.On) { p.Node.Transform = new Transform3D(Basis.Identity, p.Centre); return; }
        var pivot = p.Centre + new Vector3(-p.HangSide * p.Size.X * 0.45f, 0, 0);
        float end = Math.Sign(p.Centre.Z);                        // -1 front, +1 rear
        var rot = new Basis(Vector3.Back, -p.HangSide * 0.38f) * new Basis(Vector3.Up, p.HangSide * end * 0.22f);
        p.Node.Transform = new Transform3D(rot, pivot + rot * (p.Centre - pivot));
    }

    // ------------------------------------------------------------ hits

    /// <summary>Everything as it left the factory in Tolyatti (race restart).</summary>
    public void Reset()
    {
        if (TapeDriven) Enabled = _liveEnabled;
        TapeDriven = false; _appliedGeometry = null; DamageChanged();
        _chips.Clear(); _chipClock = 0;
        bool dirty = Dents > 0;
        Dents = 0;
        foreach (var s in AllSurfs()) Array.Clear(s.Off);
        if (dirty) RebuildBody();
        foreach (var p in _parts)
        {
            bool was = p.State != PartState.On || p.Damage > 0;
            p.Damage = 0;
            if (p.State == PartState.Off)
            {
                p.Node.TopLevel = false;
                p.Node.Visible = !p.Hidden;
            }
            p.State = PartState.On;
            p.Resting = false;
            if (was || dirty) RebuildPart(p);
        }
        _glass = 0;
        _crackStage = 0;
        _crack.Visible = false;
        _hitCd = _peak = 0;
    }

    /// <summary>
    /// A collision of the given strength (m/s) at a body-frame point; dir is the body-frame
    /// direction the panel was pushed (into the car). Potholes should not come here.
    /// </summary>
    public void Hit(float strength, Vector3 local, Vector3 dir, CarSim car, TyreEffects fx)
    {
        // One crash lasts a few ticks (body points sink in, obstacles push back): only its
        // strongest tick counts, not every one of them.
        if (TapeDriven || !Enabled || strength < DentThreshold || strength <= _peak) return;
        DamageChanged();
        _peak = strength;
        local = new Vector3(Math.Clamp(local.X, -0.95f, 0.95f), Math.Clamp(local.Y, -0.4f, 0.95f), Math.Clamp(local.Z, -2.05f, 2.05f));
        dir = dir.LengthSquared() > 1e-6f ? dir.Normalized() : -local.Normalized();
        var broke = new List<string>();

        // Dent: vertices near the hit move along dir. The weight only depends on a vertex's
        // original position, so coincident corners of neighbouring faces move together.
        float depth = Math.Min(MaxDent, (strength - DentThreshold + 2f) * 0.05f);   // exaggerated: the car is ~25 px long
        float radius = Math.Clamp(0.5f + strength * 0.02f, 0.5f, 0.8f);
        int moved = 0;
        foreach (var s in AllSurfs()) moved += Dent(s, local, dir, depth, radius);
        if (moved > 0) Dents++;

        // Loose parts: the end that was hit.
        foreach (var p in _parts)
        {
            if (p.State == PartState.Off || p.Hidden || p.Fixed || local.Y < p.MinHitY) continue;
            Vector3 d = local - p.Centre;
            bool headlight = p.Detach <= 1f;
            float reach = headlight ? 0.55f : 0.9f;
            if (Math.Abs(d.Z) > reach || Math.Abs(d.X) > p.Size.X * 0.5f + reach) continue;
            float k = (strength - DentThreshold) / (headlight ? 7f : 6f);
            if (dir.Y > 0.7f) k *= 0.5f;                            // scraping the ground, not a wall
            p.Damage += Math.Max(0, k);
            if (p.Damage >= p.Detach) { Detach(p, local, car); broke.Add(p.Name + " off"); }
            else if (p.Damage >= 1f && p.State == PartState.On)
            {
                p.State = PartState.Loose;
                p.HangSide = local.X >= p.Centre.X ? 1 : -1;
                broke.Add(p.Name + " loose");
            }
            if (p.State != PartState.Off) RebuildPart(p);
        }

        // Windscreen: roof hits (rollovers) and hard frontal ones crack it.
        float g = 0;
        if (local.Y > 0.45f) g = (strength - 2.5f) / 4f;
        else if (local.Z < -1.2f && strength > 7f) g = (strength - 6f) / 7f;
        else if (strength > 12f) g = (strength - 11f) / 8f;
        if (g > 0)
        {
            _glass += g;
            int stage = _glass >= 2.2f ? 2 : _glass >= 1f ? 1 : 0;
            if (stage > _crackStage)
            {
                _crackStage = stage;
                ((StandardMaterial3D)_crack.MaterialOverride).AlbedoTexture = _crackTex[stage - 1];
                _crack.Visible = true;
                broke.Add(stage == 1 ? "windscreen cracked" : "windscreen shattered");
            }
        }

        if (moved > 0 || broke.Count > 0) RebuildBody();

        // Paint chips and glass bits, only for a proper bang and not every tick of a scrape.
        if (strength > 6f && _hitCd <= 0 && fx != null)
        {
            _hitCd = 1f;
            Vector3 wp = car.Pos + car.Rot * local;
            Vector3 out_ = car.Rot * -dir;
            int n = Math.Min(24, (int)(strength * 1.2f));
            for (int i = 0; i < n; i++)
            {
                bool glass = i % 3 == 0;
                Color c = glass ? Glass : i % 3 == 1 ? Paint : Chrome;
                DamageBurst(fx, wp, car.Vel * 0.4f + out_ * _rng.RandfRange(2f, 5f) + new Vector3(_rng.RandfRange(-2.5f, 2.5f), _rng.RandfRange(2f, 5f), _rng.RandfRange(-2.5f, 2.5f)),
                    c, 1f, 0.14f, 0.1f, _rng.RandfRange(0.8f, 1.6f), 14f, 0.8f, true);
            }
        }

        if (OS.HasEnvironment("DAMAGEDBG"))
            GD.Print($"damage: hit {strength:F1} m/s at ({local.X:F2},{local.Y:F2},{local.Z:F2}) dir ({dir.X:F2},{dir.Y:F2},{dir.Z:F2}) depth {depth:F2} r {radius:F2} verts {moved} glass {_glass:F2}"
                + (broke.Count > 0 ? " -> " + string.Join(", ", broke) : ""));
    }

    /// <summary>Pushes the vertices near the hit in along dir; returns how many moved.</summary>
    static int Dent(Surf s, Vector3 hit, Vector3 dir, float depth, float radius)
    {
        int moved = 0;
        for (int i = 0; i < s.Orig.Count; i++)
        {
            Vector3 dp = s.Orig[i] - hit;
            float along = dp.Dot(dir);                              // > 0: deeper inside the car
            float lateral = (dp - dir * along).Length();
            float w = Falloff(lateral / radius) * (along > 0 ? Falloff(along / (radius * 1.3f)) : 1f);
            if (w <= 0) continue;
            // Crumple: a little pseudo-random wander, fixed per position, so the panel folds.
            Vector3 jit = Hash3(s.Orig[i]) * 0.6f;
            Vector3 off = s.Off[i] + (dir + jit) * depth * w;
            if (off.Length() > MaxDent) off = off.Normalized() * MaxDent;
            s.Off[i] = off;
            moved++;
        }
        return moved;
    }

    static float Falloff(float x) => x >= 1f ? 0f : (1f - x * x) * (1f - x * x);

    static Vector3 Hash3(Vector3 p)
    {
        uint h = (uint)Mathf.RoundToInt(p.X * 1000f) * 73856093u ^ (uint)Mathf.RoundToInt(p.Y * 1000f) * 19349663u ^ (uint)Mathf.RoundToInt(p.Z * 1000f) * 83492791u;
        float F() { h ^= h << 13; h ^= h >> 17; h ^= h << 5; return (h & 0xffff) / 32767.5f - 1f; }
        return new Vector3(F(), F(), F());
    }

    void Detach(Part p, Vector3 hit, CarSim car)
    {
        Transform3D xf = p.Node.GlobalTransform;
        p.State = PartState.Off;
        // Rebuild once more with the latest dents, then it flies on its own.
        p.Mesh.ClearSurfaces();
        foreach (var s in p.Surfs) AddSurf(p.Mesh, s, p.Centre);
        p.Node.TopLevel = true;
        p.Node.GlobalTransform = xf;
        p.Pos = xf.Origin;
        p.Rot = xf.Basis;
        p.Resting = false;
        Vector3 away = car.Rot * new Vector3(0, 0, Math.Sign(p.Centre.Z));
        p.Vel = car.Vel * 0.5f + away * _rng.RandfRange(1.5f, 3f) + Vector3.Up * _rng.RandfRange(2f, 3.5f);
        p.AngVel = car.Rot * new Vector3(_rng.RandfRange(-3f, 3f), _rng.RandfRange(-4f, 4f), _rng.RandfRange(-6f, 6f));
    }

    /// <summary>Loose parts tumble, clatter and lie on the road until the restart.</summary>
    public void Tick(float dt, IGround ground)
    {
        if (TapeDriven) return;
        _chipClock += dt;
        _chips.RemoveAll(c => _chipClock - c.Born >= c.Life + (c.Solid ? 2.5f : 0));
        _hitCd -= dt;
        _peak = Math.Max(0, _peak - 12f * dt);
        foreach (var p in _parts)
        {
            if (p.State != PartState.Off || p.Resting) continue;
            p.Vel.Y -= 9.81f * dt;
            p.Pos += p.Vel * dt;
            float w = p.AngVel.Length();
            if (w > 1e-4f) p.Rot = (new Basis(p.AngVel / w, w * dt) * p.Rot).Orthonormalized();
            float half = Math.Max(0.03f, Math.Min(p.Size.X, Math.Min(p.Size.Y, p.Size.Z)) * 0.5f);
            float g = (ground is TrackData t ? t.MeshHeight(p.Pos.X, p.Pos.Z) : ground.Sample(p.Pos.X, p.Pos.Z).Height) + half;
            if (p.Pos.Y < g)
            {
                p.Pos.Y = g;
                if (p.Vel.Y < -1.5f)
                {
                    p.Vel = new Vector3(p.Vel.X * 0.5f, -p.Vel.Y * 0.3f, p.Vel.Z * 0.5f);
                    p.AngVel *= 0.6f;
                }
                else
                {
                    p.Vel = new Vector3(p.Vel.X * 0.8f, 0, p.Vel.Z * 0.8f);
                    p.AngVel *= 0.8f;
                    p.Rot = p.Rot.Slerp(LieFlat(p.Rot), Math.Min(1f, 8f * dt)).Orthonormalized();
                    if (p.Vel.LengthSquared() < 0.05f && p.AngVel.LengthSquared() < 0.2f)
                    {
                        p.Rot = LieFlat(p.Rot);
                        p.Resting = true;
                    }
                }
            }
            p.Node.GlobalTransform = new Transform3D(p.Rot, p.Pos);
        }
    }

    /// <summary>Nearest orientation with a local axis straight up, keeping the heading.</summary>
    static Basis LieFlat(Basis r)
    {
        r = r.Orthonormalized();
        Vector3[] axes = { r.X, r.Y, r.Z };
        int up = 1;
        for (int i = 0; i < 3; i++) if (Math.Abs(axes[i].Y) > Math.Abs(axes[up].Y)) up = i;
        Vector3 from = axes[up] * Math.Sign(axes[up].Y == 0 ? 1 : axes[up].Y);
        Vector3 axis = from.Cross(Vector3.Up);
        if (axis.LengthSquared() < 1e-8f) return r;
        return (new Basis(axis.Normalized(), from.AngleTo(Vector3.Up)) * r).Orthonormalized();
    }

    // ------------------------------------------------------------ windscreen

    static ArrayMesh WindscreenQuad()
    {
        Vector3 Q(float u, float v) => WsBL.Lerp(WsBR, u).Lerp(WsTL.Lerp(WsTR, u), v);
        Vector3 n = (WsBR - WsBL).Cross(WsTL - WsBL).Normalized();
        if (n.Z > 0) n = -n;                                        // faces forward (-Z)
        Vector3 lift = n * 0.02f;
        Vector3 bl = Q(0.08f, 0.14f) + lift, br = Q(0.92f, 0.14f) + lift, tl = Q(0.08f, 0.88f) + lift, tr = Q(0.92f, 0.88f) + lift;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void V(Vector3 p, float u, float v) { st.SetNormal(n); st.SetUV(new Vector2(u, v)); st.AddVertex(p); }
        // Both windings, so it shows whichever way Godot culls.
        V(bl, 0, 1); V(br, 1, 1); V(tr, 1, 0);
        V(bl, 0, 1); V(tr, 1, 0); V(tl, 0, 0);
        V(bl, 0, 1); V(tr, 1, 0); V(br, 1, 1);
        V(bl, 0, 1); V(tl, 0, 0); V(tr, 1, 0);
        return st.Commit();
    }

    /// <summary>
    /// Spider-web cracks drawn big and pale so they read at 480×270: one star on the driver's
    /// side, then a second one and a milky, crazed pane.
    /// </summary>
    static Image CrackImage(int stage)
    {
        const int W = 16, H = 8;              // about one texel per screen pixel at 480×270
        var img = Image.CreateEmpty(W, H, false, Image.Format.Rgba8);
        img.Fill(new Color(0.95f, 0.97f, 1f, stage >= 2 ? 0.45f : 0f));
        var rng = new RandomNumberGenerator { Seed = (ulong)(77 + stage) };
        var line = new Color(1f, 1f, 1f, 0.95f);

        void Line(Vector2 a, Vector2 b)
        {
            int steps = (int)Math.Ceiling(a.DistanceTo(b) * 1.5f) + 1;
            for (int i = 0; i <= steps; i++)
            {
                Vector2 p = a.Lerp(b, i / (float)steps);
                int x = (int)p.X, y = (int)p.Y;
                if (x >= 0 && y >= 0 && x < W && y < H) img.SetPixel(x, y, line);
            }
        }

        void Web(Vector2 c, int spokes, float len)
        {
            var ends = new Vector2[spokes];
            for (int k = 0; k < spokes; k++)
            {
                float a = MathF.Tau * k / spokes + rng.RandfRange(-0.25f, 0.25f);
                Vector2 dirv = new(MathF.Cos(a), MathF.Sin(a) * 0.6f);
                // Jagged spoke in two legs.
                Vector2 mid = c + dirv * len * rng.RandfRange(0.4f, 0.6f) + new Vector2(rng.RandfRange(-0.7f, 0.7f), rng.RandfRange(-0.5f, 0.5f));
                ends[k] = c + dirv * len * rng.RandfRange(0.8f, 1.3f);
                Line(c, mid);
                Line(mid, ends[k]);
            }
            // Rings between the spokes.
            for (int y = -1; y <= 0; y++) for (int x = -1; x <= 0; x++) img.SetPixel((int)c.X + x, (int)c.Y + y, line);   // the stone chip
            foreach (float r in new[] { 0.55f })
                for (int k = 0; k < spokes; k++)
                {
                    Vector2 a = c + (ends[k] - c) * r, b = c + (ends[(k + 1) % spokes] - c) * r;
                    Line(a, b);
                }
        }

        Web(new Vector2(W * 0.32f, H * 0.5f), 7, 6f);
        if (stage >= 2)
        {
            Web(new Vector2(W * 0.72f, H * 0.55f), 6, 5f);
            for (int k = 0; k < 3; k++)
                Line(new Vector2(rng.RandfRange(0, W), rng.RandfRange(0, H)), new Vector2(rng.RandfRange(0, W), rng.RandfRange(0, H)));
        }
        return img;
    }
}
