using UnityEditor;
using UnityEngine;

namespace GlassRoom.Editor
{
    /// <summary>
    /// The murder victim as a body that can be pushed around: the real skinned model (Models/Victim),
    /// posed exactly like the still one it replaces, with a physics part (collider, weight) on the
    /// hips, chest, head, upper and lower arms and legs, joined loosely so the limbs flop freely.
    /// It lies still until something touches it (see VictimBody). Made once by Apply Updates.
    /// </summary>
    public static class VictimRagdollBuilder
    {
        const string Name = "Movable Body";

        // Each part: its bone, the bone it reaches to, its shape and weight, and the part it hangs from.
        enum Shape { Box, Capsule, Sphere }
        static readonly (string bone, string end, Shape shape, float radius, Vector3 box, float mass, string parent, float twist, float swing1, float swing2)[] k_Parts =
        {
            ("Bip01 Pelvis", "Bip01 Spine1", Shape.Box, 0f, new Vector3(0.34f, 0.24f, 0.22f), 12f, null, 0f, 0f, 0f),
            ("Bip01 Spine2", "Bip01 Neck", Shape.Box, 0f, new Vector3(0.36f, 0.32f, 0.22f), 14f, "Bip01 Pelvis", 20f, 25f, 25f),
            ("Bip01 Head", null, Shape.Sphere, 0.11f, Vector3.zero, 5f, "Bip01 Spine2", 45f, 40f, 35f),
            ("Bip01 L UpperArm", "Bip01 L Forearm", Shape.Capsule, 0.055f, Vector3.zero, 2.5f, "Bip01 Spine2", 40f, 80f, 60f),
            ("Bip01 R UpperArm", "Bip01 R Forearm", Shape.Capsule, 0.055f, Vector3.zero, 2.5f, "Bip01 Spine2", 40f, 80f, 60f),
            ("Bip01 L Forearm", "Bip01 L Hand", Shape.Capsule, 0.045f, Vector3.zero, 1.8f, "Bip01 L UpperArm", 50f, 90f, 25f),
            ("Bip01 R Forearm", "Bip01 R Hand", Shape.Capsule, 0.045f, Vector3.zero, 1.8f, "Bip01 R UpperArm", 50f, 90f, 25f),
            ("Bip01 L Thigh", "Bip01 L Calf", Shape.Capsule, 0.08f, Vector3.zero, 8f, "Bip01 Pelvis", 25f, 70f, 40f),
            ("Bip01 R Thigh", "Bip01 R Calf", Shape.Capsule, 0.08f, Vector3.zero, 8f, "Bip01 Pelvis", 25f, 70f, 40f),
            ("Bip01 L Calf", "Bip01 L Foot", Shape.Capsule, 0.06f, Vector3.zero, 4.5f, "Bip01 L Thigh", 20f, 90f, 20f),
            ("Bip01 R Calf", "Bip01 R Foot", Shape.Capsule, 0.06f, Vector3.zero, 4.5f, "Bip01 R Thigh", 20f, 90f, 20f),
        };

        public static void Ensure(Transform hall)
        {
            var scene = hall.Find("Crime Scene");
            if (scene == null) return;
            var still = scene.Find("Body");
            var existing = scene.Find(Name);
            VictimBody body = existing != null ? existing.GetComponent<VictimBody>() : null;
            if (body == null)
            {
                if (still == null) return;
                if (existing != null) Object.DestroyImmediate(existing.gameObject);
                body = Build(scene, still);
            }
            if (still != null) still.gameObject.SetActive(false);
            const string audio = "Assets/GlassRoom/Audio/Body/";
            body.dragClip = AssetDatabase.LoadAssetAtPath<AudioClip>(audio + "Body Drag.wav");
            body.hitClip = AssetDatabase.LoadAssetAtPath<AudioClip>(audio + "Body Hit.wav");
            body.splatClip = AssetDatabase.LoadAssetAtPath<AudioClip>(audio + "Blood Splat.wav");
            body.thudClips = new[]
            {
                AssetDatabase.LoadAssetAtPath<AudioClip>(audio + "Body Thud 1.wav"),
                AssetDatabase.LoadAssetAtPath<AudioClip>(audio + "Body Thud 2.wav"),
                AssetDatabase.LoadAssetAtPath<AudioClip>(audio + "Body Thud 3.wav"),
            };
            body.smearMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/GlassRoom/Materials/Blood smear.mat");
            body.poolMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/GlassRoom/Materials/Blood pool2.mat");
            body.wallMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/GlassRoom/Materials/Blood spray_floor.mat");
            body.stainMaterials = new[]
            {
                AssetDatabase.LoadAssetAtPath<Material>("Assets/GlassRoom/Materials/Blood pool1.mat"),
                AssetDatabase.LoadAssetAtPath<Material>("Assets/GlassRoom/Materials/Blood pool2.mat"),
                AssetDatabase.LoadAssetAtPath<Material>("Assets/GlassRoom/Materials/Blood spray_floor.mat"),
            };
            EditorUtility.SetDirty(body);
        }

        static VictimBody Build(Transform scene, Transform still)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(VictimPose.ModelPath);
            var go = (GameObject)Object.Instantiate(model);
            go.name = Name;
            var animator = go.GetComponent<Animator>();
            if (animator != null) Object.DestroyImmediate(animator);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            // Shapes are worked out standing (the model upright, facing +Z), where the body's
            // width, height and depth line up with the world's axes.
            var twistAxes = new Vector3[k_Parts.Length];
            var swingAxes = new Vector3[k_Parts.Length];
            const string matPath = "Assets/GlassRoom/Materials/Body Physics.physicMaterial";
            var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(matPath);
            if (mat == null)
            {
                mat = new PhysicsMaterial("Body") { dynamicFriction = 0.6f, staticFriction = 0.7f, bounciness = 0f, frictionCombine = PhysicsMaterialCombine.Average };
                AssetDatabase.CreateAsset(mat, matPath);
            }
            for (int i = 0; i < k_Parts.Length; i++)
            {
                var p = k_Parts[i];
                var bone = Find(go.transform, p.bone);
                var end = p.end != null ? Find(go.transform, p.end) : null;
                Vector3 along = end != null ? (end.position - bone.position).normalized : Vector3.up;
                twistAxes[i] = bone.InverseTransformDirection(along);
                swingAxes[i] = bone.InverseTransformDirection(p.bone.Contains("Arm") || p.bone.Contains("Forearm") ? Vector3.forward : Vector3.right);
                Collider c;
                switch (p.shape)
                {
                    case Shape.Box:
                    {
                        var box = bone.gameObject.AddComponent<BoxCollider>();
                        Vector3 centre = end != null ? (bone.position + end.position) * 0.5f : bone.position;
                        box.center = bone.InverseTransformPoint(centre);
                        Vector3 size = bone.InverseTransformVector(p.box);
                        box.size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
                        c = box;
                        break;
                    }
                    case Shape.Sphere:
                    {
                        var sphere = bone.gameObject.AddComponent<SphereCollider>();
                        sphere.radius = p.radius;
                        sphere.center = bone.InverseTransformPoint(bone.position + Vector3.up * 0.09f);
                        c = sphere;
                        break;
                    }
                    default:
                    {
                        var capsule = bone.gameObject.AddComponent<CapsuleCollider>();
                        Vector3 local = bone.InverseTransformPoint(end.position);
                        // Lower legs reach on to the toes; forearms on over the hand.
                        if (p.bone.Contains("Calf")) local *= 1.12f;
                        if (p.bone.Contains("Forearm")) local *= 1.35f;
                        int axis = Mathf.Abs(local.x) > Mathf.Abs(local.y) ? (Mathf.Abs(local.x) > Mathf.Abs(local.z) ? 0 : 2) : (Mathf.Abs(local.y) > Mathf.Abs(local.z) ? 1 : 2);
                        capsule.direction = axis;
                        capsule.radius = p.radius;
                        capsule.height = local.magnitude + p.radius;
                        capsule.center = local * 0.5f;
                        c = capsule;
                        break;
                    }
                }
                c.sharedMaterial = mat;
            }

            // Lying down exactly like the still body.
            VictimPose.PoseSkeleton(go);
            go.transform.SetParent(scene, false);
            go.transform.localPosition = still.localPosition;
            go.transform.localRotation = still.localRotation;
            var skin = go.GetComponentInChildren<SkinnedMeshRenderer>();
            var stillRenderer = still.GetComponent<MeshRenderer>();
            if (stillRenderer != null) skin.sharedMaterials = stillRenderer.sharedMaterials;
            skin.updateWhenOffscreen = true;
            skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            // Weights and joints. Kinematic (still) until something pushes it.
            var bodies = new Rigidbody[k_Parts.Length];
            for (int i = 0; i < k_Parts.Length; i++)
            {
                var bone = Find(go.transform, k_Parts[i].bone);
                var rb = bone.gameObject.AddComponent<Rigidbody>();
                rb.mass = k_Parts[i].mass;
                rb.linearDamping = 0.15f;
                rb.angularDamping = 0.8f;
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.solverIterations = 12;
                bodies[i] = rb;
            }
            for (int i = 0; i < k_Parts.Length; i++)
            {
                var p = k_Parts[i];
                if (p.parent == null) continue;
                var joint = bodies[i].gameObject.AddComponent<CharacterJoint>();
                joint.connectedBody = bodies[System.Array.FindIndex(k_Parts, q => q.bone == p.parent)];
                joint.axis = twistAxes[i];
                joint.swingAxis = swingAxes[i];
                joint.lowTwistLimit = new SoftJointLimit { limit = -p.twist };
                joint.highTwistLimit = new SoftJointLimit { limit = p.twist };
                joint.swing1Limit = new SoftJointLimit { limit = p.swing1 };
                joint.swing2Limit = new SoftJointLimit { limit = p.swing2 };
                joint.enableProjection = true;
                joint.enablePreprocessing = false;
            }
            return go.AddComponent<VictimBody>();
        }

        static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>()) if (t.name == name) return t;
            return null;
        }
    }
}
