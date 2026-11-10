using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GlassRoom.Editor
{
    /// <summary>
    /// Places the Poly Haven plants and seating (CC0, polyhaven.com) in the hall.
    /// Models live in Assets/GlassRoom/Models/&lt;id&gt;/ with their textures beside them.
    /// </summary>
    public static class GlassRoomProps
    {
        const string ModelRoot = "Assets/GlassRoom/Models";

        const string TallPlant = "potted_plant_01";
        const string BushyPlant = "potted_plant_02";
        const string LoungeChair = "mid_century_lounge_chair";

        // Just wider than the lamp's base disc.
        internal const float LampColliderRadius = 0.18f;

        // The plant models are houseplant-sized; a hall this big needs mall-planter sizes.
        static readonly Dictionary<string, float> k_Scale = new Dictionary<string, float>
        {
            { TallPlant, 2.6f }, { BushyPlant, 2.2f },
        };

        // Yaw that turns each model to face +Z (the direction someone sitting on it looks).
        static readonly Dictionary<string, float> k_FrontYaw = new Dictionary<string, float>
        {
            { LoungeChair, 0f },
        };

        public static void Build(Transform hall, float hallWidth, float hallLength)
        {
            var props = new GameObject("Plants and Seating").transform;
            props.SetParent(hall, false);
            float wallX = hallWidth / 2f, wallZ = hallLength / 2f;

            // Just the cozy corner near where you start; the rest of the hall stays open.
            CozyCorner.Build(props, new Vector3(wallX, 0f, -wallZ));
        }

        /// <summary>A warm seating nook in a corner: rug, floor lamp, two lounge chairs and plants.</summary>
        static class CozyCorner
        {
            // corner is the room corner (+X wall, -Z wall); everything is laid out inward from it.
            public static void Build(Transform parent, Vector3 corner)
            {
                var nook = new GameObject("Cozy Corner").transform;
                nook.SetParent(parent, false);
                Vector3 In(float x, float z) => corner + new Vector3(-x, 0f, z);

                Rug(nook, In(3.4f, 3.3f), new Vector2(3.4f, 2.6f), -12f);
                // Two chairs angled toward each other, both looking out into the hall.
                Spawn(LoungeChair, nook, In(4.4f, 2.9f), -10f);
                Spawn(LoungeChair, nook, In(2.5f, 4.4f), -78f);
                FloorLamp(nook, In(1.0f, 1.0f));
                Spawn(TallPlant, nook, In(1.1f, 6.0f), 30f);
                Spawn(BushyPlant, nook, In(5.9f, 1.2f), 200f);
                AddSpeaker(nook, corner);
            }
        }

        const string Boombox = "boombox";

        /// <summary>
        /// A speaker on the floor behind the chairs, playing the playlist. A small coloured glow on
        /// its front shines green while playing and red while paused.
        /// </summary>
        public static void AddSpeaker(Transform nook, Vector3 corner)
        {
            // Between the chairs and the corner, facing out into the hall like the chairs.
            const float facing = -45f;
            var speaker = Spawn(Boombox, nook, corner + new Vector3(-2.4f, 0f, 2.3f), facing);
            if (speaker == null) return;

            // The glow sits just off the front, on the side with the speaker cones, near the top.
            var body = WorldBounds(speaker);
            Vector3 front = FrontOf(speaker, body.center, facing);
            Vector3 side = Vector3.Cross(Vector3.up, front);
            float halfDepth = Mathf.Abs(Vector3.Dot(body.extents, new Vector3(Mathf.Abs(front.x), 0f, Mathf.Abs(front.z))));
            float halfWidth = Mathf.Abs(Vector3.Dot(body.extents, new Vector3(Mathf.Abs(side.x), 0f, Mathf.Abs(side.z))));
            Vector3 spot = body.center + front * (halfDepth + 0.004f) + side * (halfWidth * 0.7f) + Vector3.up * (body.extents.y * 0.6f);

            var glow = new GameObject("Status Glow").AddComponent<Light>();
            glow.transform.SetParent(speaker.transform, false);
            glow.transform.position = spot + front * 0.03f;
            glow.type = LightType.Point;
            glow.range = 0.35f;
            glow.intensity = 0.6f;
            glow.shadows = LightShadows.None;

            var music = speaker.AddComponent<MusicSpeaker>();
            music.statusGlow = glow;
            AssignPlaylist(music);
        }

        const string MusicFolder = "Assets/GlassRoom/Audio/Music";
        // The track the speaker always opens with (matched by name in the Music folder).
        const string OpeningTrack = "Never Gonna Give You Up";

        /// <summary>
        /// Gives the speaker every track in the Music folder: the opening track first, the rest
        /// shuffled after it. Tracks stream from disk instead of all sitting in memory.
        /// </summary>
        public static void AssignPlaylist(MusicSpeaker music)
        {
            var tracks = new List<AudioClip>();
            AudioClip first = null;
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { MusicFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is AudioImporter importer)
                {
                    var settings = importer.defaultSampleSettings;
                    if (settings.loadType != AudioClipLoadType.Streaming)
                    {
                        settings.loadType = AudioClipLoadType.Streaming;
                        importer.defaultSampleSettings = settings;
                        importer.SaveAndReimport();
                    }
                }
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) continue;
                if (first == null && Path.GetFileName(path).Contains(OpeningTrack)) first = clip;
                else tracks.Add(clip);
            }
            music.firstTrack = first;
            music.playlist = tracks.ToArray();
            EditorUtility.SetDirty(music);
        }

        // The direction the speaker's cones face: from the middle of the speaker toward the middle
        // of the part drawn with its "speakers" material. Falls back to the way it was turned.
        static Vector3 FrontOf(GameObject speaker, Vector3 centre, float facing)
        {
            foreach (var filter in speaker.GetComponentsInChildren<MeshFilter>())
            {
                var renderer = filter.GetComponent<Renderer>();
                var mesh = filter.sharedMesh;
                if (renderer == null || mesh == null) continue;
                var mats = renderer.sharedMaterials;
                for (int sub = 0; sub < mats.Length && sub < mesh.subMeshCount; sub++)
                {
                    if (mats[sub] == null || !mats[sub].name.ToLowerInvariant().Contains("speakers")) continue;
                    var verts = mesh.vertices;
                    Vector3 sum = Vector3.zero;
                    var tris = mesh.GetTriangles(sub);
                    foreach (int i in tris) sum += filter.transform.TransformPoint(verts[i]);
                    Vector3 cones = sum / Mathf.Max(1, tris.Length);
                    Vector3 dir = Vector3.ProjectOnPlane(cones - centre, Vector3.up);
                    if (dir.sqrMagnitude > 1e-6f)
                    {
                        // Snap to the way it was turned (or the opposite way) so the light sits flat on the face.
                        Vector3 fwd = Quaternion.Euler(0f, facing, 0f) * Vector3.forward;
                        return Vector3.Dot(dir, fwd) >= 0f ? fwd : -fwd;
                    }
                }
            }
            return Quaternion.Euler(0f, facing, 0f) * Vector3.forward;
        }

        // ---------- Rug and lamp (built here; Poly Haven has neither) ----------

        static void Rug(Transform parent, Vector3 centre, Vector2 size, float yaw)
        {
            var mat = SimpleMaterial("Rug", Color.white, 0.05f, 0f);
            mat.SetTexture("_BaseMap", RugTexture());
            mat.SetFloat("_EnvironmentReflections", 0f);
            mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");

            var rug = Primitive(PrimitiveType.Cube, "Rug", parent, mat);
            rug.transform.position = centre + Vector3.up * 0.006f;
            rug.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            rug.transform.localScale = new Vector3(size.x, 0.012f, size.y);
        }

        // A woven-looking rug: cream field, terracotta and sage border bands, a soft diamond lattice.
        static Texture2D RugTexture()
        {
            const string path = "Assets/GlassRoom/Textures/Rug.png";
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                const int w = 1024, h = 768;
                var cream = new Color(0.86f, 0.8f, 0.7f);
                var terracotta = new Color(0.68f, 0.38f, 0.27f);
                var sage = new Color(0.52f, 0.58f, 0.48f);
                var pixels = new Color32[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)w, v = y / (float)h;
                    float edge = Mathf.Min(Mathf.Min(u, 1f - u) * w, Mathf.Min(v, 1f - v) * h) / h;
                    Color c = cream;
                    if (edge < 0.05f) c = terracotta;
                    else if (edge < 0.065f) c = cream;
                    else if (edge < 0.085f) c = sage;
                    else if (edge < 0.1f) c = cream;
                    else if (edge < 0.11f) c = terracotta;
                    else
                    {
                        // Diamond lattice in the field.
                        float du = Mathf.Abs(Mathf.Repeat(u * 6f + v * 4.5f, 1f) - 0.5f);
                        float dv = Mathf.Abs(Mathf.Repeat(u * 6f - v * 4.5f, 1f) - 0.5f);
                        float line = Mathf.Min(du, dv);
                        if (line < 0.025f) c = Color.Lerp(cream, terracotta, 0.55f);
                    }
                    // Fine weave: alternating threads plus a little irregularity.
                    float weave = ((x + (y / 2)) % 4 < 2 ? 0.97f : 1.02f) * (0.96f + 0.08f * Hash(x, y));
                    c *= weave;
                    pixels[y * w + x] = c;
                }
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.SetPixels32(pixels);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static float Hash(int x, int y) => Mathf.Repeat(Mathf.Sin(x * 12.9898f + y * 78.233f) * 43758.5453f, 1f);

        // Slim floor lamp with a glowing fabric shade and a warm light.
        static void FloorLamp(Transform parent, Vector3 position)
        {
            var metal = SimpleMaterial("Lamp Metal", new Color(0.28f, 0.22f, 0.16f), 0.55f, 0.8f);
            var shade = SimpleMaterial("Lamp Shade", new Color(0.95f, 0.9f, 0.8f), 0.1f, 0f, new Color(1f, 0.8f, 0.55f) * 1.2f);

            var lamp = new GameObject("Floor Lamp").transform;
            lamp.SetParent(parent, false);
            lamp.position = position;

            var baseDisc = Primitive(PrimitiveType.Cylinder, "Base", lamp, metal);
            baseDisc.transform.localPosition = new Vector3(0f, 0.015f, 0f);
            baseDisc.transform.localScale = new Vector3(0.34f, 0.015f, 0.34f);

            var pole = Primitive(PrimitiveType.Cylinder, "Pole", lamp, metal);
            pole.transform.localPosition = new Vector3(0f, 0.81f, 0f);
            pole.transform.localScale = new Vector3(0.025f, 0.78f, 0.025f);

            var drum = Primitive(PrimitiveType.Cylinder, "Shade", lamp, shade);
            drum.transform.localPosition = new Vector3(0f, 1.62f, 0f);
            drum.transform.localScale = new Vector3(0.48f, 0.19f, 0.48f);

            var glow = new GameObject("Light").AddComponent<Light>();
            glow.transform.SetParent(lamp, false);
            glow.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.8f, 0.58f);
            glow.intensity = 2.4f;
            glow.range = 7f;
            glow.shadows = LightShadows.None;

            var blocker = lamp.gameObject.AddComponent<CapsuleCollider>();
            blocker.center = new Vector3(0f, 0.9f, 0f);
            blocker.height = 1.8f;
            blocker.radius = LampColliderRadius;
        }

        static GameObject Primitive(PrimitiveType type, string name, Transform parent, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic);
            return go;
        }

        static Material SimpleMaterial(string name, Color color, float smoothness, float metallic, Color? emission = null)
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
            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value);
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ---------- Spawning ----------

        /// <summary>Places a model standing on the floor, centred on <paramref name="position"/>.</summary>
        static GameObject Spawn(string id, Transform parent, Vector3 position, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelRoot}/{id}/{id}_1k.fbx");
            if (prefab == null)
            {
                Debug.LogWarning($"Model {id} not found.");
                return null;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = id;
            k_FrontYaw.TryGetValue(id, out float front);
            go.transform.rotation = Quaternion.Euler(0f, yaw + front, 0f) * prefab.transform.rotation;
            if (k_Scale.TryGetValue(id, out float scale))
                go.transform.localScale = prefab.transform.localScale * scale;
            ApplyMaterials(id, go);

            var bounds = WorldBounds(go);
            go.transform.position += new Vector3(position.x - bounds.center.x, position.y - bounds.min.y, position.z - bounds.center.z);

            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                // Kept out of the baked wall reflections, which would show them as ghosts in the
                // wrong place; the mirror floor reflects them correctly.
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic);
            }

            AddTightColliders(go);
            return go;
        }

        /// <summary>
        /// Collision that follows the model's real shape: a snug convex shell around each solid
        /// part. Leaves are left out, so a plant only blocks you at its pot.
        /// </summary>
        public static void AddTightColliders(GameObject prop)
        {
            foreach (var filter in prop.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null) continue;
                var renderer = filter.GetComponent<Renderer>();
                bool leaves = false;
                if (renderer != null)
                    foreach (var m in renderer.sharedMaterials)
                        if (m != null && m.name.ToLowerInvariant().Contains("leaves")) leaves = true;
                if (leaves) continue;
                var shell = filter.gameObject.AddComponent<MeshCollider>();
                shell.sharedMesh = filter.sharedMesh;
                shell.convex = true;
            }
        }

        static Bounds WorldBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            var b = renderers.Length > 0 ? renderers[0].bounds : new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        // ---------- Materials ----------

        static readonly string[] k_Maps = { "_diff_1k", "_nor_gl_1k", "_rough_1k", "_metal_1k", "_alpha_1k" };

        // One material per texture set (e.g. "potted_plant_01_leaves"), matched to the model's
        // material slots by name.
        static void ApplyMaterials(string id, GameObject go)
        {
            var materials = BuildMaterials(id);
            if (materials.Count == 0) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var slots = r.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                    slots[i] = Match(materials, slots[i] != null ? slots[i].name : "");
                r.sharedMaterials = slots;
            }
        }

        static Material Match(Dictionary<string, Material> materials, string slotName)
        {
            slotName = slotName.ToLowerInvariant();
            Material best = null;
            int bestScore = -1;
            foreach (var pair in materials)
            {
                // Score by the longest part of the texture-set name found in the slot name.
                int score = 0;
                foreach (string part in pair.Key.Split('_'))
                    if (part.Length > 2 && slotName.Contains(part)) score += part.Length;
                if (score > bestScore) { bestScore = score; best = pair.Value; }
            }
            return best;
        }

        static Dictionary<string, Material> BuildMaterials(string id)
        {
            string folder = $"{ModelRoot}/{id}/textures";
            var sets = new Dictionary<string, Dictionary<string, string>>();
            foreach (string file in Directory.GetFiles(folder))
            {
                if (file.EndsWith(".meta")) continue;
                string name = Path.GetFileNameWithoutExtension(file);
                foreach (string map in k_Maps)
                {
                    int at = name.IndexOf(map);
                    if (at < 0) continue;
                    string set = name.Substring(0, at);
                    if (!sets.ContainsKey(set)) sets[set] = new Dictionary<string, string>();
                    sets[set][map] = file.Replace('\\', '/');
                }
            }

            var result = new Dictionary<string, Material>();
            foreach (var pair in sets)
                result[pair.Key] = BuildMaterial($"{ModelRoot}/{id}/{pair.Key}.mat", pair.Key, pair.Value);
            return result;
        }

        static Material BuildMaterial(string path, string set, Dictionary<string, string> maps)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }

            bool cutout = maps.ContainsKey("_alpha_1k") && maps.ContainsKey("_diff_1k");
            if (maps.TryGetValue("_diff_1k", out string diff))
                mat.SetTexture("_BaseMap", cutout ? CombineAlpha(diff, maps["_alpha_1k"]) : LoadTexture(diff, false));
            mat.SetColor("_BaseColor", Color.white);

            if (maps.TryGetValue("_nor_gl_1k", out string normal))
            {
                mat.SetTexture("_BumpMap", LoadTexture(normal, true));
                mat.EnableKeyword("_NORMALMAP");
            }

            // These texture sets mix metal and non-metal parts in one material, so treating the
            // whole thing as metal made it look nearly black; a light touch keeps the true colours.
            bool metal = maps.ContainsKey("_metal_1k");
            mat.SetFloat("_Metallic", metal ? 0.15f : 0f);
            mat.SetFloat("_Smoothness", metal ? 0.45f : set.Contains("leaves") ? 0.35f : 0.3f);
            // Soft highlights help props look solid; on these rough surfaces they don't show as dots.
            mat.SetFloat("_SpecularHighlights", 1f);
            mat.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");

            if (cutout)
            {
                // Leaves: cut out along the leaf outline and visible from both sides.
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff", 0.45f);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.SetFloat("_Cull", (float)CullMode.Off);
                mat.renderQueue = (int)RenderQueue.AlphaTest;
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Texture2D LoadTexture(string path, bool normalMap)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                var type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (importer.textureType != type)
                {
                    importer.textureType = type;
                    importer.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Poly Haven keeps the leaf outline in a separate image; put it in the colour texture's
        // alpha channel, which is where Unity's material reads it from.
        static Texture2D CombineAlpha(string diffPath, string alphaPath)
        {
            string outPath = diffPath.Replace("_diff_1k", "_diff_alpha_1k");
            outPath = Path.ChangeExtension(outPath, ".png");
            if (!File.Exists(outPath))
            {
                var colour = new Texture2D(2, 2);
                colour.LoadImage(File.ReadAllBytes(diffPath));
                var alpha = new Texture2D(2, 2);
                alpha.LoadImage(File.ReadAllBytes(alphaPath));
                var pixels = colour.GetPixels32();
                var mask = alpha.GetPixels32();
                for (int i = 0; i < pixels.Length && i < mask.Length; i++)
                    pixels[i].a = mask[i].r;
                var combined = new Texture2D(colour.width, colour.height, TextureFormat.RGBA32, false);
                combined.SetPixels32(pixels);
                File.WriteAllBytes(outPath, combined.EncodeToPNG());
                Object.DestroyImmediate(colour);
                Object.DestroyImmediate(alpha);
                Object.DestroyImmediate(combined);
                AssetDatabase.ImportAsset(outPath);
                if (AssetImporter.GetAtPath(outPath) is TextureImporter importer)
                {
                    importer.alphaIsTransparency = true;
                    importer.mipMapsPreserveCoverage = true;
                    importer.alphaTestReferenceValue = 0.45f;
                    importer.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
        }
    }
}
