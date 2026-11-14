using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GlassRoom
{
    /// <summary>
    /// A mouse pointer drawn by the game, for using the settings menu while the real mouse stays
    /// captured for looking around. Opening the menu with Q on a computer no longer frees the
    /// real pointer (which meant clicking back into the game afterwards). Moving the mouse moves
    /// this pointer, and clicking and dragging work on the menu's buttons, switches and sliders.
    /// It's used only while the menu is open and the real pointer is captured.
    /// </summary>
    public class FakeCursor : MonoBehaviour
    {
        // How far the pointer moves for a given mouse movement.
        const float Speed = 1.5f;
        RectTransform m_Canvas;
        RectTransform m_Pointer;
        BaseInputModule m_RealInput;
        bool m_Active;
        Vector2 m_Position;
        PointerEventData m_Event;
        GameObject m_Hovered, m_DragTarget;
        bool m_Dragging;
        readonly List<RaycastResult> m_Hits = new List<RaycastResult>();

        /// <summary>Creates the pointer on top of everything in this canvas.</summary>
        public void Build(RectTransform canvas)
        {
            m_Canvas = canvas;
            var go = new GameObject("Fake Cursor", typeof(RectTransform));
            m_Pointer = (RectTransform)go.transform;
            m_Pointer.SetParent(canvas, false);
            m_Pointer.anchorMin = m_Pointer.anchorMax = new Vector2(0.5f, 0.5f);
            var image = go.AddComponent<Image>();
            var rick = PointerPicture.Large;
            if (rick != null)
            {
                // A small picture of Rick Astley; it points from its centre.
                image.sprite = Sprite.Create(rick, new Rect(0, 0, rick.width, rick.height), new Vector2(0.5f, 0.5f), 100f);
                m_Pointer.pivot = new Vector2(0.5f, 0.5f);
                m_Pointer.sizeDelta = new Vector2(60f, 60f);
            }
            else
            {
                image.sprite = ArrowSprite();
                m_Pointer.pivot = new Vector2(0.06f, 0.96f); // the arrow's tip
                m_Pointer.sizeDelta = new Vector2(30f, 30f);
            }
            image.raycastTarget = false;
            go.SetActive(false);
        }

        /// <summary>Call every frame: shows and runs the pointer while it's wanted.</summary>
        public void Tick(bool wanted)
        {
            if (m_Pointer == null) return;
            if (wanted != m_Active) SetActive(wanted);
            if (m_Active) Run();
        }

        void SetActive(bool active)
        {
            m_Active = active;
            m_Pointer.gameObject.SetActive(active);
            m_Pointer.SetAsLastSibling();
            // The real pointer sits captured in the middle of the screen; its clicks must not
            // reach the menu while this one is in charge.
            if (EventSystem.current != null)
            {
                if (m_RealInput == null) m_RealInput = EventSystem.current.GetComponent<BaseInputModule>();
                if (m_RealInput != null) m_RealInput.enabled = !active;
            }
            if (active)
            {
                m_Position = new Vector2(Screen.width * 0.5f, Screen.height * 0.62f);
                m_Event = EventSystem.current != null ? new PointerEventData(EventSystem.current) : null;
            }
            else
                Release(null);
            SetHovered(null);
        }

        void Run()
        {
            var mouse = Mouse.current;
            if (mouse == null || m_Event == null || EventSystem.current == null) return;

            Vector2 delta = mouse.delta.ReadValue() * Speed;
            m_Position += delta;
            m_Position.x = Mathf.Clamp(m_Position.x, 0f, Screen.width);
            m_Position.y = Mathf.Clamp(m_Position.y, 0f, Screen.height);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(m_Canvas, m_Position, null, out var local))
                m_Pointer.anchoredPosition = local;

            m_Event.position = m_Position;
            m_Event.delta = delta;
            m_Event.button = PointerEventData.InputButton.Left;
            m_Hits.Clear();
            EventSystem.current.RaycastAll(m_Event, m_Hits);
            var hit = m_Hits.Count > 0 ? m_Hits[0] : default;
            m_Event.pointerCurrentRaycast = hit;
            GameObject target = hit.gameObject;
            SetHovered(target);

            if (mouse.leftButton.wasPressedThisFrame && target != null)
            {
                m_Event.pressPosition = m_Position;
                m_Event.pointerPressRaycast = hit;
                m_Event.eligibleForClick = true;
                m_Event.useDragThreshold = true;
                var pressed = ExecuteEvents.ExecuteHierarchy(target, m_Event, ExecuteEvents.pointerDownHandler);
                if (pressed == null) pressed = ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
                m_Event.pointerPress = pressed;
                m_Event.rawPointerPress = target;
                m_DragTarget = ExecuteEvents.GetEventHandler<IDragHandler>(target);
                if (m_DragTarget != null)
                    ExecuteEvents.Execute(m_DragTarget, m_Event, ExecuteEvents.initializePotentialDrag);
                m_Event.pointerDrag = m_DragTarget;
                m_Dragging = false;
            }
            else if (mouse.leftButton.isPressed && m_DragTarget != null && delta != Vector2.zero)
            {
                if (!m_Dragging)
                {
                    m_Dragging = true;
                    m_Event.dragging = true;
                    ExecuteEvents.Execute(m_DragTarget, m_Event, ExecuteEvents.beginDragHandler);
                }
                ExecuteEvents.Execute(m_DragTarget, m_Event, ExecuteEvents.dragHandler);
            }

            if (mouse.leftButton.wasReleasedThisFrame)
                Release(target);

            // A text box chosen with this pointer still gets the typing (the real pointer's
            // input handling, which normally passes it on, is switched off meanwhile).
            var selected = EventSystem.current.currentSelectedGameObject;
            if (selected != null)
                ExecuteEvents.Execute(selected, new BaseEventData(EventSystem.current), ExecuteEvents.updateSelectedHandler);
        }

        // Ends a press: lifts the button, and clicks it if the pointer is still on it.
        void Release(GameObject target)
        {
            if (m_Event == null) return;
            if (m_Event.pointerPress != null)
            {
                ExecuteEvents.Execute(m_Event.pointerPress, m_Event, ExecuteEvents.pointerUpHandler);
                var clickTarget = target != null ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(target) : null;
                if (clickTarget != null && clickTarget == m_Event.pointerPress && m_Event.eligibleForClick)
                    ExecuteEvents.Execute(clickTarget, m_Event, ExecuteEvents.pointerClickHandler);
            }
            if (m_Dragging && m_DragTarget != null)
                ExecuteEvents.Execute(m_DragTarget, m_Event, ExecuteEvents.endDragHandler);
            m_Event.pointerPress = null;
            m_Event.rawPointerPress = null;
            m_Event.pointerDrag = null;
            m_Event.eligibleForClick = false;
            m_Event.dragging = false;
            m_DragTarget = null;
            m_Dragging = false;
        }

        // Hover highlights, the same as with the real pointer.
        void SetHovered(GameObject target)
        {
            var handler = target != null ? ExecuteEvents.GetEventHandler<IPointerEnterHandler>(target) : null;
            if (handler == m_Hovered) return;
            if (m_Hovered != null && m_Event != null)
                ExecuteEvents.Execute(m_Hovered, m_Event, ExecuteEvents.pointerExitHandler);
            m_Hovered = handler;
            if (m_Hovered != null && m_Event != null)
                ExecuteEvents.Execute(m_Hovered, m_Event, ExecuteEvents.pointerEnterHandler);
        }

        void OnDisable()
        {
            if (m_RealInput != null) m_RealInput.enabled = true;
        }

        // A classic arrow pointer: white with a thin dark outline, smoothed edges.
        static Sprite s_Arrow;
        static Sprite ArrowSprite()
        {
            if (s_Arrow != null) return s_Arrow;
            const int size = 64;
            // Outline of the arrow, in 0..1 with y pointing down from the tip.
            Vector2[] shape =
            {
                new Vector2(0.06f, 0.04f), new Vector2(0.06f, 0.80f), new Vector2(0.24f, 0.63f),
                new Vector2(0.37f, 0.94f), new Vector2(0.49f, 0.89f), new Vector2(0.36f, 0.59f),
                new Vector2(0.60f, 0.59f)
            };
            const float outline = 0.045f;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // 4x4 samples per pixel for smooth edges.
                float white = 0f, dark = 0f;
                for (int sy = 0; sy < 4; sy++)
                for (int sx = 0; sx < 4; sx++)
                {
                    var p = new Vector2((x + (sx + 0.5f) / 4f) / size, 1f - (y + (sy + 0.5f) / 4f) / size);
                    bool inside = Inside(shape, p);
                    float edge = EdgeDistance(shape, p);
                    if (inside && edge > outline) white += 1f;
                    else if (inside || edge < outline) dark += 1f;
                }
                white /= 16f;
                dark /= 16f;
                float a = white + dark;
                float shade = a > 0f ? white / a : 0f;
                byte c = (byte)(Mathf.Lerp(0.12f, 1f, shade) * 255f);
                pixels[y * size + x] = new Color32(c, c, c, (byte)(Mathf.Clamp01(a) * 255f));
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            s_Arrow = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return s_Arrow;
        }

        static bool Inside(Vector2[] poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            return inside;
        }

        static float EdgeDistance(Vector2[] poly, Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[j], b = poly[i], ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best;
        }
    }
}
