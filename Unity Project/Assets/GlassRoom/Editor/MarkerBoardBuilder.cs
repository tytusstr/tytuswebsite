using UnityEditor;
using UnityEngine;

namespace GlassRoom.Editor
{
    /// <summary>
    /// The marker board on the east wall by the cozy corner: a white writing surface in a thin
    /// aluminium frame, with a tray along the bottom. Made once (Tools > Glass Room > Apply
    /// Updates), then kept; its sounds are filled in each time.
    /// </summary>
    public static class MarkerBoardBuilder
    {
        const string Name = "Marker Board";
        const float Width = 2.2f, Height = 1.1f;
        static readonly Vector3 k_Centre = new Vector3(15.99f, 1.45f, -27f);

        public static void Ensure(Transform hall)
        {
            EnsureLayer("Board");
            var existing = hall.Find(Name);
            var board = existing != null ? existing.GetComponent<MarkerBoard>() : null;
            if (board == null) board = Build(hall);
            if (board.transform.Find("Tray Things") == null) TrayThings(board.transform);
            board.markerClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/GlassRoom/Audio/Board/Marker Writing.wav");
            board.eraseClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/GlassRoom/Audio/Board/Board Erasing.wav");
            EditorUtility.SetDirty(board);
        }

        static MarkerBoard Build(Transform hall)
        {
            var root = new GameObject(Name);
            root.transform.SetParent(hall, false);
            // Its forward points into the wall, so the board faces into the room.
            root.transform.SetPositionAndRotation(k_Centre, Quaternion.LookRotation(Vector3.right, Vector3.up));

            var surfaceMat = Material("Marker Board Surface", new Color(0.97f, 0.97f, 0.96f), 0f, 0.62f);
            var metal = Material("Marker Board Frame", new Color(0.74f, 0.75f, 0.77f), 0.85f, 0.55f);

            var surface = GameObject.CreatePrimitive(PrimitiveType.Quad);
            surface.name = "Surface";
            Object.DestroyImmediate(surface.GetComponent<Collider>());
            surface.transform.SetParent(root.transform, false);
            surface.transform.localPosition = new Vector3(0f, 0f, -0.012f);
            surface.transform.localScale = new Vector3(Width, Height, 1f);
            var surfaceRenderer = surface.GetComponent<Renderer>();
            surfaceRenderer.sharedMaterial = surfaceMat;

            var back = Box(root.transform, "Backing", new Vector3(Width, Height, 0.012f), new Vector3(0f, 0f, -0.005f), metal);
            const float t = 0.03f, d = 0.03f;
            Box(root.transform, "Frame Top", new Vector3(Width + 2 * t, t, d), new Vector3(0f, (Height + t) * 0.5f, -d * 0.5f), metal);
            Box(root.transform, "Frame Bottom", new Vector3(Width + 2 * t, t, d), new Vector3(0f, -(Height + t) * 0.5f, -d * 0.5f), metal);
            Box(root.transform, "Frame Left", new Vector3(t, Height, d), new Vector3(-(Width + t) * 0.5f, 0f, -d * 0.5f), metal);
            Box(root.transform, "Frame Right", new Vector3(t, Height, d), new Vector3((Width + t) * 0.5f, 0f, -d * 0.5f), metal);
            Box(root.transform, "Tray", new Vector3(Width * 0.7f, 0.012f, 0.07f), new Vector3(0f, -(Height * 0.5f + t + 0.006f), -0.035f), metal);
            Box(root.transform, "Tray Lip", new Vector3(Width * 0.7f, 0.025f, 0.006f), new Vector3(0f, -(Height * 0.5f + t - 0.006f), -0.068f), metal);

            // One box to click on (and bump into).
            var collider = root.AddComponent<BoxCollider>();
            collider.size = new Vector3(Width + 0.1f, Height + 0.1f, 0.06f);
            collider.center = new Vector3(0f, 0f, -0.03f);

            var board = root.AddComponent<MarkerBoard>();
            board.width = Width;
            board.height = Height;
            board.surface = surfaceRenderer;
            return board;
        }

        // Lying on the tray, just for looks: three markers (black, blue, red caps) and an eraser
        // felt side down, not quite lined up, the way they'd be left.
        static void TrayThings(Transform board)
        {
            const float t = 0.03f;
            float top = -(Height * 0.5f + t + 0.006f) + 0.006f; // the tray's upper face
            var things = new GameObject("Tray Things").transform;
            things.SetParent(board, false);
            var plastic = Material("Marker Body", new Color(0.93f, 0.93f, 0.91f), 0f, 0.55f);
            var tip = Material("Marker Tip", new Color(0.12f, 0.12f, 0.12f), 0f, 0.2f);
            Color[] caps = { new Color(0.08f, 0.08f, 0.1f), new Color(0.1f, 0.3f, 0.85f), new Color(0.82f, 0.12f, 0.12f) };
            string[] names = { "Black", "Blue", "Red" };
            float[] xs = { 0.40f, 0.45f, 0.53f }, turns = { 2f, -3f, 5f }, depth = { -0.016f, -0.036f, -0.056f };
            const float r = 0.0085f;
            for (int i = 0; i < 3; i++)
            {
                var marker = new GameObject(names[i] + " Marker").transform;
                marker.SetParent(things, false);
                marker.localPosition = new Vector3(xs[i], top + r, depth[i]);
                marker.localRotation = Quaternion.Euler(0f, turns[i], 90f);
                var cap = Material("Marker Cap " + names[i], caps[i], 0f, 0.5f);
                Part(marker, PrimitiveType.Cylinder, new Vector3(r * 2f, 0.045f, r * 2f), new Vector3(0f, 0.01f, 0f), plastic);
                Part(marker, PrimitiveType.Cylinder, new Vector3(r * 2f + 0.001f, 0.018f, r * 2f + 0.001f), new Vector3(0f, -0.052f, 0f), cap);
                Part(marker, PrimitiveType.Cylinder, new Vector3(r * 2f + 0.0005f, 0.004f, r * 2f + 0.0005f), new Vector3(0f, 0.035f, 0f), cap);
                Part(marker, PrimitiveType.Cylinder, new Vector3(0.007f, 0.004f, 0.007f), new Vector3(0f, 0.058f, 0f), tip);
            }
            var eraser = new GameObject("Eraser").transform;
            eraser.SetParent(things, false);
            eraser.localPosition = new Vector3(-0.42f, top, -0.035f);
            eraser.localRotation = Quaternion.Euler(0f, -6f, 0f);
            var felt = Material("Eraser Felt", new Color(0.2f, 0.2f, 0.22f), 0f, 0.05f);
            var wood = Material("Eraser Back", new Color(0.55f, 0.4f, 0.25f), 0f, 0.35f);
            Part(eraser, PrimitiveType.Cube, new Vector3(0.13f, 0.012f, 0.05f), new Vector3(0f, 0.006f, 0f), felt);
            Part(eraser, PrimitiveType.Cube, new Vector3(0.13f, 0.024f, 0.05f), new Vector3(0f, 0.024f, 0f), wood);
        }

        static void Part(Transform parent, PrimitiveType type, Vector3 scale, Vector3 at, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localScale = scale;
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        static GameObject Box(Transform parent, string name, Vector3 size, Vector3 at, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI);
            return go;
        }

        static Material Material(string name, Color color, float metallic, float smoothness)
        {
            string path = "Assets/GlassRoom/Materials/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void EnsureLayer(string name)
        {
            if (LayerMask.NameToLayer(name) >= 0) return;
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
            {
                var p = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(p.stringValue)) { p.stringValue = name; tagManager.ApplyModifiedProperties(); return; }
            }
        }
    }
}
