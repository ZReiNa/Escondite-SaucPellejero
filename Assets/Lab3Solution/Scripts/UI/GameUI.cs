// Lab 3 solution: L3_Game. Everybody lands here when the host presses Start.
// Placeholder for the game of Deliverable 3 (Lab 4): the connection, the player list and the chat carry on.

using UnityEngine;

namespace Lab3Solution
{
    public class GameUI : MonoBehaviour
    {
        string m_chatInput = "";

        void OnGUI()
        {
            if (LobbyGUI.NeedsSession()) return;
            LobbySession session = LobbySession.Instance;
            LobbyClient client = session.Client;

            LobbyGUI.Begin("GAME  (" + session.protocol + ")   " + client.RoomInfo);
            GUILayout.Label("Deliverable 3 (Lab 4) starts here. The connection from the lobby is still open.");
            GUILayout.Label("Players: " + string.Join(", ", client.Players) +
                            "    ping " + (client.PingMs < 0 ? "-" : client.PingMs + " ms"));

            m_chatInput = LobbyGUI.Chat(client, m_chatInput, Screen.height - 220);

            if (GUILayout.Button(session.IsHost ? "End game" : "Leave", GUILayout.Width(160), GUILayout.Height(40)))
                client.Leave();
            LobbyGUI.End();
        }
    }
}
