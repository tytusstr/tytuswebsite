using UnityEngine;
using UnityEngine.EventSystems;

namespace GlassRoom
{
    /// <summary>An on-screen button that acts the moment a finger touches it (not on release), for Jump.</summary>
    public class PressButton : MonoBehaviour, IPointerDownHandler
    {
        public System.Action pressed;

        public void OnPointerDown(PointerEventData eventData) => pressed?.Invoke();
    }
}
