using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class SocketsTCPClientSolution : MonoBehaviour
{
    public string serverIp = "127.0.0.1";
    public int port = 9050;
    public string userName = "Player";
    public bool autoStart = true;

    const int MaxPacketSize = 64 * 1024;

    Socket m_connection;
    readonly List<Thread> m_threads = new List<Thread>();
    readonly ConcurrentQueue<byte[]> m_inbox = new ConcurrentQueue<byte[]>();
    readonly List<string> m_log = new List<string>();
    readonly List<string> m_chat = new List<string>();
    volatile bool m_running;

    string m_playerList = "";
    string m_info = "";
    int m_maxPlayers;
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
        m_chat.Clear();
        m_playerList = "";
        m_info = "";
        StartThread(ClientThread);
    }

    public void Disconnect()
    {
        if (!m_running) return;
        m_running = false;

        if (m_connection != null)
        {
            try { SendString("LEAVE"); } catch { }
        }

        CloseSocket(m_connection);
        m_connection = null;

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
        {
            if (data == null) { OnConnectionLost(); continue; }
            OnPacketReceived(data);
        }
    }

    void ClientThread()
    {
        try
        {
            m_connection = StartClient();
        }
        catch (SocketException e)
        {
            Log("[CLIENT] Could not connect: " + e.SocketErrorCode);
            m_inbox.Enqueue(null);
            return;
        }
        catch (FormatException)
        {
            Log("[CLIENT] Invalid IP");
            m_inbox.Enqueue(null);
            return;
        }

        if (m_connection == null) return;

        Log("[CLIENT] Connected to " + serverIp + ":" + port);
        OnConnected();
        ReceiveLoop(m_connection);
        Log("[CLIENT] Connection closed");
        m_inbox.Enqueue(null);
    }

    void OnConnectionLost()
    {
        if (m_running) Disconnect();
    }

    public void SendPacket(byte[] payload)
    {
        Socket s = m_connection;
        if (s == null || payload == null) return;

        byte[] framed = new byte[4 + payload.Length];
        BitConverter.GetBytes(payload.Length).CopyTo(framed, 0);
        payload.CopyTo(framed, 4);

        try { lock (s) SendRaw(s, framed); }
        catch (SocketException e) { Log("[CLIENT] Send failed: " + e.SocketErrorCode); }
        catch (ObjectDisposedException) { }
    }

    public void SendString(string text)
    {
        SendPacket(Encoding.UTF8.GetBytes(text));
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
                Log("[CLIENT] Invalid packet size: " + size);
                return;
            }

            byte[] payload = new byte[size];
            if (!ReadExactly(socket, payload, size)) return;

            m_inbox.Enqueue(payload);
        }
    }

    bool ReadExactly(Socket socket, byte[] buffer, int count)
    {
        int total = 0;
        while (total < count)
        {
            int read;
            try { read = ReceiveRaw(socket, buffer, total, count - total); }
            catch (SocketException e) { Log("[CLIENT] Connection lost: " + e.SocketErrorCode); return false; }
            catch (ObjectDisposedException) { return false; }

            if (read <= 0) return false;
            total += read;
        }
        return true;
    }

    void OnPacketReceived(byte[] data)
    {
        string msg = Encoding.UTF8.GetString(data);

        if (msg.StartsWith("PLAYERS:"))
        {
            m_playerList = msg.Substring(8);
        }
        else if (msg.StartsWith("CHAT:"))
        {
            m_chat.Add(msg.Substring(5));
            if (m_chat.Count > 100) m_chat.RemoveAt(0);
            m_chatScroll.y = float.MaxValue;
        }
        else if (msg.StartsWith("INFO:"))
        {
            // INFO:<max>|name=ping,name=ping
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

    public void SendChat(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        SendString("CHAT:" + text);
    }

    void SubmitChatInput()
    {
        string t = m_chatInput.Trim();
        m_chatInput = "";
        if (t.Length > 0) SendChat(t);
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
        GUILayout.Label("TCP CLIENT   port " + port + "   name " + userName);

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
            GUILayout.Label("Waiting room" + (m_maxPlayers > 0 ? " (max " + m_maxPlayers + ")" : ""));
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
                SubmitChatInput();
            }
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(8);
        lock (m_log)
            for (int i = Mathf.Max(0, m_log.Count - 4); i < m_log.Count; i++)
                GUILayout.Label(m_log[i]);

        GUILayout.EndArea();
    }

    Socket StartClient()
    {
        Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Connect(new IPEndPoint(IPAddress.Parse(serverIp), port));
        return socket;
    }

    void OnConnected()
    {
        SendString("JOIN:" + userName);
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