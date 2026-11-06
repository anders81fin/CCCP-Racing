using System;
using System.Collections;
using System.Linq;
using CCCPRacing.Replay;
using CCCPRacing.UI;
using Godot;
namespace CCCPRacing.Race;
public partial class RaceScene
{
    IEnumerable VwLapOrder()
    {
        foreach(var x in VwWaitReady())yield return x;
        foreach(var mode in new[]{RunMode.Training,RunMode.Rally}) {
            _run=mode;_noRecord=false;_autodrive=false;
            EnterCountdown();_state=State.Racing;
            for(int lap=1;lap<=3;lap++) {
                _timer.RestoreCompletedLaps(lap);_rec.Clear();TapeBegin();
                Vector3 start=_car.Pos;
                for(int i=0;i<=90;i++) {
                    float t=i/30f;_car.Pos=start+Vector3.Forward*(i*.1f);
                    _rec.Record(t+.00001f,_car,0,0,0,400,damage:_carVis.Damage);
                    if(_tapeOn)_tapeRec.Record(t);
                }
                _autodrive=lap!=1; // first is a human record; next ordinary/void laps use the recent archive
                LapArchiveCapture(3,lap==1?LapKind.Record:lap==3?LapKind.Void:LapKind.Normal,lap!=3,false);
            }
            _autodrive=false;_state=State.Paused;_hud.Mode=HudMode.Paused;
            VwCheck(_sessionLapArchives.Count==3 && _sessionLapArchives.Values.Distinct().Count()==2,mode+": capture path spans recent and leaderboard archives");
            var last=_sessionLapOrder.OrderBy(x=>x.Value).Last().Key;
            var keep=ReplayArchiveFor(last).Retain(last,"THIRD LAP");
            foreach(var x in VrUntil(()=>keep.IsCompleted,1800,"retain third lap"))yield return x;
            VwCheck(keep.IsCompletedSuccessfully && keep.Result.Ok,mode+": ordinary lap can be retained");
            var pin=ReplayArchiveFor(last).SaveEdits(last,new LapEdits{Pinned=true});
            foreach(var x in VrUntil(()=>pin.IsCompleted,1800,"pin third lap"))yield return x;
            OpenReplayList();
            foreach(var x in VrUntil(()=>{PollReplayListing();return _hud.ReplayList.Rows.Length==3;},1800,"all replay rows"))yield return x;
            VwCheck(_replayInfos.Select(i=>i.Meta.LapNumber).SequenceEqual(new[]{3,1,2}),mode+": pinned LAP 3 comes first, then LAP 1 and LAP 2 in capture order");
            VwCheck(_replayInfos[0].Pinned,mode+": the pinned recording is the first row");
            VwCheck(_hud.ReplayList.Rows.Select((r,i)=>r.Title.StartsWith("LAP "+_replayInfos[i].Meta.LapNumber+"  ")).All(x=>x),mode+": visible recording titles include their lap number");
            VwCheck(_replayInfos.All(i=>DefaultKeepName(i).StartsWith("LAP "+i.Meta.LapNumber+" ")),mode+": default saved names include their lap number");
            foreach(var x in VrShot(mode==RunMode.Training?"lap-order-stakhanov":"lap-order-rally"))yield return x;
            string first=_replayInfos[1].Id;var source=ReplayArchiveFor(first).Open(first);string hash=source.Sha256();
            ChooseReplayRow(1);
            foreach(var x in VrUntil(()=>{PollReplayOpen();return _replayViewer?.State==ReplayViewerState.Playing;},7200,"first lap viewer"))yield return x;
            VwCheck(_replayViewer?.Scene?.ViewerSource?.Lap?.Meta.LapNumber==1 && _replayViewer.Scene.ViewerSource.Lap.Sha256()==hash,mode+": selecting LAP 1 opens that exact original recording");
            if (mode==RunMode.Training && _replayViewer!=null) foreach(var x in VwEditVShortcut(_replayViewer)) yield return x;
            _replayViewer?.Close();
            foreach(var x in VrUntil(()=>_replayViewer==null,1800,"return to numbered list"))yield return x;
            CloseReplayList();
        }
    }
    IEnumerable VwEditVShortcut(ReplayViewerSession s)
    {
        var v=s.Scene;v.ViewerSetPaused(true);GetWindow().GrabFocus();
        foreach(var x in VrUntil(()=>GetWindow().HasFocus(),600,"edit shortcut window focus"))yield return x;
        foreach(var x in VrTap(Key.E))yield return x;
        VwCheck(!s.EditOpen,"E no longer opens EDIT in preset-camera playback");
        foreach(var x in VrTap(Key.V))yield return x;
        VwCheck(s.EditOpen,"V opens EDIT in preset-camera playback");
        foreach(var x in VrTap(Key.V))yield return x;
        VwCheck(!s.EditOpen,"V closes EDIT in preset-camera playback");
        foreach(var x in VrTap(Key.W,8))yield return x;
        VwCheck(s.FreeCameraActive,"direct W entry reaches free camera for EDIT shortcut test");
        if (!s.FreeCameraActive || v.ViewerFreeRig==null) yield break;
        float y=v.ViewerFreeRig.Position.Y;
        foreach(var x in VrTap(Key.E,20))yield return x;
        VwCheck(v.ViewerFreeRig.Position.Y>y+.01f && !s.EditOpen,"E still raises the free camera without opening EDIT");
        var pose=v.ViewerCameraPose;
        foreach(var width in new[]{480,208})
            VwCheck(ReplayOverlay.Layout(s.OverlayView,new Vector2(width,270)).Texts.Any(t=>t.Text=="V EDIT/EXPORT"),"free-camera controls visibly include V EDIT/EXPORT at "+width+" pixels");
        foreach(var x in VrShot("edit-v-free-controls"))yield return x;
        foreach(var x in VrTap(Key.V))yield return x;
        VwCheck(s.EditOpen && s.FreeCameraActive && v.ViewerCameraPose.IsEqualApprox(pose),"V opens EDIT over the same free-camera composition");
        var held=v.ViewerCameraPose;
        foreach(var x in VrTap(Key.W,12))yield return x;
        VwCheck(v.ViewerCameraPose.IsEqualApprox(held),"free-camera movement is suspended while EDIT owns the controls");
        foreach(var x in VrShot("edit-v-free-panel"))yield return x;
        foreach(var x in VrTap(Key.Escape))yield return x;
        VwCheck(!s.EditOpen && s.FreeCameraActive,"ESC closes EDIT and returns to the free camera");
        foreach(var x in VrTap(Key.R))yield return x;
        VwCheck(!s.FreeCameraActive && s.State==ReplayViewerState.Playing,"R still returns from the free camera to preset playback");
    }

}
