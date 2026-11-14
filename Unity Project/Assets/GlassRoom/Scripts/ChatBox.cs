using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GlassRoom
{
    /// <summary>
    /// Text chat in the top-left corner: the last few messages ("Name: message"), fading out a
    /// while after they arrive, and a box to type in. Computer: Enter opens the box, Enter sends,
    /// Esc closes it. Mobile: the small Chat button opens it (with the phone's keyboard) and Send
    /// sends. Turned off with the Chat switch in the settings. Messages go to everyone in the
    /// room (NetworkPlayer).
    /// </summary>
    public class ChatBox : MonoBehaviour
    {
        public static ChatBox Instance { get; private set; }

        /// <summary>True while you're typing in any box (the game ignores its keys then).</summary>
        public static bool Typing
        {
            get
            {
                var es = EventSystem.current;
                var selected = es != null ? es.currentSelectedGameObject : null;
                var field = selected != null ? selected.GetComponent<InputField>() : null;
                return field != null && field.isFocused;
            }
        }

        /// <summary>Raised when you send a message (NetworkPlayer passes it on).</summary>
        public static event System.Action<string> Sent;

        const int MaxLines = 7, MaxLength = 120;
        const float ShowFor = 25f, FadeFor = 3f;

        struct Line { public Text text; public float time; }
        readonly List<Line> m_Lines = new List<Line>();
        RectTransform m_Root, m_List;
        InputField m_Input;
        GameObject m_InputRow, m_OpenButton;
        Font m_Font;
        bool m_Open;

        /// <summary>Builds the chat in the HUD's top-left corner.</summary>
        public static ChatBox Create(RectTransform parent, Font font)
        {
            var go = new GameObject("Chat", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(24f, -20f);
            rect.sizeDelta = new Vector2(620f, 420f);
            var chat = go.AddComponent<ChatBox>();
            chat.m_Font = font;
            chat.m_Root = rect;
            chat.Build();
            return chat;
        }

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Build()
        {
            // Messages, newest at the bottom.
            m_List = NewRect("Messages", m_Root, new Vector2(0f, 1f), new Vector2(1f, 1f));
            m_List.pivot = new Vector2(0f, 1f);
            m_List.sizeDelta = new Vector2(0f, 330f);
            var layout = m_List.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.LowerLeft;
            layout.spacing = 4f;
            layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            // The typing row: a dark box and a Send button.
            var row = NewRect("Type Row", m_Root, new Vector2(0f, 1f), new Vector2(1f, 1f));
            row.pivot = new Vector2(0f, 1f);
            row.anchoredPosition = new Vector2(0f, -340f);
            row.sizeDelta = new Vector2(0f, 60f);
            m_InputRow = row.gameObject;
            var box = NewRect("Box", row, Vector2.zero, Vector2.one);
            box.offsetMax = new Vector2(-130f, 0f);
            var boxBg = box.gameObject.AddComponent<Image>();
            boxBg.sprite = UISprites.RoundedPanel;
            boxBg.type = Image.Type.Sliced;
            boxBg.color = new Color(0.05f, 0.06f, 0.07f, 0.72f);
            var textRect = NewRect("Text", box, Vector2.zero, Vector2.one);
            textRect.offsetMin = new Vector2(16f, 6f);
            textRect.offsetMax = new Vector2(-16f, -6f);
            var text = textRect.gameObject.AddComponent<Text>();
            Style(text, 24, Color.white);
            text.supportRichText = false;
            var hintRect = NewRect("Placeholder", box, Vector2.zero, Vector2.one);
            hintRect.offsetMin = new Vector2(16f, 6f);
            hintRect.offsetMax = new Vector2(-16f, -6f);
            var hint = hintRect.gameObject.AddComponent<Text>();
            Style(hint, 24, new Color(1f, 1f, 1f, 0.45f));
            hint.text = "Say something...";
            m_Input = box.gameObject.AddComponent<InputField>();
            m_Input.textComponent = text;
            m_Input.placeholder = hint;
            m_Input.characterLimit = MaxLength;
            m_Input.lineType = InputField.LineType.SingleLine;
            m_Input.onEndEdit.AddListener(OnEndEdit);

            var send = NewRect("Send", row, new Vector2(1f, 0f), new Vector2(1f, 1f));
            send.pivot = new Vector2(1f, 0.5f);
            send.sizeDelta = new Vector2(118f, 0f);
            var sendBg = send.gameObject.AddComponent<Image>();
            sendBg.sprite = UISprites.RoundedPanel;
            sendBg.type = Image.Type.Sliced;
            sendBg.color = new Color(1f, 1f, 1f, 0.9f);
            send.gameObject.AddComponent<Button>().onClick.AddListener(Send);
            var sendText = NewRect("Label", send, Vector2.zero, Vector2.one).gameObject.AddComponent<Text>();
            Style(sendText, 24, new Color(0.1f, 0.1f, 0.12f));
            sendText.fontStyle = FontStyle.Bold;
            sendText.alignment = TextAnchor.MiddleCenter;
            sendText.text = "Send";
            m_InputRow.SetActive(false);

            // Mobile: a small Chat button under the messages.
            var open = NewRect("Chat Button", m_Root, new Vector2(0f, 1f), new Vector2(0f, 1f));
            open.pivot = new Vector2(0f, 1f);
            open.anchoredPosition = new Vector2(0f, -340f);
            open.sizeDelta = new Vector2(120f, 56f);
            var openBg = open.gameObject.AddComponent<Image>();
            openBg.sprite = UISprites.RoundedPanel;
            openBg.type = Image.Type.Sliced;
            openBg.color = new Color(0.06f, 0.07f, 0.09f, 0.55f);
            open.gameObject.AddComponent<Button>().onClick.AddListener(Open);
            var openText = NewRect("Label", open, Vector2.zero, Vector2.one).gameObject.AddComponent<Text>();
            Style(openText, 24, Color.white);
            openText.fontStyle = FontStyle.Bold;
            openText.alignment = TextAnchor.MiddleCenter;
            openText.text = "Chat";
            m_OpenButton = open.gameObject;
        }

        /// <summary>A message from someone (you included).</summary>
        public void Add(string name, string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            var rect = NewRect("Line", m_List, Vector2.zero, Vector2.zero);
            var text = rect.gameObject.AddComponent<Text>();
            Style(text, 24, Color.white);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            string who = string.IsNullOrEmpty(name) ? "Someone" : name;
            text.text = who + ":  " + message;
            var shadow = rect.gameObject.AddComponent<Outline>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.65f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
            m_Lines.Add(new Line { text = text, time = Time.unscaledTime });
            while (m_Lines.Count > MaxLines)
            {
                Destroy(m_Lines[0].text.gameObject);
                m_Lines.RemoveAt(0);
            }
        }

        /// <summary>Opens the typing box.</summary>
        public void Open()
        {
            if (!ChatSettings.Enabled) return;
            m_Open = true;
            m_InputRow.SetActive(true);
            m_Input.text = "";
            m_Input.Select();
            m_Input.ActivateInputField();
        }

        int m_ClosedFrame = -1;

        public void Close()
        {
            m_Open = false;
            m_ClosedFrame = Time.frameCount;
            m_Input.DeactivateInputField();
            m_InputRow.SetActive(false);
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == m_Input.gameObject)
                EventSystem.current.SetSelectedGameObject(null);
        }

        public bool IsOpen => m_Open;

        void Send()
        {
            string message = m_Input.text.Trim();
            if (message.Length > 0) Sent?.Invoke(message.Length > MaxLength ? message.Substring(0, MaxLength) : message);
            Close();
        }

        void OnEndEdit(string _)
        {
            // Enter sends; clicking away (or Esc) just closes.
            var keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)) Send();
            else if (InputModeManager.Mode == ControlMode.KeyboardMouse) Close();
        }

        void Update()
        {
            bool on = ChatSettings.Enabled && !WebPage.Covered;
            if (m_List.gameObject.activeSelf != on) m_List.gameObject.SetActive(on);
            if (!on && m_Open) Close();
            bool mobile = InputModeManager.Mode == ControlMode.Touchscreen;
            bool showButton = on && mobile && !m_Open && !ControlsHUD.AnyMenuOpen;
            if (m_OpenButton.activeSelf != showButton) m_OpenButton.SetActive(showButton);

            // Computer: Enter opens the box (unless something else is being typed in), Esc closes.
            var keyboard = Keyboard.current;
            if (on && !mobile && keyboard != null)
            {
                // (Not the same Enter that just sent a message.)
                if (!m_Open && !Typing && !ControlsHUD.AnyMenuOpen && Time.frameCount != m_ClosedFrame && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)) Open();
                else if (m_Open && keyboard.escapeKey.wasPressedThisFrame) Close();
            }

            // Old messages fade away (all show again while typing).
            float now = Time.unscaledTime;
            foreach (var line in m_Lines)
            {
                float age = now - line.time;
                float alpha = m_Open ? 1f : 1f - Mathf.Clamp01((age - ShowFor) / FadeFor);
                var c = line.text.color;
                if (!Mathf.Approximately(c.a, alpha)) { c.a = alpha; line.text.color = c; }
            }
        }

        void Style(Text text, int size, Color color)
        {
            text.font = m_Font;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
        }

        static RectTransform NewRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }
    }
}
