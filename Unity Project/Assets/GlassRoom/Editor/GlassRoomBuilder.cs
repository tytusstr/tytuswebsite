using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GlassRoom.Editor
{
    /// <summary>
    /// Builds the glass hall scene: Tools > Glass Room > Build Scene.
    /// A large hall with flat reflective walls and ceiling, one ring light, and a raised
    /// skylight in one corner: its outer sides are the hall walls continuing straight up,
    /// and on its inner sides the ceiling curves up into it.
    /// </summary>
    public static class GlassRoomBuilder
    {
        const string Root = "Assets/GlassRoom";
        const string MaterialDir = Root + "/Materials";
        const string MeshDir = Root + "/Meshes";
        const string ScenePath = "Assets/Scenes/GlassRoom.unity";

        // Hall size in metres.
        const float Width = 32f;
        const float Length = 76f;
        const float Height = 7f;

        // Skylight in the +X/+Z corner. Its opening starts at SkylightX / SkylightZ and runs to the walls.
        const float SkylightX = 2f;
        const float SkylightZ = 14f;
        const float SkylightRise = 4f;
        // Horizontal distance over which the ceiling curves up to the glass.
        const float SkylightCurve = 3.5f;
        // Rounding of the opening's inner corner.
        const float SkylightCorner = 5f;

        // Circular ceiling light in the middle of the hall.
        static readonly Vector2 k_RingCentre = Vector2.zero;
        const float RingRadius = 4.5f;
        const float RingWidth = 0.35f;
        const int RingLights = 8;

        static readonly Color k_WarmWhite = new Color(1f, 0.975f, 0.94f);

        // The scooter waits a few steps ahead and to the right of where you start.
        static readonly Vector3 k_ScooterPosition = new Vector3(3f, 0f, -30f);
        const float ScooterYaw = 20f;
        const int ScooterCount = 4;
        const float ScooterSpacing = 1f;

        /// <summary>
        /// Brings the open scene up to date without rebuilding it, so objects deleted by hand stay
        /// deleted: tidies leftover colliders, updates the skylight curve, adds the scooter.
        /// </summary>
        [MenuItem("Tools/Glass Room/Apply Updates To Open Scene")]
        public static void ApplyUpdates()
        {
            var hall = GameObject.Find("Glass Hall");
            if (hall == null)
            {
                Debug.LogWarning("Open the Glass Room scene first.");
                return;
            }

            TightenPropColliders(hall.transform);

            var curve = hall.transform.Find("Skylight Curve");
            if (curve != null)
                curve.GetComponent<MeshRenderer>().sharedMaterial = SkylightCurveMaterial();

            var probes = GameObject.Find("Reflection Probes");
            if (probes != null && probes.transform.Find(SkylightProbeName) == null)
            {
                var probe = BuildSkylightProbe(probes.transform);
                BakeProbe(probe);
                BakeProbe(probe);
            }

            // The old ride-on car was replaced by the scooter.
            var oldCar = hall.transform.Find("Ride-On Car");
            if (oldCar != null) Object.DestroyImmediate(oldCar.gameObject);
            // Speaker and scooter are updated in place, never rebuilt, so changes made to them by
            // hand in the editor are kept. Each is only created if it's missing.
            var nook = hall.transform.Find("Plants and Seating/Cozy Corner");
            var speaker = Object.FindAnyObjectByType<MusicSpeaker>();
            if (speaker != null) GlassRoomProps.AssignPlaylist(speaker);
            else if (nook != null) GlassRoomProps.AddSpeaker(nook, new Vector3(Width / 2f, 0f, -Length / 2f));

            var original = hall.transform.Find("Scooter") != null ? hall.transform.Find("Scooter").GetComponent<StandUpScooter>() : Object.FindAnyObjectByType<StandUpScooter>();
            if (original == null) original = StandUpScooterBuilder.Build(hall.transform, k_ScooterPosition, ScooterYaw).GetComponent<StandUpScooter>();
            // Four scooters side by side, all at the same slight angle: copies of the original.
            for (int i = 1; i < ScooterCount; i++)
            {
                string name = "Scooter " + (i + 1);
                if (hall.transform.Find(name) != null) continue;
                var copy = Object.Instantiate(original.gameObject, hall.transform);
                copy.name = name;
                copy.transform.SetPositionAndRotation(original.transform.position + new Vector3(ScooterSpacing * i, 0f, 0f), original.transform.rotation);
            }
            MarkerBoardBuilder.Ensure(hall.transform);
            foreach (var seat in Object.FindObjectsByType<Seat>(FindObjectsInactive.Include))
            {
                // A little smaller than imported (they were oversized for a person).
                seat.transform.localScale = Vector3.one * 88f;
                seat.WorkOutFacing();
                seat.recline = 24f;
                // The chair's real shape, not a shrink-wrapped lid over it, so you can stand in the seat.
                foreach (var c in seat.GetComponentsInChildren<MeshCollider>()) { c.convex = false; EditorUtility.SetDirty(c); }
                EditorUtility.SetDirty(seat);
            }
            foreach (var scooter in Object.FindObjectsByType<StandUpScooter>(FindObjectsInactive.Include))
            {
                StandUpScooterBuilder.AssignSounds(scooter);
                StandUpScooterBuilder.AssignColliders(scooter);
                StandUpScooterBuilder.FitGrips(scooter);
                // The saved scene keeps its old value otherwise.
                scooter.humVolume = 0.125f;
                scooter.maxSpeed = 7.8f;
                scooter.crashClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/GlassRoom/Audio/Scooter/Scooter Crash.wav");
                scooter.acceleration = 4.3f;
                scooter.headlightIntensity = 14f; // bright enough to show on the dark floor
                EditorUtility.SetDirty(scooter);
            }

            // Scene objects keep the values they were saved with, so pass on the new defaults.
            var player = Object.FindAnyObjectByType<FirstPersonController>();
            if (player != null)
            {
                var defaults = new GameObject().AddComponent<FirstPersonController>();
                player.walkSpeed = defaults.walkSpeed;
                player.runSpeed = defaults.runSpeed;
                player.walkStride = defaults.walkStride;
                player.runStride = defaults.runStride;
                player.interactRange = defaults.interactRange;
                player.jumpSpeed = defaults.jumpSpeed;
                Object.DestroyImmediate(defaults.gameObject);
                EditorUtility.SetDirty(player);
            }

            // The walls are matte plaster now (GalleryLook), not live mirrors.
            FixFloorJoins(hall.transform);
            FloorMaterial();
            // No skirting boards: the walls meet the floor directly (they run below it).
            var boards = hall.transform.Find("Baseboards");
            if (boards != null) Object.DestroyImmediate(boards.gameObject);
            var ring = hall.transform.Find("Ring Light");
            if (ring != null)
            {
                var rp = ring.localPosition;
                ring.localPosition = new Vector3(rp.x, Height - RingBelowCeiling, rp.z);
                EditorUtility.SetDirty(ring);
                // The ring is a light source: it glows on its own and isn't lit by the lights
                // around it. Lit, the spotlights a centimetre away gave it values so extreme
                // that browsers dropped pieces of it, which looked like blocks and dashes.
                // Kept out of static batching (merging of non-moving objects at startup), which
                // left pieces of the ring missing while the game runs.
                var ringFlags = GameObjectUtility.GetStaticEditorFlags(ring.gameObject);
                GameObjectUtility.SetStaticEditorFlags(ring.gameObject, ringFlags & ~StaticEditorFlags.BatchingStatic);
                // A round glowing tube instead of a flat ribbon: seen nearly edge-on from across
                // the hall the ribbon was thinner than a pixel and broke into dashes.
                var ringFilter = ring.GetComponent<MeshFilter>();
                if (ringFilter != null) ringFilter.sharedMesh = Save(RingTube());
                var ringRenderer = ring.GetComponent<Renderer>();
                var glowMat = ringRenderer.sharedMaterial;
                if (glowMat != null && glowMat.shader.name != "Universal Render Pipeline/Unlit")
                {
                    Color glow = glowMat.GetColor("_EmissionColor");
                    glowMat.shader = Shader.Find("Universal Render Pipeline/Unlit");
                    glowMat.SetColor("_BaseColor", glow);
                    EditorUtility.SetDirty(glowMat);
                }
            }
            SharpenDepth();
            // Edge smoothing: thin bright shapes like the ring light looked stair-stepped from
            // a distance without it.
            if (player != null)
            {
                var cam = player.GetComponentInChildren<Camera>();
                if (cam != null)
                {
                    // Not too close: a tiny near distance spends most of the depth precision on
                    // the first centimetres, leaving too little to tell surfaces apart across
                    // the hall (the ring light and wall bottoms broke up where they fought).
                    cam.nearClipPlane = NearClip;
                    EditorUtility.SetDirty(cam);
                    var data = cam.GetUniversalAdditionalCameraData();
                    data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                    data.antialiasingQuality = AntialiasingQuality.High;
                    EditorUtility.SetDirty(data);
                }
            }
            TuneLighting();
            GlassRoomProps.MakeSeats(hall.transform);
            if (nook != null) GlassRoomProps.UpgradeLamp(nook);
            if (player != null) GlassRoomProps.AddPlantRustle(player.gameObject);
            if (player != null) GlassRoomProps.AddBloodTracks(player.gameObject);
            // Far corner: diagonally opposite the cozy corner. Rebuilt each time (it's generated).
            var oldScene = hall.transform.Find("Crime Scene");
            if (oldScene != null) Object.DestroyImmediate(oldScene.gameObject);
            var oldSpatter = hall.transform.Find("Wall Spatter");
            if (oldSpatter != null) Object.DestroyImmediate(oldSpatter.gameObject);
            GlassRoomProps.AddCrimeScene(hall.transform, new Vector3(-Width / 2f + 3.2f, 0f, Length / 2f - 3.6f));
            if (speaker != null && nook != null) GlassRoomProps.PlaceOnRug(speaker.gameObject, nook);
            // The gallery look: plaster walls, dark ceiling with downlights, skylight slot.
            GalleryLook.Apply(hall.transform);

            AllowAnyOrientation();

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Glass Room updated.");
        }

        // The four walls become live mirrors (MirrorSurface + the Mirror Wall shader), like the
        // floor, so their reflections are exact instead of a pre-captured approximation.
        static void MakeMirrorWalls(Transform hall)
        {
            string path = Root + "/Materials/Mirror Wall.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("GlassRoom/Mirror Wall"));
                AssetDatabase.CreateAsset(mat, path);
            }
            var walls = hall.Find("Walls");
            if (walls == null) return;
            foreach (string name in new[] { "North Wall", "South Wall", "East Wall", "West Wall" })
            {
                var wall = walls.Find(name);
                if (wall == null) continue;
                wall.GetComponent<MeshRenderer>().sharedMaterial = mat;
                if (wall.GetComponent<MirrorSurface>() == null) wall.gameObject.AddComponent<MirrorSurface>();
                // Combined (batched) walls can't each show their own mirror image.
                var flags = GameObjectUtility.GetStaticEditorFlags(wall.gameObject);
                GameObjectUtility.SetStaticEditorFlags(wall.gameObject, flags & ~StaticEditorFlags.BatchingStatic);
                EditorUtility.SetDirty(wall.gameObject);
            }
        }

        const float NearClip = 0.08f;

        // The browser's default depth buffer is too coarse to tell apart surfaces a few
        // centimetres apart across the hall, so ask every renderer for a 32-bit one.
        static void SharpenDepth()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null) continue;
                data.depthAttachmentFormat = DepthFormat.Depth_32_Stencil_8;
                EditorUtility.SetDirty(data);
            }
            AssetDatabase.SaveAssets();
        }

        const float WallThickness = 0.2f;
        // Walls reach well below the floor. The floor's mirror image is drawn by a camera under
        // the floor: where it looked past the bottom of a wall it saw empty background, which
        // showed as a thin dark seam along the base of every wall. Deep walls fill that view.
        const float WallBelowFloor = 4f;

        // The floor used to stop exactly where the walls began, and the walls stood exactly on
        // the floor's surface: edges in the same place fight over which one shows (like the
        // scooter grips did), leaving glitchy lines and hairline gaps along the bottom of the
        // walls. Now the floor runs on under the walls and the walls reach a little below the
        // floor, so they simply meet.
        static void FixFloorJoins(Transform hall)
        {
            var floor = hall.Find("Floor");
            if (floor != null)
            {
                floor.localScale = new Vector3(Width + 2f * WallThickness, floor.localScale.y, Length + 2f * WallThickness);
                EditorUtility.SetDirty(floor);
            }
            var walls = hall.Find("Walls");
            if (walls == null) return;
            float top = Height + SkylightRise;
            foreach (Transform wall in walls)
            {
                if (!wall.name.EndsWith(" Wall")) continue;
                var p = wall.localPosition;
                var s = wall.localScale;
                wall.localScale = new Vector3(s.x, top + WallBelowFloor, s.z);
                wall.localPosition = new Vector3(p.x, (top - WallBelowFloor) / 2f, p.z);
                EditorUtility.SetDirty(wall);
            }
        }

        // A slim black skirting board along the bottom of every wall, like a real room: it
        // covers the line where wall meets floor, so no gap or seam can ever show there.
        const float BaseboardHeight = 0.09f, BaseboardDepth = 0.03f;
        static void AddBaseboards(Transform hall)
        {
            var old = hall.Find("Baseboards");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("Baseboards").transform;
            root.SetParent(hall, false);
            var mat = LitMaterial("Baseboard", new Color(0.03f, 0.03f, 0.032f), 0.82f, 0f);
            void Strip(string name, Vector3 centre, Vector3 size)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.transform.SetParent(root, false);
                go.transform.localPosition = centre;
                go.transform.localScale = size;
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic);
            }
            float y = BaseboardHeight / 2f - 0.01f; // a centimetre into the floor
            float h = BaseboardHeight + 0.01f;
            // Centred on each wall's inside face, so half its depth stands out into the room.
            Strip("North Baseboard", new Vector3(0f, y, Length / 2f), new Vector3(Width, h, BaseboardDepth));
            Strip("South Baseboard", new Vector3(0f, y, -Length / 2f), new Vector3(Width, h, BaseboardDepth));
            Strip("East Baseboard", new Vector3(Width / 2f, y, 0f), new Vector3(BaseboardDepth, h, Length));
            Strip("West Baseboard", new Vector3(-Width / 2f, y, 0f), new Vector3(BaseboardDepth, h, Length));
        }

        // All the hall's light comes from the ring in the middle: bright under it, fading out
        // across the hall so it barely reaches the corners, which stay dim. No fill lights, and
        // only a little ambient so nothing is pitch black. The cozy corner keeps its lamp.
        const float RingLightRange = 75f, RingLightIntensity = 22f;
        const float RingLightBelowCeiling = 2.3f;
        static readonly Color k_AmbientSky = new Color(0.16f, 0.165f, 0.18f);
        static readonly Color k_AmbientEquator = new Color(0.13f, 0.135f, 0.15f);
        static readonly Color k_AmbientGround = new Color(0.08f, 0.08f, 0.085f);

        static void TuneLighting()
        {
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (l.name.StartsWith("Fill "))
                {
                    l.enabled = false;
                }
                else if (l.name == "Moonlight")
                {
                    // Meant to light only the trains in the sky, but the renderer (Forward+)
                    // ignores a light's culling mask, so it lit one side of the hall and left
                    // the other wall darker. The trains glow faintly on their own instead.
                    l.enabled = false;
                    var trainBody = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Train Body.mat");
                    if (trainBody != null)
                    {
                        trainBody.EnableKeyword("_EMISSION");
                        trainBody.SetColor("_EmissionColor", new Color(0.07f, 0.075f, 0.09f));
                        EditorUtility.SetDirty(trainBody);
                    }
                }
                else if (l.name.StartsWith("Ring Light "))
                {
                    // Glowing all round (not just down) so the ceiling and walls around the
                    // middle are lit too, hung a little below the ring.
                    l.type = LightType.Point;
                    l.range = RingLightRange;
                    l.intensity = RingLightIntensity;
                    var lp = l.transform.position;
                    l.transform.position = new Vector3(lp.x, Height - RingLightBelowCeiling, lp.z);
                    EditorUtility.SetDirty(l.transform);
                }
                else continue;
                EditorUtility.SetDirty(l);
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = k_AmbientSky;
            RenderSettings.ambientEquatorColor = k_AmbientEquator;
            RenderSettings.ambientGroundColor = k_AmbientGround;
        }

        // Phone builds rotate with the phone: portrait and landscape, either way up.
        static void AllowAnyOrientation()
        {
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = true;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
        }

        // Props used to have one big box each ("<name> Collider") that covered whole leaf
        // spreads and blocked the space around them. Swap those for collision that follows each
        // prop's real shape, and slim the lamp's.
        static void TightenPropColliders(Transform hall)
        {
            foreach (var box in hall.GetComponentsInChildren<BoxCollider>(true))
                if (box.name.EndsWith(" Collider"))
                    Object.DestroyImmediate(box.gameObject);

            foreach (var t in hall.GetComponentsInChildren<Transform>(true))
                if (PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject))
                    GlassRoomProps.AddTightColliders(t.gameObject);

            var lamp = hall.GetComponentInChildren<CapsuleCollider>(true);
            if (lamp != null && lamp.name == "Floor Lamp")
                lamp.radius = GlassRoomProps.LampColliderRadius;
        }

        [MenuItem("Tools/Glass Room/Build Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory(MeshDir);
            var glass = LitMaterial("White Glass", new Color(0.93f, 0.94f, 0.95f), 0.93f, 0.15f);
            var ceiling = LitMaterial("Ceiling", new Color(0.95f, 0.955f, 0.96f), 0.88f, 0f);
            var ring = LitMaterial("Ring Light", Color.white, 0.5f, 0f, k_WarmWhite * 9f);
            var window = WindowMaterial();
            var floor = FloorMaterial();
            var sky = SkyMaterial();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var hall = new GameObject("Glass Hall").transform;
            BuildFloor(hall, floor);
            BuildShell(hall, glass, ceiling, window);
            BuildRing(hall, ring);
            GlassRoomProps.Build(hall, Width, Length);
            StandUpScooterBuilder.Build(hall, k_ScooterPosition, ScooterYaw);
            BuildLights();
            BuildSkyTraffic();
            BuildReflections();
            BuildVolume();
            BuildPlayer();
            ConfigureEnvironment(sky);

            EditorSceneManager.SaveScene(scene, ScenePath);
            BakeReflections();
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            Debug.Log("Glass Room built at " + ScenePath + ". Press Play to walk around.");
        }

        // ---------- Materials ----------

        static Material LoadOrCreate(string name, Shader shader)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            return mat;
        }

        // Specular highlights are off everywhere: the room is lit by hidden lights, and their
        // glints would show up as dots that don't match the visible ring light.
        static Material LitMaterial(string name, Color color, float smoothness, float metallic, Color? emission = null)
        {
            var mat = LoadOrCreate(name, Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_EnvironmentReflections", 1f);
            mat.SetFloat("_SpecularHighlights", 0f);
            mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.black);
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // Clear skylight glass: almost fully see-through, with faint reflections.
        static Material WindowMaterial()
        {
            var mat = LitMaterial("Skylight Glass", new Color(0.85f, 0.9f, 1f, 0.05f), 0.98f, 0f);
            // No probe reflections: several captures meet over the skylight, and their seam
            // showed as a hard line across the glass. Without them it's evenly clear.
            mat.SetFloat("_EnvironmentReflections", 0f);
            mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_Cull", (float)CullMode.Off);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetShaderPassEnabled("DepthOnly", false);
            mat.SetShaderPassEnabled("ShadowCaster", false);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // Shiny grey like the walls, and double-sided. Its reflections come from its own probe
        // (BuildSkylightProbe), which sees soft grey where the sky would be: with the hall's
        // probes it reflected the night sky and read as a hole beside the window.
        internal static Material SkylightCurveMaterial()
        {
            var mat = LitMaterial("Skylight Curve", new Color(0.86f, 0.87f, 0.88f), 0.95f, 0.08f);
            mat.SetFloat("_EnvironmentReflections", 1f);
            mat.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            mat.SetFloat("_Cull", (float)CullMode.Off);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // Dark blue-grey polished concrete (Textures/Polished Concrete *, ambientCG
        // Concrete015, CC0) with a soft, blurred reflection of the room on top.
        static Material FloorMaterial()
        {
            var mat = LoadOrCreate("Floor Glass", Shader.Find("GlassRoom/Planar Glass Floor"));
            // Dark, nearly black blue-grey, with a strong, slightly smeared reflection, like
            // freshly sealed concrete that still looks a little wet.
            mat.SetColor("_BaseColor", new Color(0.17f, 0.19f, 0.235f));
            mat.SetFloat("_Smoothness", 0.9f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_ReflectionStrength", 0.95f);
            mat.SetFloat("_MinReflectivity", 0.3f);
            mat.SetFloat("_FresnelPower", 2.5f);
            mat.SetFloat("_TileSize", 3f);
            mat.SetFloat("_BumpScale", 0.55f);
            mat.SetFloat("_Contrast", 2.4f);
            mat.SetFloat("_WetPatches", 0.85f);
            mat.SetFloat("_RippleStrength", 0.004f);
            mat.SetFloat("_ReflectionBlur", 0.009f);
            string dir = Root + "/Textures/";
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "Polished Concrete Color.jpg"));
            mat.SetTexture("_BumpMap", ImportAsNormalMap(dir + "Polished Concrete Normal.jpg"));
            mat.SetTexture("_RoughnessMap", AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "Polished Concrete Roughness.jpg"));
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Texture2D ImportAsNormalMap(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material SkyMaterial()
        {
            var mat = LoadOrCreate("Night Sky", Shader.Find("GlassRoom/Night Sky"));
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ---------- Geometry ----------

        static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material mat, bool collider = false, bool castShadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.TwoSided : ShadowCastingMode.Off;
            if (collider)
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.BatchingStatic);
            return go;
        }

        static void BuildFloor(Transform hall, Material mat)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(hall, false);
            floor.transform.localPosition = new Vector3(0f, -0.05f, 0f);
            // Runs under the walls (see FixFloorJoins), so floor and wall meet cleanly.
            floor.transform.localScale = new Vector3(Width + 2f * WallThickness, 0.1f, Length + 2f * WallThickness);
            floor.GetComponent<MeshRenderer>().sharedMaterial = mat;
            GameObjectUtility.SetStaticEditorFlags(floor, StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.BatchingStatic);
            // On its own layer so the reflection camera can leave the floor out.
            floor.layer = LayerMask.NameToLayer("Water");
            floor.AddComponent<PlanarReflection>().planeHeight = 0f;
        }

        static void BuildShell(Transform hall, Material walls, Material ceiling, Material window)
        {
            // Four flat walls. They run up to the skylight glass everywhere; above the flat
            // ceiling they're hidden, and in the skylight corner they form its flat sides.
            float top = Height + SkylightRise;
            const float t = 0.2f;
            var shell = new GameObject("Walls").transform;
            shell.SetParent(hall, false);
            Box("North Wall", shell, new Vector3(0f, top / 2f, Length / 2f + t / 2f), new Vector3(Width + 2f * t, top, t), walls);
            Box("South Wall", shell, new Vector3(0f, top / 2f, -Length / 2f - t / 2f), new Vector3(Width + 2f * t, top, t), walls);
            Box("East Wall", shell, new Vector3(Width / 2f + t / 2f, top / 2f, 0f), new Vector3(t, top, Length), walls);
            Box("West Wall", shell, new Vector3(-Width / 2f - t / 2f, top / 2f, 0f), new Vector3(t, top, Length), walls);

            SkylightEdge(out var edge, out var towardWindow);

            // Flat ceiling: the whole hall except the skylight corner. A fan from the opposite
            // corner covers it, since every point of that shape is visible from there.
            var outline = new List<Vector3> { new Vector3(Width / 2f, Height, -Length / 2f) };
            foreach (var p in edge) outline.Add(p + Vector3.up * Height);
            outline.Add(new Vector3(-Width / 2f, Height, Length / 2f));
            var ceilingMesh = Fan("Ceiling", new Vector3(-Width / 2f, Height, -Length / 2f), outline);
            Face(ceilingMesh, 0, Vector3.down);
            MeshObject("Ceiling", hall, Save(ceilingMesh), ceiling);

            // The ceiling curving up into the skylight: a soft S-curve along the opening's inner edge.
            var profile = new List<Vector2>();
            for (int k = 0; k <= 20; k++)
            {
                float s = k / 20f;
                profile.Add(new Vector2(SkylightCurve * s, Height + SkylightRise * s * s * (3f - 2f * s)));
            }
            var curveMesh = Sweep("Skylight Curve", edge, towardWindow, profile);
            int mid = edge.Count / 2 * profile.Count + profile.Count / 2;
            Face(curveMesh, mid, (Vector3.down - towardWindow[edge.Count / 2]).normalized);
            MeshObject("Skylight Curve", hall, Save(curveMesh), SkylightCurveMaterial());

            // Glass over the opening, fanned from the hall corner it sits in.
            var glassOutline = new List<Vector3>();
            for (int i = 0; i < edge.Count; i++)
                glassOutline.Add(edge[i] + towardWindow[i] * SkylightCurve + Vector3.up * top);
            var glassMesh = Fan("Skylight Glass", new Vector3(Width / 2f, top, Length / 2f), glassOutline);
            Face(glassMesh, 0, Vector3.down);
            MeshObject("Skylight Glass", hall, Save(glassMesh), window, castShadows: false);

            BuildSkylightFrame(hall, edge, towardWindow, top);
        }

        // A slim white frame around the glass and a grid of thin dividers, so it reads as a window.
        static void BuildSkylightFrame(Transform hall, List<Vector3> edge, List<Vector3> towardWindow, float top)
        {
            var frameMat = LitMaterial("Skylight Frame", new Color(0.94f, 0.945f, 0.95f), 0.6f, 0f);
            var frame = new GameObject("Skylight Frame").transform;
            frame.SetParent(hall, false);

            const float width = 0.22f, depth = 0.18f;
            // Border: a ribbon following the glass edge, with a short inner lip.
            var profile = new List<Vector2>
            {
                new Vector2(SkylightCurve - 0.01f, top),
                new Vector2(SkylightCurve - 0.01f, top - depth),
                new Vector2(SkylightCurve + width, top - depth),
                new Vector2(SkylightCurve + width, top),
            };
            var border = Sweep("Skylight Frame", edge, towardWindow, profile);
            Face(border, edge.Count / 2 * profile.Count + 1, Vector3.down);
            MeshObject("Border", frame, Save(border), frameMat, castShadows: false);

            // Dividers between the inner edge and the walls.
            float minX = SkylightX + SkylightCurve, minZ = SkylightZ + SkylightCurve;
            const float bar = 0.09f;
            for (int i = 1; i <= 3; i++)
            {
                float x = Mathf.Lerp(minX, Width / 2f, i / 4f);
                float z0 = minZ + (x < SkylightX + SkylightCorner ? SkylightCorner : 0f);
                Box($"Divider X {i}", frame, new Vector3(x, top - depth / 2f, (z0 + Length / 2f) / 2f), new Vector3(bar, depth, Length / 2f - z0), frameMat);
            }
            for (int i = 1; i <= 4; i++)
            {
                float z = Mathf.Lerp(minZ, Length / 2f, i / 5f);
                Box($"Divider Z {i}", frame, new Vector3((minX + Width / 2f) / 2f, top - depth / 2f, z), new Vector3(Width / 2f - minX, depth, bar), frameMat);
            }
        }

        // The skylight opening's inner edge at floor level, running from the east wall around a
        // rounded corner to the north wall, with the direction pointing into the opening.
        static void SkylightEdge(out List<Vector3> points, out List<Vector3> towardWindow)
        {
            points = new List<Vector3>();
            towardWindow = new List<Vector3>();
            const int straight = 12, corner = 24;
            float cx = SkylightX + SkylightCorner, cz = SkylightZ + SkylightCorner;

            for (int i = 0; i < straight; i++)
            {
                points.Add(new Vector3(Mathf.Lerp(Width / 2f, cx, i / (float)straight), 0f, SkylightZ));
                towardWindow.Add(Vector3.forward);
            }
            for (int i = 0; i < corner; i++)
            {
                float a = Mathf.Lerp(270f, 180f, i / (float)corner) * Mathf.Deg2Rad;
                var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                points.Add(new Vector3(cx, 0f, cz) + radial * SkylightCorner);
                towardWindow.Add(-radial);
            }
            for (int i = 0; i <= straight; i++)
            {
                points.Add(new Vector3(SkylightX, 0f, Mathf.Lerp(cz, Length / 2f, i / (float)straight)));
                towardWindow.Add(Vector3.right);
            }
        }

        static GameObject Box(string name, Transform parent, Vector3 centre, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.BatchingStatic);
            return go;
        }

        // The tube's centre, 10 cm under the ceiling, so the 7 cm tube hangs just clear of it.
        const float RingBelowCeiling = 0.1f;

        // The ring light as a tube (a torus around the vertical axis): the same radius as before,
        // 7 cm thick, so it looks solid from any angle.
        static Mesh RingTube()
        {
            const int segments = 256, sides = 16;
            const float tube = 0.07f;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (int j = 0; j <= sides; j++)
                {
                    float b = j / (float)sides * Mathf.PI * 2f;
                    Vector3 n = dir * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                    vertices.Add(dir * RingRadius + n * tube);
                    normals.Add(n);
                    uvs.Add(new Vector2(i / (float)segments, j / (float)sides));
                }
            }
            int row = sides + 1;
            for (int i = 0; i < segments; i++)
            for (int j = 0; j < sides; j++)
            {
                int v = i * row + j;
                triangles.AddRange(new[] { v, v + row, v + 1, v + 1, v + row, v + row + 1 });
            }
            var mesh = new Mesh { name = "Ring Light", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            // Faces must point outward; flip if this winding points in.
            var tris = mesh.triangles;
            Vector3 p0 = vertices[tris[0]], p1 = vertices[tris[1]], p2 = vertices[tris[2]];
            if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), normals[tris[0]]) < 0f)
            {
                for (int t = 0; t < tris.Length; t += 3) (tris[t + 1], tris[t + 2]) = (tris[t + 2], tris[t + 1]);
                mesh.triangles = tris;
            }
            return mesh;
        }

        static void BuildRing(Transform hall, Material mat)
        {
            const int segments = 256;
            float inner = RingRadius - RingWidth / 2f, outer = RingRadius + RingWidth / 2f;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                vertices.Add(dir * inner);
                vertices.Add(dir * outer);
                if (i == segments) continue;
                int v = i * 2;
                triangles.AddRange(new[] { v, v + 1, v + 2, v + 1, v + 3, v + 2 });
            }
            var mesh = new Mesh { name = "Ring Light" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            Face(mesh, 0, Vector3.down);
            var go = MeshObject("Ring Light", hall, Save(mesh), mat, castShadows: false);
            go.transform.localPosition = new Vector3(k_RingCentre.x, Height - RingBelowCeiling, k_RingCentre.y);
        }

        // Sweeps an (inset, height) profile along an open path.
        static Mesh Sweep(string name, List<Vector3> path, List<Vector3> inward, List<Vector2> profile)
        {
            int n = path.Count, m = profile.Count;
            var vertices = new Vector3[n * m];
            for (int i = 0; i < n; i++)
            for (int j = 0; j < m; j++)
                vertices[i * m + j] = path[i] + inward[i] * profile[j].x + Vector3.up * profile[j].y;

            var triangles = new List<int>();
            for (int i = 0; i < n - 1; i++)
            for (int j = 0; j < m - 1; j++)
            {
                int a = i * m + j, b = (i + 1) * m + j;
                triangles.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
            }
            var mesh = new Mesh { name = name, vertices = vertices };
            mesh.SetTriangles(triangles, 0);
            return mesh;
        }

        // A flat fan from one point around an outline (the outline is not closed back to the start).
        static Mesh Fan(string name, Vector3 centre, List<Vector3> outline)
        {
            var vertices = new List<Vector3> { centre };
            vertices.AddRange(outline);
            var triangles = new List<int>();
            for (int i = 1; i < vertices.Count - 1; i++)
                triangles.AddRange(new[] { 0, i, i + 1 });
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            return mesh;
        }

        // Makes the mesh face the given way at one vertex, flipping all triangles if needed.
        static void Face(Mesh mesh, int vertex, Vector3 desired)
        {
            mesh.RecalculateNormals();
            if (Vector3.Dot(mesh.normals[vertex], desired) >= 0f)
                return;
            var tris = mesh.triangles;
            for (int t = 0; t < tris.Length; t += 3)
                (tris[t + 1], tris[t + 2]) = (tris[t + 2], tris[t + 1]);
            mesh.triangles = tris;
            mesh.RecalculateNormals();
        }

        static Mesh Save(Mesh mesh)
        {
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            string path = $"{MeshDir}/{mesh.name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // ---------- Lighting ----------

        static void BuildLights()
        {
            var lights = new GameObject("Lights").transform;

            // Downlights spaced around the ring, so light comes from where the ring is.
            for (int i = 0; i < RingLights; i++)
            {
                float a = i / (float)RingLights * Mathf.PI * 2f;
                var l = NewLight($"Ring Light {i}", lights, LightType.Spot,
                    new Vector3(k_RingCentre.x + Mathf.Cos(a) * RingRadius, Height - 0.02f, k_RingCentre.y + Mathf.Sin(a) * RingRadius));
                l.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                l.spotAngle = 170f;
                l.innerSpotAngle = 90f;
                l.range = RingLightRange;
                l.intensity = RingLightIntensity;
                // The hall is empty, so shadows would cost frame time without showing anything.
                l.shadows = LightShadows.None;
            }

            // Soft fill across the hall, standing in for light bouncing off the white surfaces.
            // Kept at mid-height and wide so it lights floor and ceiling evenly, without hotspots.
            foreach (float x in new[] { -8f, 8f })
            for (float z = -26f; z <= 26f; z += 26f)
            {
                var l = NewLight($"Fill {x},{z}", lights, LightType.Point, new Vector3(x, Height * 0.45f, z));
                l.range = 30f;
                l.intensity = 0.45f;
                // Placed and set by TuneLighting.
                l.shadows = LightShadows.None;
            }
        }

        // ---------- Sky traffic ----------

        static void BuildSkyTraffic()
        {
            // Brushed graphite metal; unlike the hall, it keeps highlights so the moonlight glints off it.
            var body = LitMaterial("Train Body", new Color(0.3f, 0.31f, 0.34f), 0.82f, 0.8f, new Color(0.01f, 0.01f, 0.014f));
            body.SetFloat("_SpecularHighlights", 1f);
            body.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
            var strips = new[]
            {
                LitMaterial("Train Lights Cyan", Color.black, 0.5f, 0f, new Color(0.25f, 0.85f, 1f) * 6f),
                LitMaterial("Train Lights Magenta", Color.black, 0.5f, 0f, new Color(1f, 0.25f, 0.75f) * 6f),
                LitMaterial("Train Lights Amber", Color.black, 0.5f, 0f, new Color(1f, 0.62f, 0.25f) * 6f),
                LitMaterial("Train Lights White", Color.black, 0.5f, 0f, new Color(0.9f, 0.92f, 1f) * 5f),
            };
            var head = LitMaterial("Train Headlight", Color.black, 0.5f, 0f, new Color(1f, 0.97f, 0.9f) * 14f);
            var tail = LitMaterial("Train Taillight", Color.black, 0.5f, 0f, new Color(1f, 0.06f, 0.04f) * 9f);

            int layer = LayerMask.NameToLayer("TransparentFX");
            var go = new GameObject("Sky Traffic") { layer = layer };
            var traffic = go.AddComponent<SkyTraffic>();

            // Cool moonlight that lights only the trains, so their bodies show against the night sky
            // without changing the lighting inside the hall.
            var moon = new GameObject("Moonlight") { layer = layer }.AddComponent<Light>();
            moon.transform.SetParent(go.transform, false);
            moon.transform.rotation = Quaternion.Euler(35f, -40f, 0f);
            moon.type = LightType.Directional;
            moon.color = new Color(0.62f, 0.68f, 0.95f);
            moon.intensity = 1.3f;
            moon.shadows = LightShadows.None;
            moon.cullingMask = 1 << layer;
            traffic.bodyMaterial = body;
            traffic.stripMaterials = strips;
            traffic.headlightMaterial = head;
            traffic.taillightMaterial = tail;
            // Lanes above and beyond the skylight, at different heights and speeds, all heading +X.
            traffic.lanes = new[]
            {
                Lane(36f, 30f, 24f, 3, 6, 40f, 110f, 0.9f),
                Lane(52f, 50f, 12f, 4, 8, 30f, 80f, 1f),
                Lane(68f, 22f, 36f, 2, 5, 60f, 160f, 1f),
                Lane(85f, 75f, 8f, 5, 8, 25f, 70f, 1.2f),
                Lane(44f, 95f, 17f, 3, 7, 35f, 100f, 1.1f),
                Lane(110f, 60f, 28f, 4, 7, 50f, 140f, 1.4f),
            };
        }

        static SkyTraffic.Lane Lane(float height, float z, float speed, int minCars, int maxCars, float minGap, float maxGap, float scale) =>
            new SkyTraffic.Lane { height = height, z = z, speed = speed, minCars = minCars, maxCars = maxCars, minGap = minGap, maxGap = maxGap, scale = scale };

        static Light NewLight(string name, Transform parent, LightType type, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var l = go.AddComponent<Light>();
            l.type = type;
            l.color = k_WarmWhite;
            return l;
        }

        // ---------- Reflections ----------

        static void BuildReflections()
        {
            var parent = new GameObject("Reflection Probes").transform;
            var go = new GameObject("Reflection Probe 0");
            go.transform.SetParent(parent, false);
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Custom;
            probe.hdr = true;
            probe.nearClipPlane = 0.05f;
            probe.farClipPlane = 120f;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            FitHallProbe(probe);
            BuildSkylightProbe(parent);
        }

        // One capture for the whole hall, its box exactly the room's inside. Box projection then
        // places every reflection where it belongs on the walls and ceiling. (Captures covering
        // only part of the hall each treated their own box ends as walls, so the reflections on
        // the long walls were projected onto pretend walls halfway down the hall.)
        // The box runs from the floor to the hall's real ceiling (not the top of the skylight,
        // which has its own capture): reflections are projected onto this box, so a box taller
        // than the room put every reflection in the wrong place on the walls.
        static void FitHallProbe(ReflectionProbe probe)
        {
            probe.transform.position = new Vector3(0f, 1.6f, 0f);
            probe.boxProjection = true;
            probe.size = new Vector3(Width, Height, Length);
            probe.center = new Vector3(0f, Height / 2f - 1.6f, 0f);
            probe.blendDistance = 0f;
            probe.resolution = 1024;
        }

        internal const string SkylightProbeName = "Skylight Curve Probe";

        // A small probe around the skylight curve that sees soft grey instead of the night sky,
        // so the shiny curve doesn't reflect a dark hole. Higher importance wins inside its box.
        internal static ReflectionProbe BuildSkylightProbe(Transform parent)
        {
            float top = Height + SkylightRise;
            float minX = SkylightX - 1f, minZ = SkylightZ - 1f;
            var centre = new Vector3((minX + Width / 2f) / 2f, (Height - 2f + top + 0.5f) / 2f, (minZ + Length / 2f) / 2f);
            var go = new GameObject(SkylightProbeName);
            go.transform.SetParent(parent, false);
            go.transform.position = centre;
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Custom;
            probe.boxProjection = true;
            probe.size = new Vector3(Width / 2f - minX, top + 0.5f - (Height - 2f), Length / 2f - minZ);
            probe.blendDistance = 1f;
            probe.importance = 2;
            probe.resolution = 512;
            probe.hdr = true;
            probe.nearClipPlane = 0.05f;
            probe.farClipPlane = 120f;
            probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
            probe.backgroundColor = new Color(0.78f, 0.79f, 0.81f);
            return probe;
        }

        /// <summary>
        /// Re-captures the hall's soft reflections (used by the ceiling, and by the walls on Low
        /// graphics), then saves the scene.
        /// </summary>
        [MenuItem("Tools/Glass Room/Bake Reflections")]
        public static void BakeAllReflections()
        {
            var hall = GameObject.Find("Glass Hall");
            if (hall == null)
            {
                Debug.LogWarning("Open the Glass Room scene first.");
                return;
            }
            // Plants, seating and the lamp stay out of the capture: a capture can only place
            // things correctly that lie on the walls themselves, so they'd show as ghosts in the
            // wrong place. The walls' live mirrors (MirrorSurface) show them exactly.
            var props = hall.transform.Find("Plants and Seating");
            if (props != null)
                foreach (var r in props.GetComponentsInChildren<Renderer>(true))
                {
                    var flags = GameObjectUtility.GetStaticEditorFlags(r.gameObject);
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, flags & ~StaticEditorFlags.ReflectionProbeStatic);
                }
            // One capture for the whole hall (see FitHallProbe); the old part-hall ones are
            // switched off. The skylight's own small capture stays.
            foreach (var probe in Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (probe.name == SkylightProbeName) continue;
                if (probe.name == "Reflection Probe 0") { probe.gameObject.SetActive(true); FitHallProbe(probe); }
                else probe.gameObject.SetActive(false);
                EditorUtility.SetDirty(probe);
                EditorUtility.SetDirty(probe.gameObject);
            }
            BakeReflections();
            // Captures from before the probes saved under Reflections/ are no longer used.
            for (int i = 0; i < 3; i++)
                AssetDatabase.DeleteAsset($"{Root}/Reflection {i}.exr");

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Glass Room reflections baked.");
        }

        // Captures the hall into cubemaps. Baking twice lets reflections show other reflections.
        static void BakeReflections()
        {
            var probes = Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None);
            for (int bounce = 0; bounce < 2; bounce++)
                foreach (var probe in probes)
                    if (!BakeProbe(probe))
                        return;
            AssetDatabase.DeleteAsset(Root + "/GlassRoomReflection.exr");
        }

        internal static bool BakeProbe(ReflectionProbe probe)
        {
            string path = $"{Root}/Reflections/{probe.name}.exr";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (!Lightmapping.BakeReflectionProbe(probe, path))
            {
                Debug.LogWarning("Reflection bake failed.");
                return false;
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Texture>(path);
            return true;
        }

        // ---------- Camera, post and player ----------

        static void BuildVolume()
        {
            string path = Root + "/GlassRoomVolume.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var tone = AddOverride<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.ACES);

            // Only a faint glow right around the ring, so the image stays crisp.
            var bloom = AddOverride<Bloom>(profile);
            bloom.threshold.Override(1.4f);
            bloom.intensity.Override(0.3f);
            bloom.scatter.Override(0.5f);
            bloom.highQualityFiltering.Override(false);
            bloom.tint.Override(new Color(1f, 0.98f, 0.96f));

            var color = AddOverride<ColorAdjustments>(profile);
            color.postExposure.Override(0.3f);
            color.contrast.Override(12f);
            color.saturation.Override(-8f);

            var white = AddOverride<WhiteBalance>(profile);
            white.temperature.Override(-4f);

            AssetDatabase.SaveAssets();

            var go = new GameObject("Post Processing");
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }

        static T AddOverride<T>(VolumeProfile profile) where T : VolumeComponent
        {
            var component = profile.Add<T>(true);
            component.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        static void BuildPlayer()
        {
            var player = new GameObject("Player");
            player.transform.position = new Vector3(0f, 0.02f, -Length / 2f + 5f);
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.3f;
            cc.skinWidth = 0.02f;

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(player.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.65f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 68f;
            cam.nearClipPlane = NearClip;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.allowHDR = true;
            camGo.AddComponent<AudioListener>();

            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;

            var controller = player.AddComponent<FirstPersonController>();
            controller.cameraPivot = camGo.transform;
            controller.footstepClips = LoadFootstepClips();
        }

        // Any audio files placed in Assets/GlassRoom/Audio/Footsteps are used as recorded steps.
        static AudioClip[] LoadFootstepClips()
        {
            const string folder = Root + "/Audio/Footsteps";
            if (!AssetDatabase.IsValidFolder(folder))
                return new AudioClip[0];
            var clips = new List<AudioClip>();
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                // The player pads each clip with silence at runtime, which needs readable samples.
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
                clips.Add(AssetDatabase.LoadAssetAtPath<AudioClip>(path));
            }
            return clips.ToArray();
        }

        static void ConfigureEnvironment(Material sky)
        {
            RenderSettings.skybox = sky;
            // Interior lighting comes from the hall, not the dark night sky.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = k_AmbientSky;
            RenderSettings.ambientEquatorColor = k_AmbientEquator;
            RenderSettings.ambientGroundColor = k_AmbientGround;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.fog = false;
        }

        static void AddToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path);
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
