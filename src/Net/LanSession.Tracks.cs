using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CCCPRacing.Track;
using Godot;
namespace CCCPRacing.Net;

public partial class LanSession
{
    static LanSession _shared;
    public static LanSession Current => IsInstanceValid(_shared)?_shared:null;
    public static LanSession For(SceneTree tree)
    {
        if(Current!=null) return _shared;
        _shared=new LanSession {Name="LanSessionService",ProcessMode=ProcessModeEnum.Always};
        tree.Root.CallDeferred(Node.MethodName.AddChild,_shared);
        return _shared;
    }
    public readonly LanLeavePrompt LeavePrompt=new();
    public void LeaveLoading() {Close();LastCloseReason="YOU LEFT THE SESSION";Closed?.Invoke(LastCloseReason);}
    public bool RequestWindowClose()
    {
        if(!Active || !InLobby) return false;
        LeavePrompt.Ask(Engine.GetProcessFrames(),true);return true;
    }
    public List<string> TrackOptions {get;private set;}=new(){"ilyich"};
    public int RoundNumber {get;private set;}
    public string SelectedTrack => Reveal?.Track;
    public string LastLoadError {get;private set;}
    readonly Dictionary<long,(string track,string sig)> _loaded=new();
    double _loadStarted;
    int _loadedWire;
    public int LoadedCount => IsHost?_loaded.Count:_loadedWire;
    public event Action LoadRequested;
    public event Action<string> LoadFailed;

    public void ConfigureTracks(IEnumerable<string> tracks)
    {
        if(!IsHost || State!=Phase.Lobby) return;
        var list=tracks.Where(TrackIds.Known).Distinct().ToList();
        if(list.Count==0) list.Add("ilyich");
        TrackOptions=list;_dirty=true;
    }
    void BeginTrackLoad(LanReveal rv)
    {
        rv.Round=++RoundNumber;rv.StartHost=0;
        _roundRoster.Clear();foreach(var p in Players.Values)_roundRoster[p.Id]=p;
        rv.TrackVotes=Players.Values.OrderBy(p=>p.Slot).Select(p=>p.Voted?p.TrackVote:null).ToList(); // null: NOT VOTED, not the first track
        rv.Track=LanBallot.Track(rv.TrackVotes,TrackOptions,rv.Seed,out var tied);
        rv.TrackTied=tied;
        if(!CCCPRacing.Track.TrackIds.HasFuel(rv.Track)) rv.Fuel=false;
        Reveal=rv;State=Phase.Loading;_loaded.Clear();LastLoadError=null;_loadStarted=Now;
        _worldParts.Clear();_worldFrame=_worldApplied=0;_testPackets.Clear();
        Broadcast(Msg.LoadTrack,w=>WriteReveal(w,rv));_dirty=true;
        Log($"track ballot: round {rv.Round}, {rv.Track}, tied [{string.Join(",",tied)}], loading");
        LoadRequested?.Invoke();
    }
    public void WorldLoaded(string track,string signature)
    {
        if(!Active || State!=Phase.Loading || Reveal==null) return;
        if(IsHost) AcceptLoaded(MyId,Reveal.Round,track,signature);
        else Send(1,Msg.Loaded,w=>{w.Write(Reveal.Round);w.Write(track);w.Write(signature);});
    }
    void AcceptLoaded(long id,int round,string track,string signature)
    {
        if(State!=Phase.Loading || Reveal?.Round!=round || !Players.ContainsKey(id)) return;
        if(track!=SelectedTrack || signature.Length!=16) {AbortTrackLoad("TRACK DATA MISMATCH");return;}
        _loaded[id]=(track,signature);
        Log($"loaded: round {round} {Players[id].Name} {track} {_loaded.Count}/{Players.Count}");
        TryFinishLoading();
    }
    void TryFinishLoading()
    {
        if(!IsHost || State!=Phase.Loading || !_loaded.TryGetValue(1,out var host)) return;
        if(_loaded.Values.Any(v=>v.sig!=host.sig)) {AbortTrackLoad("TRACK DATA MISMATCH");return;}
        if(Players.Count<2) {AbortTrackLoad("NOT ENOUGH PLAYERS");return;}
        if(Players.Keys.Any(id=>!_loaded.ContainsKey(id))) return;
        Reveal.StartHost=Now+.25;State=Phase.Race;WinnerId=0;
        Broadcast(Msg.Reveal,w=>WriteReveal(w,Reveal));_dirty=true;
        Log($"all worlds ready: round {RoundNumber}, {SelectedTrack}, peers {string.Join(",",Players.Keys)}");
        RevealIn?.Invoke();
    }
    void AbortTrackLoad(string reason)
    {
        if(!IsHost || State!=Phase.Loading) return;
        Broadcast(Msg.LoadFailed,w=>{w.Write(RoundNumber);w.Write(reason);});
        LastLoadError=reason;State=Phase.Lobby;Reveal=null;_loaded.Clear();_dirty=true;
        Log("track load failed: "+reason);LoadFailed?.Invoke(reason);
    }
    void ReadTrackOptions(BinaryReader r)
    {
        int n=r.ReadByte();if(n<1 || n>4) throw new InvalidDataException("track options");
        var tracks=new List<string>();
        for(int i=0;i<n;i++) {string id=r.ReadString();if(!TrackIds.Known(id)||tracks.Contains(id)) throw new InvalidDataException("track option");tracks.Add(id);}
        TrackOptions=tracks;
        int fuels=r.ReadByte()&LanUnlocks.AllFuels;if(fuels==0) throw new InvalidDataException("fuel options");
        FuelMask=fuels;
    }
    void WriteTrackOptions(BinaryWriter w) {w.Write((byte)TrackOptions.Count);foreach(string id in TrackOptions)w.Write(id);w.Write((byte)FuelMask);}

    /// <summary>What this machine has opened; it goes to the host with the hello (and is the host's own entry).</summary>
    public LanUnlocks MyUnlocks=LanUnlocks.Minimum;
    /// <summary>The fuels the ballot offers (a bit per FuelGrade): those of the comrade with the most opened tracks and fuels.</summary>
    public int FuelMask {get;private set;}=LanUnlocks.AllFuels;
    /// <summary>The ballot's powers for the fuels on offer (the drum's candidates when nobody voted).</summary>
    public IReadOnlyList<int> OfferedFuels=>LanBallot.AllFuels.Where(FuelOffered).ToList();
    public bool FuelOffered(int pct) => Race.Training.GradeOf(pct) is Race.FuelGrade g && (FuelMask>>(int)g&1)!=0;
    /// <summary>Host, in the lobby: the ballot offers the tracks and fuels of the comrade with the most opened of both (a tie: the lowest slot); it follows who is in the lobby.</summary>
    void RefreshOptions()
    {
        if(!IsHost || State!=Phase.Lobby) return;
        var best=LanUnlocks.Best(Players.Values.OrderBy(p=>p.Slot).Select(p=>p.Unlocks));
        var list=best.Tracks.Where(TrackIds.Known).Distinct().Take(4).ToList();
        if(list.Count==0) list.Add("ilyich");
        TrackOptions=list;
        FuelMask=(best.Fuels&LanUnlocks.AllFuels)==0?1:best.Fuels&LanUnlocks.AllFuels;
        _dirty=true;
    }
    void ResetTrackSession() {RoundNumber=0;_loaded.Clear();_loadedWire=0;LastLoadError=null;LeavePrompt.Cancel();}
}
