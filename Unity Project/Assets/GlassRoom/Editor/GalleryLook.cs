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
        const float SlotWidth = 3.2f, SlotStart = -27f, SlotEnd = 27f;
        const float GlassWidth = 1.1f, GlassHeight = 10.8f;
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
            var ceiling = Material("Gallery Ceiling", new Color(0.32f, 0.325f, 0.33f), 0.25f);
            PlasterWalls(hall, plaster);
            Ceiling(root, ceiling);
            Skylight(root, plaster);
            Downlights(root);
            GlassDoors(root);

            // Soft bounced light everywhere (see HallAmbient).
            var ambient = hall.GetComponent<HallAmbient>() ?? hall.gameObject.AddComponent<HallAmbient>();
            ambient.above = new Color(0.26f, 0.28f, 0.32f);
            ambient.sides = new Color(0.2f, 0.21f, 0.24f);
            ambient.below = new Color(0.17f, 0.175f, 0.19f);
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
            var traffic = GameObject.Find("Sky Traffic");
            if (traffic != null) Object.DestroyImmediate(traffic);
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
                if (l.name.StartsWith("Ring Light ") || l.name.StartsWith("Fill "))
                    l.gameObject.SetActive(false);
        }

        // Matte plaster instead of mirrors.
        static void PlasterWalls(Transform hall, Material plaster)
        {
            var walls = hall.Find("Walls");
            if (walls == null) return;
            foreach (Transform wall in walls)
            {
                if (!wall.name.EndsWith(" Wall")) continue;
                var mirror = wall.GetComponent<MirrorSurface>();
                if (mirror != null) Object.DestroyImmediate(mirror);
                var r = wall.GetComponent<MeshRenderer>();
                if (r != null) r.sharedMaterial = plaster;
                EditorUtility.SetDirty(wall.gameObject);
            }
        }

        // A dark flat ceiling at Height everywhere except the skylight slot.
        static void Ceiling(Transform root, Material mat)
        {
            float slotX = Width / 2f - SlotWidth;
            // Main part, west of the slot.
            Box("Ceiling", root, mat, new Vector3((-Width / 2f + slotX) / 2f, Height + 0.1f, 0f), new Vector3(slotX + Width / 2f, 0.2f, Length));
            // Over the slot's ends.
            float southLen = SlotStart + Length / 2f, northLen = Length / 2f - SlotEnd;
            Box("Ceiling South End", root, mat, new Vector3(slotX + SlotWidth / 2f, Height + 0.1f, -Length / 2f + southLen / 2f), new Vector3(SlotWidth, 0.2f, southLen));
            Box("Ceiling North End", root, mat, new Vector3(slotX + SlotWidth / 2f, Height + 0.1f, Length / 2f - northLen / 2f), new Vector3(SlotWidth, 0.2f, northLen));
        }

        // The slot: the ceiling's edge slopes up to a narrow strip of glass against the east
        // wall; above it, bright overcast sky. Daylight comes down through it and washes the wall.
        static void Skylight(Transform root, Material plaster)
        {
            float x0 = Width / 2f - SlotWidth, xg = Width / 2f - GlassWidth, xw = Width / 2f;
            var slot = new GameObject("Skylight Slot").transform;
            slot.SetParent(root, false);

            // Sloped soffit, facing down into the room.
            var soffit = Quad("Soffit", new[]
            {
                new Vector3(x0, Height, SlotStart), new Vector3(xg, GlassHeight, SlotStart),
                new Vector3(xg, GlassHeight, SlotEnd), new Vector3(x0, Height, SlotEnd)
            }, Vector3.Cross(new Vector3(xg - x0, GlassHeight - Height, 0f), Vector3.forward).normalized);
            Place(soffit, slot, plaster);
            // End walls of the slot.
            foreach (float z in new[] { SlotStart, SlotEnd })
            {
                var cap = Polygon(z < 0 ? "Slot End South" : "Slot End North", new[]
                {
                    new Vector3(x0, Height, z), new Vector3(xw, Height, z), new Vector3(xw, Top, z), new Vector3(xg, Top, z), new Vector3(xg, GlassHeight, z)
                }, z < 0 ? Vector3.forward : Vector3.back);
                Place(cap, slot, plaster);
            }
            // The sky seen through the glass: bright, soft, cool white.
            var skyMat = UnlitMaterial("Gallery Sky", new Color(0.86f, 0.91f, 0.98f) * 1.6f);
            var sky = Quad("Sky", new[]
            {
                new Vector3(xg, GlassHeight, SlotStart), new Vector3(xw, GlassHeight, SlotStart),
                new Vector3(xw, GlassHeight, SlotEnd), new Vector3(xg, GlassHeight, SlotEnd)
            }, Vector3.down);
            Place(sky, slot, skyMat, false);
            // Glazing bars across the glass.
            var barMat = Material("Gallery Glazing Bars", new Color(0.2f, 0.2f, 0.21f), 0.4f);
            for (float z = SlotStart; z <= SlotEnd + 0.01f; z += 2f)
                Box("Glazing Bar", slot, barMat, new Vector3((xg + xw) / 2f, GlassHeight - 0.03f, z), new Vector3(GlassWidth, 0.06f, 0.05f));

            // Daylight: a row of soft cool lights in the glass, shining down the wall and onto
            // the floor below, strongest right under the slot.
            // Every 6 m: browsers draw at most 32 lights at a time, all told.
            for (float z = SlotStart + 2f; z <= SlotEnd - 1.9f; z += 6.25f)
            {
                var l = new GameObject("Daylight").AddComponent<Light>();
                l.transform.SetParent(slot, false);
                l.transform.localPosition = new Vector3((xg + xw) / 2f, GlassHeight - 0.15f, z);
                // Tipped a little toward the wall, so it washes down the wall's whole height.
                l.transform.localRotation = Quaternion.Euler(78f, 90f, 0f);
                l.type = LightType.Spot;
                l.color = k_Daylight;
                l.spotAngle = 150f;
                l.innerSpotAngle = 90f;
                l.intensity = 70f;
                l.range = 30f;
                l.shadows = LightShadows.None;
            }
        }

        // Small round recessed downlights in the dark ceiling, each throwing a soft pool of
        // warm-white light onto the floor.
        static void Downlights(Transform root)
        {
            var fixture = UnlitMaterial("Gallery Downlight", k_Downlight * 2.2f);
            var lights = new GameObject("Downlights").transform;
            lights.SetParent(root, false);
            foreach (float x in new[] { -9f, 3f })
            for (float z = -28f; z <= 28.1f; z += 14f)
            {
                var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                disc.name = "Downlight";
                Object.DestroyImmediate(disc.GetComponent<Collider>());
                disc.transform.SetParent(lights, false);
                disc.transform.localPosition = new Vector3(x, Height - 0.005f, z);
                disc.transform.localScale = new Vector3(0.16f, 0.005f, 0.16f);
                var r = disc.GetComponent<MeshRenderer>();
                r.sharedMaterial = fixture;
                r.shadowCastingMode = ShadowCastingMode.Off;
                var l = new GameObject("Downlight Light").AddComponent<Light>();
                l.transform.SetParent(lights, false);
                l.transform.localPosition = new Vector3(x, Height - 0.05f, z);
                l.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                l.type = LightType.Spot;
                l.color = k_Downlight;
                l.spotAngle = 60f;
                l.innerSpotAngle = 18f;
                l.intensity = 55f;
                l.range = 12f;
                l.shadows = LightShadows.None;
            }
        }

        // Glass doors in the far (north) wall with bright daylight outside, spilling in.
        static void GlassDoors(Transform root)
        {
            const float x = 6f, w = 3.4f, h = 3.3f;
            float z = Length / 2f - 0.01f;
            var doors = new GameObject("Glass Doors").transform;
            doors.SetParent(root, false);
            var outside = UnlitMaterial("Gallery Outside", new Color(0.9f, 0.93f, 0.97f) * 1.5f);
            var glow = Quad("Outside", new[]
            {
                new Vector3(x + w / 2f, 0f, z), new Vector3(x - w / 2f, 0f, z),
                new Vector3(x - w / 2f, h, z), new Vector3(x + w / 2f, h, z)
            }, Vector3.back);
            Place(glow, doors, outside, false);
            var frame = Material("Gallery Door Frame", new Color(0.12f, 0.12f, 0.13f), 0.45f);
            float zf = z - 0.03f;
            Box("Door Frame Left", doors, frame, new Vector3(x - w / 2f, h / 2f, zf), new Vector3(0.07f, h, 0.06f));
            Box("Door Frame Right", doors, frame, new Vector3(x + w / 2f, h / 2f, zf), new Vector3(0.07f, h, 0.06f));
            Box("Door Frame Middle", doors, frame, new Vector3(x, h / 2f, zf), new Vector3(0.05f, h, 0.06f));
            Box("Door Frame Top", doors, frame, new Vector3(x, h, zf), new Vector3(w + 0.07f, 0.07f, 0.06f));
            Box("Door Transom", doors, frame, new Vector3(x, 2.5f, zf), new Vector3(w, 0.04f, 0.06f));
            var l = new GameObject("Doorway Daylight").AddComponent<Light>();
            l.transform.SetParent(doors, false);
            l.transform.localPosition = new Vector3(x, 2.4f, z - 0.2f);
            l.transform.localRotation = Quaternion.Euler(28f, 180f, 0f);
            l.type = LightType.Spot;
            l.color = k_Daylight;
            l.spotAngle = 95f;
            l.innerSpotAngle = 35f;
            l.intensity = 20f;
            l.range = 22f;
            l.shadows = LightShadows.None;
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
            for (int i = 0; i < points.Length; i++) uv[i] = new Vector2(points[i].x + points[i].z, points[i].y);
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
