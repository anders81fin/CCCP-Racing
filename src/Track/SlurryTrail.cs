using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

/// <summary>Short-lived wet patches, independent of the permanent ground surface.</summary>
public sealed class SlurryTrail
{
    public const float Lifetime = 15f;
    public const float Radius = 2.3f;
    public const int Capacity = 64;
    public readonly record struct Patch(Vector2 Center, float Born);
    public readonly List<Patch> Patches = new();
    public float Clock { get; private set; }
    public void Tick(float dt)
    {
        Clock += dt;
        Patches.RemoveAll(p => Clock - p.Born >= Lifetime);
    }
    public void Deposit(Vector2 center)
    {
        if (Patches.Count == Capacity) Patches.RemoveAt(0);
        Patches.Add(new Patch(center, Clock));
    }
    public float Wetness(Vector2 at)
    {
        float wet = 0;
        foreach (var p in Patches)
            if (at.DistanceSquaredTo(p.Center) < Radius * Radius)
                wet = Math.Max(wet, Strength(p));
        return wet;
    }
    public float Strength(Patch p) => Math.Clamp(1f - (Clock - p.Born) / Lifetime, 0, 1);
    public static float Grip(float original, float wet) => Mathf.Lerp(original, Math.Min(original, 0.40f), wet);
    public void Clear() { Clock = 0; Patches.Clear(); }
}
