using System;
using System.Collections.Generic;
using System.Linq;
using CCCPRacing.Net;
using CCCPRacing.Track;
using CCCPRacing.UI;
using Godot;
namespace CCCPRacing.Race;

public partial class RaceScene
{
    static bool _lanWorldRebuild;
    int _lanTrackChoice;
    // Ballot row ids: 0 fuel, 1 damage, 2 fuel consumption, 3 track, 4 moving obstacles. Navigation follows the
    // card from top to bottom; a single track is shown but not chosen, and an Ilyich-only
    // ballot has no fuel-consumption choice.
    int[] LanBallotOrder => _net.TrackOptions.Count>1
        ? new[]{3,0,1,2,4}
        : _net.TrackOptions.Any(t=>t!="ilyich") ? new[]{0,1,2,4} : new[]{0,1,4};
    void StepLanBallotRow(bool up)
    {
        var order=LanBallotOrder;
        int i=Math.Max(0,Array.IndexOf(order,_lanBallotRow));
        _lanBallotRow=order[(i+(up?order.Length-1:1))%order.Length];
    }
    string[] _lanTestTracks, _lanTrackSequence=Array.Empty<string>();
    string _lanTrackVoteFlag;
    float _lanLoadDelay;
    double _lanReadyAt=-1;

    IEnumerable<string> LanHostTracks()
    {
        if(_lanTestTracks!=null) return _lanTestTracks.Where(TrackIds.Known);
        return Tracks.Where(t=>t.requires==null || (t.requires==Bonus
            ? Tracks.Any(x=>_campaign.Best(x.id)==NormPlan.Medal.Gold)
            : _campaign.Best(t.requires)>=NormPlan.Medal.Bronze)).Select(t=>t.id);
    }
    void OnLanWorldBytes(byte[] b)=>LanWorldPacket(b,false);
    void OnLanMeshBytes(byte[] b)=>LanWorldPacket(b,true);
    void DetachLan()
    {
        if(_net==null) return;
        LanBoardsDetach();      // RaceScene.LanBoards.cs: this scene's records stop being the session's
        _net.Closed-=OnLanClosed;_net.TrackRequired-=OnLanTrackRequired;
        _net.LoadRequested-=OnLanLoadTrack;_net.LoadFailed-=OnLanLoadFailed;
        _net.Changed-=OnLanChanged;_net.BallotOpened-=OnLanBallot;
        _net.RevealIn-=OnLanReveal;_net.WinnerIn-=OnLanWinner;_net.ResultsIn-=OnLanResults;_net.SessionEnded-=OnLanSessionEnded;
        _net.PlayerLeft-=OnLanLeft;_net.SnapIn-=LanSnapshotIn;_net.InputIn-=LanInputIn;_net.RescueIn-=LanRescueIn;
        _net.WorldIn-=OnLanWorldBytes;_net.MeshIn-=OnLanMeshBytes;_net.EffectIn-=LanEffectIn;_net.BrokeIn-=OnLanBroke;
    }
    void OnLanLoadTrack()
    {
        _lanWorldPending.Clear();_showLog=_askName=false;
        SetLanPage(LanPage.Loading);
        TrackLoadingScreen.ShowLan(GetTree(),_net,_padMode);
        if(_track.Id==_net.SelectedTrack) {ScheduleLanWorldReady();return;}
        _lanWorldRebuild=true;_trackId=_net.SelectedTrack;_resume=MenuItem.Lan;
        GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
    }
    void ScheduleLanWorldReady()
    {
        if(_lanLoadDelay>0) _lanReadyAt=LanSession.Now+_lanLoadDelay;
        else ReportLanWorldReady();
    }
    void ReportLanWorldReady()
    {
        if(!_net.Active || _net.State!=LanSession.Phase.Loading) return;
        _net.WorldLoaded(_track.Id,TrackSignature.Compute(_track));
    }
    bool FinishLanWorldRebuild()
    {
        if(!_lanWorldRebuild) return false;
        _lanWorldRebuild=false;_resume=null;_showLog=_askName=false;
        _lanName=_net.MyName;
        if(_net.Active && _net.State==LanSession.Phase.Loading)
        {SetLanPage(LanPage.Loading);ScheduleLanWorldReady();}
        else
        {
            TrackLoadingScreen.Active?.Cancel();
            if(_net.Active) OnLanLoadFailed(_net.LastLoadError??"ROUND CANCELLED");
            else LanMessage("SESSION CLOSED",_net.LastCloseReason??"THE SESSION HAS ENDED",LanPage.Join);
        }
        return true;
    }
    void OnLanLoadFailed(string reason)
    {
        _lanReadyAt=-1;_lanAutoDone=true;
        if(_booting) return;
        TrackLoadingScreen.Active?.Cancel();
        LanMessage("TRACK LOAD FAILED",reason,LanPage.Lobby);
    }
}
