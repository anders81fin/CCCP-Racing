using System;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// A road-service rotating beacon: a dome on a short mast, lit by a turning reflector. Seen from the camera it flashes once
/// each time the beam sweeps past (two lobes, so about 1.7 flashes a second), dim in between. A halo grows with the
/// distance so the lamp stays a few pixels across at 480x270, and shows over a crest before the truck does.
/// The sweep runs on the wall clock and is decoration only: it is not on the world tape, so a replay turns the beam
/// with its own phase. The truck's registered beacon part stays where it was, empty, so old tapes still fit.
/// </summary>
public partial class RotatingBeacon : Node3D
{
    const float Mast = 0.45f, DomeR = 0.24f, RevPerSecond = 0.85f;      // two lobes a revolution: BeaconBeep beeps at twice this
    const float HaloMin = 1.3f, HaloPerMetre = 0.016f, HaloMax = 7f, FarLimit = 700f, BeamLength = 35f, BeamHalfWidth = 8f, BeamRootWidth = 0.3f;
    static readonly Color Amber = new(1f, 0.6f, 0.1f), Hot = new(1f, 0.88f, 0.55f);

    readonly float _phase;
    Node3D _pivot;
    StandardMaterial3D _dome, _halo;
    MeshInstance3D _haloMesh, _beam;
    readonly System.Collections.Generic.List<SpotLight3D> _lamps = new();      // the reflector's two real lights: they sweep a patch of amber over the snow and the trees
    const float LampRange = 35f, LampEnergy = 9f, LampNear = 140f;
    ShaderMaterial _beamMat;
    static GradientTexture2D _glow;

    RotatingBeacon(float phase) => _phase = phase;

    /// <summary>The sweep's phase offset (radians), so a LAN client can rebuild this beacon locally.</summary>
    public float Phase => _phase;
    /// <summary>A LAN client's own copy of a host beacon (no beeper): the beam shader, pulse and halo depend on this viewer's camera and
    /// night level, so they are made here rather than replicated as frozen meshes. The caller poses it.</summary>
    public static RotatingBeacon Replica(float phase)
    {
        var b = new RotatingBeacon(phase) { Name = "LanBeacon" };
        b.Build();
        return b;
    }

    /// <summary>Puts a beacon on <paramref name="model"/> with its dome <see cref="Mast"/> m over the old beacon spot.</summary>
    public static void Add(Node3D model, Vector3 oldSpot, float phase, Func<bool> moving = null)
    {
        var b = new RotatingBeacon(phase) { Name = "RotatingBeacon", Position = oldSpot };
        model.AddChild(b);
        b.Build();
        b.AddChild(new CCCPRacing.Audio.BeaconBeep { Phase = phase / Mathf.Tau, Moving = moving, Position = new Vector3(0, Mast, 0) });
    }

    void Build()
    {
        AddChild(Props.Part(Props.Box(0.12f, Mast, 0.12f), new Color(0.06f, 0.06f, 0.06f), new Vector3(0, Mast * 0.5f - 0.1f, 0)));
        _dome = NightGlow.Keep(new StandardMaterial3D { AlbedoColor = Amber, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded });
        Vector3 top = new(0, Mast + DomeR - 0.1f, 0);
        AddChild(new MeshInstance3D { Mesh = Props.Ball(DomeR, 8), Position = top, MaterialOverride = _dome, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });

        // The reflector: a bright lump on each side of the dome, turning with the beam.
        _pivot = new Node3D { Position = top };
        AddChild(_pivot);
        foreach (float yaw in new[] { -90f, 90f })
        {
            var lamp = new SpotLight3D
            {
                LightColor = new Color(1f, 0.6f, 0.16f), SpotRange = LampRange, SpotAngle = 32f, SpotAttenuation = 0.6f, SpotAngleAttenuation = 1.1f,
                ShadowEnabled = false, LightSpecular = 0f, LightEnergy = 0f, Visible = false,
                RotationDegrees = new Vector3(-9f, yaw, 0),      // along the pivot's X axis, tipped a little down
            };
            _pivot.AddChild(lamp);
            _lamps.Add(lamp);
        }
        var hot = NightGlow.Keep(new StandardMaterial3D { AlbedoColor = Hot, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded });
        foreach (float s in new[] { -1f, 1f })
            _pivot.AddChild(new MeshInstance3D { Mesh = Props.Box(0.1f, 0.12f, 0.14f), Position = new Vector3(s * (DomeR + 0.02f), 0, 0), MaterialOverride = hot, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });

        // The beam: a soft shaft each way along the reflector's axis. A strip that turns about that axis to face the camera, so it reads as
        // a round beam of light from anywhere: narrow and bright at the lamp, widening and fading with the length, soft at the edges. Dusk and night only.
        _beamMat = NightGlow.Keep(new ShaderMaterial { Shader = BeamShader() });
        _beam = new MeshInstance3D { Mesh = BeamMesh(), MaterialOverride = _beamMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        _pivot.AddChild(_beam);

        _halo = NightGlow.Keep(new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            AlbedoTexture = Glow(),
            AlbedoColor = new Color(1, 1, 1, 0),
            DisableFog = true,
        });
        _haloMesh = new MeshInstance3D { Mesh = new QuadMesh { Size = Vector2.One }, Position = top, MaterialOverride = _halo, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_haloMesh);
    }

    static Shader _beamShader;
    static Shader BeamShader()
    {
        if (_beamShader != null) return _beamShader;
        _beamShader = new Shader { Code = @"
shader_type spatial;
render_mode blend_add, unshaded, cull_disabled, depth_draw_never, fog_disabled;
uniform float strength = 0.0;
uniform vec3 tint : source_color = vec3(1.0, 0.62, 0.14);
void vertex() {
    // the strip lies along local X: turn it about that axis to face the camera
    vec3 cam = (inverse(MODEL_MATRIX) * vec4(CAMERA_POSITION_WORLD, 1.0)).xyz;
    vec3 perp = vec3(0.0, cam.y, cam.z);
    float l = length(perp);
    perp = l > 0.001 ? perp / l : vec3(0.0, 0.0, 1.0);
    vec3 side = cross(vec3(1.0, 0.0, 0.0), perp);
    VERTEX = vec3(VERTEX.x, 0.0, 0.0) + side * VERTEX.y;
}
void fragment() {
    float across = 1.0 - abs(UV.y * 2.0 - 1.0);
    float along = 1.0 - UV.x;
    // A cone of light, not a ray: the same softness across its whole width, thinning only at the edge and towards the far end.
    float body = smoothstep(0.0, 0.9, across) * pow(along, 1.1);
    float core = pow(across, 2.0) * pow(along, 2.5);
    ALBEDO = tint * (0.85 + 0.6 * core);
    ALPHA = clamp(body * strength * 0.5, 0.0, 1.0);
}
" };
        return _beamShader;
    }

    /// <summary>
    /// Two strips, along +X and -X from the lamp: a point's width at the root, the full width at the tip (UV x 0 at the lamp .. 1 at the tip, UV y 0 .. 1 across with
    /// .5 on the axis). Each is a grid with a vertex row on the axis: two triangles over a trapezoid skew the UV so the brightest line leaves the axis (and leans
    /// the opposite way on the other half).
    /// </summary>
    static ArrayMesh BeamMesh()
    {
        const int Steps = 6;
        var v = new System.Collections.Generic.List<Vector3>();
        var uv = new System.Collections.Generic.List<Vector2>();
        void Tri(Vector3 a, Vector2 ua, Vector3 b, Vector2 ub, Vector3 c, Vector2 uc) { v.Add(a); uv.Add(ua); v.Add(b); uv.Add(ub); v.Add(c); uv.Add(uc); }
        foreach (float d in new[] { -1f, 1f })
            for (int i = 0; i < Steps; i++)
            {
                float u0 = i / (float)Steps, u1 = (i + 1) / (float)Steps;
                float x0 = d * BeamLength * u0, x1 = d * BeamLength * u1;
                float w0 = Mathf.Lerp(BeamRootWidth, BeamHalfWidth, u0), w1 = Mathf.Lerp(BeamRootWidth, BeamHalfWidth, u1);
                foreach (float side in new[] { -1f, 1f })
                {
                    // one half of the strip: from the axis (y = 0, UV .5) to the edge (y = side * w, UV 0 or 1)
                    float e = side < 0 ? 0f : 1f;
                    Vector3 a0 = new(x0, 0, 0), a1 = new(x1, 0, 0), b0 = new(x0, side * w0, 0), b1 = new(x1, side * w1, 0);
                    Vector2 ua0 = new(u0, .5f), ua1 = new(u1, .5f), ub0 = new(u0, e), ub1 = new(u1, e);
                    Tri(a0, ua0, a1, ua1, b1, ub1);
                    Tri(a0, ua0, b1, ub1, b0, ub0);
                }
            }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = v.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uv.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>A soft round glow, white in the middle and amber at the edge.</summary>
    static GradientTexture2D Glow()
    {
        if (_glow != null) return _glow;
        var g = new Gradient();
        g.SetOffset(0, 0f); g.SetColor(0, new Color(1f, 0.95f, 0.8f, 1f));
        g.SetOffset(1, 1f); g.SetColor(1, new Color(1f, 0.55f, 0.08f, 0f));
        g.AddPoint(0.3f, new Color(1f, 0.7f, 0.2f, 0.55f));
        return _glow = new GradientTexture2D { Gradient = g, Width = 64, Height = 64, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f) };
    }

    public override void _Process(double delta)
    {
        var cam = GetViewport()?.GetCamera3D();
        if (cam == null) return;
        Vector3 here = _haloMesh.GlobalPosition, toCam = cam.GlobalPosition - here;
        float dist = toCam.Length();
        _halo.AlbedoColor = new Color(1, 1, 1, 0);
        if (dist > FarLimit) { foreach (var l in _lamps) l.Visible = false; return; }

        float angle = (float)(Time.GetTicksMsec() * 0.001 * RevPerSecond * Math.Tau) + _phase;
        _pivot.Rotation = new Vector3(0, angle, 0);
        // The beam leaves along the pivot's X axis, both ways: the flash is strongest when the camera is in line with it.
        Vector3 beam = _pivot.GlobalTransform.Basis.X.Normalized();
        Vector3 flat = new(toCam.X, 0, toCam.Z);
        float along = flat.LengthSquared() > 1e-4f ? beam.Dot(flat.Normalized()) : 0f;
        float lobe = MathF.Pow(along * along, 3f);
        float pulse = 0.18f + 0.82f * lobe;
        float night = 1f + 0.6f * NightGlow.Level;
        // The beam shows from dusk, full at night, and only while the lamp is near enough to matter.
        float dark = Mathf.SmoothStep(0.12f, 0.6f, NightGlow.Level);
        _beam.Visible = dark > 0.01f && dist < 450f;
        _beamMat.SetShaderParameter("strength", dark);
        // Real lights only from dusk and only near the camera (each one costs a pass over the snow).
        bool lit = dark > 0.01f && dist < LampNear;
        foreach (var l in _lamps) { l.Visible = lit; l.LightEnergy = LampEnergy * dark; }

        _dome.AlbedoColor = Amber.Lerp(Hot, lobe) * (0.45f + 0.55f * pulse);
        float size = Math.Clamp(dist * HaloPerMetre, HaloMin, HaloMax);
        _haloMesh.Scale = new Vector3(size, size, 1f);
        _halo.AlbedoColor = new Color(1, 1, 1, Math.Clamp(pulse * pulse * night, 0f, 1f));
    }
}
