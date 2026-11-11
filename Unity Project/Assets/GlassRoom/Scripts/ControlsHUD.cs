using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace GlassRoom
{
    /// <summary>
    /// Builds the on-screen UI at runtime: the menu button (three lines) and settings menu,
    /// the touch joystick and look area, and a hint line.
    /// </summary>
    public class ControlsHUD : MonoBehaviour
    {
        // Plain, neutral palette: charcoal for the selected state, light grey otherwise.
        static readonly Color k_Ink = new Color(0.11f, 0.11f, 0.12f, 1f);
        static readonly Color k_Muted = new Color(0.45f, 0.45f, 0.47f, 1f);
        static readonly Color k_Accent = k_Ink;
        static readonly Color k_Idle = new Color(0.92f, 0.92f, 0.93f, 1f);

        public VirtualJoystick Joystick { get; private set; }
        public TouchLookPad LookPad { get; private set; }
        public bool IsMenuOpen => m_Menu != null && m_Menu.activeSelf;

        bool m_JumpRequested;
        GameObject m_JumpButton;

        /// <summary>Shows or hides the phone Jump button (hidden while riding); the other buttons drop into its place.</summary>
        public void SetJumpVisible(bool visible)
        {
            if (m_JumpButton == null || m_JumpButton.activeSelf == visible) return;
            m_JumpButton.SetActive(visible);
            int first = visible ? 1 : 0;
            for (int i = 0; i < m_ContextButtons.Length; i++)
                ((RectTransform)m_ContextButtons[i].transform).anchoredPosition = new Vector2(-160f, 140f + (i + first) * 104f);
        }

        /// <summary>True once for each press of the phone Jump button.</summary>
        public bool ConsumeJump()
        {
            bool jump = m_JumpRequested;
            m_JumpRequested = false;
            return jump;
        }

        GameObject m_TouchControls, m_Menu, m_HintBar, m_MouseRow, m_QNote;
        // m_Lower holds everything below the Controls row; it moves down to make room for the
        // Mouse? row in computer mode.
        RectTransform m_Card, m_Lower;
        Text m_FovValue;
        Slider m_FovSlider;
        const float CardHeight = 930f, MouseRowHeight = 80f;
        CanvasGroup m_HintGroup;
        // Phone buttons in the bottom-right corner, and computer key hints along the bottom.
        readonly GameObject[] m_ContextButtons = new GameObject[2];
        readonly Text[] m_ContextLabels = new Text[2];
        readonly System.Action[] m_ContextActions = new System.Action[2];
        readonly GameObject[] m_Hints = new GameObject[2];
        readonly Text[] m_HintKeys = new Text[2];
        readonly Text[] m_HintLabels = new Text[2];
        RectTransform m_Crosshair;
        RectTransform m_SafeArea;
        Rect m_AppliedSafeArea;
        Vector2 m_AppliedScreenSize;
        Image m_ComputerButtonBg, m_PhoneButtonBg, m_FullButtonBg, m_LowButtonBg;
        Text m_FullButtonText, m_LowButtonText;
        Text m_ComputerButtonText, m_PhoneButtonText, m_SensitivityValue, m_FrameRate;
        float m_SmoothedFrameTime = 1f / 60f;
        Font m_Font;
        GraphicRaycaster m_Raycaster;
        FakeCursor m_FakeCursor;
        readonly List<RaycastResult> m_Hits = new List<RaycastResult>();

        public static ControlsHUD Create()
        {
            var go = new GameObject("Controls HUD");
            return go.AddComponent<ControlsHUD>();
        }

        void Awake()
        {
            m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystem();
            Build();
            InputModeManager.ModeChanged += OnModeChanged;
            OnModeChanged(InputModeManager.Mode);
        }

        void OnDestroy() => InputModeManager.ModeChanged -= OnModeChanged;

        // Frame rate shown in the settings menu, smoothed so it's readable.
        void Update()
        {
            // The game is running: the website's loading cover can go.
            WebPage.Ready();
            m_SmoothedFrameTime = Mathf.Lerp(m_SmoothedFrameTime, Time.unscaledDeltaTime, 0.05f);
            // Menu open while the real mouse is captured for looking: the game's own pointer.
            m_FakeCursor.Tick(IsMenuOpen && Cursor.lockState == CursorLockMode.Locked
                && InputModeManager.Mode == ControlMode.KeyboardMouse);
            if (IsMenuOpen)
                m_FrameRate.text = Mathf.RoundToInt(1f / m_SmoothedFrameTime) + " fps";
            FitSafeArea();
        }

        // Keeps the controls inside the part of the screen that's actually usable: the whole
        // window on a computer, and clear of notches and rounded corners on phones. Rechecked
        // every frame, so it follows window resizes and rotation.
        // When a phone rotates, the new screen size and the new safe area can arrive on different
        // frames, so recompute whenever either changes, and keep the result inside the screen.
        void FitSafeArea()
        {
            var safe = Screen.safeArea;
            var size = new Vector2(Screen.width, Screen.height);
            if ((safe == m_AppliedSafeArea && size == m_AppliedScreenSize) || size.x <= 0f || size.y <= 0f) return;
            m_AppliedSafeArea = safe;
            m_AppliedScreenSize = size;

            Vector2 min = Clamp01(safe.position / size);
            Vector2 max = Clamp01((safe.position + safe.size) / size);
            // A safe area that doesn't fit this screen (stale during rotation) is ignored.
            if (max.x - min.x < 0.5f || max.y - min.y < 0.5f) { min = Vector2.zero; max = Vector2.one; }
            m_SafeArea.anchorMin = min;
            m_SafeArea.anchorMax = max;
            m_SafeArea.offsetMin = m_SafeArea.offsetMax = Vector2.zero;
        }

        static Vector2 Clamp01(Vector2 v) => new Vector2(Mathf.Clamp01(v.x), Mathf.Clamp01(v.y));

        /// <summary>
        /// Phone buttons in the bottom-right corner, bottom one first; pass none to hide them.
        /// </summary>
        public void SetContextButtons(params (string label, System.Action action)[] buttons)
        {
            for (int i = 0; i < m_ContextButtons.Length; i++)
            {
                bool show = i < buttons.Length;
                m_ContextButtons[i].SetActive(show);
                if (!show) continue;
                m_ContextLabels[i].text = buttons[i].label;
                m_ContextActions[i] = buttons[i].action;
            }
        }

        /// <summary>Computer key hints along the bottom, like "[E] to get off"; pass none to hide.</summary>
        public void SetKeyHints(params (string key, string label)[] hints) => SetKeyHints(1f, hints);

        /// <summary>Key hints at a given opacity (0 to 1), for hints that fade out.</summary>
        public void SetKeyHints(float opacity, params (string key, string label)[] hints)
        {
            m_HintGroup.alpha = opacity;
            m_HintBar.SetActive(hints.Length > 0);
            for (int i = 0; i < m_Hints.Length; i++)
            {
                bool show = i < hints.Length;
                m_Hints[i].SetActive(show);
                if (!show) continue;
                m_HintKeys[i].text = hints[i].key;
                m_HintLabels[i].text = hints[i].label;
            }
        }

        public void SetMenuOpen(bool open) => m_Menu.SetActive(open);
        public void ToggleMenu() => SetMenuOpen(!IsMenuOpen);

        /// <summary>Shows the centre plus; it grows slightly when it's over something clickable.</summary>
        public void SetCrosshair(bool visible, bool overClickable)
        {
            m_Crosshair.gameObject.SetActive(visible);
            float scale = Mathf.Lerp(m_Crosshair.localScale.x, overClickable ? 1.35f : 1f, 1f - Mathf.Exp(-15f * Time.unscaledDeltaTime));
            m_Crosshair.localScale = Vector3.one * scale;
        }

        /// <summary>True if a clickable HUD element is under this screen point.</summary>
        public bool IsPointerOverInteractive(Vector2 screenPosition)
        {
            if (EventSystem.current == null) return false;
            var data = new PointerEventData(EventSystem.current) { position = screenPosition };
            m_Hits.Clear();
            m_Raycaster.Raycast(data, m_Hits);
            foreach (var hit in m_Hits)
                if (hit.gameObject.GetComponentInParent<Selectable>() != null || IsMenuOpen)
                    return true;
            return false;
        }

        void OnModeChanged(ControlMode mode)
        {
            bool phone = mode == ControlMode.Touchscreen;
            m_TouchControls.SetActive(phone);
            m_ComputerButtonBg.color = phone ? k_Idle : k_Accent;
            m_ComputerButtonText.color = phone ? k_Ink : Color.white;
            m_PhoneButtonBg.color = phone ? k_Accent : k_Idle;
            m_PhoneButtonText.color = phone ? Color.white : k_Ink;
            PointerPicture.Apply(!phone);
            m_MouseRow.SetActive(!phone);
            m_QNote.SetActive(!phone);
            m_Lower.anchoredPosition = new Vector2(0f, phone ? 0f : -MouseRowHeight);
            m_Card.sizeDelta = new Vector2(m_Card.sizeDelta.x, phone ? CardHeight : CardHeight + MouseRowHeight);
            // Each mode has its own sensitivity range, so the number changes with the mode.
            m_SensitivityValue.text = LookSettings.Sensitivity.ToString("0.0") + "×";
            // Each mode has its own field of view, so the slider moves to this mode's.
            if (m_FovSlider != null) m_FovSlider.SetValueWithoutNotify(FovSettings.SliderPosition);
            if (m_FovValue != null) m_FovValue.text = Mathf.RoundToInt(FovSettings.FieldOfView) + "°";
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null)
                return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }

        void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            m_Raycaster = gameObject.AddComponent<GraphicRaycaster>();
            var canvasRect = (RectTransform)transform;
            // Everything except the menu's dimmed backdrop lives inside the safe area.
            m_SafeArea = NewRect("Safe Area", canvasRect, Vector2.zero, Vector2.one);
            var root = m_SafeArea;

            // Touch controls: left side moves, right side looks.
            m_TouchControls = NewRect("Touch Controls", root, Vector2.zero, Vector2.one).gameObject;

            var lookArea = NewRect("Look Area", m_TouchControls.transform, new Vector2(0.4f, 0f), Vector2.one);
            AddImage(lookArea.gameObject, Color.clear);
            LookPad = lookArea.gameObject.AddComponent<TouchLookPad>();

            var moveArea = NewRect("Move Area", m_TouchControls.transform, Vector2.zero, new Vector2(0.4f, 1f));
            AddImage(moveArea.gameObject, Color.clear);

            var stickBase = NewRect("Stick Base", moveArea, Vector2.zero, Vector2.zero);
            stickBase.sizeDelta = new Vector2(240f, 240f);
            stickBase.anchoredPosition = new Vector2(230f, 230f);
            stickBase.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
            AddImage(NewRect("Fill", stickBase, Vector2.zero, Vector2.one).gameObject, new Color(0f, 0f, 0f, 0.12f), UISprites.Circle, false);
            AddImage(NewRect("Ring", stickBase, Vector2.zero, Vector2.one).gameObject, new Color(1f, 1f, 1f, 0.9f), UISprites.Ring, false);

            var knob = NewRect("Knob", stickBase, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            knob.sizeDelta = new Vector2(104f, 104f);
            AddImage(knob.gameObject, new Color(1f, 1f, 1f, 0.95f), UISprites.Circle, false);

            Joystick = moveArea.gameObject.AddComponent<VirtualJoystick>();
            Joystick.baseRect = stickBase;
            Joystick.knob = knob;
            Joystick.radius = 100f;

            // Small plus in the centre for computer mode: clicks come from here.
            m_Crosshair = NewRect("Crosshair", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            m_Crosshair.sizeDelta = new Vector2(28f, 28f);
            foreach (bool horizontal in new[] { true, false })
            {
                // A soft dark outline under each white bar keeps it visible on the white room.
                var shadow = NewRect("Outline", m_Crosshair, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                shadow.sizeDelta = horizontal ? new Vector2(24f, 6f) : new Vector2(6f, 24f);
                AddImage(shadow.gameObject, new Color(0f, 0f, 0f, 0.35f), UISprites.RoundedPanel, false, 3f);
                var bar = NewRect("Bar", m_Crosshair, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                bar.sizeDelta = horizontal ? new Vector2(20f, 2.5f) : new Vector2(2.5f, 20f);
                AddImage(bar.gameObject, Color.white, UISprites.RoundedPanel, false, 1.25f);
            }

            // Phone Jump button, always in the bottom-right corner (part of the touch controls).
            var jump = NewRect("Jump Button", m_TouchControls.transform, new Vector2(1f, 0f), new Vector2(1f, 0f));
            jump.sizeDelta = new Vector2(240f, 84f);
            jump.anchoredPosition = new Vector2(-160f, 140f);
            AddImage(jump.gameObject, new Color(0.06f, 0.07f, 0.09f, 0.55f), UISprites.RoundedPanel, true, 42f);
            jump.gameObject.AddComponent<PressButton>().pressed = () => m_JumpRequested = true;
            AddText(jump, "Jump", 30, Color.white, FontStyle.Bold);
            m_JumpButton = jump.gameObject;

            // Other phone buttons, stacked above Jump (Mount, Play / Pause, ...).
            for (int i = 0; i < m_ContextButtons.Length; i++)
            {
                int index = i;
                var rect = NewRect("Context Button " + i, root, new Vector2(1f, 0f), new Vector2(1f, 0f));
                rect.sizeDelta = new Vector2(240f, 84f);
                rect.anchoredPosition = new Vector2(-160f, 140f + (i + 1) * 104f);
                var bg = AddImage(rect.gameObject, new Color(0.06f, 0.07f, 0.09f, 0.72f), UISprites.RoundedPanel, true, 42f);
                var button = rect.gameObject.AddComponent<Button>();
                button.targetGraphic = bg;
                button.onClick.AddListener(() => m_ContextActions[index]?.Invoke());
                m_ContextLabels[i] = AddText(rect, "", 30, Color.white, FontStyle.Bold);
                m_ContextButtons[i] = rect.gameObject;
                rect.gameObject.SetActive(false);
            }

            // Computer key hints: a dark pill along the bottom holding one or two "[key] label" pairs.
            var hintBar = NewRect("Key Hints", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            hintBar.pivot = new Vector2(0.5f, 0f);
            hintBar.anchoredPosition = new Vector2(0f, 30f);
            // Barely there: a faint dark tint behind white text, so it reads on the white room.
            AddImage(hintBar.gameObject, new Color(0.06f, 0.07f, 0.09f, 0.22f), UISprites.RoundedPanel, false, 26f);
            var barLayout = hintBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            barLayout.padding = new RectOffset(10, 22, 8, 8);
            barLayout.spacing = 28f;
            barLayout.childAlignment = TextAnchor.MiddleCenter;
            barLayout.childControlWidth = barLayout.childControlHeight = true;
            barLayout.childForceExpandWidth = barLayout.childForceExpandHeight = false;
            var barFit = hintBar.gameObject.AddComponent<ContentSizeFitter>();
            barFit.horizontalFit = barFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            for (int i = 0; i < m_Hints.Length; i++)
            {
                var entry = NewRect("Hint " + i, hintBar, Vector2.zero, Vector2.zero);
                var entryLayout = entry.gameObject.AddComponent<HorizontalLayoutGroup>();
                entryLayout.spacing = 10f;
                entryLayout.childAlignment = TextAnchor.MiddleLeft;
                entryLayout.childControlWidth = entryLayout.childControlHeight = true;
                entryLayout.childForceExpandWidth = entryLayout.childForceExpandHeight = false;

                var cap = NewRect("Key", entry, Vector2.zero, Vector2.zero);
                AddImage(cap.gameObject, new Color(1f, 1f, 1f, 0.16f), UISprites.RoundedPanel, false, 8f);
                var capLayout = cap.gameObject.AddComponent<HorizontalLayoutGroup>();
                capLayout.padding = new RectOffset(10, 10, 5, 5);
                capLayout.childControlWidth = capLayout.childControlHeight = true;
                var capLabel = NewRect("Text", cap, Vector2.zero, Vector2.zero);
                m_HintKeys[i] = capLabel.gameObject.AddComponent<Text>();
                StyleText(m_HintKeys[i], 22, Color.white, FontStyle.Bold);

                var label = NewRect("Label", entry, Vector2.zero, Vector2.zero);
                m_HintLabels[i] = label.gameObject.AddComponent<Text>();
                StyleText(m_HintLabels[i], 24, Color.white, FontStyle.Normal);
                m_Hints[i] = entry.gameObject;
            }
            m_HintBar = hintBar.gameObject;
            m_HintGroup = m_HintBar.AddComponent<CanvasGroup>();
            m_HintGroup.blocksRaycasts = false;
            m_HintBar.SetActive(false);

            m_Menu = BuildMenu(canvasRect);
            m_Menu.SetActive(false);

            BuildMenuButton(root);
            m_FakeCursor = gameObject.AddComponent<FakeCursor>();
            m_FakeCursor.Build(canvasRect);
        }

        // Dark round button with three white lines, so it stands out against the white room.
        void BuildMenuButton(RectTransform root)
        {
            var button = NewRect("Menu Button", root, Vector2.one, Vector2.one);
            button.sizeDelta = new Vector2(96f, 96f);
            button.anchoredPosition = new Vector2(-70f, -70f);
            var bg = AddImage(button.gameObject, new Color(0.06f, 0.07f, 0.09f, 0.62f), UISprites.Circle, true);
            var b = button.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            var colors = b.colors;
            colors.highlightedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            b.colors = colors;
            b.onClick.AddListener(ToggleMenu);

            for (int i = -1; i <= 1; i++)
            {
                var line = NewRect("Line", button, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                line.sizeDelta = new Vector2(40f, 7f);
                line.anchoredPosition = new Vector2(0f, i * 12f);
                // Radius of half the height gives fully rounded ends.
                AddImage(line.gameObject, Color.white, UISprites.RoundedPanel, false, 3.5f);
            }
        }

        GameObject BuildMenu(RectTransform root)
        {
            var menu = NewRect("Settings Menu", root, Vector2.zero, Vector2.one);

            // Tapping outside the card closes the menu.
            var dim = AddImage(menu.gameObject, new Color(0f, 0f, 0f, 0.3f));
            var dimButton = menu.gameObject.AddComponent<Button>();
            dimButton.transition = Selectable.Transition.None;
            dimButton.targetGraphic = dim;
            dimButton.onClick.AddListener(() => SetMenuOpen(false));

            var card = NewRect("Card", menu, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            card.sizeDelta = new Vector2(620f, CardHeight);
            m_Card = card;
            AddImage(card.gameObject, new Color(0.98f, 0.98f, 0.98f, 0.97f), UISprites.RoundedPanel, true, 20f);
            // Clicks on the card itself (between controls, or letting go of a slider) stop here
            // instead of reaching the backdrop behind it, which closes the menu.
            card.gameObject.AddComponent<ClickCatcher>();

            Label(card, "Settings", 36, k_Ink, -58f, FontStyle.Bold);
            m_FrameRate = Label(card, "", 20, k_Muted, -36f);
            m_FrameRate.alignment = TextAnchor.UpperRight;
            m_FrameRate.rectTransform.offsetMax = new Vector2(-28f, m_FrameRate.rectTransform.offsetMax.y);

            Label(card, "Controls", 24, k_Muted, -118f);
            (m_ComputerButtonBg, m_ComputerButtonText) = AddChoice(card, "Computer", new Vector2(-128f, -178f),
                () => InputModeManager.Mode = ControlMode.KeyboardMouse);
            (m_PhoneButtonBg, m_PhoneButtonText) = AddChoice(card, "Phone", new Vector2(128f, -178f),
                () => InputModeManager.Mode = ControlMode.Touchscreen);

            // Computer mode only: right under the Controls choice; the rest moves down for it.
            m_MouseRow = AddSwitch(card, -258f, "Mouse?", () => MouseSettings.Enabled, on => MouseSettings.Enabled = on);
            // Computer mode only: a small reminder in the top-left corner.
            var qNote = Label(card, "Press Q to open/close", 20, k_Muted, -36f);
            qNote.alignment = TextAnchor.UpperLeft;
            qNote.rectTransform.offsetMin = new Vector2(28f, qNote.rectTransform.offsetMin.y);
            m_QNote = qNote.gameObject;

            var lower = NewRect("Lower Settings", card, new Vector2(0f, 1f), new Vector2(1f, 1f));
            m_Lower = lower;

            Label(lower, "Look sensitivity", 24, k_Muted, -262f);
            m_SensitivityValue = Label(lower, "", 26, k_Ink, -350f, FontStyle.Bold);
            AddSlider(lower, -308f, "Sensitivity Slider", () => LookSettings.SliderPosition, v =>
            {
                LookSettings.SliderPosition = v;
                m_SensitivityValue.text = LookSettings.Sensitivity.ToString("0.0") + "×";
            });
            m_SensitivityValue.text = LookSettings.Sensitivity.ToString("0.0") + "×";

            Label(lower, "Field of view", 24, k_Muted, -418f);
            m_FovValue = Label(lower, "", 26, k_Ink, -506f, FontStyle.Bold);
            m_FovSlider = AddSlider(lower, -464f, "Field Of View Slider", () => FovSettings.SliderPosition, v =>
            {
                FovSettings.SliderPosition = v;
                m_FovValue.text = Mathf.RoundToInt(FovSettings.FieldOfView) + "°";
            });
            m_FovValue.text = Mathf.RoundToInt(FovSettings.FieldOfView) + "°";

            Label(lower, "Graphics", 24, k_Muted, -574f);
            (m_FullButtonBg, m_FullButtonText) = AddChoice(lower, "Full", new Vector2(-128f, -634f), () => SetGraphics(false));
            (m_LowButtonBg, m_LowButtonText) = AddChoice(lower, "Low", new Vector2(128f, -634f), () => SetGraphics(true));
            ShowGraphics(GraphicsQuality.Low);

            AddSwitch(lower, -724f, "Hints", () => HintSettings.Enabled, on => HintSettings.Enabled = on);

            var done = NewRect("Done", card, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            done.sizeDelta = new Vector2(500f, 84f);
            done.anchoredPosition = new Vector2(0f, 72f);
            var doneBg = AddImage(done.gameObject, k_Ink, UISprites.RoundedPanel, true, 14f);
            var doneButton = done.gameObject.AddComponent<Button>();
            doneButton.targetGraphic = doneBg;
            doneButton.onClick.AddListener(() => SetMenuOpen(false));
            AddText(done, "Done", 30, Color.white, FontStyle.Bold);

            return menu.gameObject;
        }

        // Full: sharp, every effect. Low: lower resolution and cheaper effects, for lag.
        void SetGraphics(bool low)
        {
            GraphicsQuality.Low = low;
            ShowGraphics(low);
        }

        void ShowGraphics(bool low)
        {
            m_FullButtonBg.color = low ? k_Idle : k_Accent;
            m_FullButtonText.color = low ? k_Ink : Color.white;
            m_LowButtonBg.color = low ? k_Accent : k_Idle;
            m_LowButtonText.color = low ? Color.white : k_Ink;
        }

        (Image, Text) AddChoice(RectTransform parent, string label, Vector2 position, UnityEngine.Events.UnityAction onClick)
        {
            var rect = NewRect(label, parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            rect.sizeDelta = new Vector2(240f, 80f);
            rect.anchoredPosition = position;
            var bg = AddImage(rect.gameObject, k_Idle, UISprites.RoundedPanel, true, 14f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            var colors = button.colors;
            colors.highlightedColor = new Color(0.94f, 0.94f, 0.94f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;
            button.onClick.AddListener(onClick);
            return (bg, AddText(rect, label, 30, k_Ink, FontStyle.Bold));
        }

        // A slider from 0 to 1 (the middle is 0.5), with the same look as the rest of the menu.
        Slider AddSlider(RectTransform parent, float y, string name, System.Func<float> get, System.Action<float> set)
        {
            var area = NewRect(name, parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            area.sizeDelta = new Vector2(500f, 44f);
            area.anchoredPosition = new Vector2(0f, y);

            var track = NewRect("Track", area, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f));
            track.sizeDelta = new Vector2(0f, 8f);
            AddImage(track.gameObject, new Color(0.85f, 0.85f, 0.86f, 1f), UISprites.RoundedPanel, true, 4f);

            var fillArea = NewRect("Fill Area", area, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f));
            fillArea.sizeDelta = new Vector2(-22f, 8f);
            var fill = NewRect("Fill", fillArea, Vector2.zero, Vector2.one);
            fill.sizeDelta = new Vector2(22f, 0f);
            AddImage(fill.gameObject, new Color(0.3f, 0.3f, 0.32f, 1f), UISprites.RoundedPanel, false, 4f);

            var handleArea = NewRect("Handle Area", area, Vector2.zero, Vector2.one);
            handleArea.sizeDelta = new Vector2(-22f, 0f);
            var handle = NewRect("Handle", handleArea, Vector2.zero, Vector2.one);
            handle.sizeDelta = new Vector2(44f, 0f);
            AddImage(NewRect("Edge", handle, Vector2.zero, Vector2.one).gameObject, new Color(0f, 0f, 0f, 0.22f), UISprites.Circle, false)
                .rectTransform.sizeDelta = new Vector2(3f, 3f);
            var knob = AddImage(NewRect("Knob", handle, Vector2.zero, Vector2.one).gameObject, Color.white, UISprites.Circle, true);

            var slider = area.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = knob;
            slider.direction = Slider.Direction.LeftToRight;
            // The value moves along a curve around the middle; the label shows the actual value.
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = get();
            slider.onValueChanged.AddListener(v => set(v));
            return slider;
        }

        // "Hints" with an on/off switch: a rounded track with a sliding knob, dark when on.
        // A labelled on/off switch row.
        GameObject AddSwitch(RectTransform parent, float y, string title, System.Func<bool> get, System.Action<bool> set)
        {
            var row = NewRect(title + " Row", parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            row.sizeDelta = new Vector2(500f, 56f);
            row.anchoredPosition = new Vector2(0f, y);
            var label = AddText(row, title, 26, k_Ink, FontStyle.Bold);
            label.alignment = TextAnchor.MiddleLeft;

            var track = NewRect("Switch", row, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            track.sizeDelta = new Vector2(92f, 50f);
            track.anchoredPosition = new Vector2(-46f, 0f);
            var trackImage = AddImage(track.gameObject, k_Idle, UISprites.RoundedPanel, true, 25f);
            var knob = NewRect("Knob", track, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            knob.sizeDelta = new Vector2(40f, 40f);
            AddImage(knob.gameObject, Color.white, UISprites.Circle, false);

            void Show(bool on)
            {
                trackImage.color = on ? k_Ink : new Color(0.82f, 0.82f, 0.83f, 1f);
                knob.anchoredPosition = new Vector2(on ? 67f : 25f, 0f);
            }

            var button = track.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = trackImage;
            button.onClick.AddListener(() =>
            {
                set(!get());
                Show(get());
            });
            Show(get());
            return row.gameObject;
        }

        Text Label(RectTransform parent, string value, int size, Color color, float y, FontStyle style = FontStyle.Normal)
        {
            var rect = NewRect(value + " Label", parent, new Vector2(0f, 1f), new Vector2(1f, 1f));
            rect.sizeDelta = new Vector2(0f, 50f);
            rect.anchoredPosition = new Vector2(0f, y);
            return AddText(rect, value, size, color, style);
        }

        static RectTransform NewRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        // cornerRadius is in UI units; for a rounded shape it sets how big the corners are drawn.
        static Image AddImage(GameObject go, Color color, Sprite sprite = null, bool raycast = true, float cornerRadius = 0f)
        {
            var image = go.AddComponent<Image>();
            image.color = color;
            image.sprite = sprite;
            image.raycastTarget = raycast;
            if (sprite != null && sprite.border != Vector4.zero)
            {
                image.type = Image.Type.Sliced;
                if (cornerRadius > 0f)
                    image.pixelsPerUnitMultiplier = sprite.border.x / cornerRadius;
            }
            return image;
        }

        void StyleText(Text text, int size, Color color, FontStyle style)
        {
            text.font = m_Font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;
        }

        Text AddText(RectTransform parent, string value, int size, Color color, FontStyle style = FontStyle.Normal)
        {
            var rect = NewRect("Label", parent, Vector2.zero, Vector2.one);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = m_Font;
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            return text;
        }
    }
}
