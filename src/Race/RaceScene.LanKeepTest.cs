using System;
using System.IO;
using System.Linq;
using CCCPRacing.Net;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// --lan-keep-probe[=keep|notnow]: answers the real KEEP question in a real LAN run through the production input path
/// (keys, pad, echo) and checks it: the question owns the keys (Results' Enter/Esc and R change nothing, no viewer
/// starts), KEEP waits for a finished file, the name step backs out, the kept copy verifies and the automatic
/// original is unchanged, NOT NOW writes nothing, and a round never asks twice. Prints LANKEEPTEST: PASS/FAIL.
/// </summary>
public partial class RaceScene
{
    int _lanKeepProbeStep;
    ulong _lanKeepProbeFrame;
    string _lanKeepProbeRound, _lanKeepProbeDone, _lanKeepProbeHash, _lanKeepProbeSource;
    int _lanKeepProbeLaunches, _lanKeepProbeKept, _lanKeepProbeOffers;
    LanPage _lanKeepProbePage;
    double _lanKeepProbeSince;
    bool _lanKeepProbePendingSeen;

    static string LanKeepHash(string path)
    {
        using var s=File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(s));
    }

    /// <summary>RECORD LAN RACES? NO: at the moment KEEP would be offered, check that nothing was asked or recorded.</summary>
    void LanKeepProbeOff()
    {
        string root=_lanReplayRoot ?? LanRecordingsRoot;
        string[] journals=Directory.Exists(root)?Directory.GetFiles(root,"*",SearchOption.AllDirectories):Array.Empty<string>();
        string why=LanRecordOn?"recording is on":_lanReplayCapture!=null?"a capture exists":_lanReplayOwn!=null?"an original-car ghost is recording"
            :_lanReplayPending.Count>0?"a journal writer is pending":_lanKeep.Visible || _lanKeep.Offered(_lanReplayRoundToken)?"KEEP was offered"
            :journals.Length>0?$"{journals.Length} file(s) under {root}":LanReplayViewerLaunches>0?"a viewer was started":null;
        if(why!=null) {GD.PrintErr($"LANKEEPTEST: FAIL off round {_lanReplayRound}: {why}");GetTree().Quit(4);return;}
        GD.Print($"LANKEEPTEST: PASS off round {_lanReplayRound} {(_net.IsHost?"host":"client")} (no capture, ghost, journal, files or KEEP; {root} {(Directory.Exists(root)?"exists, empty":"absent")})");
    }

    void LanKeepProbeTick()
    {
        if(_lanKeepProbe==null || _net is not {Active:true}) return;
        ulong frame=Engine.GetProcessFrames();
        if(frame<=_lanKeepProbeFrame) return;
        _lanKeepProbeFrame=frame;
        string kept=LanKeepFile.KeptDir(_lanReplayRoot ?? LanRecordingsRoot);
        void Assert(bool ok,string why)
        {
            if(ok) return;
            GD.PrintErr($"LANKEEPTEST: FAIL step {_lanKeepProbeStep} {why}");GetTree().Quit(4);throw new InvalidOperationException(why);
        }
        void Key(Key key,bool echo=false)
        {
            long uni=key is >= Godot.Key.A and <= Godot.Key.Z?(long)key:0;
            RawInput(new InputEventKey {Keycode=key,PhysicalKeycode=key,Unicode=uni,Pressed=true,Echo=echo});
        }
        void Unchanged(string what)
        {
            Assert(_net.Active && _lanPage==_lanKeepProbePage,what+": the LAN page changed or the session closed");
            Assert(LanReplayViewerLaunches==_lanKeepProbeLaunches,what+": a replay viewer was started");
        }
        if(_lanKeepProbeStep==0)
        {
            if(!_lanKeep.OwnsInput || _lanKeep.Round==_lanKeepProbeDone) return;
            _lanKeepProbeRound=_lanKeep.Round;_lanKeepProbePage=_lanPage;_lanKeepProbeLaunches=LanReplayViewerLaunches;
            _lanKeepProbeKept=LanKeepFile.List(kept).Length;_lanKeepProbeOffers=_lanKeep.Offers;_lanKeepProbePendingSeen=false;
            GD.Print($"LANKEEPTEST: question open on {_lanKeepProbePage} ({(_lanDone?"finished":"racing")}, recording {_lanKeep.Recording})");
            _lanKeepProbeStep=1;return;
        }
        switch(_lanKeepProbeStep)
        {
            case 1:
                // the leave question (window close) goes over KEEP and takes the keys; staying returns them to KEEP
                Assert(RequestLanWindowClose() && _lanLeaveAsk,"the window close did not ask to leave");
                _lanKeepProbeStep=11;break;
            case 11:
                Key(Godot.Key.Enter);
                Assert(!_lanLeaveAsk && _net.Active && _lanKeep.State==LanKeepPrompt.Phase.Choose,"the leave question's Enter reached KEEP or left the game");
                Unchanged("leave question");
                _lanKeepProbeStep=12;break;
            case 12:
                // through the engine's Input as a real keyboard (polled actions see it): Esc and R stay KEEP's
                Input.ParseInputEvent(new InputEventKey {Keycode=Godot.Key.R,PhysicalKeycode=Godot.Key.R,Pressed=true});
                Input.ParseInputEvent(new InputEventKey {Keycode=Godot.Key.R,PhysicalKeycode=Godot.Key.R,Pressed=false});
                _lanKeepProbeStep=13;break;
            case 13:
                Assert(!_lanLeaveAsk && _lanKeep.State==LanKeepPrompt.Phase.Choose,"a real R reached the race or closed KEEP");
                Key(Godot.Key.R);Unchanged("R");Assert(_lanKeep.State==LanKeepPrompt.Phase.Choose,"R closed the question");
                Key(Godot.Key.Enter,true);Assert(_lanKeep.State==LanKeepPrompt.Phase.Choose,"a held Enter answered");
                Key(Godot.Key.Down);Assert(!_lanKeep.KeepSelected,"Down did not select NOT NOW");
                Key(Godot.Key.Up);Assert(_lanKeep.KeepSelected,"Up did not select KEEP");
                Unchanged("arrows");
                PadButton(JoyButton.DpadDown,true,false);PadButton(JoyButton.DpadDown,false,false);
                _lanKeepProbeStep=2;break;
            case 2:
                Assert(!_lanKeep.KeepSelected && _lanKeep.OwnsInput,"the pad's d-pad did not move to NOT NOW");
                PadButton(JoyButton.DpadUp,true,false);PadButton(JoyButton.DpadUp,false,false);
                _lanKeepProbeStep=3;_lanKeepProbeSince=LanSession.Now;break;
            case 3:
                Assert(_lanKeep.KeepSelected,"the pad's d-pad did not move back to KEEP");
                if(!_lanKeep.CanKeep)
                {
                    Assert(_lanKeep.Recording==LanKeepPrompt.Source.Pending,"the recording failed: "+_lanKeep.Error);
                    if(!_lanKeepProbePendingSeen) {Key(Godot.Key.Enter);Assert(_lanKeep.State==LanKeepPrompt.Phase.Choose,"KEEP opened before the file was finished");_lanKeepProbePendingSeen=true;GD.Print("LANKEEPTEST: KEEP waits for the writer (PREPARING)");}
                    Assert(LanSession.Now-_lanKeepProbeSince<20,"the writer never finished");
                    return;
                }
                _lanKeepProbeSource=_lanKeep.SourcePath;_lanKeepProbeHash=LanKeepHash(_lanKeepProbeSource);
                if(_lanKeepProbe=="notnow")
                {
                    Key(Godot.Key.Escape);
                    Assert(_lanKeep.State==LanKeepPrompt.Phase.Closed,"Esc did not answer NOT NOW");
                    _lanKeepProbeStep=10;break;
                }
                Key(Godot.Key.Enter);Assert(_lanKeep.State==LanKeepPrompt.Phase.Name,"KEEP did not ask for a name");
                _lanKeepProbeStep=4;break;
            case 4:
                // the pad's letter wheel: up adds a first letter, B deletes it, B on the empty name goes back
                PadButton(JoyButton.DpadUp,true,false);PadButton(JoyButton.DpadUp,false,false);
                Assert(_lanKeep.Name.Length==1,"the pad's wheel did not add a letter ('"+_lanKeep.Name+"')");
                PadButton(JoyButton.B,true,false);PadButton(JoyButton.B,false,false);
                Assert(_lanKeep.Name=="","the pad's B did not delete the letter");
                PadButton(JoyButton.B,true,false);PadButton(JoyButton.B,false,false);
                _lanKeepProbeStep=41;break;
            case 41:
                Assert(_lanKeep.State==LanKeepPrompt.Phase.Choose,"the pad's B on an empty name did not go back to the question");
                _lanKeepProbeStep=43;break;
            case 43:      // (a frame later: the frame that went back takes no answer)
                Key(Godot.Key.Enter);Assert(_lanKeep.State==LanKeepPrompt.Phase.Name,"KEEP did not ask for a name again");
                _lanKeepProbeStep=42;break;
            case 42:
                foreach(var c in new[]{Godot.Key.X,Godot.Key.Y}) Key(c);
                Assert(_lanKeep.Name=="XY","typing went elsewhere: '"+_lanKeep.Name+"'");
                Key(Godot.Key.Escape);Assert(_lanKeep.State==LanKeepPrompt.Phase.Choose,"Esc in the name did not go back to the question");
                Unchanged("name Esc");
                _lanKeepProbeStep=5;break;
            case 5:
                Key(Godot.Key.Enter);Assert(_lanKeep.State==LanKeepPrompt.Phase.Name && _lanKeep.Name=="","KEEP did not reopen an empty name");
                _lanKeepProbeStep=6;break;
            case 6:
                foreach(var c in new[]{Godot.Key.P,Godot.Key.R,Godot.Key.O,Godot.Key.B,Godot.Key.E}) Key(c);
                Assert(_lanKeep.Name=="PROBE","the name is '"+_lanKeep.Name+"'");
                Key(Godot.Key.Enter);
                Assert(_lanKeep.State==LanKeepPrompt.Phase.Saving && !_lanKeep.OwnsInput && _lanKeepSave!=null,"Enter did not start the save");
                Unchanged("save");
                _lanKeepProbeStep=7;_lanKeepProbeSince=LanSession.Now;break;
            case 7:
                if(_lanKeep.State==LanKeepPrompt.Phase.Saving) {Assert(LanSession.Now-_lanKeepProbeSince<20,"the save never finished");return;}
                Assert(_lanKeep.State==LanKeepPrompt.Phase.Saved && _lanKeep.Result=="PROBE","the save failed: "+_lanKeep.Result);
                var files=LanKeepFile.List(kept);
                Assert(files.Length==_lanKeepProbeKept+1,$"expected one new kept file, found {files.Length-_lanKeepProbeKept}");
                string copy=files.First(f=>Path.GetFileName(f).StartsWith("PROBE-"));
                using(var r=new LanReplayFile.Reader(copy)) Assert(r.Entries.Count>0,"the kept copy is empty");
                Assert(LanKeepHash(copy)==_lanKeepProbeHash,"the kept copy differs from the recording");
                GD.Print("LANKEEPTEST: kept "+copy);
                _lanKeepProbeStep=10;break;
            case 10:
                Assert(File.Exists(_lanKeepProbeSource) && LanKeepHash(_lanKeepProbeSource)==_lanKeepProbeHash,"the automatic original changed or vanished");
                if(_lanKeepProbe=="notnow") Assert(LanKeepFile.List(kept).Length==_lanKeepProbeKept,"NOT NOW wrote a kept file");
                Assert(!_lanKeep.Offer(_lanKeepProbeRound,frame) && _lanKeep.Offers==_lanKeepProbeOffers,"the same round asked twice");
                Assert(!Directory.Exists(kept) || !Directory.GetFiles(kept,"*.partial").Any(),"a partial copy was left");
                Assert(LanReplayViewerLaunches==_lanKeepProbeLaunches,"a replay viewer was started");      // (the page may move on now: the results' scripted keys ran while saving)
                _lanKeepProbeDone=_lanKeepProbeRound;_lanKeepProbeStep=0;
                GD.Print($"LANKEEPTEST: PASS {_lanKeepProbe} round {_lanReplayRound} {(_net.IsHost?"host":"client")} offers {_lanKeep.Offers}");
                break;
        }
    }
}
