using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

// Player-hosted multiplayer transport: one player's machine IS the server
// (a LAN listen server), everyone else joins that player's IP — distributed
// hosting, no central infrastructure to run or pay for.
//
// Plain TCP with 4-byte length-prefixed UTF-8 frames, pumped from the main
// thread (no threads, no allocity surprises, easy to test headless).
// Protocol (host id is 0; the host relays client messages to everyone else):
//   W|id|hostShipCode     welcome: host assigns the new client its id
//   H|id|shipCode         a player announces (or changes) their ship design
//   S|id|zone|px,py,pz|rx,ry,rz,rw|vx,vy,vz   position state at 10 Hz
// Sync scope (beta): crew presence, designs, and flight state. Combat and
// NPCs stay local to each machine.
public class NetSession
{
    public const int Port = 7777;
    public const int MaxPlayers = 4;

    public class Remote
    {
        public int id;
        public string shipCode = "", zone = "";
        public float px, py, pz, rx, ry, rz, vx, vy, vz;
        public float rw = 1f;
        public bool hasState;
        public double lastRx;
    }

    class Peer
    {
        public TcpClient tcp;
        public NetworkStream stream;
        public byte[] buf = new byte[131072];
        public int have;
        public int id;
        public bool dead;
    }

    public bool IsHost { get; private set; }
    public bool Active { get; private set; }
    public int LocalId { get; private set; } = -1;
    public string Status { get; private set; } = "";
    public readonly Dictionary<int, Remote> Remotes = new Dictionary<int, Remote>();

    TcpListener listener;
    readonly List<Peer> peers = new List<Peer>(); // host: its clients; client: just the host
    string localShipCode = "";
    int nextId = 1;
    double lastStateSent;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public int PeerCount
    {
        get { int n = 0; foreach (var p in peers) if (!p.dead) n++; return n; }
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public bool StartHost(int port = Port)
    {
        try
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            IsHost = true;
            Active = true;
            LocalId = 0;
            Status = "Hosting on port " + port + " — waiting for crew";
            return true;
        }
        catch (Exception e) { Status = "Host failed: " + e.Message; return false; }
    }

    public bool Join(string ip, int port = Port)
    {
        try
        {
            var tcp = new TcpClient();
            var ar = tcp.BeginConnect(ip, port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(3000))
            {
                tcp.Close();
                Status = "No host answered at " + ip;
                return false;
            }
            tcp.EndConnect(ar);
            tcp.NoDelay = true;
            peers.Add(new Peer { tcp = tcp, stream = tcp.GetStream(), id = 0 });
            IsHost = false;
            Active = true;
            Status = "Connected — waiting for welcome";
            return true;
        }
        catch (Exception e) { Status = "Join failed: " + e.Message; return false; }
    }

    public void Shutdown()
    {
        foreach (var p in peers) { try { p.tcp.Close(); } catch { } }
        peers.Clear();
        try { if (listener != null) listener.Stop(); } catch { }
        listener = null;
        Remotes.Clear();
        Active = false;
        IsHost = false;
        LocalId = -1;
        Status = "Disconnected";
    }

    public void SetLocalShipCode(string code)
    {
        localShipCode = code ?? "";
        if (Active && LocalId >= 0)
            Broadcast("H|" + LocalId + "|" + localShipCode, null);
    }

    // ── Main-thread pump ─────────────────────────────────────────────────────

    // state: px,py,pz, rx,ry,rz,rw, vx,vy,vz — or null while not flying.
    public void Pump(double now, string zone, float[] state)
    {
        if (!Active) return;

        if (IsHost && listener != null)
        {
            while (listener.Pending() && PeerCount < MaxPlayers - 1)
            {
                var tcp = listener.AcceptTcpClient();
                tcp.NoDelay = true;
                var p = new Peer { tcp = tcp, stream = tcp.GetStream(), id = nextId++ };
                peers.Add(p);
                Remotes[p.id] = new Remote { id = p.id, lastRx = now };
                Send(p, "W|" + p.id + "|" + localShipCode);
                foreach (var kv in Remotes) // introduce the rest of the crew
                    if (kv.Key != p.id && kv.Value.shipCode.Length > 0)
                        Send(p, "H|" + kv.Key + "|" + kv.Value.shipCode);
                Status = "Hosting — " + (PeerCount + 1) + " aboard";
            }
        }

        foreach (var p in peers) PumpPeer(p, now);

        for (int i = peers.Count - 1; i >= 0; i--)
        {
            if (!peers[i].dead) continue;
            if (IsHost) Remotes.Remove(peers[i].id);
            else { Status = "Lost the host"; Shutdown(); return; }
            peers.RemoveAt(i);
            if (IsHost) Status = "Hosting — " + (PeerCount + 1) + " aboard";
        }

        if (state != null && LocalId >= 0 && now - lastStateSent > 0.1)
        {
            lastStateSent = now;
            var sb = new StringBuilder("S|").Append(LocalId).Append('|').Append(zone).Append('|');
            sb.Append(state[0].ToString("F2", Inv)).Append(',')
              .Append(state[1].ToString("F2", Inv)).Append(',')
              .Append(state[2].ToString("F2", Inv)).Append('|');
            sb.Append(state[3].ToString("F4", Inv)).Append(',')
              .Append(state[4].ToString("F4", Inv)).Append(',')
              .Append(state[5].ToString("F4", Inv)).Append(',')
              .Append(state[6].ToString("F4", Inv)).Append('|');
            sb.Append(state[7].ToString("F2", Inv)).Append(',')
              .Append(state[8].ToString("F2", Inv)).Append(',')
              .Append(state[9].ToString("F2", Inv));
            Broadcast(sb.ToString(), null);
        }
    }

    void PumpPeer(Peer p, double now)
    {
        if (p.dead) return;
        try
        {
            while (p.stream.DataAvailable && p.have < p.buf.Length)
            {
                int n = p.stream.Read(p.buf, p.have, p.buf.Length - p.have);
                if (n <= 0) { p.dead = true; return; }
                p.have += n;
            }
        }
        catch { p.dead = true; return; }

        while (p.have >= 4)
        {
            int len = p.buf[0] | (p.buf[1] << 8) | (p.buf[2] << 16) | (p.buf[3] << 24);
            if (len < 1 || len > p.buf.Length - 4) { p.dead = true; return; }
            if (p.have < 4 + len) break;
            string msg = Encoding.UTF8.GetString(p.buf, 4, len);
            Buffer.BlockCopy(p.buf, 4 + len, p.buf, 0, p.have - 4 - len);
            p.have -= 4 + len;
            Handle(msg, p, now);
        }
    }

    void Handle(string msg, Peer from, double now)
    {
        string[] f = msg.Split('|');
        if (f.Length < 2) return;

        if (f[0] == "W" && !IsHost && f.Length >= 3)
        {
            int id;
            if (!int.TryParse(f[1], out id)) return;
            LocalId = id;
            Touch(0, now).shipCode = f[2];
            Status = "Aboard as player " + (id + 1);
            if (localShipCode.Length > 0)
                Broadcast("H|" + LocalId + "|" + localShipCode, null);
            return;
        }

        int senderId;
        if (!int.TryParse(f[1], out senderId)) return;
        if (IsHost && senderId != from.id) return; // clients only speak for themselves

        if (f[0] == "H" && f.Length >= 3)
        {
            Touch(senderId, now).shipCode = f[2];
            if (IsHost) Broadcast(msg, from);
        }
        else if (f[0] == "S" && f.Length >= 6)
        {
            var r = Touch(senderId, now);
            r.zone = f[2];
            string[] pp = f[3].Split(',');
            string[] rr = f[4].Split(',');
            string[] vv = f[5].Split(',');
            if (pp.Length == 3 && rr.Length == 4 && vv.Length == 3)
            {
                float.TryParse(pp[0], NumberStyles.Float, Inv, out r.px);
                float.TryParse(pp[1], NumberStyles.Float, Inv, out r.py);
                float.TryParse(pp[2], NumberStyles.Float, Inv, out r.pz);
                float.TryParse(rr[0], NumberStyles.Float, Inv, out r.rx);
                float.TryParse(rr[1], NumberStyles.Float, Inv, out r.ry);
                float.TryParse(rr[2], NumberStyles.Float, Inv, out r.rz);
                float.TryParse(rr[3], NumberStyles.Float, Inv, out r.rw);
                float.TryParse(vv[0], NumberStyles.Float, Inv, out r.vx);
                float.TryParse(vv[1], NumberStyles.Float, Inv, out r.vy);
                float.TryParse(vv[2], NumberStyles.Float, Inv, out r.vz);
                r.hasState = true;
            }
            if (IsHost) Broadcast(msg, from);
        }
    }

    Remote Touch(int id, double now)
    {
        Remote r;
        if (!Remotes.TryGetValue(id, out r))
        {
            r = new Remote { id = id };
            Remotes[id] = r;
        }
        r.lastRx = now;
        return r;
    }

    void Broadcast(string msg, Peer except)
    {
        foreach (var p in peers)
            if (p != except && !p.dead) Send(p, msg);
    }

    void Send(Peer p, string msg)
    {
        try
        {
            byte[] body = Encoding.UTF8.GetBytes(msg);
            byte[] frame = new byte[4 + body.Length];
            frame[0] = (byte)body.Length;
            frame[1] = (byte)(body.Length >> 8);
            frame[2] = (byte)(body.Length >> 16);
            frame[3] = (byte)(body.Length >> 24);
            Buffer.BlockCopy(body, 0, frame, 4, body.Length);
            p.stream.Write(frame, 0, frame.Length);
        }
        catch { p.dead = true; }
    }
}
