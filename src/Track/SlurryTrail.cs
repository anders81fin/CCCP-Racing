using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

/// <summary>Short-lived wet patches, independent of the permanent ground surface.</summary>
public sealed class SlurryTrail
{
    public const float Lifetime = 15f;
    public const float Radius = 2.3f;
    public const int Capacity = 160;
    /// <summary>The grandmother's revenge: a patch lies longer and wider, so a strip along the road is still wet when the car comes round.</summary>
    public const float RevengeLifetime = 40f, RevengeRadius = 3.2f;
    public readonly record struct Patch(Vector2 Center, float Born, float Life = Lifetime, float Radius = SlurryTrail.Radius);
    public readonly List<Patch> Patches = new();
    public float Clock { get; private set; }
    public void Tick(float dt)
    {
        Clock += dt;
        Patches.RemoveAll(p => Clock - p.Born >= p.Life);
    }
    public void Deposit(Vector2 center, bool revenge = false)
    {
        if (Patches.Count == Capacity) Patches.RemoveAt(0);
        Patches.Add(revenge ? new Patch(center, Clock, RevengeLifetime, RevengeRadius) : new Patch(center, Clock));
    }
    /// <summary>The replay lays a patch the tape says was deposited <paramref name="age"/> seconds ago (none if it has dried by now).</summary>
    public void DepositAged(Vector2 center, bool revenge, float age)
    {
        float life = revenge ? RevengeLifetime : Lifetime;
        if (age >= life) return;
        if (Patches.Count == Capacity) Patches.RemoveAt(0);
        Patches.Add(new Patch(center, Clock - age, life, revenge ? RevengeRadius : Radius));
    }
    public float Wetness(Vector2 at)
    {
        float wet = 0;
        foreach (var p in Patches)
            if (at.DistanceSquaredTo(p.Center) < p.Radius * p.Radius)
                wet = Math.Max(wet, Strength(p));
        return wet;
    }
    public float Strength(Patch p) => Math.Clamp(1f - (Clock - p.Born) / p.Life, 0, 1);
    /// <summary>The grip left on a tyre with this much film on it (0..1): down to <see cref="SlickGrip"/> (far under the ice's 0.36), whatever the road under it is.</summary>
    public static float Grip(float original, float wet) => Mathf.Lerp(original, Math.Min(original, SlickGrip), wet);
    public const float SlickGrip = 0.12f;
    public void Clear() { Clock = 0; Patches.Clear(); }
}
