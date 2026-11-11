using UnityEngine;

namespace GlassRoom
{
    /// <summary>
    /// A stand-on electric scooter: a flat deck with a pole up to the handlebar. The player
    /// drives it (see FirstPersonController); this spins the wheels and plays the recorded
    /// sounds: a thud stepping on and off, and the motor, whose pitch and volume follow the speed.
    /// </summary>
    public class StandUpScooter : MonoBehaviour
    {
        [Header("Driving")]
        [Tooltip("Top speed in metres per second; a little faster than walking.")]
        public float maxSpeed = 6.5f;
        public float reverseSpeed = 1.6f;
        public float acceleration = 3.6f;
        public float brakeDeceleration = 7f;
        public float coastDeceleration = 2.5f;
        [Tooltip("Degrees per second at full steering. It can turn on the spot.")]
        public float turnRate = 90f;
        [Tooltip("How quickly turning builds up when you start steering (full turn after about 1 / this seconds).")]
        public float turnBuildUp = 2.2f;
        [Tooltip("How quickly turning eases off when you let go.")]
        public float turnEaseOff = 5f;
        [Tooltip("Radius of the space the scooter takes up when riding into things.")]
        public float footprintRadius = 0.42f;

        [Header("Rider")]
        [Tooltip("Height of the deck surface the rider stands on.")]
        public float deckHeight = 0.12f;
        [Tooltip("Where the rider stands, front to back, relative to the scooter's centre.")]
        public float riderOffsetZ = -0.1f;
        [Tooltip("How far the rider looks down while riding, in degrees.")]
        public float ridePitch = 22f;

        [Header("Parts")]
        [Tooltip("The box that stops you walking through the scooter. Clicks only count on the deck, pole and handlebar.")]
        public Collider body;
        public Transform[] wheels;
        public float wheelRadius = 0.05f;

        [Header("Sounds (recordings)")]
        [Tooltip("The electric motor hum: the riding sound.")]
        public AudioClip motorClip;
        [Range(0f, 1f)] public float humVolume = 0.25f;
        public AudioClip stepOnClip;
        public AudioClip stepOffClip;

        AudioSource m_Motor, m_Effects;
#if UNITY_WEBGL && !UNITY_EDITOR
        // In a web browser, 3D sound is recalculated every frame things move, which is heavy on
        // phones, so the scooter is heard plainly there.
        const float Spatial = 0f;
#else
        const float Spatial = 0.7f;
#endif
        float m_Speed;
        bool m_Ridden;

        void Awake()
        {
            m_Effects = NewSource("Step", null, false);
            m_Motor = NewSource("Motor", AtFullLevel(motorClip), true);
        }

        AudioSource NewSource(string name, AudioClip clip, bool loop)
        {
            var go = new GameObject(name + " Sound");
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = loop;
            source.playOnAwake = false;
            source.volume = 0f;
            // Mostly 3D, so it comes from the scooter when someone else is near it.
            source.spatialBlend = Spatial;
            source.minDistance = 2f;
            source.maxDistance = 25f;
            if (loop && clip != null) source.Play();
            return source;
        }

        public void SetRidden(bool ridden)
        {
            m_Ridden = ridden;
            // The rider hears it directly, at full level; others hear it from where it stands.
            m_Motor.spatialBlend = ridden ? 0f : Spatial;
            m_Effects.spatialBlend = ridden ? 0f : Spatial;
            // While riding, the player's capsule does the colliding; the scooter's own colliders
            // become triggers so they can still be clicked.
            foreach (var c in GetComponentsInChildren<Collider>()) c.isTrigger = ridden;
            var clip = ridden ? stepOnClip : stepOffClip;
            if (clip != null)
            {
                m_Effects.volume = 1f;
                m_Effects.pitch = Random.Range(0.92f, 1.05f);
                m_Effects.PlayOneShot(clip, 0.9f);
            }
        }

        // Rolls forward (or back) by this many metres, stopping at anything in the way.
        void Coast(float distance)
        {
            if (Mathf.Approximately(distance, 0f)) return;
            Vector3 direction = transform.forward * Mathf.Sign(distance);
            float length = Mathf.Abs(distance);
            if (body is BoxCollider box)
            {
                Vector3 centre = box.transform.TransformPoint(box.center);
                Vector3 half = Vector3.Scale(box.size, box.transform.lossyScale) * 0.5f;
                half.y *= 0.8f; // clear the floor
                foreach (var hit in Physics.BoxCastAll(centre, half, direction, transform.rotation, length + 0.02f,
                             Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    if (hit.transform.IsChildOf(transform)) continue;
                    m_Speed = 0f;
                    return;
                }
            }
            transform.position += direction * length;
        }

        /// <summary>Called every frame while riding with the signed speed in metres per second.</summary>
        public void UpdateMotion(float speed) => m_Speed = speed;

        void Update()
        {
            float dt = Time.deltaTime;
            // Stepped off while moving: it rolls on by itself, slowing down, until it stops.
            if (!m_Ridden && m_Speed != 0f)
            {
                m_Speed = Mathf.MoveTowards(m_Speed, 0f, coastDeceleration * dt);
                Coast(m_Speed * dt);
            }

            float degrees = m_Speed * dt / wheelRadius * Mathf.Rad2Deg;
            foreach (var wheel in wheels)
                if (wheel != null) wheel.Rotate(Vector3.right, degrees, Space.Self);

            float t = Mathf.Clamp01(Mathf.Abs(m_Speed) / maxSpeed);
            float moving = Mathf.SmoothStep(0f, 1f, Mathf.Abs(m_Speed) / 0.3f);
            float follow = 1f - Mathf.Exp(-12f * dt);
            // The electric motor, louder and higher the faster it goes.
            m_Motor.pitch = Mathf.Lerp(0.85f, 1.5f, t);
            m_Motor.volume = Mathf.Lerp(m_Motor.volume, moving * humVolume * Mathf.Lerp(0.6f, 1f, t), follow);
        }

        // Brings a recording up to full level (a plain volume change, since the recordings are quiet).
        static AudioClip AtFullLevel(AudioClip clip)
        {
            if (clip == null) return null;
            if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
            var data = new float[clip.samples * clip.channels];
            if (!clip.GetData(data, 0)) return clip;

            float peak = 0.0001f;
            foreach (float s in data) peak = Mathf.Max(peak, Mathf.Abs(s));
            float gain = 0.95f / peak;
            for (int i = 0; i < data.Length; i++) data[i] *= gain;

            var result = AudioClip.Create(clip.name + " (full level)", clip.samples, clip.channels, clip.frequency, false);
            result.SetData(data, 0);
            return result;
        }

        // Keeps only the steady middle of a recording (dropping its start and stop) and cross-fades
        // the end into the start so the repeat can't be heard. Only the recording's own sound.
        static AudioClip SteadyLoop(AudioClip clip)
        {
            if (clip == null) return null;
            int ch = clip.channels;
            var all = new float[clip.samples * ch];
            if (!clip.GetData(all, 0)) return clip;

            int start = Mathf.RoundToInt(clip.samples * 0.2f);
            int end = Mathf.RoundToInt(clip.samples * 0.85f);
            int fade = Mathf.Min(clip.frequency / 3, (end - start) / 4);
            int length = end - start - fade;
            var loop = new float[length * ch];
            for (int i = 0; i < length; i++)
            {
                float w = i < fade ? i / (float)fade : 1f;
                for (int c = 0; c < ch; c++)
                {
                    float body = all[(start + i) * ch + c];
                    float tail = i < fade ? all[(start + length + i) * ch + c] : 0f;
                    loop[i * ch + c] = body * w + tail * (1f - w);
                }
            }
            var result = AudioClip.Create(clip.name + " (loop)", length, ch, clip.frequency, false);
            result.SetData(loop, 0);
            return result;
        }
    }
}
