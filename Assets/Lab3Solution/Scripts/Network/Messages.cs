// =================================================================================================
//  Lab 3 - Challenge 3 solution: the message format
//  XJO - Xarxes per a Jocs Online
// =================================================================================================
//
//  The only new idea of Lab 3 in this lobby. Every message is a byte[] that starts with one
//  MsgType byte (slide 18), and the rest depends on that byte:
//
//    Text     01 | BinaryWriter string           the 2.2 lobby messages, unchanged: CHAT:hola...
//    Player   02 | Serialize(PlayerDTO2)         name, level, Pokemon (slide 16)
//
//  The 2.2 text messages did not have to be rewritten: they travel inside a Text message.
//  The transports (TCPLobby*, UDPLobby*) only lost their Encoding.UTF8 calls: they move byte[] now.
// =================================================================================================

using System;
using System.IO;

namespace Lab3Solution
{
    public static class Messages
    {
        public static byte[] Text(string text)
        {
            MemoryStream stream = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(stream);
            writer.Write((byte)MsgType.Text);
            writer.Write(text);
            return stream.ToArray();
        }

        public static byte[] Player(PlayerDTO2 dto)
        {
            byte[] body = SerializerSolution.Serialize(dto);
            byte[] message = new byte[1 + body.Length];
            message[0] = (byte)MsgType.Player;
            body.CopyTo(message, 1);
            return message;
        }

        // Reads the type byte. 'text' or 'player' is filled depending on it.
        // Throws on a message that is too short or malformed: the caller drops it.
        public static MsgType Read(byte[] data, out string text, out PlayerDTO2 player)
        {
            text = null;
            player = new PlayerDTO2();
            if (data == null || data.Length < 1) throw new InvalidDataException("empty message");

            MsgType type = (MsgType)data[0];
            switch (type)
            {
                case MsgType.Text:
                    BinaryReader reader = new BinaryReader(new MemoryStream(data, 1, data.Length - 1));
                    text = reader.ReadString();
                    break;

                case MsgType.Player:
                    byte[] body = new byte[data.Length - 1];
                    Array.Copy(data, 1, body, 0, body.Length);
                    player = SerializerSolution.DeserializePlayerDTO2(body);
                    break;

                default:
                    throw new InvalidDataException("unknown message type " + data[0]);
            }
            return type;
        }

        public static string Describe(PlayerDTO2 dto)
        {
            string team = "";
            if (dto.ownedPokemons != null)
                foreach (Pokemon p in dto.ownedPokemons) team += (team.Length > 0 ? ", " : "") + p.name;
            return "level " + dto.level + ", " + (team.Length > 0 ? team : "no Pokemon");
        }
    }
}
