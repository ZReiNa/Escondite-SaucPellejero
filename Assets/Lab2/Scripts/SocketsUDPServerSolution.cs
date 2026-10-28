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

public class SocketsUDPServerSolution : MonoBehaviour
{
    public int port = 9050;
    public string serverName = "MyServer";   // also the host's player name
    public bool autoStart = true;
    public int maxPlayers = 8;               // includes the host
    public float timeoutSeconds = 5f;

    const int MaxPacketSize = 64 * 1024;

    struct Packet { public byte[] data; public EndPoint from; }

    class ClientInfo
    {
        public EndPoint endpoint;
        public string name;
        public int ping;
        public float lastHeard;
    }

    Socket m_socket;
    // Only touched from the main thread (Update / OnGUI)
    readonly List<ClientInfo> m_clients = new List<ClientInfo>();
    readonly List<Thread> m_threads = new List<Thread>();
    readonly ConcurrentQueue<Packet> m_inbox = new ConcurrentQueue<Packet>();
    readonly List<string> m_log = new List<string>();
    readonly List<string> m_chat = new List<string>();
    volatile bool m_running;

    float m_nextTick;
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

        try { m_socket = StartServer(); }
        catch (SocketException e)
        {
            Log("[SERVER] Could not start: " + e.SocketErrorCode +
                (e.SocketErrorCode == SocketError.AddressAlreadyInUse
                    ? " (port " + port + " is still used by another instance)" : ""));
            m_socket = null;
            return;
        }

        m_running = true;
        m_clients.Clear();
        m_chat.Clear();
        Log("[SERVER] Listening on " + port);
        AddChatLine("[SERVER] " + serverName + " (host) created the room");
        StartThread(ReceiveLoop);
    }

    public void Disconnect()
    {
        if (!m_running) return;

        foreach (ClientInfo c in m_clients.ToArray())
            SendString("KICK:", c.endpoint);   // tell everybody the room is closed
        m_clients.Clear();

        m_running = false;
        if (m_socket != null) { try { m_socket.Close(); } catch { } m_socket = null; }

        Thread[] threads;
        lock (m_threads) { threads = m_threads.ToArray(); m_threads.Clear(); }
        foreach (Thread t in threads) if (t != Thread.CurrentThread) t.Join(500);

        Log("[SERVER] Stopped");
    }

    void OnDestroy() { Disconnect(); }

    void Update()
    {
        Packet packet;
        while (m_inbox.TryDequeue(out packet))
            OnPacketReceived(packet.data, packet.from);

        if (!m_running || Time.realtimeSinceStartup < m_nextTick) return;
        m_nextTick = Time.realtimeSinceStartup + 1f;

        // remove silent endpoints (closed window / crash: no LEAVE arrives)
        float now = Time.realtimeSinceStartup;
        foreach (ClientInfo c in m_clients.Where(x => now - x.lastHeard > timeoutSeconds).ToArray())
        {
            Log("[SERVER] Timeout: " + c.name);
            RemoveClient(c, "timed out");
        }

        // ping every client (measures round trip, also keeps the client alive)
        string ping = "PING:" + (now * 1000.0).ToString("R", CultureInfo.InvariantCulture);
        foreach (ClientInfo c in m_clients) SendString(ping, c.endpoint);
        BroadcastInfo();
    }


    void ReceiveLoop()
    {
        byte[] buffer = new byte[MaxPacketSize];

        while (m_running)
        {
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);
            int received;
            try { received = m_socket.ReceiveFrom(buffer, ref from); }
            catch (SocketException e)
            {
                if (e.SocketErrorCode == SocketError.ConnectionReset) continue; // a client vanished, keep serving
                if (m_running) Log("[SERVER] Receive failed: " + e.SocketErrorCode);
                return;
            }
            catch (ObjectDisposedException) { return; }
            catch (NullReferenceException) { return; }

            if (received <= 0) continue;

            byte[] payload = new byte[received];
            Array.Copy(buffer, payload, received);
            m_inbox.Enqueue(new Packet { data = payload, from = from });
        }
    }

    
    public void SendString(string text, EndPoint to)
    {
        Socket s = m_socket;
        if (s == null || to == null) return;
        try { byte[] b = Encoding.UTF8.GetBytes(text); s.SendTo(b, to); }
        catch (SocketException e) { Log("[SERVER] Send failed: " + e.SocketErrorCode); }
        catch (ObjectDisposedException) { }
    }

    void SendToAll(string text)
    {
        foreach (ClientInfo c in m_clients) SendString(text, c.endpoint);
    }


    ClientInfo Find(EndPoint ep)
    {
        return m_clients.FirstOrDefault(c => c.endpoint.Equals(ep));
    }

    void OnPacketReceived(byte[] data, EndPoint from)
    {
        string text = Encoding.UTF8.GetString(data);
        ClientInfo client = Find(from);
        if (client != null) client.lastHeard = Time.realtimeSinceStartup;

        if (text.StartsWith("JOIN:"))
        {
            string name = text.Substring(5).Trim();
            if (string.IsNullOrEmpty(name)) return;

            if (client != null)
            {
                SendString("PLAYERS:" + PlayerNames(), from);
                return;
            }

            if (m_clients.Count + 1 >= maxPlayers) { SendString("FULL:", from); return; }
            if (name == serverName || m_clients.Any(c => c.name == name)) { SendString("KICK:", from); return; }

            Log("[SERVER] New client: " + from + " (" + name + ")");
            m_clients.Add(new ClientInfo { endpoint = from, name = name, lastHeard = Time.realtimeSinceStartup });

            BroadcastChat("[SERVER] " + name + " joined");
            BroadcastPlayers();
            BroadcastInfo();
            return;
        }

        if (client == null) return;  

        if (text.StartsWith("CHAT:"))
        {
            BroadcastChat(client.name + ": " + text.Substring(5));
        }
        else if (text.StartsWith("PONG:"))
        {
            double sent;
            if (double.TryParse(text.Substring(5), NumberStyles.Float, CultureInfo.InvariantCulture, out sent))
                client.ping = Mathf.Max(0, (int)(Time.realtimeSinceStartup * 1000.0 - sent));
        }
        else if (text.StartsWith("PING:"))
        {
            SendString("PONG:", from); 
        }
        else if (text.StartsWith("LEAVE"))
        {
            RemoveClient(client, "left");
        }
    }

    void RemoveClient(ClientInfo c, string reason)
    {
        m_clients.Remove(c);
        BroadcastChat("[SERVER] " + c.name + " " + (reason == "left" ? "left" : reason));
        BroadcastPlayers();
        BroadcastInfo();
    }

    void KickPlayer(ClientInfo c)
    {
        SendString("KICK:", c.endpoint);
        RemoveClient(c, "was kicked");
    }

    string PlayerNames()
    {
        return string.Join(",", new[] { serverName }
            .Concat(m_clients.Select(c => c.name)).ToArray());
    }

    void BroadcastChat(string text)
    {
        AddChatLine(text);
        SendToAll("CHAT:" + text);
    }

    void BroadcastPlayers()
    {
        SendToAll("PLAYERS:" + PlayerNames());
    }

    void BroadcastInfo()
    {
        string entries = string.Join(",", new[] { serverName + "=0" }
            .Concat(m_clients.Select(c => c.name + "=" + c.ping)).ToArray());
        SendToAll("INFO:" + maxPlayers + "|" + entries);
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

    public void Log(string message)
    {
        Debug.Log(message);
        lock (m_log) { m_log.Add(message); if (m_log.Count > 100) m_log.RemoveAt(0); }
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
        GUILayout.Label("UDP SERVER   port " + port + "   host " + serverName);

        if (!m_running)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Host name:", GUILayout.Width(80));
            serverName = GUILayout.TextField(serverName, GUILayout.Width(140));
            GUILayout.Label("Max:", GUILayout.Width(35));
            string mp = GUILayout.TextField(maxPlayers.ToString(), GUILayout.Width(40));
            int parsed; if (int.TryParse(mp, out parsed)) maxPlayers = Mathf.Clamp(parsed, 1, 64);
            if (GUILayout.Button("Create game", GUILayout.Width(110))) StartNetwork();
            GUILayout.EndHorizontal();
        }
        else
        {
            if (GUILayout.Button("Stop", GUILayout.Width(80))) Disconnect();

            if (m_running)
            {
                GUILayout.Space(8);
                GUILayout.Label("Waiting room (" + (m_clients.Count + 1) + "/" + maxPlayers + ")");
                GUILayout.Label("  " + serverName + " (host)   ping 0 ms");

                ClientInfo toKick = null;
                foreach (ClientInfo c in m_clients)
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
        }

        GUILayout.Space(8);
        lock (m_log)
            for (int i = Mathf.Max(0, m_log.Count - 4); i < m_log.Count; i++)
                GUILayout.Label(m_log[i]);

        GUILayout.EndArea();
    }

    Socket StartServer()
    {
        Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Any, port));   // no Listen in UDP
        return socket;
    }
}