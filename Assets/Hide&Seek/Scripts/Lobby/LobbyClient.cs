using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

namespace HideAndSeek
{
    public abstract class LobbyClient : MonoBehaviour
    {
        public string serverIp = "127.0.0.1";
        public int port = 9050;
        public PlayerInfo myInfo;
        public float pingSeconds = 1f;

        public string userName => myInfo != null ? myInfo.playerName : "Player";

        public readonly List<string> Players = new List<string>();
        public readonly Dictionary<string, PlayerInfo> Infos = new Dictionary<string, PlayerInfo>();
        public readonly List<string> Chat = new List<string>();

        public string RoomInfo = "";
        public int PingMs = -1;
        public bool InRoom { get; private set; }

        public event Action Joined;
        public event Action GameStarted;
        public event Action ReturnedToLobby;
        public event Action<string> Closed;

        protected readonly ConcurrentQueue<byte[]> m_inbox = new ConcurrentQueue<byte[]>();
        protected volatile string m_endReason = "Connection to server lost";
        protected float m_lastHeard;
        float m_lastPing;
        bool m_closed;

        public abstract void Connect();
        protected abstract void SendBytes(byte[] message);
        protected abstract void CloseSocket();

        protected void SendText(string text) => SendBytes(LobbyMessages.Text(text));
        protected byte[] JoinMessage() => LobbyMessages.PlayerInfo(myInfo);

        public void SendChat(string line)
        {
            line = (line ?? "").Trim();
            if (line.Length > 0) SendText("CHAT:" + line);
        }

        public void Leave()
        {
            SendText("LEAVE:");
            Close("You left the room");
        }

        protected virtual void Update()
        {
            while (!m_closed && m_inbox.TryDequeue(out byte[] data))
            {
                if (data == null) { Close(m_endReason); break; }
                m_lastHeard = Time.realtimeSinceStartup;

                try
                {
                    MsgType type = LobbyMessages.Read(data, out string text, out PlayerInfo info);
                    if (type == MsgType.PlayerInfo) OnPlayerInfo(info);
                    else OnText(text);
                }
                catch (Exception e)
                {
                    Log("[CLIENT " + userName + "] Dropped bad message: " + e.Message);
                }
            }

            if (!m_closed && InRoom && Time.realtimeSinceStartup - m_lastPing >= pingSeconds)
            {
                m_lastPing = Time.realtimeSinceStartup;
                SendText("PING:" + Environment.TickCount + "," + PingMs);
            }
        }

        void OnPlayerInfo(PlayerInfo info)
        {
            if (info == null || string.IsNullOrWhiteSpace(info.playerName)) return;
            Infos[info.playerName] = info;
        }

        void OnText(string text)
        {
            int colon = text.IndexOf(':');
            string type = colon < 0 ? text : text.Substring(0, colon);
            string content = colon < 0 ? "" : text.Substring(colon + 1);

            switch (type)
            {
                case "PLAYERS":
                    Players.Clear();
                    if (content.Length > 0) Players.AddRange(content.Split(','));
                    for (int i = Infos.Count - 1; i >= 0; i--) { } // no-op for readability
                    foreach (string key in new List<string>(Infos.Keys))
                        if (!Players.Contains(key)) Infos.Remove(key);

                    if (!InRoom)
                    {
                        InRoom = true;
                        Joined?.Invoke();
                    }
                    break;

                case "ROOM":
                    RoomInfo = content;
                    break;

                case "CHAT":
                    Chat.Add(content);
                    if (Chat.Count > 60) Chat.RemoveAt(0);
                    break;

                case "PONG":
                    if (int.TryParse(content, out int sent))
                        PingMs = Environment.TickCount - sent;
                    break;

                case "START":
                    GameStarted?.Invoke();
                    break;

                case "LOBBY":
                    ReturnedToLobby?.Invoke();
                    break;

                case "KICKED":
                    Close("You were kicked by the host");
                    break;

                case "ERROR":
                    Close(content);
                    break;
            }
        }

        protected void Close(string reason)
        {
            if (m_closed) return;
            m_closed = true;
            InRoom = false;
            CloseSocket();
            Closed?.Invoke(reason);
        }

        void OnDestroy()
        {
            if (!m_closed)
            {
                m_closed = true;
                CloseSocket();
            }
        }

        protected void Log(string msg) => LobbySession.Log(msg);
    }
}