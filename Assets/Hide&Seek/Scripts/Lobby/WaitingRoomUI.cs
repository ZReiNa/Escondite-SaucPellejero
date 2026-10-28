using UnityEngine;

namespace HideAndSeek
{
    public class WaitingRoomUI : MonoBehaviour
    {
        string m_chatInput = "";

        void OnGUI()
        {
            if (LobbyGUI.NeedsSession()) return;

            LobbySession session = LobbySession.Instance;
            LobbyClient client = session.Client;

            LobbyGUI.Begin("WAITING ROOM   " + client.RoomInfo);

            GUILayout.BeginHorizontal();
            GUILayout.Label("You are " + client.userName + (session.IsHost ? " (host)" : "") +
                            "   ping " + (client.PingMs < 0 ? "-" : client.PingMs + " ms"));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawPlayers(session, client);
            GUILayout.Space(15);
            m_chatInput = LobbyGUI.Chat(client, m_chatInput, Screen.height - 220);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            bool canStart = session.IsHost && client.Players.Count >= 2;
            GUI.enabled = canStart;
            if (GUILayout.Button("Start Game", GUILayout.Width(180), GUILayout.Height(40)))
                session.Server.StartGame();
            GUI.enabled = true;

            if (GUILayout.Button(session.IsHost ? "Close Room" : "Leave", GUILayout.Width(160), GUILayout.Height(40)))
                client.Leave();

            GUILayout.EndHorizontal();
            LobbyGUI.End();
        }

        static void DrawPlayers(LobbySession session, LobbyClient client)
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(460), GUILayout.Height(Screen.height - 220));
            GUILayout.Label("Players (" + client.Players.Count + (session.IsHost ? "/" + session.Server.maxPlayers : "") + ")");

            foreach (string name in client.Players)
            {
                GUILayout.BeginHorizontal();

                string line = name + (name == client.userName ? " (you)" : "");
                LobbyServer.Player hosted = session.IsHost ? FindHosted(session, name) : null;
                if (hosted != null) line += "   " + (hosted.pingMs < 0 ? "-" : hosted.pingMs + " ms");
                GUILayout.Label(line, GUILayout.Width(320));

                bool canKick = hosted != null && name != client.userName && GUILayout.Button("Kick", GUILayout.Width(70));
                GUILayout.EndHorizontal();

                if (canKick)
                {
                    session.Server.Kick(hosted);
                    break;
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
        }

        static LobbyServer.Player FindHosted(LobbySession session, string name)
        {
            foreach (LobbyServer.Player p in session.Server.Players)
                if (p.name == name) return p;
            return null;
        }
    }
}