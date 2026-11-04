using System;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

// The world tape's side of the guided missile (docs/replay-tape.md, section 16): a dynamic instance of kind "missile" (its pose is sampled; the flame is
// the state channel), whose burst is an effect event made by ExplodeFx. The smoke trail of the burning motor is made by the replay from the pose and the
// state (MissileReplayFx), not recorded puff by puff. The missile only flies in a revenge (the pilot's grandmother) and in --missile-test.
public partial class Missile
{
    /// <summary>The world tape's view of the effects made here (the burst). Set by the jets' actor when it fires the missile.</summary>
    public ITapeFxHost TapeFx;
    /// <summary>The burst's longest particle or sound (s).</summary>
    public const float BurstLife = 4f;

    /// <summary>The tape marks of a missile: its kind, its pose and the state (1: the motor burns, the flame is lit).</summary>
    internal static void MarkTape(Node3D missile, Func<int> state, Action<int> setState = null)
    {
        missile.SetMeta(WorldTapeConst.KindMeta, "missile");
        Tape.Part(missile, "root");
        Tape.Sound(missile, TapeSoundChannel.AircraftState, () => state() / 3f, v => setState?.Invoke((int)MathF.Round(v * 3f)));
    }

    /// <summary>The missile of a tape ("missile"): the play body, flame and sound, marked alike, nothing simulated; its flame and smoke trail follow the recorded state and pose.</summary>
    public static Node3D BuildReplay(TyreEffects fx)
    {
        var m = new Node3D { Name = "Missile" };
        Dress(m, out var flame, out var sound);
        var rfx = new MissileReplayFx { Name = "TapeState", Model = m, Flame = flame, Sound = sound, Fx = fx };
        m.AddChild(rfx);
        MarkTape(m, () => rfx.State, k => rfx.State = k);
        return m;
    }
}

/// <summary>What the replay of a missile makes every physics step: the flame (lit while the motor burns) and the smoke and sparks along the path it moved, with the live code's own formulas (Missile.Tick).</summary>
public partial class MissileReplayFx : Node
{
    public int State;
    public Node3D Model;
    public MeshInstance3D Flame;
    public MissileSound Sound;
    public TyreEffects Fx;
    readonly RandomNumberGenerator _rng = new() { Seed = 1959 };
    Vector3 _prev;
    bool _has;

    public override void _PhysicsProcess(double delta)
    {
        Flame.Visible = State == 1;
        if (Flame.Visible) Flame.Scale = new Vector3(1f, 0.8f + 0.4f * _rng.Randf(), 1f);
        if (!Model.IsInsideTree()) return;
        Vector3 pos = Model.GlobalPosition;
        var cam = GetViewport()?.GetCamera3D();
        if (cam != null) Sound.Gain = Math.Clamp(40f / (cam.GlobalPosition.DistanceTo(pos) + 10f), 0.05f, 1f);
        if (Fx != null && State == 1 && _has && _prev.DistanceSquaredTo(pos) > 1e-6f)
        {
            Vector3 move = pos - _prev, f = -Model.GlobalTransform.Basis.Z;
            Vector3 tail = pos - f * 1.9f;
            int puffs = Math.Clamp((int)(move.Length() / 1.2f), 1, 40);
            Fx.BeginReplay(0);
            try
            {
                for (int k = 0; k < puffs; k++)
                {
                    Vector3 p = tail - move * (k / (float)puffs);
                    Fx.Smoke(p, new Vector3(_rng.RandfRange(-0.4f, 0.4f), _rng.RandfRange(0.1f, 0.6f), _rng.RandfRange(-0.4f, 0.4f)),
                        new Color(0.93f, 0.93f, 0.9f), 0.75f, 0.45f, 2.6f, _rng.RandfRange(2.5f, 3.5f));
                }
                Fx.Burst(tail - f * 0.3f, -f * 8f, _rng.Randf() < 0.5f ? new Color(1f, 0.95f, 0.6f) : new Color(1f, 0.6f, 0.15f),
                    1f, 0.55f, 0.15f, 0.12f, 0f, 2f, false);
            }
            finally { Fx.EndReplay(); }
        }
        _prev = pos; _has = true;
    }
}
