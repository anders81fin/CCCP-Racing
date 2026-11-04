using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

public partial class RescueTractor
{
    Node3D _ropeState, _ropeHook, _ropeTie;
    public Node3D TapeRopeRoot => _ropeState;
    public bool TapeDriven { get; private set; }
    public bool TapeRopeDriven { get; private set; }
    internal Node3D RopeMesh => _rope;

    void InitTapeRope()
    {
        // A sibling preserves every old tractor part path/hash. Scale remains a
        // derived rendering detail: record the two endpoints and active lifetime.
        _ropeState = new Node3D { Name = "RescueTowRope", Visible = false };
        GetParent().AddChild(_ropeState);
        _ropeHook = new Node3D { Name = "Hook" }; _ropeState.AddChild(_ropeHook);
        _ropeTie = new Node3D { Name = "CarTie" }; _ropeState.AddChild(_ropeTie);
        Tape.Version(_ropeState, 1); Tape.Part(_ropeState, "root");
        Tape.Part(_ropeHook, "hook"); Tape.Part(_ropeTie, "carTie");
    }

    public void SetTapeDriven(bool on)
    {
        TapeDriven = on;
        if (!on) Reset();
    }

    public void SetTapeRopeDriven(bool on)
    {
        TapeRopeDriven = on;
        if (!on) { _rope.Visible = false; _ropeState.Visible = false; }
    }

    public void RestoreTapeRope()
    {
        if (!TapeRopeDriven) return;
        _rope.Visible = _ropeState.Visible;
        if (_rope.Visible) DrawRope(_ropeHook.GlobalPosition, _ropeTie.GlobalPosition);
    }

    public override void _ExitTree()
    {
        if (IsInstanceValid(_ropeState) && !_ropeState.IsQueuedForDeletion()) _ropeState.QueueFree();
    }
}
