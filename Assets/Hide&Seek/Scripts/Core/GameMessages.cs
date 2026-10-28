using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace HideAndSeek
{
    [Serializable]
    public struct InputCommand
    {
        public int tick;
        public float moveX;
        public float moveY;
        public float yaw;
        public float pitch;
        public bool jumpPressed;
        public bool firePressed;   // LMB pressed this frame
        public bool runHeld;       // Left Shift held
    }

    [Serializable]
    public struct PlayerSnapshot
    {
        public int netId;
        public string name;
        public Vector3 position;
        public float yaw;
        public float pitch;
        public bool alive;
    }

    [Serializable]
    public struct ProjectileSnapshot
    {
        public int projectileId;
        public Vector3 position;
    }

    [Serializable]
    public struct WorldSnapshot
    {
        public int serverTick;
        public bool roundEnded;
        public int winnerNetId;
        public List<PlayerSnapshot> players;
        public List<ProjectileSnapshot> projectiles;
    }

    public static class GameMessages
    {
        public static byte[] MakeHello(string playerName)
        {
            if (string.IsNullOrWhiteSpace(playerName)) playerName = "Player";

            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write((byte)GameMsgType.Hello);
                writer.Write(playerName);
                return stream.ToArray();
            }
        }

        public static byte[] MakeWelcome(int assignedNetId)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write((byte)GameMsgType.Welcome);
                writer.Write(assignedNetId);
                return stream.ToArray();
            }
        }

        public static byte[] MakeInput(InputCommand input)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write((byte)GameMsgType.Input);
                writer.Write(input.tick);
                writer.Write(input.moveX);
                writer.Write(input.moveY);
                writer.Write(input.yaw);
                writer.Write(input.pitch);
                writer.Write(input.jumpPressed);
                writer.Write(input.firePressed);
                writer.Write(input.runHeld);
                return stream.ToArray();
            }
        }

        public static byte[] MakeSnapshot(WorldSnapshot snapshot)
        {
            if (snapshot.players == null) snapshot.players = new List<PlayerSnapshot>();
            if (snapshot.projectiles == null) snapshot.projectiles = new List<ProjectileSnapshot>();

            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write((byte)GameMsgType.Snapshot);
                writer.Write(snapshot.serverTick);
                writer.Write(snapshot.roundEnded);
                writer.Write(snapshot.winnerNetId);

                writer.Write(snapshot.players.Count);
                for (int i = 0; i < snapshot.players.Count; i++)
                {
                    PlayerSnapshot p = snapshot.players[i];
                    writer.Write(p.netId);
                    writer.Write(p.name ?? string.Empty);
                    writer.Write(p.position.x);
                    writer.Write(p.position.y);
                    writer.Write(p.position.z);
                    writer.Write(p.yaw);
                    writer.Write(p.pitch);
                    writer.Write(p.alive);
                }

                writer.Write(snapshot.projectiles.Count);
                for (int i = 0; i < snapshot.projectiles.Count; i++)
                {
                    ProjectileSnapshot pr = snapshot.projectiles[i];
                    writer.Write(pr.projectileId);
                    writer.Write(pr.position.x);
                    writer.Write(pr.position.y);
                    writer.Write(pr.position.z);
                }

                return stream.ToArray();
            }
        }

        public static GameMsgType ReadType(byte[] data)
        {
            if (data == null || data.Length < 1) throw new InvalidDataException("Empty game message");
            return (GameMsgType)data[0];
        }

        public static int ReadWelcome(byte[] data)
        {
            using (MemoryStream stream = new MemoryStream(data, 1, data.Length - 1))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
                return reader.ReadInt32();
        }

        public static string ReadHello(byte[] data)
        {
            using (MemoryStream stream = new MemoryStream(data, 1, data.Length - 1))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
                return reader.ReadString();
        }

        public static InputCommand ReadInput(byte[] data)
        {
            using (MemoryStream stream = new MemoryStream(data, 1, data.Length - 1))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
            {
                InputCommand input = new InputCommand
                {
                    tick = reader.ReadInt32(),
                    moveX = reader.ReadSingle(),
                    moveY = reader.ReadSingle(),
                    yaw = reader.ReadSingle(),
                    pitch = reader.ReadSingle(),
                    jumpPressed = reader.ReadBoolean(),
                    firePressed = reader.ReadBoolean(),
                    runHeld = reader.ReadBoolean()
                };
                return input;
            }
        }

        public static WorldSnapshot ReadSnapshot(byte[] data)
        {
            using (MemoryStream stream = new MemoryStream(data, 1, data.Length - 1))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
            {
                WorldSnapshot snapshot = new WorldSnapshot
                {
                    serverTick = reader.ReadInt32(),
                    roundEnded = reader.ReadBoolean(),
                    winnerNetId = reader.ReadInt32(),
                    players = new List<PlayerSnapshot>(),
                    projectiles = new List<ProjectileSnapshot>()
                };

                int playerCount = reader.ReadInt32();
                for (int i = 0; i < playerCount; i++)
                {
                    PlayerSnapshot p = new PlayerSnapshot
                    {
                        netId = reader.ReadInt32(),
                        name = reader.ReadString(),
                        position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                        yaw = reader.ReadSingle(),
                        pitch = reader.ReadSingle(),
                        alive = reader.ReadBoolean()
                    };
                    snapshot.players.Add(p);
                }

                int projectileCount = reader.ReadInt32();
                for (int i = 0; i < projectileCount; i++)
                {
                    ProjectileSnapshot pr = new ProjectileSnapshot
                    {
                        projectileId = reader.ReadInt32(),
                        position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle())
                    };
                    snapshot.projectiles.Add(pr);
                }

                return snapshot;
            }
        }
    }
}