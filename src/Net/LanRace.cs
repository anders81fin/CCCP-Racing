using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Net;

/// <summary>Standings of a LAN race and the comrades' colours.</summary>
public static class LanRace
{
    /// <summary>Authoritative LAN fuel: normal consumption, depot filling and timed emergency supply.</summary>
    public static void TickFuel(Car.FuelTank tank, Car.CarSim car, bool pad, bool atDepot, float dt, ref float dryWait)
    {
        if (!tank.Enabled) return;
        tank.Burn(dt, car.Throttle, car.Rpm, pad ? 1f : Car.FuelTank.KeysRate);
        if (atDepot) tank.Fill(Track.FuelDepot.FillRate * dt);
        dryWait = tank.Empty && car.Speed < 1 ? dryWait + dt : 0;
        if (dryWait >= 10) { tank.Refill(); dryWait = 0; }
    }

    /// <summary>Car paint per slot: 1 the factory olive (null: the model's own), 2 red, 3 blue, 4 white, 5 purple, 6 cyan.</summary>
    public static readonly Color?[] Paint = { null, new Color(0.7f, 0.13f, 0.1f), new Color(0.12f, 0.28f, 0.72f), new Color(0.88f, 0.88f, 0.84f), new Color(0.48f, 0.15f, 0.65f), new Color(0.05f, 0.65f, 0.62f) };
    /// <summary>Name tag, arrow and list colour per slot (readable on the road and on the dark panels).</summary>
    public static readonly Color[] Tag = { new(0.85f, 0.83f, 0.25f), new(1f, 0.32f, 0.25f), new(0.45f, 0.62f, 1f), new(0.95f, 0.95f, 0.92f), new(0.83f, 0.50f, 1f), new(0.25f, 0.94f, 0.86f) };

    public static Color TagFor(int slot) => Tag[Math.Clamp(slot, 0, Tag.Length - 1)];

    /// <summary>The pole row stands on the start position, this many m before the line; the next row RowGap m back.</summary>
    public const int PoleLine = 14, RowGap = 6;
    /// <summary>The two columns, this far either side of the centre line (the road is at least 3.5 m wide each way here).</summary>
    public const float Column = 1.6f;

    /// <summary>Centre point of grid slot g (0-5, 3x2: 0 and 1 the pole row, even slots on the left).</summary>
    public static int GridCenter(Track.TrackData t, int g)
    {
        int n = t.Center.Length;
        return ((n - PoleLine - g / 2 * RowGap) % n + n) % n;
    }

    public static void GridPose(Track.TrackData t, int g, out Vector3 pos, out float yaw)
    {
        int i = GridCenter(t, g);
        Vector2 dir = t.CenterDir(i), right = new(-dir.Y, dir.X);
        Vector2 p = t.Center[i] + right * (g % 2 == 0 ? -Column : Column);
        pos = new Vector3(p.X, t.MeshHeight(p.X, p.Y) + 0.7f, p.Y);
        yaw = Track.TrackData.YawFor(dir);
    }

    /// <summary>A car still on grid slot g (before GO): behind the line, the pole row ahead, left before right.</summary>
    public static float GridScore(Track.TrackData t, int g, int gateStep) =>
        Score(0, 1, GridCenter(t, g), t.Center.Length, gateStep) - 0.001f * g;

    public struct Standing
    {
        public long Id;
        public bool Finished, Dnf;
        public float FinishTime;
        public int LapsDone;
        /// <summary>Progress (Score): laps and gates done, higher is further.</summary>
        public float Score;
    }

    /// <summary>
    /// How far round the race a car is, in gates: laps x gates + the gate it is heading for + the
    /// fraction of the way from the previous gate (negative before the line at the start). Monotonic
    /// along the road, so it ranks cars on different laps and between the same gates.
    /// </summary>
    public static float Score(int lapsDone, int nextGate, int progress, int centerCount, int gateStep)
    {
        int gates = centerCount / gateStep;
        int n = centerCount;
        int prev = (nextGate - 1) * gateStep;
        int target = nextGate < gates ? nextGate * gateStep : n;
        int seg = Math.Max(1, target - prev);
        int s = ((progress - prev) % n + n + n / 2) % n - n / 2;    // signed, -n/2..n/2
        float frac = Math.Clamp(s / (float)seg, -0.99f, 0.999f);
        return lapsDone * gates + (nextGate - 1) + frac;
    }

    /// <summary>Finished comrades by time, then the rest by progress, DNF last.</summary>
    public static List<Standing> Rank(IEnumerable<Standing> all)
    {
        var l = new List<Standing>(all);
        l.Sort((a, b) =>
        {
            int ga = a.Dnf ? 2 : a.Finished ? 0 : 1, gb = b.Dnf ? 2 : b.Finished ? 0 : 1;
            if (ga != gb) return ga.CompareTo(gb);
            if (ga == 0) return a.FinishTime.CompareTo(b.FinishTime);
            if (ga == 1 && a.Score != b.Score) return b.Score.CompareTo(a.Score);
            return a.Id.CompareTo(b.Id);
        });
        return l;
    }
}
