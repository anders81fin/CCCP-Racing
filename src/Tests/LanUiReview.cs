using System;
using System.IO;
using CCCPRacing.UI;
using CCCPRacing.Net;
using Godot;
namespace CCCPRacing.Tests;
/// <summary>Render the production LAN cards at normal and narrow viewport sizes for visual review.</summary>
public partial class LanUiReview : Node
{
    public override async void _Ready()
    {
        string output=null;
        foreach(string arg in OS.GetCmdlineUserArgs()) if(arg.StartsWith("--out="))output=arg[6..];
        if(output==null){GetTree().Quit(2);return;}
        Directory.CreateDirectory(output);
        foreach(int width in new[]{480,208})
        {
            var vp=new SubViewport{Size=new Vector2I(width,270),RenderTargetUpdateMode=SubViewport.UpdateMode.Always};AddChild(vp);
            var h=new Hud{Mode=HudMode.Lan};vp.AddChild(h);
            h.Lan.IsHost=true;
            h.Lan.Laps=5;h.Lan.Power=100;h.Lan.Choice=4;
            h.Lan.Items.AddRange(new[]{"HOST GAME","JOIN GAME","CHANGE NAME","RECORD LAN RACES: ASK"});h.Lan.Text="COMRADE";
            h.Lan.Sub="HOST: COMRADE 1";h.Lan.Track="ILYICH";h.Lan.TrackOptions=new[]{"KOLKHOZ","SIBERIA"};h.Lan.FuelAvailable=true;
            h.Lan.Sel=3;h.Lan.BallotLeft=60;
            h.Lan.WinnerName="COMRADE 1";
            var count=new Hud.LanBallotShow{Power=100,Track="ILYICH",Times=new LanBallot.Timeline(6,false)};
            for(int i=0;i<6;i++)
            {
                string name="COMRADE "+(i+1);var col=LanRace.TagFor(i);
                h.Lan.Players.Add(new Hud.LanRow{Name=name,Color=col,Host=i==0,You=i==0,Ping=12,Voted=true});
                h.Lan.SessionScores.Add((i<2?1:i+1,name,col,i<2?2:0,i!=5,i==0));
                h.Lan.Results.Add((i+1,name,col,40+i,40+i,false));
                count.Ballots.Add((name,col,100));count.Grid.Add((name,col));count.RuleVotes.Add((true,false));count.ObstacleVotes.Add(false);count.Obstacles=false;
            }
            count.Counts[100]=6;h.Lan.Count=count;h.Lan.T=count.Times.Grid+3;
            // The ballot card states: every track (cursor on track), two tracks with the cursor on
            // damage and on fuel consumption, the ballot in, and Ilyich alone (no track choice, no fuel).
            var ballots=new (string name,string[] tracks,int sel,bool damage,bool fuel,bool voted)[]{
                ("4tracks-track",new[]{"KOLKHOZ","SIBERIA","ATOMGRAD-4","POLIGON 17"},3,true,false,false),
                ("4tracks-obstacles",new[]{"KOLKHOZ","SIBERIA","ATOMGRAD-4","POLIGON 17"},4,true,false,false),
                ("2tracks-damage",new[]{"KOLKHOZ","SIBERIA"},1,false,false,false),
                ("2tracks-fuelcons",new[]{"KOLKHOZ","SIBERIA"},2,true,true,false),
                ("3tracks-voted",new[]{"KOLKHOZ","SIBERIA","ATOMGRAD-4"},0,true,true,true),
                ("ilyich-only",new[]{"KOLKHOZ"},0,true,false,false)};
            h.Lan.Page=LanPage.Ballot;
            foreach(var b in ballots)
            {
                h.Lan.TrackOptions=b.tracks;h.Lan.TrackChoice=b.tracks.Length-1;h.Lan.Sel=b.sel;h.Lan.Damage=b.damage;h.Lan.Fuel=b.fuel;h.Lan.Voted=b.voted;
                h.Lan.Obstacles=b.sel!=4;h.Lan.FuelAvailable=b.tracks.Length>1;h.Lan.Choice=b.voted?3:2;
                h.QueueRedraw();
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                vp.GetTexture().GetImage().SavePng(Path.Combine(output,$"{width}-Ballot-{b.name}.png"));
            }
            // RECORD LAN RACES? (HudLanRecord.cs): the question on YES and on NO, the menu's row (keys and pad prompts), mouse rows.
            foreach(var (tag,sel,pad,items3) in new[]{("Record-yes",0,false,""),("Record-no",1,false,""),("Record-pad",0,true,""),
                ("Menu-record-ask",3,false,"RECORD LAN RACES: ASK"),("Menu-record-yes",3,false,"RECORD LAN RACES: YES"),("Menu-record-no-pad",3,true,"RECORD LAN RACES: NO")})
            {
                h.Lan.Page=items3==""?LanPage.Record:LanPage.Menu;h.Lan.Sel=sel;h.Pad=pad;if(items3!="") h.Lan.Items[3]=items3;
                h.QueueRedraw();
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                vp.GetTexture().GetImage().SavePng(Path.Combine(output,$"{width}-{tag}.png"));
                if(tag=="Record-yes")
                {
                    float top=Mathf.Floor((270-122)/2f),cx=width/2f;
                    int y0=h.LanRecordRowAt(new Vector2(cx,top+71)),y1=h.LanRecordRowAt(new Vector2(cx,top+85)),off=h.LanRecordRowAt(new Vector2(cx,top+20));
                    GD.Print($"lan-record {width}: mouse rows YES={y0} NO={y1} outside={off} -> {(y0==0 && y1==1 && off==-1?"PASS":"FAIL")}");
                }
            }
            h.Pad=false;h.Lan.Items[3]="RECORD LAN RACES: ASK";
            h.Lan.TrackOptions=new[]{"KOLKHOZ","SIBERIA"};h.Lan.TrackChoice=0;h.Lan.Sel=3;h.Lan.Damage=true;h.Lan.Fuel=false;h.Lan.Voted=false;h.Lan.FuelAvailable=true;h.Lan.Choice=4;
            foreach(var page in new[]{LanPage.Menu,LanPage.Ballot,LanPage.Lobby,LanPage.Reveal,LanPage.Results,LanPage.SessionResults,LanPage.SessionDecision,LanPage.SessionFinal})
            {
                h.Lan.Page=page;h.QueueRedraw();
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                vp.GetTexture().GetImage().SavePng(Path.Combine(output,$"{width}-{page}.png"));
            }
            // SHARE BOARDS (HudLanBoards.cs): the question (NO default, YES, pad) and the lobby's share line in each state.
            h.Lan.Page=LanPage.Lobby;
            for(int i=0;i<6;i++) h.Lan.Players[i].Boards=i switch{0=>1,1=>2,3=>1,_=>0};
            var boardStates=new (string tag,System.Action set,bool pad,LanPage page)[]{
                ("Boards-ask-no",()=>{h.LanBoards.Ask=false;},false,LanPage.Boards),
                ("Boards-ask-yes",()=>{h.LanBoards.Ask=true;},false,LanPage.Boards),
                ("Boards-ask-pad",()=>{h.LanBoards.Ask=false;},true,LanPage.Boards),
                ("Lobby-boards-no",()=>{h.LanBoards.On=false;h.LanBoards.Busy=false;h.LanBoards.Progress=null;h.LanBoards.Note=null;},false,LanPage.Lobby),
                ("Lobby-boards-progress",()=>{h.LanBoards.On=true;h.LanBoards.Busy=true;h.LanBoards.Progress="RECORDINGS 2/5  0.8/1.6 MB";},false,LanPage.Lobby),
                ("Lobby-boards-progress-pad",()=>{},true,LanPage.Lobby),
                ("Lobby-boards-synced",()=>{h.LanBoards.Busy=false;h.LanBoards.Progress=null;h.LanBoards.Note="BOARDS SYNCED: 5 RECORDINGS RECEIVED";h.LanBoards.NoteBad=false;},false,LanPage.Lobby),
                ("Lobby-boards-failed",()=>{h.LanBoards.Note="RECORDING NOT RECEIVED: TIMED OUT";h.LanBoards.NoteBad=true;},false,LanPage.Lobby),
                ("Lobby-boards-host-waits",()=>{h.LanBoards.On=false;h.LanBoards.Note=null;h.LanBoards.Waiting=true;},false,LanPage.Lobby),
                ("Lobby-boards-withdrawn",()=>{h.LanBoards.Waiting=false;h.LanBoards.Note="YOU STOPPED SHARING BOARDS (2 RECORDINGS NOT FETCHED)";h.LanBoards.NoteBad=false;},false,LanPage.Lobby)};
            foreach(var st in boardStates)
            {
                st.set();h.Pad=st.pad;h.Lan.Page=st.page;h.QueueRedraw();
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                vp.GetTexture().GetImage().SavePng(Path.Combine(output,$"{width}-{st.tag}.png"));
                if(st.tag=="Boards-ask-yes")
                {
                    float top=Mathf.Floor((270-140)/2f),cx=width/2f;
                    int y0=h.LanBoardsAskRowAt(new Vector2(cx,top+89)),y1=h.LanBoardsAskRowAt(new Vector2(cx,top+103)),off=h.LanBoardsAskRowAt(new Vector2(cx,top+20));
                    GD.Print($"lan-boards {width}: question mouse rows YES={y0} NO={y1} outside={off} -> {(y0==0 && y1==1 && off==-1?"PASS":"FAIL")}");
                }
                if(st.tag=="Lobby-boards-progress")
                {
                    int found0=-1,found1=-1;
                    for(int y=0;y<270;y++){int l=h.LanBoardsLobbyLineAt(new Vector2(width/2f,y));if(l==0&&found0<0)found0=y;if(l==1&&found1<0)found1=y;}
                    GD.Print($"lan-boards {width}: lobby mouse lines share@{found0} action@{found1} -> {(found0>0 && found1>found0?"PASS":"FAIL")}");
                }
            }
            h.Pad=false;h.LanBoards.Note=null;for(int i=0;i<6;i++) h.Lan.Players[i].Boards=0;
            h.Lan.Page=LanPage.Reveal;h.Lan.T=count.Times.Announce+2;
            foreach(var shot in new[]{(fuel:200,track:"SIBERIA"),(fuel:201,track:"POLIGON 17")})
            {
                count.Power=shot.fuel;count.Track=shot.track;h.QueueRedraw();
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                vp.GetTexture().GetImage().SavePng(Path.Combine(output,$"{width}-Chosen-{shot.fuel}.png"));
            }
            // Abstention: a mixed count (two ballots, four NOT VOTED) and the no-votes draw, each at
            // ballot rows, tally/drum, the chosen card and the grid.
            foreach(bool none in new[]{false,true})
            {
                var ab=new Hud.LanBallotShow{Power=none?201:50,Track="SIBERIA",TrackDraw=none,NoVotes=none,Damage=false,Fuel=true,Seed=7,Winner=none?3:0};
                for(int i=0;i<6;i++)
                {
                    string name=i==1?"COMRADE WITH A LONG NAME":"COMRADE "+(i+1);var col=LanRace.TagFor(i);
                    bool voted=!none && i<2;
                    ab.Ballots.Add((name,col,voted?50:LanBallot.Abstain));ab.Grid.Add((name,col));
                    ab.ObstacleVotes.Add(voted?(bool?)false:null);ab.Obstacles=false;
                    ab.RuleVotes.Add(voted?((bool?)(i==1),(bool?)true):((bool?)null,(bool?)null));
                }
                if(!none) ab.Counts[50]=2;
                ab.Tied.AddRange(none?LanBallot.AllFuels:new[]{50});
                ab.Times=new LanBallot.Timeline(6,ab.Tied.Count>1);
                h.Lan.Count=ab;h.Lan.Page=LanPage.Reveal;
                string tag=none?"novotes":"mixed";
                var times=new System.Collections.Generic.List<(string,float)>{("rows",ab.Times.Tally-.1f),("tally",ab.Times.Tally+.3f)};
                if(ab.Tied.Count>1) times.Add(("drum",ab.Times.Drum+LanBallot.DrumSpin+.4f));
                times.Add(("chosen",ab.Times.Announce+2));times.Add(("grid",ab.Times.Grid+3));
                foreach(var (stage,at) in times)
                {
                    h.Lan.T=at;h.QueueRedraw();
                    await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                    vp.GetTexture().GetImage().SavePng(Path.Combine(output,$"{width}-Count-{tag}-{stage}.png"));
                }
            }
            h.Lan.Page=LanPage.Ballot;h.Lan.Voted=false;h.Lan.BallotLeft=7.2f;
            for(int i=0;i<6;i++) h.Lan.Players[i].Voted=i<2;
            h.QueueRedraw();
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            vp.GetTexture().GetImage().SavePng(Path.Combine(output,$"{width}-Ballot-deadline-footer.png"));
            GD.Print($"lan-ui {width}: footer '{Hud.BallotFooter(7.2f,width<330)}' {PixelFont.Measure(Hud.BallotFooter(60,width<330))} px of {Math.Min(370,width-8)}");
            // KEEP after the race (HudLanKeep.cs): over the results, every state of the question and its notice.
            h.Lan.Page=LanPage.Results;h.LanKeepDefault="KOLKHOZ LAN 1-39-59";h.LanKeepWhere="RECORDINGS/LAN/KEPT";
            var keep=new LanKeepPrompt();ulong f=1;
            var states=new (string name,System.Action set,bool pad)[]{
                ("choose-preparing",()=>{keep.BeginRound("a");keep.Offer("a",f++);},false),
                ("choose-ready",()=>keep.SourceReady("a","x"),false),
                ("choose-ready-pad",()=>{},true),
                ("choose-notnow",()=>keep.Key(Key.Down,false,f++),false),
                ("name",()=>{keep.Key(Key.Up,false,f++);keep.Key(Key.Enter,false,f++);foreach(char c in "MY RACE") keep.Key(Key.None,false,f++,c);},false),
                ("name-pad",()=>{},true),
                ("saving",()=>keep.Key(Key.Enter,false,f++),false),
                ("saved",()=>keep.Saved("MY RACE","RECORDINGS/LAN/KEPT"),false),
                ("failed",()=>keep.Failed("NOT KEPT: NO SPACE LEFT ON DEVICE"),false)};
            foreach(var st in states)
            {
                st.set();h.LanKeep=keep;h.Pad=st.pad;h.QueueRedraw();
                await ToSignal(GetTree().CreateTimer(1.2),SceneTreeTimer.SignalName.Timeout);      // the heading's typewriter finishes, as a player sees it
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                vp.GetTexture().GetImage().SavePng(Path.Combine(output,$"{width}-Keep-{st.name}.png"));
                if(st.name=="choose-ready")
                {
                    float top=Mathf.Floor((270-122)/2f),cx=width/2f;
                    int a0=h.LanKeepRowAt(new Vector2(cx,top+69)),a1=h.LanKeepRowAt(new Vector2(cx,top+83)),off=h.LanKeepRowAt(new Vector2(cx,top+20));
                    GD.Print($"lan-keep {width}: mouse rows KEEP={a0} NOT NOW={a1} outside={off} -> {(a0==0 && a1==1 && off==-1?"PASS":"FAIL")}");
                }
            }
            h.Lan.Page=LanPage.None;h.Mode=HudMode.Race;keep.BeginRound("b");keep.Offer("b",f++);keep.SourceReady("b","y");h.Pad=false;h.QueueRedraw();
            await ToSignal(GetTree().CreateTimer(1.2),SceneTreeTimer.SignalName.Timeout);
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            vp.GetTexture().GetImage().SavePng(Path.Combine(output,$"{width}-Keep-race-finished.png"));
            h.Mode=HudMode.Lan;h.LanKeep=null;
            h.Lan.Page=LanPage.SessionFinal;
            h.LanLeaveAsk=true;h.LanLeaveHost=true;h.QueueRedraw();
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            vp.GetTexture().GetImage().SavePng(Path.Combine(output,$"{width}-final-leave.png"));
            vp.QueueFree();await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        }
        GetTree().Quit();
    }
}
