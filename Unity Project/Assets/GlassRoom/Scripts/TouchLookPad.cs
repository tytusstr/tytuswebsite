using UnityEngine;
using UnityEngine.EventSystems;

namespace GlassRoom
{
    /// <summary>Screen area that turns finger drags into look movement.</summary>
    public class TouchLookPad : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        int m_PointerId = int.MinValue;
        Vector2 m_Delta;
        Vector2 m_DownPosition;
        float m_DownTime;

        /// <summary>A quick touch without dragging, at this screen position.</summary>
        public event System.Action<Vector2> Tapped;

        /// <summary>Returns the drag accumulated since the last call, in screen pixels.</summary>
        public Vector2 ConsumeDelta()
        {
            Vector2 d = m_Delta;
            m_Delta = Vector2.zero;
            return d;
        }

        void OnDisable()
        {
            m_PointerId = int.MinValue;
            m_Delta = Vector2.zero;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (m_PointerId != int.MinValue)
                return;
            m_PointerId = eventData.pointerId;
            m_DownPosition = eventData.position;
            m_DownTime = Time.unscaledTime;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.pointerId == m_PointerId)
                m_Delta += eventData.delta;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != m_PointerId)
                return;
            m_PointerId = int.MinValue;
            if (TapDetector.IsTap(m_DownPosition, eventData.position, m_DownTime))
                Tapped?.Invoke(eventData.position);
        }
    }
}
