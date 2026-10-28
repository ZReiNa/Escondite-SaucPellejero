using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace HideAndSeek
{
    public class UDPGameServer : MonoBehaviour
    {
        public int port = 9051;
        public float timeoutSeconds = 6f;

        public bool IsRunning => m_running;
        public GameServerSimulation Sim { get; private set; }

        const int MaxPacketSize = 64 * 1024;
        const float TickRate = 30f;
        const float SnapshotRate = 20f;

        class GamePeer
        {
            public EndPoint endpoint;
            public int netId;
            public float lastSeen;
        }

        struct NetPacket { public EndPoint from; public byte[] data; }

        Socket m_socket;
        Thread m_thread;
        volatile bool m_running;

        readonly ConcurrentQueue<NetPacket> m_inbox = new ConcurrentQueue<NetPacket>();
        readonly List<GamePeer> m_peers = new List<GamePeer>();
        int m_nextNetId = 1;
        float m_nextTick;
        float m_nextSnapshot;

        public bool StartServer(int gamePort, int expectedPlayers)
        {
            StopServer();
            port = gamePort;

            try
            {
                m_socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                m_socket.Bind(new IPEndPoint(IPAddress.Any, port));
            }
            catch (Exception ex)
            {
                Debug.LogError("[GameServer] Start failed: " + ex.Message);
                try { m_socket?.Close(); } catch { }
                m_socket = null;
                return false;
            }

            Sim = new GameServerSimulation();
            Sim.Initialize(expectedPlayers);
            m_nextTick = m_nextSnapshot = Time.realtimeSinceStartup;

            m_running = true;
            m_thread = new Thread(ReceiveLoop) { IsBackground = true };
            m_thread.Start();
            return true;
        }

        public void StopServer()
        {
            m_running = false;
            Socket s = m_socket;
            m_socket = null;
            if (s != null) { try { s.Close(); } catch { } }
            if (m_thread != null) m_thread.Join(400);
            m_thread = null;
            m_peers.Clear();
            Sim = null;
        }

        void OnDestroy() => StopServer();

        void ReceiveLoop()
        {
            byte[] buffer = new byte[MaxPacketSize];
            while (m_running)
            {
                Socket s = m_socket;
                if (s == null) return;

                EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                int received;
                try { received = s.ReceiveFrom(buffer, ref from); }
                catch (SocketException e)
                {
                    if (e.SocketErrorCode == SocketError.ConnectionReset) continue;
                    return;
                }
                catch { return; }

                byte[] payload = new byte[received];
                Buffer.BlockCopy(buffer, 0, payload, 0, received);
                m_inbox.Enqueue(new NetPacket { from = from, data = payload });
            }
        }

        void Update()
        {
            if (!m_running || Sim == null) return;

            while (m_inbox.TryDequeue(out NetPacket packet))
                ProcessPacket(packet.from, packet.data);

            float now = Time.realtimeSinceStartup;

            for (int i = m_peers.Count - 1; i >= 0; i--)
            {
                if (now - m_peers[i].lastSeen <= timeoutSeconds) continue;
                Sim.RemovePlayer(m_peers[i].netId);
                m_peers.RemoveAt(i);
            }

            float tickDt = 1f / TickRate;
            int steps = 0;
            while (now >= m_nextTick && steps < 5)
            {
                m_nextTick += tickDt;
                Sim.Step(tickDt);
                steps++;
            }
            if (now >= m_nextTick) m_nextTick = now + tickDt;

            if (now >= m_nextSnapshot)
            {
                m_nextSnapshot = now + 1f / SnapshotRate;
                byte[] msg = GameMessages.MakeSnapshot(Sim.BuildSnapshot());
                for (int i = 0; i < m_peers.Count; i++) SendTo(m_peers[i].endpoint, msg);
            }
        }

        void ProcessPacket(EndPoint from, byte[] data)
        {
            GameMsgType type;
            try { type = GameMessages.ReadType(data); }
            catch { return; }

            GamePeer peer = FindPeer(from);

            if (type == GameMsgType.Hello)
            {
                if (peer == null)
                {
                    string name;
                    try { name = GameMessages.ReadHello(data); }
                    catch { return; }

                    peer = new GamePeer { endpoint = from, netId = m_nextNetId++ };
                    m_peers.Add(peer);
                    Sim.RegisterPeer(peer.netId, name);
                }
                peer.lastSeen = Time.realtimeSinceStartup;
                SendTo(peer.endpoint, GameMessages.MakeWelcome(peer.netId));
                return;
            }

            if (peer == null) return;
            peer.lastSeen = Time.realtimeSinceStartup;

            if (type == GameMsgType.Input)
            {
                try { Sim.SetInput(peer.netId, GameMessages.ReadInput(data)); }
                catch { }
            }
        }

        void SendTo(EndPoint to, byte[] msg)
        {
            Socket s = m_socket;
            if (s == null) return;
            try { s.SendTo(msg, to); } catch { }
        }

        GamePeer FindPeer(EndPoint ep)
        {
            for (int i = 0; i < m_peers.Count; i++)
                if (m_peers[i].endpoint.Equals(ep)) return m_peers[i];
            return null;
        }

        public void RestartRound() { if (Sim != null) Sim.RestartRound(); }
    }
}