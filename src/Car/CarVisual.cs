using System;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Car;

/// <summary>
/// The Pyatyorka 2105 rally special from assets/cars/pyatyorka2105.glb (built by tools/blender/build_pyatyorka.py),
/// with a primitive fallback. The body follows the sim's rigid body, so every wobble of the
/// terrible suspension is visible; wheels stay on their spring positions.
/// </summary>
public partial class CarVisual : Node3D
{
    readonly Node3D[] _wheelPivots = new Node3D[4];
    readonly Node3D[] _wheelSpin = new Node3D[4];
    MeshInstance3D _shadow;
    CarParams _p;
    bool _ghost;
    StandardMaterial3D _ghostMat;

    /// <summary>Crash damage (dents, loose bumpers, cracked glass); null for the ghost and the fallback car.</summary>
    public CarDamage Damage { get; private set; }

    public const string ModelPath = "res://assets/cars/pyatyorka2105.glb";

    // The body paint materials (Paint_Olive*) with their factory colour, for the golden car.
    readonly System.Collections.Generic.List<(StandardMaterial3D mat, Color color, float metallic)> _paint = new();
    bool _golden;

    /// <summary>
    /// Training at 200 %: the Hero of Socialist Labour drives a golden Pyatyorka. Recolours the
    /// shared paint materials, so dents and loose bumpers turn gold too.
    /// </summary>
    public bool Golden
    {
        get => _golden;
        set
        {
            if (_golden == value) return;
            _golden = value;
            RePaint();
        }
    }

    Color? _fuelPaint;
    int _wing;
    MeshInstance3D _ghostBody, _pipeGlow;
    StandardMaterial3D _pipeGlowMat;
    double _shudderPhase;
    static readonly float[] FiveBeat = { 1f, .45f, .85f, .35f, .65f };

    /// <summary>The five-cylinder's idle: 0..1, how hard the whole car trembles (a beat of five, uneven: it shows from behind). The SPUTNIK sets it; cosmetic only.</summary>
    public float Shudder;

    /// <summary>The glow of the SPUTNIK's fat pipe under boost, 0..1 (the pipe mouth turns orange; 0 hides it).</summary>
    public void PipeGlow(float level)
    {
        if (_pipeGlow == null) return;
        _pipeGlow.Visible = level > 0.03f;
        if (_pipeGlow.Visible) _pipeGlowMat.AlbedoColor = new Color(1f, 0.42f + 0.2f * level, 0.08f, Math.Clamp(level, 0f, 1f) * 0.9f);
    }

    Mesh _bodySource;
    readonly System.Collections.Generic.Dictionary<int, ArrayMesh> _wingMeshes = new();

    /// <summary>
    /// The rear wing of the fuel: 0 low (PETROL), 1 middle (JET FUEL), 2 high (SPUTNIK), 3 the duck-tail lip (DIESEL); -1 none. The glb has all three heights; the car's damage model shows the one
    /// (and can tear it off in a hard rear hit), the ghost's plain body mesh is cut down to it.
    /// </summary>
    public int Wing
    {
        get => _wing;
        set
        {
            _wing = value;
            if (Damage != null) Damage.Wing = value;
            if (_ghostBody != null) _ghostBody.Mesh = OneWing(value);
        }
    }

    /// <summary>The bumpers, the roof rack with the spare and the SPUTNIK's kit (CarDamage.Equip); the ghost keeps its plain body.</summary>
    public void Equip(bool bumpers, bool roofRack, bool kit) => Damage?.Equip(bumpers, roofRack, kit);

    ArrayMesh OneWing(int wing)
    {
        if (_wingMeshes.TryGetValue(wing, out var cached)) return cached;
        var mesh = new ArrayMesh();
        for (int si = 0; si < _bodySource.GetSurfaceCount(); si++)
        {
            var mat = _bodySource.SurfaceGetMaterial(si);
            string name = mat?.ResourceName ?? "";
            if (name.StartsWith("Kit_")) continue;      // the SPUTNIK's kit is not on the ghost
            if (name.StartsWith("Wing_"))
            {
                string height = name.StartsWith("Wing_Emblem_") ? name["Wing_Emblem_".Length..] : name["Wing_".Length..];
                if (System.Array.IndexOf(CarDamage.WingNames, height) != wing) continue;
            }
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, _bodySource.SurfaceGetArrays(si));
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, mat);
        }
        return _wingMeshes[wing] = mesh;
    }

    /// <summary>
    /// The rally's fuel colours the car: the factory paint for petrol (null), olive grey for diesel, CCCP red for rocket fuel. The gold of the OCTANE BOOSTER or the
    /// hero's 200 % goes over it.
    /// </summary>
    public Color? FuelPaint
    {
        get => _fuelPaint;
        set
        {
            if (_fuelPaint == value) return;
            _fuelPaint = value;
            RePaint();
        }
    }

    /// <summary>The paint materials in the colour of the moment: gold, else the fuel's, else the factory's. The darker panels in proportion.</summary>
    void RePaint()
    {
        float top = 0;
        foreach (var (_, color, _) in _paint) top = Math.Max(top, color.Luminance);
        foreach (var (mat, color, metallic) in _paint)
        {
            bool dark = mat.ResourceName.EndsWith("Dark");
            if (_golden)
            {
                mat.AlbedoColor = dark ? new Color(0.78f, 0.56f, 0.12f) : new Color(1f, 0.76f, 0.2f);
                mat.Metallic = 0.7f;
            }
            else if (_fuelPaint is Color c)
            {
                float shade = top > 0 ? Math.Max(color.Luminance / top, 0.75f) : 1f;
                mat.AlbedoColor = new Color(c.R * shade, c.G * shade, c.B * shade);
                mat.Metallic = 0f;      // matt army paint
            }
            else
            {
                mat.AlbedoColor = color;
                mat.Metallic = metallic;
            }
        }
    }

    float _radGlow = -1;
    OmniLight3D _radLight;
    static readonly Color RadGreen = new(0.45f, 1f, 0.25f);

    /// <summary>
    /// The zone's dose on the car (ATOMGRAD-4): its paint glows green, 0 none .. 1 full; night 0..1
    /// lets it shine out (a green light round the car) after dark. Shared paint, as Golden.
    /// </summary>
    public void SetRadGlow(float glow, float night)
    {
        float e = glow * (0.25f + 0.75f * night);
        if (Math.Abs(e - _radGlow) < 0.004f) return;
        _radGlow = e;
        foreach (var (mat, _, _) in _paint)
        {
            mat.EmissionEnabled = e > 0.001f;
            mat.Emission = RadGreen;
            mat.EmissionEnergyMultiplier = e * 1.4f;
        }
        if (_radLight == null)
        {
            _radLight = new OmniLight3D { LightColor = RadGreen, OmniRange = 7f, ShadowEnabled = false, Position = new Vector3(0, 1f, 0) };
            AddChild(_radLight);
        }
        _radLight.Visible = e > 0.01f && night > 0.05f;
        _radLight.LightEnergy = 2.2f * glow * night;
    }

    readonly System.Collections.Generic.Dictionary<int, StandardMaterial3D> _repaint = new();

    /// <summary>
    /// A LAN race paints this car alone (the paint materials are shared by every car): the body's
    /// paint surfaces get own copies in this colour, the darker panels in proportion; null restores
    /// the factory paint.
    /// </summary>
    public void Repaint(Color? paint)
    {
        float top = 0;
        foreach (var (_, color, _) in _paint) top = Math.Max(top, color.Luminance);
        foreach (Node n in FindChildren("*", "MeshInstance3D", true, false))
        {
            if (n is not MeshInstance3D mi || mi.Mesh == null) continue;
            for (int si = 0; si < mi.Mesh.GetSurfaceCount(); si++)
            {
                int k = _paint.FindIndex(p => p.mat == mi.Mesh.SurfaceGetMaterial(si));
                if (k < 0) continue;
                if (paint is not Color c) { mi.SetSurfaceOverrideMaterial(si, null); continue; }
                if (!_repaint.TryGetValue(k, out var m)) _repaint[k] = m = (StandardMaterial3D)_paint[k].mat.Duplicate();
                float shade = top > 0 ? _paint[k].color.Luminance / top : 1f;
                m.AlbedoColor = new Color(c.R * shade, c.G * shade, c.B * shade);
                mi.SetSurfaceOverrideMaterial(si, m);
            }
        }
    }

    public static CarVisual Create(CarParams p, Color paint, bool ghost = false)
    {
        var v = new CarVisual { Name = ghost ? "Ghost" : "Car", _p = p, _ghost = ghost };
        v.BuildModel(paint);
        return v;
    }

    StandardMaterial3D M(Color c)
    {
        if (!_ghost) return Props.Mat(c);
        return new StandardMaterial3D
        {
            AlbedoColor = new Color(c, 0.35f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
    }

    void Add(Node3D parent, Mesh mesh, Color c, Vector3 pos, Vector3? rotDeg = null)
    {
        parent.AddChild(new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = M(c),
            Position = pos,
            RotationDegrees = rotDeg ?? Vector3.Zero,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    void BuildModel(Color paint)
    {
        Mesh wheelMesh = null;
        if (ResourceLoader.Exists(ModelPath) && ResourceLoader.Load<PackedScene>(ModelPath) is { } scene)
        {
            Node model = scene.Instantiate();
            var body = model.FindChild("Body", true, false) as MeshInstance3D;
            var wheel = model.FindChild("Wheel", true, false) as MeshInstance3D;
            if (body != null && wheel != null)
            {
                body.GetParent().RemoveChild(body);
                body.Owner = null;
                body.Transform = Transform3D.Identity;   // authored around the centre of gravity
                for (int si = 0; !_ghost && si < body.Mesh.GetSurfaceCount(); si++)
                    if (body.Mesh.SurfaceGetMaterial(si) is StandardMaterial3D m && m.ResourceName.StartsWith("Paint_Olive"))
                        _paint.Add((m, m.AlbedoColor, m.Metallic));
                _bodySource = body.Mesh;
                AddChild(Ghostify(body));
                if (!_ghost)
                {
                    Damage = CarDamage.Create(body);
                    AddChild(Damage);
                    // The glow of the fat pipe's mouth (the SPUTNIK's kit; hidden until it is lit): a small unshaded disc just behind the pipe.
                    _pipeGlowMat = new StandardMaterial3D
                    {
                        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        AlbedoColor = new Color(1f, 0.5f, 0.1f, 0f), CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                        AlbedoTexture = new GradientTexture2D
                        {
                            Width = 32, Height = 32, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f),
                            Gradient = new Gradient { Colors = new[] { Colors.White, Colors.White, new Color(1, 1, 1, 0) }, Offsets = new[] { 0f, 0.45f, 1f } },
                        },
                    };
                    _pipeGlow = new MeshInstance3D
                    {
                        Name = "PipeGlow", Mesh = new QuadMesh { Size = new Vector2(0.255f, 0.255f) }, MaterialOverride = _pipeGlowMat, Visible = false,
                        Position = new Vector3(0.40f, -0.398f, 2.085f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    };
                    AddChild(_pipeGlow);
                }
                else { _ghostBody = body; body.Mesh = OneWing(_wing); }
                wheelMesh = wheel.Mesh;
            }
            else GD.PushWarning($"{ModelPath}: missing Body/Wheel nodes, using primitive car");
            model.Free();
        }
        if (wheelMesh == null) BuildPrimitiveBody(paint);

        // Close the recessed opening between the wheel wells at the sill's underside.
        // Rigid body-mounted plate: separate from the recorded deformable body mesh.
        float floorFront = _p.FrontZ + .44f * .94f, floorRear = _p.RearZ - .44f * .94f;
        Add(this, new BoxMesh { Size = new Vector3(1.798f, .035f, floorRear - floorFront) },
            new Color(.048f, .048f, .051f), new Vector3(0, -.4125f, (floorFront + floorRear) * .5f));

        for (int i = 0; i < 4; i++)
        {
            var pivot = new Node3D();
            var spin = new Node3D();
            pivot.AddChild(spin);
            bool front = i < 2, left = i % 2 == 0;
            if (wheelMesh != null)
            {
                // Rim face is modelled on +X; turn the left wheels so it faces outward.
                spin.AddChild(Ghostify(new MeshInstance3D
                {
                    Mesh = wheelMesh,
                    RotationDegrees = new Vector3(0, left ? 180 : 0, 0),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                }));
            }
            else
            {
                Add(spin, new CylinderMesh { TopRadius = _p.WheelRadius, BottomRadius = _p.WheelRadius, Height = 0.2f, RadialSegments = 8, Rings = 1 },
                    new Color(0.08f, 0.08f, 0.08f), Vector3.Zero, new Vector3(0, 0, 90));
            }
            AddChild(pivot);
            _wheelPivots[i] = pivot;
            _wheelSpin[i] = spin;
            pivot.Position = new Vector3(left ? -_p.HalfTrack : _p.HalfTrack, _p.AttachY - _p.RestLength, front ? _p.FrontZ : _p.RearZ);
        }

        if (!_ghost)
        {
            _shadow = new MeshInstance3D
            {
                Mesh = new PlaneMesh { Size = new Vector2(2.1f, 4.2f) },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0, 0, 0, 0.45f),
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                },
                TopLevel = true,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_shadow);
        }
    }

    MeshInstance3D Ghostify(MeshInstance3D mi)
    {
        if (_ghost)
        {
            _ghostMat ??= NightGlow.Keep(new StandardMaterial3D
            {
                AlbedoColor = new Color(0.9f, 0.9f, 1f, 0.35f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            });    // a ghost glows in the dark
            mi.MaterialOverride = _ghostMat;
        }
        mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        return mi;
    }

    /// <summary>Fallback when the model file is missing.</summary>
    void BuildPrimitiveBody(Color paint)
    {
        var glass = new Color(0.18f, 0.22f, 0.26f);
        var chrome = new Color(0.75f, 0.75f, 0.72f);
        var dark = new Color(0.08f, 0.08f, 0.08f);

        // Origin = centre of gravity; sills sit ~0.25 m below it.
        Add(this, new BoxMesh { Size = new Vector3(1.6f, 0.55f, 4.05f) }, paint, new Vector3(0, 0.02f, 0));
        Add(this, new BoxMesh { Size = new Vector3(1.4f, 0.5f, 1.9f) }, paint, new Vector3(0, 0.55f, 0.25f));
        Add(this, new BoxMesh { Size = new Vector3(1.42f, 0.36f, 1.7f) }, glass, new Vector3(0, 0.54f, 0.25f));
        Add(this, new BoxMesh { Size = new Vector3(1.3f, 0.04f, 1.6f) }, paint, new Vector3(0, 0.81f, 0.25f));
        Add(this, new BoxMesh { Size = new Vector3(1.64f, 0.1f, 0.12f) }, chrome, new Vector3(0, -0.15f, -2.05f));
        Add(this, new BoxMesh { Size = new Vector3(1.64f, 0.1f, 0.12f) }, chrome, new Vector3(0, -0.15f, 2.05f));
        foreach (float s in new[] { -0.55f, 0.55f })
        {
            Add(this, new BoxMesh { Size = new Vector3(0.3f, 0.18f, 0.05f) }, new Color(1f, 0.95f, 0.7f), new Vector3(s, 0.1f, -2.03f));
            Add(this, new BoxMesh { Size = new Vector3(0.3f, 0.14f, 0.05f) }, new Color(0.7f, 0.05f, 0.05f), new Vector3(s, 0.1f, 2.03f));
        }
        Add(this, new BoxMesh { Size = new Vector3(0.1f, 0.1f, 0.3f) }, dark, new Vector3(0.45f, -0.25f, 2.1f)); // exhaust
    }

    public void Sync(CarSim car, IGround ground)
    {
        Transform = car.Transform;
        Transform3D shudder = Transform3D.Identity;
        if (Shudder > 0.01f)
        {
            // The five-cylinder at idle: the body (not the wheels: they stay on the ground) takes a kick on each of five beats of unequal strength, settling between them.
            _shudderPhase += GetProcessDeltaTime() * 8.0;
            int beat = (int)(_shudderPhase % 5.0);
            float kick = FiveBeat[beat] * MathF.Exp(-(float)(_shudderPhase - Math.Floor(_shudderPhase)) * 5f) * Shudder;
            shudder = new Transform3D(new Basis(Vector3.Right, kick * 0.012f), new Vector3(0, kick * 0.012f, 0));
            Transform *= shudder;
        }
        Transform3D unshudder = shudder.AffineInverse();      // (the wheels' pivots undo it: the wheels stand where the car stands)
        for (int i = 0; i < 4; i++)
        {
            var w = car.Wheels[i];
            var pv = _wheelPivots[i];
            if (Shudder > 0.01f)
            {
                pv.Transform = unshudder * new Transform3D(Basis.FromEuler(new Vector3(0, w.Front ? -car.SteerAngle : 0, 0)), new Vector3(w.Local.X, w.Local.Y - w.VisualLength, w.Local.Z));
                _wheelSpin[i].Rotation = new Vector3(-w.SpinAngle, 0, 0);
                continue;
            }
            pv.Position = new Vector3(w.Local.X, w.Local.Y - w.VisualLength, w.Local.Z);
            pv.Rotation = new Vector3(0, w.Front ? -car.SteerAngle : 0, 0);
            _wheelSpin[i].Rotation = new Vector3(-w.SpinAngle, 0, 0);
        }

        UpdateShadow(car.Pos, car.Forward, ground);
        Damage?.Tick((float)GetProcessDeltaTime(), ground);
    }

    void UpdateShadow(Vector3 pos, Vector3 forward, IGround ground)
    {
        if (_shadow == null) return;
        GroundSample g = ground.Sample(pos.X, pos.Z);
        float yaw = MathF.Atan2(-forward.X, -forward.Z);
        var b = new Basis(Vector3.Up, yaw);
        // Tilt to ground.
        Vector3 n = g.Normal;
        Vector3 axis = Vector3.Up.Cross(n);
        if (axis.LengthSquared() > 1e-6f) b = new Basis(axis.Normalized(), Vector3.Up.AngleTo(n)) * b;
        float height = pos.Y - g.Height;
        float fade = Math.Clamp(1.4f - height * 0.25f, 0.2f, 1f);
        ((StandardMaterial3D)_shadow.MaterialOverride).AlbedoColor = new Color(0, 0, 0, 0.45f * fade);
        _shadow.GlobalTransform = new Transform3D(b, new Vector3(pos.X, g.Height + 0.06f, pos.Z));
    }
}
