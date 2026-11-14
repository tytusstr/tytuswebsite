using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GlassRoom
{
    /// <summary>
    /// Text chat in the top-left corner. The first message appears at the top; later ones go
    /// underneath, and once the space is full the newest stays at the bottom and older ones rise
    /// and fade out towards the top. Scroll up to read back (the last 100 are kept). Long
    /// messages wrap onto new lines. Computer: Enter opens the box ("Press Enter to chat" shows
    /// under it while hints are on), Enter sends, Esc closes; the mouse wheel scrolls while
    /// typing. Mobile: the Chat button opens it, Send sends, drag to scroll. Someone joining is
    /// announced with a soft bell. Everyone in the room shares it (NetworkPlayer), and newcomers
    /// get what's been said; it starts empty once everyone has left. The Chat switch in the
    /// settings turns it off.
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

        public const int MaxLength = 150, MaxKept = 100;
        const float Width = 600f, ListHeight = 330f, FadeZone = 80f;

        public struct Message { public string name, text; public bool system; }
        readonly List<Message> m_History = new List<Message>();
        readonly List<Text> m_Lines = new List<Text>();
        /// <summary>The last 100 messages, oldest first.</summary>
        public IReadOnlyList<Message> History => m_History;

        RectTransform m_Root, m_Viewport, m_Content;
        ScrollRect m_Scroll;
        InputField m_Input;
        GameObject m_InputRow, m_OpenButton, m_Hint;
        Font m_Font;
        AudioSource m_Sound;
        AudioClip m_JoinClip;
        bool m_Open, m_StickToBottom = true;
        int m_ClosedFrame = -1, m_ScrollDown;

        /// <summary>Builds the chat in the HUD's top-left corner.</summary>
        public static ChatBox Create(RectTransform parent, Font font)
        {
            var go = new GameObject("Chat", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(24f, -20f);
            rect.sizeDelta = new Vector2(Width + 20f, 430f);
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
            m_Sound = gameObject.AddComponent<AudioSource>();
            m_Sound.playOnAwake = false;
            m_Sound.spatialBlend = 0f;
            m_JoinClip = Resources.Load<AudioClip>("Sounds/Player Joined");

            // The message list: clipped to its box, scrollable.
            m_Viewport = NewRect("Messages", m_Root, new Vector2(0f, 1f), new Vector2(0f, 1f));
            m_Viewport.pivot = new Vector2(0f, 1f);
            m_Viewport.sizeDelta = new Vector2(Width, ListHeight);
            var catcher = m_Viewport.gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            m_Viewport.gameObject.AddComponent<RectMask2D>();
            m_Content = NewRect("Content", m_Viewport, new Vector2(0f, 1f), new Vector2(1f, 1f));
            m_Content.pivot = new Vector2(0f, 1f);
            var layout = m_Content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.spacing = 6f;
            layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            var fit = m_Content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            m_Scroll = m_Viewport.gameObject.AddComponent<ScrollRect>();
            m_Scroll.content = m_Content;
            m_Scroll.viewport = m_Viewport;
            m_Scroll.horizontal = false;
            m_Scroll.vertical = true;
            m_Scroll.movementType = ScrollRect.MovementType.Clamped;
            m_Scroll.scrollSensitivity = 30f;
            m_Scroll.onValueChanged.AddListener(_ => m_StickToBottom = AtBottom());

            float below = -ListHeight - 12f;

            // The typing row: a dark box and a Send button.
            var row = NewRect("Type Row", m_Root, new Vector2(0f, 1f), new Vector2(0f, 1f));
            row.pivot = new Vector2(0f, 1f);
            row.anchoredPosition = new Vector2(0f, below);
            row.sizeDelta = new Vector2(Width, 60f);
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
            var placeholder = hintRect.gameObject.AddComponent<Text>();
            Style(placeholder, 24, new Color(1f, 1f, 1f, 0.45f));
            placeholder.text = "Say something...";
            m_Input = box.gameObject.AddComponent<InputField>();
            m_Input.textComponent = text;
            m_Input.placeholder = placeholder;
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
            open.anchoredPosition = new Vector2(0f, below);
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

            // Computer, with hints on: how to start typing.
            var hint = NewRect("Chat Hint", m_Root, new Vector2(0f, 1f), new Vector2(0f, 1f));
            hint.pivot = new Vector2(0f, 1f);
            hint.anchoredPosition = new Vector2(2f, below - 4f);
            hint.sizeDelta = new Vector2(400f, 36f);
            var hintText = hint.gameObject.AddComponent<Text>();
            Style(hintText, 22, new Color(1f, 1f, 1f, 0.8f));
            hintText.text = "Press Enter to chat";
            var hintShadow = hint.gameObject.AddComponent<Outline>();
            hintShadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
            hintShadow.effectDistance = new Vector2(1.2f, -1.2f);
            m_Hint = hint.gameObject;
        }

        // ---- Messages ----

        /// <summary>A message from someone (you included).</summary>
        public void Add(string name, string message) => AddLine(new Message { name = name, text = message });

        /// <summary>A notice from the game, like someone joining.</summary>
        public void Notice(string message) => AddLine(new Message { text = message, system = true });

        /// <summary>A message from before you joined.</summary>
        public void AddOld(string name, string message, bool system) => AddLine(new Message { name = name, text = message, system = system });

        /// <summary>Someone has joined: a notice and a soft bell.</summary>
        public void Joined(string name)
        {
            Notice((string.IsNullOrEmpty(name) ? "Someone" : name) + " joined");
            if (m_JoinClip != null) m_Sound.PlayOneShot(m_JoinClip, 0.55f);
        }

        void AddLine(Message m)
        {
            if (string.IsNullOrWhiteSpace(m.text)) return;
            if (m.text.Length > MaxLength) m.text = m.text.Substring(0, MaxLength);
            m_History.Add(m);
            var rect = NewRect("Line", m_Content, Vector2.zero, Vector2.zero);
            var text = rect.gameObject.AddComponent<Text>();
            Style(text, 24, m.system ? new Color(1f, 1f, 1f, 0.75f) : Color.white);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.alignment = TextAnchor.UpperLeft;
            if (m.system) text.fontStyle = FontStyle.Italic;
            string who = string.IsNullOrEmpty(m.name) ? "Someone" : m.name;
            text.text = m.system ? m.text : who + ":  " + m.text;
            var shadow = rect.gameObject.AddComponent<Outline>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.65f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
            m_Lines.Add(text);
            while (m_Lines.Count > MaxKept)
            {
                Destroy(m_Lines[0].gameObject);
                m_Lines.RemoveAt(0);
                m_History.RemoveAt(0);
            }
            if (m_StickToBottom) m_ScrollDown = 2;
        }

        bool AtBottom() => m_Content.rect.height <= m_Viewport.rect.height + 1f || m_Scroll.verticalNormalizedPosition <= 0.02f;

        // ---- Typing ----

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
            m_StickToBottom = true;
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
            if (m_Viewport.gameObject.activeSelf != on) m_Viewport.gameObject.SetActive(on);
            if (!on && m_Open) Close();
            bool mobile = InputModeManager.Mode == ControlMode.Touchscreen;
            bool menu = ControlsHUD.AnyMenuOpen;
            bool showButton = on && mobile && !m_Open && !menu;
            if (m_OpenButton.activeSelf != showButton) m_OpenButton.SetActive(showButton);
            bool showHint = on && !mobile && !m_Open && !menu && HintSettings.Enabled;
            if (m_Hint.activeSelf != showHint) m_Hint.SetActive(showHint);

            // Computer: Enter opens the box (not the same Enter that just sent a message), Esc
            // closes it, and the wheel scrolls back while typing.
            var keyboard = Keyboard.current;
            if (on && !mobile && keyboard != null)
            {
                if (!m_Open && !Typing && !menu && Time.frameCount != m_ClosedFrame
                    && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)) Open();
                else if (m_Open && keyboard.escapeKey.wasPressedThisFrame) Close();
            }
            var mouse = Mouse.current;
            if (m_Open && mouse != null && m_Content.rect.height > m_Viewport.rect.height)
            {
                float wheel = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > 0.01f)
                {
                    float range = m_Content.rect.height - m_Viewport.rect.height;
                    m_Scroll.verticalNormalizedPosition = Mathf.Clamp01(m_Scroll.verticalNormalizedPosition + Mathf.Sign(wheel) * 60f / range);
                }
            }

            // New messages keep the newest in view (unless you've scrolled up to read back).
            if (m_ScrollDown > 0)
            {
                Canvas.ForceUpdateCanvases();
                m_Scroll.verticalNormalizedPosition = 0f;
                m_ScrollDown--;
            }
            FadeTop();
        }

        // Messages fade out as they near the top of the box.
        void FadeTop()
        {
            if (m_Content.rect.height <= m_Viewport.rect.height) { foreach (var t in m_Lines) SetAlpha(t, 1f); return; }
            float top = m_Viewport.rect.yMax;
            foreach (var t in m_Lines)
            {
                var r = t.rectTransform;
                Vector3 centre = m_Viewport.InverseTransformPoint(r.TransformPoint(r.rect.center));
                SetAlpha(t, Mathf.Clamp01((top - centre.y) / FadeZone));
            }
        }

        static void SetAlpha(Text t, float a)
        {
            var c = t.color;
            if (Mathf.Abs(c.a - a) < 0.01f) return;
            c.a = a;
            t.color = c;
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
