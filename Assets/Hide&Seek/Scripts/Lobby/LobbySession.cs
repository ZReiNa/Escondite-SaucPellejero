using UnityEngine;
using UnityEngine.SceneManagement;

namespace HideAndSeek
{
    public class LobbySession : MonoBehaviour
    {
        public const string MenuScene = "HS_Menu";
        public const string CreateScene = "HS_CreateGame";
        public const string JoinScene = "HS_JoinGame";
        public const string RoomScene = "HS_WaitingRoom";
        public const string GameScene = "HS_Game";

        public string playerName = "Player";
        public string message = "";

        public LobbyServer Server { get; private set; }
        public LobbyClient Client { get; private set; }
        public bool IsHost => Server != null;

        static LobbySession s_instance;

        public static LobbySession Instance
        {
            get
            {
                if (s_instance == null)
                {
                    GameObject go = new GameObject("LobbySession");
                    DontDestroyOnLoad(go);
                    s_instance = go.AddComponent<LobbySession>();
                }
                return s_instance;
            }
        }

        public static void Log(string text) => Debug.Log(text);

        void Awake()
        {
            if (s_instance != null && s_instance != this) { Destroy(gameObject); return; }
            s_instance = this;
            Application.runInBackground = true;
        }

        public bool Host(string serverName, int port, int maxPlayers)
        {
            EndSession();

            Server = gameObject.AddComponent<UDPLobbyServer>();
            Server.serverName = serverName;
            Server.port = port;
            Server.maxPlayers = Mathf.Clamp(maxPlayers, 2, 8);

            if (!Server.StartServer())
            {
                Destroy(Server);
                Server = null;
                message = "Could not create the room: port " + port + " is in use";
                return false;
            }

            Join("127.0.0.1", port);
            return true;
        }

        public void Join(string ip, int port)
        {
            if (Client != null) Destroy(Client);
            message = "";

            Client = gameObject.AddComponent<UDPLobbyClient>();
            Client.serverIp = ip.Trim();
            Client.port = port;
            Client.myInfo = new PlayerInfo { playerName = string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName.Trim() };

            Client.Joined += () => SceneManager.LoadScene(RoomScene);
            Client.GameStarted += () => SceneManager.LoadScene(GameScene);
            Client.ReturnedToLobby += () => SceneManager.LoadScene(RoomScene);
            Client.Closed += OnClientClosed;
            Client.Connect();
        }

        void OnClientClosed(string reason)
        {
            message = reason;
            EndSession();
            SceneManager.LoadScene(MenuScene);
        }

        public void EndSession()
        {
            if (Client != null) { Destroy(Client); Client = null; }
            if (Server != null) { Server.StopServer(); Destroy(Server); Server = null; }
        }
    }
}