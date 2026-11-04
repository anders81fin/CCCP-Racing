using System;
using System.Collections.Generic;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

/// <summary>
/// The rally's fuel from the village: every other babushka carries a red jerrycan. One who sees
/// the car coming steps to the verge and holds it out; pass her close (the car's centre within her
/// reach, not touching her) at any speed and she tosses it in (CanCaught: CanisterLitres); run
/// her down and it bursts; honk and
/// she dives into the ditch with it. A new can after a minute or so. Off (Canisters false) on the
/// title, where they walk as they always did.
/// </summary>
public partial class Babushkas
{
    /// <summary>She sees a car coming this far off (m) and steps out with the can.</summary>
    public const float OfferRange = 80f;
    /// <summary>The car's centre passes within this (m) of her: her arm reaches the window.</summary>
    public const float PassReach = 4.5f;
    /// <summary>What one can holds: about a lap flat out in the full rally world.</summary>
    public const float CanisterLitres = 20f;
    /// <summary>Seconds until a carrier has a new can.</summary>
    public static readonly (float min, float max) CanRespawn = (60f, 90f);
    const float OfferLane = 12f;         // m either side of the car's path she can be to count as ahead of it
    const float OfferTimeout = 12f;
    const float TossTime = 0.45f;

    public enum Pass { Missed, Catch }

    static readonly Color CanRed = new(0.75f, 0.1f, 0.07f), CanDark = new(0.3f, 0.05f, 0.04f);
    static readonly Color CanGold = new(0.95f, 0.74f, 0.16f), CanGoldDark = new(0.5f, 0.33f, 0.05f);

    class FlyingCan
    {
        public Node3D Node;
        public Vector3 From, Spin;
        public float T;
        public int Owner;
        public bool Golden;
        public CarSim Target;
    }

    readonly List<FlyingCan> _cans = new();
    bool _canisters;

    /// <summary>The rally: carriers have their cans. Off: none (the title, Stakhanov).</summary>
    public bool Canisters
    {
        get => _canisters;
        set
        {
            _canisters = value;
            foreach (var w in _w) ShowCan(w, value && w.HasCan);
            if (!value) ClearFlyingCans();
        }
    }

    /// <summary>(index, golden) her tossed can landed in the car: CanisterLitres (golden: OCTANE BOOSTER too).</summary>
    public event Action<int, bool> CanCaught;
    /// <summary>LAN host: the car to which this can was thrown.</summary>
    public event Action<CarSim, bool> CanCaughtForCar;
    /// <summary>(index) she has just lobbed her can at the car.</summary>
    public event Action<int> CanTossed;
    /// <summary>(index, where) the car hit her with the can in hand: it burst.</summary>
    public event Action<int, Vector3> CanBurst;

    /// <summary>What passing her does with the can: the car's centre lateral m off her (any speed catches).</summary>
    public static Pass JudgePass(float lateral) => Math.Abs(lateral) > PassReach ? Pass.Missed : Pass.Catch;

    /// <summary>She sees the car coming: within OfferRange, ahead of it and near its path, moving.</summary>
    public static bool Sees(Vector2 her, Vector2 carPos, Vector2 carVel)
    {
        float sp = carVel.Length();
        Vector2 rel = her - carPos;
        if (sp < 2f || rel.Length() > OfferRange) return false;
        Vector2 dir = carVel / sp;
        return rel.Dot(dir) > 4f && Math.Abs(dir.Cross(rel)) < OfferLane;
    }

    /// <summary>A red jerrycan in her hand's frame (her scale applies): handle up, the X pressed in the side.</summary>
    /// <summary>The can in her hand: the red one, or the golden octane booster one (Golden).</summary>
    static void ShowCan(Walker w, bool on)
    {
        w.Can.Visible = on && !w.Golden;
        w.GoldCan.Visible = on && w.Golden;
        if(w.GoldVest!=null) w.GoldVest.Visible=on&&w.Golden;
    }

    Node3D HeldCan(Walker w) => w.Golden ? w.GoldCan : w.Can;

    /// <summary>
    /// One carrier with a can (not holding it out yet) gets the golden OCTANE BOOSTER can; false when
    /// nobody could take it (the rally tries again at the next line). One golden can at a time.
    /// </summary>
    public bool MakeGolden(RandomNumberGenerator rng)
    {
        if (!_canisters || GoldenOut) return false;
        var free = _w.FindAll(w => w.Carrier && w.HasCan && w.Mode != Mode.Offer);
        if (free.Count == 0) return false;
        var g = free[rng.RandiRange(0, free.Count - 1)];
        g.Golden = true;
        ShowCan(g, true);
        if (FuelDebug) GD.Print($"canister: babushka {g.Index} has the golden can");
        return true;
    }

    /// <summary>A golden can is in a hand or in the air.</summary>
    public bool GoldenOut => _w.Exists(w => w.Golden && w.HasCan) || _cans.Exists(c => c.Golden);

    static Node3D BuildCan(bool gold = false)
    {
        var n = new Node3D { Name = "Can" };
        Color body = gold ? CanGold : CanRed, CanDark = gold ? CanGoldDark : Babushkas.CanDark;
        n.AddChild(Props.Part(Props.Box(0.3f, 0.34f, 0.13f), body, new Vector3(0, -0.14f, 0)));
        n.AddChild(Props.Part(Props.Box(0.2f, 0.05f, 0.05f), CanDark, new Vector3(0, 0.05f, 0)));        // handle
        n.AddChild(Props.Part(Props.Box(0.05f, 0.08f, 0.05f), CanDark, new Vector3(0.11f, 0.05f, 0)));    // spout
        n.AddChild(Props.Part(Props.Box(0.26f, 0.03f, 0.14f), CanDark, new Vector3(0, -0.14f, 0), new Vector3(0, 0, 45)));
        n.AddChild(Props.Part(Props.Box(0.26f, 0.03f, 0.14f), CanDark, new Vector3(0, -0.14f, 0), new Vector3(0, 0, -45)));
        return n;
    }

    void ResetCan(Walker w)
    {
        w.HasCan = w.Carrier;
        w.Golden = false;
        w.CanCd = w.OfferCd = w.SayTime = 0;
        w.PrevAlong = float.NaN;
        w.OfferCar = null;
        if (w.Can != null) ShowCan(w, _canisters && w.HasCan);
    }

    /// <summary>Her word, the offer's cooldown and, for a carrier without a can, the wait for a new one.</summary>
    void CanTimers(Walker w, float dt)
    {
        w.SayTime -= dt;
        w.OfferCd -= dt;
        if (_canisters && w.Carrier && !w.HasCan && (w.CanCd -= dt) <= 0)
        {
            w.HasCan = true;
            if (_debug || FuelDebug) GD.Print($"canister: babushka {w.Index} has a new can");
        }
        ShowCan(w, _canisters && w.HasCan);
    }

    static readonly bool FuelDebug = OS.HasEnvironment("FUELDBG");

    /// <summary>A carrier walking along sees the car coming: to the verge with the can.</summary>
    bool StartOffer(Walker w, CarSim car, Vector2 carPos)
    {
        if (!_canisters || !w.HasCan || w.Mode != Mode.Walk || w.OfferCd > 0) return false;
        if (!Sees(w.Pos, carPos, new Vector2(car.Vel.X, car.Vel.Z))) return false;
        w.Mode = Mode.Offer;
        w.OfferCar = car;
        w.OfferTime = 0;
        w.PrevAlong = float.NaN;
        if (FuelDebug) GD.Print($"canister: babushka {w.Index} holds out her can, car {w.Pos.DistanceTo(carPos):F0} m off at {car.Speed * 3.6f:F0} km/h");
        return true;
    }

    /// <summary>At the verge, the can held out towards the road; the car's pass decides what happens to it.</summary>
    void Offer(Walker w, float dt, CarSim car, Vector2 carPos)
    {
        w.OfferTime += dt;
        int n = _t.Center.Length, k = (int)w.S % n;
        Vector2 dir = _t.CenterDir(k), normal = new(-dir.Y, dir.X);
        float side = w.Lateral >= 0 ? 1f : -1f;
        Vector2 verge = PathPos(w) + normal * (side * (_t.HalfWidth[k] + 0.35f) - w.Lateral);
        Vector2 to = verge - w.Pos;
        float step = 1.6f * dt;
        if (to.Length() > 0.05f) { w.Pos += to.Length() <= step ? to : to.Normalized() * step; w.Phase += dt * 8f; }
        Place(w, dt, carPos);
        float wave = MathF.Sin(Time.GetTicksMsec() * 0.008f) * 0.12f;
        w.ArmR.Rotation = new Vector3(1.45f + wave, 0, 0.15f);
        w.ArmL.Rotation = new Vector3(0, 0, -0.6f);
        w.Body.Rotation = new Vector3(0.1f, 0, 0);

        Vector2 v = new(car.Vel.X, car.Vel.Z);
        float sp = v.Length();
        Vector2 cd = sp > 0.5f ? v / sp : new Vector2(car.Forward.X, car.Forward.Z).Normalized();
        Vector2 rel = w.Pos - carPos;
        float along = rel.Dot(cd), lateral = cd.Cross(rel);
        if (!float.IsNaN(w.PrevAlong) && w.PrevAlong > 0 && along <= 0)
        {
            var pass = JudgePass(lateral);
            if (FuelDebug) GD.Print($"canister: car passes babushka {w.Index} {Math.Abs(lateral):F2} m off at {car.Speed * 3.6f:F0} km/h: {pass}");
            if (pass == Pass.Catch) { Toss(w, car); return; }
        }
        w.PrevAlong = along;
        if (w.OfferTime > OfferTimeout || along < -10f || rel.Length() > OfferRange + 15f) EndOffer(w);
    }

    void EndOffer(Walker w)
    {
        w.Mode = Mode.Return;
        w.OfferCar = null;
        w.OfferCd = 5f;
        w.PrevAlong = float.NaN;
    }

    void LoseCan(Walker w)
    {
        w.HasCan = false;
        w.CanCd = _hornRng.RandfRange(CanRespawn.min, CanRespawn.max);
        ShowCan(w, false);
        w.Golden = false;
    }

    FlyingCan Throw(Walker w, CarSim target)
    {
        var fc = new FlyingCan { Node = BuildCan(w.Golden), From = HeldCan(w).GlobalPosition, Owner = w.Index, Golden = w.Golden,
            Target = target,
            Spin = new Vector3(_hornRng.RandfRange(-6f, 6f), _hornRng.RandfRange(-4f, 4f), _hornRng.RandfRange(-6f, 6f)) };
        fc.Node.Scale = Vector3.One * SizeScale;
        AddChild(fc.Node);
        fc.Node.GlobalPosition = fc.From;
        _cans.Add(fc);
        LoseCan(w);
        return fc;
    }

    /// <summary>Close enough: she lobs the can in through the window. "ДЕРЖИ!"</summary>
    void Toss(Walker w, CarSim car)
    {
        Throw(w, car);
        w.Say = "ДЕРЖИ!";
        w.SayTime = 2.2f;
        EndOffer(w);
        CanTossed?.Invoke(w.Index);
    }

    /// <summary>Run down with the can in her hand: it bursts (no fuel for hooligans).</summary>
    void BurstCan(Walker w)
    {
        if (!_canisters || !w.HasCan) return;
        Vector3 at = HeldCan(w).GlobalPosition;
        LoseCan(w);
        if (FuelDebug) GD.Print($"canister: babushka {w.Index} hit, her can bursts");
        TapeFx?.Fx(new TapeEvent(0f, TapeEventType.Effect, Kind: TapeFxKind.CanisterBurst, Effect: TapeEffect.Explosion, Pos: at, Life: 0.5f));
        CanBurst?.Invoke(w.Index, at);
    }

    /// <summary>Cans in the air on their way into the car (they follow it).</summary>
    void TickFlyingCans(float dt, CarSim car)
    {
        for (int i = _cans.Count - 1; i >= 0; i--)
        {
            var fc = _cans[i];
            fc.T += dt;
            var n = fc.Node;
            float u = Math.Min(1f, fc.T / TossTime);
            Vector3 to = (fc.Target ?? car).Pos + Vector3.Up * 0.5f;
            n.GlobalPosition = fc.From.Lerp(to, u) + Vector3.Up * MathF.Sin(u * MathF.PI) * 1.4f;
            n.Rotation += fc.Spin * dt;
            if (u < 1f) continue;
            n.QueueFree();
            _cans.RemoveAt(i);
            if (FuelDebug) GD.Print($"canister: babushka {fc.Owner}'s can lands in the car");
            CanCaught?.Invoke(fc.Owner, fc.Golden);
            CanCaughtForCar?.Invoke(fc.Target ?? car, fc.Golden);
        }
    }

    void ClearFlyingCans()
    {
        foreach (var fc in _cans) fc.Node.QueueFree();
        _cans.Clear();
    }

    /// <summary>
    /// Test hook (--canister-test): babushka i becomes a carrier holding her can out at the right
    /// verge of road metre at, and the car rolls towards her at speed (m/s) from distance m back,
    /// on a line passing miss m from her.
    /// </summary>
    public void AimPass(CarSim car, int i, int at, float speed, float distance, float miss, bool golden = false)
    {
        var w = _w[Math.Clamp(i, 0, _w.Count - 1)];
        int n = _t.Center.Length, k = (at % n + n) % n;
        w.Carrier = w.HasCan = true;
        w.Golden = golden;
        ShowCan(w, _canisters);
        w.S = k;
        w.Dir = 1f;
        w.Lateral = _t.HalfWidth[k] + 0.35f;
        w.Pos = PathPos(w);
        w.Angry = 0;
        w.React = -1;
        w.Mode = Mode.Offer;
        w.OfferCar = car;
        w.OfferTime = 0;
        w.PrevAlong = float.NaN;
        Place(w, 0, null);
        Vector2 dir = _t.CenterDir(k), side = new(-dir.Y, dir.X);
        Vector2 from = _t.Center[k] - dir * distance + side * (w.Lateral - miss);
        SetRolling(car, from, dir, speed);
    }

    /// <summary>The car at from, heading aim at speed (m/s), in the gear for it, wheels turning.</summary>
    void SetRolling(CarSim car, Vector2 from, Vector2 aim, float speed)
    {
        car.Reset(new Vector3(from.X, _t.MeshHeight(from.X, from.Y) + 0.55f, from.Y), TrackData.YawFor(aim));
        car.Vel = new Vector3(aim.X, 0, aim.Y) * speed;
        var p = car.P;
        for (int g = 1; g <= p.Gears.Length; g++)
        {
            car.Gear = g;
            float rpm = speed / p.WheelRadius * p.Gears[g - 1] * p.FinalDrive * 60f / MathF.Tau;
            if (rpm < p.ShiftUp * 0.85f) break;
        }
        foreach (var wh in car.Wheels) wh.SpinSpeed = speed / p.WheelRadius;
    }
}
