using Godot;

namespace CCCPRacing.Net;

/// <summary>
/// The once-per-round KEEP question after a LAN race (docs/lan-replay-access.md). Pure state: the scene feeds it
/// the round's recording state and keys; it answers with an action. Choose and Name own the input; Saving, Saved
/// and Failed are passive notices that never take a key. Opening and held/repeated keys never confirm.
/// </summary>
public sealed class LanKeepPrompt
{
    public enum Phase { Closed, Choose, Name, Saving, Saved, Failed }
    public enum Source { None, Pending, Ready, Error }
    public enum Action { None, Save, Dismissed }

    public Phase State {get;private set;}
    public Source Recording {get;private set;}
    public string SourcePath {get;private set;}
    public string Error {get;private set;}
    public string Round {get;private set;}
    public bool KeepSelected {get;private set;}=true;
    public string Name {get;private set;}="";
    public string Result {get;private set;}      // the kept name (Saved) or the reason (Failed)
    public string Where {get;private set;}       // the kept folder, as shown
    public double NoticeLeft {get;private set;}
    public int Offers {get;private set;}         // how many times a question opened (tests: once per round)
    public const int NameMax=24;
    public const double NoticeSeconds=8;
    ulong _opened;
    string _offeredRound;

    /// <summary>Modal: keys and pad go here before the LAN pages.</summary>
    public bool OwnsInput=>State is Phase.Choose or Phase.Name;
    public bool Visible=>State!=Phase.Closed;
    public bool CanKeep=>Recording==Source.Ready;

    /// <summary>A new round's recording begins: forget the old one (a save in flight keeps its own identity).</summary>
    public void BeginRound(string round)
    {
        if(OwnsInput) GD.Print("LAN keep: question closed unanswered by the next round (nothing saved)");
        Round=round;Recording=Source.Pending;SourcePath=null;Error=null;
        if(State is not (Phase.Saving or Phase.Saved or Phase.Failed)) State=Phase.Closed;
    }
    public void SourceReady(string round,string path) {if(round!=Round) return;Recording=Source.Ready;SourcePath=path;}
    /// <summary>This machine does not record LAN races (RECORD LAN RACES? NO): the round is never offered, and no failure is shown.</summary>
    public void NotRecording(string round)
    {
        if(round!=Round) return;
        Recording=Source.None;SourcePath=null;Error=null;
        if(OwnsInput) State=Phase.Closed;
    }
    public void SourceFailed(string round,string error)
    {
        if(round!=Round) return;
        Recording=Source.Error;Error=error;
        if(OwnsInput) Notice(Phase.Failed,"THE RECORDING WAS NOT SAVED");
    }
    /// <summary>Opens the question once per round, and only for a recording that is (or is still becoming) saved.</summary>
    public bool Offer(string round,ulong frame)
    {
        if(round==null || round!=Round || _offeredRound==round || Recording is Source.None or Source.Error) return false;
        _offeredRound=round;Offers++;
        State=Phase.Choose;KeepSelected=true;Name="";_opened=frame;
        return true;
    }
    public bool Offered(string round)=>round!=null && _offeredRound==round;
    /// <summary>The LAN session ended or the scene left it: the question goes, nothing is saved.</summary>
    public void Close() {if(OwnsInput) GD.Print("LAN keep: question closed with the LAN race (nothing saved)");State=Phase.Closed;}

    public void Select(bool keep) {if(State==Phase.Choose) KeepSelected=keep;}
    public Action Key(Key key,bool echo,ulong frame,char typed=default)
    {
        if(!OwnsInput || echo || frame<=_opened) return Action.None;
        if(State==Phase.Name)
        {
            switch(key)
            {
                case Godot.Key.Enter or Godot.Key.KpEnter: State=Phase.Saving;Result=null;return Action.Save;
                case Godot.Key.Escape: State=Phase.Choose;_opened=frame;return Action.None;
                case Godot.Key.Backspace: if(Name.Length>0) Name=Name[..^1];return Action.None;
            }
            if(typed!=default) Type(typed);
            return Action.None;
        }
        if(key==Godot.Key.Escape) return Dismiss();
        if(key is Godot.Key.Up or Godot.Key.Left or Godot.Key.W or Godot.Key.A) KeepSelected=true;
        if(key is Godot.Key.Down or Godot.Key.Right or Godot.Key.S or Godot.Key.D) KeepSelected=false;
        if(key is not (Godot.Key.Enter or Godot.Key.KpEnter)) return Action.None;
        return KeepSelected?StartName(frame):Dismiss();
    }
    /// <summary>A mouse click or pad choice on a row: 0 KEEP, 1 NOT NOW.</summary>
    public Action Choose(int row,ulong frame)
    {
        if(State!=Phase.Choose || frame<=_opened) return Action.None;
        KeepSelected=row==0;
        return KeepSelected?StartName(frame):Dismiss();
    }
    Action StartName(ulong frame)
    {
        if(!CanKeep) return Action.None;      // KEEP waits for the finished file; the row says PREPARING
        State=Phase.Name;Name="";_opened=frame;return Action.None;
    }
    Action Dismiss() {State=Phase.Closed;GD.Print("LAN keep: NOT NOW (the recording stays only in the automatic LAN folder)");return Action.Dismissed;}
    /// <summary>The pad's letter wheel edits the name directly.</summary>
    public void SetName(string name) {if(State==Phase.Name) Name=name.Length>NameMax?name[..NameMax]:name;}
    void Type(char c)
    {
        c=char.ToUpperInvariant(c);
        if(Name.Length>=NameMax || !CCCPRacing.Race.Leaderboard.IsNameChar(c)) return;      // solo KEEP's characters: drawable by the pixel font
        if(c==' ' && (Name.Length==0 || Name[^1]==' ')) return;
        Name+=c;
    }
    public void Saved(string name,string where) {Result=name;Where=where;Notice(Phase.Saved,name);}
    public void Failed(string reason) {Notice(Phase.Failed,reason);}
    void Notice(Phase phase,string text) {State=phase;Result=text;NoticeLeft=NoticeSeconds;}
    public void Tick(double dt)
    {
        if(State is not (Phase.Saved or Phase.Failed)) return;
        NoticeLeft-=dt;if(NoticeLeft<=0) State=Phase.Closed;
    }
}
