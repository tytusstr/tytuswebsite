using System.Collections.Generic;
using UnityEngine;

namespace GlassRoom
{
    /// <summary>
    /// Blood you can walk and ride through. Stepping in a pool squelches and soaks your shoes:
    /// the steps after leave bloody prints, fainter each step. The scooter squelches through
    /// and leaves bloody tyre tracks, one under each wheel, that thin out as it rolls on.
    /// Prints and tracks stay where they were made, like real blood would (only the very
    /// oldest are reused once there are a great many). Sounds: recorded foley (Audio/Blood).
    /// </summary>
    public class BloodTracks : MonoBehaviour
    {
        public static BloodTracks Instance { get; private set; }

        public AudioClip[] squelchClips;
        [Range(0f, 1f)] public float squelchVolume = 0.8f;
        [Tooltip("A transparent material the prints and tracks are drawn with.")]
        public Material printMaterial;

        static readonly Color k_Blood = new Color(0.46f, 0.02f, 0.03f, 1f);
        static readonly int k_ColorId = Shader.PropertyToID("_BaseColor");
        const int MaxMarks = 700;
        // Where the scooter's wheels touch the floor (StandUpScooterBuilder): 19 cm either side
        // of the middle, each tyre about 4 cm wide.
        const float WheelOffset = 0.19f, TyreWidth = 0.048f;

        struct Pool { public Vector3 centre; public float radius; }
        class Mark { public Transform t; public Renderer r; public float strength; }

        readonly List<Pool> m_Pools = new List<Pool>();
        readonly List<Mark> m_Marks = new List<Mark>();
        int m_NextMark;
        Material m_FootMat, m_TyreMat;
        Mesh m_Quad;
        MaterialPropertyBlock m_Block;
        AudioSource m_Source;
        float m_ShoeBlood, m_TyreBlood, m_TyreDistance, m_NextRollSquelch;
        int m_LastClip = -1;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            m_Source = gameObject.AddComponent<AudioSource>();
            m_Source.playOnAwake = false;
            m_Source.spatialBlend = 0f;
            m_Block = new MaterialPropertyBlock();
            m_Quad = FlatQuad();
            if (printMaterial != null)
            {
                m_FootMat = new Material(printMaterial) { mainTexture = ShoePrint() };
                m_TyreMat = new Material(printMaterial) { mainTexture = TyreTrack() };
            }
            // The pools: the crime scene's blood, by name.
            foreach (var r in FindObjectsByType<MeshRenderer>())
            {
                string n = r.gameObject.name;
                if (!n.StartsWith("Blood Pool") && !n.StartsWith("Blood Smear") && !n.StartsWith("Blood Trail")) continue;
                var b = r.bounds;
                m_Pools.Add(new Pool { centre = new Vector3(b.center.x, 0f, b.center.z), radius = Mathf.Min(b.extents.x, b.extents.z) * 0.9f });
            }
        }

        // ---- Shared with other players (see NetworkPlayer) ----

        /// <summary>A print or track you made: tyre track or footprint, where, which way, how big, how strong.</summary>
        public static event System.Action<bool, Vector3, float, Vector3, float, bool> MarkPlaced;
        /// <summary>A squelch you made, and how loud.</summary>
        public static event System.Action<Vector3, float> Squelched;
        bool m_Remote;

        /// <summary>Another player's print or track, made here too.</summary>
        public void PlaceRemote(bool tyre, Vector3 at, float yaw, Vector3 size, float strength, bool mirror)
        {
            var mat = tyre ? m_TyreMat : m_FootMat;
            if (mat == null) return;
            m_Remote = true;
            RemoteMarks++;
            PlaceMark(mat, at, Quaternion.Euler(0f, yaw, 0f), size, strength, mirror);
            m_Remote = false;
        }

        /// <summary>Another player's squelch, heard from where they are.</summary>
        public void SquelchAt(Vector3 position, float loudness)
        {
            if (squelchClips == null || squelchClips.Length == 0) return;
            var clip = squelchClips[Random.Range(0, squelchClips.Length)];
            var go = new GameObject("Squelch");
            go.transform.position = position;
            var a = go.AddComponent<AudioSource>();
            a.spatialBlend = 1f;
            a.minDistance = 2f;
            a.maxDistance = 25f;
            a.rolloffMode = AudioRolloffMode.Logarithmic;
            a.pitch = Random.Range(0.9f, 1.1f);
            a.PlayOneShot(clip, squelchVolume * loudness);
            Destroy(go, clip.length + 0.2f);
        }

        /// <summary>Prints and tracks on the floor (testing).</summary>
        public int MarkCount { get { int n = 0; foreach (var m in m_Marks) if (m.t != null && m.t.gameObject.activeSelf) n++; return n; } }
        public int RemoteMarks { get; private set; }

        public bool InBlood(Vector3 p)
        {
            foreach (var pool in m_Pools)
            {
                float dx = p.x - pool.centre.x, dz = p.z - pool.centre.z;
                if (dx * dx + dz * dz < pool.radius * pool.radius && p.y < 0.6f) return true;
            }
            return false;
        }

        /// <summary>A footstep at the feet, facing yaw degrees; left or right foot.</summary>
        public void Footstep(Vector3 feet, float yaw, bool left)
        {
            if (InBlood(feet))
            {
                m_ShoeBlood = 1f;
                Squelch(1f);
            }
            if (m_ShoeBlood <= 0.02f || m_FootMat == null) return;
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            Vector3 at = feet + rotation * new Vector3(left ? -0.11f : 0.11f, 0f, 0f);
            PlaceMark(m_FootMat, at, rotation * Quaternion.Euler(0f, left ? -4f : 4f, 0f), new Vector3(0.12f, 1f, 0.29f), m_ShoeBlood, left);
            m_ShoeBlood -= 0.11f;
        }

        /// <summary>The scooter rolling: where it is, its heading and sideways, and how far it moved.</summary>
        public void Roll(Vector3 position, Vector3 forward, Vector3 right, float distance)
        {
            if (distance <= 0f) return;
            if (InBlood(position))
            {
                m_TyreBlood = 1f;
                if (Time.time >= m_NextRollSquelch)
                {
                    Squelch(0.75f);
                    m_NextRollSquelch = Time.time + 0.32f;
                }
            }
            if (m_TyreBlood <= 0.02f || m_TyreMat == null) return;
            m_TyreDistance += distance;
            const float step = 0.12f;
            var rotation = Quaternion.LookRotation(new Vector3(forward.x, 0f, forward.z));
            while (m_TyreDistance >= step)
            {
                m_TyreDistance -= step;
                foreach (float side in new[] { -WheelOffset, WheelOffset })
                    PlaceMark(m_TyreMat, position + right * side, rotation, new Vector3(TyreWidth, 1f, 0.15f), m_TyreBlood, false);
                m_TyreBlood -= 0.022f;
                if (m_TyreBlood <= 0.02f) break;
            }
        }

        void Squelch(float loudness)
        {
            if (squelchClips == null || squelchClips.Length == 0) return;
            int pick = Random.Range(0, squelchClips.Length);
            if (squelchClips.Length > 1 && pick == m_LastClip) pick = (pick + 1) % squelchClips.Length;
            m_LastClip = pick;
            m_Source.pitch = Random.Range(0.9f, 1.1f);
            m_Source.PlayOneShot(squelchClips[pick], squelchVolume * loudness);
            Squelched?.Invoke(transform.position, loudness);
        }

        void PlaceMark(Material mat, Vector3 at, Quaternion rotation, Vector3 size, float strength, bool mirror)
        {
            Mark m;
            if (m_Marks.Count < MaxMarks)
            {
                var go = new GameObject("Blood Mark");
                go.AddComponent<MeshFilter>().sharedMesh = m_Quad;
                var r = go.AddComponent<MeshRenderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                m = new Mark { t = go.transform, r = r };
                m_Marks.Add(m);
            }
            else
            {
                m = m_Marks[m_NextMark];
                m_NextMark = (m_NextMark + 1) % MaxMarks;
            }
            m.r.sharedMaterial = mat;
            m.r.enabled = true;
            // Slightly different heights so overlapping marks never flicker against each other.
            m.t.SetPositionAndRotation(new Vector3(at.x, 0.0045f + (m_Marks.IndexOf(m) % 7) * 0.0004f, at.z), rotation);
            m.t.localScale = new Vector3(mirror ? -size.x : size.x, 1f, size.z);
            m.strength = Mathf.Clamp01(strength);
            Show(m);
            if (!m_Remote) MarkPlaced?.Invoke(mat == m_TyreMat, at, rotation.eulerAngles.y, size, strength, mirror);
        }

        void Show(Mark m)
        {
            var c = k_Blood;
            c.a = Mathf.Lerp(0.35f, 0.92f, m.strength);
            m_Block.SetColor(k_ColorId, c);
            m.r.SetPropertyBlock(m_Block);
        }

        // A 1×1 square lying flat, facing up, centred.
        static Mesh FlatQuad()
        {
            var mesh = new Mesh { name = "Blood Mark" };
            mesh.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        // A shoe sole: rounded forefoot and heel joined at the arch, with patchy tread.
        static Texture2D ShoePrint()
        {
            const int w = 64, h = 160;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            var rnd = new System.Random(5);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                float fore = Ellipse(u, v, 0.53f, 0.68f, 0.42f, 0.27f);
                float heel = Ellipse(u, v, 0.48f, 0.17f, 0.34f, 0.15f);
                float arch = Ellipse(u, v, 0.55f, 0.42f, 0.24f, 0.2f);
                float a = Mathf.Max(fore, Mathf.Max(heel, arch * 0.85f));
                // Tread: thin gaps across the sole, and uneven soaking.
                if ((y / 6) % 2 == 0 && v > 0.36f) a *= 0.55f;
                a *= 0.7f + 0.3f * (float)rnd.NextDouble();
                px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        // A tyre track segment: solid through the middle, soft at the sides, with tread lines.
        static Texture2D TyreTrack()
        {
            const int w = 16, h = 48;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            var rnd = new System.Random(9);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;
                float a = Mathf.Clamp01(1f - Mathf.Abs(u - 0.5f) * 2.2f) * 1.4f;
                if ((y / 4) % 2 == 0) a *= 0.7f;
                a *= 0.75f + 0.25f * (float)rnd.NextDouble();
                px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        static float Ellipse(float u, float v, float cu, float cv, float ru, float rv)
        {
            float d = ((u - cu) * (u - cu)) / (ru * ru) + ((v - cv) * (v - cv)) / (rv * rv);
            return Mathf.Clamp01((1f - d) * 6f);
        }
    }
}
