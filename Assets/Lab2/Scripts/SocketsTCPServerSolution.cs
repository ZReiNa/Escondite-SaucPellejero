using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class SocketsTCPServerSolution : MonoBehaviour
{
    public int port = 9050;
    public string serverName = "MyServer";
    public bool autoStart = true;
    public int maxPlayers = 8;

    const int MaxPacketSize = 64 * 1024;

    struct Packet
    {
        public byte[] data;
        public Socket from;
    }

    class ClientInfo
    {
        public Socket socket;
        public string name;
        public int ping;
    }

    Socket m_listener;
    readonly List<ClientInfo> m_clients = new List<ClientInfo>();
    readonly List<Thread> m_threads = new List<Thread>();
    readonly ConcurrentQueue<Packet> m_inbox = new ConcurrentQueue<Packet>();
    readonly List<string> m_log = new List<string>();
    readonly List<string> m_chat = new List<string>();
    volatile bool m_running;

    float m_nextPing;
    string m_chatInput = "";
    Vector2 m_chatScroll;

    public bool IsRunning { get { return m_running; } }

    void Start()
    {
        Application.runInBackground = true;
        ParseCommandLine();
        if (autoStart) StartNetwork();
    }

    public void StartNetwork()
    {
        if (m_running) return;
        m_running = true;
        StartThread(ServerThread);
        BroadcastChat("[SERVER] " + serverName + " created the room");
    }

    public void Disconnect()
    {
        if (!m_running) return;
        m_running = false;

        ClientInfo[] clients;
        lock (m_clients) { clients = m_clients.ToArray(); m_clients.Clear(); }
        foreach (ClientInfo c in clients) CloseSocket(c.socket);

        CloseSocket(m_listener);
        m_listener = null;

        Thread[] threads;
        lock (m_threads) { threads = m_threads.ToArray(); m_threads.Clear(); }
        foreach (Thread t in threads)
            if (t != Thread.CurrentThread) t.Join(500);

        Log("[SERVER] Stopped");
    }

    void OnDestroy()
    {
        Disconnect();
    }

    void Update()
    {
        Packet packet;
        while (m_inbox.TryDequeue(out packet))
        {
            if (packet.data == null) OnClientDisconnected(packet.from);
            else OnPacketReceived(packet.data, packet.from);
        }

        if (m_running && Time.realtimeSinceStartup >= m_nextPing)
        {
            m_nextPing = Time.realtimeSinceStartup + 1f;
            string ping = "PING:" + (Time.realtimeSinceStartup * 1000.0).ToString("R", CultureInfo.InvariantCulture);
            foreach (Socket s in Sockets()) SendString(ping, s);
            BroadcastInfo();
        }
    }

    void ServerThread()
    {
        try { m_listener = StartServer(); }
        catch (SocketException e)
        {
            Log("[SERVER] Could not start: " + e.SocketErrorCode +
                (e.SocketErrorCode == SocketError.AddressAlreadyInUse
                    ? " (port " + port + " is still used by another instance)" : ""));
            m_running = false;
            return;
        }

        Log("[SERVER] Listening on " + port);

        while (m_running)
        {
            Socket client;
            try { client = AcceptClient(); }
            catch (SocketException) { break; }
            catch (ObjectDisposedException) { break; }

            if (client == null) return;

            lock (m_clients) m_clients.Add(new ClientInfo { socket = client, name = null });
            Log("[SERVER] Client connected: " + client.RemoteEndPoint);

            Socket captured = client;
            StartThread(delegate { ClientThread(captured); });
        }
    }

    void ClientThread(Socket client)
    {
        ReceiveLoop(client);
        m_inbox.Enqueue(new Packet { data = null, from = client });
    }

    void ReceiveLoop(Socket socket)
    {
        byte[] header = new byte[4];
        while (m_running)
        {
            if (!ReadExactly(socket, header, 4)) return;

            int size = BitConverter.ToInt32(header, 0);
            if (size <= 0 || size > MaxPacketSize)
            {
                Log("[SERVER] Invalid packet size: " + size);
                return;
            }

            byte[] payload = new byte[size];
            if (!ReadExactly(socket, payload, size)) return;

            m_inbox.Enqueue(new Packet { data = payload, from = socket });
        }
    }

    bool ReadExactly(Socket socket, byte[] buffer, int count)
    {
        int total = 0;
        while (total < count)
        {
            int read;
            try { read = ReceiveRaw(socket, buffer, total, count - total); }
            catch (SocketException e) { Log("[SERVER] Connection lost: " + e.SocketErrorCode); return false; }
            catch (ObjectDisposedException) { return false; }

            if (read <= 0) return false;
            total += read;
        }
        return true;
    }

    public void SendPacket(byte[] payload, Socket to)
    {
        if (to == null || payload == null) return;

        byte[] framed = new byte[4 + payload.Length];
        BitConverter.GetBytes(payload.Length).CopyTo(framed, 0);
        payload.CopyTo(framed, 4);

        try { lock (to) SendRaw(to, framed); }
        catch (SocketException e) { Log("[SERVER] Send failed: " + e.SocketErrorCode); }
        catch (ObjectDisposedException) { }
    }

    public void SendString(string text, Socket to)
    {
        SendPacket(Encoding.UTF8.GetBytes(text), to);
    }

    Socket[] Sockets()
    {
        lock (m_clients)
            return m_clients.Select(c => c.socket).Where(s => s != null).ToArray();
    }

    void OnPacketReceived(byte[] data, Socket from)
    {
        string text = Encoding.UTF8.GetString(data);

        if (text.StartsWith("JOIN:"))
        {
            string name = text.Substring(5).Trim();
            if (string.IsNullOrEmpty(name)) return;

            bool full, duplicate;
            lock (m_clients)
            {
                int count = m_clients.Count(x => !string.IsNullOrEmpty(x.name));
                full = count >= maxPlayers;
                duplicate = name == serverName || m_clients.Any(x => x.name == name);
            }

            if (full || duplicate)
            {
                SendString(full ? "FULL:" : "KICK:", from);
                RemoveClient(from);
                CloseSocket(from);
                return;
            }

            lock (m_clients)
            {
                var c = m_clients.FirstOrDefault(x => x.socket == from);
                if (c != null) c.name = name;
            }

            BroadcastChat("[SERVER] " + name + " joined");
            BroadcastPlayers();
            BroadcastInfo();
            return;
        }

        if (text.StartsWith("CHAT:"))
        {
            string name = GetClientName(from);
            if (string.IsNullOrEmpty(name)) name = "Unknown";
            BroadcastChat(name + ": " + text.Substring(5));
            return;
        }

        if (text.StartsWith("PONG:"))
        {
            double sent;
            if (double.TryParse(text.Substring(5), NumberStyles.Float, CultureInfo.InvariantCulture, out sent))
            {
                int ms = Mathf.Max(0, (int)(Time.realtimeSinceStartup * 1000.0 - sent));
                lock (m_clients)
                {
                    var c = m_clients.FirstOrDefault(x => x.socket == from);
                    if (c != null) c.ping = ms;
                }
            }
            return;
        }

        if (text.StartsWith("LEAVE"))
        {
            OnClientDisconnected(from);
            CloseSocket(from);
        }
    }

    void OnClientDisconnected(Socket socket)
    {
        string name = RemoveClient(socket);
        CloseSocket(socket);

        if (!string.IsNullOrEmpty(name))
        {
            BroadcastChat("[SERVER] " + name + " left");
            BroadcastPlayers();
            BroadcastInfo();
        }

        Log("[SERVER] Client disconnected");
    }

    string RemoveClient(Socket socket)
    {
        lock (m_clients)
        {
            int idx = m_clients.FindIndex(c => c.socket == socket);
            if (idx < 0) return null;
            string n = m_clients[idx].name;
            m_clients.RemoveAt(idx);
            return n;
        }
    }

    string GetClientName(Socket socket)
    {
        lock (m_clients)
        {
            var c = m_clients.FirstOrDefault(x => x.socket == socket);
            return c != null ? c.name : null;
        }
    }

    void KickPlayer(ClientInfo c)
    {
        SendString("KICK:", c.socket);
        OnClientDisconnected(c.socket);
    }

    void BroadcastChat(string text)
    {
        AddChatLine(text);
        byte[] payload = Encoding.UTF8.GetBytes("CHAT:" + text);
        foreach (Socket s in Sockets()) SendPacket(payload, s);
    }

    void BroadcastPlayers()
    {
        string[] names;
        lock (m_clients)
            names = m_clients.Where(c => !string.IsNullOrEmpty(c.name)).Select(c => c.name).ToArray();

        byte[] payload = Encoding.UTF8.GetBytes("PLAYERS:" + string.Join(",", new[] { serverName }.Concat(names).ToArray()));
        foreach (Socket s in Sockets()) SendPacket(payload, s);
    }

    void BroadcastInfo()
    {
        string[] entries;
        lock (m_clients)
            entries = m_clients.Where(c => !string.IsNullOrEmpty(c.name)).Select(c => c.name + "=" + c.ping).ToArray();

        string msg = "INFO:" + maxPlayers + "|" + string.Join(",", new[] { serverName + "=0" }.Concat(entries).ToArray());
        byte[] payload = Encoding.UTF8.GetBytes(msg);
        foreach (Socket s in Sockets()) SendPacket(payload, s);
    }

    void AddChatLine(string line)
    {
        m_chat.Add(line);
        if (m_chat.Count > 100) m_chat.RemoveAt(0);
        m_chatScroll.y = float.MaxValue;
    }

    void StartThread(ThreadStart work)
    {
        Thread t = new Thread(work);
        t.IsBackground = true;
        lock (m_threads) m_threads.Add(t);
        t.Start();
    }

    void CloseSocket(Socket socket)
    {
        if (socket == null) return;
        try { socket.Shutdown(SocketShutdown.Both); } catch { }
        try { socket.Close(); } catch { }
    }

    public void Log(string message)
    {
        Debug.Log(message);
        lock (m_log)
        {
            m_log.Add(message);
            if (m_log.Count > 100) m_log.RemoveAt(0);
        }
    }

    void ParseCommandLine()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-port" && i + 1 < args.Length) int.TryParse(args[++i], out port);
            else if (args[i] == "-name" && i + 1 < args.Length) serverName = args[++i];
            else if (args[i] == "-max" && i + 1 < args.Length) int.TryParse(args[++i], out maxPlayers);
            else if (args[i] == "-autostart") autoStart = true;
            else if (args[i] == "-noautostart") autoStart = false;
        }
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, Screen.width - 20, Screen.height - 20));
        GUILayout.Label("TCP SERVER   port " + port + "   host " + serverName);

        if (!m_running)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Host name:", GUILayout.Width(80));
            serverName = GUILayout.TextField(serverName, GUILayout.Width(140));
            GUILayout.Label("Max:", GUILayout.Width(35));
            string mp = GUILayout.TextField(maxPlayers.ToString(), GUILayout.Width(40));
            int parsed;
            if (int.TryParse(mp, out parsed)) maxPlayers = Mathf.Clamp(parsed, 1, 64);
            if (GUILayout.Button("Create game", GUILayout.Width(110))) StartNetwork();
            GUILayout.EndHorizontal();
        }
        else
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Stop", GUILayout.Width(80))) Disconnect();
            GUILayout.EndHorizontal();

            ClientInfo[] players;
            lock (m_clients) players = m_clients.Where(c => !string.IsNullOrEmpty(c.name)).ToArray();

            GUILayout.Space(8);
            GUILayout.Label("Waiting room (" + (players.Length + 1) + "/" + maxPlayers + ")");
            GUILayout.Label("  " + serverName + " (host)   ping 0 ms");

            ClientInfo toKick = null;
            foreach (ClientInfo c in players)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("  " + c.name + "   ping " + c.ping + " ms", GUILayout.Width(250));
                if (GUILayout.Button("Kick", GUILayout.Width(60))) toKick = c;
                GUILayout.EndHorizontal();
            }
            if (toKick != null) KickPlayer(toKick);

            GUILayout.Space(8);
            GUILayout.Label("Chat");
            m_chatScroll = GUILayout.BeginScrollView(m_chatScroll, GUILayout.Height(160));
            foreach (string line in m_chat) GUILayout.Label(line);
            GUILayout.EndScrollView();

            Event e = Event.current;
            bool enter = e.type == EventType.KeyDown &&
                         (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) &&
                         GUI.GetNameOfFocusedControl() == "ChatInput";

            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("ChatInput");
            m_chatInput = GUILayout.TextField(m_chatInput, 200, GUILayout.Width(300));
            if (GUILayout.Button("Send", GUILayout.Width(70)) || enter)
            {
                if (enter) e.Use();
                string t = m_chatInput.Trim();
                m_chatInput = "";
                if (t.Length > 0) BroadcastChat(serverName + ": " + t);
            }
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(8);
        lock (m_log)
            for (int i = Mathf.Max(0, m_log.Count - 4); i < m_log.Count; i++)
                GUILayout.Label(m_log[i]);

        GUILayout.EndArea();
    }

    Socket StartServer()
    {
        Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Any, port));
        socket.Listen(10);
        return socket;
    }

    Socket AcceptClient()
    {
        return m_listener.Accept();
    }

    int SendRaw(Socket socket, byte[] data)
    {
        return socket.Send(data);
    }

    int ReceiveRaw(Socket socket, byte[] buffer, int offset, int count)
    {
        return socket.Receive(buffer, offset, count, SocketFlags.None);
    }
}