using UnityEngine;
using UnityEngine.EventSystems;

namespace GlassRoom
{
    /// <summary>
    /// Floating on-screen joystick. Touching anywhere in its zone moves the base there;
    /// dragging moves the knob. Value is in the unit circle.
    /// </summary>
    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform baseRect;
        public RectTransform knob;
        public float radius = 110f;

        RectTransform m_Zone;
        Vector2 m_RestPosition;
        int m_PointerId = int.MinValue;
        Vector2 m_DownPosition;
        float m_DownTime;

        public Vector2 Value { get; private set; }

        /// <summary>A quick touch without dragging, at this screen position.</summary>
        public event System.Action<Vector2> Tapped;

        // Start, not Awake: the HUD assigns baseRect and knob right after AddComponent.
        void Start()
        {
            m_Zone = (RectTransform)transform;
            m_RestPosition = baseRect.anchoredPosition;
        }

        void OnDisable() => Release();

        public void OnPointerDown(PointerEventData eventData)
        {
            if (m_PointerId != int.MinValue)
                return;
            m_PointerId = eventData.pointerId;
            m_DownPosition = eventData.position;
            m_DownTime = Time.unscaledTime;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(m_Zone, eventData.position, eventData.pressEventCamera, out var local))
                baseRect.anchoredPosition = local - (Vector2)m_Zone.rect.min;
            SetAlpha(1f);
            UpdateKnob(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId == m_PointerId)
                UpdateKnob(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != m_PointerId)
                return;
            Release();
            if (TapDetector.IsTap(m_DownPosition, eventData.position, m_DownTime))
                Tapped?.Invoke(eventData.position);
        }

        void UpdateKnob(PointerEventData eventData)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(baseRect, eventData.position, eventData.pressEventCamera, out var local))
                return;
            Vector2 offset = Vector2.ClampMagnitude(local, radius);
            knob.anchoredPosition = offset;
            Value = offset / radius;
        }

        void Release()
        {
            m_PointerId = int.MinValue;
            Value = Vector2.zero;
            if (knob != null) knob.anchoredPosition = Vector2.zero;
            // Before Start has run, the rest position isn't known yet.
            if (baseRect != null && m_Zone != null) baseRect.anchoredPosition = m_RestPosition;
            SetAlpha(0.55f);
        }

        void SetAlpha(float a)
        {
            var group = baseRect != null ? baseRect.GetComponent<CanvasGroup>() : null;
            if (group != null) group.alpha = a;
        }
    }
}
