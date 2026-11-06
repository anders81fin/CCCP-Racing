using CCCPRacing.Net;
using Godot;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    readonly LanLeavePrompt _lanLeaveFallback = new();
    LanLeavePrompt _lanLeave => (_net ?? LanSession.Current)?.LeavePrompt ?? _lanLeaveFallback;
    bool _lanLeaveAsk => _lanLeave.Open;
    public bool LanLeaveActive => _lanLeave.Open;
    bool LanJoined => _net is {Active:true,InLobby:true};

    void AskLanLeave(bool quit=false)
    {
        if(!LanJoined) return;
        _lanLeave.Ask(Engine.GetProcessFrames(),quit);
        LanLeaveHud();
    }
    void CancelLanLeave()
    {
        _lanLeave.Cancel();
        _overlayExitFrame=Engine.GetProcessFrames();
        LanLeaveHud();
    }
    void LanLeaveHud()
    {
        if(_hud==null) return;
        _hud.LanLeaveAsk=_lanLeave.Open;
        _hud.LanLeaveSelected=_lanLeave.LeaveSelected;
        _hud.LanLeaveHost=_net?.IsHost==true;
    }
    public bool RequestLanWindowClose()
    {
        if(!LanJoined) return false;
        AskLanLeave(true);return true;
    }
    // Called after pad translation but before any screen-specific raw handlers.
    bool LanLeaveRawInput(InputEvent e)
    {
        if(!_lanLeave.Open) return false;
        if(e is InputEventKey {Pressed:true,AltPressed:false} k)
        {
            var result=_lanLeave.KeyPress(k.Keycode,k.Echo,Engine.GetProcessFrames());
            if(result==LanLeavePrompt.Result.Leave)
            {
                bool quit=_lanLeave.QuitApplication;
                CancelLanLeave();
                if(InLanRace) LeaveLan(); else LanLeavePage();
                if(quit) CCCPRacing.Audio.AudioMix.Quit(this);
            }
            else if(result==LanLeavePrompt.Result.Stay) CancelLanLeave();
            LanLeaveHud();
        }
        else if(e is InputEventMouseButton {Pressed:true,ButtonIndex:MouseButton.Left})
        {
            int row=_hud.LanLeaveRowAt(_hud.GetLocalMousePosition());
            if(row>=0) {_lanLeave.Select(row==1);LanLeaveHud();}
        }
        return true;
    }
}
