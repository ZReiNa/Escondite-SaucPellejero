using System.Collections.Generic;
using UnityEngine;

namespace HideAndSeek
{
    public class GameServerSimulation
    {
        public const float ArenaHalf = 24f;

        class SimPlayer
        {
            public int netId;
            public string name;
            public Vector3 position;      
            public float yaw;
            public float pitch;
            public float verticalSpeed;
            public bool alive = true;
            public float fireCooldown;
            public InputCommand input;
            public bool hasInput;
            public int lastTick = -1;
            public bool pendingJump;
            public bool pendingFire;
        }

        class SimProjectile
        {
            public int id;
            public Vector3 position;
            public Vector3 velocity;
            public float life;
        }

        readonly Dictionary<int, SimPlayer> m_players = new Dictionary<int, SimPlayer>();
        readonly List<SimProjectile> m_projectiles = new List<SimProjectile>();

        int m_expected;
        float m_waited;
        bool m_started;
        int m_serverTick;
        int m_nextProjectileId = 1;
        bool m_roundEnded;
        int m_winnerNetId = -1;

        const float WalkSpeed = 4.5f;
        const float RunSpeed = 7.5f;
        const float JumpSpeed = 6f;
        const float Gravity = 18f;
        const float StartTimeout = 8f;

        const float ProjectileSpeed = 40f;
        const float ProjectileLife = 1.0f;
        const float EyeHeight = 1.6f;
        const float BodyCenter = 0.9f;
        const float HitRadius = 0.6f;
        const float FireCooldownSeconds = 0.25f;
        const float MaxShotRange = 80f;

        static readonly Vector3[] SpawnPoints =
        {
            new Vector3(-18f, 0f, -18f), new Vector3(18f, 0f, -18f),
            new Vector3(-18f, 0f, 18f),  new Vector3(18f, 0f, 18f),
            new Vector3(0f, 0f, -20f),   new Vector3(20f, 0f, 0f),
            new Vector3(0f, 0f, 20f),    new Vector3(-20f, 0f, 0f)
        };

        public void Initialize(int expectedPlayers)
        {
            m_expected = expectedPlayers;
        }

        public void RegisterPeer(int netId, string name)
        {
            if (m_players.ContainsKey(netId)) return;

            m_players[netId] = new SimPlayer
            {
                netId = netId,
                name = string.IsNullOrWhiteSpace(name) ? "Player" : name.Trim(),
                position = SpawnPoints[(m_players.Count) % SpawnPoints.Length]
            };
        }

        public void RemovePlayer(int netId)
        {
            if (!m_players.Remove(netId)) return;
            CheckRoundEnd();
        }

        public void SetInput(int netId, InputCommand input)
        {
            if (!m_players.TryGetValue(netId, out SimPlayer p)) return;
            if (input.tick <= p.lastTick) return;   // old or duplicated UDP packet

            p.lastTick = input.tick;
            p.input = input;
            p.hasInput = true;
            p.pendingJump |= input.jumpPressed;
            p.pendingFire |= input.firePressed;
        }

        public void Step(float dt)
        {
            m_serverTick++;

            if (!m_started)
            {
                m_waited += dt;
                if (m_players.Count >= m_expected || m_waited > StartTimeout) m_started = true;
                else return;
            }

            foreach (SimPlayer sp in m_players.Values)
            {
                sp.fireCooldown -= dt;
                if (!sp.alive || m_roundEnded || !sp.hasInput)
                {
                    sp.pendingJump = false;
                    sp.pendingFire = false;
                    continue;
                }

                InputCommand input = sp.input;
                sp.yaw = input.yaw;
                sp.pitch = Mathf.Clamp(input.pitch, -85f, 85f);

                float speed = (input.runHeld && input.moveY > 0.01f) ? RunSpeed : WalkSpeed;
                Quaternion yawRot = Quaternion.Euler(0f, sp.yaw, 0f);
                Vector3 wish = yawRot * Vector3.right * input.moveX + yawRot * Vector3.forward * input.moveY;
                if (wish.sqrMagnitude > 1f) wish.Normalize();

                sp.position += wish * speed * dt;
                sp.position.x = Mathf.Clamp(sp.position.x, -ArenaHalf, ArenaHalf);
                sp.position.z = Mathf.Clamp(sp.position.z, -ArenaHalf, ArenaHalf);

                if (sp.position.y <= 0f && sp.verticalSpeed <= 0f)
                {
                    sp.position.y = 0f;
                    sp.verticalSpeed = sp.pendingJump ? JumpSpeed : 0f;
                }
                else sp.verticalSpeed -= Gravity * dt;

                sp.position.y = Mathf.Max(0f, sp.position.y + sp.verticalSpeed * dt);
                sp.pendingJump = false;

                if (sp.pendingFire && sp.fireCooldown <= 0f)
                {
                    sp.fireCooldown = FireCooldownSeconds;
                    TryHit(sp);
                    SpawnTracer(sp);
                }
                sp.pendingFire = false;
            }

            CheckRoundEnd();

            for (int i = m_projectiles.Count - 1; i >= 0; i--)
            {
                SimProjectile pr = m_projectiles[i];
                pr.life -= dt;
                pr.position += pr.velocity * dt;
                if (pr.life <= 0f) m_projectiles.RemoveAt(i);
            }
        }

        static Vector3 AimDirection(SimPlayer p)
        {
            return (Quaternion.Euler(p.pitch, p.yaw, 0f) * Vector3.forward).normalized;
        }

        void TryHit(SimPlayer shooter)
        {
            Vector3 origin = shooter.position + Vector3.up * EyeHeight;
            Vector3 dir = AimDirection(shooter);

            float bestDist = float.MaxValue;
            SimPlayer best = null;

            foreach (SimPlayer target in m_players.Values)
            {
                if (!target.alive || target.netId == shooter.netId) continue;

                Vector3 center = target.position + Vector3.up * BodyCenter;
                float along = Vector3.Dot(center - origin, dir);
                if (along <= 0f || along > MaxShotRange) continue;

                Vector3 closest = origin + dir * along;
                if ((center - closest).sqrMagnitude <= HitRadius * HitRadius && along < bestDist)
                {
                    bestDist = along;
                    best = target;
                }
            }

            if (best != null) best.alive = false;
        }

        void SpawnTracer(SimPlayer owner)
        {
            Vector3 dir = AimDirection(owner);
            m_projectiles.Add(new SimProjectile
            {
                id = m_nextProjectileId++,
                position = owner.position + Vector3.up * EyeHeight + dir * 0.8f,
                velocity = dir * ProjectileSpeed,
                life = ProjectileLife
            });
        }

        void CheckRoundEnd()
        {
            if (m_roundEnded || !m_started) return;

            int aliveCount = 0;
            int lastAlive = -1;
            foreach (SimPlayer p in m_players.Values)
            {
                if (!p.alive) continue;
                aliveCount++;
                lastAlive = p.netId;
            }

            if (aliveCount <= 1)
            {
                m_roundEnded = true;
                m_winnerNetId = lastAlive;
            }
        }

        public WorldSnapshot BuildSnapshot()
        {
            WorldSnapshot snap = new WorldSnapshot
            {
                serverTick = m_serverTick,
                roundEnded = m_roundEnded,
                winnerNetId = m_winnerNetId,
                players = new List<PlayerSnapshot>(),
                projectiles = new List<ProjectileSnapshot>()
            };

            foreach (SimPlayer p in m_players.Values)
                snap.players.Add(new PlayerSnapshot
                {
                    netId = p.netId,
                    name = p.name,
                    position = p.position,
                    yaw = p.yaw,
                    pitch = p.pitch,
                    alive = p.alive
                });

            foreach (SimProjectile pr in m_projectiles)
                snap.projectiles.Add(new ProjectileSnapshot { projectileId = pr.id, position = pr.position });

            return snap;
        }

        public void RestartRound()
        {
            m_projectiles.Clear();
            m_roundEnded = false;
            m_winnerNetId = -1;

            int i = 0;
            foreach (SimPlayer p in m_players.Values)
            {
                p.alive = true;
                p.verticalSpeed = 0f;
                p.fireCooldown = 0f;
                p.pendingFire = false;
                p.pendingJump = false;
                p.position = SpawnPoints[i++ % SpawnPoints.Length];
            }
        }
    }
}