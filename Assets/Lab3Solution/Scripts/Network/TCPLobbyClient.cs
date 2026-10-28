// =================================================================================================
//  Lab 3 - Challenge 3 solution: TCP transport of the lobby client
// =================================================================================================
//
//  The 2.1 TCP client: Connect once, then one thread reads framed packets.
//  If the server goes away, Receive returns 0 or throws and we tell the main thread.
//
//  Lab 3: packets stay byte[]. SendBytes and the receive loop lost their Encoding.UTF8 calls.
// =================================================================================================

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Lab3Solution
{
    public class TCPLobbyClient : LobbyClient
    {
        const int MaxPacketSize = 64 * 1024;

        Socket m_socket;
        Thread m_thread;
        volatile bool m_running;

        public override void Connect()
        {
            m_running = true;
            m_thread = new Thread(NetworkThread) { IsBackground = true };
            m_thread.Start();
        }

        void NetworkThread()
        {
            try
            {
                m_socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                m_socket.Connect(new IPEndPoint(IPAddress.Parse(serverIp), port));   // blocks: thread
            }
            catch (Exception e)
            {
                m_endReason = "Could not connect to " + serverIp + ":" + port + " (" + Reason(e) + ")";
                m_inbox.Enqueue(null);
                return;
            }

            SendBytes(JoinMessage());

            byte[] header = new byte[4];
            while (m_running)
            {
                if (!ReadExactly(header, 4)) break;
                int size = BitConverter.ToInt32(header, 0);
                if (size <= 0 || size > MaxPacketSize) break;
                byte[] payload = new byte[size];
                if (!ReadExactly(payload, size)) break;
                m_inbox.Enqueue(payload);
            }
            m_inbox.Enqueue(null);   // the server closed or vanished
        }

        bool ReadExactly(byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read;
                try { read = m_socket.Receive(buffer, total, count - total, SocketFlags.None); }
                catch (SocketException) { return false; }
                catch (ObjectDisposedException) { return false; }
                if (read == 0) return false;
                total += read;
            }
            return true;
        }

        protected override void SendBytes(byte[] payload)
        {
            Socket s = m_socket;
            if (s == null) return;
            byte[] framed = new byte[4 + payload.Length];
            BitConverter.GetBytes(payload.Length).CopyTo(framed, 0);
            payload.CopyTo(framed, 4);
            try { s.Send(framed); }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }

        protected override void CloseSocket()
        {
            m_running = false;
            Socket s = m_socket; m_socket = null;
            if (s != null) { try { s.Shutdown(SocketShutdown.Both); } catch { } try { s.Close(); } catch { } }
            if (m_thread != null && m_thread != Thread.CurrentThread) m_thread.Join(500);
            m_thread = null;
        }

        static string Reason(Exception e)
        {
            SocketException se = e as SocketException;
            return se != null ? se.SocketErrorCode.ToString() : e.Message;
        }
    }
}
