using System;
using System.IO;
using System.Text;

namespace HideAndSeek
{
    public static class LobbyMessages
    {
        public static byte[] Text(string text)
        {
            if (text == null) text = string.Empty;

            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write((byte)MsgType.Text);
                writer.Write(text);
                return stream.ToArray();
            }
        }

        public static byte[] PlayerInfo(PlayerInfo info)
        {
            byte[] body = Serializer.SerializePlayerInfo(info);
            byte[] message = new byte[1 + body.Length];
            message[0] = (byte)MsgType.PlayerInfo;
            Buffer.BlockCopy(body, 0, message, 1, body.Length);
            return message;
        }

        public static MsgType Read(byte[] data, out string text, out PlayerInfo info)
        {
            text = null;
            info = null;

            if (data == null || data.Length < 1)
                throw new InvalidDataException("Empty message");

            MsgType type = (MsgType)data[0];

            switch (type)
            {
                case MsgType.Text:
                    using (MemoryStream stream = new MemoryStream(data, 1, data.Length - 1))
                    using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
                    {
                        text = reader.ReadString();
                    }
                    return type;

                case MsgType.PlayerInfo:
                    byte[] body = new byte[data.Length - 1];
                    Buffer.BlockCopy(data, 1, body, 0, body.Length);
                    info = Serializer.DeserializePlayerInfo(body);
                    return type;

                default:
                    throw new InvalidDataException("Unknown lobby MsgType: " + data[0]);
            }
        }
    }
}