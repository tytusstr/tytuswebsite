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

        /// <summary>
        /// The direction someone sitting here faces, along the floor: away from the backrest.
        /// Worked out from the chair's shape (its tall part is the back), since imported chairs
        /// often have "forward" pointing straight up.
        /// </summary>
        public Vector3 Facing
        {
            get
            {
                Vector3 f = facing;
                if (f.sqrMagnitude < 0.0001f) { f = transform.forward; f.y = 0f; }
                if (f.sqrMagnitude < 0.0001f) f = Vector3.forward;
                return Quaternion.Euler(0f, facingOffset, 0f) * f.normalized;
            }
        }

        [Tooltip("Which way the chair faces along the floor (set by Tools > Glass Room > Apply Updates, from the chair's shape).")]
        public Vector3 facing;

        /// <summary>Editor: works out <see cref="facing"/> from the chair's shape.</summary>
        public void WorkOutFacing() => facing = FacingFromShape();

        Vector3 FacingFromShape()
        {
            Vector3 all = Vector3.zero, high = Vector3.zero;
            int na = 0, nh = 0;
            float top = FloorPoint.y + 0.85f;
            foreach (var mf in GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                foreach (var v in mesh.vertices)
                {
                    var w = mf.transform.TransformPoint(v);
                    all += w; na++;
                    if (w.y > top) { high += w; nh++; }
                }
            }
            if (na > 0 && nh > 0)
            {
                Vector3 away = all / na - high / nh;
                away.y = 0f;
                if (away.sqrMagnitude > 0.0004f) return away.normalized;
            }
            Vector3 f = transform.forward;
            f.y = 0f;
            return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
        }

        /// <summary>Where the sitter's hips go, on the floor (the middle of the seat).</summary>
        public Vector3 SitPoint => FloorPoint - Facing * 0.04f;

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
