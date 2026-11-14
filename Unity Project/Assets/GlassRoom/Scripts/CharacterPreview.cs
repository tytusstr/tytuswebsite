using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GlassRoom
{
    /// <summary>
    /// The person you are, shown in the settings: they stand in a small lit studio far below the
    /// hall, filmed by their own camera into a picture on the settings card. Click (or touch) and
    /// drag the picture to turn them around. Drawn only while the settings are open.
    /// </summary>
    public class CharacterPreview : MonoBehaviour, IPointerDownHandler, IDragHandler, IBeginDragHandler
    {
        static readonly Vector3 k_Stage = new Vector3(0f, -400f, 0f);
        const string LayerName = "Preview";

        RawImage m_Image;
        RenderTexture m_Texture;
        Camera m_Camera;
        Light m_Key, m_Fill;
        PlayerAvatar m_Avatar;
        int m_Shown = -1;
        float m_Yaw = 20f;
        int m_Layer;

        /// <summary>Builds the picture inside this UI rectangle.</summary>
        public static CharacterPreview Create(RectTransform area)
        {
            var preview = area.gameObject.AddComponent<CharacterPreview>();
            preview.m_Image = area.gameObject.AddComponent<RawImage>();
            preview.m_Image.color = Color.white;
            return preview;
        }

        void Start()
        {
            m_Layer = LayerMask.NameToLayer(LayerName);
            if (m_Layer < 0) m_Layer = 31;
            m_Texture = new RenderTexture(512, 768, 24, RenderTextureFormat.ARGB32) { name = "Character Preview" };
            m_Image.texture = m_Texture;

            var rig = new GameObject("Character Preview Stage");
            rig.transform.position = k_Stage;
            m_Camera = new GameObject("Preview Camera").AddComponent<Camera>();
            m_Camera.transform.SetParent(rig.transform, false);
            m_Camera.transform.localPosition = new Vector3(0f, 1.0f, 3.4f);
            m_Camera.transform.LookAt(k_Stage + new Vector3(0f, 0.92f, 0f));
            m_Camera.fieldOfView = 32f;
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            m_Camera.cullingMask = 1 << m_Layer;
            m_Camera.targetTexture = m_Texture;
            m_Camera.enabled = false;

            // Only the studio's own lights reach the person here: the hall's lights (the sun and
            // its shadows above all) leave this layer alone.
            foreach (var light in FindObjectsByType<Light>(FindObjectsInactive.Include))
                light.cullingMask &= ~(1 << m_Layer);
            m_Key = NewLight(rig.transform, new Vector3(1.6f, 2.4f, 2.6f), 6f);
            m_Fill = NewLight(rig.transform, new Vector3(-2.2f, 1.4f, 1.4f), 2.6f);
        }

        Light NewLight(Transform parent, Vector3 at, float intensity)
        {
            var light = new GameObject("Preview Light").AddComponent<Light>();
            light.transform.SetParent(parent, false);
            light.transform.localPosition = at;
            light.type = LightType.Point;
            light.range = 8f;
            light.intensity = intensity;
            light.color = new Color(1f, 0.97f, 0.93f);
            light.cullingMask = 1 << m_Layer;
            light.shadows = LightShadows.None;
            light.enabled = false;
            return light;
        }

        void LateUpdate()
        {
            bool showing = m_Image != null && m_Image.isActiveAndEnabled;
            if (m_Camera == null) return;
            m_Camera.enabled = showing;
            m_Key.enabled = m_Fill.enabled = showing;
            if (!showing) return;

            var body = LocalPlayerBody.Instance;
            int index = body != null ? body.AvatarIndex : 0;
            if (index != m_Shown || m_Avatar == null)
            {
                if (m_Avatar != null) Destroy(m_Avatar.gameObject);
                var library = PlayerAvatarLibrary.Instance;
                if (library == null) return;
                m_Avatar = library.Spawn(index, null);
                m_Avatar.name = "Preview Person";
                m_Avatar.SetLayer(m_Layer);
                // Down here there's none of the hall's stored lighting; only the studio's lights.
                foreach (var r in m_Avatar.GetComponentsInChildren<Renderer>())
                {
                    r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    // (The hall's sun shadows don't belong down here: they darkened the top half.)
                    r.receiveShadows = false;
                }
                m_Shown = index;
            }
            m_Avatar.Move(Vector2.zero);
            m_Avatar.Pose(k_Stage, m_Yaw, 0f, false, Time.unscaledDeltaTime);
        }

        public void OnPointerDown(PointerEventData eventData) { }
        public void OnBeginDrag(PointerEventData eventData) { }

        // Dragging sideways turns them.
        public void OnDrag(PointerEventData eventData) => m_Yaw -= eventData.delta.x * 0.5f;

        void OnDestroy()
        {
            if (m_Avatar != null) Destroy(m_Avatar.gameObject);
            if (m_Texture != null) m_Texture.Release();
        }
    }
}
