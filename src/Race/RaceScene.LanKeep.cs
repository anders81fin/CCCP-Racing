using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CCCPRacing.Net;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// KEEP after a LAN race (docs/lan-replay-access.md): once per round, when the own race is over (finished, or the
/// results for a DNF), a question offers to keep the round's recording. KEEP asks for a name (empty = the race's own
/// words) and copies the finished journal into the kept folder through LanKeepFile (verified, never touching the
/// automatic original). NOT NOW saves nothing. No viewer opens: the live session has no player-facing watch entry.
/// The question owns keys and pad while open; the LAN session, transport and results run on regardless.
/// </summary>
public partial class RaceScene
{
    // The question and a save in flight outlive a track reload (like the replay lease): a notice still lands.
    static readonly LanKeepPrompt _lanKeep=new();
    static Task<LanKeepFile.Result> _lanKeepSave;
    static string _lanKeepSaveName,_lanKeepSaveSource;
    string _lanReplayRoundToken;
    string _lanReplayRoot;
    double _lanKeepOfferAt=-1;
    bool _lanKeepWasDone;          // the own finish, host (LanLap) or joined (the host's correction): its rising edge asks
    /// <summary>Test seam: every viewer process this game started (KEEP and the results must never add one).</summary>
    internal static int LanReplayViewerLaunches;
    internal static LanKeepPrompt LanKeepForTest=>_lanKeep;
    string _lanKeepProbe;          // --lan-keep-probe=keep|notnow: the in-scene check (LanKeepTest.cs); autodrive runs otherwise skip the question
    bool _lanKeepSkipLogged;

    string LanRecordingsRoot=>string.IsNullOrEmpty(_dataDir)?ProjectSettings.GlobalizePath("user://recordings/lan"):Path.Combine(_dataDir,"lan-recordings");
    string LanKeptShown=>string.IsNullOrEmpty(_dataDir)?"RECORDINGS/LAN/KEPT":"LAN-RECORDINGS/KEPT";

    void LanKeepBeginRound()
    {
        _lanReplayRoundToken=Guid.NewGuid().ToString("N");
        _lanKeepOfferAt=-1;_lanKeepSkipLogged=false;_lanKeepWasDone=false;
        _lanKeep.BeginRound(_lanReplayRoundToken);
    }

    /// <summary>The own race is over: ask a moment later (FINISHED! has the screen first). At the results at once.</summary>
    void LanKeepOfferIn(double seconds)
    {
        double at=LanSession.Now+seconds;
        if(_lanKeepOfferAt<0 || at<_lanKeepOfferAt) _lanKeepOfferAt=at;
    }

    string LanKeepDefaultName()
    {
        string time=_lanDnf?"DNF":LapTimer.Format(_lanRaceTime).Replace(':','-').Replace('.','-');
        return CleanKeepName($"{CCCPRacing.Track.TrackIds.Label(_track.Id)} LAN {time}");
    }

    /// <summary>Per frame (with LanReplayPoll): the question's opening, closing, a save's outcome and the notices.</summary>
    void LanKeepTick(double dt)
    {
        if(_lanDone && !_lanKeepWasDone && InLanRace && _net?.State==LanSession.Phase.Race) LanKeepOfferIn(3.6);      // after FINISHED!; others may still race
        _lanKeepWasDone=_lanDone;
        if(_lanKeepOfferAt>=0 && LanSession.Now>=_lanKeepOfferAt && !_lanLeaveAsk)
        {
            _lanKeepOfferAt=-1;
            if(_lanKeep.Recording==LanKeepPrompt.Source.None && _lanKeep.Round==_lanReplayRoundToken)
            {
                GD.Print($"LAN keep: not offered (round {_lanReplayRound} not recorded on this machine)");
                if(_lanKeepProbe!=null) LanKeepProbeOff();
            }
            else if(_autodrive && _lanKeepProbe==null) {if(!_lanKeepSkipLogged) GD.Print("LAN keep: question skipped in an autodrive run (no --lan-keep-probe)");_lanKeepSkipLogged=true;}
            else if(_net is {Active:true} && _lanKeep.Offer(_lanReplayRoundToken,Engine.GetProcessFrames()))
                GD.Print($"LAN keep: question open (round {_lanReplayRound}, recording {_lanKeep.Recording})");
        }
        // The next ballot is the player's own 60 seconds: the question never stands in front of it.
        if(_lanKeep.OwnsInput && (_net is not {Active:true} || _lanPage is LanPage.Ballot or LanPage.Reveal or LanPage.Loading)) _lanKeep.Close();
        if(_lanKeepSave is {IsCompleted:true} t)
        {
            _lanKeepSave=null;
            var r=t.IsFaulted?new LanKeepFile.Result(false,null,t.Exception?.GetBaseException().Message ?? "UNKNOWN ERROR"):t.Result;
            if(r.Ok) {_lanKeep.Saved(_lanKeepSaveName,LanKeptShown);GD.Print($"LAN keep: KEPT '{_lanKeepSaveName}' as {r.Path} (original {_lanKeepSaveSource} untouched)");}
            else {_lanKeep.Failed(("NOT KEPT: "+r.Message).ToUpperInvariant());GD.PushWarning($"LAN keep: FAILED '{_lanKeepSaveName}': {r.Message} (original {_lanKeepSaveSource} untouched)");}
        }
        _lanKeep.Tick(dt);
        if(_hud!=null)
        {
            _hud.LanKeep=_lanKeep.Visible?_lanKeep:null;
            _hud.LanKeepSaving=_lanKeepSave!=null;
            _hud.LanKeepWhere=LanKeptShown;
            _hud.LanKeepDefault=_lanKeep.OwnsInput && _track!=null?LanKeepDefaultName():"";
        }
        LanKeepProbeTick();
    }

    /// <summary>Raw input while the question is open: all of it (Results' Enter and Esc never fall through). True: taken.</summary>
    bool LanKeepRawInput(InputEvent e)
    {
        if(!_lanKeep.OwnsInput || _lanLeaveAsk) return false;
        ulong frame=Engine.GetProcessFrames();
        var action=LanKeepPrompt.Action.None;
        if(e is InputEventKey {Pressed:true} k)
        {
            if(k.AltPressed || k.CtrlPressed || k.MetaPressed) return true;
            char typed=k.Unicode is > 0 and <= char.MaxValue?(char)k.Unicode:default;
            action=_lanKeep.Key(k.Keycode,k.Echo,frame,typed);
        }
        else if(e is InputEventMouseButton {Pressed:true,ButtonIndex:MouseButton.Left} && _lanKeep.State==LanKeepPrompt.Phase.Choose)
        {
            int row=_hud.LanKeepRowAt(_hud.GetLocalMousePosition());
            if(row>=0) action=_lanKeep.Choose(row,frame);
        }
        else if(e is InputEventMouseMotion && _lanKeep.State==LanKeepPrompt.Phase.Choose)
        {
            int row=_hud.LanKeepRowAt(_hud.GetLocalMousePosition());
            if(row>=0) _lanKeep.Select(row==0);
        }
        if(action==LanKeepPrompt.Action.Save) LanKeepStartSave();
        if(!_lanKeep.OwnsInput) _overlayExitFrame=frame;
        return true;
    }

    void LanKeepStartSave()
    {
        string name=CleanKeepName(_lanKeep.Name);
        if(name.Length==0) name=LanKeepDefaultName();
        if(_lanKeepSave!=null) {_lanKeep.Failed("STILL SAVING THE LAST RECORDING - WAIT");return;}
        _lanKeepSaveName=name;_lanKeepSaveSource=_lanKeep.SourcePath;
        _lanKeepSave=LanKeepFile.Retain(_lanKeep.SourcePath,LanKeepFile.KeptDir(_lanReplayRoot ?? LanRecordingsRoot),name,DateTime.Now);
        GD.Print($"LAN keep: saving '{name}' from {_lanKeep.SourcePath}");
    }
}
