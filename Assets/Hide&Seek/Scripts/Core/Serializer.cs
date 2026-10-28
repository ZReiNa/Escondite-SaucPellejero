using System;
using System.IO;
using System.Text;

namespace HideAndSeek
{
    public static class Serializer
    {
        public static byte[] SerializePlayerInfo(PlayerInfo data)
        {
            if (data == null) data = new PlayerInfo();
            if (string.IsNullOrEmpty(data.playerName)) data.playerName = "Player";

            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(data.playerName);
                return stream.ToArray();
            }
        }

        public static PlayerInfo DeserializePlayerInfo(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));

            using (MemoryStream stream = new MemoryStream(bytes))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
            {
                return new PlayerInfo
                {
                    playerName = reader.ReadString()
                };
            }
        }
    }
}