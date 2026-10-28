using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace HideAndSeek
{
    public class UDPLobbyClient : LobbyClient
    {
        public float timeoutSeconds = 5f;
        const int MaxPacketSize = 64 * 1024;

        Socket m_socket;
        EndPoint m_server;
        Thread m_thread;
        volatile bool m_running;
        float m_lastJoin;

        public override void Connect()
        {
            try { m_server = new IPEndPoint(IPAddress.Parse(serverIp), port); }
            catch (FormatException)
            {
                m_endReason = "Invalid IP: " + serverIp;
                m_inbox.Enqueue(null);
                return;
            }

            m_socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            m_running = true;
            m_lastHeard = m_lastJoin = Time.realtimeSinceStartup;

            SendBytes(JoinMessage());
            m_thread = new Thread(ReceiveLoop) { IsBackground = true };
            m_thread.Start();
        }

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
                catch (ObjectDisposedException) { return; }

                byte[] payload = new byte[received];
                Buffer.BlockCopy(buffer, 0, payload, 0, received);
                m_inbox.Enqueue(payload);
            }
        }

        protected override void Update()
        {
            base.Update();
            if (!m_running) return;

            float now = Time.realtimeSinceStartup;
            if (!InRoom && now - m_lastJoin >= 1f)
            {
                m_lastJoin = now;
                SendBytes(JoinMessage());
            }

            if (now - m_lastHeard > timeoutSeconds)
                Close(InRoom ? "Server timed out" : "No answer from " + serverIp + ":" + port);
        }

        protected override void SendBytes(byte[] message)
        {
            Socket s = m_socket;
            if (s == null) return;
            try { s.SendTo(message, m_server); }
            catch { }
        }

        protected override void CloseSocket()
        {
            m_running = false;
            Socket s = m_socket;
            m_socket = null;
            if (s != null) try { s.Close(); } catch { }

            if (m_thread != null) m_thread.Join(500);
            m_thread = null;
        }
    }
}