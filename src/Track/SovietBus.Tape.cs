using System;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

// Bus fire is a persistent age-driven effect, while passengers are sampled dynamic instances.
// The native bus body is retained for its indexed parts and reused when the spare departs.
public partial class SovietBus
{
    public ITapeFxHost TapeFx;
    public ITapeInstanceHost TapeHost;
    /// <summary>The native wreck was cleared by a reset; a recording host can drop its carried fire event.</summary>
    public event Action TapeReset;

    bool _tapeDriven;
    /// <summary>A covered bus neither simulates nor keeps native wrecks, passengers or fire audio alive.</summary>
    public bool TapeDriven
    {
        get => _tapeDriven;
        set
        {
            if (_tapeDriven == value) return;
            _tapeDriven = value;
            if (value) ResetDitch();
            else Reset();
        }
    }

    static void MarkPassenger(DrunkMen.Figure f, string coat)
    {
        f.Root.SetMeta(WorldTapeConst.KindMeta, "bus-passenger/" + coat);
        Tape.Part(f.Root, "root"); Tape.Part(f.Body, "body"); Tape.Part(f.Head, "head");
        Tape.Part(f.ArmR, "armR"); Tape.Part(f.LegL, "legL"); Tape.Part(f.LegR, "legR"); Tape.Part(f.Bottle, "bottle");
        // A visible marker records the timed HUD shout as part of the passenger's pose.
        var shout = new Node3D { Name = "TapeShout", Position = Vector3.Up * (3.7f / f.Root.Scale.Y), Visible = false };
        f.Root.AddChild(shout);
        Tape.Part(shout, "shout");
    }

    /// <summary>The same drunk figure and moving parts as a native passenger; the tape supplies all poses.</summary>
    public static Node3D BuildReplayPassenger(string coatHtml)
    {
        var f = DrunkMen.BuildFigure(Color.FromHtml(coatHtml));
        MarkPassenger(f, coatHtml);
        return f.Root;
    }

    static StandardMaterial3D CreateBurntOverlay(Node3D wreck)
    {
        var burnt = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.045f, 0.04f, 0.035f, 0.35f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        SetOverlay(wreck, burnt);
        return burnt;
    }

    /// <summary>The native wreck body, damaged bumper and recorded door state, without any live simulation.</summary>
    public static Node3D BuildReplayWreck(in TapeEvent e, out StandardMaterial3D burnt)
    {
        var wreck = BuildModelBody(out var doorL, out var doorR, out var bumper);
        wreck.Name = "Tape bus wreck";
        wreck.Transform = new Transform3D(Basis.FromEuler(e.Dir), e.Pos);
        doorL.Position = e.Velocity;
        doorR.Position = new Vector3(e.Velocity.X, e.Velocity.Y, e.Param);
        bumper.Position = BumperPos + new Vector3(0.1f, -0.08f, 0.3f);
        bumper.Rotation = new Vector3(0, 0.25f, 0.12f);
        burnt = CreateBurntOverlay(wreck);
        return wreck;
    }

    public static float FireBlaze(float age) => age < BlazeTime ? 1f : Math.Max(Smoulder, 1f - (age - BlazeTime) / FadeTime);

    /// <summary>The charring and sound intensity at the recorded fire age, also used immediately after seeking.</summary>
    public static void SetFireAge(StandardMaterial3D burnt, FireSound sound, float age)
    {
        var col = burnt.AlbedoColor;
        burnt.AlbedoColor = new Color(col.R, col.G, col.B, Math.Min(0.93f, 0.35f + age * 0.1f));
        if (sound != null) sound.Level = FireBlaze(age);
    }

    /// <summary>The native ignition burst. Replay supplies an independent random generator.</summary>
    public static void EmitIgnition(TyreEffects fx, RandomNumberGenerator rng, Transform3D xf)
    {
        Vector3 c = xf.Origin + Vector3.Up * 1.5f;
        for (int i = 0; i < 60; i++)
            fx.Burst(c + xf.Basis * new Vector3(rng.RandfRange(-1.2f, 1.2f), rng.RandfRange(-0.5f, 1f), rng.RandfRange(-3.5f, 3.5f)),
                new Vector3(rng.RandfRange(-5, 5), rng.RandfRange(2, 8), rng.RandfRange(-5, 5)),
                FireCols[i % 3], 1f, rng.RandfRange(1.4f, 2.4f), 0.4f, rng.RandfRange(0.4f, 0.8f), -1f, 2.2f, false);
        for (int i = 0; i < 12; i++)
            fx.Smoke(c + new Vector3(rng.RandfRange(-2f, 2f), rng.RandfRange(0f, 1.5f), rng.RandfRange(-2f, 2f)),
                new Vector3(rng.RandfRange(-1.5f, 1.5f), rng.RandfRange(2f, 5f), rng.RandfRange(-1.5f, 1.5f)),
                new Color(0.06f, 0.05f, 0.05f), 0.85f, 1.8f, 6f, rng.RandfRange(2.5f, 4f));
    }

    /// <summary>The native ongoing fire and smoke formulas. Particle ages are assigned by the replay effect sink.</summary>
    public static void EmitFire(TyreEffects fx, RandomNumberGenerator rng, Transform3D xf, float age, float dt, ref float fireAcc, ref float smokeAcc)
    {
        float blaze = FireBlaze(age);
        fireAcc += dt * 70f * blaze;
        while (fireAcc >= 1f)
        {
            fireAcc -= 1f;
            Vector3 p = xf * new Vector3(rng.RandfRange(-1.2f, 1.2f), rng.RandfRange(1.4f, 3.2f), rng.RandfRange(-3.6f, 3.6f));
            fx.Burst(p, new Vector3(rng.RandfRange(-0.6f, 0.6f), rng.RandfRange(2.5f, 5f), rng.RandfRange(-0.6f, 0.6f)),
                FireCols[rng.RandiRange(0, 2)], 1f, rng.RandfRange(1.3f, 2.3f) * (0.6f + 0.4f * blaze), 0.3f, rng.RandfRange(0.5f, 0.9f), -2f, 1.5f, false);
        }
        // Thick and buoyant: it climbs well above the trees, readable from across the map; it
        // starts above the flames so they stay in sight.
        smokeAcc += dt * (4f + 9f * blaze);
        while (smokeAcc >= 1f)
        {
            smokeAcc -= 1f;
            Vector3 p = xf * new Vector3(rng.RandfRange(-1f, 1f), 5.5f, rng.RandfRange(-3f, 3f));
            float g = 0.04f + 0.1f * (1f - blaze);
            fx.Burst(p, new Vector3(rng.RandfRange(-0.5f, 0.5f), rng.RandfRange(3f, 5f), rng.RandfRange(-0.5f, 0.5f)),
                new Color(g, g, g * 0.95f), 0.85f, rng.RandfRange(1.5f, 2.2f), rng.RandfRange(6f, 9f), rng.RandfRange(6f, 8f), -2.5f, 0.6f, false);
        }
    }
}
