using UnityEngine;

namespace GlassRoom
{
    /// <summary>
    /// The sky slowly turns, so the clouds drift past the windows (about one full turn every
    /// fifteen minutes). Works on the sky picture itself, so it stays sharp.
    /// </summary>
    public class SkyDrift : MonoBehaviour
    {
        const float DegreesPerSecond = 0.4f;
        static readonly int k_Rotation = Shader.PropertyToID("_Rotation");
        Material m_Sky;
        float m_Start;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Begin()
        {
            if (RenderSettings.skybox == null || !RenderSettings.skybox.HasProperty(k_Rotation)) return;
            var go = new GameObject("Sky Drift");
            DontDestroyOnLoad(go);
            go.AddComponent<SkyDrift>();
        }

        void Start()
        {
            // A copy, so the saved material is never changed.
            m_Sky = new Material(RenderSettings.skybox);
            m_Start = m_Sky.GetFloat(k_Rotation);
            RenderSettings.skybox = m_Sky;
        }

        void Update()
        {
            if (m_Sky != null) m_Sky.SetFloat(k_Rotation, Mathf.Repeat(m_Start + Time.time * DegreesPerSecond, 360f));
        }
    }
}
