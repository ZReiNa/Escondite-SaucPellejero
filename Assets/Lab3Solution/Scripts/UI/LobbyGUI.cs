// Lab 3 solution: drawing helpers shared by the scene UIs. Plain OnGUI, readable on a projector.
// Nothing here touches a socket: the UI only reads LobbySession / LobbyClient / LobbyServer.

using UnityEngine;

namespace Lab3Solution
{
    public static class LobbyGUI
    {
        const int FontSize = 18;

        public static void Begin(string title)
        {
            GUI.skin.label.fontSize = FontSize;
            GUI.skin.button.fontSize = FontSize;
            GUI.skin.textField.fontSize = FontSize;
            GUI.skin.toggle.fontSize = FontSize;
            GUILayout.BeginArea(new Rect(20, 15, Screen.width - 40, Screen.height - 30));
            GUILayout.Label("<b>" + title + "</b>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = FontSize + 6 });
            GUILayout.Space(8);
        }

        public static void End() { GUILayout.EndArea(); }

        public static string Field(string label, string value, int width = 220)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(140));
            value = GUILayout.TextField(value, GUILayout.Width(width));
            GUILayout.EndHorizontal();
            return value;
        }

        public static int IntField(string label, int value, int min, int max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(140));
            if (GUILayout.Button("-", GUILayout.Width(36))) value--;
            GUILayout.Label(value.ToString(), GUILayout.Width(70));
            if (GUILayout.Button("+", GUILayout.Width(36))) value++;
            GUILayout.EndHorizontal();
            return Mathf.Clamp(value, min, max);
        }

        public static void Message(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            GUILayout.Space(8);
            GUILayout.Label("<color=#ffb347>" + text + "</color>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = FontSize, wordWrap = true });
        }

        // The chat box: last lines, an input field, Send button or Enter.
        public static string Chat(LobbyClient client, string input, float height)
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Height(height));
            GUILayout.Label("Chat");
            int first = Mathf.Max(0, client.Chat.Count - Mathf.Max(1, (int)(height / 28) - 3));
            for (int i = first; i < client.Chat.Count; i++) GUILayout.Label(client.Chat[i]);
            GUILayout.FlexibleSpace();

            GUILayout.BeginHorizontal();
            bool enter = Event.current.type == EventType.KeyDown &&
                         (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            GUI.SetNextControlName("chat");
            input = GUILayout.TextField(input, 200);
            if ((GUILayout.Button("Send", GUILayout.Width(90)) || (enter && GUI.GetNameOfFocusedControl() == "chat"))
                && input.Trim().Length > 0)
            {
                client.SendChat(input);
                input = "";
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            return input;
        }

        // Any scene opened with Play in the Editor without going through the menu.
        public static bool NeedsSession()
        {
            if (LobbySession.Instance.Client != null) return false;
            UnityEngine.SceneManagement.SceneManager.LoadScene(LobbySession.MenuScene);
            return true;
        }
    }
}
