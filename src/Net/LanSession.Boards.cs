using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace CCCPRacing.Net;

/// <summary>
/// SHARE BOARDS (docs/lan-leaderboard-sync.md): comrades who each say YES in the lobby exchange their boards of honour (times and names)
/// and the recordings attached to them. Consent is per comrade, starts NO and is forgotten when the session closes. Everything goes
/// through the host, which relays between sharing comrades even when it does not share itself (it then merges and stores nothing on
/// disk). Only in the lobby: the host's ballot waits while any comrade is still syncing, and leaving the lobby stops every transfer.
/// Recordings move in acknowledged 16 KB chunks on their own reliable channel, a few per frame, one download at a time per comrade.
/// </summary>
public partial class LanSession
{
    public const int BoardChannel = 5;
    public const int BoardChunkBytes = 16 * 1024;
    const int BoardWindow = 4, BoardChunksPerFrame = 4, BoardMaxServing = 2, BoardMaxQueue = 128, BoardBusyRetries = 6;
    const double BoardTimeout = 20, BoardBusyRetryDelay = 1.5;

    /// <summary>Test seam: when set, every packet goes here (to, bytes, channel) instead of ENet, and the session counts as active.</summary>
    internal Action<long, byte[], int> TestWire;

    /// <summary>This machine's records, while a race scene is up (RaceScene.LanBoards.cs). Null: nothing is merged here.</summary>
    public ILanBoardStore BoardStore;
    /// <summary>This machine said YES in this session.</summary>
    public bool BoardShareMine { get; private set; }
    /// <summary>When this session began (names the board backups of the session).</summary>
    public string BoardSessionStamp { get; private set; } = DateTime.Now.ToString("yyyyMMdd-HHmmss");
    public event Action BoardChanged;

    // ---- host: the stored offers and every comrade's sync state (delivered offers, acknowledged, busy)
    readonly Dictionary<long, byte[]> _boardOffers = new();
    readonly Dictionary<long, (uint Delivered, uint Acked, bool Busy)> _boardPeers = new();
    uint _boardDeliverSeq;
    readonly Dictionary<long,double> _boardAwaitingOffers = new();

    // ---- this machine as a participant
    uint _boardAckSeq, _boardAckSent; bool _boardBusySent; bool _boardStatusDirty;
    int _boardGen;
    readonly List<(LanBoardWant Want, double NotBefore, int Retries)> _boardWants = new();
    readonly HashSet<string> _boardWanted = new();            // recording fingerprints/ids queued or being fetched (no double fetch)
    readonly HashSet<string> _boardOffered = new();           // recording ids in this machine's last offer (only these are served)
    readonly Dictionary<string, string> _boardKnown = new();  // track -> its current signature (from the comrades' built tracks)
    BoardIncoming _boardIn;
    readonly List<BoardServe> _boardServe = new();
    readonly List<(Task Task, Action<Task> Done)> _boardTasks = new();   // worker jobs: polled on the main thread, outlive a closed session
    int _boardTotal, _boardDone, _boardFailed, _boardSkipped;

    sealed class BoardIncoming
    {
        public LanBoardWant Want; public int Tid, Retries; public byte[] Buffer; public bool[] Got; public int Count, Received; public double Progress;
    }
    sealed class BoardServe
    {
        public long To; public int Tid; public string Id; public Task<byte[]> Read; public byte[] Payload; public int Next, Count, Acked; public bool[] AckedParts; public double Progress;
    }

    // ---- what the lobby shows
    public string BoardNote { get; private set; }
    public bool BoardNoteBad { get; private set; }
    public double BoardNoteAt { get; private set; } = -1;
    public int BoardRowsAdded, BoardRecordingsImported, BoardRecordingsFailed, BoardOffersIn;
    /// <summary>Counters for the tests: chunks sent and relayed, and chunks that arrived outside the lobby (must stay 0).</summary>
    public int BoardChunksSent, BoardChunksRelayed, BoardChunksIn, BoardChunksOutsideLobby, BoardMessagesBlocked;
    public static bool BoardDebug = OS.HasEnvironment("LANDBG") || OS.HasEnvironment("BOARDDBG");

    /// <summary>The recordings of this sync: fetched (or found here) of all wanted; bytes of the one coming in now.</summary>
    public (int Done, int Total, int Failed, long Bytes, long Of) BoardProgress
        => (_boardDone, _boardTotal, _boardFailed, _boardIn?.Received * (long)BoardChunkBytes ?? 0, _boardIn?.Buffer?.Length ?? (_boardIn?.Want.Bytes ?? 0));

    /// <summary>This machine still has board work in hand: an offer to merge, recordings to fetch or serve, a worker job running.</summary>
    public bool BoardBusyLocal => _boardWants.Count > 0 || _boardIn != null || _boardServe.Count > 0 || _boardTasks.Count > 0;

    void BoardLog(string s) { GD.Print($"lan-boards[{MyName}]: {s}"); }

    void BoardSay(string note, bool bad = false)
    {
        BoardNote = note; BoardNoteBad = bad; BoardNoteAt = Now;
        BoardLog((bad ? "NOTICE (bad): " : "notice: ") + note);
        BoardChanged?.Invoke();
    }

    // ------------------------------------------------------------ consent, offers

    /// <summary>YES / NO for this machine (lobby only). NO withdraws: every transfer stops and nothing more is sent or merged. False: not now.</summary>
    public bool SetBoardShare(bool on)
    {
        if (!Active || !InLobby || State != Phase.Lobby) return false;
        if (on == BoardShareMine) return true;
        BoardShareMine = on;
        if (!on) BoardStopLocal("YOU STOPPED SHARING BOARDS", false);
        else { _boardTotal = _boardDone = _boardFailed = _boardSkipped = 0; BoardSay("SHARING BOARDS WITH COMRADES WHO SAY YES"); }
        if (IsHost) HostConsent(1, on, on);
        else
        {
            Send(1, Msg.BoardConsent, w => { w.Write(on); w.Write(on); });
            if (on) SendOwnOffer();
        }
        BoardLog($"consent {(on ? "YES" : "NO")}");
        _boardStatusDirty = true;
        BoardChanged?.Invoke();
        return true;
    }

    /// <summary>Resync (YES, idle): offer this machine's boards again and get everyone's offers again; duplicates merge to nothing. False: not now.</summary>
    public bool ResyncBoards()
    {
        if (!BoardShareMine || State != Phase.Lobby || BoardBusyLocal) return false;
        _boardTotal = _boardDone = _boardFailed = _boardSkipped = 0;
        BoardSay("RESYNCING BOARDS");
        if (IsHost) HostConsent(1, true, true);
        else { Send(1, Msg.BoardConsent, w => { w.Write(true); w.Write(true); }); SendOwnOffer(); }
        return true;
    }

    /// <summary>Cancel the running sync (still YES): transfers stop, the queue is dropped; a worker job finishes and is thrown away.</summary>
    public void CancelBoardSync()
    {
        if (!BoardBusyLocal) return;
        BoardStopLocal("BOARD SYNC CANCELLED", false);
    }

    LanBoardManifest BuildOwnOffer()
    {
        _boardOffered.Clear();
        if (BoardStore == null) return null;
        LanBoardManifest m;
        try { m = BoardStore.BuildManifest(out string note); if (note != null) BoardSay(note); }
        catch (Exception e) { BoardSay("BOARDS NOT OFFERED: " + e.Message.ToUpperInvariant(), true); return null; }
        foreach (var t in m.Tracks) foreach (var s in t.Sets) foreach (var r in s.Rows) if (r.RecordingId != null) _boardOffered.Add(r.RecordingId);
        if (m.BuiltTrack.Length > 0) _boardKnown[m.BuiltTrack] = m.BuiltSignature;
        BoardLog($"offer: {m.Tracks.Count} tracks, {m.RowCount} rows, {m.RecordingCount} recordings, {m.Pending} still saving");
        return m;
    }

    void SendOwnOffer()
    {
        var m = BuildOwnOffer();
        var bytes = (m ?? new LanBoardManifest()).ToBytes();
        if (bytes.Length > LanBoardManifest.MaxBytes) { BoardSay("BOARDS TOO LARGE TO OFFER", true); return; }
        Send(1, Msg.BoardOffer, w => { w.Write(bytes.Length); w.Write(bytes); });
    }

    /// <summary>Host: a comrade's YES / NO (and resync: deliver the stored offers again).</summary>
    void HostConsent(long from, bool on, bool deliver)
    {
        if (State != Phase.Lobby || !Players.TryGetValue(from, out var p)) { BoardMessagesBlocked++; return; }
        p.BoardShare = on;
        if (!on) { _boardOffers.Remove(from); _boardAwaitingOffers.Remove(from); _dirty = true; return; }
        if (deliver) _boardAwaitingOffers[from] = Now;
        if (from == 1)
        {
            var m = BuildOwnOffer();
            var bytes = (m ?? new LanBoardManifest()).ToBytes();
            HostStoreOffer(1, bytes);
        }
        if (deliver)
            foreach (var (origin, bytes) in _boardOffers.ToList())
                if (origin != from && Players.TryGetValue(origin, out var o) && o.BoardShare) DeliverOffer(from, origin, bytes);
        _dirty = true;
    }

    /// <summary>Host: keep a sharing comrade's latest offer (memory only) and deliver it to every other sharing comrade.</summary>
    void HostStoreOffer(long from, byte[] bytes)
    {
        if (State != Phase.Lobby || !Players.TryGetValue(from, out var p) || !p.BoardShare) { BoardMessagesBlocked++; return; }
        try { LanBoardManifest.Read(bytes); } catch (Exception e) when (e is InvalidDataException or EndOfStreamException) { GD.PushWarning($"lan: bad board offer from {from}: {e.Message}"); return; }
        _boardAwaitingOffers.Remove(from);
        _boardOffers[from] = bytes;
        foreach (var to in Players.Values.Where(x => x.Id != from && x.BoardShare).Select(x => x.Id).ToList()) DeliverOffer(to, from, bytes);
    }

    void DeliverOffer(long to, long origin, byte[] bytes)
    {
        uint seq = ++_boardDeliverSeq;
        var st = _boardPeers.GetValueOrDefault(to); st.Delivered = seq; _boardPeers[to] = st;
        if (to == 1) { ProcessOffer(origin, seq, bytes); return; }
        Send(to, Msg.BoardOffer, w => { w.Write(origin); w.Write(seq); w.Write(bytes.Length); w.Write(bytes); });
    }

    /// <summary>A sharing comrade's offer reached this machine: merge it now (small files, main thread) and queue the recordings to fetch.</summary>
    void ProcessOffer(long origin, uint seq, byte[] bytes)
    {
        _boardAckSeq = Math.Max(_boardAckSeq, seq); _boardStatusDirty = true;
        if (IsHost) { var st = _boardPeers.GetValueOrDefault(1); st.Acked = Math.Max(st.Acked, seq); _boardPeers[1] = st; }
        if (!BoardShareMine || State != Phase.Lobby || origin == MyId) { BoardMessagesBlocked++; return; }
        LanBoardManifest m;
        try { m = LanBoardManifest.Read(bytes); }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException) { BoardSay("A COMRADE'S BOARDS WERE UNREADABLE", true); GD.PushWarning($"lan: bad board offer from {origin}: {e.Message}"); return; }
        BoardOffersIn++;
        if (m.BuiltTrack.Length > 0) _boardKnown[m.BuiltTrack] = m.BuiltSignature;
        string who = Players.GetValueOrDefault(origin)?.Name ?? "A COMRADE";
        if (BoardStore == null) { BoardSay("BOARDS FROM " + who + " NOT MERGED: NO RECORDS HERE NOW", true); return; }
        LanBoardMergeResult res;
        try { res = BoardStore.Merge(m, origin, _boardKnown); }
        catch (Exception e) { BoardSay("BOARDS FROM " + who + " NOT MERGED: " + e.Message.ToUpperInvariant(), true); return; }
        BoardRowsAdded += res.RowsAdded;
        int queued = 0;
        foreach (var w in res.Wants)
        {
            string key = w.Track + "|" + w.Set + "|" + w.RowKey;
            if (_boardWants.Count >= BoardMaxQueue || !_boardWanted.Add(key)) continue;
            _boardWants.Add((w, 0, 0)); queued++; _boardTotal++;
        }
        BoardLog($"merged offer {seq} from {who}: +{res.RowsAdded} rows, {res.Duplicates} already here, {res.LinkedLocal} linked to recordings here, {queued} recordings to fetch, skipped [{string.Join("; ", res.Skipped)}]{(res.Error != null ? ", error " + res.Error : "")}");
        string skipped = res.Skipped.Count > 0 ? $", {res.Skipped.Count} SKIPPED" : "";
        if (res.Error != null) BoardSay($"BOARDS FROM {who}: {res.Error.ToUpperInvariant()}", true);
        else BoardSay(res.RowsAdded == 0 && queued == 0 ? $"{who}: NOTHING NEW{skipped}" : $"{who}: +{res.RowsAdded} RESULTS, {queued} RECORDINGS TO FETCH{skipped}");
        if (m.Pending > 0) BoardSay($"{who}: {m.Pending} RECORDINGS STILL SAVING THERE, NOT OFFERED");
        _boardStatusDirty = true;
    }

    // ------------------------------------------------------------ host: the barrier and the relay

    /// <summary>Host: some comrade (or the host) still has an offer to merge or board work in hand. The ballot waits for it.</summary>
    public bool BoardSyncPending => Players.Keys.Any(BoardPendingFor);

    bool BoardPendingFor(long id)
    {
        if (_boardAwaitingOffers.ContainsKey(id)) return true;
        var st = _boardPeers.GetValueOrDefault(id);
        if (id == MyId) return BoardBusyLocal || st.Delivered > st.Acked;
        return st.Delivered > st.Acked || st.Busy;
    }

    /// <summary>Host (tests): at least <paramref name="sharing"/> comrades share, each has offered and nobody has work left.</summary>
    public bool BoardSettled(int sharing)
    {
        var on = Players.Values.Where(p => p.BoardShare).ToList();
        return on.Count >= sharing && on.All(p => _boardOffers.ContainsKey(p.Id)) && !BoardSyncPending;
    }

    /// <summary>Host (tests): at least <paramref name="sharing"/> comrades share and each has offered (the sync may still be running).</summary>
    public bool BoardSharersOffered(int sharing)
    {
        var on = Players.Values.Where(p => p.BoardShare).ToList();
        return on.Count >= sharing && on.All(p => _boardOffers.ContainsKey(p.Id));
    }

    /// <summary>The names of comrades whose board sync holds the ballot (host).</summary>
    public string BoardPendingNames => string.Join(", ", Players.Values.Where(p => BoardPendingFor(p.Id)).Select(p => p.Name));

    void HostBoardPacket(long from, Msg type, BinaryReader r)
    {
        switch (type)
        {
            case Msg.BoardConsent:
            {
                bool on = r.ReadBoolean(), deliver = r.ReadBoolean();
                HostConsent(from, on, deliver);
                return;
            }
            case Msg.BoardOffer:
            {
                int n = r.ReadInt32();
                if (n < 0 || n > LanBoardManifest.MaxBytes) throw new InvalidDataException("board offer size");
                var bytes = r.ReadBytes(n); if (bytes.Length != n) throw new EndOfStreamException();
                HostStoreOffer(from, bytes);
                return;
            }
            case Msg.BoardStatus:
            {
                uint ack = r.ReadUInt32(); bool busy = r.ReadBoolean();
                var st = _boardPeers.GetValueOrDefault(from);
                st.Acked = Math.Min(Math.Max(st.Acked, ack), st.Delivered); st.Busy = busy; _boardPeers[from] = st;
                if (Players.TryGetValue(from, out var p) && p.BoardBusy != busy) { p.BoardBusy = busy; _dirty = true; }
                return;
            }
            case Msg.BoardWant or Msg.BoardChunk or Msg.BoardAck or Msg.BoardAbort:
            {
                long target = r.ReadInt64();
                var body = r.ReadBytes((int)(r.BaseStream.Length - r.BaseStream.Position));
                if (type == Msg.BoardChunk && body.Length > BoardChunkBytes + 64) throw new InvalidDataException("board chunk size");
                if (target == from || !Players.TryGetValue(target, out var to) || !Players.TryGetValue(from, out var by)) { BoardMessagesBlocked++; return; }
                // Only between two sharing comrades, only in the lobby. An abort always goes through (it only ever stops work).
                if (type != Msg.BoardAbort && (State != Phase.Lobby || !to.BoardShare || !by.BoardShare)) { BoardMessagesBlocked++; if (type == Msg.BoardWant) BoardRefuse(from, target, body); return; }
                if (type == Msg.BoardChunk) BoardChunksRelayed++;
                if (target == 1) { using var br = new BinaryReader(new MemoryStream(body)); BoardPacket(from, type, br); return; }
                Send(target, type, w => { w.Write(from); w.Write(body); }, MultiplayerPeer.TransferModeEnum.Reliable, BoardChannel);
                return;
            }
        }
    }

    /// <summary>Host: a want that cannot be relayed gets an abort back at once (so the asker does not wait for its timeout).</summary>
    void BoardRefuse(long asker, long target, byte[] want)
    {
        try
        {
            using var br = new BinaryReader(new MemoryStream(want));
            int tid = br.ReadInt32();
            if (asker == 1) { BoardAbortIn(target, tid, "THE COMRADE DOES NOT SHARE NOW"); return; }
            Send(asker, Msg.BoardAbort, w => { w.Write(target); w.Write(tid); w.Write("THE COMRADE DOES NOT SHARE NOW"); }, MultiplayerPeer.TransferModeEnum.Reliable, BoardChannel);
        }
        catch (EndOfStreamException) { }
    }

    void ClientBoardPacket(Msg type, BinaryReader r)
    {
        switch (type)
        {
            case Msg.BoardOffer:
            {
                long origin = r.ReadInt64(); uint seq = r.ReadUInt32(); int n = r.ReadInt32();
                if (n < 0 || n > LanBoardManifest.MaxBytes) throw new InvalidDataException("board offer size");
                var bytes = r.ReadBytes(n); if (bytes.Length != n) throw new EndOfStreamException();
                ProcessOffer(origin, seq, bytes);
                return;
            }
            case Msg.BoardWant or Msg.BoardChunk or Msg.BoardAck or Msg.BoardAbort:
                BoardPacket(r.ReadInt64(), type, r);
                return;
        }
    }

    // ------------------------------------------------------------ transfers (this machine as asker or giver)

    void BoardSendTo(long to, Msg type, Action<BinaryWriter> body)
    {
        if (IsHost)
        {
            if (to == 1) return;
            if (type != Msg.BoardAbort && (!Players.TryGetValue(to, out var p) || !p.BoardShare || State != Phase.Lobby)) { BoardMessagesBlocked++; return; }
            Send(to, type, w => { w.Write(1L); body(w); }, MultiplayerPeer.TransferModeEnum.Reliable, BoardChannel);
        }
        else Send(1, type, w => { w.Write(to); body(w); }, MultiplayerPeer.TransferModeEnum.Reliable, BoardChannel);
    }

    void BoardPacket(long origin, Msg type, BinaryReader r)
    {
        switch (type)
        {
            case Msg.BoardWant:
            {
                int tid = r.ReadInt32(), epoch = r.ReadInt32(); string id = r.ReadString();
                BoardWantIn(origin, tid, epoch, id);
                return;
            }
            case Msg.BoardChunk:
            {
                int tid = r.ReadInt32(), epoch = r.ReadInt32(), index = r.ReadInt32(), count = r.ReadInt32(), total = r.ReadInt32(), n = r.ReadInt32();
                if (n < 0 || n > BoardChunkBytes) throw new InvalidDataException("board chunk length");
                var data = r.ReadBytes(n); if (data.Length != n) throw new EndOfStreamException();
                BoardChunkIn(origin, tid, epoch, index, count, total, data);
                return;
            }
            case Msg.BoardAck:
            {
                int tid = r.ReadInt32(), index = r.ReadInt32();
                var s = _boardServe.Find(x => x.To == origin && x.Tid == tid);
                if (s?.AckedParts != null && index >= 0 && index < s.Count && !s.AckedParts[index]) { s.AckedParts[index] = true; s.Acked++; s.Progress = Now; }
                return;
            }
            case Msg.BoardAbort:
            {
                int tid = r.ReadInt32(); string why = r.ReadString();
                if (why.Length > 80) why = why[..80];
                BoardAbortIn(origin, tid, why);
                return;
            }
        }
    }

    // A cancelled/disconnected recipient cannot make its disk-read worker disappear.
    // Keep it in the lobby barrier until the worker actually finishes.
    int RemoveBoardServes(Predicate<BoardServe> matches)
    {
        foreach(var s in _boardServe.FindAll(matches))
            if(s.Read is {IsCompleted:false} read) _boardTasks.Add((read,_=>{}));
        return _boardServe.RemoveAll(matches);
    }

    void BoardAbortIn(long origin, int tid, string why)
    {
        int served = RemoveBoardServes(s => s.To == origin && s.Tid == tid);
        if (served > 0) BoardLog($"transfer {tid} to {origin} aborted by the asker: {why}");
        if (_boardIn != null && _boardIn.Want.Origin == origin && _boardIn.Tid == tid)
        {
            var w = _boardIn.Want; int retries = _boardIn.Retries; _boardIn = null;
            if (why == "BUSY" && retries < BoardBusyRetries && BoardShareMine) { _boardWants.Insert(0, (w, Now + BoardBusyRetryDelay, retries + 1)); BoardLog($"transfer {tid}: the giver is busy, retry {retries + 1}"); }
            else BoardFetchFailed(w, why);
        }
        _boardStatusDirty = true;
    }

    void BoardWantIn(long asker, int tid, int epoch, string id)
    {
        string refuse = !BoardShareMine ? "NOT SHARING" : State != Phase.Lobby ? "NOT IN THE LOBBY" : epoch != RoundNumber ? "STALE REQUEST"
            : Race.Leaderboard.CleanRecordingId(id) != id || !_boardOffered.Contains(id) ? "NOT OFFERED" : BoardStore == null ? "NO RECORDS HERE NOW"
            : _boardServe.Any(s => s.To == asker && s.Tid == tid) ? "DUPLICATE REQUEST" : _boardServe.Count >= BoardMaxServing ? "BUSY" : null;
        if (refuse != null) { BoardSendTo(asker, Msg.BoardAbort, w => { w.Write(tid); w.Write(refuse); }); BoardLog($"want {tid} from {asker} for {id} refused: {refuse}"); return; }
        var s = new BoardServe { To = asker, Tid = tid, Id = id, Progress = Now };
        try { s.Read = BoardStore.ReadPayload(id); }
        catch (Exception e) { BoardSendTo(asker, Msg.BoardAbort, w => { w.Write(tid); w.Write("CANNOT READ: " + e.Message); }); return; }
        _boardServe.Add(s);
        BoardLog($"serving {id} to {asker} (transfer {tid})");
        _boardStatusDirty = true;
    }

    void BoardChunkIn(long origin, int tid, int epoch, int index, int count, int total, byte[] data)
    {
        BoardChunksIn++;
        if (State != Phase.Lobby) { BoardChunksOutsideLobby++; return; }
        var t = _boardIn;
        if (t == null || t.Want.Origin != origin || t.Tid != tid || epoch != RoundNumber || !BoardShareMine) { BoardMessagesBlocked++; return; }
        if (total <= 0 || total > LanBoardPayload.MaxBytes || total > t.Want.Bytes || count != (total + BoardChunkBytes - 1) / BoardChunkBytes || index < 0 || index >= count
            || data.Length != (index == count - 1 ? total - (count - 1) * BoardChunkBytes : BoardChunkBytes) || t.Buffer != null && (t.Buffer.Length != total || t.Count != count))
        {
            BoardSendTo(origin, Msg.BoardAbort, w => { w.Write(tid); w.Write("BAD CHUNK"); });
            _boardIn = null; BoardFetchFailed(t.Want, "THE RECORDING ARRIVED DAMAGED");
            return;
        }
        if (t.Buffer == null) { t.Buffer = new byte[total]; t.Got = new bool[count]; t.Count = count; }
        if (!t.Got[index]) { Buffer.BlockCopy(data, 0, t.Buffer, index * BoardChunkBytes, data.Length); t.Got[index] = true; t.Received++; }
        t.Progress = Now;
        BoardSendTo(origin, Msg.BoardAck, w => { w.Write(tid); w.Write(index); });
        if (t.Received < t.Count) return;
        _boardIn = null;
        var want = t.Want; var store = BoardStore; int gen = _boardGen; var payload = t.Buffer;
        BoardLog($"received {want.RecordingId} ({payload.Length / 1048576.0:F2} MB) from {origin}; verifying");
        Task<LanBoardStage> job;
        try { job = store.Stage(want, payload); }
        catch (Exception e) { BoardFetchFailed(want, e.Message); return; }
        _boardTasks.Add((job, done => BoardStaged(store, gen, want, (Task<LanBoardStage>)done)));
        _boardStatusDirty = true;
    }

    /// <summary>A verified recording is in staging: commit it now if this is still the same lobby visit and the row still wants it.</summary>
    void BoardStaged(ILanBoardStore store, int gen, LanBoardWant want, Task<LanBoardStage> job)
    {
        var stage = job.IsCompletedSuccessfully ? job.Result : new LanBoardStage { Want = want, Error = job.Exception?.GetBaseException().Message ?? "cancelled" };
        if (!stage.Ok && stage.ExistingId == null) { BoardFetchFailed(want, stage.Error); return; }
        if (gen != _boardGen || !BoardShareMine || State != Phase.Lobby || store != BoardStore || !Active)
        { store.Discard(stage); BoardLog($"{want.RecordingId}: verified but not kept (the sync was stopped)"); return; }
        if (!store.StillWanted(want)) { store.Discard(stage); _boardSkipped++; _boardDone++; BoardLog($"{want.RecordingId}: no longer needed (its result left the board)"); return; }
        string err = store.Commit(stage);
        if (err != null) { BoardFetchFailed(want, err); return; }
        _boardDone++; BoardRecordingsImported++;
        BoardLog($"imported {want.RecordingId} for {want.Track} set {want.Set} as {stage.LocalId ?? stage.ExistingId}");
        if (_boardWants.Count == 0 && _boardIn == null) BoardSay($"BOARDS SYNCED: {_boardDone - _boardSkipped} RECORDINGS RECEIVED{(_boardFailed > 0 ? $", {_boardFailed} FAILED" : "")}", _boardFailed > 0);
    }

    void BoardFetchFailed(LanBoardWant w, string why)
    {
        _boardFailed++; _boardDone++; BoardRecordingsFailed++;
        _boardWanted.Remove(w.Track + "|" + w.Set + "|" + w.RowKey);
        BoardSay("RECORDING NOT RECEIVED: " + (why ?? "?").ToUpperInvariant(), true);
        _boardStatusDirty = true;
    }

    /// <summary>Stops everything this machine does for board sync (withdraw, cancel, leaving the lobby, a closed session). Worker jobs finish and their results are thrown away.</summary>
    void BoardStopLocal(string why, bool bad)
    {
        _boardGen++;
        if (_boardIn != null) { var t = _boardIn; _boardIn = null; if (Active) BoardSendTo(t.Want.Origin, Msg.BoardAbort, w => { w.Write(t.Tid); w.Write("CANCELLED"); }); }
        if (Active) foreach (var s in _boardServe) BoardSendTo(s.To, Msg.BoardAbort, w => { w.Write(s.Tid); w.Write("CANCELLED"); });
        foreach (var s in _boardServe) if (s.Read is { IsCompleted: false } read) _boardTasks.Add((read, _ => { }));
        _boardServe.Clear();
        int dropped = _boardWants.Count;
        _boardWants.Clear(); _boardWanted.Clear();
        _boardStatusDirty = true;
        if (why != null) BoardSay(why + (dropped > 0 ? $" ({dropped} RECORDINGS NOT FETCHED)" : ""), bad);
    }

    /// <summary>A comrade left: its transfers and the recordings wanted from it go.</summary>
    void BoardPeerGone(long id)
    {
        _boardOffers.Remove(id); _boardPeers.Remove(id); _boardAwaitingOffers.Remove(id);
        RemoveBoardServes(s => s.To == id);
        int dropped = _boardWants.RemoveAll(w => w.Want.Origin == id);
        if (_boardIn?.Want.Origin == id) { var w = _boardIn.Want; _boardIn = null; BoardFetchFailed(w, "THE COMRADE LEFT"); }
        if (dropped > 0) { _boardDone += dropped; _boardFailed += dropped; _boardWanted.Clear(); foreach (var w in _boardWants) _boardWanted.Add(w.Want.Track + "|" + w.Want.Set + "|" + w.Want.RowKey); BoardSay($"A COMRADE LEFT: {dropped} RECORDINGS NOT FETCHED", true); }
        _boardStatusDirty = true;
    }

    /// <summary>The lobby is over (the ballot opened, a track loads): nothing more moves.</summary>
    void BoardLeftLobby()
    {
        if (BoardBusyLocal) BoardLog($"left the lobby with board work in hand: stopping it (wants {_boardWants.Count}, in {(_boardIn != null ? 1 : 0)}, serving {_boardServe.Count}, jobs {_boardTasks.Count})");
        BoardStopLocal(_boardWants.Count > 0 || _boardIn != null || _boardServe.Count > 0 ? "BOARD SYNC STOPPED: THE BALLOT OPENED" : null, true);
        BoardLog($"stats on leaving the lobby: chunks sent {BoardChunksSent}, relayed {BoardChunksRelayed}, in {BoardChunksIn}, outside the lobby {BoardChunksOutsideLobby}, rows added {BoardRowsAdded}, recordings imported {BoardRecordingsImported}, failed {BoardRecordingsFailed}");
    }

    void ResetBoards()
    {
        BoardStopLocal(null, false);
        BoardShareMine = false;
        _boardOffers.Clear(); _boardPeers.Clear(); _boardAwaitingOffers.Clear(); _boardOffered.Clear(); _boardKnown.Clear();
        _boardDeliverSeq = _boardAckSeq = _boardAckSent = 0; _boardBusySent = false; _boardStatusDirty = false;
        _boardTotal = _boardDone = _boardFailed = _boardSkipped = 0;
        BoardNote = null; BoardNoteAt = -1;
        BoardSessionStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
    }

    // ------------------------------------------------------------ per frame

    /// <summary>Main thread, every frame (also when no session is open: worker jobs of a closed session are finished and discarded).</summary>
    void BoardFrame()
    {
        for (int i = _boardTasks.Count - 1; i >= 0; i--)
        {
            var (task, done) = _boardTasks[i];
            if (!task.IsCompleted) continue;
            _boardTasks.RemoveAt(i);
            try { done(task); } catch (Exception e) { GD.PushWarning("lan boards: " + e.Message); }
            _boardStatusDirty = true;
        }
        if (!Active) return;
        double now = Now;
        if (IsHost) foreach (var pending in _boardAwaitingOffers.ToArray())
            if (now-pending.Value>BoardTimeout) {
                _boardAwaitingOffers.Remove(pending.Key);
                BoardSay("NO BOARD OFFER FROM " + (Players.GetValueOrDefault(pending.Key)?.Name ?? "A COMRADE") + " - RETRY IN THE LOBBY",true);
            }
        if (State == Phase.Lobby && BoardShareMine)
        {
            // the next recording: one download at a time, only while its row still wants it
            if (_boardIn == null && _boardTasks.Count == 0)
                for (int i = 0; i < _boardWants.Count && _boardIn == null; i++)
                {
                    var (w, notBefore, retries) = _boardWants[i];
                    if (notBefore > now) continue;
                    _boardWants.RemoveAt(i--);
                    if (!Players.ContainsKey(w.Origin)) { BoardFetchFailed(w, "THE COMRADE LEFT"); continue; }
                    if (BoardStore == null || !BoardStore.StillWanted(w)) { _boardDone++; _boardSkipped++; _boardWanted.Remove(w.Track + "|" + w.Set + "|" + w.RowKey); continue; }
                    int tid = (int)(GD.Randi() & 0x7fffffff), epoch = RoundNumber;
                    _boardIn = new BoardIncoming { Want = w, Tid = tid, Retries = retries, Progress = now };
                    BoardSendTo(w.Origin, Msg.BoardWant, x => { x.Write(tid); x.Write(epoch); x.Write(w.RecordingId); });
                    BoardLog($"asking {w.Origin} for {w.RecordingId} ({w.Track} set {w.Set}, about {w.Bytes / 1048576.0:F1} MB), transfer {tid}");
                    _boardStatusDirty = true;
                }
            if (_boardIn != null && now - _boardIn.Progress > BoardTimeout)
            {
                var t = _boardIn; _boardIn = null;
                BoardSendTo(t.Want.Origin, Msg.BoardAbort, w => { w.Write(t.Tid); w.Write("TIMED OUT"); });
                BoardFetchFailed(t.Want, "TIMED OUT");
            }
            // giving: a few chunks a frame, at most a small window unacknowledged per transfer
            int budget = BoardChunksPerFrame;
            for (int i = _boardServe.Count - 1; i >= 0; i--)
            {
                var s = _boardServe[i];
                if (s.Payload == null)
                {
                    if (!s.Read.IsCompleted) continue;
                    if (!s.Read.IsCompletedSuccessfully)
                    {
                        string why = "CANNOT READ: " + (s.Read.Exception?.GetBaseException().Message ?? "?");
                        BoardSendTo(s.To, Msg.BoardAbort, w => { w.Write(s.Tid); w.Write(why.Length > 80 ? why[..80] : why); });
                        _boardServe.RemoveAt(i); BoardSay("A RECORDING COULD NOT BE SENT: " + why, true); continue;
                    }
                    s.Payload = s.Read.Result; s.Count = (s.Payload.Length + BoardChunkBytes - 1) / BoardChunkBytes; s.AckedParts = new bool[s.Count]; s.Progress = now;
                }
                if (s.Acked >= s.Count) { _boardServe.RemoveAt(i); BoardLog($"sent {s.Id} to {s.To} ({s.Payload.Length / 1048576.0:F2} MB)"); _boardStatusDirty = true; continue; }
                if (now - s.Progress > BoardTimeout)
                {
                    BoardSendTo(s.To, Msg.BoardAbort, w => { w.Write(s.Tid); w.Write("TIMED OUT"); });
                    _boardServe.RemoveAt(i); BoardSay("A RECORDING COULD NOT BE SENT: TIMED OUT", true); continue;
                }
                while (budget > 0 && s.Next < s.Count && s.Next - s.Acked < BoardWindow)
                {
                    int index = s.Next++, off = index * BoardChunkBytes, len = Math.Min(BoardChunkBytes, s.Payload.Length - off), epoch = RoundNumber;
                    BoardSendTo(s.To, Msg.BoardChunk, w => { w.Write(s.Tid); w.Write(epoch); w.Write(index); w.Write(s.Count); w.Write(s.Payload.Length); w.Write(len); w.Write(s.Payload, off, len); });
                    BoardChunksSent++; budget--;
                }
            }
        }
        else if (BoardBusyLocal && (_boardWants.Count > 0 || _boardIn != null || _boardServe.Count > 0))
            BoardStopLocal(State == Phase.Lobby ? null : "BOARD SYNC STOPPED: THE BALLOT OPENED", State != Phase.Lobby);
        bool busy = BoardBusyLocal;
        if (IsHost)
        {
            var st = _boardPeers.GetValueOrDefault(1); st.Busy = busy; _boardPeers[1] = st;
            if (Players.TryGetValue(1, out var me) && me.BoardBusy != busy) { me.BoardBusy = busy; _dirty = true; }
        }
        else if (InLobby && (_boardStatusDirty || busy != _boardBusySent || _boardAckSeq != _boardAckSent))
        {
            _boardStatusDirty = false; _boardBusySent = busy; _boardAckSent = _boardAckSeq;
            Send(1, Msg.BoardStatus, w => { w.Write(_boardAckSeq); w.Write(busy); });
        }
    }
}
