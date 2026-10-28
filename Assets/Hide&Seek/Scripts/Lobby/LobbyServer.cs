using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

namespace HideAndSeek
{
    public abstract class LobbyServer : MonoBehaviour
    {
        public string serverName = "HideAndSeekRoom";
        public int port = 9050;
        public int maxPlayers = 8;

        public class Player
        {
            public object connection;
            public PlayerInfo info;
            public int pingMs = -1;
            public float lastSeen;
            public string name => info != null ? info.playerName : "Player";
        }

        protected struct NetEvent
        {
            public object from;
            public byte[] data;
        }

        protected readonly ConcurrentQueue<NetEvent> m_inbox = new ConcurrentQueue<NetEvent>();
        readonly List<Player> m_players = new List<Player>();

        public IList<Player> Players => m_players.AsReadOnly();
        public bool Started { get; private set; }

        public abstract bool IsRunning { get; }
        public abstract bool StartServer();
        public abstract void StopServer();
        protected abstract void Send(object connection, byte[] message);
        protected abstract void Drop(object connection);
        protected virtual string Describe(object connection) => connection.ToString();

        protected void SendText(object c, string text) => Send(c, LobbyMessages.Text(text));

        protected virtual void Update()
        {
            while (m_inbox.TryDequeue(out NetEvent e))
            {
                if (e.data == null)
                {
                    OnConnectionLost(e.from);
                    continue;
                }

                try
                {
                    MsgType type = LobbyMessages.Read(e.data, out string text, out PlayerInfo info);
                    Player p = Find(e.from);
                    if (p != null) p.lastSeen = Time.realtimeSinceStartup;

                    if (type == MsgType.PlayerInfo) OnPlayerInfo(e.from, p, info);
                    else OnText(e.from, p, text);
                }
                catch (Exception ex)
                {
                    Log("[SERVER] Dropped bad packet from " + Describe(e.from) + ": " + ex.Message);
                }
            }
        }

        void OnPlayerInfo(object from, Player existing, PlayerInfo info)
        {
            string name = SanitizeName(info != null ? info.playerName : "Player");

            if (existing != null)
            {
                if (existing.name != name)
                {
                    SendRoomStateTo(from);
                    return;
                }
                SendRoomStateTo(from);
                return;
            }

            if (Started) { Refuse(from, "Game already started"); return; }
            if (m_players.Count >= maxPlayers) { Refuse(from, "Room is full"); return; }
            if (FindByName(name) != null) { Refuse(from, "Name already taken: " + name); return; }

            m_players.Add(new Player
            {
                connection = from,
                info = new PlayerInfo { playerName = name },
                lastSeen = Time.realtimeSinceStartup
            });

            Broadcast("CHAT:* " + name + " joined");
            SendRoomState();
        }

        void OnText(object from, Player p, string text)
        {
            int colon = text.IndexOf(':');
            string type = colon < 0 ? text : text.Substring(0, colon);
            string content = colon < 0 ? "" : text.Substring(colon + 1);

            switch (type)
            {
                case "CHAT":
                    if (p == null) return;
                    Broadcast("CHAT:" + p.name + ": " + content);
                    break;

                case "PING":
                    if (p == null) return;
                    string[] parts = content.Split(',');
                    if (parts.Length > 0) SendText(from, "PONG:" + parts[0]);
                    if (parts.Length > 1 && int.TryParse(parts[1], out int ping))
                        p.pingMs = ping;
                    break;

                case "LEAVE":
                    if (p != null) RemovePlayer(p, "left");
                    Drop(from);
                    break;

                case "RETURN_TO_LOBBY":
                    Started = false;
                    Broadcast("LOBBY:");
                    break;
            }
        }

        void Refuse(object to, string reason)
        {
            SendText(to, "ERROR:" + reason);
            Drop(to);
        }

        void OnConnectionLost(object connection)
        {
            Player p = Find(connection);
            if (p != null) RemovePlayer(p, "disconnected");
        }

        protected void RemovePlayer(Player p, string reason)
        {
            if (!m_players.Remove(p)) return;
            Broadcast("CHAT:* " + p.name + " " + reason);
            SendRoomState();
        }

        public void Kick(Player p)
        {
            object c = p.connection;
            SendText(c, "KICKED:");
            RemovePlayer(p, "was kicked");
            Drop(c);
        }

        public void StartGame()
        {
            if (Started) return;
            if (m_players.Count < 2) return;
            Started = true;
            Broadcast("START:");
        }

        public void ReturnToLobbyForAll()
        {
            Started = false;
            Broadcast("LOBBY:");
        }

        void SendRoomState()
        {
            foreach (Player target in m_players.ToArray())
                SendRoomStateTo(target.connection);
        }

        void SendRoomStateTo(object connection)
        {
            foreach (Player p in m_players.ToArray())
                Send(connection, LobbyMessages.PlayerInfo(p.info));

            SendText(connection, "PLAYERS:" + Names(","));
            SendText(connection, "ROOM:" + serverName + "   " + m_players.Count + "/" + maxPlayers + " players");
        }

        protected void Broadcast(string text)
        {
            byte[] msg = LobbyMessages.Text(text);
            foreach (Player p in m_players.ToArray()) Send(p.connection, msg);
        }

        public string Names(string sep)
        {
            List<string> n = new List<string>();
            foreach (Player p in m_players) n.Add(p.name);
            return string.Join(sep, n);
        }

        protected Player Find(object conn)
        {
            foreach (Player p in m_players)
                if (p.connection.Equals(conn)) return p;
            return null;
        }

        Player FindByName(string name)
        {
            foreach (Player p in m_players)
                if (p.name == name) return p;
            return null;
        }

        protected void ClearPlayers()
        {
            m_players.Clear();
            Started = false;
        }

        static string SanitizeName(string raw)
        {
            string s = (raw ?? "Player").Replace(",", "").Replace(":", "").Trim();
            return s.Length == 0 ? "Player" : s;
        }

        protected void Log(string msg) => LobbySession.Log(msg);
    }
}