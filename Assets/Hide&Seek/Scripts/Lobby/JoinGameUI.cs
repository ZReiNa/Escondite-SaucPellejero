using UnityEngine;
using UnityEngine.SceneManagement;

namespace HideAndSeek
{
    public class JoinGameUI : MonoBehaviour
    {
        string m_ip = "127.0.0.1";
        string m_port = "9050";

        void OnGUI()
        {
            LobbySession session = LobbySession.Instance;
            bool joining = session.Client != null;

            LobbyGUI.Begin("JOIN GAME");

            GUI.enabled = !joining;
            m_ip = LobbyGUI.Field("Host IP", m_ip);
            m_port = LobbyGUI.Field("Port", m_port, 120);

            GUILayout.Space(15);
            GUILayout.BeginHorizontal();

            bool valid = int.TryParse(m_port, out int port) && !string.IsNullOrWhiteSpace(m_ip);
            GUI.enabled = !joining && valid;
            if (GUILayout.Button(joining ? "Joining..." : "Join", GUILayout.Width(160), GUILayout.Height(40)))
                session.Join(m_ip.Trim(), port);

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