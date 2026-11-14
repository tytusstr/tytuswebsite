using UnityEngine;

namespace GlassRoom
{
    /// <summary>
    /// A knocked-over player's body gone limp: once they've fallen, the animation lets go and the
    /// body is a rag doll lying on the floor (joints free), which scooters can hit and shove along
    /// until they get up. On their own screen it's simulated; everyone else is sent where every
    /// part is and shows it there (also anyone who arrives while they're lying there). Getting up,
    /// the animation takes over again, blending smoothly out of the pose they ended in.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class PlayerRagdoll : MonoBehaviour
    {
        static readonly (string bone, string end, float radius, float mass, string parent, float twist, float swing1, float swing2)[] k_Parts =
        {
            ("Bip01 Pelvis", "Bip01 Spine1", 0.14f, 12f, null, 0f, 0f, 0f),
            ("Bip01 Spine2", "Bip01 Neck", 0.15f, 14f, "Bip01 Pelvis", 20f, 25f, 25f),
            ("Bip01 Head", null, 0.11f, 5f, "Bip01 Spine2", 45f, 40f, 35f),
            ("Bip01 L UpperArm", "Bip01 L Forearm", 0.05f, 2.5f, "Bip01 Spine2", 40f, 80f, 60f),
            ("Bip01 R UpperArm", "Bip01 R Forearm", 0.05f, 2.5f, "Bip01 Spine2", 40f, 80f, 60f),
            ("Bip01 L Forearm", "Bip01 L Hand", 0.045f, 1.8f, "Bip01 L UpperArm", 50f, 90f, 25f),
            ("Bip01 R Forearm", "Bip01 R Hand", 0.045f, 1.8f, "Bip01 R UpperArm", 50f, 90f, 25f),
            ("Bip01 L Thigh", "Bip01 L Calf", 0.075f, 8f, "Bip01 Pelvis", 25f, 70f, 40f),
            ("Bip01 R Thigh", "Bip01 R Calf", 0.075f, 8f, "Bip01 Pelvis", 25f, 70f, 40f),
            ("Bip01 L Calf", "Bip01 L Foot", 0.055f, 4.5f, "Bip01 L Thigh", 20f, 90f, 20f),
            ("Bip01 R Calf", "Bip01 R Foot", 0.055f, 4.5f, "Bip01 R Thigh", 20f, 90f, 20f),
        };

        Rigidbody[] m_Bodies;
        Collider[] m_Colliders;
        Transform m_Head, m_Neck, m_LThigh, m_RThigh;
        Animator m_Animator;
        Vector3[] m_TargetPos;
        Quaternion[] m_TargetRot;
        bool m_Simulating, m_Following, m_HasTarget;
        // Getting up: the pose it ended in, faded into the animation.
        Transform[] m_Bones;
        Quaternion[] m_From;
        float m_Blend = 1f;

        public bool Active => m_Simulating || m_Following;
        public Transform Pelvis => m_Bodies != null ? m_Bodies[0].transform : null;

        public static PlayerRagdoll Of(PlayerAvatar avatar)
        {
            if (avatar == null) return null;
            var r = avatar.GetComponent<PlayerRagdoll>();
            return r != null ? r : avatar.gameObject.AddComponent<PlayerRagdoll>();
        }

        Transform Find(string name)
        {
            foreach (var t in GetComponentsInChildren<Transform>()) if (t.name == name) return t;
            return null;
        }

        void Build()
        {
            if (m_Bodies != null) return;
            m_Animator = GetComponent<Animator>();
            if (m_Animator != null) m_Animator.keepAnimatorStateOnDisable = true;
            m_Bodies = new Rigidbody[k_Parts.Length];
            m_Colliders = new Collider[k_Parts.Length];
            for (int i = 0; i < k_Parts.Length; i++)
            {
                var p = k_Parts[i];
                var bone = Find(p.bone);
                var end = p.end != null ? Find(p.end) : null;
                if (bone == null) { m_Bodies = null; return; }
                Collider c;
                if (end == null)
                {
                    var s = bone.gameObject.AddComponent<SphereCollider>();
                    s.radius = p.radius;
                    // The head bone sits at the bottom of the skull.
                    Vector3 up = m_Neck != null ? (bone.position - m_Neck.position).normalized : transform.up;
                    s.center = bone.InverseTransformPoint(bone.position + up * 0.09f);
                    c = s;
                }
                else
                {
                    var cap = bone.gameObject.AddComponent<CapsuleCollider>();
                    Vector3 local = bone.InverseTransformPoint(end.position);
                    if (p.bone.Contains("Calf")) local *= 1.12f;
                    if (p.bone.Contains("Forearm")) local *= 1.35f;
                    int axis = Mathf.Abs(local.x) > Mathf.Abs(local.y) ? (Mathf.Abs(local.x) > Mathf.Abs(local.z) ? 0 : 2) : (Mathf.Abs(local.y) > Mathf.Abs(local.z) ? 1 : 2);
                    cap.direction = axis;
                    cap.radius = p.radius;
                    cap.height = local.magnitude + p.radius;
                    cap.center = local * 0.5f;
                    c = cap;
                }
                c.enabled = false;
                m_Colliders[i] = c;
                var rb = bone.gameObject.AddComponent<Rigidbody>();
                rb.mass = p.mass;
                rb.linearDamping = 0.2f;
                rb.angularDamping = 0.9f;
                rb.isKinematic = true;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.solverIterations = 12;
                m_Bodies[i] = rb;
                if (p.bone == "Bip01 Spine2") m_Neck = Find("Bip01 Neck");
            }
            for (int i = 0; i < k_Parts.Length; i++)
            {
                var p = k_Parts[i];
                if (p.parent == null) continue;
                var parent = m_Bodies[System.Array.FindIndex(k_Parts, q => q.bone == p.parent)];
                var joint = m_Bodies[i].gameObject.AddComponent<CharacterJoint>();
                joint.connectedBody = parent;
                var end = p.end != null ? Find(p.end) : null;
                Vector3 along = end != null ? m_Bodies[i].transform.InverseTransformPoint(end.position).normalized : Vector3.right;
                joint.axis = along;
                joint.swingAxis = Vector3.Cross(along, Mathf.Abs(along.y) < 0.9f ? Vector3.up : Vector3.forward).normalized;
                joint.lowTwistLimit = new SoftJointLimit { limit = -p.twist };
                joint.highTwistLimit = new SoftJointLimit { limit = p.twist };
                joint.swing1Limit = new SoftJointLimit { limit = p.swing1 };
                joint.swing2Limit = new SoftJointLimit { limit = p.swing2 };
                joint.enableProjection = true;
            }
            m_Head = Find("Bip01 Head");
            m_LThigh = Find("Bip01 L Thigh");
            m_RThigh = Find("Bip01 R Thigh");
            m_TargetPos = new Vector3[m_Bodies.Length];
            m_TargetRot = new Quaternion[m_Bodies.Length];
            m_Bones = GetComponentsInChildren<Transform>();
            m_From = new Quaternion[m_Bones.Length];
        }

        /// <summary>Goes limp here (simulated), moving at this speed; never bumping into the given collider (your own).</summary>
        public void Begin(Vector3 velocity, Collider ignore)
        {
            Build();
            if (m_Bodies == null) return;
            m_Following = false;
            m_Simulating = true;
            m_Blend = 1f;
            if (m_Animator != null) m_Animator.enabled = false;
            foreach (var c in m_Colliders)
            {
                c.enabled = true;
                if (ignore != null) Physics.IgnoreCollision(c, ignore, true);
            }
            foreach (var rb in m_Bodies)
            {
                rb.isKinematic = false;
                rb.linearVelocity = velocity;
                rb.WakeUp();
            }
        }

        /// <summary>Hit by a scooter while lying here: shoved along, harder the faster it was going.</summary>
        public void Hit(Vector3 heading, float speed)
        {
            if (!m_Simulating) return;
            heading.y = 0f;
            heading.Normalize();
            foreach (var rb in m_Bodies) rb.AddForce((heading + Vector3.up * 0.15f) * speed * 0.8f, ForceMode.VelocityChange);
        }

        /// <summary>Lying where someone else's game has them (and bumpable here).</summary>
        public void Follow(string packed, bool snap)
        {
            Build();
            if (m_Bodies == null || !Unpack(packed)) return;
            if (!m_Following)
            {
                m_Simulating = false;
                m_Following = true;
                m_Blend = 1f;
                snap = true;
                if (m_Animator != null) m_Animator.enabled = false;
                foreach (var rb in m_Bodies) rb.isKinematic = true;
                foreach (var c in m_Colliders) c.enabled = true;
            }
            m_HasTarget = true;
            if (snap) for (int i = 0; i < m_Bodies.Length; i++) m_Bodies[i].transform.SetPositionAndRotation(m_TargetPos[i], m_TargetRot[i]);
        }

        /// <summary>Back to the animation (getting up), fading out of the pose it's in now.</summary>
        public void End()
        {
            if (!Active) return;
            m_Simulating = m_Following = m_HasTarget = false;
            for (int i = 0; i < m_Bones.Length; i++) if (m_Bones[i] != null) m_From[i] = m_Bones[i].localRotation;
            m_FromRoot = m_Bodies[0].transform.position;
            foreach (var rb in m_Bodies) rb.isKinematic = true;
            foreach (var c in m_Colliders) c.enabled = false;
            if (m_Animator != null) m_Animator.enabled = true;
            m_Blend = 0f;
        }
        Vector3 m_FromRoot;

        /// <summary>Lying on their back (else face down).</summary>
        public bool OnBack
        {
            get
            {
                if (m_Bodies == null || m_Neck == null || m_LThigh == null || m_RThigh == null) return true;
                Vector3 spine = m_Neck.position - m_Bodies[0].position;
                Vector3 across = m_LThigh.position - m_RThigh.position;
                return Vector3.Cross(spine, across).y > 0f;
            }
        }

        /// <summary>Which way the head points from the hips, in degrees (0 = +Z).</summary>
        public float HeadYaw
        {
            get
            {
                Vector3 d = (m_Head != null ? m_Head.position : m_Bodies[0].position + transform.forward) - m_Bodies[0].position;
                return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            }
        }

        void LateUpdate()
        {
            if (m_Following && m_HasTarget)
            {
                float k = 1f - Mathf.Exp(-12f * Time.deltaTime);
                for (int i = 0; i < m_Bodies.Length; i++)
                {
                    var t = m_Bodies[i].transform;
                    t.SetPositionAndRotation(Vector3.Lerp(t.position, m_TargetPos[i], k), Quaternion.Slerp(t.rotation, m_TargetRot[i], k));
                }
            }
            else if (m_Blend < 1f)
            {
                // Getting up: out of the limp pose into the animation over a moment.
                m_Blend = Mathf.Min(1f, m_Blend + Time.deltaTime / 0.35f);
                float w = 1f - Mathf.SmoothStep(0f, 1f, m_Blend);
                for (int i = 0; i < m_Bones.Length; i++)
                    if (m_Bones[i] != null) m_Bones[i].localRotation = Quaternion.Slerp(m_Bones[i].localRotation, m_From[i], w);
            }
        }

        /// <summary>True while it's still moving (shared often), else lying still (shared now and then).</summary>
        public bool Moving
        {
            get
            {
                if (!m_Simulating) return false;
                foreach (var rb in m_Bodies) if (rb.linearVelocity.sqrMagnitude > 0.01f) return true;
                return false;
            }
        }

        // Where every part is: the hips' place, each part's turn, and its place relative to the hips (mm).
        public string Pack()
        {
            if (m_Bodies == null) return "";
            int n = m_Bodies.Length;
            var bytes = new byte[12 + n * 8 + (n - 1) * 6];
            int o = 0;
            Vector3 root = m_Bodies[0].transform.position;
            void F(float v) { System.BitConverter.GetBytes(v).CopyTo(bytes, o); o += 4; }
            void S(float v) { short s = (short)Mathf.Clamp(Mathf.RoundToInt(v), -32767, 32767); bytes[o++] = (byte)s; bytes[o++] = (byte)(s >> 8); }
            F(root.x); F(root.y); F(root.z);
            for (int i = 0; i < n; i++) { var q = m_Bodies[i].transform.rotation; S(q.x * 32767f); S(q.y * 32767f); S(q.z * 32767f); S(q.w * 32767f); }
            for (int i = 1; i < n; i++) { Vector3 d = (m_Bodies[i].transform.position - root) * 1000f; S(d.x); S(d.y); S(d.z); }
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
            for (int i = 0; i < n; i++) m_TargetRot[i] = Quaternion.Normalize(new Quaternion(S() / 32767f, S() / 32767f, S() / 32767f, S() / 32767f));
            m_TargetPos[0] = root;
            for (int i = 1; i < n; i++) m_TargetPos[i] = root + new Vector3(S(), S(), S()) / 1000f;
            return true;
        }
    }
}
