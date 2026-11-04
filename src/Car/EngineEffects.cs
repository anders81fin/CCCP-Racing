using System;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Car;

/// <summary>
/// What a dying Pyatyorka looks like: smoke from under the bonnet when the engine is sick, oil
/// spurting out and a black slick on the road when it seizes, steam afterwards; and for a car
/// blown up by a direct hit a fireball, a blackened body and flames with black smoke that keep
/// going. Everything is exaggerated so it reads from the high chase camera.
/// </summary>
public partial class EngineEffects : Node3D
{
    static readonly Vector3 Bonnet = new(0, 0.5f, -1.35f);     // body frame, over the engine
    static readonly Vector3 Sump = new(0, -0.3f, -1.3f);
    static readonly Color[] FireCols = { new(1f, 0.92f, 0.5f), new(1f, 0.6f, 0.15f), new(1f, 0.35f, 0.05f) };
    static readonly Color OilCol = new(0.04f, 0.035f, 0.03f);

    TrackData _track;
    TyreEffects _fx;
    MeshInstance3D _slick;
    StandardMaterial3D _burnt;
    readonly RandomNumberGenerator _rng = new() { Seed = 1953 };
    float _smokeAcc, _fireAcc, _sinceDeath = -1, _sinceBoom = -1, _slickSize, _slickTarget;
    CarVisual _blackened;

    public void Init(TrackData track, TyreEffects fx)
    {
        _track = track;
        _fx = fx;
        _slick = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = Vector2.One },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoTexture = SlickTexture(),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            TopLevel = true,
            Visible = false,
        };
        AddChild(_slick);
        // Scorched paint: laid over every part of the body, the original paint just shows through.
        _burnt = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.045f, 0.04f, 0.035f, 0.93f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
    }

    /// <summary>A new run: clean car, no oil on the road.</summary>
    public void Reset()
    {
        _sinceDeath = _sinceBoom = -1;
        _slick.Visible = false;
        _slickSize = _slickTarget = 0;
        if (_blackened != null) SetOverlay(_blackened, null);
        _blackened = null;
    }

    /// <summary>Engine seized: a gush of oil under the car and a cloud of steam.</summary>
    public void Seize(CarSim car)
    {
        _sinceDeath = 0;
        Vector3 sump = car.Pos + car.Rot * Sump;
        for (int i = 0; i < 26; i++)
            _fx.Burst(sump, car.Vel * 0.5f + new Vector3(_rng.RandfRange(-3, 3), _rng.RandfRange(1, 4), _rng.RandfRange(-3, 3)),
                OilCol, 1f, _rng.RandfRange(0.25f, 0.45f), 0.3f, _rng.RandfRange(0.8f, 1.4f), 14f, 0.3f, true);
        Steam(car, 10);
        PlaceSlick(sump, 0.8f, 4.2f);
    }

    /// <summary>Direct hit: fireball, debris, the body burnt black.</summary>
    public void Explode(CarSim car, CarVisual vis, Vector3 at)
    {
        _sinceBoom = 0;
        _sinceDeath = 0;
        Vector3 c = car.Pos + Vector3.Up * 0.4f;
        for (int i = 0; i < 70; i++)
            _fx.Burst(c + Rand(0.8f, 0.4f, 1.4f), new Vector3(_rng.RandfRange(-9, 9), _rng.RandfRange(2, 11), _rng.RandfRange(-9, 9)),
                FireCols[i % 3], 1f, _rng.RandfRange(1.4f, 2.4f), 0.4f, _rng.RandfRange(0.4f, 0.8f), -1f, 2.2f, false);
        for (int i = 0; i < 30; i++)
            _fx.Burst(c, new Vector3(_rng.RandfRange(-10, 10), _rng.RandfRange(5, 14), _rng.RandfRange(-10, 10)),
                i % 2 == 0 ? new Color(0.12f, 0.12f, 0.12f) : new Color(0.55f, 0.1f, 0.08f), 1f, _rng.RandfRange(0.2f, 0.4f), 0.3f,
                _rng.RandfRange(1f, 1.8f), 16f, 0.2f, true);
        for (int i = 0; i < 16; i++)
            _fx.Smoke(c + Rand(1.5f, 1f, 1.5f), new Vector3(_rng.RandfRange(-1.5f, 1.5f), _rng.RandfRange(2f, 5f), _rng.RandfRange(-1.5f, 1.5f)),
                new Color(0.08f, 0.07f, 0.07f), 0.85f, 1.5f, 5.5f, _rng.RandfRange(2.5f, 4f));
        // The fuel goes too: a burning pool round the car.
        PlaceSlick(car.Pos, 3f, 6f);
        SetOverlay(vis, _burnt);
        _blackened = vis;
        _ = at;
    }

    /// <summary>Per physics tick: sick-engine smoke, post-mortem steam, and the fire of a wreck.</summary>
    public void Tick(float dt, CarSim car, EngineHealth h)
    {
        // Slick spreads over a few seconds; on a slope it is just a puddle, fine.
        if (_slick.Visible && _slickSize < _slickTarget)
        {
            _slickSize = Math.Min(_slickTarget, _slickSize + dt * 1.2f);
            SizeSlick();
        }

        Vector3 bonnet = car.Pos + car.Rot * Bonnet;
        if (h.Smoking)
        {
            // Thicker and darker the closer it gets to the end.
            float k = Math.Clamp((EngineHealth.SmokeAt - h.Health) / EngineHealth.SmokeAt, 0f, 1f);
            _smokeAcc += dt * (22f + 26f * k);
            while (_smokeAcc >= 1f)
            {
                _smokeAcc -= 1f;
                float grey = 0.4f - 0.3f * k;
                _fx.Smoke(bonnet + Rand(0.4f, 0.1f, 0.4f), car.Vel * 0.6f + new Vector3(_rng.RandfRange(-0.5f, 0.5f), 2.4f + k, _rng.RandfRange(-0.5f, 0.5f)),
                    new Color(grey, grey, grey * 0.95f), 0.75f + 0.2f * k, 0.9f + 0.3f * k, 2.6f + 1.6f * k, 1.2f + 0.8f * k);
            }
        }

        if (_sinceDeath < 0) return;
        _sinceDeath += dt;
        if (_sinceBoom >= 0)
        {
            // Burning wreck: flames licking out of the body and a column of black smoke.
            _sinceBoom += dt;
            _fireAcc += dt * 70f;
            while (_fireAcc >= 1f)
            {
                _fireAcc -= 1f;
                Vector3 p = car.Pos + Vector3.Up * 0.4f + car.Rot * new Vector3(_rng.RandfRange(-0.7f, 0.7f), 0.2f, _rng.RandfRange(-1.7f, 1.3f));
                _fx.Burst(p, car.Vel * 0.9f + new Vector3(_rng.RandfRange(-0.6f, 0.6f), _rng.RandfRange(2.5f, 5f), _rng.RandfRange(-0.6f, 0.6f)),
                    FireCols[_rng.RandiRange(0, 2)], 1f, _rng.RandfRange(0.9f, 1.5f), 0.2f, _rng.RandfRange(0.35f, 0.7f), -2f, 1.5f, false);
                if (_rng.Randf() < 0.4f)
                    _fx.Smoke(p + Vector3.Up * 1.2f, car.Vel * 0.5f + new Vector3(_rng.RandfRange(-0.4f, 0.4f), _rng.RandfRange(3f, 4.5f), _rng.RandfRange(-0.4f, 0.4f)),
                        new Color(0.05f, 0.05f, 0.05f), 0.85f, 1.2f, 5f, _rng.RandfRange(2.5f, 4f));
            }
        }
        else if (_sinceDeath < 8f)
        {
            // Seized: steam and oil smoke hissing out of the bonnet, fading off.
            _smokeAcc += dt * 18f * (1f - _sinceDeath / 8f);
            while (_smokeAcc >= 1f)
            {
                _smokeAcc -= 1f;
                Steam(car, 1);
            }
        }
    }

    void PlaceSlick(Vector3 at, float size, float target)
    {
        _slickPos = new Vector3(at.X, _track.MeshHeight(at.X, at.Z) + 0.08f, at.Z);
        _slickSize = size;
        _slickTarget = target;
        _slick.Visible = true;
        SizeSlick();
    }

    Vector3 _slickPos;

    void SizeSlick() =>
        _slick.GlobalTransform = new Transform3D(new Basis(Vector3.Up, _slickPos.X * 0.37f) * Basis.FromScale(Vector3.One * _slickSize) * new Basis(Vector3.Right, -MathF.PI / 2f), _slickPos);

    void Steam(CarSim car, int n)
    {
        Vector3 bonnet = car.Pos + car.Rot * Bonnet;
        for (int i = 0; i < n; i++)
        {
            bool white = _rng.Randf() < 0.6f;
            _fx.Smoke(bonnet + Rand(0.5f, 0.1f, 0.5f), new Vector3(_rng.RandfRange(-1f, 1f), _rng.RandfRange(2f, 3.5f), _rng.RandfRange(-1f, 1f)),
                white ? new Color(0.92f, 0.92f, 0.9f) : new Color(0.25f, 0.24f, 0.22f), 0.7f, 0.7f, 3f, _rng.RandfRange(1.2f, 2.2f));
        }
    }

    static void SetOverlay(Node n, Material m)
    {
        if (n is GeometryInstance3D g && !g.TopLevel) g.MaterialOverlay = m;
        foreach (Node c in n.GetChildren()) SetOverlay(c, m);
    }

    Vector3 Rand(float x, float y, float z) =>
        new(_rng.RandfRange(-x, x), _rng.RandfRange(-y, y), _rng.RandfRange(-z, z));

    /// <summary>Ragged black puddle with a few dull brown sheens.</summary>
    static ImageTexture SlickTexture()
    {
        const int S = 32;
        var img = Image.CreateEmpty(S, S, false, Image.Format.Rgba8);
        var rng = new RandomNumberGenerator { Seed = 66 };
        float[] lobes = new float[7];
        for (int i = 0; i < lobes.Length; i++) lobes[i] = rng.RandfRange(0.7f, 1f);
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            var d = new Vector2(x - S / 2f + 0.5f, y - S / 2f + 0.5f) / (S / 2f);
            float a = (d.Angle() + MathF.PI) / MathF.Tau * lobes.Length;
            int i0 = (int)a % lobes.Length, i1 = (i0 + 1) % lobes.Length;
            float r = Mathf.Lerp(lobes[i0], lobes[i1], a - MathF.Floor(a));
            float len = d.Length();
            if (len > r) { img.SetPixel(x, y, new Color(0, 0, 0, 0)); continue; }
            bool sheen = rng.Randf() < 0.05f;
            img.SetPixel(x, y, sheen ? new Color(0.25f, 0.2f, 0.12f, 0.9f) : new Color(0.03f, 0.03f, 0.03f, len > r - 0.12f ? 0.7f : 0.92f));
        }
        return ImageTexture.CreateFromImage(img);
    }
}
