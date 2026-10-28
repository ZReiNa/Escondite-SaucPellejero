using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class SocketsUDPClientSolution : MonoBehaviour
{
    public string serverIp = "127.0.0.1";
    public int port = 9050;
    public string userName = "Player";
    public bool autoStart = true;
    public float timeoutSeconds = 5f;

    const int MaxPacketSize = 64 * 1024;

    Socket m_socket;
    readonly List<Thread> m_threads = new List<Thread>();
    readonly ConcurrentQueue<byte[]> m_inbox = new ConcurrentQueue<byte[]>();
    readonly List<string> m_log = new List<string>();
    readonly List<string> m_chat = new List<string>();
    volatile bool m_running;

    string m_playerList = "";
    string m_info = "";
    int m_maxPlayers;
    bool m_joined;
    float m_lastHeard;
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

        try
        {
            m_socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            m_socket.Connect(IPAddress.Parse(serverIp), port);
        }
        catch (FormatException)
        {
            Log("[CLIENT] Invalid IP");
            return;
        }
        catch (SocketException e)
        {
            Log("[CLIENT] Could not create/connect socket: " + e.SocketErrorCode);
            return;
        }

        m_running = true;
        m_joined = false;
        m_chat.Clear();
        m_playerList = "";
        m_info = "";
        m_maxPlayers = 0;
        m_lastHeard = Time.realtimeSinceStartup;
        m_nextTick = 0f;

        Log("[CLIENT] Joining " + serverIp + ":" + port);
        StartThread(ReceiveLoop);
        SendString("JOIN:" + userName);
    }

    public void Disconnect()
    {
        if (!m_running) return;

        m_running = false;
        m_joined = false;

        if (m_socket != null)
        {
            try { m_socket.Close(); } catch { }
            m_socket = null;
        }

        Thread[] threads;
        lock (m_threads) { threads = m_threads.ToArray(); m_threads.Clear(); }
        foreach (Thread t in threads)
            if (t != Thread.CurrentThread) t.Join(500);

        Log("[CLIENT] Disconnected");
    }

    void OnDestroy()
    {
        Disconnect();
    }

    void Update()
    {
        byte[] data;
        while (m_inbox.TryDequeue(out data))
            OnPacketReceived(data);

        if (!m_running) return;

        float now = Time.realtimeSinceStartup;

        if (now - m_lastHeard > timeoutSeconds)
        {
            Log(m_joined ? "[CLIENT] Server not answering: connection lost"
                         : "[CLIENT] No server on " + serverIp + ":" + port);
            Disconnect();
            return;
        }

        if (now >= m_nextTick)
        {
            m_nextTick = now + 1f;
            if (!m_joined) SendString("JOIN:" + userName);
            else SendString("PING:");
        }
    }

    void ReceiveLoop()
    {
        byte[] buffer = new byte[MaxPacketSize];

        while (m_running)
        {
            int received;
            try
            {
                received = m_socket.Receive(buffer);
            }
            catch (SocketException e)
            {
                if (m_running) Log("[CLIENT] Receive failed: " + e.SocketErrorCode);
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (received <= 0) continue;

            byte[] payload = new byte[received];
            Array.Copy(buffer, payload, received);
            m_inbox.Enqueue(payload);
        }
    }

    public void SendString(string text)
    {
        Socket s = m_socket;
        if (s == null) return;

        try
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            s.Send(data);
        }
        catch (SocketException e)
        {
            Log("[CLIENT] Send failed: " + e.SocketErrorCode);
        }
        catch (ObjectDisposedException) { }
    }

    public void SendChat(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        SendString("CHAT:" + text);
    }

    void OnPacketReceived(byte[] data)
    {
        string msg = Encoding.UTF8.GetString(data);
        m_lastHeard = Time.realtimeSinceStartup;

        if (msg.StartsWith("PLAYERS:"))
        {
            m_joined = true;
            m_playerList = msg.Substring(8);
        }
        else if (msg.StartsWith("CHAT:"))
        {
            m_joined = true;
            m_chat.Add(msg.Substring(5));
            if (m_chat.Count > 100) m_chat.RemoveAt(0);
            m_chatScroll.y = float.MaxValue;
        }
        else if (msg.StartsWith("INFO:"))
        {
            string body = msg.Substring(5);
            int bar = body.IndexOf('|');
            if (bar > 0)
            {
                int.TryParse(body.Substring(0, bar), out m_maxPlayers);
                m_info = body.Substring(bar + 1);
            }
        }
        else if (msg.StartsWith("PING:"))
        {
            SendString("PONG:" + msg.Substring(5));
        }
        else if (msg.StartsWith("KICK:"))
        {
            Log("[CLIENT] You were kicked");
            Disconnect();
        }
        else if (msg.StartsWith("FULL:"))
        {
            Log("[CLIENT] The room is full");
            Disconnect();
        }
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
            if (args[i] == "-ip" && i + 1 < args.Length) serverIp = args[++i];
            else if (args[i] == "-port" && i + 1 < args.Length) int.TryParse(args[++i], out port);
            else if (args[i] == "-name" && i + 1 < args.Length) userName = args[++i];
            else if (args[i] == "-autostart") autoStart = true;
            else if (args[i] == "-noautostart") autoStart = false;
        }
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, Screen.width - 20, Screen.height - 20));
        GUILayout.Label("UDP CLIENT   port " + port + "   name " + userName);

        GUILayout.BeginHorizontal();
        if (!m_running)
        {
            GUILayout.Label("Server IP:", GUILayout.Width(70));
            serverIp = GUILayout.TextField(serverIp, GUILayout.Width(120));
            GUILayout.Label("Name:", GUILayout.Width(45));
            userName = GUILayout.TextField(userName, GUILayout.Width(120));
            if (GUILayout.Button("Join", GUILayout.Width(110))) StartNetwork();
        }
        else if (GUILayout.Button("Leave", GUILayout.Width(110))) Disconnect();
        GUILayout.EndHorizontal();

        if (m_running)
        {
            GUILayout.Space(8);
            GUILayout.Label(m_joined
                ? "Waiting room" + (m_maxPlayers > 0 ? " (max " + m_maxPlayers + ")" : "")
                : "Joining...");
            GUILayout.Label("Players: " + m_playerList);
            if (m_info.Length > 0) GUILayout.Label("Ping: " + m_info);

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
                if (t.Length > 0) SendChat(t);
            }
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(8);
        lock (m_log)
            for (int i = Mathf.Max(0, m_log.Count - 4); i < m_log.Count; i++)
                GUILayout.Label(m_log[i]);

        GUILayout.EndArea();
    }
}