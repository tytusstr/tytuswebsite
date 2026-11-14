using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GlassRoom
{
    /// <summary>
    /// Writing or drawing on the marker board: the board, big, with what you're adding on top.
    /// Write: type your text, then drag its box to where it should go and drag the corner to size
    /// it (the words wrap onto new lines to fit; the box can't be stretched much past what the
    /// words need, and stays on the board); pick a colour; Write puts it up. Draw: pick a colour
    /// and a marker size and draw with the pointer or a finger; Draw puts it up. Erase wipes the
    /// board (only offered when there's something on it). You stand still meanwhile; on a
    /// computer the game's own pointer is used.
    /// </summary>
    public class BoardPanel : MonoBehaviour
    {
        public static BoardPanel Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance.m_Root != null && Instance.m_Root.activeSelf;

        static readonly Color k_Card = new Color(0.06f, 0.065f, 0.075f, 0.9f);
        static readonly Color k_Light = new Color(0.94f, 0.94f, 0.96f, 1f);
        static readonly Color k_Faint = new Color(1f, 1f, 1f, 0.09f);
        static readonly Color k_Dark = new Color(0.08f, 0.08f, 0.1f, 1f);
        const float BoardW = MarkerBoard.UnitsWide, BoardH = MarkerBoard.UnitsHigh;
        const float CardW = 1120f, CardH = 860f;

        GameObject m_Root;
        RectTransform m_Card, m_Board, m_Frame, m_Handle;
        RawImage m_Picture;
        Text m_Preview, m_ConfirmLabel, m_WriteTab, m_DrawTab;
        Image m_WriteTabBg, m_DrawTabBg;
        InputField m_Input;
        GameObject m_WriteControls, m_DrawControls, m_EraseButton, m_UndoButton, m_DrawSurface, m_ConfirmButton;
        Text m_FullNote;
        readonly List<Image> m_ColorDots = new List<Image>(), m_SizeDots = new List<Image>();
        readonly List<StrokeGraphic> m_Lines = new List<StrokeGraphic>();
        Font m_Font;
        bool m_Drawing;
        int m_Color, m_Size = 1;
        Rect m_Box = new Rect(300f, 170f, 400f, 160f);

        public static BoardPanel Create(RectTransform canvas, Font font)
        {
            var go = new GameObject("Board Panel", typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var panel = go.AddComponent<BoardPanel>();
            panel.m_Font = font;
            panel.Build(rect);
            return panel;
        }

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Build(RectTransform root)
        {
            m_Root = root.gameObject;
            var dim = root.gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.35f);

            m_Card = Rect(root, "Card", new Vector2(0.5f, 0.5f), new Vector2(CardW, CardH), Vector2.zero);
            Img(m_Card, k_Card, 12f);

            // Write | Draw
            var writeTab = Button(m_Card, "Write", new Vector2(-95f, CardH * 0.5f - 60f), new Vector2(180f, 60f), () => SetMode(false));
            m_WriteTabBg = writeTab.bg; m_WriteTab = writeTab.label;
            var drawTab = Button(m_Card, "Draw", new Vector2(95f, CardH * 0.5f - 60f), new Vector2(180f, 60f), () => SetMode(true));
            m_DrawTabBg = drawTab.bg; m_DrawTab = drawTab.label;

            // The board as it is, with what you're adding on top.
            m_Board = Rect(m_Card, "Board", new Vector2(0.5f, 0.5f), new Vector2(BoardW, BoardH), new Vector2(0f, 60f));
            m_Board.pivot = new Vector2(0.5f, 0.5f);
            m_Picture = m_Board.gameObject.AddComponent<RawImage>();
            var overlay = Rect(m_Board, "Overlay", Vector2.zero, new Vector2(BoardW, BoardH), Vector2.zero);
            overlay.pivot = Vector2.zero;

            // Drawing surface (draw mode).
            var surface = Rect(overlay, "Draw Surface", Vector2.zero, new Vector2(BoardW, BoardH), Vector2.zero);
            surface.pivot = Vector2.zero;
            var catcher = surface.gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            var pad = surface.gameObject.AddComponent<DrawPad>();
            pad.panel = this;
            m_DrawSurface = surface.gameObject;

            // The text box (write mode): drag it to move, drag its corner to size it.
            m_Frame = Rect(overlay, "Text Box", Vector2.zero, m_Box.size, m_Box.position);
            m_Frame.pivot = Vector2.zero;
            var frameImage = Img(m_Frame, new Color(0.25f, 0.55f, 1f, 0.12f), 6f);
            frameImage.raycastTarget = true;
            m_Frame.gameObject.AddComponent<BoxDrag>().panel = this;
            var outline = m_Frame.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.25f, 0.55f, 1f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);
            var preview = Rect(m_Frame, "Preview", Vector2.zero, Vector2.zero, Vector2.zero);
            preview.anchorMin = Vector2.zero; preview.anchorMax = Vector2.one;
            preview.offsetMin = preview.offsetMax = Vector2.zero;
            m_Preview = preview.gameObject.AddComponent<Text>();
            MarkerBoard.StyleText(m_Preview, m_Font);
            m_Handle = Rect(m_Frame, "Corner", new Vector2(1f, 0f), new Vector2(44f, 44f), Vector2.zero);
            m_Handle.pivot = new Vector2(0.5f, 0.5f);
            var handleImage = m_Handle.gameObject.AddComponent<Image>();
            handleImage.sprite = UISprites.Circle;
            handleImage.color = new Color(0.25f, 0.55f, 1f, 1f);
            m_Handle.gameObject.AddComponent<CornerDrag>().panel = this;

            // Controls under the board.
            float row = -BoardH * 0.5f + 60f - 70f;
            m_WriteControls = new GameObject("Write Controls", typeof(RectTransform));
            var wc = (RectTransform)m_WriteControls.transform;
            wc.SetParent(m_Card, false);
            wc.anchorMin = wc.anchorMax = new Vector2(0.5f, 0.5f);
            var box = Rect(wc, "Type Here", new Vector2(0.5f, 0.5f), new Vector2(620f, 64f), new Vector2(-200f, row));
            Img(box, k_Faint, 10f);
            var typed = Label(box, "", 26, k_Light);
            typed.alignment = TextAnchor.MiddleLeft;
            typed.rectTransform.offsetMin = new Vector2(16f, 0f);
            typed.rectTransform.offsetMax = new Vector2(-16f, 0f);
            typed.supportRichText = false;
            var hint = Label(box, "Type what to write...", 26, new Color(1f, 1f, 1f, 0.35f));
            hint.alignment = TextAnchor.MiddleLeft;
            hint.rectTransform.offsetMin = new Vector2(16f, 0f);
            m_Input = box.gameObject.AddComponent<InputField>();
            m_Input.textComponent = typed;
            m_Input.placeholder = hint;
            m_Input.characterLimit = 120;
            m_Input.lineType = InputField.LineType.SingleLine;
            m_Input.onValueChanged.AddListener(_ => FitBox());

            m_DrawControls = new GameObject("Draw Controls", typeof(RectTransform));
            var dc = (RectTransform)m_DrawControls.transform;
            dc.SetParent(m_Card, false);
            dc.anchorMin = dc.anchorMax = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < MarkerBoard.Sizes.Length; i++)
            {
                int index = i;
                var dot = Rect(dc, "Size " + i, new Vector2(0.5f, 0.5f), new Vector2(56f, 56f), new Vector2(-470f + i * 66f, row));
                var bg = dot.gameObject.AddComponent<Image>();
                bg.sprite = UISprites.Circle;
                bg.color = k_Faint;
                dot.gameObject.AddComponent<Button>().onClick.AddListener(() => SetSize(index));
                var inner = Rect(dot, "Tip", new Vector2(0.5f, 0.5f), Vector2.one * (8f + i * 9f), Vector2.zero);
                var tip = inner.gameObject.AddComponent<Image>();
                tip.sprite = UISprites.Circle;
                tip.color = k_Light;
                tip.raycastTarget = false;
                m_SizeDots.Add(bg);
            }
            m_UndoButton = Button(dc, "Undo", new Vector2(-180f, row), new Vector2(150f, 60f), Undo).bg.gameObject;

            // Colours (both modes).
            for (int i = 0; i < MarkerBoard.Colors.Length; i++)
            {
                int index = i;
                var dot = Rect(m_Card, "Colour " + i, new Vector2(0.5f, 0.5f), new Vector2(50f, 50f), new Vector2(170f + i * 62f, row));
                var bg = dot.gameObject.AddComponent<Image>();
                bg.sprite = UISprites.Circle;
                bg.color = MarkerBoard.Colors[i];
                dot.gameObject.AddComponent<Button>().onClick.AddListener(() => SetColor(index));
                var ring = dot.gameObject.AddComponent<Outline>();
                ring.effectDistance = new Vector2(3f, -3f);
                m_ColorDots.Add(bg);
            }

            // Bottom row: Erase (when there's something to erase), Cancel, and Write / Draw.
            float bottom = -CardH * 0.5f + 55f;
            m_EraseButton = Button(m_Card, "Erase board", new Vector2(-400f, bottom), new Vector2(240f, 66f), EraseBoard).bg.gameObject;
            Button(m_Card, "Cancel", new Vector2(220f, bottom), new Vector2(200f, 66f), Close);
            var confirm = Button(m_Card, "Write", new Vector2(430f, bottom), new Vector2(200f, 66f), Confirm);
            confirm.bg.color = k_Light;
            confirm.label.color = k_Dark;
            m_ConfirmLabel = confirm.label;
            m_ConfirmButton = confirm.bg.gameObject;
            m_FullNote = Label(Rect(m_Card, "Full Note", new Vector2(0.5f, 0.5f), new Vector2(900f, 44f), new Vector2(0f, bottom + 58f)),
                "The board is full (" + MarkerBoard.MaxEntries + "). Erase it to write or draw more.", 24, new Color(1f, 0.8f, 0.5f));

            SetColor(0);
            SetSize(1);
            m_Root.SetActive(false);
        }

        // ---- Opening and closing ----

        public void Open()
        {
            var board = MarkerBoard.Instance;
            if (board == null || MarkerBoard.LocalBusy) return;
            m_Picture.texture = board.Picture;
            m_Input.text = "";
            ClearLines();
            m_EraseButton.SetActive(board.HasContent);
            // Full: wipe it first.
            bool full = board.Full;
            m_FullNote.gameObject.SetActive(full);
            m_ConfirmButton.SetActive(!full);
            m_Root.SetActive(true);
            m_Root.transform.SetAsLastSibling();
            SetMode(false);
            FitBox();
            if (InputModeManager.Mode == ControlMode.KeyboardMouse) { m_Input.Select(); m_Input.ActivateInputField(); }
        }

        public void Close()
        {
            m_Root.SetActive(false);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        void Update()
        {
            if (!IsOpen) return;
            // Fit the card on small screens.
            var screen = ((RectTransform)transform).rect;
            float scale = Mathf.Min(1f, (screen.width - 20f) / CardW, (screen.height - 20f) / CardH);
            if (!Mathf.Approximately(m_Card.localScale.x, scale)) m_Card.localScale = Vector3.one * scale;
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Close();
        }

        void SetMode(bool drawing)
        {
            m_Drawing = drawing;
            m_WriteControls.SetActive(!drawing);
            m_DrawControls.SetActive(drawing);
            m_Frame.gameObject.SetActive(!drawing);
            m_DrawSurface.SetActive(drawing);
            m_ConfirmLabel.text = drawing ? "Draw" : "Write";
            m_WriteTabBg.color = drawing ? k_Faint : k_Light;
            m_WriteTab.color = drawing ? k_Light : k_Dark;
            m_DrawTabBg.color = drawing ? k_Light : k_Faint;
            m_DrawTab.color = drawing ? k_Dark : k_Light;
            m_UndoButton.SetActive(drawing);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        void SetColor(int index)
        {
            m_Color = index;
            for (int i = 0; i < m_ColorDots.Count; i++)
            {
                var ring = m_ColorDots[i].GetComponent<Outline>();
                ring.effectColor = i == index ? Color.white : new Color(1f, 1f, 1f, 0f);
                m_ColorDots[i].rectTransform.localScale = Vector3.one * (i == index ? 1.15f : 1f);
            }
            m_Preview.color = MarkerBoard.Colors[index];
            foreach (var line in m_Lines) if (line == m_Current) line.color = MarkerBoard.Colors[index];
        }

        void SetSize(int index)
        {
            m_Size = index;
            for (int i = 0; i < m_SizeDots.Count; i++) m_SizeDots[i].color = i == index ? new Color(1f, 1f, 1f, 0.3f) : k_Faint;
        }

        // ---- The text box ----

        // The box stays on the board, at least a little size, and no bigger than the words need
        // at the largest letters (so it can't be stretched far past them).
        void FitBox()
        {
            string s = string.IsNullOrEmpty(m_Input.text) ? "Text" : m_Input.text;
            m_Preview.text = m_Input.text;
            var settings = m_Preview.GetGenerationSettings(new Vector2(m_Box.width, m_Box.height));
            settings.resizeTextForBestFit = false;
            settings.fontSize = MarkerBoard.TextMaxSize;
            settings.horizontalOverflow = HorizontalWrapMode.Overflow;
            var gen = new TextGenerator();
            float maxW = Mathf.Clamp(gen.GetPreferredWidth(s, settings) / m_Preview.pixelsPerUnit + 16f, 80f, BoardW);
            float w = Mathf.Clamp(m_Box.width, 80f, maxW);
            settings.horizontalOverflow = HorizontalWrapMode.Wrap;
            settings.generationExtents = new Vector2(w, 10000f);
            float maxH = Mathf.Clamp(gen.GetPreferredHeight(s, settings) / m_Preview.pixelsPerUnit + 10f, 40f, BoardH);
            float h = Mathf.Clamp(m_Box.height, 40f, maxH);
            m_Box.size = new Vector2(w, h);
            m_Box.x = Mathf.Clamp(m_Box.x, 0f, BoardW - w);
            m_Box.y = Mathf.Clamp(m_Box.y, 0f, BoardH - h);
            m_Frame.anchoredPosition = m_Box.position;
            m_Frame.sizeDelta = m_Box.size;
        }

        internal void MoveBox(Vector2 by)
        {
            m_Box.position += by;
            FitBox();
        }

        internal void SizeBox(Vector2 by)
        {
            // The corner is bottom-right: dragging down makes the box taller (its bottom moves down).
            float top = m_Box.yMax;
            m_Box.width += by.x;
            m_Box.height -= by.y;
            m_Box.y = top - m_Box.height;
            FitBox();
            m_Box.y = Mathf.Clamp(top - m_Box.height, 0f, BoardH - m_Box.height);
            m_Frame.anchoredPosition = m_Box.position;
        }

        /// <summary>Converts a screen point to board units (in the big board on this panel).</summary>
        internal bool ToBoard(Vector2 screen, out Vector2 board)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(m_Board, screen, null, out var local)) { board = default; return false; }
            board = local + new Vector2(BoardW * 0.5f, BoardH * 0.5f);
            return board.x >= 0f && board.y >= 0f && board.x <= BoardW && board.y <= BoardH;
        }

        /// <summary>A pointer movement on screen, in board units.</summary>
        internal Vector2 ToBoardDelta(Vector2 screen, Vector2 screenDelta)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(m_Board, screen, null, out var now);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(m_Board, screen - screenDelta, null, out var before);
            return now - before;
        }

        // ---- Drawing ----

        StrokeGraphic m_Current;

        internal void BeginLine(Vector2 at)
        {
            var rect = Rect(m_DrawSurface.transform, "Line", Vector2.zero, new Vector2(BoardW, BoardH), Vector2.zero);
            rect.pivot = Vector2.zero;
            m_Current = rect.gameObject.AddComponent<StrokeGraphic>();
            m_Current.raycastTarget = false;
            m_Current.color = MarkerBoard.Colors[m_Color];
            m_Current.width = MarkerBoard.Sizes[m_Size];
            m_Current.points.Add(at);
            m_Current.Refresh();
            m_Lines.Add(m_Current);
        }

        internal void ExtendLine(Vector2 at)
        {
            if (m_Current == null) return;
            if (Vector2.Distance(m_Current.points[m_Current.points.Count - 1], at) < 4f) return;
            m_Current.points.Add(at);
            m_Current.Refresh();
        }

        internal void EndLine() => m_Current = null;

        void Undo()
        {
            if (m_Lines.Count == 0) return;
            var last = m_Lines[m_Lines.Count - 1];
            m_Lines.RemoveAt(m_Lines.Count - 1);
            Destroy(last.gameObject);
        }

        void ClearLines()
        {
            foreach (var line in m_Lines) if (line != null) Destroy(line.gameObject);
            m_Lines.Clear();
            m_Current = null;
        }

        // ---- Putting it up ----

        void Confirm()
        {
            var board = MarkerBoard.Instance;
            if (board == null) { Close(); return; }
            if (m_Drawing)
            {
                if (m_Lines.Count == 0) return;
                // Each line in the colour and size it was drawn with.
                var lines = new List<List<Vector2>>();
                var colors = new List<int>();
                var sizes = new List<int>();
                foreach (var line in m_Lines)
                {
                    lines.Add(new List<Vector2>(line.points));
                    colors.Add(Mathf.Max(0, System.Array.IndexOf(MarkerBoard.Colors, line.color)));
                    sizes.Add(Mathf.Max(0, System.Array.IndexOf(MarkerBoard.Sizes, line.width)));
                }
                board.Draw(lines, colors, sizes);
                ClearLines();
            }
            else
            {
                if (string.IsNullOrWhiteSpace(m_Input.text)) return;
                FitBox();
                board.Write(m_Input.text, m_Box, m_Color);
            }
            Close();
        }

        void EraseBoard()
        {
            var board = MarkerBoard.Instance;
            if (board != null) board.EraseAll();
            Close();
        }

        // ---- Building blocks ----

        RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 size, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        Image Img(RectTransform rect, Color color, float corner)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = UISprites.RoundedPanel;
            image.type = Image.Type.Sliced;
            if (corner > 0f && UISprites.RoundedPanel != null && UISprites.RoundedPanel.border.x > 0f)
                image.pixelsPerUnitMultiplier = UISprites.RoundedPanel.border.x / corner;
            image.color = color;
            return image;
        }

        Text Label(RectTransform parent, string value, int size, Color color)
        {
            var rect = Rect(parent, "Label", Vector2.zero, Vector2.zero, Vector2.zero);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var text = rect.gameObject.AddComponent<Text>();
            text.font = m_Font;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = value;
            text.raycastTarget = false;
            return text;
        }

        (Image bg, Text label) Button(RectTransform parent, string label, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction action)
        {
            var rect = Rect(parent, label, new Vector2(0.5f, 0.5f), size, position);
            var bg = Img(rect, k_Faint, 10f);
            var b = rect.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            b.onClick.AddListener(action);
            var text = Label(rect, label, 26, k_Light);
            text.fontStyle = FontStyle.Bold;
            return (bg, text);
        }

        // Dragging the text box around.
        class BoxDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IPointerDownHandler
        {
            public BoardPanel panel;
            public void OnPointerDown(PointerEventData e) { }
            public void OnBeginDrag(PointerEventData e) { }
            public void OnDrag(PointerEventData e) => panel.MoveBox(panel.ToBoardDelta(e.position, e.delta));
        }

        // Dragging its corner to size it.
        class CornerDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IPointerDownHandler
        {
            public BoardPanel panel;
            public void OnPointerDown(PointerEventData e) { }
            public void OnBeginDrag(PointerEventData e) { }
            public void OnDrag(PointerEventData e) => panel.SizeBox(panel.ToBoardDelta(e.position, e.delta));
        }

        // Drawing with the pointer or a finger.
        class DrawPad : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IPointerUpHandler
        {
            public BoardPanel panel;
            public void OnPointerDown(PointerEventData e) { if (panel.ToBoard(e.position, out var p)) panel.BeginLine(p); }
            public void OnBeginDrag(PointerEventData e) { }
            public void OnDrag(PointerEventData e) { if (panel.ToBoard(e.position, out var p)) panel.ExtendLine(p); }
            public void OnPointerUp(PointerEventData e) => panel.EndLine();
        }
    }
}
