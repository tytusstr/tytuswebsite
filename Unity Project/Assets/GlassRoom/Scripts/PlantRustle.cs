using System.Collections.Generic;
using UnityEngine;

namespace GlassRoom
{
    /// <summary>
    /// Leaves rustle while you move through a plant: only when you're right over its pot
    /// (standing on it, among the leaves), not when you walk past beside it. Recorded rustles
    /// (Audio/Plants), a random one each time, quicker the faster you move.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlantRustle : MonoBehaviour
    {
        public AudioClip[] clips;
        [Range(0f, 1f)] public float volume = 0.7f;

        struct Plant
        {
            public Vector3 centre;   // middle of the pot, on the floor
            public float radius;     // how far from the middle still counts as over the pot
            public float top;        // top of the leaves
        }

        readonly List<Plant> m_Plants = new List<Plant>();
        CharacterController m_Controller;
        AudioSource m_Source;
        float m_NextRustle;
        int m_Last = -1;

        void Start()
        {
            m_Controller = GetComponent<CharacterController>();
            m_Source = gameObject.AddComponent<AudioSource>();
            SoundSettings.Tag(m_Source, SoundKind.Room);
            m_Source.playOnAwake = false;
            m_Source.spatialBlend = 0f;
            FindPlants();
        }

        // Every Poly Haven plant in the hall ("potted_plant_..."). Its colliders cover only the
        // pot, so they give the pot's size; its leaves give the height.
        void FindPlants()
        {
            foreach (var t in FindObjectsByType<Transform>())
            {
                if (!t.name.StartsWith("potted_plant")) continue;
                if (t.parent != null && t.parent.name.StartsWith("potted_plant")) continue;
                var renderers = t.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) continue;
                Bounds leaves = renderers[0].bounds;
                foreach (var r in renderers) leaves.Encapsulate(r.bounds);
                Bounds pot = leaves;
                var colliders = t.GetComponentsInChildren<Collider>();
                if (colliders.Length > 0)
                {
                    pot = colliders[0].bounds;
                    foreach (var c in colliders) pot.Encapsulate(c.bounds);
                }
                m_Plants.Add(new Plant
                {
                    centre = new Vector3(pot.center.x, leaves.min.y, pot.center.z),
                    radius = Mathf.Max(pot.extents.x, pot.extents.z) + 0.1f,
                    top = leaves.max.y
                });
            }
        }

        void Update()
        {
            if (clips == null || clips.Length == 0 || m_Controller == null) return;
            Vector3 velocity = m_Controller.velocity;
            float speed = new Vector2(velocity.x, velocity.z).magnitude;
            if (speed < 0.3f || !InPlant()) return;
            if (Time.time < m_NextRustle) return;

            int pick = Random.Range(0, clips.Length);
            if (clips.Length > 1 && pick == m_Last) pick = (pick + 1) % clips.Length;
            m_Last = pick;
            m_Source.pitch = Random.Range(0.92f, 1.08f);
            float loudness = volume * Mathf.Clamp01(0.5f + speed / 6f);
            m_Source.PlayOneShot(clips[pick], loudness);
            SharedSounds.Raise(SharedSound.Rustle, pick, transform.position + Vector3.up * 0.5f, loudness);
            // Faster movement, quicker rustles.
            m_NextRustle = Time.time + Mathf.Lerp(0.55f, 0.28f, Mathf.InverseLerp(1f, 6f, speed));
        }

        bool InPlant()
        {
            Vector3 p = transform.position;
            foreach (var plant in m_Plants)
            {
                float dx = p.x - plant.centre.x, dz = p.z - plant.centre.z;
                if (dx * dx + dz * dz <= plant.radius * plant.radius && p.y < plant.top && p.y > plant.centre.y - 0.5f)
                    return true;
            }
            return false;
        }
    }
}
