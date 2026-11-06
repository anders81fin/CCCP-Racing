using System;
using CCCPRacing.Net;
using Godot;

namespace CCCPRacing.UI;

/// <summary>
/// KEEP after a LAN race (RaceScene.LanKeep.cs): the question (KEEP / NOT NOW), the recording's name in the
/// Party Office's KEEP field, and the passive notice of the save (saving, kept where, or why not).
/// </summary>
public partial class Hud
{
    public LanKeepPrompt LanKeep;
    public bool LanKeepSaving;
    public string LanKeepWhere="RECORDINGS/LAN/KEPT", LanKeepDefault="";

    Rect2 LanKeepRect()
    {
        float w=Math.Min(300,Screen.X-12),h=122;
        return new(Mathf.Floor((Screen.X-w)/2),Mathf.Floor((Screen.Y-h)/2),w,h);
    }

    /// <summary>The row under the mouse: 0 KEEP, 1 NOT NOW, -1 none.</summary>
    public int LanKeepRowAt(Vector2 p)
    {
        if(LanKeep is not {State:LanKeepPrompt.Phase.Choose}) return -1;
        var r=LanKeepRect();
        if(!new Rect2(r.Position.X+10,r.Position.Y+62,r.Size.X-20,28).HasPoint(p)) return -1;
        return p.Y<r.Position.Y+76?0:1;
    }

    void DrawLanKeep()
    {
        var k=LanKeep;
        if(k==null) return;
        switch(k.State)
        {
            case LanKeepPrompt.Phase.Choose: DrawLanKeepChoose(k);break;
            case LanKeepPrompt.Phase.Name:
                DrawRect(new Rect2(Vector2.Zero,Screen),new Color(0,0,0,.55f));
                KeepName=k.Name;KeepNameSub="LAN RACE: "+LanKeepDefault;
                DrawKeepName();break;
            case LanKeepPrompt.Phase.Saving: DrawLanKeepNotice("SAVING THE RECORDING...","DO NOT CLOSE THE GAME",Paper);break;
            case LanKeepPrompt.Phase.Saved: DrawLanKeepNotice("KEPT: "+k.Result,"IN "+(k.Where ?? LanKeepWhere),Gold);break;
            case LanKeepPrompt.Phase.Failed: DrawLanKeepNotice(k.Result ?? "NOT KEPT","NOTHING WAS KEPT",Red);break;
        }
    }

    void DrawLanKeepChoose(LanKeepPrompt k)
    {
        DrawRect(new Rect2(Vector2.Zero,Screen),new Color(0,0,0,.55f));
        var r=LanKeepRect();Panel(r,.98f);
        float cx=r.GetCenter().X,y=r.Position.Y,inner=r.Size.X-8;
        const string head="KEEP THE RECORDING?";
        DrawHeading(head,cx,y+7,_menuTextClock,PixelFont.Measure(head,2)<=inner?2:1);
        PixelFont.DrawCentered(this,ReplayFit("YOUR MULTIPLAYER LAN RECORDING",inner),cx,y+28,Paper);
        if(k.CanKeep)
        {
            PixelFont.DrawCentered(this,"A COPY IS SAVED IN",cx,y+40,Grey);
            PixelFont.DrawCentered(this,ReplayFit(LanKeepWhere,inner),cx,y+50,Paper);
        }
        else
        {
            PixelFont.DrawCentered(this,"PREPARING THE RECORDING...",cx,y+40,Grey);
            PixelFont.DrawCentered(this,ReplayFit("KEEP OPENS WHEN IT IS READY",inner),cx,y+50,Grey);
        }
        MenuItem(k.CanKeep?"KEEP":"KEEP (PREPARING)",k.KeepSelected,cx,y+66,!k.CanKeep);
        MenuItem("NOT NOW",!k.KeepSelected,cx,y+80);
        PixelFont.DrawCentered(this,P("UP/DOWN  ENTER CHOOSE","D-PAD  A CHOOSE"),cx,y+97,Grey);
        PixelFont.DrawCentered(this,P("ESC NOT NOW","B NOT NOW"),cx,y+108,Grey);
    }

    /// <summary>Passive: no key is taken; the race, results and lobby go on above and below it.</summary>
    void DrawLanKeepNotice(string line,string sub,Color color)
    {
        float w=Math.Min(300,Screen.X-12);
        var r=new Rect2(Mathf.Floor((Screen.X-w)/2),Screen.Y-46,w,38);
        Panel(r,.92f);
        float cx=r.GetCenter().X;
        PixelFont.DrawCentered(this,ReplayFit(line,w-8),cx,r.Position.Y+7,color);
        PixelFont.DrawCentered(this,ReplayFit(sub,w-8),cx,r.Position.Y+21,Grey);
    }
}
