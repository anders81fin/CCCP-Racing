using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;

namespace CCCPRacing.Race;

/// <summary>A sound property registered on a node: the recorder calls <see cref="Get"/>, the player calls <see cref="Set"/>.</summary>
public sealed record TapeSoundReg(TapeSoundChannel Channel, Func<float> Get, Action<float> Set);

/// <summary>
/// What an actor's builder calls to take part in the world tape (docs/replay-tape.md, section 4): name the parts, register
/// the spinners and the sound properties, set the structure version. Nothing here touches the simulation.
/// </summary>
public static class Tape
{
    static readonly ConditionalWeakTable<Node3D, ITapeSpinner> Spinners = new();
    static readonly ConditionalWeakTable<Node3D, List<TapeSoundReg>> Sounds = new();

    /// <summary>The node is a part of its actor, recorded under this name ("root" for the actor root, "2/wheel_fl" for instance 2's wheel).</summary>
    public static void Part(Node3D node, string name) => node.SetMeta(WorldTapeConst.PartMeta, name);

    /// <summary>The part turns continuously; the spinner publishes its real, unwrapped angle. Register after <see cref="Part"/> (or before: order does not matter).</summary>
    public static void Spinner(Node3D node, ITapeSpinner spinner) => Spinners.AddOrUpdate(node, spinner);

    /// <summary>A sound property of the sound node owned by this part (or the actor root).</summary>
    public static void Sound(Node3D node, TapeSoundChannel channel, Func<float> get, Action<float> set)
    {
        var list = Sounds.GetOrCreateValue(node);
        list.RemoveAll(r => r.Channel == channel);
        list.Add(new TapeSoundReg(channel, get, set));
    }

    /// <summary>The actor's own structure version; bump it when its builder changes in a way the part list cannot show.</summary>
    public static void Version(Node3D node, int version) => node.SetMeta(WorldTapeConst.VersionMeta, version);

    /// <summary>A part that turns about <paramref name="axis"/> (its local frame) by the unwrapped angle <paramref name="angle"/> returns (radians); registered once, at build.</summary>
    public static void Spin(Node3D node, Vector3 axis, Func<float> angle) => Spinner(node, new FuncSpinner(axis, angle));

    sealed class FuncSpinner : ITapeSpinner
    {
        readonly Func<float> _angle;
        public FuncSpinner(Vector3 axis, Func<float> angle) { Axis = axis; _angle = angle; }
        public Vector3 Axis { get; }
        public float Angle => _angle();
    }

    public static ITapeSpinner SpinnerOf(Node3D node) => Spinners.TryGetValue(node, out var s) ? s : null;
    public static IReadOnlyList<TapeSoundReg> SoundsOf(Node3D node) => Sounds.TryGetValue(node, out var l) ? l : null;
}
