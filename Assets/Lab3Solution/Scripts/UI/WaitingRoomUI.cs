// Lab 3 solution: L3_WaitingRoom. Player list with every player's PlayerDTO2 (level, Pokemon),
// chat, Leave, and your own level to change: it is sent again as a Player message and everybody's
// list updates. The host also gets every player's ping, Kick buttons and Start.
//
// The host reads pings and connections straight from its own server object. Everybody else only
// has what the server sent: PLAYERS: for the names, Player messages for the data.

using UnityEngine;

namespace Lab3Solution
{
    public class WaitingRoomUI : MonoBehaviour
    {
        string m_chatInput = "";

        void OnGUI()
        {
            if (LobbyGUI.NeedsSession()) return;
            LobbySession session = LobbySession.Instance;
            LobbyClient client = session.Client;

            LobbyGUI.Begin("WAITING ROOM  (" + session.protocol + ")   " + client.RoomInfo);
            GUILayout.BeginHorizontal();
            GUILayout.Label("You are " + client.userName + (session.IsHost ? " (host)" : "") +
                            "    ping " + (client.PingMs < 0 ? "-" : client.PingMs + " ms"), GUILayout.Width(420));
            int level = LobbyGUI.IntField("Your level", session.playerLevel, 1, 100);
            if (level != session.playerLevel)
            {
                session.playerLevel = level;
                client.UpdateData(session.MakePlayerData());
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawPlayers(session, client);
            GUILayout.Space(15);
            m_chatInput = LobbyGUI.Chat(client, m_chatInput, Screen.height - 200);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (session.IsHost && GUILayout.Button("Start game", GUILayout.Width(180), GUILayout.Height(40)))
                session.Server.StartGame();
            if (GUILayout.Button(session.IsHost ? "Close room" : "Leave", GUILayout.Width(160), GUILayout.Height(40)))
                client.Leave();
            GUILayout.EndHorizontal();

            LobbyGUI.End();
        }

        static void DrawPlayers(LobbySession session, LobbyClient client)
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(460), GUILayout.Height(Screen.height - 200));
            GUILayout.Label("Players (" + client.Players.Count +
                            (session.IsHost ? "/" + session.Server.maxPlayers : "") + ")");
            foreach (string name in client.Players)
            {
                GUILayout.BeginHorizontal();
                string line = name + (name == client.userName ? " (you)" : "");
                LobbyServer.Player hosted = session.IsHost ? FindHosted(session, name) : null;
                if (hosted != null) line += "   " + (hosted.pingMs < 0 ? "-" : hosted.pingMs + " ms");
                GUILayout.Label(line, GUILayout.Width(300));
                bool kick = hosted != null && name != client.userName && GUILayout.Button("Kick", GUILayout.Width(70));
                GUILayout.EndHorizontal();

                PlayerDTO2 data;
                GUILayout.Label("    " + (client.Teams.TryGetValue(name, out data) ? Messages.Describe(data) : "..."));

                if (kick)
                {
                    session.Server.Kick(hosted);
                    break;   // the list just changed: stop drawing it this frame
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
        }

        static LobbyServer.Player FindHosted(LobbySession session, string name)
        {
            foreach (LobbyServer.Player p in session.Server.Players) if (p.name == name) return p;
            return null;
        }
    }
}
