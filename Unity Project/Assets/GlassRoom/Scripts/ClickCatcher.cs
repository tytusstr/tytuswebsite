using UnityEngine;
using UnityEngine.EventSystems;

namespace GlassRoom
{
    /// <summary>Takes clicks so they don't pass on to whatever the object sits inside.</summary>
    public class ClickCatcher : MonoBehaviour, IPointerClickHandler
    {
        public void OnPointerClick(PointerEventData eventData) { }
    }
}
