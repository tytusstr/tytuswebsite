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
                var old = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
                if (old != null) { EditorUtility.CopySerialized(mesh, old); mesh = old; }
                else AssetDatabase.CreateAsset(mesh, MeshPath);
                AssetDatabase.SaveAssets();
                return mesh;
            }
            finally { Object.DestroyImmediate(inst); }
        }

        static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>()) if (t.name == name) return t;
            return null;
        }
    }
}
