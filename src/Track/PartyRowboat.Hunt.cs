using System;
using CCCPRacing.Audio;
using Godot;

namespace CCCPRacing.Track;

public partial class PartyRowboat
{
    static float ShotInterval(int shot) => 11f + 3f * (0.5f + MathF.Sin(shot * 2.37f) * 0.5f);

    void BuildShotgun(Node3D guest, Color skin, Color shade)
    {
        _gun = new Node3D { Name = "DrunkenShotgun" }; guest.AddChild(_gun);
        Color wood = new(0.36f, 0.17f, 0.07f), steel = new(0.17f, 0.19f, 0.20f);
        void Box(Node3D parent, Vector3 size, Vector3 at, Color color, Vector3 rotation = default) =>
            parent.AddChild(Props.Part(Props.Box(size.X, size.Y, size.Z), color, at, rotation));
        Box(_gun, new Vector3(0.19f, 0.21f, 0.55f), new Vector3(0, 0.02f, 0.30f), wood, new Vector3(-9, 0, 0));
        Box(_gun, new Vector3(0.20f, 0.24f, 0.05f), new Vector3(0, 0.06f, 0.58f), new Color(0.055f, 0.05f, 0.04f));
        Box(_gun, new Vector3(0.24f, 0.21f, 0.26f), new Vector3(0, 0.10f, -0.04f), steel);
        Box(_gun, new Vector3(0.10f, 0.12f, 0.18f), new Vector3(0, -0.09f, 0.02f), steel);
        // The arms and hands travel with the raised gun, one at the trigger, one under the fore-end.
        Box(_gun, new Vector3(0.19f, 0.20f, 0.57f), new Vector3(0.27f, -0.13f, 0.12f), skin, new Vector3(0, 27, 0));
        Box(_gun, new Vector3(0.18f, 0.18f, 0.63f), new Vector3(-0.23f, -0.15f, -0.13f), skin, new Vector3(0, -26, 0));
        Box(_gun, new Vector3(0.19f, 0.16f, 0.19f), new Vector3(0.08f, -0.03f, -0.02f), shade);
        Box(_gun, new Vector3(0.24f, 0.14f, 0.19f), new Vector3(-0.03f, -0.06f, -0.40f), shade);
        DrunkTractor.BatchStatic(_gun, null);
        _barrels = new Node3D { Name = "BreakActionBarrels" }; _gun.AddChild(_barrels);
        Box(_barrels, new Vector3(0.22f, 0.13f, 0.42f), new Vector3(0, 0.01f, -0.42f), wood);
        foreach (float side in new[] { -1f, 1f })
        {
            _barrels.AddChild(Props.Part(Props.Cyl(0.058f, 1.27f, 8), steel, new Vector3(side * 0.066f, 0.14f, -0.77f), new Vector3(90, 0, 0)));
            _barrels.AddChild(Props.Part(Props.Cyl(0.040f, 0.018f, 8), new Color(0.015f, 0.015f, 0.012f), new Vector3(side * 0.066f, 0.14f, -1.41f), new Vector3(90, 0, 0)));
        }
        DrunkTractor.BatchStatic(_barrels, null);
        _flash = new Node3D { Name = "ShotgunMuzzleFlash", Position = new Vector3(0, 0.14f, -1.44f), Visible = false };
        _barrels.AddChild(_flash);
        var flame = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.8f, 0.25f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        foreach (float rotation in new[] { 0f, 60f, 120f })
        {
            var flash = Props.Part(Props.Box(0.65f, 0.06f, 0.50f), Colors.White, Vector3.Zero, new Vector3(0, 0, rotation));
            flash.MaterialOverride = flame; flash.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            _flash.AddChild(flash);
        }
        // Empties on the floor make the visitor's lack of coordination readable from the road.
        foreach (float side in new[] { -1f, 1f })
            _boat.AddChild(Props.Part(Props.Cyl(0.095f, 0.48f, 8, 0.045f), new Color(0.22f, 0.32f, 0.12f), new Vector3(side * 0.46f, 0.25f, 0.95f), new Vector3(75, 0, side * 18)));
    }

    void BuildHuntEffects()
    {
        _smokeMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.78f, 0.77f, 0.70f, 0),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        var smoke = Props.Part(Props.Ball(1, 8), Colors.White, Vector3.Zero);
        smoke.MaterialOverride = _smokeMat; smoke.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        _smoke = smoke; AddChild(_smoke);
        _splash = new Node3D { Name = "MissedShotSplash", Visible = false }; AddChild(_splash);
        Color water = new(0.79f, 0.87f, 0.91f);
        for (int i = 0; i < _droplets.Length; i++)
        {
            _droplets[i] = Props.Part(Props.Box(0.10f, 0.28f, 0.10f), water, Vector3.Zero);
            _droplets[i].CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            _splash.AddChild(_droplets[i]);
        }
        _splashRing = Props.Part(new TorusMesh { InnerRadius = 0.88f, OuterRadius = 1f, Rings = 16, RingSegments = 3 }, water, Vector3.Zero);
        _splash.AddChild(_splashRing);
    }

    void Fire()
    {
        Vector2 miss = MissAt(_clock, _shots);
        _impact = new Vector3(miss.X, _track.WaterLevel + 0.04f, miss.Y);
        _impactDelay = 0.12f;
        _recoil = 1f;
        _lastShot = _clock;
        _smoke.Position = _flash.GlobalPosition;
        AddChild(new ShotgunSound { Position = _flash.GlobalPosition });
        _shots++;
        _nextShot = _clock + ShotInterval(_shots);
        if (OS.HasEnvironment("DACHADBG"))
            GD.Print($"dacha: shotgun {_shots} at {_clock:F2} s, duck missed by {miss.DistanceTo(PartyPondDuck.PositionAt(_clock)):F1} m, splash {miss}");
    }

    void HuntEffects(float dt)
    {
        float since = _clock - _lastShot;
        _flash.Visible = since < 0.09f;
        _smoke.Visible = since < 1.2f;
        if (_smoke.Visible)
        {
            _smoke.Position += new Vector3(0.18f, 0.55f, 0.10f) * dt;
            _smoke.Scale = Vector3.One * (0.10f + since * 0.5f);
            _smokeMat.AlbedoColor = new Color(0.78f, 0.77f, 0.70f, (1f - since / 1.2f) * 0.6f);
        }
        if (_impactDelay >= 0)
        {
            _impactDelay -= dt;
            if (_impactDelay <= 0) { _splash.Position = _impact; _splashAge = 0; }
        }
        _splashAge += dt;
        _splash.Visible = _splashAge < 0.85f;
        if (!_splash.Visible) return;
        for (int i = 0; i < _droplets.Length; i++)
        {
            float angle = i * MathF.Tau / _droplets.Length;
            float radius = 0.16f + _splashAge * 0.75f;
            _droplets[i].Position = new Vector3(MathF.Cos(angle) * radius,
                Math.Max(0, _splashAge * (3.4f + (i % 3) * 0.3f) - _splashAge * _splashAge * 5f), MathF.Sin(angle) * radius);
            _droplets[i].Scale = Vector3.One * (1f - _splashAge / 0.85f);
        }
        _splashRing.Scale = new Vector3(0.2f + _splashAge * 1.5f, 0.12f, 0.2f + _splashAge * 1.5f);
    }
}
