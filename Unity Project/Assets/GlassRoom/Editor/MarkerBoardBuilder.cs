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
