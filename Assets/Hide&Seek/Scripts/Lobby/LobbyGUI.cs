using UnityEngine;
using UnityEngine.SceneManagement;

namespace HideAndSeek
{
    public static class LobbyGUI
    {
        const int FontSize = 18;

        public static void Begin(string title)
        {
            GUI.skin.label.fontSize = FontSize;
            GUI.skin.button.fontSize = FontSize;
            GUI.skin.textField.fontSize = FontSize;

            GUILayout.BeginArea(new Rect(20, 15, Screen.width - 40, Screen.height - 30));
            GUILayout.Label("<b>" + title + "</b>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = FontSize + 8 });
            GUILayout.Space(8);
        }

        public static void End() => GUILayout.EndArea();

        public static string Field(string label, string value, int width = 260)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(150));
            value = GUILayout.TextField(value ?? "", GUILayout.Width(width));
            GUILayout.EndHorizontal();
            return value;
        }

        public static int IntField(string label, int value, int min, int max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(150));
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
            GUILayout.Label("<color=#ffb347>" + text + "</color>", new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true });
        }

        public static string Chat(LobbyClient client, string input, float height)
        {
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Height(height));
            GUILayout.Label("Chat");

            int first = Mathf.Max(0, client.Chat.Count - Mathf.Max(1, (int)(height / 28f) - 3));
            for (int i = first; i < client.Chat.Count; i++) GUILayout.Label(client.Chat[i]);

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();

            bool enter = Event.current.type == EventType.KeyDown &&
                         (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);

            GUI.SetNextControlName("chat");
            input = GUILayout.TextField(input ?? "", 220);

            if ((GUILayout.Button("Send", GUILayout.Width(90)) || (enter && GUI.GetNameOfFocusedControl() == "chat"))
                && (input ?? "").Trim().Length > 0)
            {
                client.SendChat(input);
                input = "";
            }

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            return input;
        }

        public static bool NeedsSession()
        {
            if (LobbySession.Instance.Client != null) return false;
            SceneManager.LoadScene(LobbySession.MenuScene);
            return true;
        }
    }
}
