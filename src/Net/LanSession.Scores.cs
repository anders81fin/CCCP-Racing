using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CCCPRacing.Net;

/// <summary>Host-owned session totals; transport IDs and display names are not identities.</summary>
public sealed class LanScore
{
    public string Token, Name;
    public int Slot, Wins;
    public bool Connected;
}

public partial class LanSession
{
    readonly string _identity = Guid.NewGuid().ToString("N");
    readonly Dictionary<string,LanScore> _scores = new();
    readonly Dictionary<long,string> _identities = new();
    readonly Dictionary<long,LanPlayer> _roundRoster = new();
    readonly HashSet<int> _awardedRounds = new();
    int _lastResultsRound;
    public bool OwnScore(LanScore score)=>score.Token==_identity;
    public IEnumerable<LanScore> Scores => _scores.Values.OrderByDescending(s=>s.Wins).ThenBy(s=>s.Name,StringComparer.Ordinal).ThenBy(s=>s.Token,StringComparer.Ordinal);

    void RegisterScore(long peer,string token,LanPlayer p)
    {
        _identities[peer]=token;
        if(!_scores.TryGetValue(token,out var s)) _scores[token]=s=new LanScore{Token=token};
        s.Name=p.Name;s.Slot=p.Slot;s.Connected=true;
    }
    void DisconnectScore(long peer)
    {
        if(_identities.Remove(peer,out var token) && _scores.TryGetValue(token,out var s)) s.Connected=false;
        if(_roundRoster.TryGetValue(peer,out var p) && !p.Finished) p.Dnf=true;
    }
    void AwardSessionWin()
    {
        if(!_awardedRounds.Add(RoundNumber)) return;
        if(_roundRoster.TryGetValue(WinnerId,out var p) && p.Finished && p.FinishTime>=0 && p.SessionToken!=null && _scores.TryGetValue(p.SessionToken,out var score)) score.Wins++;
    }
    void WriteScores(BinaryWriter w)
    {
        w.Write(_scores.Count);
        foreach(var s in Scores){w.Write(s.Token);w.Write(s.Name);w.Write((byte)s.Slot);w.Write(s.Wins);w.Write(s.Connected);}
    }
    void ReadScores(BinaryReader r)
    {
        int n=r.ReadInt32();if(n<0 || n>4096) throw new InvalidDataException("session score count");
        var fresh=new Dictionary<string,LanScore>();
        for(int i=0;i<n;i++){var s=new LanScore{Token=r.ReadString(),Name=r.ReadString(),Slot=r.ReadByte(),Wins=r.ReadInt32(),Connected=r.ReadBoolean()};if(s.Wins<0)throw new InvalidDataException("negative wins");fresh.Add(s.Token,s);}
        _scores.Clear();foreach(var pair in fresh)_scores.Add(pair.Key,pair.Value);
    }
    public event Action SessionEnded;
    public void EndSession()
    {
        if(!IsHost || State!=Phase.Results) return;
        State=Phase.SessionFinished;
        Broadcast(Msg.SessionEnd,w=>{w.Write(RoundNumber);WriteScores(w);});
        Log("session finished: "+string.Join(", ",Scores.Select(s=>$"{s.Name} {s.Wins}")));
        _dirty=true;SessionEnded?.Invoke();
    }
    void ResetScores(){_scores.Clear();_identities.Clear();_roundRoster.Clear();_awardedRounds.Clear();_lastResultsRound=0;}
}
