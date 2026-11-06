using System;
using System.IO;
using System.Linq;
using CCCPRacing.Net;
using Godot;

namespace CCCPRacing.Tests;

/// <summary>--lan-keep-tests: the KEEP question after a LAN race and its verified copy (RaceScene.LanKeep.cs).</summary>
public partial class PhysicsTest
{
    static string LanKeepSha(string path) {using var s=File.OpenRead(path);return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(s));}

    static string LanKeepJournal(string dir)
    {
        string path=Path.Combine(dir,$"lan-test-{Guid.NewGuid():N}.lanreplay");
        var header=new LanReplayFile.Header("ilyich","0123456789abcdef","0.853",LanSession.Protocol,5,1,new[]{new LanReplayFile.Player(1,"HOST",0),new LanReplayFile.Player(2,"CLIENT",1)},100);
        var w=new LanReplayFile.Writer(path,header);
        w.Add(0,LanReplayFile.Kind.Go,Array.Empty<byte>());
        for(int i=0;i<50;i++) w.Add(i*.1f,LanReplayFile.Kind.World,new byte[]{(byte)i,1,2,3});
        w.Finish();
        return w.Completion.GetAwaiter().GetResult() ?? throw new IOException("fixture journal failed: "+w.Error);
    }

    void LanKeepTest()
    {
        int launches=Race.RaceScene.LanReplayViewerLaunches;
        // ---- the question: once per round, gated on the writer, keys/mouse/echo/opening frame
        var p=new LanKeepPrompt();
        Check(!p.Offer("r1",10),"LAN keep: no question before a round's recording begins");
        p.BeginRound("r1");
        Check(p.Recording==LanKeepPrompt.Source.Pending && !p.Visible,"LAN keep: a new round starts pending and closed");
        Check(p.Offer("r1",10) && p.OwnsInput && p.KeepSelected,"LAN keep: the question opens on KEEP");
        Check(!p.Offer("r1",11) && p.Offers==1,"LAN keep: a round never asks twice");
        Check(p.Key(Key.Escape,false,10)==LanKeepPrompt.Action.None && p.OwnsInput,"LAN keep: the opening frame's key does not answer");
        Check(p.Key(Key.Escape,true,12)==LanKeepPrompt.Action.None && p.OwnsInput,"LAN keep: a held (echo) key does not answer");
        p.Key(Key.Enter,false,12);
        Check(p.State==LanKeepPrompt.Phase.Choose && !p.CanKeep,"LAN keep: KEEP waits while the writer is still finishing");
        Check(p.Choose(0,12)==LanKeepPrompt.Action.None && p.State==LanKeepPrompt.Phase.Choose,"LAN keep: clicking KEEP waits for the writer too");
        p.SourceReady("old-round","/tmp/stale.lanreplay");
        Check(!p.CanKeep,"LAN keep: another round's finished file is not this round's");
        p.SourceReady("r1","/tmp/x.lanreplay");
        Check(p.CanKeep && p.SourcePath=="/tmp/x.lanreplay","LAN keep: the finished file makes KEEP available");
        p.Key(Key.Down,false,13);Check(!p.KeepSelected,"LAN keep: Down selects NOT NOW");
        p.Key(Key.W,false,13);Check(p.KeepSelected,"LAN keep: W (up) selects KEEP");
        p.Key(Key.Enter,false,14);
        Check(p.State==LanKeepPrompt.Phase.Name && p.Name=="","LAN keep: KEEP asks for a name");
        p.Key(Key.A,false,14,'a');Check(p.Name=="","LAN keep: typing in the name's opening frame is ignored");
        foreach(char c in "ab  c/1") p.Key(Key.None,false,15,c);
        Check(p.Name=="AB C1","LAN keep: the name takes letters, digits and single spaces, upper case ('"+p.Name+"')");
        p.Key(Key.None,false,15,'ä');
        Check(p.Name==(CCCPRacing.Race.Leaderboard.IsNameChar('Ä')?"AB C1Ä":"AB C1"),"LAN keep: the name takes solo KEEP's characters only (Ä drawable: "+CCCPRacing.UI.PixelFont.CanDraw('Ä')+")");
        if(p.Name.EndsWith('Ä')) p.Key(Key.Backspace,false,15);
        p.Key(Key.Backspace,false,15);Check(p.Name=="AB C","LAN keep: Backspace deletes");
        p.SetName(new string('Q',40));Check(p.Name.Length==LanKeepPrompt.NameMax,"LAN keep: the pad's wheel name is limited to 24");
        p.Key(Key.Escape,false,15);
        Check(p.State==LanKeepPrompt.Phase.Choose,"LAN keep: Esc in the name goes back to the question (nothing saved)");
        p.Key(Key.Enter,false,15);Check(p.State==LanKeepPrompt.Phase.Choose,"LAN keep: the frame that went back does not reopen the name");
        p.Key(Key.Enter,false,16);p.Key(Key.Enter,false,17);
        Check(p.State==LanKeepPrompt.Phase.Saving && !p.OwnsInput,"LAN keep: Enter in the name saves; the notice takes no keys");
        // a round change during the save keeps the save's own notice; the new round asks again
        p.BeginRound("r2");
        Check(p.State==LanKeepPrompt.Phase.Saving && p.Recording==LanKeepPrompt.Source.Pending,"LAN keep: a new round does not hide the previous save");
        p.Saved("PROBE","RECORDINGS/LAN/KEPT");
        Check(p.State==LanKeepPrompt.Phase.Saved && p.Result=="PROBE" && p.Where=="RECORDINGS/LAN/KEPT" && !p.OwnsInput,"LAN keep: the saved notice names the recording and its folder");
        p.Tick(LanKeepPrompt.NoticeSeconds+.1);Check(!p.Visible,"LAN keep: the notice goes by itself");
        Check(p.Offer("r2",30) && p.Offers==2,"LAN keep: the next round asks once again");
        Check(p.Choose(1,31)==LanKeepPrompt.Action.Dismissed && !p.Visible,"LAN keep: clicking NOT NOW closes and saves nothing");
        Check(!p.Offer("r2",32),"LAN keep: NOT NOW is the round's answer");
        p.BeginRound("r3");p.Offer("r3",40);
        p.BeginRound("r4");
        Check(!p.Visible && !p.Offered("r4"),"LAN keep: the next round's start closes an unanswered question");
        p.Offer("r4",50);p.SourceFailed("r4","LAN replay file limit reached");
        Check(p.State==LanKeepPrompt.Phase.Failed && !p.OwnsInput && p.Recording==LanKeepPrompt.Source.Error,"LAN keep: a writer error turns the question into a passive failure notice");
        p.BeginRound("r5");p.SourceFailed("r5","disabled");
        Check(!p.Offer("r5",60),"LAN keep: a failed recording asks nothing");
        p.BeginRound("r6");p.Offer("r6",70);p.Close();
        Check(!p.Visible,"LAN keep: leaving the LAN session closes the question");
        p.BeginRound("r7");p.Offer("r7",80);
        Check(p.Key(Key.Escape,false,81)==LanKeepPrompt.Action.Dismissed && !p.Visible,"LAN keep: Esc answers NOT NOW");

        // ---- the copy: verified, unique, the original untouched; failures leave nothing behind
        string root=Path.Combine(Path.GetTempPath(),"cccp-lan-keep-test-"+Guid.NewGuid().ToString("N"));
        try {
            Directory.CreateDirectory(root);
            string original=LanKeepJournal(root),kept=LanKeepFile.KeptDir(root),sha=LanKeepSha(original);
            Check(LanKeepFile.FileStem("ILYICH LAN 1-39-59")=="ILYICH-LAN-1-39-59" && LanKeepFile.FileStem("  / ")=="LAN-RACE" && LanKeepFile.FileStem("MÄKI ÅÖ")=="MAKI-AO","LAN keep: the file name is letters, digits and dashes");
            var now=new DateTime(2026,11,6,12,0,0);
            var a=LanKeepFile.Retain(original,kept,"MY RACE",now).GetAwaiter().GetResult();
            Check(a.Ok && File.Exists(a.Path) && Path.GetFileName(a.Path)=="MY-RACE-20261106-120000.lanreplay","LAN keep: KEEP writes the named copy ("+(a.Path??a.Message)+")");
            using(var r=new LanReplayFile.Reader(a.Path)) Check(r.Entries.Count==51 && r.Metadata.Track=="ilyich","LAN keep: the kept copy opens as a whole LAN journal");
            Check(LanKeepSha(a.Path)==sha && File.Exists(original) && LanKeepSha(original)==sha,"LAN keep: copy byte-identical, automatic original unchanged");
            var b=LanKeepFile.Retain(original,kept,"MY RACE",now).GetAwaiter().GetResult();
            Check(b.Ok && b.Path!=a.Path && Path.GetFileName(b.Path)=="MY-RACE-20261106-120000-2.lanreplay" && LanKeepSha(a.Path)==sha,"LAN keep: the same name never overwrites an earlier kept file");
            LanKeepFile.FailForTest="disk full (test)";
            var f=LanKeepFile.Retain(original,kept,"FAILS",now).GetAwaiter().GetResult();
            Check(!f.Ok && f.Message.Contains("disk full") && LanKeepFile.FailForTest==null,"LAN keep: a failed save reports its reason (once)");
            Check(LanKeepFile.List(kept).Length==2 && !Directory.GetFiles(kept,"*.partial").Any() && LanKeepSha(original)==sha,"LAN keep: a failed save leaves no partial and the original intact");
            string broken=Path.Combine(root,"broken.lanreplay");
            var bytes=File.ReadAllBytes(original);File.WriteAllBytes(broken,bytes[..(bytes.Length-20)]);
            var d=LanKeepFile.Retain(broken,kept,"BROKEN",now).GetAwaiter().GetResult();
            Check(!d.Ok && d.Message=="THE RECORDING IS DAMAGED" && LanKeepFile.List(kept).Length==2 && File.Exists(broken),"LAN keep: a damaged journal is not kept (and not deleted)");
            var m=LanKeepFile.Retain(Path.Combine(root,"missing.lanreplay"),kept,"GONE",now).GetAwaiter().GetResult();
            Check(!m.Ok && m.Message=="THE RECORDING FILE IS GONE","LAN keep: a missing file is reported");
            string blocked=Path.Combine(root,"blocked");File.WriteAllText(blocked,"a file where the folder should be");
            var u=LanKeepFile.Retain(original,Path.Combine(blocked,"kept"),"NOWHERE",now).GetAwaiter().GetResult();
            Check(!u.Ok && LanKeepSha(original)==sha,"LAN keep: an unwritable kept folder fails visibly, original intact ("+u.Message+")");
            Check(Directory.GetFiles(root,"*.lanreplay").Length==2,"LAN keep: no automatic recording was deleted or moved");
        } finally {LanKeepFile.FailForTest=null;try {Directory.Delete(root,true);} catch { }}
        Check(Race.RaceScene.LanReplayViewerLaunches==launches,"LAN keep: no replay viewer was started");
        LanRecordChoiceTest();
    }

    /// <summary>RECORD LAN RACES? (LanRecordChoice, RaceScene.LanRecord.cs): the saved answer and a non-recording round.</summary>
    void LanRecordChoiceTest()
    {
        string root=Path.Combine(Path.GetTempPath(),"cccp-lan-record-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path=Path.Combine(root,"settings.cfg");
        try {
            Check(LanRecordChoice.Load(path)==null,"LAN record: no settings file = never answered");
            var cfg=new ConfigFile();cfg.SetValue("input","rumble",false);cfg.SetValue("files","active",2);cfg.Save(path);
            Check(LanRecordChoice.Load(path)==null,"LAN record: a settings file without [lan] = never answered");
            Check(LanRecordChoice.Save(path,false) && LanRecordChoice.Load(path)==false,"LAN record: NO saves and reads back");
            Check(LanRecordChoice.Save(path,true) && LanRecordChoice.Load(path)==true,"LAN record: YES saves and reads back");
            cfg=new ConfigFile();cfg.Load(path);
            Check(cfg.GetValue("input","rumble",true).AsBool()==false && cfg.GetValue("files","active",1).AsInt32()==2,"LAN record: the answer keeps the other settings (rumble, active file)");
            cfg.SetValue("lan","record_races","maybe");cfg.Save(path);
            Check(LanRecordChoice.Load(path)==null,"LAN record: a value that is not yes/no counts as never answered");
            string broken=Path.Combine(root,"broken.cfg");File.WriteAllText(broken,"[input\nrumble=");
            string before=File.ReadAllText(broken);
            Check(!LanRecordChoice.Save(broken,true) && File.ReadAllText(broken)==before,"LAN record: an unreadable settings file is never overwritten");
            Check(!LanRecordChoice.Apply(broken,true) && LanRecordChoice.Load(broken)==null && LanRecordChoice.ForSession(broken)==true
                && LanRecordChoice.ForSession(Path.Combine(root,"other.cfg"))==null && File.ReadAllText(broken)==before,
                "LAN record: a failed preference save preserves YES across scene reloads, isolates other data roots and leaves the damaged file untouched");
        } finally {try {Directory.Delete(root,true);} catch { }}
        Check(LanRecordChoice.Override(new[]{"--lan-record=yes"})==true && LanRecordChoice.Override(new[]{"--lan-record=no"})==false
            && LanRecordChoice.Override(new[]{"--lan-host"})==null && LanRecordChoice.Override(new[]{"--lan-record=bogus"})==null,"LAN record: --lan-record=yes|no is parsed, anything else is no flag");
        Check(LanRecordChoice.Suggested && LanRecordChoice.Label(null)=="ASK" && LanRecordChoice.Label(false)=="NO","LAN record: the question suggests YES; the menu row says ASK until answered");
        // A round on a machine that does not record: never offered, no failure notice, and the next round still can be.
        var p=new LanKeepPrompt();
        p.BeginRound("off1");p.NotRecording("off1");
        Check(p.Recording==LanKeepPrompt.Source.None && !p.Offer("off1",1) && !p.Visible && p.Offers==0,"LAN record: a round that is not recorded never offers KEEP and shows nothing");
        p.NotRecording("stale");p.BeginRound("on2");p.NotRecording("off1");
        Check(p.Recording==LanKeepPrompt.Source.Pending && p.Offer("on2",2),"LAN record: another round's NotRecording does not touch this round");
    }
}
