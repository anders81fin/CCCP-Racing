using System.Collections;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// Phase 2 spike 2: runs the viewer-freeze scenario script of the live RaceScene one step per rendered frame. It lives beside the scene (a child of
/// the scene's parent, ProcessMode Always), so it keeps running while the live scene is Disabled; that is the point of the test.
/// </summary>
public partial class ViewerFreezeDriver : Node
{
    IEnumerator _script;
    public System.Action OnDone;

    public void Start(IEnumerable script) { _script = script.GetEnumerator(); }

    public override void _Process(double delta)
    {
        if (_script == null) return;
        bool more;
        try { more = _script.MoveNext(); }
        catch (System.Exception e) { GD.Print("VIEWERFREEZE: script FAILED: " + e); GD.PrintErr(e); _script = null; GetTree().Quit(2); return; }
        if (!more) { _script = null; OnDone?.Invoke(); }
    }
}
