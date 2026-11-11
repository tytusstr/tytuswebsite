using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GlassRoom.Editor
{
    /// <summary>
    /// Builds the stand-on scooter: a flat rounded deck with a grip pad, four small wheels
    /// underneath, and a pole rising from the front to a T-handlebar. It faces +Z.
    /// </summary>
    public static class StandUpScooterBuilder
    {
        const string SoundDir = "Assets/GlassRoom/Audio/Scooter";

        // Deck size in metres (width, length) and its height above the floor.
        const float DeckWidth = 0.52f, DeckLength = 0.64f, DeckThickness = 0.06f, DeckBottom = 0.06f;
        const float DeckCorner = 0.08f;

        public static GameObject Build(Transform parent, Vector3 position, float yaw)
        {
            var deckMat = Material("Scooter Deck", new Color(0.15f, 0.155f, 0.17f), 0.55f, 0.6f);
            var aluminium = Material("Scooter Aluminium", new Color(0.11f, 0.115f, 0.125f), 0.3f, 0.15f);
            // No highlights or reflections on the pole and handlebar: the floor's mirror sees them
            // from below, where light from the ceiling glances off their sides and showed as a
            // bright white streak. Plain satin charcoal looks the same from every angle.
            aluminium.SetFloat("_SpecularHighlights", 0f);
            aluminium.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            aluminium.SetFloat("_EnvironmentReflections", 0f);
            aluminium.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            var rubber = Material("Scooter Rubber", new Color(0.03f, 0.03f, 0.035f), 0.18f, 0f);
            var screen = Material("Scooter Display", new Color(0.02f, 0.025f, 0.03f), 0.95f, 0f, new Color(0.35f, 0.75f, 1f) * 0.6f);
            var accent = Material("Scooter Light", Color.white, 0.9f, 0f, new Color(0.9f, 0.95f, 1f) * 3f);

            var root = new GameObject("Scooter");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            var t = root.transform;

            float deckTop = DeckBottom + DeckThickness;
            MeshPart("Deck", t, RoundedSlab("Scooter Deck", DeckWidth, DeckLength, DeckCorner, DeckThickness), deckMat, new Vector3(0f, DeckBottom, 0f));
            MeshPart("Grip Pad", t, RoundedSlab("Scooter Grip Pad", DeckWidth - 0.06f, DeckLength - 0.1f, DeckCorner - 0.03f, 0.004f), rubber, new Vector3(0f, deckTop, -0.02f));
            Part(PrimitiveType.Cube, "Front Light", t, accent, new Vector3(0f, DeckBottom + DeckThickness * 0.5f, DeckLength / 2f + 0.001f), new Vector3(DeckWidth - 2f * DeckCorner, 0.012f, 0.004f));

            // Small wheels tucked under the deck, each on a pivot that spins around its axle.
            var wheels = new List<Transform>();
            foreach (float z in new[] { 0.22f, -0.22f })
            foreach (float x in new[] { -0.19f, 0.19f })
            {
                var pivot = new GameObject("Wheel").transform;
                pivot.SetParent(t, false);
                pivot.localPosition = new Vector3(x, 0.05f, z);
                Part(PrimitiveType.Cylinder, "Tyre", pivot, rubber, Vector3.zero, new Vector3(0.1f, 0.02f, 0.1f), new Vector3(0f, 0f, 90f));
                Part(PrimitiveType.Cylinder, "Hub", pivot, aluminium, new Vector3(Mathf.Sign(x) * 0.004f, 0f, 0f), new Vector3(0.05f, 0.018f, 0.05f), new Vector3(0f, 0f, 90f));
                wheels.Add(pivot);
            }

            // Pole from the front of the deck, leaning slightly back toward the rider.
            const float poleLength = 0.92f, lean = 8f;
            var poleBase = new Vector3(0f, deckTop, DeckLength / 2f - 0.07f);
            var poleDir = Quaternion.Euler(-lean, 0f, 0f) * Vector3.up;
            var poleTop = poleBase + poleDir * poleLength;
            Part(PrimitiveType.Cube, "Pole Clamp", t, deckMat, poleBase + Vector3.up * 0.03f, new Vector3(0.08f, 0.06f, 0.07f));
            Part(PrimitiveType.Cylinder, "Pole", t, aluminium, (poleBase + poleTop) / 2f, new Vector3(0.034f, poleLength / 2f, 0.034f), new Vector3(-lean, 0f, 0f));

            // T-handlebar with rubber grips and a small display in the middle.
            const float barWidth = 0.5f;
            Part(PrimitiveType.Cylinder, "Handlebar", t, aluminium, poleTop, new Vector3(0.026f, barWidth / 2f, 0.026f), new Vector3(0f, 0f, 90f));
            foreach (float x in new[] { -1f, 1f })
                Part(PrimitiveType.Cylinder, "Grip", t, rubber, poleTop + Vector3.right * x * (barWidth / 2f - GripInset), new Vector3(0.034f, GripHalfLength, 0.034f), new Vector3(0f, 0f, 90f));
            Part(PrimitiveType.Cube, "Display", t, screen, poleTop + new Vector3(0f, 0.022f, -0.01f), new Vector3(0.07f, 0.014f, 0.045f), new Vector3(-20f, 0f, 0f));

            var scooter = root.AddComponent<StandUpScooter>();
            scooter.wheels = wheels.ToArray();
            scooter.deckHeight = deckTop;
            AssignColliders(scooter);
            AssignSounds(scooter);
            return root;
        }

        /// <summary>
        // Each grip reaches a few millimetres past the end of the bar, so the two ends never sit
        // in the same place (they flickered, fighting over which one shows).
        const float GripHalfLength = 0.063f;
        const float GripInset = 0.057f;

        /// <summary>Fits the grips of a scooter already in the scene (see GripHalfLength).</summary>
        public static void FitGrips(StandUpScooter scooter)
        {
            var bar = scooter.transform.Find("Handlebar");
            if (bar == null) return;
            float halfBar = bar.localScale.y;
            foreach (Transform child in scooter.transform)
            {
                if (child.name != "Grip") continue;
                var p = child.localPosition;
                float side = Mathf.Sign(p.x - bar.localPosition.x);
                child.localPosition = new Vector3(bar.localPosition.x + side * (halfBar - GripInset), p.y, p.z);
                var s = child.localScale;
                child.localScale = new Vector3(s.x, GripHalfLength, s.z);
                EditorUtility.SetDirty(child);
            }
        }

        /// Gives a scooter tight click areas around just its deck, pole and handlebar, plus a
        /// taller box that only stops you walking through it (clicks and taps ignore that box).
        /// Also updates a scooter already in a scene, without touching anything else on it.
        /// </summary>
        public static void AssignColliders(StandUpScooter scooter)
        {
            var t = scooter.transform;
            foreach (var old in t.GetComponents<Collider>()) Object.DestroyImmediate(old);
            foreach (string name in new[] { "Click Areas", "Blocker" })
            {
                var existing = t.Find(name);
                if (existing != null) Object.DestroyImmediate(existing.gameObject);
            }

            // Only as tall as the deck, so you can jump up and stand on the deck. The pole and
            // handlebar have their own solid shapes (the click areas below), so you can stand
            // on those too. (It used to be a 1.2 m tall box: an invisible wall.)
            var blocker = new GameObject("Blocker");
            blocker.layer = 2; // Ignore Raycast: blocks walking, never clicks.
            blocker.transform.SetParent(t, false);
            var box = blocker.AddComponent<BoxCollider>();
            float deckHeight = DeckBottom + DeckThickness + 0.01f;
            box.center = new Vector3(0f, deckHeight / 2f, 0f);
            box.size = new Vector3(DeckWidth + 0.04f, deckHeight, DeckLength + 0.04f);

            var areas = new GameObject("Click Areas").transform;
            areas.SetParent(t, false);

            var deck = new GameObject("Deck").AddComponent<BoxCollider>();
            deck.transform.SetParent(areas, false);
            deck.center = new Vector3(0f, (DeckBottom + DeckThickness) / 2f + 0.005f, 0f);
            deck.size = new Vector3(DeckWidth, DeckBottom + DeckThickness + 0.03f, DeckLength + 0.02f);

            var pole = t.Find("Pole");
            if (pole != null)
            {
                var c = new GameObject("Pole").AddComponent<CapsuleCollider>();
                c.transform.SetParent(areas, false);
                c.transform.SetLocalPositionAndRotation(pole.localPosition, pole.localRotation);
                c.direction = 1;
                c.radius = 0.035f;
                c.height = pole.localScale.y * 2f + 0.07f;
            }
            var bar = t.Find("Handlebar");
            if (bar != null)
            {
                var c = new GameObject("Handlebar").AddComponent<CapsuleCollider>();
                c.transform.SetParent(areas, false);
                c.transform.SetLocalPositionAndRotation(bar.localPosition, bar.localRotation);
                c.direction = 1;
                c.radius = 0.04f;
                c.height = bar.localScale.y * 2f + 0.04f;
            }

            scooter.body = box;
            EditorUtility.SetDirty(scooter);
            EditorUtility.SetDirty(scooter.gameObject);
        }

        /// <summary>Gives a scooter its recorded sounds (also used to update one already in a scene).</summary>
        public static void AssignSounds(StandUpScooter scooter)
        {
            scooter.motorClip = LoadClip("Electric Hum.ogg");
            scooter.stepOnClip = LoadClip("impactSoft_medium_001.ogg");
            scooter.stepOffClip = LoadClip("impactSoft_heavy_002.ogg");
            EditorUtility.SetDirty(scooter);
        }

        static AudioClip LoadClip(string file)
        {
            string path = $"{SoundDir}/{file}";
            // The motor loop is cut from the recording at runtime, which needs readable samples.
            if (AssetImporter.GetAtPath(path) is AudioImporter importer)
            {
                var settings = importer.defaultSampleSettings;
                if (settings.loadType != AudioClipLoadType.DecompressOnLoad || importer.loadInBackground)
                {
                    settings.loadType = AudioClipLoadType.DecompressOnLoad;
                    importer.defaultSampleSettings = settings;
                    importer.loadInBackground = false;
                    importer.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        // A flat slab with rounded corners: top, bottom and sides, each with its own normals.
        static Mesh RoundedSlab(string name, float width, float length, float radius, float thickness)
        {
            const int cornerSteps = 10;
            var outline = new List<Vector3>();
            var normals = new List<Vector3>();
            float hx = width / 2f - radius, hz = length / 2f - radius;
            var centres = new[] { new Vector2(hx, hz), new Vector2(-hx, hz), new Vector2(-hx, -hz), new Vector2(hx, -hz) };
            for (int k = 0; k < 4; k++)
            for (int i = 0; i <= cornerSteps; i++)
            {
                float a = (k + i / (float)cornerSteps) * Mathf.PI / 2f;
                var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                outline.Add(new Vector3(centres[k].x, 0f, centres[k].y) + n * radius);
                normals.Add(n);
            }

            var v = new List<Vector3>();
            var nm = new List<Vector3>();
            var tris = new List<int>();
            int count = outline.Count;

            // Top and bottom: fans from the centre.
            foreach (bool top in new[] { true, false })
            {
                float y = top ? thickness : 0f;
                int centre = v.Count;
                v.Add(new Vector3(0f, y, 0f));
                nm.Add(top ? Vector3.up : Vector3.down);
                for (int i = 0; i < count; i++)
                {
                    v.Add(outline[i] + Vector3.up * y);
                    nm.Add(top ? Vector3.up : Vector3.down);
                }
                for (int i = 0; i < count; i++)
                {
                    int a = centre + 1 + i, b = centre + 1 + (i + 1) % count;
                    if (top) tris.AddRange(new[] { centre, b, a });
                    else tris.AddRange(new[] { centre, a, b });
                }
            }

            // Sides.
            int side = v.Count;
            for (int i = 0; i < count; i++)
            {
                v.Add(outline[i]);
                v.Add(outline[i] + Vector3.up * thickness);
                nm.Add(normals[i]);
                nm.Add(normals[i]);
            }
            for (int i = 0; i < count; i++)
            {
                int a = side + i * 2, b = side + (i + 1) % count * 2;
                tris.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(v);
            mesh.SetNormals(nm);
            mesh.SetTriangles(tris, 0);
            FaceOutward(mesh);
            mesh.RecalculateBounds();
            string path = $"Assets/GlassRoom/Meshes/{name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // Flips any triangle whose winding disagrees with its vertices' normals.
        static void FaceOutward(Mesh mesh)
        {
            var v = mesh.vertices;
            var n = mesh.normals;
            var tris = mesh.triangles;
            for (int t = 0; t < tris.Length; t += 3)
            {
                Vector3 face = Vector3.Cross(v[tris[t + 1]] - v[tris[t]], v[tris[t + 2]] - v[tris[t]]);
                if (Vector3.Dot(face, n[tris[t]]) < 0f)
                    (tris[t + 1], tris[t + 2]) = (tris[t + 2], tris[t + 1]);
            }
            mesh.triangles = tris;
        }

        static void MeshPart(string name, Transform parent, Mesh mesh, Material mat, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        static void Part(PrimitiveType type, string name, Transform parent, Material mat, Vector3 position, Vector3 scale, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        static Material Material(string name, Color color, float smoothness, float metallic, Color? emission = null)
        {
            string path = $"Assets/GlassRoom/Materials/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_SpecularHighlights", 1f);
            mat.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value);
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
