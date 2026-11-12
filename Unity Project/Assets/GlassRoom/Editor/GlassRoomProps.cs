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

        /// <summary>
        /// Moves the speaker so it stands entirely on the rug (a little in from its edges) and
        /// sits on the rug's surface instead of the floor. Only moves it as far as needed.
        /// </summary>
        public static void PlaceOnRug(GameObject speaker, Transform nook)
        {
            var rug = nook.Find("Rug");
            if (rug == null) return;
            const float margin = 0.06f;
            float halfX = rug.lossyScale.x / 2f - margin, halfZ = rug.lossyScale.z / 2f - margin;
            var b = WorldBounds(speaker);
            // The speaker's footprint corners, in the rug's own (turned) frame.
            var corners = new[]
            {
                new Vector3(b.min.x, 0f, b.min.z), new Vector3(b.max.x, 0f, b.min.z),
                new Vector3(b.min.x, 0f, b.max.z), new Vector3(b.max.x, 0f, b.max.z)
            };
            float shiftX = 0f, shiftZ = 0f;
            foreach (var c in corners)
            {
                Vector3 local = Quaternion.Inverse(rug.rotation) * (c - rug.position);
                if (local.x > halfX) shiftX = Mathf.Min(shiftX, halfX - local.x);
                if (local.x < -halfX) shiftX = Mathf.Max(shiftX, -halfX - local.x);
                if (local.z > halfZ) shiftZ = Mathf.Min(shiftZ, halfZ - local.z);
                if (local.z < -halfZ) shiftZ = Mathf.Max(shiftZ, -halfZ - local.z);
            }
            Vector3 move = rug.rotation * new Vector3(shiftX, 0f, shiftZ);
            float rugTop = rug.position.y + rug.lossyScale.y / 2f;
            move.y = rugTop - b.min.y;
            speaker.transform.position += move;
            EditorUtility.SetDirty(speaker.transform);
        }

        const string MusicFolder = "Assets/GlassRoom/Audio/Music";
        // The track the speaker always opens with (matched by name in the Music folder).
        const string OpeningTrack = "atmospheric-piano";

        /// <summary>
        /// Gives the speaker every track in the Music folder: the opening track first, the rest
        /// shuffled after it. Tracks stream from disk instead of all sitting in memory.
        /// </summary>
        // A recorded button click (Kenney, CC0) for pressing the speaker's buttons.
        const string ClickSound = "Assets/GlassRoom/Audio/Speaker/Button Click.wav";

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
            music.clickClip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClickSound);
            // Heard over 70% of the original distances (full volume within 7 m, fading to 90 m).
            music.fullVolumeDistance = 4.9f;
            music.maxDistance = 63f;
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
        /// <summary>
        /// Rebuilds the cozy corner's floor lamp in place, realistically: a weighted brass base,
        /// slim pole with a switch collar, socket and glowing bulb, and a tapered drum shade in
        /// real linen (Poly Haven "Rough Linen") that glows warm through the fabric. Lit by a
        /// warm amber bulb light plus a soft pool of light on the floor below.
        /// </summary>
        public static void UpgradeLamp(Transform nook)
        {
            var lamp = nook.Find("Floor Lamp");
            if (lamp == null) return;
            foreach (var name in new[] { "Base", "Pole", "Shade", "Light", "Lamp Parts", "Floor Glow", "Ceiling Glow" })
            {
                var old = lamp.Find(name);
                while (old != null) { Object.DestroyImmediate(old.gameObject); old = lamp.Find(name); }
            }
            var parts = new GameObject("Lamp Parts").transform;
            parts.SetParent(lamp, false);

            var brass = SimpleMaterial("Lamp Brass", new Color(0.62f, 0.47f, 0.27f), 0.68f, 1f);
            var bulbMat = SimpleMaterial("Lamp Bulb", new Color(1f, 0.93f, 0.8f), 0.9f, 0f, new Color(1f, 0.7f, 0.38f) * 5f);
            var linen = LinenShadeMaterial();

            // Base: a low, weighted dome with a rounded edge.
            LatheObject("Base", parts, brass, new[]
            {
                new Vector2(0f, 0.045f), new Vector2(0.05f, 0.044f), new Vector2(0.11f, 0.036f),
                new Vector2(0.155f, 0.024f), new Vector2(0.17f, 0.014f), new Vector2(0.172f, 0.004f), new Vector2(0.168f, 0f)
            }, 64, false);
            // Pole, with a small collar where the turn switch sits, and the socket at the top.
            LatheObject("Pole", parts, brass, new[]
            {
                new Vector2(0.016f, 0.04f), new Vector2(0.013f, 0.06f), new Vector2(0.012f, 1.0f),
                new Vector2(0.02f, 1.0f), new Vector2(0.02f, 1.04f), new Vector2(0.012f, 1.04f),
                new Vector2(0.012f, 1.5f), new Vector2(0.022f, 1.5f), new Vector2(0.022f, 1.56f), new Vector2(0f, 1.56f)
            }, 32, false);
            var knob = Primitive(PrimitiveType.Cylinder, "Switch", parts, brass);
            knob.transform.localPosition = new Vector3(0.028f, 1.02f, 0f);
            knob.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            knob.transform.localScale = new Vector3(0.012f, 0.012f, 0.012f);
            // Bulb, seen from below the shade.
            var bulb = Primitive(PrimitiveType.Sphere, "Bulb", parts, bulbMat);
            bulb.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            bulb.transform.localScale = new Vector3(0.06f, 0.075f, 0.06f);
            // Shade: a drum, a little narrower at the top, open top and bottom, both sides drawn.
            LatheObject("Shade", parts, linen, new[]
            {
                new Vector2(0.205f, 1.79f), new Vector2(0.24f, 1.47f)
            }, 64, true);
            // Thin brass rims at the shade's top and bottom edges.
            LatheObject("Shade Rim Top", parts, brass, new[] { new Vector2(0.207f, 1.795f), new Vector2(0.207f, 1.785f) }, 64, true);
            LatheObject("Shade Rim Bottom", parts, brass, new[] { new Vector2(0.242f, 1.475f), new Vector2(0.242f, 1.465f) }, 64, true);

            // Light, all from the bulb, the way a shaded lamp really lights a room: a bright warm
            // pool spilling out of the open bottom of the shade, a softer cone thrown up out of
            // the open top, and only a dim glow through the linen itself (the shade blocks most
            // of the light sideways). Each cone's width is set by the shade's opening.
            var amber = new Color(1f, 0.62f, 0.3f);
            var bulbAt = new Vector3(0f, 1.6f, 0f);
            float down = 2f * Mathf.Atan2(0.24f, 1.6f - 1.47f) * Mathf.Rad2Deg;   // bottom opening
            float up = 2f * Mathf.Atan2(0.205f, 1.79f - 1.6f) * Mathf.Rad2Deg;    // top opening
            LampLight("Floor Glow", lamp, bulbAt, Quaternion.Euler(90f, 0f, 0f), LightType.Spot, amber, 5f, 5.5f, down);
            LampLight("Ceiling Glow", lamp, bulbAt, Quaternion.Euler(-90f, 0f, 0f), LightType.Spot, amber, 3f, 7.5f, up);
            LampLight("Light", lamp, bulbAt, Quaternion.identity, LightType.Point, amber, 0.9f, 4f, 0f);
            EditorUtility.SetDirty(lamp.gameObject);
        }

        static void LampLight(string name, Transform lamp, Vector3 at, Quaternion rotation, LightType type, Color color, float intensity, float range, float angle)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.transform.SetParent(lamp, false);
            l.transform.localPosition = at;
            l.transform.localRotation = rotation;
            l.type = type;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            if (type == LightType.Spot)
            {
                l.spotAngle = angle;
                l.innerSpotAngle = angle * 0.55f; // soft edge where the shade's rim cuts it off
            }
            l.shadows = LightShadows.None;
        }

        static Material LinenShadeMaterial()
        {
            string tex = "Assets/GlassRoom/Textures/Linen Shade.jpg", nor = "Assets/GlassRoom/Textures/Linen Shade Normal.jpg";
            if (AssetImporter.GetAtPath(nor) is TextureImporter ni && ni.textureType != TextureImporterType.NormalMap)
            {
                ni.textureType = TextureImporterType.NormalMap;
                ni.SaveAndReimport();
            }
            var mat = SimpleMaterial("Lamp Shade Linen", new Color(1f, 0.95f, 0.87f), 0.15f, 0f, new Color(1f, 0.58f, 0.24f) * 1.1f);
            var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(tex);
            mat.SetTexture("_BaseMap", baseMap);
            mat.SetTextureScale("_BaseMap", new Vector2(3f, 1f));
            mat.SetTexture("_EmissionMap", baseMap);
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(nor));
            mat.EnableKeyword("_NORMALMAP");
            mat.SetFloat("_Cull", 0f); // both sides: the inside of the shade shows from below
            mat.doubleSidedGI = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // A round object made by spinning a side profile (radius, height) around the vertical axis.
        static void LatheObject(string name, Transform parent, Material mat, Vector2[] profile, int segments, bool doubleSided)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            int rows = profile.Length;
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                float c = Mathf.Cos(a), sn = Mathf.Sin(a);
                for (int j = 0; j < rows; j++)
                {
                    vertices.Add(new Vector3(profile[j].x * c, profile[j].y, profile[j].x * sn));
                    uvs.Add(new Vector2(i / (float)segments, j / (float)Mathf.Max(1, rows - 1)));
                }
            }
            for (int i = 0; i < segments; i++)
            for (int j = 0; j < rows - 1; j++)
            {
                int v = i * rows + j, n = v + rows;
                triangles.AddRange(new[] { v, v + 1, n, v + 1, n + 1, n });
            }
            // Faces point outward: check on the widest part of the profile, flip if needed.
            int widest = 0;
            for (int j = 0; j < rows - 1; j++) if (profile[j].x + profile[j + 1].x > profile[widest].x + profile[widest + 1].x) widest = j;
            {
                int v = widest, n = v + rows;
                Vector3 p0 = vertices[v], p1 = vertices[v + 1], p2 = vertices[n];
                Vector3 face = Vector3.Cross(p1 - p0, p2 - p0);
                Vector3 centre = (p0 + p1 + p2) / 3f;
                if (Vector3.Dot(face, new Vector3(centre.x, 0f, centre.z)) < 0f)
                    for (int t = 0; t < triangles.Count; t += 3) (triangles[t + 1], triangles[t + 2]) = (triangles[t + 2], triangles[t + 1]);
            }
            var mesh = new Mesh { name = "Lamp " + name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            string path = $"Assets/GlassRoom/Meshes/Lamp {name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            MeshPart(name, parent, mesh, mat, Vector3.zero, 0f);
        }

        static void FloorLamp(Transform parent, Vector3 position)
        {
            var metal = SimpleMaterial("Lamp Metal", new Color(0.28f, 0.22f, 0.16f), 0.55f, 0.8f);
            var shade = SimpleMaterial("Lamp Shade", new Color(0.95f, 0.9f, 0.8f), 0.1f, 0f, new Color(1f, 0.57f, 0.22f) * 1.3f); // amber glow

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
            glow.color = new Color(1f, 0.56f, 0.2f); // warm amber
            glow.intensity = 2.8f;
            glow.range = 7f;
            glow.shadows = LightShadows.None;

            var blocker = lamp.gameObject.AddComponent<CapsuleCollider>();
            blocker.center = new Vector3(0f, 0.9f, 0f);
            blocker.height = 1.8f;
            blocker.radius = LampColliderRadius;
        }

        // ---------- Seats, plant sounds, corner scene ----------

        const string SitSound = "Assets/GlassRoom/Audio/Seating/Sit Down.wav";
        const string RustleFolder = "Assets/GlassRoom/Audio/Plants";

        /// <summary>Lets you sit in each lounge chair (Seat), with the sitting sound.</summary>
        public static void MakeSeats(Transform hall)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(SitSound);
            foreach (var t in hall.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith(LoungeChair) || !PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)) continue;
                var seat = t.GetComponent<Seat>() ?? t.gameObject.AddComponent<Seat>();
                seat.sitClip = clip;
                EditorUtility.SetDirty(seat);
            }
        }

        /// <summary>Gives the player bloody footprints, tyre tracks and squelches (BloodTracks).</summary>
        public static void AddBloodTracks(GameObject player)
        {
            var tracks = player.GetComponent<BloodTracks>() ?? player.AddComponent<BloodTracks>();
            var clips = new List<AudioClip>();
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/GlassRoom/Audio/Blood" }))
                clips.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid)));
            tracks.squelchClips = clips.ToArray();
            // Transparent, lit and glossy like the rest of the blood: prints are wet red at
            // whatever strength each one has, and lit by the room (not glowing in the dark).
            string path = "Assets/GlassRoom/Materials/Blood Print.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = Shader.Find("Universal Render Pipeline/Lit");
            mat.SetFloat("_Smoothness", 0.86f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetFloat("_BlendModePreserveSpecular", 0f);
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 10;
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetColor("_BaseColor", new Color(0.46f, 0.02f, 0.03f, 0.85f));
            EditorUtility.SetDirty(mat);
            tracks.printMaterial = mat;
            EditorUtility.SetDirty(tracks);
        }

        /// <summary>Gives the player the leaf rustles for walking through plants.</summary>
        public static void AddPlantRustle(GameObject player)
        {
            var rustle = player.GetComponent<PlantRustle>() ?? player.AddComponent<PlantRustle>();
            var clips = new List<AudioClip>();
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { RustleFolder }))
                clips.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid)));
            rustle.clips = clips.ToArray();
            EditorUtility.SetDirty(rustle);
        }

        /// <summary>
        /// A murder scene in the far corner (diagonally opposite the cozy corner): a man lying
        /// face down where he fell, head turned, one arm flung past his head, one knee bent out,
        /// in a wet pool of blood that has spread from under his head and chest, with a smear
        /// where he was dragged, spray across the floor and spatter running down the wall.
        /// The body is a real character model (Models/Victim, Microsoft Rocketbox, MIT) posed with
        /// its own skeleton (VictimPose). The blood uses recoloured CC0 stain and spatter
        /// textures (Textures/Blood) on wet, glossy decals. Lit only by the hall itself.
        /// </summary>
        public static void AddCrimeScene(Transform hall, Vector3 corner)
        {
            if (hall.Find("Crime Scene") != null) return;
            var root = new GameObject("Crime Scene").transform;
            root.SetParent(hall, false);
            root.position = corner;
            root.rotation = Quaternion.Euler(0f, 32f, 0f);

            // Blood on the floor, drawn in order (spray under pools, pools under the smear) so
            // overlapping decals never flicker against each other.
            BloodDecal("Blood Spray", root, "spray_floor", new Vector3(0.2f, 0.0022f, 0.55f), 15f, new Vector2(3.2f, 3.2f), 0.85f, 0);
            BloodDecal("Blood Pool", root, "pool1", new Vector3(0.1f, 0.0026f, 0.55f), 0f, new Vector2(2.1f, 2.1f), 0.94f, 1);
            BloodDecal("Blood Pool 2", root, "pool2", new Vector3(-0.15f, 0.003f, -0.35f), 70f, new Vector2(1.5f, 1.5f), 0.94f, 2);
            BloodDecal("Blood Smear", root, "smear", new Vector3(0.75f, 0.0034f, -0.4f), 105f, new Vector2(1.3f, 1.3f), 0.9f, 3);
            BloodDecal("Blood Trail", root, "smear", new Vector3(1.05f, 0.0036f, -1.9f), 200f, new Vector2(1.1f, 2.6f), 0.88f, 4);

            // The body, face down, head in the pool.
            var mesh = VictimPose.Bake();
            if (mesh != null)
            {
                var body = new GameObject("Body");
                body.transform.SetParent(root, false);
                // Standing up the model faces +Z; tipped forward it lies face down, head toward +Z.
                // Lowered until the front-most point touches the floor.
                body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                body.transform.localPosition = new Vector3(0.05f, mesh.bounds.max.z - 0.012f, -1.25f);
                body.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = body.AddComponent<MeshRenderer>();
                r.sharedMaterials = new[] { VictimMaterial("Victim Body", "m027_body"), VictimMaterial("Victim Head", "m027_head") };
                r.shadowCastingMode = ShadowCastingMode.On;
            }

            // (No blood on the walls: only on the floor.)

            // Written in blood beside the body: "dw its / just ketchup" (Textures/Blood Text.png),
            // facing the hall so it reads the right way up as you walk over.
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/GlassRoom/Textures/Blood Text.png");
            if (tex != null)
            {
                var writing = SimpleMaterial("Blood Writing", Color.white, 0.9f, 0f);
                writing.SetTexture("_BaseMap", tex);
                writing.SetFloat("_AlphaClip", 1f);
                writing.SetFloat("_Cutoff", 0.5f);
                writing.EnableKeyword("_ALPHATEST_ON");
                writing.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                var quad = Primitive(PrimitiveType.Quad, "Blood Writing", root, writing);
                quad.transform.localPosition = new Vector3(1.9f, 0.004f, -0.2f);
                quad.transform.localRotation = Quaternion.Euler(90f, -90f, 0f);
                quad.transform.localScale = new Vector3(2.6f, 1.3f, 1f);
            }
        }

        // A flat decal of wet blood lying on the floor (or, turned, on a wall): one of the
        // Textures/Blood maps with its normal map, glossy, see-through at its thin edges.
        static GameObject BloodDecal(string name, Transform parent, string map, Vector3 position, float yaw, Vector2 size, float smoothness, int order)
        {
            string dir = "Assets/GlassRoom/Textures/Blood/";
            string matPath = $"Assets/GlassRoom/Materials/Blood {map}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(dir + $"Blood {map}.png"));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BumpMap", NormalMap(dir + $"Blood {map} normal.png"));
            mat.SetFloat("_BumpScale", 0.35f);
            mat.EnableKeyword("_NORMALMAP");
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", 0f);
            // Transparent, blended by the texture's alpha.
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            // Shine fades with the blood: where the texture is clear, nothing at all is drawn
            // (by default transparent surfaces keep their reflections, showing the whole square).
            mat.SetFloat("_BlendModePreserveSpecular", 0f);
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)RenderQueue.Transparent - 50 + order;
            EditorUtility.SetDirty(mat);

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = new Vector3(size.x, 1f, size.y);
            go.AddComponent<MeshFilter>().sharedMesh = FlatSquare();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        // The character's own textures (Models/Victim/Textures) on a skin/cloth material.
        static Material VictimMaterial(string name, string prefix)
        {
            string dir = "Assets/GlassRoom/Models/Victim/Textures/";
            var mat = SimpleMaterial(name, Color.white, 0.32f, 0f);
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(dir + prefix + "_color.png"));
            mat.SetTexture("_BumpMap", NormalMap(dir + prefix + "_normal.png"));
            mat.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Texture2D NormalMap(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // A 1×1 square lying flat and facing up, centred, with UVs (saved once as an asset).
        static Mesh FlatSquare()
        {
            const string path = "Assets/GlassRoom/Meshes/Flat Square.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            mesh = new Mesh { name = "Flat Square" };
            mesh.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.tangents = new[] { new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }




        static void MeshPart(string name, Transform root, Mesh mesh, Material mat, Vector3 position, float yaw, Vector3? scale = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = scale ?? Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
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
                // Marked as really emitting, or Unity strips the glow back off the material.
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
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
