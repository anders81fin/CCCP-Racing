using Godot;

namespace CCCPRacing.Net;

/// <summary>One modal across LAN phase changes. Opening and held/repeated keys never confirm leaving.</summary>
public sealed class LanLeavePrompt
{
    public enum Result { None, Stay, Leave }
    public bool Open { get; private set; }
    public bool LeaveSelected { get; private set; }
    public bool QuitApplication { get; private set; }
    public ulong ConsumedFrame {get;private set;}
    ulong _opened;

    public void Ask(ulong frame,bool quit=false)
    {
        if(Open)
        {
            if(quit && !QuitApplication) {LeaveSelected=false;_opened=frame;}
            QuitApplication |= quit;return;
        }
        Open=true;LeaveSelected=false;QuitApplication=quit;_opened=frame;
    }
    public void Cancel() {Open=false;LeaveSelected=false;QuitApplication=false;}
    public Result KeyPress(Key key,bool echo,ulong frame)
    {
        if(Open) ConsumedFrame=frame;
        if(!Open || echo || frame<=_opened) return Result.None;
        if(key==Key.Escape) {Cancel();return Result.Stay;}
        if(key is Key.Left or Key.Up or Key.A or Key.W) LeaveSelected=false;
        if(key is Key.Right or Key.Down or Key.D or Key.S) LeaveSelected=true;
        if(key is not (Key.Enter or Key.KpEnter)) return Result.None;
        if(LeaveSelected) return Result.Leave;
        Cancel();return Result.Stay;
    }
    public void Select(bool leave) => LeaveSelected=leave;
}
