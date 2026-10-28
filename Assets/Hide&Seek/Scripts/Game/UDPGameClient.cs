using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace HideAndSeek
{
    public class UDPGameClient : MonoBehaviour
    {
        public string serverIp;
        public int port;
        public string playerName;

        public bool Connected => m_running;
        public int AssignedNetId { get; private set; } = -1;
        public bool IsWelcomed => AssignedNetId >= 0;

        public event Action<int> OnWelcomed;
        public event Action<WorldSnapshot> OnSnapshot;

        const int MaxPacketSize = 64 * 1024;

        Socket m_socket;
        EndPoint m_server;
        Thread m_thread;
        volatile bool m_running;

        readonly ConcurrentQueue<byte[]> m_inbox = new ConcurrentQueue<byte[]>();

        float m_lastHelloSent;
        float m_lastHeard;
        public float timeoutSeconds = 6f;

        public void StartClient(string ip, int gamePort, string name)
{
    StopClient();

    serverIp = ip;
    port = gamePort;
    playerName = string.IsNullOrWhiteSpace(name) ? "Player" : name.Trim();

    try { m_server = new IPEndPoint(IPAddress.Parse(serverIp), port); }
    catch { Debug.LogError("[GameClient] Invalid IP " + serverIp); return; }

    try
    {
        m_socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        m_socket.Bind(new IPEndPoint(IPAddress.Any, 0));   // local port first, so ReceiveFrom works
    }
    catch (SocketException e)
    {
        Debug.LogError("[GameClient] Could not create socket: " + e.SocketErrorCode);
        return;
    }

    m_running = true;
    m_lastHeard = Time.realtimeSinceStartup;

    m_thread = new Thread(ReceiveLoop) { IsBackground = true };
    m_thread.Start();

    SendHello();
}

        public void StopClient()
        {
            m_running = false;
            Socket s = m_socket;
            m_socket = null;
            if (s != null) { try { s.Close(); } catch { } }
            if (m_thread != null) m_thread.Join(400);
            m_thread = null;
            AssignedNetId = -1;
        }

        void OnDestroy() => StopClient();

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
                m_inbox.Enqueue(payload);
            }
        }

        void Update()
        {
            while (m_inbox.TryDequeue(out byte[] data))
            {
                m_lastHeard = Time.realtimeSinceStartup;
                try
                {
                    GameMsgType type = GameMessages.ReadType(data);
                    if (type == GameMsgType.Welcome)
                    {
                        int id = GameMessages.ReadWelcome(data);
                        if (AssignedNetId != id)
                        {
                            AssignedNetId = id;
                            OnWelcomed?.Invoke(id);
                        }
                    }
                    else if (type == GameMsgType.Snapshot)
                    {
                        WorldSnapshot snap = GameMessages.ReadSnapshot(data);
                        OnSnapshot?.Invoke(snap);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[GameClient] Bad packet: " + ex.Message);
                }
            }

            if (!m_running) return;

            float now = Time.realtimeSinceStartup;
            if (!IsWelcomed && now - m_lastHelloSent >= 1f) SendHello();

            if (now - m_lastHeard > timeoutSeconds)
            {
                Debug.LogWarning("[GameClient] Timeout");
                StopClient();
            }
        }

        public void SendInput(InputCommand input)
        {
            Socket s = m_socket;
            if (!m_running || s == null) return;

            byte[] msg = GameMessages.MakeInput(input);
            try { s.SendTo(msg, m_server); } catch { }
        }

        void SendHello()
        {
            m_lastHelloSent = Time.realtimeSinceStartup;
            Socket s = m_socket;
            if (!m_running || s == null) return;
            byte[] msg = GameMessages.MakeHello(playerName);
            try { s.SendTo(msg, m_server); } catch { }
        }
    }
}