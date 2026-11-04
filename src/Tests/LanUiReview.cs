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
            h.Lan.Laps=5;h.Lan.Power=100;h.Lan.Choice=2;
            h.Lan.Sub="HOST: COMRADE 1";h.Lan.Track="ILYICH";h.Lan.TrackOptions=new[]{"ILYICH","SIBERIA"};h.Lan.FuelAvailable=true;
            h.Lan.WinnerName="COMRADE 1";
            var count=new Hud.LanBallotShow{Power=100,Track="ILYICH",Times=new LanBallot.Timeline(6,false)};
            for(int i=0;i<6;i++)
            {
                string name="COMRADE "+(i+1);var col=LanRace.TagFor(i);
                h.Lan.Players.Add(new Hud.LanRow{Name=name,Color=col,Host=i==0,You=i==0,Ping=12,Voted=true});
                h.Lan.SessionScores.Add((i<2?1:i+1,name,col,i<2?2:0,i!=5,i==0));
                h.Lan.Results.Add((i+1,name,col,40+i,40+i,false));
                count.Ballots.Add((name,col,100));count.Grid.Add((name,col));count.RuleVotes.Add((true,false));
            }
            count.Counts[100]=6;h.Lan.Count=count;h.Lan.T=count.Times.Grid+3;
            foreach(var page in new[]{LanPage.Lobby,LanPage.Ballot,LanPage.Reveal,LanPage.Results,LanPage.SessionResults,LanPage.SessionDecision,LanPage.SessionFinal})
            {
                h.Lan.Page=page;h.QueueRedraw();
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                vp.GetTexture().GetImage().SavePng(Path.Combine(output,$"{width}-{page}.png"));
            }
            h.LanLeaveAsk=true;h.LanLeaveHost=true;h.QueueRedraw();
            await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            vp.GetTexture().GetImage().SavePng(Path.Combine(output,$"{width}-final-leave.png"));
            vp.QueueFree();await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        }
        GetTree().Quit();
    }
}
