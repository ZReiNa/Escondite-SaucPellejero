using UnityEngine;
using UnityEngine.SceneManagement;

namespace HideAndSeek
{
    public class MenuUI : MonoBehaviour
    {
        void Start()
        {
            LobbySession.Instance.EndSession();
        }

        void OnGUI()
        {
            LobbySession session = LobbySession.Instance;
            LobbyGUI.Begin("HIDE & SEEK");

            session.playerName = LobbyGUI.Field("Your name", session.playerName);

            GUILayout.Space(15);
            GUI.enabled = !string.IsNullOrWhiteSpace(session.playerName);

            if (GUILayout.Button("Host (Server)", GUILayout.Width(260), GUILayout.Height(45)))
                SceneManager.LoadScene(LobbySession.CreateScene);

            if (GUILayout.Button("Guest (Client)", GUILayout.Width(260), GUILayout.Height(45)))
                SceneManager.LoadScene(LobbySession.JoinScene);

            GUI.enabled = true;
            LobbyGUI.Message(session.message);
            LobbyGUI.End();
        }
    }
}