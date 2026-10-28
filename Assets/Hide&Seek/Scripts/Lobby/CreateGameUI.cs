using UnityEngine;
using UnityEngine.SceneManagement;

namespace HideAndSeek
{
    public class CreateGameUI : MonoBehaviour
    {
        string m_roomName = "HideAndSeekRoom";
        string m_port = "9050";
        int m_maxPlayers = 8;
        bool m_creating;

        void OnGUI()
        {
            LobbySession session = LobbySession.Instance;
            LobbyGUI.Begin("CREATE GAME");

            GUI.enabled = !m_creating;
            m_roomName = LobbyGUI.Field("Room name", m_roomName);
            m_port = LobbyGUI.Field("Port", m_port, 120);
            m_maxPlayers = LobbyGUI.IntField("Max players", m_maxPlayers, 2, 8);

            GUILayout.Space(15);
            GUILayout.BeginHorizontal();

            bool valid = int.TryParse(m_port, out int port) && !string.IsNullOrWhiteSpace(m_roomName);
            GUI.enabled = !m_creating && valid;
            if (GUILayout.Button(m_creating ? "Creating..." : "Create", GUILayout.Width(160), GUILayout.Height(40)))
                m_creating = session.Host(m_roomName.Trim(), port, m_maxPlayers);

            GUI.enabled = true;
            if (GUILayout.Button("Back", GUILayout.Width(120), GUILayout.Height(40)))
            {
                session.EndSession();
                SceneManager.LoadScene(LobbySession.MenuScene);
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            GUILayout.Label("Guests must use your LAN IP and port " + m_port);
            LobbyGUI.Message(session.message);
            LobbyGUI.End();
        }
    }
}