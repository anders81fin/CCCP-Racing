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
    // card from top to bottom; a single track is shown but not chosen, and a ballot of
    // dry tracks only (Ilyich, Atomgrad-4) has no fuel-consumption choice.
    int[] LanBallotOrder => LanBallotRows(_net.TrackOptions);
    /// <summary>The navigable rows for a set of track options: the fuel-consumption row (2) only when some option has fuel (not for Ilyich/Atomgrad only), the track row (3) only with a choice of tracks.</summary>
    internal static int[] LanBallotRows(IReadOnlyCollection<string> options)
    {
        bool fuel = options.Any(TrackIds.HasFuel);
        if (options.Count > 1) return fuel ? new[]{3,0,1,2,4} : new[]{3,0,1,4};
        return fuel ? new[]{0,1,2,4} : new[]{0,1,4};
    }
    /// <summary>Fuel key 1..4 is a grade: is it on the ballot (the comrade with the most opened offers his fuels)?</summary>
    bool LanFuelKeyOffered(int key) => key is >= 1 and <= LanRace.FuelCount && (_net.FuelMask >> (key - 1) & 1) != 0;
    /// <summary>The offered key nearest to this one (the key itself when it is offered).</summary>
    int LanFuelKeyNear(int key)
    {
        key = Math.Clamp(key, 1, LanRace.FuelCount);
        for (int d = 0; d < LanRace.FuelCount; d++)
        {
            if (LanFuelKeyOffered(key - d)) return key - d;
            if (LanFuelKeyOffered(key + d)) return key + d;
        }
        return 1;
    }
    /// <summary>One step left or right on the fuel row, over the fuels that are not offered; at the end it stays.</summary>
    int LanFuelStep(int dir)
    {
        for (int k = _lanChoice + dir; k >= 1 && k <= LanRace.FuelCount; k += dir) if (LanFuelKeyOffered(k)) return k;
        return _lanChoice;
    }
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

    /// <summary>What this machine has opened, for the LAN lobby: the tracks the campaign has opened and the fuels opened (silvers on the fuel below); a test host offers the tracks of --lan-tracks and every fuel.</summary>
    Net.LanUnlocks LanUnlocksNow()
    {
        if(_lanTestTracks!=null) return new Net.LanUnlocks(LanHostTracks().ToList(),Net.LanUnlocks.AllFuels);
        int fuels=0;
        for(int g=0;g<FuelGrades.Count;g++)
        {
            var grade=(FuelGrade)g;
            if(CheatUnlock || FuelOpenByMedals(grade)) fuels|=1<<g;
        }
        return new Net.LanUnlocks(LanHostTracks().ToList(),fuels);
    }

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
