// =================================================================================================
//  Lab 3 - Challenge 3 solution: the lobby client, shared by TCP and UDP
// =================================================================================================
//
//  The Lab 2.2 solution's LobbyClient with serialization added. What changed:
//    - messages arrive as byte[] and are read with Messages.Read (type byte first, slide 18)
//    - joining sends 'myData', a PlayerDTO2, instead of JOIN:<name> (see JoinMessage)
//    - Player messages from the server fill 'Teams', so the waiting room shows every team
//  Everything else is the 2.2 code, travelling as Text messages.
//
//  The client PINGs every second with its own clock time. The server echoes it back in PONG, so
//  now - echoed time = round trip. The client reports that number in its next PING, which is how
//  the host gets the ping of every player. In UDP the same PING is what keeps the player alive.
// =================================================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

namespace Lab3Solution
{
    public abstract class LobbyClient : MonoBehaviour
    {
        public string serverIp = "127.0.0.1";
        public int port = 9050;
        public PlayerDTO2 myData;              // sent when joining. myData.playerName is our name
        public float pingSeconds = 1f;

        public string userName { get { return myData.playerName; } }

        public readonly List<string> Players = new List<string>();
        public readonly Dictionary<string, PlayerDTO2> Teams = new Dictionary<string, PlayerDTO2>();
        public readonly List<string> Chat = new List<string>();
        public string RoomInfo = "";
        public int PingMs = -1;
        public bool InRoom { get; private set; }

        public event Action Joined;            // first player list received: we are in
        public event Action GameStarted;       // the host pressed Start
        public event Action<string> Closed;    // why we are out: left, kicked, refused, lost

        // Network thread to main thread. null means "the connection ended" (see m_endReason).
        protected readonly ConcurrentQueue<byte[]> m_inbox = new ConcurrentQueue<byte[]>();
        protected volatile string m_endReason = "Connection to the server lost";
        protected float m_lastHeard;           // last time anything arrived from the server
        float m_lastPing;
        bool m_closed;

        // ------------------------------------------------------------------ what TCP and UDP provide

        public abstract void Connect();
        protected abstract void SendBytes(byte[] message);
        protected abstract void CloseSocket();

        protected void SendText(string text) { SendBytes(Messages.Text(text)); }

        // 2.2 sent JOIN:<name>. Now joining is our whole PlayerDTO2.
        protected byte[] JoinMessage() { return Messages.Player(myData); }

        // ------------------------------------------------------------------------ buttons and UI

        public void SendChat(string line)
        {
            line = line.Trim();
            if (line.Length > 0) SendText("CHAT:" + line);
        }

        // Level or team changed in the waiting room: send the whole PlayerDTO2 again. The server
        // takes it as an update because it comes from a player already in the room.
        public void UpdateData(PlayerDTO2 data)
        {
            data.playerName = userName;
            myData = data;
            SendBytes(JoinMessage());
        }

        public void Leave()
        {
            SendText("LEAVE:");
            Close("You left the room");
        }

        // ----------------------------------------------------------------------------- main thread

        protected virtual void Update()
        {
            byte[] data;
            while (!m_closed && m_inbox.TryDequeue(out data))
            {
                if (data == null) { Close(m_endReason); break; }
                m_lastHeard = Time.realtimeSinceStartup;

                string text;
                PlayerDTO2 dto;
                MsgType type;
                try { type = Messages.Read(data, out text, out dto); }
                catch (Exception e) { Log("[CLIENT " + userName + "] Dropped a bad message: " + e.Message); continue; }

                if (type == MsgType.Player) OnPlayerData(dto);
                else OnText(text);
            }

            if (!m_closed && InRoom && Time.realtimeSinceStartup - m_lastPing >= pingSeconds)
            {
                m_lastPing = Time.realtimeSinceStartup;
                SendText("PING:" + Environment.TickCount + "," + PingMs);
            }
        }

        void OnPlayerData(PlayerDTO2 dto)
        {
            PlayerDTO2 old;
            bool changed = !Teams.TryGetValue(dto.playerName, out old) || Messages.Describe(old) != Messages.Describe(dto);
            Teams[dto.playerName] = dto;
            if (changed) Log("[CLIENT " + userName + "] Data of " + dto.playerName + ": " + Messages.Describe(dto));
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
                    // Forget the data of players who are gone.
                    foreach (string name in new List<string>(Teams.Keys))
                        if (!Players.Contains(name)) Teams.Remove(name);
                    Log("[CLIENT " + userName + "] Players (" + Players.Count + "): " + string.Join(", ", Players));
                    if (!InRoom) { InRoom = true; if (Joined != null) Joined(); }
                    break;

                case "ROOM":
                    RoomInfo = content;
                    break;

                case "CHAT":
                    Chat.Add(content);
                    if (Chat.Count > 50) Chat.RemoveAt(0);
                    Log("[CLIENT " + userName + "] Chat: " + content);
                    break;

                case "PONG":
                    int sent;
                    if (int.TryParse(content, out sent)) PingMs = Environment.TickCount - sent;
                    break;

                case "START":
                    Log("[CLIENT " + userName + "] The host started the game");
                    if (GameStarted != null) GameStarted();
                    break;

                case "KICKED":
                    Close("The host kicked you out of the room");
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
            Log("[CLIENT " + userName + "] Closed: " + reason);
            if (Closed != null) Closed(reason);
        }

        void OnDestroy() { if (!m_closed) { m_closed = true; CloseSocket(); } }

        protected void Log(string message) { LobbySession.Log(message); }
    }
}
