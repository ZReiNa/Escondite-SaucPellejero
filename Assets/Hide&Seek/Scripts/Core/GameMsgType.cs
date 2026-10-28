namespace HideAndSeek
{
    public enum GameMsgType : byte
    {
        Hello = 1,      // client -> server
        Welcome = 2,    // server -> client
        Input = 3,      // client -> server
        Snapshot = 4    // server -> client
    }
}