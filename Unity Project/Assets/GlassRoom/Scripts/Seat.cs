using UnityEngine;

namespace GlassRoom
{
    /// <summary>
    /// A chair you can sit in (see FirstPersonController): where you sit, which way you face,
    /// and the sound of sitting down (Audio/Seating).
    /// </summary>
    public class Seat : MonoBehaviour
    {
        [Tooltip("Height of the seat cushion above the floor, in metres.")]
        public float seatHeight = 0.42f;
        [Tooltip("Turn added to the chair's own forward to face out of the seat, in degrees.")]
        public float facingOffset = 0f;
        public AudioClip sitClip;
        [Range(0f, 1f)] public float sitVolume = 0.8f;

        AudioSource m_Source;

        /// <summary>The middle of the seat, on the floor.</summary>
        public Vector3 FloorPoint
        {
            get
            {
                var renderers = GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) return transform.position;
                var b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                return new Vector3(b.center.x, b.min.y, b.center.z);
            }
        }

        /// <summary>The direction someone sitting here faces, along the floor.</summary>
        public Vector3 Facing
        {
            get
            {
                Vector3 f = Quaternion.Euler(0f, facingOffset, 0f) * transform.forward;
                f.y = 0f;
                return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
            }
        }

        public void PlaySitSound()
        {
            if (sitClip == null) return;
            if (m_Source == null)
            {
                m_Source = gameObject.AddComponent<AudioSource>();
                m_Source.playOnAwake = false;
                m_Source.spatialBlend = 0f;
            }
            m_Source.pitch = Random.Range(0.95f, 1.05f);
            m_Source.PlayOneShot(sitClip, sitVolume);
        }
    }
}
