// =================================================================================================
//  Lab 3 - Challenge 3 solution: UDP transport of the lobby client
// =================================================================================================
//
//  The 2.1 UDP client: no Connect, the server address goes in every SendTo.
//  What is new for the lobby:
//    - the join message (our PlayerDTO2) can be lost, so it is repeated every second until
//      the first player list arrives
//
//  Lab 3: packets stay byte[]. SendBytes and the receive loop lost their Encoding.UTF8 calls.
//    - nothing tells us the server is gone (Lab 2.2 slide 9): if it says nothing for timeoutSeconds,
//      even though we PING every second, we consider it lost
// =================================================================================================

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace Lab3Solution
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
                m_endReason = "Not a valid IP: " + serverIp;
                m_inbox.Enqueue(null);
                return;
            }

            m_socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            m_running = true;
            m_lastHeard = m_lastJoin = Time.realtimeSinceStartup;

            // The first SendTo gives the socket its local port. Only then can ReceiveFrom run.
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
                    if (e.SocketErrorCode == SocketError.ConnectionReset) continue;   // nobody on that port
                    return;
                }
                catch (ObjectDisposedException) { return; }

                byte[] payload = new byte[received];
                    Array.Copy(buffer, payload, received);
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
                SendBytes(JoinMessage());   // the first one may have been lost
            }
            if (now - m_lastHeard > timeoutSeconds)
                Close(InRoom ? "Server lost: nothing received for " + timeoutSeconds + " s"
                             : "No answer from " + serverIp + ":" + port);
        }

        protected override void SendBytes(byte[] message)
        {
            Socket s = m_socket;
            if (s == null) return;
            try { s.SendTo(message, m_server); }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }

        protected override void CloseSocket()
        {
            m_running = false;
            Socket s = m_socket; m_socket = null;
            if (s != null) try { s.Close(); } catch { }
            if (m_thread != null) m_thread.Join(500);
            m_thread = null;
        }
    }
}
