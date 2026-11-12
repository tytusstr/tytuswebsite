using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GlassRoom.Editor
{
    /// <summary>
    /// The hall as a quiet, dim modern gallery (after a reference the owner picked): matte white
    /// plaster walls, a dark ceiling with small recessed downlights, a dark blue-grey polished
    /// concrete floor with soft reflections, and the main light coming in as cool daylight from
    /// a long skylight slot along the east wall: the ceiling slopes up to a narrow strip of glass
    /// and the daylight washes down the wall. A pair of bright glass doors glows at the far end.
    /// Replaces the old corner skylight, the ring light and the sky trains. The cozy corner, the
    /// scooter and the crime scene are left as they are.
    /// </summary>
    public static class GalleryLook
    {
        const string Root = "Assets/GlassRoom";
        // Hall size (see GlassRoomBuilder).
        const float Width = 32f, Length = 76f, Height = 7f, Top = 11f;
        // The skylight slot: along the east wall, most of the hall's length.
        const float SlotWidth = 4.5f, SlotStart = -27f, SlotEnd = 27f;
        const float GlassWidth = 2f, GlassHeight = 10.6f;
        static readonly Color k_Daylight = new Color(0.8f, 0.88f, 1f);
        static readonly Color k_Downlight = new Color(1f, 0.93f, 0.84f);

        public static void Apply(Transform hall)
        {
            RemoveOld(hall);
            var old = hall.Find("Gallery");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("Gallery").transform;
            root.SetParent(hall, false);

            var plaster = Material("Gallery Plaster", new Color(0.8f, 0.805f, 0.81f), 0.18f);
            var ceiling = Material("Gallery Ceiling", new Color(0.62f, 0.625f, 0.63f), 0.25f);
            var washed = Washed("Gallery Plaster Daylit", 6f, 0f, 0.22f);
            var mirror = MirrorPlaster("Gallery Mirror Plaster", null, 0f);
            var mirrorDaylit = MirrorPlaster("Gallery Mirror Plaster Daylit", washed.GetTexture("_EmissionMap"), 6f * 0.3f);
            var soffitWash = Washed("Gallery Skylight Soffit", 5.2f, Height, 0.35f);
            PlasterWalls(hall, mirror, mirrorDaylit);
            Ceiling(root, ceiling);
            Skylight(root, soffitWash);
            Downlights(root);
            DaytimeSky();
            CloudVideo(root);
            OutsideShell(root);

            // Soft bounced light everywhere (see HallAmbient).
            var ambient = hall.GetComponent<HallAmbient>() ?? hall.gameObject.AddComponent<HallAmbient>();
            ambient.above = new Color(0.26f, 0.28f, 0.32f);
            ambient.sides = new Color(0.2f, 0.21f, 0.24f);
            ambient.below = new Color(0.24f, 0.245f, 0.265f);
            ambient.Apply();
            EditorUtility.SetDirty(ambient);
            CoolGrade();
        }

        // The old look's pieces: the corner skylight and flat white ceiling, the ring light and
        // its lights, and the trains in the night sky with their moonlight.
        static void RemoveOld(Transform hall)
        {
            foreach (string name in new[] { "Ceiling", "Skylight Curve", "Skylight Glass", "Skylight Frame", "Ring Light" })
            {
                var t = hall.Find(name);
                if (t != null) t.gameObject.SetActive(false);
            }
            // The old skylight's own reflection probe still showed the old window's bright
            // reflections on the new ceiling in that corner.
            foreach (var probe in Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include))
                if (probe.name == "Skylight Curve Probe") probe.gameObject.SetActive(false);
            var traffic = GameObject.Find("Sky Traffic");
            if (traffic != null) Object.DestroyImmediate(traffic);
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
                if (l.name.StartsWith("Ring Light ") || l.name.StartsWith("Fill "))
                    l.gameObject.SetActive(false);
        }

        // Plaster walls that reflect the room like satin-polished stone: each is a live mirror
        // (MirrorSurface + the Mirror Wall shader), with the skylight's daylight glow on the
        // east wall.
        static void PlasterWalls(Transform hall, Material plaster, Material daylit)
        {
            var walls = hall.Find("Walls");
            if (walls == null) return;
            foreach (Transform wall in walls)
            {
                if (!wall.name.EndsWith(" Wall")) continue;
                if (wall.GetComponent<MirrorSurface>() == null) wall.gameObject.AddComponent<MirrorSurface>();
                var r = wall.GetComponent<MeshRenderer>();
                if (r != null) r.sharedMaterial = wall.name == "East Wall" ? daylit : plaster;
                EditorUtility.SetDirty(wall.gameObject);
            }
        }

        static Material MirrorPlaster(string name, Texture glow, float glowStrength)
        {
            string path = $"{Root}/Materials/{name}.mat";
            var shader = Shader.Find("GlassRoom/Mirror Wall");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = shader;
            mat.SetColor("_BaseColor", new Color(0.8f, 0.805f, 0.81f));
            mat.SetFloat("_Smoothness", 0.6f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_ReflectionStrength", 0.55f);
            mat.SetFloat("_MinReflectivity", 0.32f);
            mat.SetFloat("_FresnelPower", 2.5f);
            mat.SetTexture("_EmissionMap", glow);
            mat.SetColor("_EmissionColor", glow != null ? Color.white * glowStrength : Color.black);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // A dark flat ceiling at Height everywhere except the skylight slot. Every piece runs
        // right through the walls to their outer faces and overlaps its neighbours, so no seam
        // or gap can ever open where ceiling meets wall or where two pieces meet.
        const float WallT = 0.2f, Overlap = 0.15f;
        static void Ceiling(Transform root, Material mat)
        {
            float slotX = Width / 2f - SlotWidth;
            float x0 = -Width / 2f - WallT, x1 = slotX + Overlap, xe = Width / 2f + WallT;
            float zs = -Length / 2f - WallT, zn = Length / 2f + WallT;
            const float t = 0.4f; // thick, so its edge never looks paper-thin
            float y = Height + t / 2f;
            Box("Ceiling", root, mat, new Vector3((x0 + x1) / 2f, y, 0f), new Vector3(x1 - x0, t, zn - zs));
            float s1 = SlotStart + Overlap, n0 = SlotEnd - Overlap;
            Box("Ceiling South End", root, mat, new Vector3((slotX + xe) / 2f, y, (zs + s1) / 2f), new Vector3(xe - slotX, t, s1 - zs));
            Box("Ceiling North End", root, mat, new Vector3((slotX + xe) / 2f, y, (n0 + zn) / 2f), new Vector3(xe - slotX, t, zn - n0));
        }

        // The slot: the ceiling's edge slopes up to a wide strip of glass against the east wall,
        // with glazing bars, and bright overcast sky above it. From the room you look up the
        // sloped soffit to the glass. The daylight on the wall below is smooth and even (the
        // Washed Plaster shader); a few soft lights in the slot light the floor beneath it.
        static void Skylight(Transform root, Material slotMat)
        {
            float x0 = Width / 2f - SlotWidth, xg = Width / 2f - GlassWidth, xw = Width / 2f + 0.2f;
            const float e = 0.08f; // pieces run a little past each other: no cracks
            var slot = new GameObject("Skylight Slot").transform;
            slot.SetParent(root, false);

            // Sloped soffit, facing down into the room. It starts a little inside the ceiling slab
            // (which runs past the slot's edge), so it rises cleanly out of the slab's edge:
            // no two surfaces lie on top of each other to flicker or leave a crack.
            float slope = (GlassHeight - Height) / (xg - x0);
            float xs = x0 + 0.06f, ys = Height + 0.06f * slope;
            var soffit = Quad("Soffit", new[]
            {
                new Vector3(xs, ys, SlotStart - e), new Vector3(xg + e, GlassHeight + e * slope, SlotStart - e),
                new Vector3(xg + e, GlassHeight + e * slope, SlotEnd + e), new Vector3(xs, ys, SlotEnd + e)
            }, Vector3.Cross(new Vector3(xg - x0, GlassHeight - Height, 0f), Vector3.forward).normalized);
            Place(soffit, slot, slotMat);
            // End walls of the slot, reaching into the east wall and above the glass.
            foreach (float z in new[] { SlotStart, SlotEnd })
            {
                var cap = Polygon(z < 0 ? "Slot End South" : "Slot End North", new[]
                {
                    new Vector3(x0 + 0.1f, Height + 0.2f, z), new Vector3(xw, Height + 0.2f, z), new Vector3(xw, Top, z),
                    new Vector3(xg - e, Top, z), new Vector3(xg - e, GlassHeight + 0.1f, z)
                }, z < 0 ? Vector3.forward : Vector3.back);
                Place(cap, slot, slotMat);
            }
            // Open to the sky: you look straight out through the glass at the daytime sky.
            // Slim dark glazing bars across the glass, and a frame along both long edges.
            var barMat = Material("Gallery Glazing Bars", new Color(0.18f, 0.18f, 0.19f), 0.4f);
            for (float z = SlotStart; z <= SlotEnd + 0.01f; z += 1.8f)
                Box("Glazing Bar", slot, barMat, new Vector3((xg + Width / 2f) / 2f, GlassHeight - 0.04f, z), new Vector3(GlassWidth, 0.08f, 0.05f));
            Box("Glazing Frame Inner", slot, barMat, new Vector3(xg + 0.03f, GlassHeight - 0.04f, 0f), new Vector3(0.07f, 0.08f, SlotEnd - SlotStart));
            Box("Glazing Frame Outer", slot, barMat, new Vector3(Width / 2f - 0.03f, GlassHeight - 0.04f, 0f), new Vector3(0.07f, 0.08f, SlotEnd - SlotStart));

            // Soft daylight onto the floor below the slot, aimed a little out into the room so
            // it doesn't streak the wall (the wall's own daylight is in its shader).
            for (float z = SlotStart + 5f; z <= SlotEnd - 4.9f; z += 11f)
            {
                var l = new GameObject("Daylight").AddComponent<Light>();
                l.transform.SetParent(slot, false);
                l.transform.localPosition = new Vector3(x0 + 1.4f, Height + 2.2f, z);
                l.transform.localRotation = Quaternion.Euler(72f, -90f, 0f);
                l.type = LightType.Spot;
                l.color = k_Daylight;
                l.spotAngle = 130f;
                l.innerSpotAngle = 100f;
                l.intensity = 5f;
                l.range = 16f;
                l.shadows = LightShadows.None;
            }
        }

        // Plaster lit by the skylight. Daylight from a long overcast skylight is a huge soft
        // light: brightest at the top of the wall under the glass, fading smoothly toward the
        // floor, and fading out past the slot's ends. Spotlights can't do that (they leave a row
        // of scallops), so the daylight is painted into the material's glow as a smooth
        // gradient, on Unity's standard lit material (works the same in every browser).
        static Material Washed(string name, float strength, float bottom, float atBottom)
        {
            string path = $"{Root}/Materials/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader.name != "Universal Render Pipeline/Lit")
            {
                if (mat != null) AssetDatabase.DeleteAsset(path);
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            var albedo = new Color(0.8f, 0.805f, 0.81f);
            mat.SetColor("_BaseColor", albedo);
            mat.SetFloat("_Smoothness", 0.18f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_SpecularHighlights", 0f);
            mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            mat.SetTexture("_EmissionMap", DaylightGradient(name, bottom, atBottom, albedo * new Color(0.82f, 0.9f, 1f)));
            mat.SetColor("_EmissionColor", Color.white * strength * 0.3f);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // The gradient, laid out the way the surfaces' texture coordinates run:
        // the east wall (a stretched box) maps its whole face, z across and y up (from the
        // wall's foot, WallBottom, to Top); the slot's soffit and end caps use y directly.
        const float WallBottom = -4f;
        static Texture2D DaylightGradient(string name, float bottom, float atBottom, Color tint)
        {
            const int w = 256, h = 256;
            string path = $"{Root}/Textures/{name} Daylight.png";
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float v = (y + 0.5f) / h, u = (x + 0.5f) / w;
                float worldY = bottom >= Height ? Mathf.Lerp(Height, Top, v) : Mathf.Lerp(WallBottom, Top, v);
                float hgt = Mathf.Clamp01((worldY - bottom) / Mathf.Max(0.01f, GlassHeight - bottom));
                float vertical = Mathf.Lerp(atBottom, 1f, hgt * hgt * (3f - 2f * hgt));
                vertical *= Mathf.Lerp(1f, 1.35f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.75f, 1f, hgt)));
                float along = 1f;
                if (bottom < Height)
                {
                    float z = (u - 0.5f) * Length;
                    along = Mathf.Clamp01(1f - Mathf.Max(SlotStart - z, z - SlotEnd) / 7f);
                    along = along * along * (3f - 2f * along);
                }
                // Stored at half strength so the brightest part still fits (the material's
                // glow colour doubles it back).
                var c = tint * (vertical * along * 0.5f);
                px[y * w + x] = new Color(c.r, c.g, c.b, 1f);
            }
            tex.SetPixels(px);
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Round recessed downlights in the ceiling, each throwing a pool of warm-white light.
        // The lens and its metal ring are drawn from a smooth circle texture with mipmaps, so
        // they stay perfectly round from any distance (a many-sided disc breaks into blocky
        // shapes once it's only a few pixels across).
        static void Downlights(Transform root)
        {
            var circle = CircleTexture();
            var lens = CircleMaterial("Gallery Downlight", circle, k_Downlight * 6f);
            var ring = CircleMaterial("Gallery Downlight Trim", circle, new Color(0.22f, 0.225f, 0.235f));
            var mesh = CircleQuad();
            var lights = new GameObject("Downlights").transform;
            lights.SetParent(root, false);
            foreach (float x in new[] { -9f, 3f })
            for (float z = -28f; z <= 28.1f; z += 14f)
            {
                Disc("Downlight Trim", lights, mesh, ring, new Vector3(x, Height - 0.004f, z), 0.9f);
                Disc("Downlight", lights, mesh, lens, new Vector3(x, Height - 0.008f, z), 0.75f);
                var l = new GameObject("Downlight Light").AddComponent<Light>();
                l.transform.SetParent(lights, false);
                l.transform.localPosition = new Vector3(x, Height - 0.05f, z);
                l.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                l.type = LightType.Spot;
                l.color = k_Downlight;
                l.spotAngle = 80f;
                l.innerSpotAngle = 30f;
                l.intensity = 95f;
                l.range = 12f;
                l.shadows = LightShadows.None;
            }
        }

        static void Disc(string name, Transform parent, Mesh mesh, Material mat, Vector3 at, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localScale = new Vector3(size, 1f, size);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        // A white disc with a soft, smooth edge on a clear background.
        static Texture2D CircleTexture()
        {
            const int n = 256;
            string path = $"{Root}/Textures/Round Light.png";
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01((0.985f - d) * n * 0.5f); // about a pixel of soft edge
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = true;
            imp.filterMode = FilterMode.Trilinear;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // The Round Light shader keeps each disc smooth and steady however far away it is.
        static Material CircleMaterial(string name, Texture2D circle, Color color)
        {
            string path = $"{Root}/Materials/{name}.mat";
            var shader = Shader.Find("GlassRoom/Round Light");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader != shader)
            {
                if (mat != null) AssetDatabase.DeleteAsset(path);
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("_BaseMap", circle);
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_MinPixels", 3f);
            mat.renderQueue = (int)RenderQueue.Transparent + (name.EndsWith("Trim") ? 0 : 1);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // A 1×1 square facing down (seen from below), with texture coordinates.
        static Mesh CircleQuad()
        {
            const string path = Root + "/Meshes/Gallery Downward Square.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            mesh = new Mesh { name = "Gallery Downward Square" };
            mesh.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.normals = new[] { Vector3.down, Vector3.down, Vector3.down, Vector3.down };
            mesh.triangles = new[] { 0, 1, 2, 1, 3, 2 };
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // A plain wall-coloured shell around the outside of the building's sides (not its top,
        // so the skylight still looks out at the sky). The wall mirrors' images are drawn at
        // reduced resolution, and at the corners where two walls meet, their edge pixels
        // picked up whatever lay just past the walls: the bright sky, as a thin line down the
        // corner. Now there's only wall colour there.
        static void OutsideShell(Transform root)
        {
            var mat = UnlitMaterial("Gallery Outside Shell", new Color(0.42f, 0.44f, 0.47f));
            var shell = new GameObject("Outside Shell").transform;
            shell.SetParent(root, false);
            const float gap = 0.6f, t = 0.2f, bottom = -6f, top = Top + 0.2f;
            float h = top - bottom, y = (top + bottom) / 2f;
            float w = Width + 2f * (gap + t) + 0.4f, l = Length + 2f * (gap + t) + 0.4f;
            float ox = Width / 2f + 0.2f + gap + t / 2f, oz = Length / 2f + 0.2f + gap + t / 2f;
            Box("Shell North", shell, mat, new Vector3(0f, y, oz), new Vector3(w, h, t));
            Box("Shell South", shell, mat, new Vector3(0f, y, -oz), new Vector3(w, h, t));
            Box("Shell East", shell, mat, new Vector3(ox, y, 0f), new Vector3(t, h, l));
            Box("Shell West", shell, mat, new Vector3(-ox, y, 0f), new Vector3(t, h, l));
            foreach (var r in shell.GetComponentsInChildren<MeshRenderer>())
                r.shadowCastingMode = ShadowCastingMode.Off;
        }

        // Outside it's daytime: a real sky photo (Textures/Daytime Sky.jpg, Poly Haven, CC0)
        // all round, seen through the skylight's glass.
        static void DaytimeSky()
        {
            string path = $"{Root}/Materials/Daytime Sky.mat";
            var shader = Shader.Find("Skybox/Panoramic");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = shader;
            mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Root}/Textures/Daytime Sky.jpg"));
            mat.SetFloat("_Exposure", 1.15f);
            mat.SetFloat("_Rotation", 40f);
            EditorUtility.SetDirty(mat);
            RenderSettings.skybox = mat;
        }

        // The moving sky: a cloud time-lapse filmed looking straight up (SkyVideo), on a big
        // plane high above the skylight, the way the sky looks from below. Its long side runs
        // along the hall, like the slot.
        static void CloudVideo(Transform root)
        {
            var shader = Shader.Find("GlassRoom/Sky Video");
            string path = $"{Root}/Materials/Sky Video.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
            mat.shader = shader;
            mat.SetFloat("_Exposure", 1.1f);
            mat.SetFloat("_EdgeFade", 0.18f);
            EditorUtility.SetDirty(mat);
            var go = new GameObject("Sky Video");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(Width / 2f - GlassWidth / 2f, 75f, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            go.transform.localScale = new Vector3(190f, 1f, 107f); // 16:9, like the video
            go.AddComponent<MeshFilter>().sharedMesh = CircleQuad();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.AddComponent<SkyVideo>();
        }

        // A slightly cool, subdued grade like the reference.
        static void CoolGrade()
        {
            var volume = Object.FindAnyObjectByType<Volume>();
            if (volume == null || volume.sharedProfile == null) return;
            if (volume.sharedProfile.TryGet(out UnityEngine.Rendering.Universal.WhiteBalance wb))
            {
                wb.active = true;
                wb.temperature.overrideState = true;
                wb.temperature.value = -8f;
            }
            if (volume.sharedProfile.TryGet(out UnityEngine.Rendering.Universal.ColorAdjustments ca))
            {
                ca.active = true;
                ca.saturation.overrideState = true;
                ca.saturation.value = -18f;
            }
            if (volume.sharedProfile.TryGet(out UnityEngine.Rendering.Universal.Bloom bloom))
            {
                bloom.active = true;
                bloom.intensity.overrideState = true;
                bloom.intensity.value = 0.35f;
                bloom.threshold.overrideState = true;
                bloom.threshold.value = 1.1f;
            }
            EditorUtility.SetDirty(volume.sharedProfile);
        }

        // ---------- helpers ----------

        static Material Material(string name, Color color, float smoothness)
        {
            string path = $"{Root}/Materials/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", 0f);
            // Matte: no sharp glints from the lights.
            mat.SetFloat("_SpecularHighlights", smoothness > 0.3f ? 1f : 0f);
            if (smoothness <= 0.3f) mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material UnlitMaterial(string name, Color color)
        {
            string path = $"{Root}/Materials/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void Box(string name, Transform parent, Material mat, Vector3 centre, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.BatchingStatic);
        }

        static Mesh Quad(string name, Vector3[] corners, Vector3 facing) => Polygon(name, corners, facing);

        // A flat convex polygon facing the given way, saved as an asset.
        static Mesh Polygon(string name, Vector3[] points, Vector3 facing)
        {
            var tris = new List<int>();
            for (int i = 1; i < points.Length - 1; i++) tris.AddRange(new[] { 0, i, i + 1 });
            Vector3 n = Vector3.Cross(points[1] - points[0], points[2] - points[0]);
            if (Vector3.Dot(n, facing) < 0f)
                for (int t = 0; t < tris.Count; t += 3) (tris[t + 1], tris[t + 2]) = (tris[t + 2], tris[t + 1]);
            var uv = new Vector2[points.Length];
            // Up the texture = up through the skylight slot (see DaylightGradient).
            for (int i = 0; i < points.Length; i++) uv[i] = new Vector2((points[i].x + points[i].z) * 0.1f, (points[i].y - Height) / (Top - Height));
            var mesh = new Mesh { name = "Gallery " + name };
            mesh.vertices = points;
            mesh.uv = uv;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            string path = $"{Root}/Meshes/Gallery {name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static void Place(Mesh mesh, Transform parent, Material mat, bool shadows = true)
        {
            var go = new GameObject(mesh.name.Replace("Gallery ", ""));
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.BatchingStatic);
        }
    }
}
