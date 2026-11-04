using System;
using System.Collections.Generic;
using CCCPRacing.Car;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// Yellow traffic cones marking the road's edges where a winter road runs over the ice (a frozen
/// river: the road and the river are the same white and the snow banks stop there). They carry the
/// banks' line on across the ice, every Spacing metres, closer (Close) for the first metres where a
/// bank ends. Not solid: the car knocks one flying (no damage, no slowing), it tumbles and lies on
/// its side; all stand again on a restart. A white reflective collar glows faintly after dark.
/// </summary>
public partial class TrafficCones : Node3D
{
    const float Spacing = 12f, Close = 6f, CloseFor = 18f, Reach = 6f;
    // A size up from life (a road cone is ~0.5 m), or the chase camera sees only dots.
    const float H = 1.1f, R = 0.32f, HitDist = 1.6f, Gravity = 9.8f;
    /// <summary>The collar's glow at full night: brighter than the snow poles' bands, these show the way.</summary>
    const float GlowAtNight = 0.8f;
    static readonly Color Yellow = new(1f, 0.8f, 0.05f), Base = new(0.12f, 0.12f, 0.12f);

    class Cone
    {
        public Node3D Node;
        public Vector3 Home, Pos, Vel, Spin;
        public float HomeYaw, Air;
        public Basis Rot;
        public bool Down, Flying;
    }

    readonly List<Cone> _cones = new();
    TrackData _t;
    StandardMaterial3D _collar;

    /// <summary>How many were knocked over this run (tests, commentary).</summary>
    public int Knocked { get; private set; }

    /// <summary>Cones along both edges of the ice stretches; null when the road never crosses ice.</summary>
    public static TrafficCones Build(TrackData t)
    {
        int n = t.Center.Length;
        var ice = new bool[n];
        bool any = false;
        for (int i = 0; i < n; i++)
            if (t.CenterSurf[i] == SurfaceType.Ice)
                for (int k = -(int)Reach; k <= Reach; k++) { ice[((i + k) % n + n) % n] = true; any = true; }
        if (!any) return null;
        var cones = new TrafficCones { Name = "TrafficCones", _t = t };
        cones._collar = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.95f, 0.95f, 0.93f), Roughness = 0.15f, MetallicSpecular = 1f,
            EmissionEnabled = true, Emission = new Color(1f, 1f, 0.92f), EmissionEnergyMultiplier = 0f,
        };
        NightGlow.Glows.Add((cones._collar, GlowAtNight));
        // Each ice stretch from where it starts: metres into it, to the edges of it.
        for (int i = 0; i < n; i++)
        {
            if (!ice[i] || ice[(i - 1 + n) % n]) continue;
            int len = 0;
            while (len < n && ice[(i + len) % n]) len++;
            float s = 0;
            while (s <= len)
            {
                int k = (i + (int)s) % n;
                foreach (bool left in new[] { true, false }) cones.Add(k, left);
                bool nearEnd = s < CloseFor || s > len - CloseFor;
                s += nearEnd ? Close : Spacing;
            }
        }
        if (OS.HasEnvironment("CONEDBG")) GD.Print($"traffic cones: {cones._cones.Count}");
        return cones;
    }

    void Add(int i, bool left)
    {
        Vector2 dir = _t.CenterDir(i);
        Vector2 side = left ? new Vector2(dir.Y, -dir.X) : new Vector2(-dir.Y, dir.X);
        // On the banks' line: where a bank's inner foot would be.
        float edge = _t.Banks != null ? _t.Banks.Inner(i, left) : _t.HalfWidth[i] + 0.5f;
        Vector2 p = _t.Center[i] + side * edge;
        var home = new Vector3(p.X, _t.MeshHeight(p.X, p.Y), p.Y);
        var node = new Node3D();
        node.AddChild(Props.Part(Props.Box(R * 2.2f, 0.06f, R * 2.2f), Base, new Vector3(0, 0.03f, 0)));
        node.AddChild(Props.Part(Props.Cyl(R, H, 10, 0.035f), Yellow, new Vector3(0, H / 2f + 0.04f, 0)));
        node.AddChild(new MeshInstance3D
        {
            Mesh = Props.Cyl(R * 0.62f, H * 0.16f, 10, R * 0.48f),
            MaterialOverride = _collar,
            Position = new Vector3(0, H * 0.58f, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        AddChild(node);
        float yaw = TrackData.YawFor(dir);
        _cones.Add(new Cone { Node = node, Home = home, HomeYaw = yaw });
        Place(_cones[^1], home, new Basis(Vector3.Up, yaw));
    }

    static void Place(Cone c, Vector3 pos, Basis rot)
    {
        c.Pos = pos;
        c.Rot = rot;
        c.Node.Transform = new Transform3D(rot, pos);
    }

    public void Reset()
    {
        foreach (var c in _cones)
        {
            c.Down = c.Flying = false;
            c.Vel = c.Spin = Vector3.Zero;
            Place(c, c.Home, new Basis(Vector3.Up, c.HomeYaw));
        }
        Knocked = 0;
    }

    /// <summary>Per physics tick: the car knocks standing cones flying; flying ones fall, bounce and come to rest.</summary>
    public void Tick(float dt, CarSim car)
    {
        if (dt <= 0) return;
        var cp = car != null ? new Vector2(car.Pos.X, car.Pos.Z) : Vector2.Zero;
        foreach (var c in _cones)
        {
            if (!c.Down && car != null && car.Speed > 1.5f && new Vector2(c.Pos.X, c.Pos.Z).DistanceTo(cp) < HitDist)
            {
                // Off it goes, faster than the car, up and away from its path, tumbling.
                var away = new Vector3(c.Pos.X - car.Pos.X, 0, c.Pos.Z - car.Pos.Z);
                away = away.LengthSquared() > 1e-4f ? away.Normalized() : Vector3.Right;
                float sp = car.Speed;
                c.Vel = new Vector3(car.Vel.X, 0, car.Vel.Z) * 1.15f + away * (2f + sp * 0.25f) + Vector3.Up * (2.5f + sp * 0.12f);
                float h = Noise.Hash01((int)(c.Home.X * 10), (int)(c.Home.Z * 10), 77);
                c.Spin = new Vector3(6f + 6f * h, 3f * (h - 0.5f), 5f * (0.5f - h)) * Math.Min(1.5f, 0.4f + sp / 20f);
                c.Down = c.Flying = true;
                c.Air = 0;
                Knocked++;
                if (OS.HasEnvironment("CONEDBG")) GD.Print($"cone knocked at ({c.Pos.X:F0},{c.Pos.Z:F0}), car {sp * 3.6f:F0} km/h");
            }
            if (!c.Flying) continue;
            c.Air += dt;
            c.Vel += Vector3.Down * Gravity * dt;
            var pos = c.Pos + c.Vel * dt;
            float ground = _t.MeshHeight(pos.X, pos.Z);
            var rot = c.Rot;
            float w = c.Spin.Length();
            if (w > 1e-3f) rot = new Basis(c.Spin / w, w * dt) * rot;
            if (pos.Y < ground)
            {
                // Bounce low off the ice, sliding on and slowing; at rest it lies on its side.
                pos.Y = ground;
                c.Vel = new Vector3(c.Vel.X * 0.6f, Math.Abs(c.Vel.Y) * 0.3f, c.Vel.Z * 0.6f);
                c.Spin *= 0.5f;
                if (c.Vel.Length() < 0.8f || c.Air > 4f)     // (never sliding on for ever)
                {
                    c.Flying = false;
                    Vector3 fwd = rot.Y; fwd.Y = 0;      // the cone's axis, laid flat
                    float yaw = fwd.LengthSquared() > 1e-4f ? MathF.Atan2(fwd.X, fwd.Z) : c.HomeYaw;
                    rot = new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, MathF.PI / 2f);
                    pos.Y = ground + R * 0.7f;
                    if (OS.HasEnvironment("CONEDBG")) GD.Print($"cone at rest {new Vector2(pos.X - c.Home.X, pos.Z - c.Home.Z).Length():F1} m from its place");
                }
            }
            if (c.Air > 4f && c.Flying && OS.HasEnvironment("CONEDBG")) GD.Print($"cone still flying after 4 s at ({pos.X:F0},{pos.Y:F1},{pos.Z:F0}), ground {ground:F1}, vel {c.Vel}");
            Place(c, pos, rot.Orthonormalized());
        }
    }
}
