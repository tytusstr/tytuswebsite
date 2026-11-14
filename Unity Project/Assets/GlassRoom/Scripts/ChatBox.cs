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

        public const int MaxLength = 225, MaxKept = 100;
        const float Width = 600f, ListHeight = 330f, FadeZone = 80f;
        // In a notice, where the names go: "\u0001 joined", "\u0002 changed their name to \u0001".
        public const string NameMark = "\u0001", OldNameMark = "\u0002";

        /// <summary>One message: who, what, whether it's a notice from the game, and when (realtime seconds).</summary>
        public struct Message { public string name, text, oldName; public bool system; public float time; }
        readonly List<Message> m_History = new List<Message>();
        readonly List<(CanvasGroup group, Text text, ChatUnderlines lines)> m_Lines = new List<(CanvasGroup, Text, ChatUnderlines)>();
        /// <summary>The last 100 messages, oldest first.</summary>
        public IReadOnlyList<Message> History => m_History;
        /// <summary>How long ago this message was said, in seconds.</summary>
        public static float Age(Message m) => Mathf.Max(0f, Time.realtimeSinceStartup - m.time);

        RectTransform m_Root, m_Viewport, m_Content, m_InputRect, m_OpenRect, m_HintRect, m_BottomHintRect;
        ScrollRect m_Scroll;
        InputField m_Input;
        GameObject m_InputRow, m_OpenButton, m_Hint, m_BottomHint, m_BottomArrow;
        Font m_Font;
        AudioSource m_Sound;
        AudioClip m_JoinClip, m_MessageClip;
        GameObject m_LimitNote;
        bool m_Open, m_StickToBottom = true;
        int m_ClosedFrame = -1, m_ScrollDown, m_Count;
        float m_NextRefresh;
        // Gliding down to the newest message: from where, when it started and how long it takes.
        float m_GlideFrom = -1f, m_GlideStart, m_GlideTime;
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
            m_MessageClip = Resources.Load<AudioClip>("Sounds/Chat Message");

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
            m_InputRect = row;
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

            // Under the box, in red, once you've typed as much as a message can hold.
            var limit = NewRect("Limit Note", row, new Vector2(0f, 0f), new Vector2(0f, 0f));
            limit.pivot = new Vector2(0f, 1f);
            limit.anchoredPosition = new Vector2(4f, -4f);
            limit.sizeDelta = new Vector2(Width, 30f);
            var limitText = limit.gameObject.AddComponent<Text>();
            Style(limitText, 20, new Color(1f, 0.32f, 0.3f));
            limitText.text = "That's the most a message can hold (" + MaxLength + " characters).";
            var limitShadow = limit.gameObject.AddComponent<Outline>();
            limitShadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            m_LimitNote = limit.gameObject;
            m_LimitNote.SetActive(false);

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
            m_OpenRect = open;

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
            m_HintRect = hint;

            // Computer, with hints on, after scrolling up: how to get back down.
            var bottomHint = NewRect("Scroll Hint", m_Root, new Vector2(0f, 1f), new Vector2(0f, 1f));
            bottomHint.pivot = new Vector2(0f, 1f);
            bottomHint.sizeDelta = new Vector2(400f, 36f);
            var bottomText = bottomHint.gameObject.AddComponent<Text>();
            Style(bottomText, 22, new Color(1f, 1f, 1f, 0.8f));
            bottomText.text = "V  scroll to bottom";
            var bottomShadow = bottomHint.gameObject.AddComponent<Outline>();
            bottomShadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
            bottomShadow.effectDistance = new Vector2(1.2f, -1.2f);
            m_BottomHint = bottomHint.gameObject;
            m_BottomHintRect = bottomHint;
            m_BottomHint.SetActive(false);

            // Mobile, after scrolling up: a round down-arrow at the bottom of the messages.
            var arrow = NewRect("Scroll Down", m_Root, new Vector2(0f, 1f), new Vector2(0f, 1f));
            arrow.pivot = new Vector2(1f, 0f);
            arrow.sizeDelta = new Vector2(64f, 64f);
            arrow.anchoredPosition = new Vector2(Width - 6f, -ListHeight + 6f);
            var arrowBg = arrow.gameObject.AddComponent<Image>();
            arrowBg.sprite = UISprites.Circle;
            arrowBg.color = new Color(0.06f, 0.07f, 0.09f, 0.7f);
            arrow.gameObject.AddComponent<Button>().onClick.AddListener(GlideToBottom);
            foreach (float side in new[] { -1f, 1f })
            {
                var bar = NewRect("Chevron", arrow, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                bar.sizeDelta = new Vector2(22f, 5f);
                bar.anchoredPosition = new Vector2(side * 7f, -2f);
                bar.localRotation = Quaternion.Euler(0f, 0f, -side * 40f);
                var img = bar.gameObject.AddComponent<Image>();
                img.sprite = UISprites.RoundedPanel;
                img.type = Image.Type.Sliced;
                img.color = Color.white;
                img.raycastTarget = false;
            }
            m_BottomArrow = arrow.gameObject;
            m_BottomArrow.SetActive(false);
        }

        // ---- Messages ----

        /// <summary>A message from someone (you included).</summary>
        public void Add(string name, string message)
        {
            AddLine(new Message { name = name, text = message, time = Time.realtimeSinceStartup });
            if (m_MessageClip != null) m_Sound.PlayOneShot(m_MessageClip, 0.45f);
        }

        /// <summary>A message from before you joined, said this many seconds ago.</summary>
        public void AddOld(string name, string message, bool system, string oldName, float age) =>
            AddLine(new Message { name = name, text = message, system = system, oldName = oldName, time = Time.realtimeSinceStartup - Mathf.Max(0f, age) });

        /// <summary>Someone has joined: "[name joined]" and a soft bell.</summary>
        public void Joined(string name)
        {
            AddLine(new Message { name = name, text = NameMark + " joined", system = true, time = Time.realtimeSinceStartup });
            if (m_JoinClip != null) m_Sound.PlayOneShot(m_JoinClip, 0.55f);
        }

        /// <summary>Someone changed their name: "[old changed their name to new]".</summary>
        public void Renamed(string oldName, string newName) =>
            AddLine(new Message { name = newName, oldName = oldName, text = OldNameMark + " changed their name to " + NameMark, system = true, time = Time.realtimeSinceStartup });

        // The line as shown, and where the names in it are (to underline them).
        static string Compose(Message m, List<(int start, int length)> names)
        {
            names.Clear();
            string Who(string n) => string.IsNullOrEmpty(n) ? "Someone" : n;
            var sb = new System.Text.StringBuilder();
            if (m.system)
            {
                sb.Append('[');
                foreach (char c in m.text)
                {
                    string insert = c == NameMark[0] ? Who(m.name) : c == OldNameMark[0] ? Who(m.oldName) : null;
                    if (insert == null) { sb.Append(c); continue; }
                    names.Add((sb.Length, insert.Length));
                    sb.Append(insert);
                }
                sb.Append(']');
            }
            else
            {
                names.Add((0, Who(m.name).Length));
                sb.Append(Who(m.name)).Append(":  ").Append(m.text);
            }
            // How long ago, once it's a minute or more.
            int minutes = Mathf.FloorToInt(Age(m) / 60f);
            if (minutes >= 60) sb.Append("  [").Append(minutes / 60).Append("h ago]");
            else if (minutes >= 1) sb.Append("  [").Append(minutes).Append("m ago]");
            return sb.ToString();
        }

        readonly List<(int, int)> m_Names = new List<(int, int)>();

        void AddLine(Message m)
        {
            if (string.IsNullOrWhiteSpace(m.text)) return;
            if (m.text.Length > MaxLength) m.text = m.text.Substring(0, MaxLength);
            m_History.Add(m);
            // Each message on a shaded strip, light and dark grey in turn, so they're easy to tell apart.
            var row = NewRect("Line", m_Content, Vector2.zero, Vector2.zero);
            var bg = row.gameObject.AddComponent<Image>();
            bg.sprite = UISprites.RoundedPanel;
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = UISprites.RoundedPanel != null && UISprites.RoundedPanel.border.x > 0 ? UISprites.RoundedPanel.border.x / 8f : 1f;
            bg.color = (m_Count++ % 2 == 0) ? new Color(0.24f, 0.25f, 0.27f, 0.62f) : new Color(0.12f, 0.125f, 0.14f, 0.66f);
            bg.raycastTarget = false;
            var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.padding = new RectOffset(10, 10, 4, 5);
            rowLayout.childControlWidth = rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = false;
            var group = row.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            var textRect = NewRect("Text", row, Vector2.zero, Vector2.zero);
            var text = textRect.gameObject.AddComponent<Text>();
            Style(text, 24, m.system ? new Color(1f, 1f, 1f, 0.82f) : Color.white);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.alignment = TextAnchor.UpperLeft;
            if (m.system) text.fontStyle = FontStyle.Italic;
            var shadow = textRect.gameObject.AddComponent<Outline>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
            shadow.effectDistance = new Vector2(1.2f, -1.2f);
            var underlines = textRect.gameObject.AddComponent<ChatUnderlines>();
            text.text = Compose(m, m_Names);
            underlines.Set(text, m_Names);
            m_Lines.Add((group, text, underlines));
            while (m_Lines.Count > MaxKept)
            {
                Destroy(m_Lines[0].group.gameObject);
                m_Lines.RemoveAt(0);
                m_History.RemoveAt(0);
            }
            if (m_StickToBottom) m_ScrollDown = 2;
        }

        // The "[4m ago]" parts move on as time passes.
        void RefreshTimes()
        {
            for (int i = 0; i < m_Lines.Count && i < m_History.Count; i++)
            {
                string shown = Compose(m_History[i], m_Names);
                if (m_Lines[i].text.text == shown) continue;
                m_Lines[i].text.text = shown;
                m_Lines[i].lines.Set(m_Lines[i].text, m_Names);
            }
        }

        bool CanScroll => m_Content.rect.height > m_Viewport.rect.height + 1f;
        bool AtBottom() => !CanScroll || m_Scroll.verticalNormalizedPosition <= 0.02f;

        /// <summary>Scrolls smoothly down to the newest message: quick for a little, up to about a second and a half for a lot.</summary>
        public void GlideToBottom()
        {
            if (!CanScroll) { m_StickToBottom = true; return; }
            float from = m_Scroll.verticalNormalizedPosition;
            if (from <= 0.001f) { m_StickToBottom = true; return; }
            float distance = from * (m_Content.rect.height - m_Viewport.rect.height);
            m_GlideFrom = from;
            m_GlideStart = Time.unscaledTime;
            m_GlideTime = Mathf.Clamp(distance / 2200f, 0.15f, 1.5f);
        }

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
            if (message.Length > 0 && !AtBottom()) GlideToBottom();
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
            bool atLimit = m_Open && m_Input.text.Length >= MaxLength;
            if (m_LimitNote.activeSelf != atLimit) m_LimitNote.SetActive(atLimit);
            bool showHint = on && !mobile && !m_Open && !menu && HintSettings.Enabled;
            if (m_Hint.activeSelf != showHint) m_Hint.SetActive(showHint);
            bool scrolledUp = on && CanScroll && !AtBottom() && m_GlideFrom < 0f;
            bool showBottomHint = showHint && scrolledUp;
            if (m_BottomHint.activeSelf != showBottomHint) m_BottomHint.SetActive(showBottomHint);
            bool showArrow = on && mobile && scrolledUp && !menu;
            if (m_BottomArrow.activeSelf != showArrow) m_BottomArrow.SetActive(showArrow);

            // The typing box, Chat button and hints sit just under the messages: at the top when
            // there are none, moving down as they fill the space.
            float shown = m_Lines.Count == 0 ? 0f : Mathf.Min(m_Content.rect.height, ListHeight);
            float below = shown > 0f ? -shown - 12f : 0f;
            m_InputRect.anchoredPosition = new Vector2(0f, below);
            m_OpenRect.anchoredPosition = new Vector2(0f, below);
            m_HintRect.anchoredPosition = new Vector2(2f, below - 4f);
            m_BottomHintRect.anchoredPosition = new Vector2(2f, below - 36f);
            ((RectTransform)m_BottomArrow.transform).anchoredPosition = new Vector2(Width - 6f, -shown + 6f);

            // Computer: Enter opens the box (not the same Enter that just sent a message), Esc
            // closes it, and the wheel scrolls back while typing.
            var keyboard = Keyboard.current;
            if (on && !mobile && keyboard != null)
            {
                if (!m_Open && !Typing && !menu && Time.frameCount != m_ClosedFrame
                    && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)) Open();
                else if (m_Open && keyboard.escapeKey.wasPressedThisFrame) Close();
                else if (!m_Open && !Typing && !menu && keyboard.vKey.wasPressedThisFrame) GlideToBottom();
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

            // Gliding down (eased), then staying with the newest.
            if (m_GlideFrom >= 0f)
            {
                float t = m_GlideTime > 0f ? Mathf.Clamp01((Time.unscaledTime - m_GlideStart) / m_GlideTime) : 1f;
                m_Scroll.StopMovement();
                m_Scroll.verticalNormalizedPosition = Mathf.Lerp(m_GlideFrom, 0f, 1f - (1f - t) * (1f - t) * (1f - t));
                if (t >= 1f) { m_GlideFrom = -1f; m_StickToBottom = true; }
            }
            if (Time.unscaledTime >= m_NextRefresh) { m_NextRefresh = Time.unscaledTime + 10f; RefreshTimes(); }

            // New messages keep the newest in view (unless you've scrolled up to read back).
            if (m_ScrollDown > 0 && m_GlideFrom < 0f)
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
            if (!CanScroll) { foreach (var l in m_Lines) l.group.alpha = 1f; return; }
            float top = m_Viewport.rect.yMax;
            foreach (var l in m_Lines)
            {
                var r = (RectTransform)l.group.transform;
                Vector3 centre = m_Viewport.InverseTransformPoint(r.TransformPoint(r.rect.center));
                float a = Mathf.Clamp01((top - centre.y) / FadeZone);
                if (Mathf.Abs(l.group.alpha - a) > 0.01f) l.group.alpha = a;
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
