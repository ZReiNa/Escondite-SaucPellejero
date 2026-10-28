// =================================================================================================
//  Lab 3 - Challenge 3 solution: the lobby server, shared by TCP and UDP
//  XJO - Xarxes per a Jocs Online
// =================================================================================================
//
//  The Lab 2.2 solution's LobbyServer with serialization added. What changed:
//    - messages arrive as byte[] and are read with Messages.Read (type byte first, slide 18)
//    - joining is a Player message: the client's PlayerDTO2 replaces 2.2's JOIN:<name>
//    - the server keeps every player's PlayerDTO2 and sends all of them to everyone on each
//      change, as Player messages, so every waiting room shows every team
//  Everything else (CHAT, PING, LEAVE, kick, start, the rules of the room) is the 2.2 code,
//  travelling as Text messages.
//
//  MESSAGES
//    client to server   Player(PlayerDTO2)   Text: CHAT:<text>  LEAVE:  PING:<clientMs>,<lastPingMs>
//    server to client   Player(PlayerDTO2), one per player
//                       Text: PLAYERS:<name>,...  CHAT:<name>: <text>  ROOM:<info>  PONG:<clientMs>
//                             ERROR:<reason>  KICKED:  START:
//
//  THREADS (Lab 2.2 slide 10)
//    Network threads only push into m_inbox. Update() empties it on the main thread, so the
//    player list is only ever touched by the main thread and needs no lock.
// =================================================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

namespace Lab3Solution
{
    public abstract class LobbyServer : MonoBehaviour
    {
        public string serverName = "MyRoom";
        public int port = 9050;
        public int maxPlayers = 4;

        public class Player
        {
            public object connection;   // a Socket in TCP, an EndPoint in UDP
            public PlayerDTO2 data;     // what the client sent when joining
            public int pingMs = -1;     // reported by the client in every PING
            public float lastSeen;      // last time anything arrived from it (UDP timeout)
            public string name { get { return data.playerName; } }
        }

        // Network thread to main thread. data == null means "this connection is gone" (TCP only).
        protected struct NetEvent { public object from; public byte[] data; }
        protected readonly ConcurrentQueue<NetEvent> m_inbox = new ConcurrentQueue<NetEvent>();

        readonly List<Player> m_players = new List<Player>();   // main thread only

        public IList<Player> Players { get { return m_players.AsReadOnly(); } }
        public bool Started { get; private set; }
        public abstract bool IsRunning { get; }

        // ------------------------------------------------------------------ what TCP and UDP provide

        public abstract bool StartServer();                         // false if the port is taken
        public abstract void StopServer();
        protected abstract void Send(object connection, byte[] message);
        protected abstract void Drop(object connection);           // stop talking to it
        protected virtual string Describe(object connection) { return connection.ToString(); }

        protected void SendText(object connection, string text) { Send(connection, Messages.Text(text)); }

        // ----------------------------------------------------------------------------- main thread

        protected virtual void Update()
        {
            NetEvent e;
            while (m_inbox.TryDequeue(out e))
            {
                if (e.data == null) { OnConnectionLost(e.from); continue; }

                string text;
                PlayerDTO2 dto;
                MsgType type;
                try { type = Messages.Read(e.data, out text, out dto); }
                catch (Exception ex)
                {
                    // Garbage, or a 2.2 client still sending plain text: drop it, keep the room alive.
                    Log("[SERVER] Dropped a bad message from " + Describe(e.from) + ": " + ex.Message);
                    continue;
                }

                Player player = Find(e.from);
                if (player != null) player.lastSeen = Time.realtimeSinceStartup;

                if (type == MsgType.Player) OnPlayerData(e.from, player, dto);
                else OnText(e.from, player, text);
            }
        }

        void OnText(object from, Player player, string text)
        {
            int colon = text.IndexOf(':');
            string type = colon < 0 ? text : text.Substring(0, colon);
            string content = colon < 0 ? "" : text.Substring(colon + 1);

            switch (type)
            {
                case "CHAT":
                    if (player == null) break;                 // only players in the room may talk
                    Log("[CHAT] " + player.name + ": " + content);
                    Broadcast("CHAT:" + player.name + ": " + content);
                    break;

                case "PING":
                    if (player == null) break;
                    // content = "<clientMs>,<lastPingMs>". Echo the first part, keep the second.
                    string[] parts = content.Split(',');
                    SendText(from, "PONG:" + parts[0]);
                    int ping;
                    if (parts.Length > 1 && int.TryParse(parts[1], out ping)) player.pingMs = ping;
                    break;

                case "LEAVE":
                    if (player != null) RemovePlayer(player, "left");
                    Drop(from);
                    break;
            }
        }

        // The PlayerDTO2 is the join request. From somebody already in the room it is a repeat
        // (UDP resends until answered) or an update of its data.
        void OnPlayerData(object from, Player player, PlayerDTO2 dto)
        {
            // PLAYERS: is still a comma-separated text list, so names keep the 2.2 restrictions.
            string name = (dto.playerName ?? "").Replace(",", "").Replace(":", "").Trim();
            if (name.Length == 0) name = "Player";
            dto.playerName = name;
            if (dto.ownedPokemons == null) dto.ownedPokemons = new List<Pokemon>();

            if (player != null)
            {
                if (dto.playerName != player.name) { SendRoomStateTo(from); return; }   // no renames in the room
                if (Messages.Describe(dto) == Messages.Describe(player.data)) { SendRoomStateTo(from); return; }
                player.data = dto;
                Log("[SERVER] " + name + " updated: " + Messages.Describe(dto));
                SendRoomState();
                return;
            }

            if (Started)                       { Refuse(from, "The game has already started"); return; }
            if (m_players.Count >= maxPlayers) { Refuse(from, "The room is full (" + maxPlayers + "/" + maxPlayers + ")"); return; }
            if (FindByName(name) != null)      { Refuse(from, "The name " + name + " is already taken"); return; }

            m_players.Add(new Player { connection = from, data = dto, lastSeen = Time.realtimeSinceStartup });
            Log("[SERVER] " + name + " joined (" + Describe(from) + "): " + Messages.Describe(dto));
            BroadcastSystem(name + " joined");
            SendRoomState();
        }

        void Refuse(object to, string reason)
        {
            Log("[SERVER] Refused " + Describe(to) + ": " + reason);
            SendText(to, "ERROR:" + reason);
            Drop(to);
        }

        // TCP: the client thread saw Receive return 0 or fail.
        void OnConnectionLost(object connection)
        {
            Player player = Find(connection);
            if (player != null) RemovePlayer(player, "disconnected");
        }

        protected void RemovePlayer(Player player, string reason)
        {
            if (!m_players.Remove(player)) return;
            Log("[SERVER] " + player.name + " " + reason);
            BroadcastSystem(player.name + " " + reason);
            SendRoomState();
        }

        // ------------------------------------------------------------------------- host buttons

        public void Kick(Player player)
        {
            object connection = player.connection;
            SendText(connection, "KICKED:");
            RemovePlayer(player, "was kicked");
            Drop(connection);
        }

        public void StartGame()
        {
            if (Started) return;
            Started = true;
            Log("[SERVER] Game started with " + Names(", "));
            Broadcast("START:");
        }

        // ------------------------------------------------------------------------------ helpers

        // The server owns the list: on every change everybody gets all of it again. First every
        // player's data, then PLAYERS, so a client never shows a name it has no data for.
        void SendRoomState()
        {
            Log("[SERVER] Players (" + m_players.Count + "/" + maxPlayers + "): " + Names(", "));
            foreach (Player p in m_players.ToArray()) SendRoomStateTo(p.connection);
        }

        void SendRoomStateTo(object connection)
        {
            foreach (Player p in m_players.ToArray()) Send(connection, Messages.Player(p.data));
            SendText(connection, "PLAYERS:" + Names(","));
            SendText(connection, "ROOM:" + serverName + "   " + m_players.Count + "/" + maxPlayers + " players");
        }

        void BroadcastSystem(string text) { Broadcast("CHAT:* " + text); }

        // ToArray: Send can fail and remove a player while we are still looping.
        protected void Broadcast(string text)
        {
            byte[] message = Messages.Text(text);
            foreach (Player p in m_players.ToArray()) Send(p.connection, message);
        }

        public string Names(string separator)
        {
            List<string> names = new List<string>();
            foreach (Player p in m_players) names.Add(p.name);
            return string.Join(separator, names);
        }

        // Equals, not ==: two IPEndPoint objects with the same address and port are the same client.
        protected Player Find(object connection)
        {
            foreach (Player p in m_players) if (p.connection.Equals(connection)) return p;
            return null;
        }

        Player FindByName(string name)
        {
            foreach (Player p in m_players) if (p.name == name) return p;
            return null;
        }

        protected void ClearPlayers() { m_players.Clear(); Started = false; }

        protected void Log(string message) { LobbySession.Log(message); }
    }
}
