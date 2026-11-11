using UnityEngine;

namespace GlassRoom
{
    /// <summary>
    /// A failing light: mostly on with a faint unsteady hum, but every few seconds it
    /// stutters, dips or cuts out for a moment. For the crime scene corner.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class FlickerLight : MonoBehaviour
    {
        public float intensity = 3.2f;
        Light m_Light;
        float m_NextBurst;
        float m_BurstEnd;

        void Awake()
        {
            m_Light = GetComponent<Light>();
            m_NextBurst = Time.time + Random.Range(1f, 3f);
        }

        void Update()
        {
            float t = Time.time;
            // A slight, constant unsteadiness.
            float level = 0.9f + 0.1f * Mathf.PerlinNoise(t * 7f, 0.3f);
            if (t >= m_NextBurst)
            {
                m_BurstEnd = t + Random.Range(0.15f, 0.9f);
                m_NextBurst = m_BurstEnd + Random.Range(1.5f, 6f);
            }
            if (t < m_BurstEnd)
            {
                // Stuttering: snaps between off, dim and full.
                float r = Mathf.PerlinNoise(t * 45f, 1.7f);
                level = r < 0.35f ? 0f : r < 0.6f ? 0.25f : 1f;
            }
            m_Light.intensity = intensity * level;
        }
    }
}
