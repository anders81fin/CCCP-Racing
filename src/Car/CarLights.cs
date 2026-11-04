using System;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Car;

/// <summary>
/// The Pyatyorka's lamps after dark (Race.DayNight): two headlight beams (or one combined, cheaper),
/// glowing lamp glass, tail lights that brighten under braking. A headlight the damage has popped
/// out (CarDamage) goes dark; a new car or the mechanic's repair puts it back. Off: nothing shows.
/// Lives in the car's frame (child of CarVisual).
/// </summary>
public partial class CarLights : Node3D
{
    // Lamp centres in the body frame (build_pyatyorka.py: head and red tail lamps, scaled, CG shifted).
    static readonly Vector3 HeadL = new(-0.59f, 0.03f, -1.97f), HeadR = new(0.59f, 0.03f, -1.97f);
    static readonly Vector3 TailL = new(-0.78f, 0.02f, 1.96f), TailR = new(0.78f, 0.02f, 1.96f);
    static readonly Color Beam = new(1f, 0.93f, 0.74f);
    // The SPUTNIK's four auxiliary lamps on the nose bar (build_pyatyorka.py: x 0.43 and 0.15 in design space, scaled): glow on each, a long beam over each pair.
    static readonly Vector3[] AuxAt = { new(-0.477f, 0.405f, -1.9f), new(-0.167f, 0.405f, -1.9f), new(0.167f, 0.405f, -1.9f), new(0.477f, 0.405f, -1.9f) };
    const float AuxEnergy = 9f, AuxRange = 85f, AuxAngle = 14f;
    SpotLight3D[] _aux;      // (built the first time a kit is worn: the other cars carry no extra lights)
    readonly MeshInstance3D[] _auxLamps = new MeshInstance3D[4];
    StandardMaterial3D _auxMat;
    const float BeamEnergy = 7f, BeamRange = 60f, BeamAngle = 26f, BeamDip = -1.2f;

    readonly SpotLight3D[] _beams;
    readonly MeshInstance3D[] _lamps = new MeshInstance3D[2], _tails = new MeshInstance3D[2];
    readonly StandardMaterial3D[] _lampMat = new StandardMaterial3D[2];
    readonly StandardMaterial3D _tailMat;
    readonly bool _combined;
    readonly RandomNumberGenerator _rng = new();
    float _flicker;

    /// <summary>Headlights 0 (off) .. 1; set by the night.</summary>
    public float Level;
    /// <summary>Test: DAYNIGHTDBG prints the beams' state when it changes.</summary>
    string _last;

    /// <param name="combined">one spot between the lamps instead of two (half the lights to shade)</param>
    public CarLights(bool combined)
    {
        Name = "Lights";
        _combined = combined;
        _beams = new SpotLight3D[combined ? 1 : 2];
        for (int i = 0; i < _beams.Length; i++)
        {
            Vector3 at = combined ? (HeadL + HeadR) * 0.5f : i == 0 ? HeadL : HeadR;
            _beams[i] = new SpotLight3D
            {
                LightColor = Beam,
                SpotRange = BeamRange,
                SpotAngle = combined ? BeamAngle + 4f : BeamAngle,
                SpotAttenuation = 0.6f,
                SpotAngleAttenuation = 1.4f,
                ShadowEnabled = false,
                LightSpecular = 0f,
                Position = at + new Vector3(0, 0, -0.1f),
                RotationDegrees = new Vector3(BeamDip, 0, 0),     // a spot shines along -Z: forward, dipped
                Visible = false,
            };
            AddChild(_beams[i]);
        }
        for (int i = 0; i < 2; i++)
        {
            _lampMat[i] = Glow(new Color(1f, 0.97f, 0.85f));
            _lamps[i] = Blob(new SphereMesh { Radius = 0.17f, Height = 0.26f, RadialSegments = 8, Rings = 4 }, _lampMat[i], i == 0 ? HeadL : HeadR);
        }
        _tailMat = Glow(new Color(0.55f, 0.04f, 0.02f));
        for (int i = 0; i < 2; i++) _tails[i] = Blob(new BoxMesh { Size = new Vector3(0.3f, 0.24f, 0.16f) }, _tailMat, (i == 0 ? TailL : TailR) + new Vector3(0, 0.04f, 0));
    }

    void EnsureAux()
    {
        if (_aux != null) return;
        bool combined = _combined;
        _aux = new SpotLight3D[combined ? 1 : 2];
        for (int i = 0; i < _aux.Length; i++)
        {
            float x = combined ? 0f : i == 0 ? -0.32f : 0.32f;
            _aux[i] = new SpotLight3D
            {
                LightColor = new Color(1f, 0.98f, 0.9f), SpotRange = AuxRange, SpotAngle = combined ? AuxAngle + 5f : AuxAngle, SpotAttenuation = 0.5f, SpotAngleAttenuation = 1.2f,
                ShadowEnabled = false, LightSpecular = 0f, Position = new Vector3(x, 0.405f, -1.95f), RotationDegrees = new Vector3(-0.6f, 0, 0), Visible = false,
            };
            AddChild(_aux[i]);
        }
        _auxMat = Glow(new Color(1f, 0.98f, 0.88f));
        for (int i = 0; i < 4; i++) _auxLamps[i] = Blob(new SphereMesh { Radius = 0.085f, Height = 0.12f, RadialSegments = 8, Rings = 4 }, _auxMat, AuxAt[i]);
    }

    static StandardMaterial3D Glow(Color c) => NightGlow.Keep(new StandardMaterial3D
    {
        AlbedoColor = c,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
    });

    MeshInstance3D Blob(Mesh mesh, Material mat, Vector3 at)
    {
        var m = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = at, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(m);
        return m;
    }

    /// <summary>Per frame: brake 0..1 (the pedal), damage for the headlights' state (null: both work).</summary>
    public void Tick(float dt, float brake, CarDamage damage)
        => Tick(dt, brake, damage?.HeadlightWorks(true) ?? true, damage?.HeadlightWorks(false) ?? true, damage?.KitWorn ?? false);

    /// <summary>Network puppets receive each lamp's cosmetic state from the owning driver's snapshots.</summary>
    public void Tick(float dt, float brake, bool left, bool right, bool aux = false)
    {
        bool on = Level > 0.001f;
        bool auxOn = on && aux;
        if (auxOn) EnsureAux();
        if (_aux != null)
        {
            foreach (var a in _aux) { a.Visible = auxOn; a.LightEnergy = AuxEnergy * Level; }
            foreach (var l in _auxLamps) l.Visible = auxOn;
            _auxMat.AlbedoColor = new Color(1f, 0.98f, 0.88f, Level);
        }
        // A lamp just knocked out flickers for a moment before it dies (the flicker restarts it).
        _flicker = Math.Max(0, _flicker - dt);
        float k = Level * (_flicker > 0 ? (_rng.Randf() < 0.5f ? 0.2f : 1f) : 1f);
        for (int i = 0; i < 2; i++)
        {
            bool works = i == 0 ? left : right;
            _lamps[i].Visible = on && works;
            _lampMat[i].AlbedoColor = new Color(1f, 0.97f, 0.85f, k);
        }
        if (_combined)
        {
            int n = (left ? 1 : 0) + (right ? 1 : 0);
            var b = _beams[0];
            b.Visible = on && n > 0;
            b.LightEnergy = BeamEnergy * k * (n == 2 ? 1.6f : 0.9f);
            // One lamp left: the beam moves over to its side.
            b.Position = (n == 2 ? (HeadL + HeadR) * 0.5f : left ? HeadL : HeadR) + new Vector3(0, 0, -0.1f);
        }
        else
            for (int i = 0; i < 2; i++)
            {
                _beams[i].Visible = on && (i == 0 ? left : right);
                _beams[i].LightEnergy = BeamEnergy * k;
            }
        float glow = 0.7f + 0.3f * Math.Clamp(brake * 1.5f, 0f, 1f);
        _tailMat.AlbedoColor = new Color(glow, glow * 0.08f * (1f + brake), glow * 0.05f, Level);
        foreach (var t in _tails)
        {
            t.Visible = on;
            t.Scale = Vector3.One * (1f + 0.35f * Math.Clamp(brake * 1.5f, 0f, 1f));
        }
        string state = !on ? "off" : $"{(left ? "L" : "-")}{(right ? "R" : "-")}";
        if (state != _last && OS.HasEnvironment("DAYNIGHTDBG")) GD.Print($"car lights: {state}");
        if (_last != null && state.Length == 2 && _last.Length == 2 && state != _last && (state[0] == '-' && _last[0] == 'L' || state[1] == '-' && _last[1] == 'R')) _flicker = 0.35f;
        _last = state;
    }
}
