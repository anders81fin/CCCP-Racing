using System;
using System.Collections.Generic;
using System.Text;
using Godot;

namespace CCCPRacing.Net;

/// <summary>
/// Finding games on the LAN: a host shouts a small UDP beacon to the whole network every second
/// (game, host name, players, version, track signature, port); the join screen listens and lists
/// what it hears, forgetting a game that has been silent for 3 s. The beacon also goes to the
/// loopback address, so a game on this very machine is found even where a firewall drops broadcasts.
/// </summary>
public partial class LanDiscovery : Node
{
    public const int Port = 24106;
    const string Magic = "CCCPLAN1";
    const float Every = 1f, Forget = 3f;

    public class Game
    {
        public string Ip, Host, Version, Signature;
        public int Players, Max, GamePort;
        public double Seen;
    }

    PacketPeerUdp _udp;
    bool _beacon;
    float _acc;
    Func<string> _payload;
    /// <summary>Games heard, oldest first.</summary>
    public readonly List<Game> Games = new();
    /// <summary>Listening failed (the port is taken, e.g. another game on this machine is listening).</summary>
    public bool Deaf { get; private set; }

    /// <summary>Host: shout (host, players, max, version, signature, port) every second.</summary>
    public void StartBeacon(Func<(string host, int players, int max, string version, string sig, int port)> info)
    {
        Stop();
        _udp = new PacketPeerUdp();
        _udp.SetBroadcastEnabled(true);
        _beacon = true;
        _acc = Every;
        _payload = () =>
        {
            var i = info();
            return string.Join("|", Magic, Clean(i.host), i.players, i.max, i.version, i.sig, i.port);
        };
    }

    /// <summary>Join screen: listen for beacons.</summary>
    public void StartListening()
    {
        Stop();
        _udp = new PacketPeerUdp();
        Deaf = _udp.Bind(Port, "*") != Error.Ok;
        if (Deaf) { _udp = null; GD.Print($"lan: cannot listen for games on UDP {Port} (taken?)"); }
    }

    public void Stop()
    {
        _udp?.Close();
        _udp = null;
        _beacon = false;
        Games.Clear();
    }

    public override void _Process(double delta)
    {
        if (_udp == null) return;
        if (_beacon)
        {
            _acc += (float)delta;
            if (_acc < Every) return;
            _acc = 0;
            byte[] b = Encoding.UTF8.GetBytes(_payload());
            foreach (string to in new[] { "255.255.255.255", "127.0.0.1" })
            {
                _udp.SetDestAddress(to, Port);
                _udp.PutPacket(b);
            }
            return;
        }
        double now = LanSession.Now;
        while (_udp.GetAvailablePacketCount() > 0)
        {
            byte[] data = _udp.GetPacket();
            string ip = _udp.GetPacketIP();
            string[] f = Encoding.UTF8.GetString(data).Split('|');
            if (f.Length < 7 || f[0] != Magic || !int.TryParse(f[2], out int players) || !int.TryParse(f[3], out int max)
                || !int.TryParse(f[6], out int port)) continue;
            // The same game heard over the network and the loopback: one row, the network address.
            bool local = ip.StartsWith("127.");
            var g = Games.Find(x => x.GamePort == port && x.Host == f[1] && x.Signature == f[5] && (x.Ip == ip || local || x.Ip.StartsWith("127.")));
            if (g == null) Games.Add(g = new Game { Ip = ip, GamePort = port });
            else if (!local) g.Ip = ip;
            g.Host = f[1];
            g.Players = players;
            g.Max = max;
            g.Version = f[4];
            g.Signature = f[5];
            g.Seen = now;
        }
        Games.RemoveAll(x => now - x.Seen > Forget);
    }

    public override void _ExitTree() => Stop();

    static string Clean(string s) => s.Replace("|", " ");
}
