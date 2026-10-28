LAB 3 - DATA SERIALIZATION: CHALLENGE 3 SOLUTION
XJO - Xarxes per a Jocs Online

The Lab 2.2 solution lobby with Lab 3 plugged in: every message has a type byte, and joining
sends your PlayerDTO2 (name, level, Pokemon) instead of JOIN:<name>. Every waiting room shows
every player's level and team. It is one way to do Challenge 3, not the only valid one.
Read it and compare it with your own lobby: the point is how little had to change.

NEEDS
  Assets/Lab3/Scripts/PlayerDTO.cs and SerializerSolution.cs (included in this package, and in
  the Lab 3 handout).

HOW TO RUN IT
  1. File > Build Profiles (Build Settings): the five scenes of Lab3Solution/Scenes must be in
     the list, L3_Menu first. SceneManager.LoadScene needs them there, in the Editor too.
  2. Build, run the exe once as host (Create game) and join it from the Editor or a second exe.
     Same PC: IP 127.0.0.1. With a classmate: their ipconfig IP.
  3. In the menu set your name, level and Pokemon. In the waiting room change your level with
     - / +: everybody's list updates.

WHAT CHANGED FROM THE LAB 2.2 SOLUTION (diff the folders to see it line by line)
  Network/Messages.cs         NEW. Wraps and reads every message: type byte, then the content
  Network/TCPLobby*.cs        packets stay byte[]: the Encoding.UTF8 calls are gone (the seam)
  Network/UDPLobby*.cs        same
  Network/LobbyServer.cs      joining is a Player message; keeps each player's PlayerDTO2 and
                              sends everybody's to everyone; the 2.2 text messages are untouched
  Network/LobbyClient.cs      sends myData to join, fills Teams from Player messages
  LobbySession.cs, UI/*.cs    level and Pokemon in the menu, teams in the waiting room

MESSAGES (first byte = MsgType)
  Player  02 + Serialize(PlayerDTO2)   client to server: join, or update your data
                                       server to client: one per player, on every change
  Text    01 + BinaryWriter string     the 2.2 lobby messages, unchanged:
          client to server   CHAT:<text>   LEAVE:   PING:<clientMs>,<lastPingMs>
          server to client   PLAYERS:<name>,...   CHAT:<name>: <text>   ROOM:<info>
                             PONG:<clientMs>   ERROR:<reason>   KICKED:   START:
  TCP adds the 2.1 4-byte length prefix before each message. UDP: one datagram, one message.

DESIGN DECISIONS WORTH LOOKING AT
  - The PlayerDTO2 is the join request. UDP repeats it every second until the first PLAYERS
    arrives, exactly as 2.2 repeated JOIN, so a lost packet costs nothing.
  - The same Player message from someone already in the room is an update, not a second join.
  - The server sends every player's data before PLAYERS, so a client never shows a name it has
    no data for.
  - PLAYERS is still comma-separated text, so names still cannot contain commas. Making every
    message binary removes that limit.
  - A message that cannot be read (garbage, or an old 2.2 client sending plain text) is logged
    and dropped. It never takes the room down.
