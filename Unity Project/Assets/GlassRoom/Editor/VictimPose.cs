using UnityEditor;
using UnityEngine;

namespace GlassRoom.Editor
{
    /// <summary>
    /// Poses the downloaded Rocketbox character (Models/Victim/Victim.fbx, MIT) as a murder victim
    /// lying face down, and bakes the result into a static mesh (Models/Victim/Victim Posed.asset).
    /// Rotations are about world axes of the standing model (Y up, facing +Z), each about its joint:
    /// Z swings a limb sideways (along the floor once the body is lying face down), X swings it
    /// forward or back (toward or away from the floor), Y twists.
    /// </summary>
    public static class VictimPose
    {
        public const string ModelPath = "Assets/GlassRoom/Models/Victim/Victim.fbx";
        public const string MeshPath = "Assets/GlassRoom/Models/Victim/Victim Posed.asset";

        static readonly (string bone, Vector3 axis, float degrees)[] Pose =
        {
            // Slumped spine, bent a little to one side.
            ("Bip01 Spine", Vector3.forward, 5f), ("Bip01 Spine1", Vector3.forward, 4f),
            ("Bip01 Spine1", Vector3.up, -6f),
            // Head turned hard to the side, cheek on the floor.
            ("Bip01 Neck", Vector3.up, 25f), ("Bip01 Head", Vector3.up, 55f), ("Bip01 Head", Vector3.forward, -10f),
            // Left arm flung up past the head, elbow bent.
            ("Bip01 L UpperArm", Vector3.forward, -68f), ("Bip01 L UpperArm", Vector3.right, 14f),
            ("Bip01 L Forearm", Vector3.forward, -48f), ("Bip01 L Hand", Vector3.right, 20f),
            // Right arm limp along the side, a little behind.
            ("Bip01 R UpperArm", Vector3.forward, -38f), ("Bip01 R UpperArm", Vector3.right, -14f),
            ("Bip01 R UpperArm", Vector3.up, 70f), ("Bip01 R Forearm", Vector3.forward, -8f),
            // Left knee bent out to the side, flat on the floor.
            ("Bip01 L Thigh", Vector3.forward, -38f), ("Bip01 L Calf", Vector3.forward, 62f),
            ("Bip01 L Foot", Vector3.right, 45f),
            // Right leg straight and a little apart, toes pointing back.
            ("Bip01 R Thigh", Vector3.forward, 7f), ("Bip01 R Thigh", Vector3.up, -12f), ("Bip01 R Foot", Vector3.right, 50f),
        };

        static readonly string[] CurlBones = { "Finger1", "Finger2", "Finger3", "Finger4" };

        [MenuItem("Tools/Glass Room/Bake Victim Pose")]
        public static Mesh Bake()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) return null;
            var inst = (GameObject)Object.Instantiate(model);
            try
            {
                inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                foreach (var (bone, axis, degrees) in Pose)
                {
                    var t = Find(inst.transform, bone);
                    if (t == null) { Debug.LogWarning("Victim pose: no bone " + bone); continue; }
                    t.rotation = Quaternion.AngleAxis(degrees, axis) * t.rotation;
                }
                // Loosely curled fingers.
                foreach (var t in inst.GetComponentsInChildren<Transform>())
                    foreach (var c in CurlBones)
                        if (t.name.EndsWith(c) || t.name.EndsWith(c + "1"))
                            t.localRotation = t.localRotation * Quaternion.Euler(0f, 0f, -25f);

                var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
                Settle(inst.transform, smr);
                var mesh = new Mesh { name = "Victim Posed", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                smr.BakeMesh(mesh, true);
                // Into the model's own space (the renderer may sit under a scaled/rotated node).
                var verts = mesh.vertices; var normals = mesh.normals; var tangents = mesh.tangents;
                var m = smr.transform.localToWorldMatrix;
                for (int i = 0; i < verts.Length; i++)
                {
                    verts[i] = m.MultiplyPoint3x4(verts[i]);
                    normals[i] = m.MultiplyVector(normals[i]).normalized;
                    if (tangents.Length == verts.Length)
                    {
                        var tv = m.MultiplyVector(new Vector3(tangents[i].x, tangents[i].y, tangents[i].z)).normalized;
                        tangents[i] = new Vector4(tv.x, tv.y, tv.z, tangents[i].w);
                    }
                }
                mesh.vertices = verts; mesh.normals = normals;
                if (tangents.Length == verts.Length) mesh.tangents = tangents;
                mesh.RecalculateBounds();
                // A fresh asset each time (copying into the old one left the editor drawing the
                // old shape).
                AssetDatabase.DeleteAsset(MeshPath);
                AssetDatabase.CreateAsset(mesh, MeshPath);
                AssetDatabase.SaveAssets();
                return mesh;
            }
            finally { Object.DestroyImmediate(inst); }
        }

        // Lying face down, the floor is the plane the front of the chest rests on (the largest
        // z of the torso in the standing model, which faces +Z). Each limb segment in turn is
        // swung toward or away from that plane, about its joint, until the part beyond it
        // (elbow onward, hand onward, fingers; knee onward, foot onward, toes; the head) just
        // touches it: nothing floats above the floor and nothing sinks into it.
        static readonly (string bone, string measure)[] SettleChain =
        {
            ("Bip01 L Thigh", "Bip01 L Calf"), ("Bip01 R Thigh", "Bip01 R Calf"),
            ("Bip01 L Calf", "Bip01 L Foot"), ("Bip01 R Calf", "Bip01 R Foot"),
            ("Bip01 L Foot", "Bip01 L Toe0"), ("Bip01 R Foot", "Bip01 R Toe0"),
            // The head first: in this skeleton the arms hang from the neck, so turning it later
            // would swing the arms out of place again.
            ("Bip01 Neck", "Bip01 Head"),
            // Shoulders roll down so the upper arms lie on the floor too.
            ("Bip01 L Clavicle", "Bip01 L UpperArm"), ("Bip01 R Clavicle", "Bip01 R UpperArm"),
            ("Bip01 L UpperArm", "Bip01 L Forearm"), ("Bip01 R UpperArm", "Bip01 R Forearm"),
            ("Bip01 L Forearm", "Bip01 L Hand"), ("Bip01 R Forearm", "Bip01 R Hand"),
            ("Bip01 L Hand", "Bip01 L Finger21"), ("Bip01 R Hand", "Bip01 R Finger21"),
        };

        static void Settle(Transform root, SkinnedMeshRenderer smr)
        {
            var bones = smr.bones;
            var weights = smr.sharedMesh.boneWeights;
            var baked = new Mesh();
            int Index(Transform t) => System.Array.IndexOf(bones, t);

            // Which bone each vertex mostly follows.
            var owner = new int[weights.Length];
            for (int i = 0; i < weights.Length; i++) owner[i] = weights[i].boneIndex0;

            System.Collections.Generic.HashSet<int> Subtree(Transform t)
            {
                var set = new System.Collections.Generic.HashSet<int>();
                foreach (var d in t.GetComponentsInChildren<Transform>()) { int k = Index(d); if (k >= 0) set.Add(k); }
                return set;
            }
            float FrontZ(System.Collections.Generic.HashSet<int> set)
            {
                smr.BakeMesh(baked, true);
                var v = baked.vertices;
                var m = smr.transform.localToWorldMatrix;
                float z = float.MinValue;
                for (int i = 0; i < v.Length; i++) if (set.Contains(owner[i])) z = Mathf.Max(z, m.MultiplyPoint3x4(v[i]).z);
                return z;
            }

            // The torso: everything that isn't an arm, a leg or the head.
            var torso = new System.Collections.Generic.HashSet<int>();
            var limbs = new System.Collections.Generic.HashSet<int>();
            foreach (var n in new[] { "Bip01 L Clavicle", "Bip01 R Clavicle", "Bip01 L Thigh", "Bip01 R Thigh", "Bip01 Head" })
            { var t = Find(root, n); if (t != null) limbs.UnionWith(Subtree(t)); }
            for (int k = 0; k < bones.Length; k++) if (!limbs.Contains(k)) torso.Add(k);
            float floor = FrontZ(torso);

            foreach (var (boneName, measureName) in SettleChain)
            {
                var bone = Find(root, boneName);
                var measure = Find(root, measureName);
                if (bone == null || measure == null) { Debug.Log($"Victim settle {boneName}: missing {(bone == null ? boneName : measureName)}"); continue; }
                // Only the next segment itself (the forearm, not the hand beyond it), so every
                // segment ends up on the floor, not just the furthest one. The head takes its
                // face with it.
                // Each segment rests its own far end on the floor: the upper arm at the elbow,
                // the forearm at the wrist, the thigh at the knee, the shin at the ankle. Hands
                // and feet are measured whole (fingers, toes), the head with its face, and a
                // shoulder by the upper arm it carries.
                var set = measure.name == "Bip01 Head" ? Subtree(measure)
                        : boneName.EndsWith(" Hand") || boneName.EndsWith(" Foot") ? Subtree(bone)
                        : boneName.Contains("Clavicle") ? new System.Collections.Generic.HashSet<int> { Index(measure) }
                        : new System.Collections.Generic.HashSet<int> { Index(bone) };
                Vector3 dir = (measure.position - bone.position).normalized;
                Vector3 axis = Vector3.Cross(dir, Vector3.forward);
                // Pointing straight at the floor (a hand bent down): swing it the way the
                // segment before it points instead.
                if (axis.sqrMagnitude < 0.04f && bone.parent != null)
                    axis = Vector3.Cross((bone.position - bone.parent.position).normalized, Vector3.forward);
                if (axis.sqrMagnitude < 1e-4f) { Debug.Log($"Victim settle {boneName}: no axis"); continue; }
                axis.Normalize();
                var start = bone.rotation;
                float Try(float a) { bone.rotation = Quaternion.AngleAxis(a, axis) * start; return FrontZ(set) - floor; }
                // Which way brings it toward the floor (+z)?
                float sign = Try(4f) > Try(-4f) ? 1f : -1f;
                float gap = Try(0f);
                if (Mathf.Abs(gap) < 0.004f) { bone.rotation = start; Debug.Log($"Victim settle {boneName}: already touching ({gap:F3})"); continue; }
                // Search the swing that makes it just touch: gap below zero = floating.
                // Shoulders only roll so far.
                float limit = boneName.Contains("Clavicle") ? 25f : 70f;
                float lo = 0f, hi = gap < 0f ? limit : -limit;
                for (int it = 0; it < 18; it++)
                {
                    float mid = (lo + hi) / 2f;
                    float g = Try(sign * mid);
                    if ((g < 0f) == (gap < 0f)) lo = mid; else hi = mid;
                }
                float final = Try(sign * (lo + hi) / 2f);
                Debug.Log($"Victim settle {boneName}: gap {gap:F3} -> {final:F3} (swing {sign * (lo + hi) / 2f:F1})");
            }
            Debug.Log($"Victim settle: torso front {floor:F3}, whole body front {FrontZ(Subtree(root)):F3}");
            {
                smr.BakeMesh(baked, true);
                var v = baked.vertices; var m = smr.transform.localToWorldMatrix;
                var worst = new System.Collections.Generic.Dictionary<int, float>();
                for (int i = 0; i < v.Length; i++) { float z = m.MultiplyPoint3x4(v[i]).z; if (z > floor + 0.005f) { worst.TryGetValue(owner[i], out float w); worst[owner[i]] = Mathf.Max(w, z - floor); } }
                foreach (var kv in worst) Debug.Log($"Victim settle: {bones[kv.Key].name} sticks out {kv.Value:F3}");
            }
            Object.DestroyImmediate(baked);
        }

        static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>()) if (t.name == name) return t;
            return null;
        }
    }
}
