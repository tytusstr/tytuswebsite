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
        public float maxSpeed = 7.8f;
        public float reverseSpeed = 1.6f;
        public float acceleration = 4.3f;
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
        [Range(0f, 1f)] public float humVolume = 0.125f;
        public AudioClip stepOnClip;
        public AudioClip stepOffClip;
        [Tooltip("Running into something.")]
        public AudioClip crashClip;
        [Tooltip("Running into something at 80% of top speed or more.")]
        public AudioClip crashHardClip;

        /// <summary>Losing more speed than this (m/s) all at once is a crash.</summary>
        public const float CrashSpeed = 2.5f;
        float m_LastCrash = -10f;

        /// <summary>A crash: a bump, louder the harder it was (others hear it too).</summary>
        public void Crash(float speedLost, bool share = true)
        {
            if (crashClip == null || Time.time - m_LastCrash < 0.5f) return;
            m_LastCrash = Time.time;
            bool hard = speedLost >= maxSpeed * 0.8f && crashHardClip != null;
            float loudness = Mathf.Lerp(0.45f, 1f, Mathf.InverseLerp(CrashSpeed, maxSpeed, speedLost));
            m_Effects.volume = 1f;
            m_Effects.pitch = Random.Range(0.92f, 1.06f);
            m_Effects.PlayOneShot(hard ? crashHardClip : crashClip, loudness);
            if (share) SharedSounds.Raise(hard ? SharedSound.ScooterCrashHard : SharedSound.ScooterCrash, Multiplayer.ScooterIndex(this), transform.position, loudness);
        }

        /// <summary>
        /// Pushed by another scooter: moved over, and set rolling at this speed. False (and not
        /// moved) if a wall or anything else solid is in the way.
        /// </summary>
        public bool Shove(Vector3 by, float speed)
        {
            by.y = 0f;
            if (Blocked(by)) { m_Speed = 0f; return false; }
            transform.position += by;
            Vector3 dir = by.sqrMagnitude > 0.000001f ? by.normalized : transform.forward;
            float along = Vector3.Dot(dir, transform.forward) * speed;
            if (Mathf.Abs(along) > Mathf.Abs(m_Speed)) m_Speed = along;
            return true;
        }

        // Anything solid (not people, not this scooter) within this move?
        bool Blocked(Vector3 by)
        {
            float length = by.magnitude;
            if (length < 0.0001f || !(body is BoxCollider box)) return false;
            Vector3 centre = box.transform.TransformPoint(box.center);
            Vector3 half = Vector3.Scale(box.size, box.transform.lossyScale) * 0.5f;
            half.y *= 0.6f;
            foreach (var hit in Physics.BoxCastAll(centre, half, by / length, transform.rotation, length + 0.03f,
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(transform)) continue;
                if (hit.collider is CharacterController || hit.transform.GetComponentInParent<PlayerAvatar>() != null) continue;
                if (hit.transform.GetComponentInParent<StandUpScooter>() != null) continue;
                return true;
            }
            return false;
        }

        AudioSource m_Motor, m_Effects;

        [Header("Headlight")]
        [Tooltip("Brightness of the headlight when fully on.")]
        public float headlightIntensity = 10f;
        [Tooltip("Seconds to fade on after stepping on, and off after stepping off.")]
        public float headlightFade = 1.2f;
        Light m_Headlight;
        Renderer m_HeadlightStrip;
        Color m_StripGlow;
        float m_HeadlightLevel;
        MaterialPropertyBlock m_StripBlock;
        static readonly int k_EmissionId = Shader.PropertyToID("_EmissionColor");
#if UNITY_WEBGL && !UNITY_EDITOR
        // In a web browser, 3D sound is recalculated every frame things move, which is heavy on
        // phones, so the scooter is heard plainly there.
        const float Spatial = 0f;
#else
        const float Spatial = 0.7f;
#endif
        float m_Speed;
        bool m_Ridden;

        Vector3 m_Home;
        Quaternion m_HomeTurn;
        float m_LastRemote;

        void Awake()
        {
            m_Home = transform.position;
            m_HomeTurn = transform.rotation;
            BuildHeadlight();
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
                half.y *= 0.6f; // clear the floor
                foreach (var hit in Physics.BoxCastAll(centre, half, direction, transform.rotation, length + 0.02f,
                             Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    if (hit.transform.IsChildOf(transform)) continue;
                    // People aren't walls: a rolling scooter pushes them (they move themselves).
                    if (hit.collider is CharacterController || hit.transform.GetComponentInParent<PlayerAvatar>() != null) continue;
                    if (Mathf.Abs(m_Speed) > CrashSpeed) Crash(Mathf.Abs(m_Speed));
                    m_Speed = 0f;
                    return;
                }
            }
            transform.position += direction * length;
        }

        // The front strip lights the way: a row of lights along the whole strip, a few
        // millimetres in front of it (never inside the scooter), shining ahead and slightly down.
        Light[] m_Headlights;
        const int HeadlightCount = 3;

        void BuildHeadlight()
        {
            var strip = transform.Find("Front Light");
            Vector3 local = strip != null ? strip.localPosition : new Vector3(0f, 0.09f, 0.321f);
            float width = strip != null ? strip.localScale.x : 0.36f;
            float front = local.z + (strip != null ? strip.localScale.z * 0.5f : 0.002f) + 0.004f;
            m_Headlights = new Light[HeadlightCount];
            for (int i = 0; i < HeadlightCount; i++)
            {
                var go = new GameObject("Headlight " + (i + 1));
                go.transform.SetParent(transform, false);
                float x = (i - (HeadlightCount - 1) * 0.5f) * (width * 0.8f / (HeadlightCount - 1));
                go.transform.localPosition = new Vector3(x, local.y, front);
                go.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);
                var light = go.AddComponent<Light>();
                light.type = LightType.Spot;
                light.color = new Color(0.92f, 0.96f, 1f);
                light.spotAngle = 80f;
                light.innerSpotAngle = 40f;
                light.range = 13f;
                light.intensity = 0f;
                light.shadows = LightShadows.None;
                light.enabled = false;
                m_Headlights[i] = light;
            }
            m_Headlight = m_Headlights[HeadlightCount / 2];
            if (strip != null)
            {
                m_HeadlightStrip = strip.GetComponent<Renderer>();
                if (m_HeadlightStrip != null && m_HeadlightStrip.sharedMaterial != null && m_HeadlightStrip.sharedMaterial.HasProperty(k_EmissionId))
                    m_StripGlow = m_HeadlightStrip.sharedMaterial.GetColor(k_EmissionId);
                m_StripBlock = new MaterialPropertyBlock();
            }
            BuildRearLight();
            ShowHeadlight();
        }

        // A dim red strip across the back of the deck, lit while someone rides.
        Renderer m_RearStrip;
        Light m_RearGlow;
        static readonly Color k_Red = new Color(1f, 0.08f, 0.05f);

        void BuildRearLight()
        {
            // Flat against the back face of the deck, the same size as the front strip.
            var front = transform.Find("Front Light");
            var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            strip.name = "Rear Light";
            Destroy(strip.GetComponent<Collider>());
            strip.transform.SetParent(transform, false);
            float back = front != null ? -front.localPosition.z : -0.321f;
            strip.transform.localPosition = new Vector3(0f, front != null ? front.localPosition.y : 0.09f, back);
            strip.transform.localScale = front != null ? front.localScale : new Vector3(0.36f, 0.012f, 0.004f);
            m_RearStrip = strip.GetComponent<Renderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(0.22f, 0.03f, 0.03f));
            mat.SetFloat("_Smoothness", 0.3f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Color.black);
            m_RearStrip.material = mat;
            m_RearStrip.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var glow = new GameObject("Rear Glow");
            glow.transform.SetParent(transform, false);
            glow.transform.localPosition = new Vector3(0f, strip.transform.localPosition.y, back - 0.03f);
            m_RearGlow = glow.AddComponent<Light>();
            m_RearGlow.type = LightType.Point;
            m_RearGlow.color = k_Red;
            m_RearGlow.range = 0.6f;
            m_RearGlow.intensity = 0f;
            m_RearGlow.shadows = LightShadows.None;
            m_RearGlow.enabled = false;
        }

        // Fades the headlight towards on while ridden, off otherwise.
        void UpdateHeadlight(float dt)
        {
            float target = m_Ridden ? 1f : 0f;
            if (Mathf.Approximately(m_HeadlightLevel, target)) return;
            m_HeadlightLevel = Mathf.MoveTowards(m_HeadlightLevel, target, dt / Mathf.Max(0.01f, headlightFade));
            ShowHeadlight();
        }

        void ShowHeadlight()
        {
            // Eased, so it glows up softly rather than switching.
            float level = Mathf.SmoothStep(0f, 1f, m_HeadlightLevel);
            foreach (var light in m_Headlights)
            {
                light.intensity = headlightIntensity / HeadlightCount * level;
                light.enabled = level > 0.001f;
            }
            if (m_RearStrip != null)
            {
                m_RearStrip.material.SetColor("_EmissionColor", k_Red * (0.55f * level));
                m_RearGlow.intensity = 0.12f * level;
                m_RearGlow.enabled = level > 0.001f;
            }
            if (m_HeadlightStrip != null)
            {
                m_HeadlightStrip.GetPropertyBlock(m_StripBlock);
                m_StripBlock.SetColor(k_EmissionId, m_StripGlow * Mathf.Lerp(0.08f, 1f, level));
                m_HeadlightStrip.SetPropertyBlock(m_StripBlock);
            }
        }

        // ---- Ridden by another player (see NetworkPlayer) ----
        bool m_RemoteControlled;
        Vector3 m_RemotePosition;
        float m_RemoteYaw;

        /// <summary>
        /// Moves the scooter where another player has it (riding it or after they've let it roll
        /// to a stop). It glides to each new spot, so it moves smoothly.
        /// </summary>
        public void FollowRemote(Vector3 position, float yaw, float speed, bool ridden)
        {
            if (!m_RemoteControlled) { transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f)); }
            m_RemoteControlled = true;
            m_RemotePosition = position;
            m_RemoteYaw = yaw;
            m_Speed = speed;
            m_Ridden = ridden; // headlight on while someone rides it
            m_LastRemote = Time.time;
        }

        Transform m_LeftGrip, m_RightGrip;

        /// <summary>The middle of a handlebar grip: side -1 left, +1 right.</summary>
        public Vector3 GripPoint(int side)
        {
            if (m_LeftGrip == null || m_RightGrip == null)
            {
                foreach (var t in GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != "Grip") continue;
                    if (transform.InverseTransformPoint(t.position).x < 0f) m_LeftGrip = t; else m_RightGrip = t;
                }
            }
            var grip = side < 0 ? m_LeftGrip : m_RightGrip;
            Vector3 p = grip != null ? grip.position : transform.TransformPoint(new Vector3(0.193f * side, 1.031f, 0.122f));
            return p;
        }

        /// <summary>This player has the scooter again (they've stepped on it).</summary>
        public void TakeBackControl() => m_RemoteControlled = false;

        public bool RemoteControlled => m_RemoteControlled;
        public float Speed => m_Speed;

        /// <summary>Called every frame while riding with the signed speed in metres per second.</summary>
        public void UpdateMotion(float speed) => m_Speed = speed;

        void Update()
        {
            float dt = Time.deltaTime;
            // Its rider has gone (left the game): lights off.
            if (m_RemoteControlled && m_Ridden && Time.time - m_LastRemote > 1.5f) m_Ridden = false;
            // Somehow fallen out of the building: back where it started.
            if (transform.position.y < -10f)
            {
                transform.SetPositionAndRotation(m_Home, m_HomeTurn);
                m_Speed = 0f;
                m_RemoteControlled = false;
            }
            UpdateHeadlight(dt);
            if (m_RemoteControlled)
            {
                // Another player's scooter: follow them smoothly. (Their tyre tracks arrive
                // through the network, so none are made here.)
                float k = 1f - Mathf.Exp(-18f * dt);
                transform.SetPositionAndRotation(Vector3.Lerp(transform.position, m_RemotePosition, k),
                    Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, m_RemoteYaw, 0f), k));
            }
            else
            {
                // Stepped off while moving: it rolls on by itself, slowing down, until it stops.
                if (!m_Ridden && m_Speed != 0f)
                {
                    m_Speed = Mathf.MoveTowards(m_Speed, 0f, coastDeceleration * dt);
                    Coast(m_Speed * dt);
                }

                // Rolling through blood: a squelch and bloody tyre tracks (ridden or coasting).
                if (BloodTracks.Instance != null && Mathf.Abs(m_Speed) > 0.05f)
                    BloodTracks.Instance.Roll(transform.position, transform.forward, transform.right, Mathf.Abs(m_Speed) * dt);
            }

            float degrees = m_Speed * dt / wheelRadius * Mathf.Rad2Deg;
            foreach (var wheel in wheels)
                if (wheel != null) wheel.Rotate(Vector3.right, degrees, Space.Self);

            float t = Mathf.Clamp01(Mathf.Abs(m_Speed) / maxSpeed);
            float moving = Mathf.SmoothStep(0f, 1f, Mathf.Abs(m_Speed) / 0.3f);
            float follow = 1f - Mathf.Exp(-12f * dt);
            // The electric motor, louder and higher the faster it goes.
            m_Motor.pitch = Mathf.Lerp(0.85f, 1.5f, t);
            // Someone else's scooter (or one rolling by itself) is quieter the further away it is.
            float distanceLevel = 1f;
            if (!m_Ridden || m_RemoteControlled)
            {
                var cam = Camera.main;
                float d = cam != null ? Vector3.Distance(cam.transform.position, transform.position) : 0f;
                distanceLevel = 2.5f / Mathf.Max(2.5f, d);
            }
            m_Motor.volume = Mathf.Lerp(m_Motor.volume, moving * humVolume * Mathf.Lerp(0.6f, 1f, t) * distanceLevel, follow);
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
