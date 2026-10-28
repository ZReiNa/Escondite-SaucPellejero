// Lab 3 solution: L3_Menu. Your PlayerDTO2 (name, level, Pokemon), protocol, create or join.

using UnityEngine;
using UnityEngine.SceneManagement;

namespace Lab3Solution
{
    public class MenuUI : MonoBehaviour
    {
        void Start()
        {
            LobbySession.Instance.EndSession();   // coming back here always closes the old session
        }

        void OnGUI()
        {
            LobbySession session = LobbySession.Instance;
            LobbyGUI.Begin("XJO LOBBY  -  Lab 3 solution");

            session.playerName = LobbyGUI.Field("Your name", session.playerName);
            session.playerLevel = LobbyGUI.IntField("Level", session.playerLevel, 1, 100);
            session.playerTeam = LobbyGUI.Field("Pokemon", session.playerTeam, 360);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Protocol", GUILayout.Width(140));
            session.protocol = (LobbyProtocol)GUILayout.Toolbar((int)session.protocol, new[] { "TCP", "UDP" },
                                                                 GUILayout.Width(220));
            GUILayout.EndHorizontal();

            GUILayout.Space(15);
            GUI.enabled = session.playerName.Trim().Length > 0;
            if (GUILayout.Button("Create game", GUILayout.Width(220), GUILayout.Height(40)))
                SceneManager.LoadScene(LobbySession.CreateScene);
            if (GUILayout.Button("Join game", GUILayout.Width(220), GUILayout.Height(40)))
                SceneManager.LoadScene(LobbySession.JoinScene);
            GUI.enabled = true;

            LobbyGUI.Message(session.message);
            LobbyGUI.End();
        }
    }
}
