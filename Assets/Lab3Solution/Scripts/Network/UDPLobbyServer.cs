// =================================================================================================
//  Lab 3 - Challenge 3 solution: UDP transport of the lobby server
// =================================================================================================
//
//  The 2.1 UDP server: one socket for everybody, one receive thread, no framing.
//  What is new for the lobby:
//    - the endpoint that ReceiveFrom fills in IS the player's identity (Lab 2.2 slide 7)
//    - nothing tells us a client is gone (Lab 2.2 slide 9). Clients PING every second, LobbyServer stores
//      the time of the last message of each player, and Update() removes the silent ones
//
//  Lab 3: packets stay byte[]. Send and the receive loop lost their Encoding.UTF8 calls.
// =================================================================================================

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace Lab3Solution
{
    public class UDPLobbyServer : LobbyServer
    {
        public float timeoutSeconds = 5f;

        const int MaxPacketSize = 64 * 1024;

        Socket m_socket;
        Thread m_thread;
        volatile bool m_running;

        public override bool IsRunning { get { return m_running; } }

        public override bool StartServer()
        {
            try
            {
                m_socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                m_socket.Bind(new IPEndPoint(IPAddress.Any, port));
            }
            catch (SocketException e)
            {
                Log("[SERVER] Could not start: " + e.SocketErrorCode);
                try { m_socket.Close(); } catch { }
                m_socket = null;
                return false;
            }

            m_running = true;
            Log("[SERVER] UDP room " + serverName + " listening on " + port);
            m_thread = new Thread(ReceiveLoop) { IsBackground = true };
            m_thread.Start();
            return true;
        }

        public override void StopServer()
        {
            if (!m_running) return;
            m_running = false;
            try { m_socket.Close(); } catch { }   // unblocks ReceiveFrom()
            m_socket = null;
            if (m_thread != null) m_thread.Join(500);
            ClearPlayers();
            Log("[SERVER] Stopped");
        }

        void OnDestroy() { StopServer(); }

        // ------------------------------------------------------------------------ network thread

        void ReceiveLoop()
        {
            byte[] buffer = new byte[MaxPacketSize];
            while (m_running)
            {
                EndPoint from = new IPEndPoint(IPAddress.Any, 0);   // ReceiveFrom fills it with the sender
                int received;
                try { received = m_socket.ReceiveFrom(buffer, ref from); }
                catch (SocketException e)
                {
                    // An earlier SendTo reached a closed port (2.1 slide 34). Not this receive: go on.
                    if (e.SocketErrorCode == SocketError.ConnectionReset) continue;
                    return;
                }
                catch (ObjectDisposedException) { return; }
                catch (NullReferenceException) { return; }

                byte[] payload = new byte[received];   // one datagram = one message: no framing (slide 19)
                    Array.Copy(buffer, payload, received);
                    m_inbox.Enqueue(new NetEvent { from = from, data = payload });
            }
        }

        // ---------------------------------------------------------------------------- main thread

        protected override void Update()
        {
            base.Update();

            // The only way to notice a closed window or a crash in UDP (Lab 2.2 slide 9).
            float now = Time.realtimeSinceStartup;
            foreach (Player p in Players)
                if (now - p.lastSeen > timeoutSeconds) { RemovePlayer(p, "timed out"); break; }
        }

        // ---------------------------------------------------------------------------- transport

        protected override void Send(object connection, byte[] message)
        {
            Socket s = m_socket;
            if (s == null) return;
            try { s.SendTo(message, (EndPoint)connection); }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }

        // There is no connection to close. Once it is out of the player list we ignore it, and since
        // nobody answers its PINGs any more, the client times out by itself if it missed our message.
        protected override void Drop(object connection) { }
    }
}
