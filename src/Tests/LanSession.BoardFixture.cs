using System;
using System.Collections.Generic;
using System.Linq;
namespace CCCPRacing.Net;

// SHARE BOARDS fixtures: an in-process "network" of sessions (host id 1, clients 2..n) whose packets go through the production
// receive handlers (OnPacket) via the TestWire seam. Real ENet peers and scene reloads are covered by tools/net/lan_smoke.py.
public partial class LanSession
{
    internal sealed class BoardNet
    {
        public readonly SortedDictionary<long, LanSession> Peers = new();
        public readonly Queue<(long From, long To, byte[] Bytes, int Channel)> Wire = new();
        public int Packets, Bytes, MaxPacket;
        /// <summary>Optional filter: false drops the packet (tests of loss / forged packets).</summary>
        public Func<long, long, byte[], bool> Filter = null;

        public LanSession Host => Peers[1];

        public BoardNet(params string[] names)
        {
            for (int i = 0; i < names.Length; i++) Add(i + 1, names[i]);
        }

        public LanSession Add(long id, string name)
        {
            var s = new LanSession { IsHost = id == 1, InLobby = true, State = Phase.Lobby, MyName = name };
            if (id != 1) s._myId = id;
            long me = id;
            s.TestWire = (to, bytes, ch) => { Packets++; Bytes += bytes.Length; MaxPacket = Math.Max(MaxPacket, bytes.Length); Wire.Enqueue((me, me == 1 ? to : 1, bytes, ch)); };
            Peers[id] = s;
            // every session sees the same roster (the host's lobby would send it)
            foreach (var p in Peers.Values)
                foreach (var (pid, pv) in Peers)
                    if (!p.Players.ContainsKey(pid)) p.Players[pid] = new LanPlayer { Id = pid, Name = pv.MyName, Slot = (int)pid - 1 };
            return s;
        }

        /// <summary>The comrade leaves: the host notices the disconnect and tells the others (as ENet + Msg.Left do).</summary>
        public void Leave(long id)
        {
            Peers.Remove(id);
            Host.OnPeerDisconnected(id);      // (broadcasts Msg.Left to the others through the wire)
        }

        /// <summary>Delivers packets and runs every session's frame until nothing moves (worker jobs are waited for), or the step limit.</summary>
        public int Pump(int maxSteps = 200000, Func<bool> until = null, double seconds = 60)
        {
            int steps = 0;
            var end = DateTime.UtcNow.AddSeconds(seconds);
            for (; steps < maxSteps && DateTime.UtcNow < end; steps++)
            {
                if (until != null && until()) break;
                bool moved = false;
                int n = Wire.Count;
                for (int i = 0; i < n; i++)
                {
                    var (from, to, bytes, _) = Wire.Dequeue();
                    if (Filter != null && !Filter(from, to, bytes)) continue;
                    if (Peers.TryGetValue(to, out var dest)) dest.OnPacket(from, bytes);
                    moved = true;
                }
                bool busy = false;
                foreach (var p in Peers.Values.ToList()) { p.BoardFrame(); busy |= p._boardTasks.Count > 0 || p._boardServe.Any(s => s.Payload == null); }
                if (!moved && Wire.Count == 0 && !busy && Peers.Values.All(p => !p.BoardBusyLocal)) break;
                if (!moved && (busy || Peers.Values.Any(p => p.BoardBusyLocal))) System.Threading.Thread.Sleep(2);      // a worker job, or a retry waiting for its time
            }
            return steps;
        }
    }

    internal void ClientLeftFixture(long id)
    {
        using var m = new System.IO.MemoryStream(); using (var w = new System.IO.BinaryWriter(m, System.Text.Encoding.UTF8, true)) { w.Write((byte)Msg.Left); w.Write(id); w.Write("GONE"); }
        OnPacket(1, m.ToArray());
    }

    /// <summary>A forged chunk / want as a client would send it to the host (target, then body).</summary>
    internal static byte[] BoardChunkFixture(long target, int tid, int epoch, int index, int count, int total, byte[] data)
    {
        using var m = new System.IO.MemoryStream(); using (var w = new System.IO.BinaryWriter(m, System.Text.Encoding.UTF8, true))
        { w.Write((byte)Msg.BoardChunk); w.Write(target); w.Write(tid); w.Write(epoch); w.Write(index); w.Write(count); w.Write(total); w.Write(data.Length); w.Write(data); }
        return m.ToArray();
    }
    internal int BoardIncomingTid => _boardIn?.Tid ?? -1;
    internal long BoardIncomingOrigin => _boardIn?.Want.Origin ?? -1;
    internal int BoardWantsQueued => _boardWants.Count;
    internal int BoardServing => _boardServe.Count;
    internal void BoardFixtureState(Phase state) => State = state;
    internal static bool IsBoardChunk(byte[] b) => b.Length > 0 && b[0] == (byte)Msg.BoardChunk;
    internal static bool IsBoardOffer(byte[] b) => b.Length > 0 && b[0] == (byte)Msg.BoardOffer;
    internal void BoardFixtureClose() => Close();
}
