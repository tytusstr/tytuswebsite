using UnityEngine;
using UnityEngine.UI;

namespace GlassRoom
{
    /// <summary>
    /// A button on the wall of the cozy corner that opens Among Us (a TurboWarp project) full
    /// screen over the site. While it's open the room's sound is paused; the page's ✕ closes it
    /// and calls Closed.
    /// </summary>
    public class GameButton : MonoBehaviour
    {
        public const string Url = "https://turbowarp.org/523967150/embed?autoplay&hqpen&addons=remove-curved-stage-border";
        public const string Title = "Among Us";

        /// <summary>True while the game is open over the room.</summary>
        public static bool IsOpen { get; private set; }

        Transform m_Cap;
        float m_PressedAt = -10f;
        Vector3 m_CapRest;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Place()
        {
            var nook = GameObject.Find("Cozy Corner");
            var rug = nook != null ? nook.transform.Find("Rug") : null;
            if (rug == null) return;
            // On the wall behind the rug (the -Z wall of the corner), at about chest height.
            var from = new Vector3(rug.position.x, 1.25f, rug.position.z);
            Vector3 at = default, normal = default; bool found = false;
            float best = float.MaxValue;
            foreach (var h in Physics.RaycastAll(from, Vector3.back, 10f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.rigidbody != null && !h.rigidbody.isKinematic) continue;
                if (Mathf.Abs(h.normal.y) > 0.3f || h.distance >= best) continue;
                best = h.distance; at = h.point; normal = h.normal; found = true;
            }
            if (!found) return;
            normal.y = 0f; normal.Normalize();
            var go = new GameObject("Game Button");
            go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(normal));
            var button = go.AddComponent<GameButton>();
            button.Build();
            foreach (var player in FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None)) player.AddInteractable(button);
        }

        void Build()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            Material Mat(Color c, float smooth, Color emission)
            {
                var m = new Material(lit);
                m.SetColor("_BaseColor", c);
                m.SetFloat("_Smoothness", smooth);
                if (emission.maxColorComponent > 0f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission); }
                return m;
            }
            // A dark plate on the wall, a big red push button on it, and the game's name above.
            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "Plate";
            plate.transform.SetParent(transform, false);
            plate.transform.localPosition = new Vector3(0f, 0f, 0.015f);
            plate.transform.localScale = new Vector3(0.42f, 0.42f, 0.03f);
            plate.GetComponent<Renderer>().sharedMaterial = Mat(new Color(0.07f, 0.07f, 0.08f), 0.6f, Color.black);

            var cap = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cap.name = "Button";
            Destroy(cap.GetComponent<Collider>());
            cap.transform.SetParent(transform, false);
            cap.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            cap.transform.localPosition = new Vector3(0f, -0.03f, 0.045f);
            cap.transform.localScale = new Vector3(0.2f, 0.02f, 0.2f);
            cap.GetComponent<Renderer>().sharedMaterial = Mat(new Color(0.78f, 0.06f, 0.05f), 0.75f, new Color(0.35f, 0.02f, 0.01f));
            m_Cap = cap.transform;
            m_CapRest = m_Cap.localPosition;

            var canvasGo = new GameObject("Label", typeof(Canvas));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.transform.localPosition = new Vector3(0f, 0.15f, 0.031f);
            canvasGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = canvasGo.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(400f, 80f);
            canvasGo.transform.localScale = Vector3.one * 0.001f;
            var text = new GameObject("Text", typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(canvasGo.transform, false);
            var tr = text.rectTransform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 56;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = Title.ToUpperInvariant();
        }

        /// <summary>Opens the game full screen.</summary>
        public void Press()
        {
            if (IsOpen) return;
            m_PressedAt = Time.unscaledTime;
            IsOpen = true;
            AudioListener.pause = true;
            WebPage.OpenGame(Url);
        }

        /// <summary>The page closed the game (its ✕).</summary>
        public void Closed()
        {
            IsOpen = false;
            AudioListener.pause = false;
        }

        void Update()
        {
            if (m_Cap == null) return;
            float t = Time.unscaledTime - m_PressedAt;
            float pushed = t < 0.25f ? Mathf.Sin(Mathf.Clamp01(t / 0.25f) * Mathf.PI) : 0f;
            m_Cap.localPosition = m_CapRest - Vector3.forward * (0.012f * pushed);
        }
    }
}
