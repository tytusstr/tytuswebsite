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
        // Dark, slightly see-through settings card: light text, white for the chosen option
        // (with dark lettering), faint white for the rest.
        static readonly Color k_Ink = new Color(0.94f, 0.94f, 0.96f, 1f);
        static readonly Color k_Muted = new Color(0.62f, 0.63f, 0.67f, 1f);
        static readonly Color k_Accent = new Color(0.97f, 0.97f, 0.98f, 1f);
        static readonly Color k_Idle = new Color(1f, 1f, 1f, 0.09f);
        static readonly Color k_OnAccent = new Color(0.08f, 0.08f, 0.1f, 1f);
        static readonly Color k_Card = new Color(0.06f, 0.065f, 0.075f, 0.86f);

        public VirtualJoystick Joystick { get; private set; }
        public TouchLookPad LookPad { get; private set; }
        public bool IsMenuOpen => (m_Menu != null && m_Menu.activeSelf) || BoardPanel.IsOpen;

        /// <summary>True while the settings or the board panel are open (anywhere).</summary>
        public static bool AnyMenuOpen => s_Instance != null && s_Instance.IsMenuOpen;
        static ControlsHUD s_Instance;

        bool m_JumpRequested;
        GameObject m_JumpButton, m_SprintButton;
        Image m_SprintBg;
        Text m_SprintText;

        /// <summary>Phone: the Sprint button is switched on (tap to sprint, tap again or stop to walk).</summary>
        public bool SprintOn { get; private set; }

        public void SetSprint(bool on)
        {
            SprintOn = on;
            if (m_SprintBg != null) m_SprintBg.color = on ? new Color(1f, 1f, 1f, 0.92f) : new Color(0.06f, 0.07f, 0.09f, 0.55f);
            if (m_SprintText != null) m_SprintText.color = on ? k_Ink : Color.white;
        }

        /// <summary>Shows or hides the phone Sprint button (hidden while seated or riding).</summary>
        public void SetSprintVisible(bool visible)
        {
            if (m_SprintButton == null || m_SprintButton.activeSelf == visible) return;
            m_SprintButton.SetActive(visible);
            if (!visible) SetSprint(false);
        }

        // Sprint stamina: a thin bar in the bottom-right corner, under the buttons, shown while
        // it isn't full.
        RectTransform m_StaminaFill;
        CanvasGroup m_StaminaGroup;

        public void SetStamina(float level)
        {
            if (m_StaminaFill == null)
            {
                var bar = NewRect("Stamina", m_SafeArea, new Vector2(1f, 0f), new Vector2(1f, 0f));
                bar.pivot = new Vector2(1f, 0f);
                bar.sizeDelta = new Vector2(240f, 12f);
                bar.anchoredPosition = new Vector2(-40f, 22f);
                AddImage(bar.gameObject, new Color(0.06f, 0.07f, 0.09f, 0.55f), UISprites.RoundedPanel, false, 6f);
                m_StaminaFill = NewRect("Fill", bar, Vector2.zero, Vector2.one);
                m_StaminaFill.offsetMin = new Vector2(2f, 2f);
                m_StaminaFill.offsetMax = new Vector2(-2f, -2f);
                AddImage(m_StaminaFill.gameObject, Color.white, UISprites.RoundedPanel, false, 4f);
                m_StaminaGroup = bar.gameObject.AddComponent<CanvasGroup>();
                m_StaminaGroup.blocksRaycasts = false;
            }
            level = Mathf.Clamp01(level);
            m_StaminaFill.anchorMax = new Vector2(Mathf.Max(0.02f, level), 1f);
            m_StaminaFill.GetComponent<Image>().color = level < 0.25f ? new Color(1f, 0.45f, 0.4f) : Color.white;
            float target = level < 0.999f && !IsMenuOpen ? 1f : 0f;
            m_StaminaGroup.alpha = Mathf.MoveTowards(m_StaminaGroup.alpha, target, Time.unscaledDeltaTime * 3f);
        }

        /// <summary>Shows or hides the phone Jump button (hidden while riding); the other buttons drop into its place.</summary>
        /// <summary>Shows or hides the movement joystick (hidden while seated).</summary>
        public void SetJoystickVisible(bool visible)
        {
            if (Joystick != null && Joystick.gameObject.activeSelf != visible) Joystick.gameObject.SetActive(visible);
        }

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
        const float CardWidth = 1180f, CardHeight = 760f;
        Text m_CharacterValue, m_NameStatus, m_HelpText, m_Online;
        GameObject m_TurnHint;
        InputField m_NameField;
        GameObject[] m_Pages;
        Image[] m_TabBgs;
        Text[] m_TabTexts;
        int m_Tab;
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
            s_Instance = this;
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
            {
                m_FrameRate.text = Mathf.RoundToInt(1f / m_SmoothedFrameTime) + " fps";
                ShowCharacter();
                FitCard();
                if (m_TurnHint.activeSelf != HintSettings.Enabled) m_TurnHint.SetActive(HintSettings.Enabled);
                bool qNote = HintSettings.Enabled && InputModeManager.Mode == ControlMode.KeyboardMouse;
                if (m_QNote.activeSelf != qNote) m_QNote.SetActive(qNote);
            }
            FitSafeArea();
            // How many people are here, snug in the bottom-left corner.
            int online = Multiplayer.PlayerCount;
            m_Online.text = online > 0 ? online + " online" : "";
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

        // The settings card shrinks to fit short screens (it's tall in computer mode).
        void FitCard()
        {
            var screen = ((RectTransform)transform).rect;
            float scale = Mathf.Min(1f, (screen.height - 40f) / CardHeight, (screen.width - 30f) / CardWidth);
            if (!Mathf.Approximately(m_Card.localScale.x, scale)) m_Card.localScale = Vector3.one * scale;
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

        // The first-moments tutorial: a little picture and "KEY what it does" on each line, one
        // under the other, their left edges lined up, the block centred at the bottom. No boxes:
        // white text with a soft dark edge so it reads on the white room.
        readonly List<(GameObject line, Image icon, Text label, CanvasGroup group)> m_TutorialLines = new List<(GameObject, Image, Text, CanvasGroup)>();
        RectTransform m_TutorialBlock;
        static readonly Dictionary<string, Sprite> s_Icons = new Dictionary<string, Sprite>();

        static Sprite Icon(string name)
        {
            if (s_Icons.TryGetValue(name, out var sprite)) return sprite;
            var tex = Resources.Load<Texture2D>("Icons/" + name);
            sprite = tex != null ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f)) : null;
            s_Icons[name] = sprite;
            return sprite;
        }

        /// <summary>Computer: tutorial lines (icon name in Resources/Icons, rich text) at the bottom; none hides them.</summary>
        public void SetTutorial(float opacity, params (string icon, string text)[] lines)
        {
            if (m_TutorialBlock == null)
            {
                m_TutorialBlock = NewRect("Tutorial", m_SafeArea, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
                m_TutorialBlock.pivot = new Vector2(0.5f, 0f);
                m_TutorialBlock.anchoredPosition = new Vector2(0f, 34f);
                var layout = m_TutorialBlock.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.spacing = 6f;
                layout.childAlignment = TextAnchor.LowerLeft;
                layout.childControlWidth = layout.childControlHeight = true;
                layout.childForceExpandWidth = layout.childForceExpandHeight = false;
                var fit = m_TutorialBlock.gameObject.AddComponent<ContentSizeFitter>();
                fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            while (m_TutorialLines.Count < lines.Length) m_TutorialLines.Add(BuildTutorialLine());
            for (int i = 0; i < m_TutorialLines.Count; i++)
            {
                var line = m_TutorialLines[i];
                bool show = i < lines.Length && opacity > 0.001f;
                if (line.line.activeSelf != show) line.line.SetActive(show);
                if (!show) continue;
                line.icon.sprite = Icon(lines[i].icon);
                line.label.text = lines[i].text;
                line.group.alpha = opacity;
            }
        }

        (GameObject, Image, Text, CanvasGroup) BuildTutorialLine()
        {
            var row = NewRect("Tutorial Line", m_TutorialBlock, Vector2.zero, Vector2.zero);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            var iconRect = NewRect("Icon", row, Vector2.zero, Vector2.zero);
            var icon = iconRect.gameObject.AddComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            var size = iconRect.gameObject.AddComponent<LayoutElement>();
            size.preferredWidth = size.preferredHeight = size.minWidth = size.minHeight = 44f;
            var label = NewRect("Label", row, Vector2.zero, Vector2.zero).gameObject.AddComponent<Text>();
            StyleText(label, 26, Color.white, FontStyle.Normal);
            label.alignment = TextAnchor.MiddleLeft;
            label.supportRichText = true;
            var shadow = label.gameObject.AddComponent<Outline>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(1.2f, -1.2f);
            var group = row.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            row.gameObject.SetActive(false);
            return (row.gameObject, icon, label, group);
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

        public void SetMenuOpen(bool open)
        {
            m_Menu.SetActive(open);
            if (open)
            {
                if (m_NameField != null && !m_NameField.isFocused) m_NameField.text = WebPage.PlayerName;
                if (m_NameStatus != null) m_NameStatus.text = "";
            }
            else if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        /// <summary>Opens the settings on the Help page.</summary>
        public void OpenHelp()
        {
            SetMenuOpen(true);
            ShowTab(4);
        }
        public void ToggleMenu()
        {
            if (BoardPanel.IsOpen) { BoardPanel.Instance.Close(); return; }
            SetMenuOpen(!(m_Menu != null && m_Menu.activeSelf));
        }

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
            m_ComputerButtonText.color = phone ? k_Ink : k_OnAccent;
            m_PhoneButtonBg.color = phone ? k_Accent : k_Idle;
            m_PhoneButtonText.color = phone ? k_OnAccent : k_Ink;
            PointerPicture.Apply(!phone);
            m_MouseRow.SetActive(!phone);
            if (m_HelpText != null) m_HelpText.text = HelpText(phone);
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

            // Phone Sprint button, just left of Jump: tap to sprint, tap again to walk.
            var sprint = NewRect("Sprint Button", m_TouchControls.transform, new Vector2(1f, 0f), new Vector2(1f, 0f));
            sprint.sizeDelta = new Vector2(240f, 84f);
            sprint.anchoredPosition = new Vector2(-420f, 140f);
            m_SprintBg = AddImage(sprint.gameObject, new Color(0.06f, 0.07f, 0.09f, 0.55f), UISprites.RoundedPanel, true, 42f);
            sprint.gameObject.AddComponent<PressButton>().pressed = () => SetSprint(!SprintOn);
            m_SprintText = AddText(sprint, "Sprint", 30, Color.white, FontStyle.Bold);
            m_SprintButton = sprint.gameObject;

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
            ChatBox.Create(root, m_Font);
            BoardPanel.Create(canvasRect, m_Font);
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

            // The number of people here, small and snug in the bottom-left corner.
            var online = NewRect("Online", root, Vector2.zero, Vector2.zero);
            online.pivot = new Vector2(0f, 0f);
            online.sizeDelta = new Vector2(330f, 40f);
            // (In from the corner: phone screens have rounded corners.)
            online.anchoredPosition = new Vector2(44f, 14f);
            m_Online = AddText(online, "", 27, Color.white, FontStyle.Bold);
            m_Online.alignment = TextAnchor.LowerLeft;
            var outline = m_Online.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.55f);
            outline.effectDistance = new Vector2(1f, -1f);

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
            var dim = AddImage(menu.gameObject, new Color(0f, 0f, 0f, 0.25f));
            var dimButton = menu.gameObject.AddComponent<Button>();
            dimButton.transition = Selectable.Transition.None;
            dimButton.targetGraphic = dim;
            dimButton.onClick.AddListener(() => SetMenuOpen(false));

            var card = NewRect("Card", menu, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            card.sizeDelta = new Vector2(CardWidth, CardHeight);
            m_Card = card;
            AddImage(card.gameObject, k_Card, UISprites.RoundedPanel, true, 12f);
            // Clicks on the card itself (between controls, or letting go of a slider) stop here
            // instead of reaching the backdrop behind it, which closes the menu.
            card.gameObject.AddComponent<ClickCatcher>();

            var title = Corner(card, "Settings", 34, k_Ink, new Vector2(36f, -30f), FontStyle.Bold);
            title.alignment = TextAnchor.UpperLeft;
            m_FrameRate = Corner(card, "", 20, k_Muted, new Vector2(-30f, -36f));
            m_FrameRate.alignment = TextAnchor.UpperRight;
            // (The box the text fills sits in the top-right corner.)
            var fpsBox = (RectTransform)m_FrameRate.rectTransform.parent;
            fpsBox.anchorMin = fpsBox.anchorMax = fpsBox.pivot = new Vector2(1f, 1f);
            fpsBox.anchoredPosition = new Vector2(-30f, -36f);

            // Left: you, turnable, with arrows to change who you are.
            var stage = NewRect("Character", card, new Vector2(0f, 1f), new Vector2(0f, 1f));
            stage.pivot = new Vector2(0f, 1f);
            stage.anchoredPosition = new Vector2(30f, -96f);
            stage.sizeDelta = new Vector2(340f, 520f);
            AddImage(stage.gameObject, new Color(1f, 1f, 1f, 0.05f), UISprites.RoundedPanel, false, 10f);
            var picture = NewRect("Picture", stage, Vector2.zero, Vector2.one);
            CharacterPreview.Create(picture);
            var turnHint = AddText(NewRect("Hint", stage, new Vector2(0f, 0f), new Vector2(1f, 0f)), "Drag to turn", 20, k_Muted);
            m_TurnHint = turnHint.gameObject;
            turnHint.rectTransform.sizeDelta = new Vector2(0f, 40f);
            turnHint.rectTransform.anchoredPosition = new Vector2(0f, 26f);
            var pick = NewRect("Character Row", card, new Vector2(0f, 1f), new Vector2(0f, 1f));
            pick.pivot = new Vector2(0f, 1f);
            pick.anchoredPosition = new Vector2(30f, -630f);
            pick.sizeDelta = new Vector2(340f, 60f);
            m_CharacterValue = AddText(pick, "", 26, k_Ink, FontStyle.Bold);
            SmallButton(pick, "<", new Vector2(0f, 0f), () => ChangeCharacter(-1), new Vector2(0f, 0.5f));
            SmallButton(pick, ">", new Vector2(0f, 0f), () => ChangeCharacter(1), new Vector2(1f, 0.5f));

            // Right: tabs and their pages.
            string[] tabs = { "Controls", "Display", "Player", "Sounds", "Help" };
            m_Pages = new GameObject[tabs.Length];
            m_TabBgs = new Image[tabs.Length];
            m_TabTexts = new Text[tabs.Length];
            for (int i = 0; i < tabs.Length; i++)
            {
                int index = i;
                var tab = NewRect(tabs[i] + " Tab", card, new Vector2(0f, 1f), new Vector2(0f, 1f));
                tab.pivot = new Vector2(0f, 1f);
                tab.anchoredPosition = new Vector2(400f + i * 150f, -96f);
                tab.sizeDelta = new Vector2(142f, 60f);
                m_TabBgs[i] = AddImage(tab.gameObject, k_Idle, UISprites.RoundedPanel, true, 10f);
                var b = tab.gameObject.AddComponent<Button>();
                b.targetGraphic = m_TabBgs[i];
                b.onClick.AddListener(() => ShowTab(index));
                m_TabTexts[i] = AddText(tab, tabs[i], 26, k_Ink, FontStyle.Bold);

                var page = NewRect(tabs[i] + " Page", card, new Vector2(0f, 1f), new Vector2(0f, 1f));
                page.pivot = new Vector2(0f, 1f);
                page.anchoredPosition = new Vector2(400f, -176f);
                page.sizeDelta = new Vector2(750f, 470f);
                m_Pages[i] = page.gameObject;
            }
            BuildControlsPage((RectTransform)m_Pages[0].transform);
            BuildDisplayPage((RectTransform)m_Pages[1].transform);
            BuildPlayerPage((RectTransform)m_Pages[2].transform);
            BuildSoundsPage((RectTransform)m_Pages[3].transform);
            BuildHelpPage((RectTransform)m_Pages[4].transform);
            ShowTab(0);

            // Computer, with hints on: a small reminder beside the title.
            var qNote = Corner(card, "Press Q to open/close", 20, k_Muted, new Vector2(196f, -42f));
            qNote.alignment = TextAnchor.UpperLeft;
            m_QNote = qNote.gameObject;

            var done = NewRect("Done", card, new Vector2(1f, 0f), new Vector2(1f, 0f));
            done.pivot = new Vector2(1f, 0f);
            done.sizeDelta = new Vector2(260f, 70f);
            done.anchoredPosition = new Vector2(-30f, 30f);
            var doneBg = AddImage(done.gameObject, k_Accent, UISprites.RoundedPanel, true, 10f);
            var doneButton = done.gameObject.AddComponent<Button>();
            doneButton.targetGraphic = doneBg;
            doneButton.onClick.AddListener(() => SetMenuOpen(false));
            AddText(done, "Done", 28, k_OnAccent, FontStyle.Bold);

            ShowCharacter();
            return menu.gameObject;
        }

        void BuildControlsPage(RectTransform page)
        {
            Label(page, "Controls", 24, k_Muted, -10f);
            (m_ComputerButtonBg, m_ComputerButtonText) = AddChoice(page, "Computer", new Vector2(-128f, -85f),
                () => InputModeManager.Mode = ControlMode.KeyboardMouse);
            (m_PhoneButtonBg, m_PhoneButtonText) = AddChoice(page, "Mobile", new Vector2(128f, -85f),
                () => InputModeManager.Mode = ControlMode.Touchscreen);
            m_MouseRow = AddSwitch(page, -175f, "Mouse?", () => MouseSettings.Enabled, on => MouseSettings.Enabled = on);

            Label(page, "Look sensitivity", 24, k_Muted, -235f);
            m_SensitivityValue = Label(page, "", 26, k_Ink, -315f, FontStyle.Bold);
            AddSlider(page, -277f, "Sensitivity Slider", () => LookSettings.SliderPosition, v =>
            {
                LookSettings.SliderPosition = v;
                m_SensitivityValue.text = LookSettings.Sensitivity.ToString("0.0") + "×";
            });
            m_SensitivityValue.text = LookSettings.Sensitivity.ToString("0.0") + "×";

            Label(page, "Field of view", 24, k_Muted, -360f);
            m_FovValue = Label(page, "", 26, k_Ink, -440f, FontStyle.Bold);
            m_FovSlider = AddSlider(page, -402f, "Field Of View Slider", () => FovSettings.SliderPosition, v =>
            {
                FovSettings.SliderPosition = v;
                m_FovValue.text = Mathf.RoundToInt(FovSettings.FieldOfView) + "°";
            });
            m_FovValue.text = Mathf.RoundToInt(FovSettings.FieldOfView) + "°";
        }

        void BuildDisplayPage(RectTransform page)
        {
            Label(page, "Graphics", 24, k_Muted, -10f);
            (m_FullButtonBg, m_FullButtonText) = AddChoice(page, "Full", new Vector2(-128f, -85f), () => SetGraphics(false));
            (m_LowButtonBg, m_LowButtonText) = AddChoice(page, "Low", new Vector2(128f, -85f), () => SetGraphics(true));
            ShowGraphics(GraphicsQuality.Low);
            AddSwitch(page, -200f, "Hints", () => HintSettings.Enabled, on => HintSettings.Enabled = on);
            AddSwitch(page, -275f, "Names", () => NameSettings.Enabled, on => NameSettings.Enabled = on);
            AddSwitch(page, -350f, "Chat", () => ChatSettings.Enabled, on => ChatSettings.Enabled = on);
        }

        void BuildPlayerPage(RectTransform page)
        {
            Label(page, "Your name", 24, k_Muted, -10f);
            var box = NewRect("Name Box", page, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            box.sizeDelta = new Vector2(500f, 72f);
            box.anchoredPosition = new Vector2(0f, -90f);
            AddImage(box.gameObject, k_Idle, UISprites.RoundedPanel, true, 10f);
            var text = AddText(box, "", 28, k_Ink);
            text.rectTransform.offsetMin = new Vector2(20f, 0f);
            text.rectTransform.offsetMax = new Vector2(-20f, 0f);
            text.alignment = TextAnchor.MiddleCenter;
            text.supportRichText = false;
            var hint = AddText(box, "Type a name", 28, new Color(1f, 1f, 1f, 0.35f));
            m_NameField = box.gameObject.AddComponent<InputField>();
            m_NameField.textComponent = text;
            m_NameField.placeholder = hint;
            m_NameField.characterLimit = 20;
            m_NameField.lineType = InputField.LineType.SingleLine;
            m_NameField.onEndEdit.AddListener(_ =>
            {
                // Enter saves. On phones the keyboard's own Return/Done key closes the
                // keyboard without a key press reaching the game, so finishing typing saves.
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                bool enter = keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame);
                if (enter || (InputModeManager.Mode == ControlMode.Touchscreen && !m_NameField.wasCanceled)) SaveName();
            });

            var save = NewRect("Save", page, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            save.sizeDelta = new Vector2(240f, 70f);
            save.anchoredPosition = new Vector2(0f, -185f);
            var saveBg = AddImage(save.gameObject, k_Accent, UISprites.RoundedPanel, true, 10f);
            var saveButton = save.gameObject.AddComponent<Button>();
            saveButton.targetGraphic = saveBg;
            saveButton.onClick.AddListener(SaveName);
            AddText(save, "Save", 28, k_OnAccent, FontStyle.Bold);
            m_NameStatus = Label(page, "", 22, k_Muted, -250f);
            var note = Label(page, "Shown above your head to everyone. Up to 20 letters.\nLeave it empty to play without a name.", 22, k_Muted, -330f);
            note.rectTransform.sizeDelta = new Vector2(0f, 80f);
        }

        void SaveName()
        {
            string name = m_NameField.text.Trim();
            if (name.Length > 20) name = name.Substring(0, 20);
            WebPage.SetPlayerName(name);
            m_NameField.text = name;
            m_NameStatus.text = name.Length > 0 ? "Saved" : "Name removed";
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        // Each kind of sound on or off, in two columns.
        void BuildSoundsPage(RectTransform page)
        {
            Label(page, "Sounds", 24, k_Muted, -10f);
            var kinds = SoundSettings.Kinds;
            for (int i = 0; i < kinds.Length; i++)
            {
                var kind = kinds[i].kind;
                float x = i % 2 == 0 ? -190f : 190f;
                float y = -85f - (i / 2) * 80f;
                AddSwitch(page, y, kinds[i].label, () => SoundSettings.Enabled(kind), on => SoundSettings.SetEnabled(kind, on), x, 340f);
            }
        }

        void BuildHelpPage(RectTransform page)
        {
            var area = NewRect("Help Text", page, Vector2.zero, Vector2.one);
            area.offsetMin = new Vector2(10f, 0f);
            area.offsetMax = new Vector2(-10f, -6f);
            m_HelpText = AddText(area, "", 22, k_Ink);
            m_HelpText.alignment = TextAnchor.UpperLeft;
            m_HelpText.supportRichText = true;
            m_HelpText.lineSpacing = 1.05f;
            m_HelpText.text = HelpText(InputModeManager.Mode == ControlMode.Touchscreen);
        }

        static string HelpText(bool mobile)
        {
            string controls = mobile
                ? "<b>Controls</b>\n" +
                  "Left stick: walk          Drag on the right: look around\n" +
                  "Jump / Sprint buttons: jump, sprint (tap Sprint again to walk)\n" +
                  "Buttons appear near things you can use: sit, ride, radio, board\n" +
                  "Chat button (top left): send a message\n"
                : "<b>Keys</b>\n" +
                  "W A S D / arrows: walk          Mouse: look around\n" +
                  "Double tap W, or hold Shift: sprint          Space: jump\n" +
                  "Click: sit, ride, play / pause the radio, use the board\n" +
                  "Right click or N (at the radio): next song          E: get off the scooter\n" +
                  "Enter: chat          Q: settings\n";
            return controls + "\n" +
                   "<b>Why is it slow?</b>\n" +
                   "- Low power mode (battery saver) slows the browser down: turn it off.\n" +
                   "- A slow or patchy internet connection makes other people jump around.\n" +
                   "- Try Graphics: Low (Display tab).\n" +
                   "- Close other tabs and apps.";
        }

        void ShowTab(int index)
        {
            m_Tab = index;
            for (int i = 0; i < m_Pages.Length; i++)
            {
                bool on = i == index;
                m_Pages[i].SetActive(on);
                m_TabBgs[i].color = on ? k_Accent : k_Idle;
                m_TabTexts[i].color = on ? k_OnAccent : k_Ink;
            }
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        // Text placed at a corner of the card.
        Text Corner(RectTransform parent, string value, int size, Color color, Vector2 position, FontStyle style = FontStyle.Normal)
        {
            var rect = NewRect(value + " Text", parent, new Vector2(0f, 1f), new Vector2(0f, 1f));
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(400f, 50f);
            rect.anchoredPosition = position;
            var text = AddText(rect, value, size, color, style);
            return text;
        }

        void SmallButton(RectTransform parent, string label, Vector2 position, UnityEngine.Events.UnityAction action, Vector2 anchor)
        {
            var rect = NewRect(label + " Button", parent, anchor, anchor);
            rect.pivot = anchor;
            rect.sizeDelta = new Vector2(60f, 60f);
            rect.anchoredPosition = position;
            var bg = AddImage(rect.gameObject, k_Idle, UISprites.Circle, true);
            var b = rect.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            b.onClick.AddListener(action);
            AddText(rect, label, 34, k_Ink, FontStyle.Bold);
        }

        void ChangeCharacter(int step)
        {
            var body = LocalPlayerBody.Instance;
            if (body == null) return;
            body.SetAvatar(body.AvatarIndex + step);
            ShowCharacter();
        }

        void ShowCharacter()
        {
            var body = LocalPlayerBody.Instance;
            int count = LocalPlayerBody.Choices;
            if (m_CharacterValue != null) m_CharacterValue.text = body != null && count > 0 ? (body.AvatarIndex + 1) + " / " + count : "";
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
            m_FullButtonText.color = low ? k_Ink : k_OnAccent;
            m_LowButtonBg.color = low ? k_Accent : k_Idle;
            m_LowButtonText.color = low ? k_OnAccent : k_Ink;
        }

        (Image, Text) AddChoice(RectTransform parent, string label, Vector2 position, UnityEngine.Events.UnityAction onClick)
        {
            var rect = NewRect(label, parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            rect.sizeDelta = new Vector2(240f, 80f);
            rect.anchoredPosition = position;
            var bg = AddImage(rect.gameObject, k_Idle, UISprites.RoundedPanel, true, 10f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            var colors = button.colors;
            colors.highlightedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
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
            AddImage(track.gameObject, new Color(1f, 1f, 1f, 0.18f), UISprites.RoundedPanel, true, 4f);

            var fillArea = NewRect("Fill Area", area, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f));
            fillArea.sizeDelta = new Vector2(-22f, 8f);
            var fill = NewRect("Fill", fillArea, Vector2.zero, Vector2.one);
            fill.sizeDelta = new Vector2(22f, 0f);
            AddImage(fill.gameObject, new Color(1f, 1f, 1f, 0.8f), UISprites.RoundedPanel, false, 4f);

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
        GameObject AddSwitch(RectTransform parent, float y, string title, System.Func<bool> get, System.Action<bool> set,
            float x = 0f, float width = 500f)
        {
            var row = NewRect(title + " Row", parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            row.sizeDelta = new Vector2(width, 56f);
            row.anchoredPosition = new Vector2(x, y);
            var label = AddText(row, title, 26, k_Ink, FontStyle.Bold);
            label.alignment = TextAnchor.MiddleLeft;

            var track = NewRect("Switch", row, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f));
            track.sizeDelta = new Vector2(92f, 50f);
            track.anchoredPosition = new Vector2(-46f, 0f);
            var trackImage = AddImage(track.gameObject, k_Idle, UISprites.RoundedPanel, true, 25f);
            var knob = NewRect("Knob", track, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            knob.sizeDelta = new Vector2(40f, 40f);
            var knobImage = AddImage(knob.gameObject, Color.white, UISprites.Circle, false);

            void Show(bool on)
            {
                trackImage.color = on ? k_Accent : new Color(1f, 1f, 1f, 0.16f);
                knobImage.color = on ? k_OnAccent : Color.white;
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
