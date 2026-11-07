using System;
using System.Collections.Generic;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// A labour detail marched out of the camp (TrackDef.Column): a guard in front, prisoners in grey
/// padded jackets two metres apart, heads down and hands behind their backs, and a guard at the
/// back with a submachine gun levelled at them. They trudge round the lap with the race along
/// the right-hand edge of the road, and step off into the snow bank for a snowplough from either
/// side or a car coming fast. A car that hits one knocks him over (Knockdown, like the Kolkhoz workers):
/// he tips away from the car, lies a while where he fell, gets up and jogs back to his place in the column.
/// </summary>
public partial class PrisonerColumn : Node3D
{
    const float Walk = 1.4f, Spacing = 2.1f, SizeScale = 1.6f, Radius = 0.42f * SizeScale;
    const float EdgeIn = 1.0f, Aside = 2.4f;
    static readonly Color Quilt = new(0.33f, 0.34f, 0.33f), QuiltDark = new(0.26f, 0.27f, 0.27f), Felt = new(0.2f, 0.19f, 0.18f);
    static readonly Color Skin = new(0.84f, 0.66f, 0.56f), Greatcoat = new(0.36f, 0.33f, 0.22f), Fur = new(0.3f, 0.24f, 0.18f);
    static readonly Color Gun = new(0.12f, 0.12f, 0.13f), Wood = new(0.4f, 0.26f, 0.15f), Star = new(0.85f, 0.12f, 0.08f);
    static readonly string[] Barks = { "ДАВАЙ!", "ШАГОМ МАРШ!", "НЕ ОТСТАВАТЬ!" };

    class Walker
    {
        public Node3D Root, Body, Head, LegL, LegR, ArmL, ArmR;
        public float Back, Side, Phase;      // metres behind the head of the column; how far it has stepped aside (0..1)
        public bool Guard, Gunner;
        public int Obs;
        public Knockdown Down = new();
        public Vector2 Pos;            // where he stands now (a man knocked over is left behind by the march)
        public bool Straggling;        // knocked over: lying, or jogging back to his place
    }

    TrackData _t;
    readonly List<Walker> _walkers = new();
    float _s, _startS, _clock, _barkCd, _bark;
    /// <summary>The snowploughs to keep clear of (null: none).</summary>
    public SnowPloughs Ploughs;

    public void Init(TrackData t, float startS, int prisoners)
    {
        _t = t;
        _startS = startS;
        for (int k = 0; k < prisoners + 2; k++)
        {
            bool guard = k == 0 || k == prisoners + 1;
            var w = new Walker { Back = k * Spacing + (k == prisoners + 1 ? 1.2f : 0f), Guard = guard, Gunner = k == prisoners + 1, Phase = k * 0.7f, Obs = t.Obstacles.Count };
            t.Obstacles.Add(new Obstacle(new Vector2(-1e4f, -1e4f), 0.1f));
            w.Down.Bind(t, w.Obs);
            Build(w, k);
            AddChild(w.Root);
            // The world tape: the figure (it moves, the PrisonerColumn node does not), head, arms and legs.
            string n = $"{k + 1}/";
            Tape.Part(w.Root, n + "figure"); Tape.Part(w.Body, n + "body"); Tape.Part(w.Head, n + "head");
            Tape.Part(w.ArmL, n + "armL"); Tape.Part(w.ArmR, n + "armR"); Tape.Part(w.LegL, n + "legL"); Tape.Part(w.LegR, n + "legR");
            _walkers.Add(w);
        }
        Tape.Version(this, 1); Tape.Part(this, "root");
        Reset();
    }

    public void Reset()
    {
        _s = _startS;
        _barkCd = 4f;
        _bark = 0;
        foreach (var w in _walkers) { w.Side = 0; w.Down.Reset(); w.Straggling = false; }
        Tick(0, null);
    }

    /// <summary>What knocks the column's men over (Breakables.AddKnock).</summary>
    public IEnumerable<(int obstacle, Func<Vector3, bool> hit)> Knocks()
    {
        foreach (var w in _walkers) yield return (w.Obs, w.Down.Hit);
    }

    /// <summary>Return the marching detail to the camp for its scenic introduction.</summary>
    public void CueSchoolCamp()
    {
        Reset();
        _s = 1218f;
        Tick(0, null);
    }

    /// <summary>Walk on; step aside for what is coming. car may be null.</summary>
    public void Tick(float dt, CarSim car)
    {
        int n = _t.Center.Length;
        _clock += dt;
        _s = (_s + Walk * dt) % n;
        _barkCd -= dt;
        _bark -= dt;
        bool carNear = false;
        foreach (var w in _walkers)
        {
            float s = ((_s - w.Back) % n + n) % n;
            int i = (int)s;
            Vector2 dir = _t.CenterDir(i), right = new(-dir.Y, dir.X);
            Vector2 edge = _t.Center[i].Lerp(_t.Center[(i + 1) % n], s - i) + right * (_t.HalfWidth[i] - EdgeIn);
            // Anything big coming along the road from either way, or the car fast: into the bank.
            bool clear = true;
            if (Ploughs != null)
                foreach (var at in Ploughs.Positions)
                    if (at.DistanceTo(edge) < 28f) { clear = false; break; }
            if (car != null)
            {
                Vector2 cp = new(car.Pos.X, car.Pos.Z), rel = edge - cp;
                float d = rel.Length();
                if (d < 40f) carNear = true;
                Vector2 v = new(car.Vel.X, car.Vel.Z);
                if (d < 25f && car.Speed > 7f && v.Dot(rel) > 0) clear = false;
            }
            w.Side = Mathf.MoveToward(w.Side, clear ? 0f : 1f, (clear ? 0.6f : 2.2f) * dt);
            Vector2 slot = edge + right * (Aside * Mathf.SmoothStep(0f, 1f, w.Side));
            if (w.Down.Down) w.Straggling = true;
            w.Down.Tick(dt);
            Vector2 pos = slot;
            bool jog = false;
            if (w.Down.Down) pos = w.Pos;      // lies where he fell while the column marches on
            else if (w.Straggling)
            {
                // Up again: a jog back to his place (faster than the march).
                Vector2 to = slot - w.Pos;
                float step = 5f * dt;
                if (to.Length() <= step) w.Straggling = false;
                else { pos = w.Pos + to.Normalized() * step; jog = true; }
            }
            w.Pos = pos;
            w.Phase += dt * (w.Down.Down ? 0f : jog || (w.Side > 0.02f && w.Side < 0.98f) ? 11f : 6.5f);
            Pose(w, pos, dir, right);
            if (!w.Down.Down) _t.Obstacles[w.Obs] = new Obstacle(pos, Radius, dir * Walk);      // (a man knocked over has his obstacle out of the way)
        }
        // The guard at the back hurries them along when a car is about.
        if (carNear && _barkCd <= 0) { _bark = 1.8f; _barkCd = 7f; _barkText = Barks[(int)(_clock * 7f) % Barks.Length]; }
    }

    string _barkText = "";

    /// <summary>The rear guard's shout, while it lasts.</summary>
    public IEnumerable<(Vector3 pos, string text)> ActiveShouts()
    {
        if (_bark > 0 && _walkers.Count > 0) yield return (_walkers[^1].Root.GlobalPosition + Vector3.Up * 2.4f * SizeScale, _barkText);
    }

    void Pose(Walker w, Vector2 pos, Vector2 dir, Vector2 right)
    {
        float y = _t.MeshHeight(pos.X, pos.Y);
        Vector2 face = w.Side > 0.05f && w.Side < 0.95f ? (dir + right * 0.8f).Normalized() : dir;
        w.Root.GlobalTransform = new Transform3D(w.Down.Tilt * new Basis(Vector3.Up, TrackData.YawFor(face)).Scaled(Vector3.One * SizeScale), new Vector3(pos.X, y, pos.Y));
        float sw = MathF.Sin(w.Phase);
        w.LegL.Rotation = new Vector3(sw * 0.5f, 0, 0);
        w.LegR.Rotation = new Vector3(-sw * 0.5f, 0, 0);
        w.Body.Position = new Vector3(0, MathF.Abs(sw) * 0.03f, 0);
        if (w.Guard)
        {
            w.Body.Rotation = new Vector3(0, 0, sw * 0.03f);
            if (!w.Gunner)
            {
                w.ArmL.Rotation = new Vector3(-sw * 0.4f, 0, 0);
                w.Head.Rotation = new Vector3(0, MathF.Sin(_clock * 0.4f) > 0.85f ? 2.6f : 0f, 0);   // now and then a look back
            }
        }
        else
        {
            // Bent forward, heads down, hands behind their backs.
            w.Body.Rotation = new Vector3(0.18f, 0, sw * 0.05f);
            w.Head.Rotation = new Vector3(0.35f, 0, 0);
        }
    }

    // ------------------------------------------------------------ figures

    void Build(Walker w, int k)
    {
        var root = new Node3D();
        var body = new Node3D();
        root.AddChild(body);
        Color coat = w.Guard ? Greatcoat : k % 2 == 0 ? Quilt : QuiltDark;
        Color legs = w.Guard ? Greatcoat * 0.8f : Felt;
        w.LegL = new Node3D { Position = new Vector3(-0.1f, 0.75f, 0) };
        w.LegR = new Node3D { Position = new Vector3(0.1f, 0.75f, 0) };
        body.AddChild(w.LegL);
        body.AddChild(w.LegR);
        foreach (var leg in new[] { w.LegL, w.LegR })
        {
            leg.AddChild(Props.Part(Props.Box(0.16f, 0.75f, 0.19f), legs, new Vector3(0, -0.37f, 0)));
            leg.AddChild(Props.Part(Props.Box(0.18f, 0.2f, 0.26f), Felt, new Vector3(0, -0.66f, -0.03f)));     // felt boots
        }
        if (w.Guard)
            body.AddChild(Props.Part(Props.Box(0.56f, 0.95f, 0.36f), coat, new Vector3(0, 0.95f, 0)));          // greatcoat to the knee
        else
        {
            body.AddChild(Props.Part(Props.Box(0.5f, 0.62f, 0.32f), coat, new Vector3(0, 1.05f, 0)));           // padded jacket
            for (int q = 0; q < 3; q++)
                body.AddChild(Props.Part(Props.Box(0.51f, 0.03f, 0.33f), QuiltDark * 0.85f, new Vector3(0, 0.85f + q * 0.17f, 0)));
            // A number patch on the back.
            body.AddChild(Props.Part(Props.Box(0.22f, 0.1f, 0.02f), new Color(0.85f, 0.83f, 0.78f), new Vector3(0, 1.2f, 0.17f)));
        }
        w.Head = new Node3D { Position = new Vector3(0, 1.5f, 0) };
        body.AddChild(w.Head);
        w.Head.AddChild(Props.Part(Props.Ball(0.14f), Skin, Vector3.Zero));
        // The ushanka: the guards' with a red star.
        w.Head.AddChild(Props.Part(Props.Box(0.34f, 0.14f, 0.34f), w.Guard ? Fur : QuiltDark, new Vector3(0, 0.12f, 0)));
        w.Head.AddChild(Props.Part(Props.Box(0.36f, 0.18f, 0.08f), w.Guard ? Fur : QuiltDark, new Vector3(0, 0.0f, 0.15f)));
        if (w.Guard) w.Head.AddChild(Props.Part(Props.Box(0.08f, 0.08f, 0.02f), Star, new Vector3(0, 0.13f, -0.18f)));
        w.ArmL = new Node3D { Position = new Vector3(-0.31f, 1.3f, 0) };
        w.ArmR = new Node3D { Position = new Vector3(0.31f, 1.3f, 0) };
        body.AddChild(w.ArmL);
        body.AddChild(w.ArmR);
        w.ArmL.AddChild(Props.Part(Props.Box(0.12f, 0.5f, 0.12f), coat, new Vector3(0, -0.25f, 0)));
        w.ArmR.AddChild(Props.Part(Props.Box(0.12f, 0.5f, 0.12f), coat, new Vector3(0, -0.25f, 0)));
        if (!w.Guard)
        {
            w.ArmL.Rotation = new Vector3(-0.5f, 0, -0.35f);     // hands clasped behind the back
            w.ArmR.Rotation = new Vector3(-0.5f, 0, 0.35f);
        }
        else if (w.Gunner)
        {
            // Both arms forward holding the submachine gun levelled at the column ahead: stock,
            // receiver, a drum magazine, the perforated barrel jacket.
            w.ArmL.Rotation = new Vector3(1.35f, 0, -0.35f);
            w.ArmR.Rotation = new Vector3(1.25f, 0, 0.1f);
            var gun = new Node3D { Position = new Vector3(0.05f, 1.12f, -0.35f), RotationDegrees = new Vector3(4f, 0, 0) };
            body.AddChild(gun);
            gun.AddChild(Props.Part(Props.Box(0.08f, 0.13f, 0.32f), Wood, new Vector3(0, 0, 0.24f)));
            gun.AddChild(Props.Part(Props.Box(0.07f, 0.1f, 0.34f), Gun, new Vector3(0, 0.02f, -0.08f)));
            gun.AddChild(Props.Part(Props.Cyl(0.11f, 0.06f, 10), Gun, new Vector3(0, -0.1f, -0.06f), new Vector3(0, 0, 90)));
            gun.AddChild(Props.Part(Props.Cyl(0.035f, 0.32f, 6), Gun, new Vector3(0, 0.03f, -0.4f), new Vector3(90, 0, 0)));
        }
        else
        {
            // The front guard: a rifle slung on his back.
            var rifle = Props.Part(Props.Box(0.05f, 1.1f, 0.06f), Wood, new Vector3(0.12f, 1.1f, 0.2f), new Vector3(0, 0, 25f));
            body.AddChild(rifle);
        }
        w.Root = root;
        w.Body = body;
    }
}
