using System.Collections.Generic;
using UnityEngine;

namespace GlassRoom
{
    /// <summary>
    /// The murder victim's body, which can be pushed around: walk into it to shove it slowly
    /// along, or hit it with a scooter to send it sliding (further the faster you go). It's a
    /// rag doll, so its arms, legs and head flop as it moves. Wherever it's dragged it leaves
    /// smears of blood on the floor (which you can then walk or ride through and spread), and
    /// thrown hard into a wall it leaves a splash of blood there. Thuds, a wet scrape while it
    /// slides, and a splat on the wall.
    ///
    /// Whoever moves it works out the physics and shares where every part is (NetworkPlayer);
    /// everyone else shows it there. Once it stops it lies still again.
    /// </summary>
    public class VictimBody : MonoBehaviour
    {
        public static VictimBody Instance { get; private set; }

        public AudioClip dragClip, hitClip, splatClip;
        public AudioClip[] thudClips;
        public Material smearMaterial, poolMaterial, wallMaterial;

        /// <summary>Where every part is now (packed), to send to everyone.</summary>
        public static event System.Action<string> PoseShared;
        /// <summary>A splash of blood on a wall: where, which way the wall faces, how big.</summary>
        public static event System.Action<Vector3, Vector3, float> StainShared;
        /// <summary>A thud (0) or a hard hit (1) or a splat (2), where, how loud.</summary>
        public static event System.Action<int, Vector3, float> SoundShared;

        Rigidbody[] m_Bodies;
        Vector3[] m_TargetPos;
        Quaternion[] m_TargetRot;
        bool m_Simulating, m_HasTarget, m_EverMoved;
        float m_Quiet, m_NextSend, m_LastPush = -10f, m_LastSound = -10f;
        AudioSource m_Drag, m_Fx;
        Vector3 m_LastPelvis;
        // Where the last smear was left, for the hips and the chest.
        readonly Vector3[] m_SmearFrom = new Vector3[2];
        Rigidbody[] m_Smearing;
        readonly List<Renderer> m_Stains = new List<Renderer>();
        int m_StainOrder;
        Mesh m_Quad;
        const int MaxStains = 90;

        /// <summary>True once it's been moved from where it started (newcomers are told where it is).</summary>
        public bool EverMoved => m_EverMoved;

        void Awake()
        {
            Instance = this;
            m_Bodies = GetComponentsInChildren<Rigidbody>();
            m_TargetPos = new Vector3[m_Bodies.Length];
            m_TargetRot = new Quaternion[m_Bodies.Length];
            foreach (var rb in m_Bodies)
            {
                rb.isKinematic = true;
                foreach (var c in rb.GetComponents<Collider>())
                    c.gameObject.AddComponent<VictimPart>().body = this;
            }
            var pelvis = m_Bodies[0].transform;
            m_Drag = pelvis.gameObject.AddComponent<AudioSource>();
            m_Drag.clip = dragClip;
            m_Drag.loop = true;
            m_Drag.playOnAwake = false;
            m_Drag.spatialBlend = 1f;
            m_Drag.minDistance = 1.5f;
            m_Drag.maxDistance = 25f;
            m_Drag.rolloffMode = AudioRolloffMode.Logarithmic;
            m_Drag.volume = 0f;
            m_Fx = pelvis.gameObject.AddComponent<AudioSource>();
            m_Fx.playOnAwake = false;
            m_Fx.spatialBlend = 1f;
            m_Fx.minDistance = 1.5f;
            m_Fx.maxDistance = 30f;
            m_Fx.rolloffMode = AudioRolloffMode.Logarithmic;
            m_LastPelvis = pelvis.position;
            // The hips and the chest leave the smears.
            var chest = System.Array.Find(m_Bodies, b => b.name.Contains("Spine"));
            m_Smearing = new[] { m_Bodies[0], chest != null ? chest : m_Bodies[0] };
            m_SmearFrom[0] = Floor(m_Smearing[0].position);
            m_SmearFrom[1] = Floor(m_Smearing[1].position);
            m_Quad = FlatQuad();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        static Vector3 Floor(Vector3 p) => new Vector3(p.x, 0f, p.z);

        // ---- Being pushed (here, by you) ----

        /// <summary>Shoved at a point: force (walking, applied each frame) or a sudden hit (scooter).</summary>
        public void Push(Vector3 point, Vector3 amount, bool sudden)
        {
            Wake();
            var rb = Nearest(point);
            rb.AddForceAtPosition(amount, point, sudden ? ForceMode.Impulse : ForceMode.Force);
            m_LastPush = Time.time;
        }

        /// <summary>Hit by a scooter going this fast: thrown along (and a thud everyone hears).</summary>
        public void Hit(Vector3 point, Vector3 heading, float speed)
        {
            if (speed < 0.4f) return;
            heading.y = 0f;
            heading.Normalize();
            // Hard hits lift it a little off the floor, too.
            Vector3 impulse = (heading + Vector3.up * Mathf.Lerp(0.05f, 0.35f, speed / 8f)) * speed * 9f;
            Push(point, impulse, true);
            // The rest of the body is carried along too, so it slides rather than only folding.
            foreach (var rb in m_Bodies) rb.AddForce(heading * speed * 0.55f, ForceMode.VelocityChange);
            Sound(speed > 4.5f ? 1 : 0, point, Mathf.Clamp01(speed / 6f), true);
        }

        void Wake()
        {
            if (!m_Simulating)
            {
                // Start from exactly where it's shown.
                m_Simulating = true;
                m_HasTarget = false;
                foreach (var rb in m_Bodies) { rb.isKinematic = false; rb.WakeUp(); }
            }
            m_Quiet = 0f;
            m_EverMoved = true;
        }

        Rigidbody Nearest(Vector3 point)
        {
            Rigidbody best = m_Bodies[0];
            float bestD = float.MaxValue;
            foreach (var rb in m_Bodies)
            {
                float d = (rb.worldCenterOfMass - point).sqrMagnitude;
                if (d < bestD) { bestD = d; best = rb; }
            }
            return best;
        }

        void FixedUpdate()
        {
            if (!m_Simulating) return;
            float fastest = 0f;
            foreach (var rb in m_Bodies) fastest = Mathf.Max(fastest, rb.linearVelocity.magnitude);
            m_Quiet = fastest < 0.06f ? m_Quiet + Time.fixedDeltaTime : 0f;
            // Still again: it stays exactly there (and everyone is told).
            if (m_Quiet > 1f && Time.time - m_LastPush > 1f)
            {
                m_Simulating = false;
                foreach (var rb in m_Bodies) rb.isKinematic = true;
                PoseShared?.Invoke(Pack());
            }
        }

        // ---- Shown where someone else has it ----

        /// <summary>Where someone else's game has it: shown there, smoothly.</summary>
        public void ShowRemote(string packed, bool snap)
        {
            // While you're pushing it yourself, your own version wins.
            if (m_Simulating && Time.time - m_LastPush < 0.6f) return;
            if (!Unpack(packed)) return;
            if (m_Simulating)
            {
                m_Simulating = false;
                foreach (var rb in m_Bodies) rb.isKinematic = true;
            }
            m_EverMoved = true;
            m_HasTarget = true;
            if (snap) for (int i = 0; i < m_Bodies.Length; i++) m_Bodies[i].transform.SetPositionAndRotation(m_TargetPos[i], m_TargetRot[i]);
        }

        void Update()
        {
            if (m_Simulating && Time.time >= m_NextSend)
            {
                m_NextSend = Time.time + 0.1f;
                PoseShared?.Invoke(Pack());
            }
            else if (!m_Simulating && m_HasTarget)
            {
                float k = 1f - Mathf.Exp(-12f * Time.deltaTime);
                for (int i = 0; i < m_Bodies.Length; i++)
                {
                    var t = m_Bodies[i].transform;
                    t.SetPositionAndRotation(Vector3.Lerp(t.position, m_TargetPos[i], k), Quaternion.Slerp(t.rotation, m_TargetRot[i], k));
                }
            }

            // Sliding along the floor: a wet scrape, and smears where the hips and chest go.
            var pelvis = m_Bodies[0].transform;
            Vector3 moved = Floor(pelvis.position) - Floor(m_LastPelvis);
            float speed = Time.deltaTime > 0f ? moved.magnitude / Time.deltaTime : 0f;
            m_LastPelvis = pelvis.position;
            bool onFloor = pelvis.position.y < 0.45f;
            float target = onFloor && dragClip != null ? Mathf.Clamp01((speed - 0.08f) / 1.2f) * 0.9f : 0f;
            m_Drag.volume = Mathf.MoveTowards(m_Drag.volume, target, 4f * Time.deltaTime);
            m_Drag.pitch = Mathf.Lerp(0.85f, 1.15f, Mathf.Clamp01(speed / 3f));
            if (m_Drag.volume > 0.01f && !m_Drag.isPlaying) m_Drag.Play();
            else if (m_Drag.volume <= 0.01f && m_Drag.isPlaying) m_Drag.Stop();
            for (int part = 0; part < 2; part++)
            {
                Vector3 at = m_Smearing[part].worldCenterOfMass;
                if (at.y > 0.45f) { m_SmearFrom[part] = Floor(at); continue; }
                Vector3 from = m_SmearFrom[part];
                Vector3 step = Floor(at) - from;
                if (step.magnitude < 0.3f) continue;
                Smear(from + step * 0.5f, step);
                m_SmearFrom[part] = Floor(at);
            }
        }

        // ---- Blood ----

        void Smear(Vector3 centre, Vector3 along)
        {
            var mat = smearMaterial != null ? smearMaterial : poolMaterial;
            if (mat == null) return;
            float yaw = Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg + Random.Range(-12f, 12f);
            float length = Mathf.Clamp(along.magnitude * 1.4f, 0.45f, 1.1f);
            Stain("Drag Smear", mat, centre + Vector3.up * (0.0045f + 0.00002f * (m_StainOrder % 40)),
                Quaternion.Euler(0f, yaw + 90f, 0f), new Vector3(length, 1f, Random.Range(0.4f, 0.6f)));
            if (BloodTracks.Instance != null) BloodTracks.Instance.AddPool(centre, 0.22f);
        }

        /// <summary>A splash of blood on a wall (made here, or by whoever threw it there).</summary>
        public void WallStain(Vector3 at, Vector3 normal, float size, bool share)
        {
            if (wallMaterial == null) return;
            normal.y = 0f;
            if (normal.sqrMagnitude < 0.01f) return;
            normal.Normalize();
            // A flat square standing on the wall, a few millimetres out from it.
            var rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            Stain("Wall Splash", wallMaterial, at + normal * 0.004f, rotation, new Vector3(size, 1f, size));
            if (BloodTracks.Instance != null) BloodTracks.Instance.AddPool(new Vector3(at.x, 0f, at.z) + normal * 0.25f, 0.18f);
            if (share)
            {
                StainShared?.Invoke(at, normal, size);
                Sound(2, at, Mathf.Clamp01(size / 0.8f), true);
            }
        }

        Renderer Stain(string name, Material mat, Vector3 at, Quaternion rotation, Vector3 scale)
        {
            Renderer r;
            if (m_Stains.Count >= MaxStains)
            {
                r = m_Stains[0];
                m_Stains.RemoveAt(0);
            }
            else
            {
                var go = new GameObject(name);
                go.AddComponent<MeshFilter>().sharedMesh = m_Quad;
                r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = true;
            }
            r.sharedMaterial = mat;
            // Later stains draw over earlier ones (no flicker where they overlap).
            r.sortingOrder = ++m_StainOrder;
            r.transform.SetPositionAndRotation(at, rotation);
            r.transform.localScale = scale;
            m_Stains.Add(r);
            return r;
        }

        // A one-metre square lying flat (facing up), for stains.
        static Mesh FlatQuad()
        {
            var m = new Mesh { name = "Stain" };
            m.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.tangents = new[] { new Vector4(1f, 0f, 0f, -1f), new Vector4(1f, 0f, 0f, -1f), new Vector4(1f, 0f, 0f, -1f), new Vector4(1f, 0f, 0f, -1f) };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateBounds();
            return m;
        }

        // ---- Bumping into things ----

        internal void PartHit(Collision c)
        {
            if (!m_Simulating) return;
            if (c.rigidbody != null && c.rigidbody.GetComponentInParent<VictimBody>() == this) return;
            float impact = c.relativeVelocity.magnitude;
            if (impact < 1.2f || c.contactCount == 0) return;
            var contact = c.GetContact(0);
            bool wall = Mathf.Abs(contact.normal.y) < 0.5f && c.collider.GetComponent<CharacterController>() == null;
            if (wall && impact > 1.6f) WallStain(contact.point, contact.normal, Mathf.Clamp(0.35f + impact * 0.12f, 0.35f, 1.1f), true);
            else if (Time.time - m_LastSound > 0.25f) Sound(0, contact.point, Mathf.Clamp01(impact / 5f), true);
        }

        /// <summary>Plays a body sound here (and, if made here, for everyone).</summary>
        public void Sound(int kind, Vector3 at, float loudness, bool share)
        {
            if (Time.time - m_LastSound < 0.12f) return;
            m_LastSound = Time.time;
            AudioClip clip = kind == 1 ? hitClip : kind == 2 ? splatClip : (thudClips != null && thudClips.Length > 0 ? thudClips[Random.Range(0, thudClips.Length)] : null);
            if (clip != null)
            {
                m_Fx.transform.position = m_Bodies[0].transform.position;
                m_Fx.pitch = Random.Range(0.92f, 1.08f);
                m_Fx.PlayOneShot(clip, Mathf.Lerp(0.35f, 1f, loudness));
            }
            if (share) SoundShared?.Invoke(kind, at, loudness);
        }

        // ---- Packing where every part is: the hips' place, then each part's turn and its
        // place relative to the hips (in millimetres). ----

        public string Pack()
        {
            int n = m_Bodies.Length;
            var bytes = new byte[12 + n * 8 + (n - 1) * 6];
            int o = 0;
            Vector3 root = m_Bodies[0].transform.position;
            void F(float v) { System.BitConverter.GetBytes(v).CopyTo(bytes, o); o += 4; }
            void S(float v) { short s = (short)Mathf.Clamp(Mathf.RoundToInt(v), -32767, 32767); bytes[o++] = (byte)s; bytes[o++] = (byte)(s >> 8); }
            F(root.x); F(root.y); F(root.z);
            for (int i = 0; i < n; i++)
            {
                var q = m_Bodies[i].transform.rotation;
                S(q.x * 32767f); S(q.y * 32767f); S(q.z * 32767f); S(q.w * 32767f);
            }
            for (int i = 1; i < n; i++)
            {
                Vector3 d = (m_Bodies[i].transform.position - root) * 1000f;
                S(d.x); S(d.y); S(d.z);
            }
            return System.Convert.ToBase64String(bytes);
        }

        bool Unpack(string packed)
        {
            byte[] bytes;
            try { bytes = System.Convert.FromBase64String(packed); } catch { return false; }
            int n = m_Bodies.Length;
            if (bytes.Length != 12 + n * 8 + (n - 1) * 6) return false;
            int o = 0;
            float F() { float v = System.BitConverter.ToSingle(bytes, o); o += 4; return v; }
            float S() { short s = (short)(bytes[o] | (bytes[o + 1] << 8)); o += 2; return s; }
            var root = new Vector3(F(), F(), F());
            for (int i = 0; i < n; i++)
            {
                var q = new Quaternion(S() / 32767f, S() / 32767f, S() / 32767f, S() / 32767f);
                m_TargetRot[i] = Quaternion.Normalize(q);
            }
            m_TargetPos[0] = root;
            for (int i = 1; i < n; i++) m_TargetPos[i] = root + new Vector3(S(), S(), S()) / 1000f;
            return true;
        }
    }

    /// <summary>Passes a body part's bumps on to the body.</summary>
    public class VictimPart : MonoBehaviour
    {
        public VictimBody body;
        void OnCollisionEnter(Collision c) { if (body != null) body.PartHit(c); }
    }
}
