using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace CCCPRacing.Net;

/// <summary>One comrade in a LAN game, as the lobby shows them.</summary>
public class LanPlayer
{
    public long Id;
    public string Name = "";
    internal string SessionToken;
    public int Slot;
    public int PingMs = -1;
    public bool Voted, Finished, Dnf;
    public float FinishTime = -1, BestLap = -1;
    /// <summary>He drove the race with a game pad (sent with his finish).</summary>
    public bool Pad;
    /// <summary>SHARE BOARDS: this comrade said YES in this session (the host's lobby says so); Busy: board sync work in hand.</summary>
    public bool BoardShare, BoardBusy;
    // Host only: the secret ballot and the clock sync samples (rtt, the peer's clock minus the host's).
    internal int Vote;
    internal bool DamageVote = true, FuelVote, ObstaclesVote = true;
    internal string TrackVote;
    internal double Offset;
    internal readonly List<(double rtt, double offset)> Sync = new();
}

/// <summary>The count of the ballot as the host drew it: the same on every screen.</summary>
public class LanReveal
{
    public double StartHost, GoHost;
    public int Laps, Seed, WinnerIndex;
    public int Round;
    public string Track="ilyich";
    public List<string> TrackVotes=new(), TrackTied=new();
    public List<int> Tied = new();
    public List<(long id, string name, int slot, int pct)> Ballots = new();
    public List<long> Grid = new();
    public bool Damage = true, Fuel, Obstacles = true;
    public List<bool?> ObstacleVotes = new();
    /// <summary>One per ballot row; null: NOT VOTED (the ballot's pct is LanBallot.Abstain).</summary>
    public List<(bool? damage, bool? fuel)> RuleVotes = new();
    /// <summary>No comrade submitted a ballot: track, fuel and all rules were drawn by lot.</summary>
    public bool NoVotes => Ballots.All(b => !LanBallot.Voted(b.pct));
    public int Power => Tied[Math.Clamp(WinnerIndex, 0, Tied.Count - 1)];
    public bool Drum => Tied.Count > 1;
    public LanBallot.Timeline Timeline => new(Ballots.Count, Drum);
}

public class LanResult
{
    public long Id;
    public string Name;
    public int Slot;
    public float Time = -1, Best = -1;   // Time < 0: DNF
    public bool Pad;                      // driven with a game pad
}

/// <summary>
/// A LAN game over Godot's ENet multiplayer (the host is the server and a player too, UDP 24105,
/// six comrades at most). Raw packets (SceneMultiplayer.SendBytes) with our own tiny protocol:
/// lobby, clock sync, the secret ballot and the results are reliable and go through the host;
/// the host sends authoritative car snapshots. Inputs, world poses and reliable mesh definitions
/// use separate channels; clients cannot relay positions, finishes or scenery breaks.
/// </summary>
public partial class LanSession : Node
{
    public const int Port = 24105, MaxPlayers = 6;
    public const float BallotTime = 60f, DnfAfter = 60f;
    const int SnapChannel = 1, Channels = 6;      // 0 lobby, 1 snapshots, 2 inputs, 3 world, 4 meshes, 5 board sync (LanSession.Boards.cs)
    public const int Protocol = 15; // 15: SHARE BOARDS in the lobby (consent flags, offers, chunked recordings on channel 5); 14: shared moving-obstacles vote; 13: NOT VOTED ballots (pct 0, rule byte 2) are real abstentions; 12: four fuel-specific engines/bodies
    const float PingEvery = 0.25f, PingEveryRacing = 1f, LobbyEvery = 0.5f, HelloTimeout = 5f;

    public enum Phase : byte { Lobby, Ballot, Race, Results, Loading, SessionFinished }
    enum Msg : byte { Hello = 1, Reject, Lobby, Ping, Pong, Ballot, Vote, Reveal, Snap, Finish, Winner, Results, Left, Broke, Input, World, WorldMesh, Rescue, Effect, Countdown, TrackRequired, LoadTrack, Loaded, LoadFailed, SessionEnd,
        BoardConsent, BoardOffer, BoardStatus, BoardWant, BoardChunk, BoardAck, BoardAbort }

    /// <summary>This machine's clock, seconds (monotonic).</summary>
    public static double Now => Time.GetTicksUsec() / 1e6;
    public static string GameVersion => (string)ProjectSettings.GetSetting("application/config/version", "0");

    ENetMultiplayerPeer _peer;
    SceneMultiplayer _mp;
    long _myId;
    double _offset;          // client: this clock minus the host's
    float _pingAcc, _lobbyAcc;
    double _dnfAt = -1;
    readonly Dictionary<long, double> _pending = new();         // host: connected, no hello yet
    readonly List<(long id, double at)> _kick = new();          // host: rejected, disconnect after the message went out
    bool _dirty;
    readonly Random _testRandom=new(24105);
    readonly List<(double at, byte[] data, int to, MultiplayerPeer.TransferModeEnum mode,int channel)> _testPackets=new();
    readonly float _testDrop=float.TryParse(OS.GetEnvironment("LAN_TEST_DROP"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var drop)?Math.Clamp(drop,0,.5f):0;
    readonly float _testDelay=float.TryParse(OS.GetEnvironment("LAN_TEST_DELAY_MS"),out var delay)?Math.Clamp(delay,0,500)/1000f:0;
    uint _worldFrame, _worldApplied;
    readonly Dictionary<uint, byte[][]> _worldParts=new();

    public bool Active => _peer != null || TestWire != null;
    public bool IsHost { get; private set; }
    /// <summary>Client: the host has let us in (the first lobby message came).</summary>
    public bool InLobby { get; private set; }
    public long MyId => IsHost ? 1 : _myId;
    public Phase State { get; private set; }
    public int Laps = 5;
    public string MyName = "", Signature = "", Version = GameVersion, TrackId = "";
    public string LastCloseReason {get;private set;}
    /// <summary>Host clock now (the client's estimate from the host's sync).</summary>
    public double HostNow => Now - (IsHost ? 0 : _offset);
    public double BallotEndsHost;
    public readonly SortedDictionary<long, LanPlayer> Players = new();
    public LanReveal Reveal { get; private set; }
    public readonly List<LanResult> Results = new();
    public long WinnerId { get; private set; }
    /// <summary>Host clock when the unfinished become DNF (after the winner), else negative.</summary>
    public double DnfAtHost { get; private set; } = -1;
    public LanPlayer Me => Players.GetValueOrDefault(MyId);
    public static bool Debug = OS.HasEnvironment("LANDBG");
    public int SnapsSent, SnapBytes, SnapsIn, WorldFramesIn;

    /// <summary>The game is over for us: the reason (null: we left ourselves).</summary>
    public event Action<string> Closed;
    public event Action WindowCloseRequested;
    public event Action<string> TrackRequired;
    public event Action<long, DriverCommand> InputIn;
    public event Action<byte[]> WorldIn, MeshIn;
    public event Action<long> RescueIn;
    public event Action<Vector3,int> EffectIn;
    public event Action Changed, BallotOpened, RevealIn, ResultsIn;
    public event Action<long, string> PlayerLeft;
    public event Action<long, float> WinnerIn;
    public event Action<long, Snapshot> SnapIn;
    // Local replay observes existing authoritative output; no second world capture or protocol change.
    public event Action<long,Snapshot> ReplaySnapOut;
    public event Action<byte[],bool> ReplayWorldOut;
    public event Action<Vector3,int> ReplayEffectOut;
    public event Action<int,float,Vector3,Vector3> ReplayBreakOut;
    public event Action<int, float, Vector3, Vector3> BrokeIn;

    public override void _Ready()
    {
        _mp = (SceneMultiplayer)GetTree().GetMultiplayer();
        _mp.ServerRelay = false;
        _mp.PeerConnected += OnPeerConnected;
        _mp.PeerDisconnected += OnPeerDisconnected;
        _mp.ConnectedToServer += OnConnected;
        _mp.ConnectionFailed += OnConnectionFailed;
        _mp.ServerDisconnected += OnServerDisconnected;
        _mp.PeerPacket += OnPacket;
    }

    void Log(string s)
    {
        if (Debug) GD.Print($"lan[{MyName}] {HostNow:F3} {s}");
    }

    // ------------------------------------------------------------ start and end

    public Error Host(string name)
    {
        Close();
        var p = new ENetMultiplayerPeer();
        Error err = p.CreateServer(Port, MaxPlayers + 2, Channels);
        if (err != Error.Ok) return err;
        _peer = p;
        _mp.MultiplayerPeer = p;
        IsHost = InLobby = true;
        MyName = name;
        State = Phase.Lobby;
        Players[1] = new LanPlayer { Id = 1, Name = name, Slot = 0, PingMs = 0 };
        Players[1].SessionToken=_identity; RegisterScore(1,_identity,Players[1]);
        Log($"hosting on UDP {Port} as {name}, version {Version}");
        Changed?.Invoke();
        return Error.Ok;
    }

    public Error Join(string ip, string name)
    {
        Close();
        var p = new ENetMultiplayerPeer();
        Error err = p.CreateClient(ip, Port, Channels);
        if (err != Error.Ok) return err;
        _peer = p;
        _mp.MultiplayerPeer = p;
        IsHost = InLobby = false;
        MyName = name;
        Log($"joining {ip}:{Port} as {name}");
        return Error.Ok;
    }

    /// <summary>Leave (a host leaving ends the game for everyone). No event.</summary>
    public void Leave()
    {
        Log("leaving");
        Close();
    }

    void End(string why)
    {
        if (!Active) return;
        Log($"closed: {why}");
        Close();
        LastCloseReason=why;
        Closed?.Invoke(why);
    }

    void Close()
    {
        LastCloseReason=null;
        if (_peer != null)
        {
            _peer.Close();
            _mp.MultiplayerPeer = new OfflineMultiplayerPeer();
        }
        _peer = null;
        ResetTrackSession();
        ResetScores();
        ResetBoards();
        IsHost = InLobby = false;
        Players.Clear();
        _testPackets.Clear();_worldParts.Clear();_worldFrame=_worldApplied=0;
        _pending.Clear();
        _kick.Clear();
        Results.Clear();
        Reveal = null;
        WinnerId = 0;
        DnfAtHost = _dnfAt = -1;
        State = Phase.Lobby;
        _offset = 0;
    }

    void OnConnectionFailed() => End("NO ANSWER FROM THE HOST");
    void OnServerDisconnected() => End(InLobby ? "THE HOST HAS LEFT THE KOLKHOZ" : "THE HOST CLOSED THE DOOR");
    public override void _ExitTree()
    {
        Close();
        if(_mp!=null && IsInstanceValid(_mp))
        {
            _mp.PeerConnected-=OnPeerConnected;_mp.PeerDisconnected-=OnPeerDisconnected;
            _mp.ConnectedToServer-=OnConnected;_mp.ConnectionFailed-=OnConnectionFailed;
            _mp.ServerDisconnected-=OnServerDisconnected;_mp.PeerPacket-=OnPacket;
        }
        if(_shared==this) _shared=null;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            if(RequestWindowClose()) WindowCloseRequested?.Invoke();
            else if(WindowCloseRequested!=null) WindowCloseRequested();
            else Close();
        }
    }

    // ------------------------------------------------------------ connection events

    void OnConnected()
    {
        _peer.GetPeer(1)?.SetTimeout(32, 2000, 4000);     // a vanished host is noticed in a few seconds
        _myId = _mp.GetUniqueId();
        Send(1, Msg.Hello, w => { w.Write(Version + "/lan" + Protocol); w.Write(Signature); w.Write(MyName); w.Write(TrackId); w.Write(_identity); });
        Log($"connected as peer {_myId}, hello sent");
    }

    void OnPeerConnected(long id)
    {
        if (!IsHost) return;
        _peer.GetPeer((int)id)?.SetTimeout(32, 2000, 4000);
        _pending[id] = Now;
    }

    void OnPeerDisconnected(long id)
    {
        if (!IsHost) return;
        _pending.Remove(id);
        _kick.RemoveAll(k => k.id == id);
        _testPackets.RemoveAll(p => p.to == id);
        _loaded.Remove(id);
        if(State==Phase.Loading) Reveal?.Grid.Remove(id);
        if (!Players.Remove(id, out var p)) return;
        DisconnectScore(id);
        BoardPeerGone(id);
        Log($"{p.Name} left (peer {id})");
        Broadcast(Msg.Left, w => { w.Write(id); w.Write(p.Name); });
        PlayerLeft?.Invoke(id, p.Name);
        if (State == Phase.Ballot && Players.Values.All(x => x.Voted)) Tally();
        if(State==Phase.Loading) TryFinishLoading();
        CheckRaceOver();
        _dirty = true;
    }

    // ------------------------------------------------------------ sending

    void Send(long to, Msg type, Action<BinaryWriter> body, MultiplayerPeer.TransferModeEnum mode = MultiplayerPeer.TransferModeEnum.Reliable, int channel = 0)
    {
        if (_peer == null && TestWire == null) return;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)type);
        body?.Invoke(w);
        w.Flush();
        var bytes=ms.ToArray();
        if(TestWire!=null) {TestWire(to,bytes,channel);return;}      // transport-free fixtures (src/Tests/LanSession.BoardFixture.cs)
        // A comrade whose disconnect is still to be reported (two leaving in the same poll) has no channels left: nothing to send to.
        if(IsHost && to>1 && _peer.GetPeer((int)to) is {} enet && enet.GetState()!=ENetPacketPeer.PeerState.Connected) return;
        if(mode!=MultiplayerPeer.TransferModeEnum.Reliable && (_testDrop>0 || _testDelay>0)) {
            if(_testRandom.NextDouble()<_testDrop) return;
            _testPackets.Add((Now+_testDelay,bytes,(int)to,mode,channel));
        } else _mp.SendBytes(bytes,(int)to,mode,channel);
    }

    /// <summary>Host: to every comrade in the game (not the pending ones).</summary>
    void Broadcast(Msg type, Action<BinaryWriter> body)
    {
        foreach (var id in Players.Keys)
            if (id != 1) Send(id, type, body);
    }

    // ------------------------------------------------------------ per frame

    public override void _Process(double delta)
    {
        BoardFrame();      // LanSession.Boards.cs (also finishes a closed session's worker jobs)
        if(!Active) return;
        for(int k=0;k<_testPackets.Count;) {
            var p=_testPackets[k];if(p.at>Now) {k++;continue;}
            _mp.SendBytes(p.data,p.to,p.mode,p.channel);_testPackets.RemoveAt(k);
        }
        if(!IsHost) return;
        float dt = (float)delta;
        double now = Now;
        foreach (var (id, since) in _pending.ToList())
            if (now - since > HelloTimeout) { _pending.Remove(id); _peer.DisconnectPeer((int)id); }
        for (int i = _kick.Count - 1; i >= 0; i--)
            if (now >= _kick[i].at) { long id=_kick[i].id; _kick.RemoveAt(i); _peer.DisconnectPeer((int)id); }
        _pingAcc += dt;
        if (_pingAcc >= (State is Phase.Race or Phase.Results ? PingEveryRacing : PingEvery))
        {
            _pingAcc = 0;
            foreach (var id in Players.Keys)
                if (id != 1) Send(id, Msg.Ping, w => w.Write(Now));
        }
        BallotDeadline(now);
        if(State==Phase.Loading && now-_loadStarted>90) AbortTrackLoad("TRACK LOAD TIMEOUT");
        if (State == Phase.Race && _dnfAt > 0 && now >= _dnfAt)
        {
            foreach (var p in Players.Values)
                if (!p.Finished) p.Dnf = true;
            Log("60 s after the winner: the rest are DNF");
            CheckRaceOver();
        }
        _lobbyAcc += dt;
        if (_dirty || _lobbyAcc >= LobbyEvery) SendLobby();
    }

    /// <summary>Host: the lobby to each comrade, with that comrade's own id and clock offset.</summary>
    void SendLobby()
    {
        _dirty = false;
        _lobbyAcc = 0;
        var list = Players.Values.ToList();
        foreach (var to in list)
        {
            if (to.Id == 1) continue;
            Send(to.Id, Msg.Lobby, w =>
            {
                w.Write((byte)State);
                w.Write((byte)Laps);
                w.Write(to.Id);
                w.Write(to.Offset);
                w.Write(BallotEndsHost);
                w.Write(DnfAtHost);
                w.Write((byte)list.Count);
                foreach (var p in list)
                {
                    w.Write(p.Id);
                    w.Write(p.Name);
                    w.Write((byte)p.Slot);
                    w.Write((short)Math.Clamp(p.PingMs, -1, 9999));
                    w.Write((byte)((p.Voted ? 1 : 0) | (p.Finished ? 2 : 0) | (p.Dnf ? 4 : 0) | (p.BoardShare ? 8 : 0) | (p.BoardBusy ? 16 : 0)));
                }
                WriteTrackOptions(w);
                w.Write((byte)LoadedCount);
                WriteScores(w);
            });
        }
        Changed?.Invoke();
    }

    // ------------------------------------------------------------ receiving

    internal void OnPacket(long from, byte[] data)
    {
        if (data.Length == 0) return;
        try
        {
            using var r = new BinaryReader(new MemoryStream(data));
            var type = (Msg)r.ReadByte();
            if (IsHost) HostPacket(from, type, r);
            else ClientPacket(from, type, r);
        }
        catch (Exception e) when (e is EndOfStreamException or InvalidDataException or ArgumentException)
        {
            GD.PushWarning($"lan: bad packet from {from}: {e.Message}");
        }
    }

    void HostPacket(long from, Msg type, BinaryReader r)
    {
        if (type == Msg.Hello)
        {
            string version=r.ReadString(), sig=r.ReadString(), name=r.ReadString();
            // Older protocols have no track field; still send their explicit version rejection.
            string track=r.BaseStream.Position<r.BaseStream.Length?r.ReadString():"";
            string token=r.BaseStream.Position<r.BaseStream.Length?r.ReadString():"";
            Hello(from, version, sig, name, track, token);
            return;
        }
        if (!Players.TryGetValue(from, out var p)) return;
        if (type >= Msg.BoardConsent) { HostBoardPacket(from, type, r); return; }      // LanSession.Boards.cs
        switch (type)
        {
            case Msg.Pong:
            {
                double sent = r.ReadDouble(), theirs = r.ReadDouble(), rtt = Now - sent;
                p.Sync.Add((rtt, theirs - (sent + rtt / 2)));
                if (p.Sync.Count > 24) p.Sync.RemoveAt(0);
                var best = p.Sync.MinBy(s => s.rtt);
                p.Offset = best.offset;
                p.PingMs = (int)Math.Round(p.Sync.Skip(Math.Max(0, p.Sync.Count - 4)).Average(s => s.rtt) * 1000);
                break;
            }
            case Msg.Vote:
                CastVote(p, r.ReadUInt16(), r.ReadBoolean(), r.ReadBoolean(), r.ReadString(), r.ReadBoolean());
                break;
            case Msg.Input:
                if (Reveal == null || r.ReadInt32()!=Reveal.Seed || State!=Phase.Race) break;
                int count=r.ReadByte(); if(count>16) throw new InvalidDataException("input count");
                for(int i=0;i<count;i++) InputIn?.Invoke(from, DriverCommand.Read(r));
                break;
            case Msg.Loaded:
                AcceptLoaded(from,r.ReadInt32(),r.ReadString(),r.ReadString());
                break;
            case Msg.Rescue:
                if (Reveal!=null && r.ReadInt32()==Reveal.Seed && State==Phase.Race) RescueIn?.Invoke(from);
                break;
        }
    }

    void ClientPacket(long from, Msg type, BinaryReader r)
    {
        if(from!=1) return;
        if (type >= Msg.BoardConsent) { ClientBoardPacket(type, r); return; }      // LanSession.Boards.cs
        switch (type)
        {
            case Msg.LoadTrack:
            {
                var rv=ReadReveal(r);
                if(rv.Round<=RoundNumber) break;
                BoardLeftLobby();
                RoundNumber=rv.Round;Reveal=rv;State=Phase.Loading;_loaded.Clear();_loadedWire=0;LastLoadError=null;_testPackets.Clear();
                LoadRequested?.Invoke();break;
            }
            case Msg.LoadFailed:
            {
                int round=r.ReadInt32();string reason=r.ReadString();
                if(round!=RoundNumber) break;
                LastLoadError=reason;State=Phase.Lobby;Reveal=null;LoadFailed?.Invoke(reason);break;
            }
            case Msg.TrackRequired:
                if (!InLobby) TrackRequired?.Invoke(r.ReadString());
                break;
            case Msg.Reject:
            {
                string why = r.ReadString();
                Log($"rejected: {why}");
                End(why);
                break;
            }
            case Msg.Ping:
                Send(1, Msg.Pong, w => { w.Write(r.ReadDouble()); w.Write(Now); });
                break;
            case Msg.Lobby:
                ReadLobby(r);
                break;
            case Msg.Ballot:
                BallotEndsHost = r.ReadDouble();
                BoardLeftLobby();
                State = Phase.Ballot;
                foreach (var p in Players.Values) p.Voted = false;
                Log("ballot opened");
                BallotOpened?.Invoke();
                break;
            case Msg.Reveal:
                _worldParts.Clear();_worldApplied=0;
                Reveal = ReadReveal(r);
                State = Phase.Race;
                Results.Clear();
                WinnerId = 0;
                RevealIn?.Invoke();
                break;
            case Msg.Countdown:
                if(Reveal != null && r.ReadInt32()==Reveal.Seed)
                {
                    double go=r.ReadDouble();
                    if(!double.IsFinite(go) || go<=0) throw new InvalidDataException("countdown clock");
                    Reveal.GoHost=go;
                    Log($"all clutches ready: GO at host time {go:F3}");
                }
                break;
            case Msg.Snap:
                SnapsIn++;
                if(Reveal!=null && r.ReadInt32()==Reveal.Seed) { long id=r.ReadInt64(); SnapIn?.Invoke(id, Snapshot.Read(r)); }
                break;
            case Msg.Effect:
                if(Reveal!=null && r.ReadInt32()==Reveal.Seed) EffectIn?.Invoke(ReadV(r),r.ReadByte());
                break;
            case Msg.World:
                if(Reveal!=null && r.ReadInt32()==Reveal.Seed) ReadWorldPart(r);
                break;
            case Msg.WorldMesh:
                if(Reveal!=null && r.ReadInt32()==Reveal.Seed) {
                    int size=r.ReadInt32(); if(size<0 || size>8*1024*1024) throw new InvalidDataException("world size");
                    var bytes=r.ReadBytes(size); if(bytes.Length!=size) throw new EndOfStreamException();
                    MeshIn?.Invoke(bytes);
                }
                break;
            case Msg.Winner:
            {
                if(r.ReadInt32()!=RoundNumber) break;
                long id = r.ReadInt64();
                float t = r.ReadSingle();
                DnfAtHost = r.ReadDouble();
                WinnerId = id;
                if (Players.TryGetValue(id, out var winner)) { winner.Finished = true; winner.FinishTime = t; }
                WinnerIn?.Invoke(id, t);
                break;
            }
            case Msg.SessionEnd:
                if(r.ReadInt32()!=RoundNumber || State!=Phase.Results) break;
                ReadScores(r);State=Phase.SessionFinished;SessionEnded?.Invoke();
                break;
            case Msg.Results:
                if(!ReadResults(r)) break;
                State = Phase.Results;
                ResultsIn?.Invoke();
                break;
            case Msg.Left:
            {
                long id = r.ReadInt64();
                string name = r.ReadString();
                Players.Remove(id);
                BoardPeerGone(id);
                PlayerLeft?.Invoke(id, name);
                Changed?.Invoke();
                break;
            }
            case Msg.Broke:
                BrokeIn?.Invoke(r.ReadInt32(), r.ReadSingle(), ReadV(r), ReadV(r));
                break;
        }
    }

    void ReadLobby(BinaryReader r)
    {
        var state = (Phase)r.ReadByte();
        Laps = r.ReadByte();
        _myId = r.ReadInt64();
        _offset = r.ReadDouble();
        BallotEndsHost = r.ReadDouble();
        DnfAtHost = r.ReadDouble();
        int n = r.ReadByte();
        var seen = new HashSet<long>();
        for (int i = 0; i < n; i++)
        {
            long id = r.ReadInt64();
            if (!Players.TryGetValue(id, out var p)) Players[id] = p = new LanPlayer { Id = id };
            p.Name = r.ReadString();
            p.Slot = r.ReadByte();
            p.PingMs = r.ReadInt16();
            int f = r.ReadByte();
            p.Voted = (f & 1) != 0;
            p.Finished = (f & 2) != 0;
            p.Dnf = (f & 4) != 0;
            p.BoardShare = (f & 8) != 0; p.BoardBusy = (f & 16) != 0;
            seen.Add(id);
        }
        foreach (var id in Players.Keys.Where(k => !seen.Contains(k)).ToList()) Players.Remove(id);
        ReadTrackOptions(r);
        _loadedWire=r.ReadByte();
        ReadScores(r);
        bool first = !InLobby;
        InLobby = true;
        if (first) Log($"in the lobby as peer {_myId}, slot {Me?.Slot + 1}");
        // The host went back to the lobby after the results.
        if (state == Phase.Lobby && State != Phase.Lobby) { Reveal = null; WinnerId = 0; }
        State = state;
        Changed?.Invoke();
    }

    // ------------------------------------------------------------ host: the door

    void Hello(long id, string version, string sig, string name, string track, string token)
    {
        if (!_pending.Remove(id)) return;
        string why = version != Version + "/lan" + Protocol ? $"VERSION MISMATCH: HOST {Version}, YOU {version}"
            : !Guid.TryParseExact(token,"N",out _) ? "INVALID SESSION IDENTITY"
            : _identities.Values.Contains(token) ? "PLAYER ALREADY CONNECTED"
            : Players.Count >= MaxPlayers ? "GAME FULL"
            : State != Phase.Lobby ? "RACE ALREADY RUNNING"
            : null;
        if (why != null)
        {
            Log($"rejected {name} (peer {id}): {why}");
            Send(id, Msg.Reject, w => w.Write(why));
            _kick.Add((id, Now + 0.5));
            return;
        }
        name = UniqueName(Race.Leaderboard.CleanName(name));
        int slot = _scores.TryGetValue(token,out var prior) && Players.Values.All(p=>p.Slot!=prior.Slot)
            ? prior.Slot : Enumerable.Range(0, MaxPlayers).First(s => Players.Values.All(p => p.Slot != s));
        Players[id] = new LanPlayer { Id = id, Name = name, Slot = slot, SessionToken=token };
        RegisterScore(id,token,Players[id]);
        Log($"{name} joined (peer {id}, slot {slot + 1}), {Players.Count} in the lobby");
        _dirty = true;
        _pingAcc = PingEvery;
    }

    string UniqueName(string name)
    {
        if (name.Length == 0) name = Race.Leaderboard.DefaultName;
        string n = name;
        for (int k = 2; Players.Values.Any(p => p.Name == n); k++)
            n = (name.Length > Race.Leaderboard.MaxName - 2 ? name[..(Race.Leaderboard.MaxName - 2)] : name) + " " + k;
        return n;
    }

    // ------------------------------------------------------------ lobby and the secret ballot

    public void SetLaps(int laps)
    {
        if (!IsHost || State != Phase.Lobby) return;
        Laps = Math.Clamp(laps, 1, 99);
        _dirty = true;
    }

    /// <summary>Host: open the ballot box (two comrades or more, from the lobby).</summary>
    double _ballotWaitLogged = -10;
    public bool StartBallot()
    {
        if (!IsHost || State != Phase.Lobby || Players.Count < 2) return false;
        // SHARE BOARDS: the ballot waits until no comrade has board work in hand (cancel or NO quiesces it).
        if (BoardSyncPending) { if (Now - _ballotWaitLogged > 2) { _ballotWaitLogged = Now; BoardLog("the ballot waits for board sync: " + BoardPendingNames); } return false; }
        BoardLeftLobby();
        State = Phase.Ballot;
        BallotEndsHost = Now + BallotTime;
        foreach (var p in Players.Values) { p.Voted = false; p.Vote = 0; p.DamageVote = true; p.FuelVote = false; p.ObstaclesVote = true; p.Finished = p.Dnf = p.Pad = false; p.FinishTime = p.BestLap = -1; }
        Results.Clear();
        WinnerId = 0;
        DnfAtHost = _dnfAt = -1;
        Log($"ballot opened, {Players.Count} comrades, {Laps} laps");
        Broadcast(Msg.Ballot, w => w.Write(BallotEndsHost));
        _dirty = true;
        BallotOpened?.Invoke();
        return true;
    }

    /// <summary>The own ballot (power in percent): to the host only.</summary>
    public void Vote(int pct, bool damage = true, bool fuel = false, string track = null, bool obstacles = true)
    {
        if (State != Phase.Ballot) return;
        track ??= TrackOptions[0];
        if (IsHost) CastVote(Players[1], pct, damage, fuel, track, obstacles);
        else Send(1, Msg.Vote, w => { w.Write((ushort)pct); w.Write(damage); w.Write(fuel); w.Write(track); w.Write(obstacles); });
    }

    void CastVote(LanPlayer p, int pct, bool damage, bool fuel, string track, bool obstacles)
    {
        if (State != Phase.Ballot || p.Voted || !LanRace.ValidFuel(pct) || !TrackOptions.Contains(track)) return;
        p.Voted = true;
        p.Vote = pct;
        p.DamageVote = damage; p.FuelVote = fuel; p.TrackVote=track; p.ObstaclesVote=obstacles;
        Log($"{p.Name} voted");
        _dirty = true;
        if (Players.Values.All(x => x.Voted)) Tally();
    }

    /// <summary>Host: the 60 s are up; whoever has not submitted is NOT VOTED (no default ballot).</summary>
    internal void BallotDeadline(double now)
    {
        if (!IsHost || State != Phase.Ballot || now < BallotEndsHost) return;
        Log($"ballot deadline: {Players.Values.Count(p => !p.Voted)} of {Players.Count} NOT VOTED");
        Tally();
    }

    /// <summary>
    /// Host: count the ballots, draw the drum and the grid, fix GO; everyone gets the same count.
    /// Only submitted ballots count; the unsubmitted keep their grid place and show NOT VOTED.
    /// With no ballot at all the drum draws among every fuel, both rules and every host track.
    /// </summary>
    void Tally()
    {
        var ballots = Players.Values.OrderBy(p => p.Slot).Select(p => (p.Id, p.Name, p.Slot, p.Voted ? p.Vote : LanBallot.Abstain)).ToList();
        var tied = LanBallot.Tally(ballots.Select(b => b.Item4), out var counts);
        int seed = TestSeed ?? (int)(GD.Randi() & 0x7fffffff);
        var rv = new LanReveal
        {
            Laps = Laps, Seed = seed, Tied = tied, WinnerIndex = LanBallot.Draw(tied.Count, seed), Ballots = ballots,
            Grid = LanBallot.Shuffle(ballots.Select(b => b.Id), seed),
            StartHost = Now + 0.15,
        };
        rv.RuleVotes = Players.Values.OrderBy(p => p.Slot).Select(p => p.Voted ? ((bool?)p.DamageVote, (bool?)p.FuelVote) : ((bool?)null, (bool?)null)).ToList();
        rv.ObstacleVotes = Players.Values.OrderBy(p => p.Slot).Select(p => p.Voted ? (bool?)p.ObstaclesVote : null).ToList();
        rv.Obstacles = LanBallot.Rule(rv.ObstacleVotes, seed, 0x4f4253);
        rv.Damage = LanBallot.Rule(rv.RuleVotes.Select(v => v.damage), seed, 0x44414d);
        rv.Fuel = LanBallot.Rule(rv.RuleVotes.Select(v => v.fuel), seed, 0x465545);
        Log($"rules: damage {rv.Damage}, fuel consumption {rv.Fuel}, moving obstacles {rv.Obstacles}");
        rv.GoHost = 0; // the grid waits until every connected driver's clutch is down
        BeginTrackLoad(rv);
    }

    void WriteReveal(BinaryWriter w,LanReveal rv)
    {
        w.Write(rv.Round);w.Write(rv.Track);
        w.Write((byte)rv.TrackTied.Count);foreach(string id in rv.TrackTied) w.Write(id);
            w.Write(rv.StartHost); w.Write(rv.GoHost);
            w.Write((byte)rv.Laps); w.Write(rv.Seed); w.Write((byte)rv.WinnerIndex);
            w.Write((byte)rv.Tied.Count);
            foreach (int t in rv.Tied) w.Write((ushort)t);
            w.Write((byte)rv.Ballots.Count);
            foreach (var b in rv.Ballots) { w.Write(b.id); w.Write(b.name); w.Write((byte)b.slot); w.Write((ushort)b.pct); }
            w.Write((byte)rv.Grid.Count);
            foreach (long g in rv.Grid) w.Write(g);
            w.Write(rv.Damage); w.Write(rv.Fuel);
            foreach (var v in rv.RuleVotes) { w.Write(RuleByte(v.damage)); w.Write(RuleByte(v.fuel)); }
            w.Write(rv.Obstacles);
            for (int i=0;i<rv.Ballots.Count;i++) w.Write(RuleByte(i<rv.ObstacleVotes.Count ? rv.ObstacleVotes[i] : LanBallot.Voted(rv.Ballots[i].pct) ? rv.Obstacles : null));
    }

    LanReveal ReadReveal(BinaryReader r)
    {
        int round=r.ReadInt32();string track=r.ReadString();
        if(round<1 || !CCCPRacing.Track.TrackIds.Known(track)) throw new InvalidDataException("track reveal");
        int tn=r.ReadByte();if(tn>4) throw new InvalidDataException("track ties");
        var ties=new List<string>();for(int i=0;i<tn;i++) {string id=r.ReadString();if(!CCCPRacing.Track.TrackIds.Known(id))throw new InvalidDataException("track tie");ties.Add(id);}
        var rv = new LanReveal { Round=round,Track=track,TrackTied=ties,StartHost = r.ReadDouble(), GoHost = r.ReadDouble(), Laps = r.ReadByte(), Seed = r.ReadInt32(), WinnerIndex = r.ReadByte() };
        int n = r.ReadByte();
        for (int i = 0; i < n; i++) rv.Tied.Add(r.ReadUInt16());
        n = r.ReadByte();
        for (int i = 0; i < n; i++)
        {
            var b = (r.ReadInt64(), r.ReadString(), r.ReadByte(), (int)r.ReadUInt16());
            if (LanBallot.Voted(b.Item4) && !LanRace.ValidFuel(b.Item4)) throw new InvalidDataException("ballot fuel");
            rv.Ballots.Add(b);
        }
        n = r.ReadByte();
        for (int i = 0; i < n; i++) rv.Grid.Add(r.ReadInt64());
        rv.Damage = r.ReadBoolean(); rv.Fuel = r.ReadBoolean();
        for (int i = 0; i < rv.Ballots.Count; i++)
        {
            bool? d = ReadRule(r), f = ReadRule(r);
            // NOT VOTED is one ballot: the fuel marker and both rule markers agree, or the count is forged.
            if (LanBallot.Voted(rv.Ballots[i].pct) == (d == null) || (d == null) != (f == null)) throw new InvalidDataException("ballot abstention");
            rv.RuleVotes.Add((d, f));
        }
        rv.Obstacles = r.ReadBoolean();
        for (int i=0;i<rv.Ballots.Count;i++) {
            bool? o=ReadRule(r);
            if (LanBallot.Voted(rv.Ballots[i].pct)==(o==null)) throw new InvalidDataException("obstacle abstention");
            rv.ObstacleVotes.Add(o);
        }
        if (rv.Tied.Count == 0 || rv.Tied.Any(t => !LanRace.ValidFuel(t)) || rv.WinnerIndex >= rv.Tied.Count) throw new InvalidDataException("empty tally");
        Laps = rv.Laps;
        Log($"the count: tied [{string.Join(",", rv.Tied)}], winner {rv.Power}%, GO at host time {rv.GoHost:F3} (local {rv.GoHost + _offset:F3}, offset {_offset * 1000:F2} ms)");
        return rv;
    }

    /// <summary>Rule vote on the wire: 0 NO, 1 YES, 2 NOT VOTED.</summary>
    const byte RuleAbstain = 2;
    static byte RuleByte(bool? v) => v is bool b ? (byte)(b ? 1 : 0) : RuleAbstain;
    static bool? ReadRule(BinaryReader r) => r.ReadByte() switch { 0 => false, 1 => true, RuleAbstain => null, _ => throw new InvalidDataException("rule vote") };

    // ------------------------------------------------------------ the race

    public void StartCountdown()
    {
        if(!IsHost || State!=Phase.Race || Reveal==null || Reveal.GoHost>0 || Now < Reveal.StartHost + Reveal.Timeline.End) return;
        Reveal.GoHost=Now+LanBallot.Countdown;
        Broadcast(Msg.Countdown,w=>{w.Write(Reveal.Seed);w.Write(Reveal.GoHost);});
        Log($"all clutches ready: GO at host time {Reveal.GoHost:F3}");
    }

    /// <summary>Host: one authoritative car, including its last consumed input sequence.</summary>
    public void SendSnapshot(long id, Snapshot s)
    {
        if(!IsHost || Reveal==null) return;
        foreach(long to in Players.Keys) if(to!=1)
            Send(to,Msg.Snap,w=>{ w.Write(Reveal.Seed); w.Write(id); s.Write(w); },MultiplayerPeer.TransferModeEnum.UnreliableOrdered,1);
        ReplaySnapOut?.Invoke(id,s);
        SnapsSent++; SnapBytes=s.State.Length+200;
    }

    public void SendInputs(System.Collections.Generic.IReadOnlyList<DriverCommand> commands)
    {
        if(IsHost || Reveal==null || State!=Phase.Race) return;
        Send(1,Msg.Input,w=> { w.Write(Reveal.Seed); int n=Math.Min(commands.Count,12); w.Write((byte)n);
            for(int k=commands.Count-n;k<commands.Count;k++) commands[k].Write(w);
        },MultiplayerPeer.TransferModeEnum.UnreliableOrdered,2);
    }

    public void SendWorld(byte[] bytes, bool mesh=false)
    {
        if(!IsHost || Reveal==null) return;
        ReplayWorldOut?.Invoke(bytes,mesh);
        if(mesh) {
            foreach(long to in Players.Keys) if(to!=1) Send(to,Msg.WorldMesh,w=>{w.Write(Reveal.Seed);w.Write(bytes.Length);w.Write(bytes);},MultiplayerPeer.TransferModeEnum.Reliable,4);
            return;
        }
        // ENet unreliable packets must fit the path MTU. Reassemble only complete frames;
        // a lost part discards one frame and the next complete frame replaces it.
        const int chunk=1000;
        uint frame=++_worldFrame;int count=(bytes.Length+chunk-1)/chunk;
        // One XOR parity fragment recovers one lost data fragment without retransmission.
        var parity=new byte[chunk];for(int k=0;k<bytes.Length;k++) parity[k%chunk]^=bytes[k];
        for(int part=0;part<=count;part++) {
            int index=part,offset=part*chunk,length=part==count?chunk:Math.Min(chunk,bytes.Length-offset);
            foreach(long to in Players.Keys) if(to!=1) Send(to,Msg.World,w=>{
                w.Write(Reveal.Seed);w.Write(frame);w.Write((ushort)index);w.Write((ushort)count);w.Write(bytes.Length);w.Write((ushort)length);
                if(index==count) w.Write(parity);else w.Write(bytes,offset,length);
            },MultiplayerPeer.TransferModeEnum.Unreliable,3);
        }
    }
    internal void ReadWorldPart(BinaryReader r)
    {
        uint frame=r.ReadUInt32();int part=r.ReadUInt16(),count=r.ReadUInt16(),total=r.ReadInt32(),length=r.ReadUInt16();
        if(count<1||count>1024||part>count||total<=0||total>count*1000||total<=(count-1)*1000||length!=(part==count?1000:Math.Min(1000,total-part*1000))) throw new InvalidDataException("world fragment");
        if(frame<=_worldApplied) return;
        if(!_worldParts.TryGetValue(frame,out var parts)) {_worldParts[frame]=parts=new byte[count+1][];}
        if(parts.Length!=count+1) throw new InvalidDataException("world fragment count");
        parts[part]=r.ReadBytes(length);if(parts[part].Length!=length) throw new EndOfStreamException();
        var missing=Enumerable.Range(0,count).Where(i=>parts[i]==null).ToArray();
        if(missing.Length==1 && parts[count]!=null) {
            int lost=missing[0];var recovered=(byte[])parts[count].Clone();
            for(int i=0;i<count;i++) if(i!=lost) for(int j=0;j<parts[i].Length;j++) recovered[j]^=parts[i][j];
            Array.Resize(ref recovered,lost==count-1?total-(count-1)*1000:1000);parts[lost]=recovered;
            missing=Array.Empty<int>();
        }
        if(missing.Length==0) {
            using var stream=new MemoryStream();for(int i=0;i<count;i++) stream.Write(parts[i]);
            _worldApplied=frame;WorldFramesIn++;WorldIn?.Invoke(stream.ToArray());
        }
        foreach(uint old in _worldParts.Keys.Where(f=>f<=_worldApplied||f+4<frame).ToArray()) _worldParts.Remove(old);
    }
    public void SendEffect(Vector3 p,int kind) {if(IsHost && Reveal!=null) {ReplayEffectOut?.Invoke(p,kind);Broadcast(Msg.Effect,w=>{w.Write(Reveal.Seed);WriteV(w,p);w.Write((byte)kind);});}}
    public void RequestRescue() { if(IsHost) RescueIn?.Invoke(1); else if(Reveal!=null) Send(1,Msg.Rescue,w=>w.Write(Reveal.Seed)); }
    public void FinishFor(long id,float time,float best,bool pad=false) { if(IsHost && Players.TryGetValue(id,out var p)) Finished(p,time,best,pad); }

    /// <summary>The own car broke a tree, pole or wall: it falls on every screen.</summary>
    public void SendBroke(int obstacle, float impact, Vector3 vel, Vector3 pos)
    {
        if (!IsHost) return;
        ReplayBreakOut?.Invoke(obstacle,impact,vel,pos);
        Send(0, Msg.Broke, w => { w.Write(obstacle); w.Write(impact); WriteV(w, vel); WriteV(w, pos); });
    }

    void Finished(LanPlayer p, float time, float best, bool pad)
    {
        if (State != Phase.Race || p.Finished) return;
        p.Finished = true;
        p.FinishTime = time;
        p.BestLap = best;
        p.Pad = pad;
        Log($"{p.Name} finished in {Race.LapTimer.Format(time)}, best lap {Race.LapTimer.Format(best)}");
        if (WinnerId == 0)
        {
            WinnerId = p.Id;
            DnfAtHost = _dnfAt = Now + DnfAfter;
            Broadcast(Msg.Winner, w => { w.Write(RoundNumber); w.Write(p.Id); w.Write(time); w.Write(DnfAtHost); });
            WinnerIn?.Invoke(p.Id, time);
        }
        _dirty = true;
        CheckRaceOver();
    }

    /// <summary>Host: everyone is in or out: the results to all.</summary>
    void CheckRaceOver()
    {
        if (!IsHost || State != Phase.Race || Reveal == null || Players.Values.Any(p => !p.Finished && !p.Dnf)) return;
        Results.Clear();
        foreach (var p in _roundRoster.Values.OrderBy(p => p.Finished ? 0 : 1).ThenBy(p => p.Finished ? p.FinishTime : p.Slot))
            Results.Add(new LanResult { Id = p.Id, Name = p.Name, Slot = p.Slot, Time = p.Finished ? p.FinishTime : -1, Best = p.BestLap, Pad = p.Finished && p.Pad });
        AwardSessionWin();
        State = Phase.Results;
        _dnfAt = -1;
        Log("results: " + string.Join(", ", Results.Select((x, i) => $"{i + 1}. {x.Name} {(x.Time < 0 ? "DNF" : Race.LapTimer.Format(x.Time))}")));
        Broadcast(Msg.Results, w =>
        {
            w.Write(RoundNumber);
            w.Write((byte)Results.Count);
            foreach (var x in Results) { w.Write(x.Id); w.Write(x.Name); w.Write((byte)x.Slot); w.Write(x.Time); w.Write(x.Best); w.Write((byte)(x.Pad ? 1 : 0)); }
            WriteScores(w);
        });
        _dirty = true;
        ResultsIn?.Invoke();
    }

    bool ReadResults(BinaryReader r)
    {
        int round=r.ReadInt32();
        if(round!=RoundNumber || round<=_lastResultsRound) return false;
        Results.Clear();
        int n = r.ReadByte();
        for (int i = 0; i < n; i++)
            Results.Add(new LanResult { Id = r.ReadInt64(), Name = r.ReadString(), Slot = r.ReadByte(), Time = r.ReadSingle(), Best = r.ReadSingle(), Pad = r.ReadByte() != 0 });
        ReadScores(r);
        _lastResultsRound=round;
        Log("results in");
        return true;
    }

    /// <summary>Host: after the results, back to the lobby for another ballot.</summary>
    public void BackToLobby()
    {
        if (!IsHost || State != Phase.Results) return;
        State = Phase.Lobby;
        Reveal = null;
        WinnerId = 0;
        DnfAtHost = _dnfAt = -1;
        foreach (var p in Players.Values) { p.Voted = p.Finished = p.Dnf = false; }
        _dirty = true;
        Log("back to the lobby");
    }

    static void WriteV(BinaryWriter w, Vector3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
    static Vector3 ReadV(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
}
