using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GlassRoom
{
    /// <summary>
    /// Other players' names, floating just above their heads: white text with a soft dark
    /// outline, smaller further away, hidden behind walls, beyond a distance, and when the
    /// "Names" switch in the settings is off. Drawn on the screen, under the menu and buttons.
    /// </summary>
    public class NameTags : MonoBehaviour
    {
        const float AboveHead = 0.32f, MaxDistance = 30f;

        readonly Dictionary<NetworkPlayer, Text> m_Tags = new Dictionary<NetworkPlayer, Text>();
        readonly List<NetworkPlayer> m_Gone = new List<NetworkPlayer>();
        RectTransform m_Root;
        Canvas m_Canvas;
        Font m_Font;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (FindAnyObjectByType<Multiplayer>() == null) return;
            new GameObject("Name Tags").AddComponent<NameTags>();
        }

        void Awake()
        {
            m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            m_Canvas = gameObject.AddComponent<Canvas>();
            m_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            m_Canvas.sortingOrder = 5; // under the controls and menu (10)
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            m_Root = (RectTransform)transform;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            bool show = NameSettings.Enabled && cam != null && !WebPage.Covered;

            foreach (var p in Multiplayer.Players)
            {
                if (p == null || p.IsLocal || p.Object == null || !p.Object.IsValid) continue;
                if (!m_Tags.TryGetValue(p, out var tag)) m_Tags[p] = tag = NewTag();
                var avatar = p.Avatar;
                bool visible = show && avatar != null && Place(tag, avatar, cam);
                if (visible) tag.text = p.PlayerName.ToString();
                if (tag.gameObject.activeSelf != visible) tag.gameObject.SetActive(visible);
            }

            // People who've left.
            m_Gone.Clear();
            foreach (var pair in m_Tags)
                if (pair.Key == null || !Multiplayer.Players.Contains(pair.Key)) m_Gone.Add(pair.Key);
            foreach (var p in m_Gone)
            {
                if (m_Tags[p] != null) Destroy(m_Tags[p].gameObject);
                m_Tags.Remove(p);
            }
        }

        // Puts the tag over their head on screen; false if they're behind you, too far, or out of sight.
        bool Place(Text tag, PlayerAvatar avatar, Camera cam)
        {
            Vector3 head = (avatar.Head != null ? avatar.Head.position : avatar.transform.position + Vector3.up * 1.7f) + Vector3.up * AboveHead;
            Vector3 eye = cam.transform.position;
            float distance = Vector3.Distance(eye, head);
            if (distance > MaxDistance) return false;
            Vector3 screen = cam.WorldToScreenPoint(head);
            if (screen.z <= 0.05f) return false;
            // Hidden behind walls and other things (their own body doesn't count).
            if (Physics.Linecast(eye, head, out var hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && !hit.transform.IsChildOf(avatar.transform)) return false;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(m_Root, screen, null, out var local);
            var rect = tag.rectTransform;
            rect.anchoredPosition = local;
            // Readable up close, smaller further away.
            float scale = Mathf.Clamp(4f / Mathf.Max(distance, 0.5f), 0.45f, 1.1f);
            rect.localScale = Vector3.one * scale;
            var color = tag.color;
            color.a = Mathf.Clamp01((MaxDistance - distance) / 6f);
            tag.color = color;
            return true;
        }

        Text NewTag()
        {
            var go = new GameObject("Name", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(m_Root, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(600f, 60f);
            var text = go.AddComponent<Text>();
            text.font = m_Font;
            text.fontSize = 34;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.LowerCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.6f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            go.SetActive(false);
            return text;
        }
    }
}
