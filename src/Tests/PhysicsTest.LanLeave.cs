using CCCPRacing.Net;
using Godot;
namespace CCCPRacing.Tests;
public partial class PhysicsTest
{
    void LanLeavePromptTest()
    {
        var q=new LanLeavePrompt();q.Ask(10);
        Check(q.Open && !q.LeaveSelected,"LAN leave: opening defaults to staying in the game");
        Check(q.KeyPress(Key.Enter,false,10)==LanLeavePrompt.Result.None && q.Open,"LAN leave: opening frame cannot confirm a second action");
        Check(q.KeyPress(Key.Enter,false,11)==LanLeavePrompt.Result.Stay && !q.Open,"LAN leave: Enter on default stays connected");
        q.Ask(20);q.KeyPress(Key.Right,false,21);
        Check(q.LeaveSelected,"LAN leave: leaving requires an explicit navigation choice");
        Check(q.KeyPress(Key.Enter,true,22)==LanLeavePrompt.Result.None && q.Open,"LAN leave: held Enter cannot confirm leaving");
        Check(q.KeyPress(Key.Escape,false,23)==LanLeavePrompt.Result.Stay && !q.Open,"LAN leave: Escape cancels even with leave selected");
        q.Ask(30);q.KeyPress(Key.Down,false,31);
        Check(q.KeyPress(Key.Enter,false,32)==LanLeavePrompt.Result.Leave && !q.QuitApplication,"LAN leave: deliberate selection and new confirmation leave the session");
        q.Cancel();q.Ask(40);q.KeyPress(Key.Right,false,41);q.Ask(42);
        Check(q.Open && q.LeaveSelected,"LAN leave: phase changes and repeated asks preserve the pending choice");
        q.Ask(43,true);
        Check(q.Open && q.QuitApplication && !q.LeaveSelected,"LAN leave: window close requires a new safe choice instead of escalating an armed session exit");
        Check(q.KeyPress(Key.Enter,false,43)==LanLeavePrompt.Result.None,"LAN leave: window-close event cannot also confirm quitting");
        q.KeyPress(Key.Right,false,44);
        Check(q.KeyPress(Key.Enter,false,45)==LanLeavePrompt.Result.Leave && q.QuitApplication,"LAN leave: deliberate window-close confirmation requests application quit");
        q.Cancel();q.Ask(50,true);q.KeyPress(Key.Left,false,51);q.KeyPress(Key.Enter,false,52);
        Check(!q.Open && !q.QuitApplication,"LAN leave: staying cancels a pending application quit");
        q.Ask(60);q.Select(true);q.KeyPress(Key.Escape,false,61);q.Ask(62);
        Check(!q.LeaveSelected,"LAN leave: reopening forgets an earlier leave selection");
    }
}
