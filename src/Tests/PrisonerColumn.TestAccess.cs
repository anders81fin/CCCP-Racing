using Godot;

namespace CCCPRacing.Track;

// Development-only access to the column's private state for PhysicsTest.PrisonerKnees.cs (src/Tests is left out of the release build).
public partial class PrisonerColumn
{
    internal int WalkerCount => _walkers.Count;
    internal Node3D WalkerKnee(int i, bool left) => left ? _walkers[i].KneeL : _walkers[i].KneeR;
    internal Node3D WalkerHip(int i, bool left) => left ? _walkers[i].LegL : _walkers[i].LegR;
    internal Node3D WalkerBody(int i) => _walkers[i].Body;
    internal float WalkerGait(int i) => _walkers[i].Gait;
    internal bool WalkerDown(int i) => _walkers[i].Down.Down;
    internal bool WalkerStraggling(int i) => _walkers[i].Straggling;
    internal bool HitWalker(int i) => _walkers[i].Down.Hit(new Vector3(0, 0, 1));
}
