using System;
using System.IO;
using System.Linq;
using CCCPRacing.Net;
using CCCPRacing.UI;
using CCCPRacing.Audio;
using Godot;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    LanReplayCapture _lanReplayCapture;
    Ghost _lanReplayOwn;
    float _lanReplayStopAt=-1;
    bool _lanReplayAutoCheck;
    int _lanReplayRound;
    int _lanReplayAutoOpenedRound=-1;
    static LanReplayAccess.Lease _lanReplayLease; // stays owned across track/main-scene replacements, like the child PID
    readonly System.Collections.Generic.List<(LanReplayCapture capture,int round)> _lanReplayPending=new();
    void LanReplayBegin()
    {
        LanReplayPublishAccess(LanReplayAccess.Permission.Closed);
        LanReplayEnd();
        _lanReplayRound++;
        _lanReplayReady=null;
        _lanReplayButton?.Hide();
        if(OS.GetCmdlineUserArgs().Contains("--lan-replay-disable")) {GD.Print("LAN replay: disabled for baseline test");return;}
        _lanReplayStopAt=-1;
        _lanReplayAutoCheck=OS.GetCmdlineUserArgs().Contains("--lan-replay-auto-check");
        foreach(string arg in OS.GetCmdlineUserArgs()) if(arg.StartsWith("--lan-replay-capture-at="))
            _lanReplayStopAt=float.Parse(arg["--lan-replay-capture-at=".Length..],System.Globalization.CultureInfo.InvariantCulture);
        try {
            string root=string.IsNullOrEmpty(_dataDir)?ProjectSettings.GlobalizePath("user://recordings/lan"):Path.Combine(_dataDir,"lan-recordings");
            string path=Path.Combine(root,$"lan-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.lanreplay");
            var roster=_lanReveal.Grid.Where(_net.Players.ContainsKey).Select(id=>{
                var p=_net.Players[id];return new LanReplayFile.Player(id,p.Name,p.Slot);
            }).ToArray();
            var header=new LanReplayFile.Header(_track.Id,Track.TrackSignature.Compute(_track),LanSession.GameVersion,LanSession.Protocol,_lanReveal.Seed,_net.MyId,roster);
            _lanReplayOwn=new Ghost();
            _lanReplayOwn.Record(0,_car,0,0,0,_dayNight.Clock,damage:_carVis.Damage);
            _lanReplayCapture=new(_net,()=>LanPoseClock,path,header,()=>{
                var frozen=_lanReplayOwn.CopyWithTime(Math.Max(0,LanPoseClock));
                return ()=>frozen.ToBytes();
            });
            foreach(var (bytes,mesh) in _lanWorldPending) _lanReplayCapture.WorldOut(bytes,mesh);
            GD.Print("LAN replay: recording "+path);
        } catch(Exception e) {GD.PushWarning("LAN replay unavailable: "+e.Message);_lanReplayCapture=null;}
    }
    void LanReplaySample()
    {
        if(_lanReplayCapture is {Recording:true} && LanPoseClock<=1800) {
            _lanReplayOwn.Record(Math.Max(0,LanPoseClock),_car,_maxImpact,0,0,_dayNight.Clock,impactMaterial:_impactMaterial,impactBroke:_impactBroke,damage:_carVis.Damage);
            if(_lanReplayOwn.RecordingEstimatedBytes>128L*1024*1024) {_lanReplayCapture.Abort("LAN replay original-car memory limit reached");LanReplayEnd();}
        }
        else if(LanPoseClock>1800 && _lanReplayCapture!=null) {_lanReplayCapture.Abort("LAN replay duration limit reached (30 minutes)");LanReplayEnd();}
    }
    void LanReplayEnd()
    {
        var capture=_lanReplayCapture;_lanReplayCapture=null;
        if(capture==null) return;
        capture.Dispose();
        // No Godot calls on the disk worker; report completion while the scene remains alive.
        _lanReplayPending.Add((capture,_lanReplayRound));
    }
    bool LanReplayAvailable=>_lanReplayReady!=null && _net!=null && LanReplayAccess.For(_net.Active,_net.State)!=LanReplayAccess.Permission.Closed;
    string _lanReplayReady;
    Button _lanReplayButton;
    static int _lanReplayViewerPid;
    static bool _lanReplayMuted,_lanReplayOldMute;
    void LanReplayOpen()
    {
        if(_lanReplayReady==null || _lanLeaveAsk || !LanReplayAvailable) return;
        if(LanReplayViewerAlive()) return;
        try {
            _lanReplayLease=new(1,Guid.NewGuid().ToString("N"),System.Environment.ProcessId,_lanReplayRound,LanReplayAccess.For(_net.Active,_net.State));
            LanReplayAccess.Write(_lanReplayLease);
            var args=new System.Collections.Generic.List<string>();
            if(OS.HasFeature("editor")) {args.Add("--path");args.Add(ProjectSettings.GlobalizePath("res://"));}
            bool probe=_lanReplayAutoCheck;
            bool exportProbe=probe && OS.GetCmdlineUserArgs().Contains("--lan-replay-export-check");
            if(probe) {if(!exportProbe) args.Add("--headless");args.Add("--audio-driver");args.Add("Dummy");args.Add("--max-fps");args.Add("60");}
            args.Add("--log-file");args.Add(_lanReplayReady+".viewer.log");
            args.Add("--");args.Add("--lan-replay="+_lanReplayReady);
            args.Add("--lan-replay-access="+LanReplayAccess.PathFor(_lanReplayLease.Token));
            args.Add("--lan-replay-sounds="+string.Join(",",SoundBalance.Levels));
            args.Add($"--lan-replay-video={_aaMode},{_softLevel}");
            if(AudioServer.IsBusMute(0)) args.Add("--lan-replay-muted");
            if(exportProbe) args.Add("--lan-export-ui-run="+_lanReplayReady+".export-ui.png");
            else if(probe) args.Add("--lan-replay-check");
            _lanReplayViewerPid=OS.CreateInstance(args.ToArray());
            if(_lanReplayViewerPid<=0) throw new IOException("Could not start replay window");
            _lanReplayAutoOpenedRound=_lanReplayRound;
            _lanReplayOldMute=AudioServer.IsBusMute(0);AudioServer.SetBusMute(0,true);_lanReplayMuted=true;
            GD.Print("LAN replay: independent viewer process "+_lanReplayViewerPid+"; transport continues");
        } catch(Exception e) {if(_lanReplayLease!=null) LanReplayAccess.DeleteOwned(LanReplayAccess.PathFor(_lanReplayLease.Token));_lanReplayLease=null;GD.PushWarning("LAN replay could not open: "+e.Message);}
    }
    void LanReplayPublishAccess(LanReplayAccess.Permission permission)
    {
        if(_lanReplayLease==null || _lanReplayLease.Access==permission) return;
        try {var updated=_lanReplayLease with {Access=permission};LanReplayAccess.Write(updated);_lanReplayLease=updated;}
        catch(Exception e) {GD.PushWarning("LAN replay access update failed: "+e.Message);LanReplayAccess.DeleteOwned(LanReplayAccess.PathFor(_lanReplayLease.Token));}
    }
    void LanReplayShutdownAccess()
    {
        if(_lanReplayLease==null || !LanReplayViewerAlive()) return;
        // A completed session's offline viewer/export may finish after the game closes.
        if(_lanReplayLease.Access!=LanReplayAccess.Permission.Export || _net is {Active:true} && _net.State!=LanSession.Phase.SessionFinished)
            LanReplayPublishAccess(LanReplayAccess.Permission.Closed);
    }
    bool LanReplayViewerAlive()
    {
        if(_lanReplayViewerPid<=0) return false;
        if(OS.IsProcessRunning(_lanReplayViewerPid)) return true;
        // Query/reap each child only once after it exits; Godot drops its child registration.
        _lanReplayViewerPid=0;
        if(_lanReplayLease!=null) {LanReplayAccess.DeleteOwned(LanReplayAccess.PathFor(_lanReplayLease.Token));_lanReplayLease=null;}
        if(_lanReplayMuted) {AudioServer.SetBusMute(0,_lanReplayOldMute);_lanReplayMuted=false;}
        return false;
    }
    void LanReplayPoll()
    {
        LanReplayViewerAlive();
        if(_lanReplayLease!=null) {
                var access=_net==null?LanReplayAccess.Permission.Closed:LanReplayAccess.For(_net.Active,_net.State);
                if(_net?.Active!=true && _lanReplayLease.Access==LanReplayAccess.Permission.Export) access=LanReplayAccess.Permission.Export;
                LanReplayPublishAccess(access);
        }
        if(_lanReplayStopAt>=0 && _lanReplayCapture is {Recording:true} && LanPoseClock>=_lanReplayStopAt) LanReplayEnd();
        if(_lanReplayCapture is {LocalFinished:true}) {LanReplaySample();LanReplayEnd();}
        if(_lanReplayCapture is {} active && active.Completion.IsCompleted) LanReplayEnd();
        for(int i=_lanReplayPending.Count-1;i>=0;i--) {
            var (c,round)=_lanReplayPending[i];
            if(!c.Completion.IsCompleted) continue;
            _lanReplayPending.RemoveAt(i);
            string path=c.Completion.GetAwaiter().GetResult();
            if(round==_lanReplayRound && _lanReplayCapture==null) _lanReplayOwn=null;
            if(path==null) {
                GD.PushWarning(c.Error??"LAN replay save failed");
                _hud?.Alert("REPLAY NOT SAVED",c.Error?.Contains("limit")==true?"RECORDING LIMIT REACHED":"RECORDING STORAGE IS UNAVAILABLE",4f,UI.Hud.Red);
                continue;
            }
            GD.Print("LAN replay: saved "+path);
            if(round!=_lanReplayRound) continue;
            _lanReplayReady=path;
            if(_lanReplayButton==null) {
                var layer=new CanvasLayer {Layer=25};AddChild(layer);
                _lanReplayButton=new Button {Text="R  REPLAY",Position=new Vector2(8,GetViewport().GetVisibleRect().Size.Y-30)};
                _lanReplayButton.AddThemeFontSizeOverride("font_size",11);layer.AddChild(_lanReplayButton);
                _lanReplayButton.Pressed+=LanReplayOpen;
            }
        }
        if(_lanReplayButton!=null) {
            _lanReplayButton.Visible=_lanReplayReady!=null && _net is {Active:true} && (_lanDone || _lanDnf || LanReplayAvailable);
            _lanReplayButton.Disabled=_lanLeaveAsk || !LanReplayAvailable;
            _lanReplayButton.Text=LanReplayAvailable?"R  REPLAY":"REPLAY AVAILABLE AFTER THE RACE";
        }
        if(_lanReplayAutoCheck && _lanReplayAutoOpenedRound!=_lanReplayRound && LanReplayAvailable) LanReplayOpen();
    }
}
