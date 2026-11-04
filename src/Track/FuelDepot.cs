using System;
using CCCPRacing.Audio;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// The kolkhoz fuel depot: a rusty tank up on four legs with a hose hanging down, on the apron
/// beside the runway straight a few dozen metres before the start line (the driver's left, clear
/// of the Balalaikas' taxiway). Stop in the zone under the hose and fuel pours in, gurgling; drive
/// off and it stops. An empty tank is towed here too. Only the rally uses it (RaceScene.Fuel); the
/// road, the lap gates and the track signature are untouched.
/// </summary>
public partial class FuelDepot : Node3D
{
    public const string IconPath = "res://assets/ui/fuel-pump.svg";
    /// <summary>Radius (m) of the stopping zone under the hose.</summary>
    public const float ZoneRadius = 4.5f;
    /// <summary>Litres per second into the car's tank.</summary>
    public const float FillRate = 6f;
    /// <summary>Slower than this (m/s, ~3 km/h) counts as stopped.</summary>
    public const float StopSpeed = 3f / 3.6f;
    const float BeforeLine = 28f;       // road metres before the start line
    const float Off = 10.5f;            // zone centre from the centre line, towards the driver's left
    const float TankRadius = 1.4f;

    static readonly Color Rust = new(0.42f, 0.3f, 0.2f);
    static readonly Color TankGreen = new(0.3f, 0.36f, 0.26f);
    static readonly Color Steel = new(0.35f, 0.35f, 0.36f);
    static readonly Color Rubber = new(0.1f, 0.1f, 0.1f);
    static readonly Color Red = new(0.72f, 0.12f, 0.08f);

    TrackData _t;
    MeshInstance3D _drip;
    GurgleSound _gurgle;
    float _dripT;
    (bool Pouring, float Level, float Envelope, float Clock, bool Drip) _beforeTape;
    public bool TapeDriven { get; private set; }
    internal GurgleSound Sound => _gurgle;
    internal Node3D StreamNode => _drip;

    /// <summary>Centre of the stopping zone.</summary>
    public Vector2 Zone { get; private set; }
    /// <summary>Where a towed car is put down (in the zone, nose to the road) and its heading.</summary>
    public Vector2 ParkPos { get; private set; }
    public float ParkYaw { get; private set; }
    /// <summary>The tank's collision circle in the track's obstacle list.</summary>
    public int Obstacle { get; private set; }
    /// <summary>Fuel is pouring (the gurgle, the drip under the nozzle).</summary>
    public bool Pouring
    {
        get => _gurgle.Pouring;
        set { if (!TapeDriven) _gurgle.Pouring = value; }
    }
    /// <summary>The car's tank level while pouring, 0..1 (the glugs rise with it).</summary>
    public float Level { get => _gurgle.Level; set { if (!TapeDriven) _gurgle.Level = value; } }

    /// <summary>A car at carPos with this speed (m/s) gets fuel from a depot zone at zone.</summary>
    public static bool Fills(Vector2 carPos, Vector2 zone, float speed) => carPos.DistanceTo(zone) <= ZoneRadius && speed < StopSpeed;

    public bool FillsCar(Vector2 carPos, float speed) => Visible && Fills(carPos, Zone, speed);

    /// <summary>The road metre the depot stands by (a tow puts the void lap there).</summary>
    public int Metre { get; private set; }
    /// <summary>It stands just before the line (the default): a tow's void lap ends at the line just ahead.</summary>
    public bool AtLine { get; private set; }

    /// <summary>Shared fuel-depot layout for its model, terrain pad and buried power service.</summary>
    public static (Vector2 Zone, Vector2 Tank, Vector2 Dir) Layout(TrackData t, DepotDef at = null)
    {
        int n = t.Center.Length, i = at != null ? (at.At % n + n) % n : (n - (int)BeforeLine) % n;
        Vector2 c = t.Center[i], dir = t.CenterDir(i), left = new(dir.Y, -dir.X);
        if (at?.Side == "right") left = -left;
        var zone = c + left * (at?.Off ?? Off);
        return (zone, zone + left * 4.6f - dir * 1.2f, dir);
    }

    /// <param name="at">its place on the track (TrackDef.Depot); null: BeforeLine metres before the line, on the left</param>
    public void Init(TrackData t, DepotDef at = null)
    {
        Tape.Part(this, "root");
        Tape.Version(this, 1);
        _t = t;
        int n = t.Center.Length, i = at != null ? (at.At % n + n) % n : (n - (int)BeforeLine) % n;
        Metre = i;
        AtLine = at == null;
        var layout = Layout(t, at);
        Vector2 dir = layout.Dir, left = (layout.Zone - t.Center[i]).Normalized();
        Zone = layout.Zone;
        ParkPos = Zone - dir * 1.5f;
        ParkYaw = TrackData.YawFor((dir * 2f - left).Normalized());   // nose out towards the road
        Vector2 tank = layout.Tank;
        Obstacle = t.Obstacles.Count;
        t.Obstacles.Add(new Obstacle(tank, TankRadius));

        // The tank on its legs, lengthwise along the road; a ladder, a red band and a star.
        float gy = t.MeshHeight(tank.X, tank.Y);
        var root = new Node3D { Name = "DepotTank", Position = new Vector3(tank.X, gy, tank.Y), Rotation = new Vector3(0, TrackData.YawFor(dir), 0) };
        AddChild(root);
        root.AddChild(new ServiceMarker(IconPath) { Name = "FuelMarker", Position = new Vector3(0, 8.4f, 0) });
        foreach (float sx in new[] { -0.9f, 0.9f })
            foreach (float sz in new[] { -1.5f, 1.5f })
            {
                root.AddChild(Props.Part(Props.Box(0.14f, 2.6f, 0.14f), Rust, new Vector3(sx, 1.3f, sz)));
                root.AddChild(Props.Part(Props.Box(0.5f, 0.1f, 0.5f), Steel, new Vector3(sx, 0.05f, sz)));
            }
        root.AddChild(Props.Part(Props.Box(1.9f, 0.1f, 0.1f), Rust, new Vector3(0, 1.2f, -1.5f), new Vector3(0, 0, 0)));   // cross braces
        root.AddChild(Props.Part(Props.Box(1.9f, 0.1f, 0.1f), Rust, new Vector3(0, 1.2f, 1.5f)));
        root.AddChild(Props.Part(Props.Box(1.6f, 0.2f, 3.6f), Rust, new Vector3(0, 2.62f, 0)));          // cradle
        root.AddChild(Props.Part(Props.Cyl(1.05f, 4.4f, 10), TankGreen, new Vector3(0, 3.55f, 0), new Vector3(90, 0, 0)));
        root.AddChild(Props.Part(Props.Cyl(1.07f, 0.35f, 10), Red, new Vector3(0, 3.55f, 0), new Vector3(90, 0, 0)));
        root.AddChild(Props.Part(Props.Cyl(0.95f, 0.08f, 10), Steel, new Vector3(0, 3.55f, -2.22f), new Vector3(90, 0, 0)));   // end caps
        root.AddChild(Props.Part(Props.Cyl(0.95f, 0.08f, 10), Steel, new Vector3(0, 3.55f, 2.22f), new Vector3(90, 0, 0)));
        root.AddChild(Props.Part(Props.Cyl(0.22f, 0.25f, 8), Steel, new Vector3(0, 4.66f, 1.2f)));          // filler cap
        // A white star on both sides, facing the road and the fields.
        foreach (float sx in new[] { -1.06f, 1.06f })
        {
            root.AddChild(Props.Part(Props.Box(0.02f, 0.5f, 0.12f), new Color(0.9f, 0.88f, 0.8f), new Vector3(sx, 3.55f, -1.2f)));
            root.AddChild(Props.Part(Props.Box(0.02f, 0.12f, 0.5f), new Color(0.9f, 0.88f, 0.8f), new Vector3(sx, 3.6f, -1.2f)));
        }
        // Ladder up the road-side end.
        for (int k = 0; k < 2; k++)
            root.AddChild(Props.Part(Props.Box(0.05f, 3.4f, 0.05f), Steel, new Vector3(-0.25f + k * 0.5f, 1.7f, -2.35f)));
        for (int k = 0; k < 7; k++)
            root.AddChild(Props.Part(Props.Box(0.5f, 0.04f, 0.04f), Steel, new Vector3(0, 0.4f + k * 0.45f, -2.35f)));
        if (t.Id == "siberia") SiberiaModelDetails.FuelTank(root);

        // The outlet, a valve wheel and the hose down to a nozzle hung on a post by the zone.
        Vector2 post = Zone + left * 3.4f;
        Vector3 outlet = root.Transform * new Vector3(0.9f, 2.7f, 0.4f);
        float py = t.MeshHeight(post.X, post.Y);
        var postTop = new Vector3(post.X, py + 1.5f, post.Y);
        AddChild(Props.Part(Props.Box(0.14f, 1.6f, 0.14f), Rust, new Vector3(post.X, py + 0.8f, post.Y)));
        AddChild(Props.Part(Props.Cyl(0.22f, 0.05f, 8), Red, outlet + Vector3.Down * 0.15f));
        // The hose sags between the outlet and the post: a few straight pieces.
        Vector3 prev = outlet;
        const int Pieces = 5;
        for (int k = 1; k <= Pieces; k++)
        {
            float u = k / (float)Pieces;
            Vector3 p = outlet.Lerp(postTop, u) + Vector3.Down * MathF.Sin(u * MathF.PI) * 0.9f;
            AddChild(Segment(prev, p, 0.07f, Rubber));
            prev = p;
        }
        AddChild(Segment(postTop, postTop + (new Vector3(Zone.X, py, Zone.Y) - postTop).Normalized() * 0.5f + Vector3.Down * 0.3f, 0.06f, Steel));   // nozzle
        // A drip under the nozzle while it pours; a dark stain on the ground.
        _drip = Props.Part(Props.Box(0.05f, 0.25f, 0.05f), new Color(0.75f, 0.62f, 0.3f), postTop + Vector3.Down * 0.8f);
        _drip.Visible = false;
        AddChild(_drip);
        Tape.Part(_drip, "stream");
        var stain = Props.Part(Props.Cyl(0.75f, 0.02f, 10), new Color(0.3f, 0.26f, 0.2f), new Vector3(post.X, py + 0.03f, post.Y));
        AddChild(stain);

        _gurgle = new GurgleSound();
        AddChild(_gurgle);
        Tape.Sound(this, TapeSoundChannel.FuelPouring, () => _gurgle.Pouring ? 1f : 0f, v => _gurgle.Pouring = v > .5f);
        Tape.Sound(this, TapeSoundChannel.FuelLevel, () => _gurgle.Level, v => _gurgle.Level = v);
        Tape.Sound(this, TapeSoundChannel.FuelEnvelope, () => _gurgle.Envelope, v => _gurgle.Envelope = v);
    }

    /// <summary>The drip under the nozzle blinks while fuel pours.</summary>
    public override void _Process(double delta)
    {
        if (TapeDriven) return;
        _dripT += (float)delta;
        _drip.Visible = Pouring && (int)(_dripT * 6) % 2 == 0;
    }

    /// <summary>Entry and every seek/loop, before recorded sound state is restored.</summary>
    public void SetTapeDriven(bool on)
    {
        if (!on && !TapeDriven) return;
        if (on && !TapeDriven)
            _beforeTape = (Pouring, Level, _gurgle.Envelope, _dripT, _drip.Visible);
        _gurgle.ClearReplayAudio();
        TapeDriven = on;
        if (!on)
        {
            _gurgle.Pouring = _beforeTape.Pouring;
            _gurgle.Level = _beforeTape.Level;
            _gurgle.Envelope = _beforeTape.Envelope;
            _dripT = _beforeTape.Clock;
            _drip.Visible = _beforeTape.Drip;
        }
    }

    static MeshInstance3D Segment(Vector3 a, Vector3 b, float r, Color c)
    {
        Vector3 d = b - a;
        float len = Math.Max(0.01f, d.Length());
        var mi = Props.Part(Props.Box(r, r, len), c, (a + b) * 0.5f);
        mi.Basis = Basis.LookingAt(d / len, Math.Abs(d.Y / len) > 0.99f ? Vector3.Right : Vector3.Up);
        return mi;
    }
}
