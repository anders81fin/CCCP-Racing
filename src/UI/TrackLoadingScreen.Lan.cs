using System;
using CCCPRacing.Net;
using CCCPRacing.Track;
using Godot;
namespace CCCPRacing.UI;

public partial class TrackLoadingScreen
{
    LanSession _lan;
    public bool IsLan => _lan!=null;
    public static void ShowLan(SceneTree tree,LanSession session,bool pad)
    {
        if(Active?.IsLan==true) return;
        Active?.Cancel();
        var screen=new TrackLoadingScreen {Layer=100,_lan=session};
        Active=screen;tree.Root.AddChild(screen);
        screen._hud.LoadingTrack=false;screen._hud.Mode=HudMode.Lan;screen._hud.Pad=pad;
    }
    public override void _Process(double delta)
    {
        if(!IsLan) return;
        _hud.Lan.Page=LanPage.Loading;
        _hud.Lan.Title=_lan.Active?"LOADING TRACK":"SESSION CLOSED";
        _hud.Lan.Message=_lan.Active
            ? TrackIds.Label(_lan.SelectedTrack)+" - "+_lan.LoadedCount+"/"+_lan.Players.Count+" READY"
            : "FINISHING WORLD LOAD...";
        if(_lan.LastLoadError!=null) {_hud.Lan.Title="TRACK LOAD FAILED";_hud.Lan.Message=_lan.LastLoadError;}
        _hud.LanLeaveAsk=_lan.LeavePrompt.Open;_hud.LanLeaveSelected=_lan.LeavePrompt.LeaveSelected;_hud.LanLeaveHost=_lan.IsHost;
    }
    public override void _Input(InputEvent e)
    {
        if(!IsLan || e is InputEventKey {AltPressed:true}) return;
        Press(e);GetViewport().SetInputAsHandled();
    }
    bool PressLan(InputEvent e)
    {
        if(!IsLan) return false;
        if(!_lan.Active || !_lan.InLobby) return true;
        Key key=Key.None;bool echo=false;
        if(e is InputEventKey {Pressed:true,AltPressed:false} k) {key=k.Keycode;echo=k.Echo;_hud.Pad=false;}
        else if(e is InputEventJoypadButton {Pressed:true} b)
        {
            _hud.Pad=true;
            key=b.ButtonIndex switch {JoyButton.A=>Key.Enter,JoyButton.B or JoyButton.Start=>Key.Escape,
                JoyButton.DpadLeft or JoyButton.DpadUp=>Key.Left,JoyButton.DpadRight or JoyButton.DpadDown=>Key.Right,_=>Key.None};
        }
        else if(e is InputEventJoypadMotion m && Math.Abs(m.AxisValue)>.65f && m.Axis is JoyAxis.LeftX or JoyAxis.LeftY)
        {_hud.Pad=true;key=m.AxisValue<0?Key.Left:Key.Right;}
        var prompt=_lan.LeavePrompt;
        if(!prompt.Open) {if(key==Key.Escape && !echo)prompt.Ask(Engine.GetProcessFrames());return true;}
        var answer=prompt.KeyPress(key,echo,Engine.GetProcessFrames());
        if(answer==LanLeavePrompt.Result.Leave)
        {
            bool quit=prompt.QuitApplication;_lan.LeaveLoading();
            if(quit) CCCPRacing.Audio.AudioMix.Quit(this);
        }
        return true;
    }
}
