using System.Collections.Generic;
using CCCPRacing.UI;
using Godot;
namespace CCCPRacing.Race;

public partial class RaceScene
{
    bool _lanLeaveProbe;
    readonly HashSet<string> _lanLeaveProbed=new();
    string _lanLeaveProbePhase;
    int _lanLeaveProbeStep;
    ulong _lanLeaveProbeFrame;

    void LanLeaveProbeTick()
    {
        if(!_lanLeaveProbe || !LanJoined) return;
        ulong frame=Engine.GetProcessFrames();
        if(frame<=_lanLeaveProbeFrame) return;
        _lanLeaveProbeFrame=frame;
        string phase=_lanPage switch
        {
            LanPage.SessionDecision=>"session-decision",LanPage.SessionFinal=>"session-final",LanPage.SessionResults=>"session-results",LanPage.Loading=>"loading",LanPage.Lobby=>"lobby", LanPage.Ballot=>"ballot", LanPage.Reveal=>"reveal", LanPage.Results=>"results",
            LanPage.None when InLanRace=>_state==State.Countdown?"grid":"race", _=>null
        };
        void Assert(bool ok,string why)
        {
            if(ok) return;
            GD.PrintErr("LANLEAVETEST: FAIL "+_lanLeaveProbePhase+" "+why);GetTree().Quit(4);throw new System.InvalidOperationException(why);
        }
        void Key(Key key) => RawInput(new InputEventKey {Keycode=key,PhysicalKeycode=key,Pressed=true});
        if(_lanLeaveProbeStep==0)
        {
            if(phase==null || _lanLeaveProbed.Contains(phase)) return;
            _lanLeaveProbePhase=phase;
            if(_lanPage is LanPage.None or LanPage.Loading) AskLanLeave();else Key(Godot.Key.Escape);
            _lanLeaveProbeStep=1;return;
        }
        switch(_lanLeaveProbeStep++)
        {
            case 1:
                Assert(_lanLeaveAsk && !_lanLeave.LeaveSelected && _net.Active,"safe default did not open");
                if(_lanShots!=null) LanShotIn(0,_lanLeaveProbePhase+"-leave");
                break;
            case 2:
                var page=_lanPage;var state=_state;
                Key(Godot.Key.Enter);
                Assert(!_lanLeaveAsk && _net.Active && _lanPage==page && _state==state,"default Enter changed page, paused or disconnected");
                AskLanLeave();break;
            case 3:
                if(_lanLeaveProbePhase=="race") Assert(_car.ThrottleTarget<.01f && System.Math.Abs(_car.SteerIn)<.001f,"modal navigation leaked into driving controls");
                Key(Godot.Key.Right);Assert(_lanLeave.LeaveSelected,"right did not select leave");
                RawInput(new InputEventKey {Keycode=Godot.Key.Enter,Pressed=true,Echo=true});
                Assert(_lanLeaveAsk && _net.Active,"held Enter confirmed exit");
                Key(Godot.Key.Escape);Assert(!_lanLeaveAsk && _net.Active,"Escape did not cancel");
                RequestLanWindowClose();break;
            case 4:
                Assert(_lanLeaveAsk && _lanLeave.QuitApplication,"window close bypassed confirmation");
                Key(Godot.Key.Enter);Assert(!_lanLeaveAsk && _net.Active,"window-close default did not stay");
                AskLanLeave();break;
            case 5:
                PadButton(JoyButton.DpadRight,true,false);PadButton(JoyButton.DpadRight,false,false);
                break;
            case 6:
                Assert(_lanLeave.LeaveSelected,"controller d-pad did not navigate");
                PadButton(JoyButton.B,true,false);PadButton(JoyButton.B,false,false);
                break;
            case 7:
                Assert(!_lanLeaveAsk && _net.Active,"controller B did not cancel");
                _lanLeaveProbed.Add(_lanLeaveProbePhase);
                GD.Print("LANLEAVETEST: PASS "+_lanLeaveProbePhase+" "+(_net.IsHost?"host":"client"));
                _lanLeaveProbeStep=0;break;
        }
    }
}
