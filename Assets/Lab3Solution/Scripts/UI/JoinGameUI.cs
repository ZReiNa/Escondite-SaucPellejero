// Lab 3 solution: L3_JoinGame. Server IP and port, then Join.
// The scene changes to L3_WaitingRoom on the first player list, or back to L3_Menu with the reason
// (refused, room full, name taken, no answer).

using UnityEngine;
using UnityEngine.SceneManagement;

namespace Lab3Solution
{
    public class JoinGameUI : MonoBehaviour
    {
        string m_ip = "127.0.0.1";
        string m_port = "9050";

        void OnGUI()
        {
            LobbySession session = LobbySession.Instance;
            bool joining = session.Client != null;
            LobbyGUI.Begin("JOIN GAME  (" + session.protocol + ")");

            GUI.enabled = !joining;
            m_ip = LobbyGUI.Field("Server IP", m_ip);
            m_port = LobbyGUI.Field("Port", m_port, 100);

            GUILayout.Space(15);
            GUILayout.BeginHorizontal();
            int port;
            GUI.enabled = !joining && int.TryParse(m_port, out port);
            if (GUILayout.Button(joining ? "Joining..." : "Join", GUILayout.Width(160), GUILayout.Height(40)))
                session.Join(m_ip, int.Parse(m_port));
            GUI.enabled = true;
            if (GUILayout.Button(joining ? "Cancel" : "Back", GUILayout.Width(120), GUILayout.Height(40)))
            {
                session.EndSession();
                SceneManager.LoadScene(LobbySession.MenuScene);
            }
            GUILayout.EndHorizontal();

            LobbyGUI.Message(session.message);
            LobbyGUI.End();
        }
    }
}
