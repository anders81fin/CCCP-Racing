using System;
using Godot;

namespace CCCPRacing.Track;

// Development-only access to the horses' private state for PhysicsTest.Horses.cs (src/Tests is left out of the release build).
public partial class ZoneLife
{
    internal int HorseCount => _horses.Count;
    internal Vector2 HorsePos(int i) => _horses[i].Pos;
    internal Vector2 HorseFace(int i) => _horses[i].Face;
    internal float HorseSpeed(int i) => _horses[i].Speed;
    internal Vector2 HorseHome(int i) => _horses[i].Home;
    internal Node3D HorseRoot(int i) => _horses[i].Node;
    internal Node3D HorseHead(int i) => _horses[i].Head;
    internal Node3D HorseTailNode(int i) => _horses[i].Tail;
    internal Node3D HorseHip(int i, int leg) => _horses[i].Hip[leg];
    internal Node3D HorseShin(int i, int leg) => _horses[i].Shin[leg];
    /// <summary>The sudden stop a blocked horse makes (Speed to 0 at once): the legs must follow it down smoothly.</summary>
    internal void HorseHardStop(int i) { var h = _horses[i]; h.Speed = 0; h.Run = 0; h.Graze = h.Pos; h.Change = 1000; }
    internal float HorseGait(int i) => _horses[i].Gait;
    /// <summary>Put a horse somewhere standing (as a spawn would).</summary>
    internal void HorsePlace(int i, Vector2 pos, Vector2 face)
    {
        var h = _horses[i];
        h.Pos = pos; h.Face = face.Normalized(); h.Speed = 0; h.Gait = 0; h.Move = 0; h.Blend = 0; h.Run = 0; h.Graze = pos; h.Change = 5;
        Pose(h, 0);
    }
}
