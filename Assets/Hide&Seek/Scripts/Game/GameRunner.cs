using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HideAndSeek
{
    public class GameRunner : MonoBehaviour
    {
        public int gamePortOffset = 1;
        public float inputSendRate = 30f;

        UDPGameServer m_server;
        UDPGameClient m_client;

        FPSLocalInput m_localInput;
        CharacterController m_localController;
        Transform m_localRoot;

        readonly Dictionary<int, GameObject> m_remotePlayers = new Dictionary<int, GameObject>();
        readonly Dictionary<int, GameObject> m_projectiles = new Dictionary<int, GameObject>();

        WorldSnapshot m_lastSnapshot;
        int m_localNetId = -1;
        float m_nextInputSend;
        float m_localVerticalSpeed;
        bool m_alive = true;
        bool m_placed;
        bool m_wasRoundEnded;
        bool m_pendingFire;
        bool m_pendingJump;
        string m_winnerName = "-";

        const float WalkSpeed = 4.5f;
        const float RunSpeed = 7.5f;
        const float Gravity = 18f;
        const float JumpSpeed = 6f;
        const float SnapDistance = 3f;

        void Start()
        {
            LobbySession session = LobbySession.Instance;
            if (session.Client == null) { SceneManager.LoadScene(LobbySession.MenuScene); return; }

            int gamePort = session.Client.port + gamePortOffset;

            if (session.IsHost)
            {
                m_server = session.gameObject.AddComponent<UDPGameServer>();
                m_server.StartServer(gamePort, session.Client.Players.Count);
            }

            m_client = session.gameObject.AddComponent<UDPGameClient>();
            m_client.OnWelcomed += id => m_localNetId = id;
            m_client.OnSnapshot += snap => { m_lastSnapshot = snap; m_winnerName = ResolveWinnerName(snap); };
            m_client.StartClient(session.IsHost ? "127.0.0.1" : session.Client.serverIp, gamePort, session.playerName);

            SetupLocalPlayer();
            LockCursor(true);
        }

        void OnDestroy()
        {
            LockCursor(false);
            if (m_client != null) Destroy(m_client);
            if (m_server != null) Destroy(m_server);
        }

        void SetupLocalPlayer()
        {
            GameObject local = new GameObject("LocalPlayer");
            m_localRoot = local.transform;
            m_localRoot.position = new Vector3(0f, 0f, 0f);

            m_localController = local.AddComponent<CharacterController>();
            m_localController.height = 1.8f;
            m_localController.radius = 0.35f;
            m_localController.center = new Vector3(0f, 0.9f, 0f);

            GameObject pivot = new GameObject("CameraPivot");
            pivot.transform.SetParent(local.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 1.6f, 0f);

            GameObject camObj = new GameObject("PlayerCamera");
            camObj.transform.SetParent(pivot.transform, false);
            camObj.AddComponent<Camera>();
            camObj.AddComponent<AudioListener>();
            camObj.tag = "MainCamera";

            m_localInput = local.AddComponent<FPSLocalInput>();
            m_localInput.cameraPivot = pivot.transform;
        }

        void Update()
        {
            LobbySession session = LobbySession.Instance;
            if (session.Client == null || m_client == null) return;

            if (Input.GetKeyDown(KeyCode.Escape)) LockCursor(Cursor.lockState != CursorLockMode.Locked);

            bool cursorLocked = Cursor.lockState == CursorLockMode.Locked;
            bool canPlay = m_client.IsWelcomed && m_placed && m_alive && !m_lastSnapshot.roundEnded;

            if (canPlay)
            {
                if (cursorLocked && Input.GetMouseButtonDown(0)) m_pendingFire = true;
                if (cursorLocked && Input.GetKeyDown(KeyCode.Space)) m_pendingJump = true;

                InputCommand cmd = m_localInput.BuildInput();
                MoveLocal(cmd);

                if (Time.time >= m_nextInputSend)
                {
                    m_nextInputSend = Time.time + 1f / inputSendRate;
                    cmd.firePressed = m_pendingFire;
                    cmd.jumpPressed = m_pendingJump;
                    m_pendingFire = false;
                    m_pendingJump = false;
                    m_client.SendInput(cmd);
                }
            }

            ApplySnapshot();
        }

        void MoveLocal(InputCommand cmd)
        {
            float dt = Time.deltaTime;
            Vector3 wish = m_localRoot.right * cmd.moveX + m_localRoot.forward * cmd.moveY;
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            float speed = (cmd.runHeld && cmd.moveY > 0.01f) ? RunSpeed : WalkSpeed;

            if (m_localController.isGrounded)
            {
                m_localVerticalSpeed = -1f;
                if (m_pendingJump) m_localVerticalSpeed = JumpSpeed;
            }
            else m_localVerticalSpeed -= Gravity * dt;

            m_localController.Move((wish * speed + Vector3.up * m_localVerticalSpeed) * dt);

            Vector3 p = m_localRoot.position;
            float limit = GameServerSimulation.ArenaHalf;
            if (Mathf.Abs(p.x) > limit || Mathf.Abs(p.z) > limit)
                Teleport(new Vector3(Mathf.Clamp(p.x, -limit, limit), p.y, Mathf.Clamp(p.z, -limit, limit)));
        }

        void Teleport(Vector3 position)
        {
            m_localController.enabled = false;
            m_localRoot.position = position;
            m_localController.enabled = true;
            m_localVerticalSpeed = 0f;
        }

        void ApplySnapshot()
        {
            if (m_lastSnapshot.players == null) return;

            HashSet<int> present = new HashSet<int>();
            foreach (PlayerSnapshot p in m_lastSnapshot.players)
            {
                present.Add(p.netId);

                if (p.netId == m_localNetId)
                {
                    bool restarted = m_wasRoundEnded && !m_lastSnapshot.roundEnded;
                    if (!m_placed || restarted || (p.alive && Vector3.Distance(m_localRoot.position, p.position) > SnapDistance))
                    {
                        Teleport(p.position);
                        m_placed = true;
                    }
                    m_alive = p.alive;
                    continue;
                }

                if (!m_remotePlayers.TryGetValue(p.netId, out GameObject go))
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    go.name = "Remote_" + p.name;
                    go.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
                    m_remotePlayers[p.netId] = go;
                    go.transform.position = p.position + Vector3.up * 0.9f;
                }

                go.SetActive(p.alive);
                go.transform.position = Vector3.Lerp(go.transform.position, p.position + Vector3.up * 0.9f, 0.35f);
                go.transform.rotation = Quaternion.Euler(0f, p.yaw, 0f);
            }
            m_wasRoundEnded = m_lastSnapshot.roundEnded;

            RemoveMissing(m_remotePlayers, present);

            HashSet<int> shots = new HashSet<int>();
            foreach (ProjectileSnapshot pr in m_lastSnapshot.projectiles)
            {
                shots.Add(pr.projectileId);
                if (!m_projectiles.TryGetValue(pr.projectileId, out GameObject proj))
                {
                    proj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    proj.transform.localScale = Vector3.one * 0.15f;
                    Destroy(proj.GetComponent<Collider>());
                    m_projectiles[pr.projectileId] = proj;
                }
                proj.transform.position = pr.position;
            }
            RemoveMissing(m_projectiles, shots);
        }

        static void RemoveMissing(Dictionary<int, GameObject> objects, HashSet<int> keep)
        {
            List<int> remove = new List<int>();
            foreach (int id in objects.Keys) if (!keep.Contains(id)) remove.Add(id);
            foreach (int id in remove) { Destroy(objects[id]); objects.Remove(id); }
        }

        static string ResolveWinnerName(WorldSnapshot snap)
        {
            if (!snap.roundEnded || snap.players == null) return "-";
            foreach (PlayerSnapshot p in snap.players) if (p.netId == snap.winnerNetId) return p.name;
            return "Nobody";
        }

        void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        void OnGUI()
        {
            LobbySession session = LobbySession.Instance;
            if (session.Client == null) return;

            GUI.Label(new Rect(Screen.width / 2f - 6f, Screen.height / 2f - 12f, 24f, 24f), "+");

            GUILayout.BeginArea(new Rect(10, 10, 420, 260));
            GUILayout.Label("HIDE & SEEK   " + (m_alive ? "ALIVE" : "DEAD") + "   ping " + (session.Client.PingMs < 0 ? "-" : session.Client.PingMs + " ms"));
            GUILayout.Label("WASD move | Shift run | Space jump | LMB shoot | ESC cursor");

            if (m_lastSnapshot.roundEnded)
            {
                GUILayout.Label("<b>Round over. Winner: " + m_winnerName + "</b>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = 22 });

                if (session.IsHost)
                {
                    if (GUILayout.Button("Play again", GUILayout.Width(200), GUILayout.Height(34))) m_server.RestartRound();
                    if (GUILayout.Button("Back to lobby", GUILayout.Width(200), GUILayout.Height(34))) session.Server.ReturnToLobbyForAll();
                }
                else GUILayout.Label("Waiting for the host...");
            }

            if (GUILayout.Button(session.IsHost ? "Close room" : "Leave", GUILayout.Width(200), GUILayout.Height(34)))
                session.Client.Leave();

            GUILayout.EndArea();
        }
    }
}