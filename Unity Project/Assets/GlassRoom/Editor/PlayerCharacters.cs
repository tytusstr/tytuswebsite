using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace GlassRoom.Editor
{
    /// <summary>
    /// Sets up the people players appear as (Models/Players, Microsoft Rocketbox, MIT): import
    /// settings, materials, one animation state machine (standing / walking / running blended by
    /// speed, and sitting), one prefab per person, and the library the game picks from
    /// (Resources/Player Avatars). Also adds the "Local Body" layer (your own body: shown in
    /// mirrors, hidden from your own camera) and gives the player their body.
    /// </summary>
    public static class PlayerCharacters
    {
        const string Root = "Assets/GlassRoom/Models/Players";
        const string PrefabDir = "Assets/GlassRoom/Prefabs/Players";
        static readonly string[] k_People =
        {
            "Male_Adult_01", "Female_Adult_08", "Male_Adult_08", "Female_Adult_17", "Male_Adult_12",
            "Female_Adult_04", "Male_Adult_17", "Female_Adult_12", "Male_Adult_04", "Male_Adult_16", "Male_Adult_19",
        };

        [MenuItem("Tools/Glass Room/Set Up Player Characters")]
        public static void SetUp()
        {
            EnsureLayer(LocalPlayerBody.LayerName);
            EnsureLayer("Preview"); // the person shown in the settings (CharacterPreview)
            // Men move with the men's recordings, women with the women's.
            var male = BuildController("m", "Male_Adult_01", "Player");
            var female = BuildController("f", "Female_Adult_04", "Player Female");
            Directory.CreateDirectory(PrefabDir);
            var prefabs = new List<PlayerAvatar>();
            foreach (string person in k_People)
            {
                var prefab = BuildPrefab(person, person.StartsWith("Female") ? female : male);
                if (prefab != null) prefabs.Add(prefab);
            }
            Directory.CreateDirectory("Assets/GlassRoom/Resources");
            const string libPath = "Assets/GlassRoom/Resources/Player Avatars.asset";
            var library = AssetDatabase.LoadAssetAtPath<PlayerAvatarLibrary>(libPath);
            if (library == null) { library = ScriptableObject.CreateInstance<PlayerAvatarLibrary>(); AssetDatabase.CreateAsset(library, libPath); }
            library.avatars = prefabs.ToArray();
            EditorUtility.SetDirty(library);

            // The player gets a body; their own camera doesn't draw it.
            var player = Object.FindAnyObjectByType<FirstPersonController>();
            if (player != null)
            {
                if (player.GetComponent<LocalPlayerBody>() == null) player.gameObject.AddComponent<LocalPlayerBody>();
                var cam = player.GetComponentInChildren<Camera>();
                int layer = LayerMask.NameToLayer(LocalPlayerBody.LayerName);
                if (cam != null && layer >= 0) { cam.cullingMask &= ~(1 << layer); EditorUtility.SetDirty(cam); }
                EditorUtility.SetDirty(player.gameObject);
            }
            SetUpMultiplayer();
            AssetDatabase.SaveAssets();
            Debug.Log($"Player characters set up: {prefabs.Count}");
        }

        // The networked stand-in each player gets (Photon Fusion), and the scene's connection.
        static void SetUpMultiplayer()
        {
            const string path = "Assets/GlassRoom/Prefabs/Network Player.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                var go = new GameObject("Network Player");
                go.AddComponent<Fusion.NetworkObject>();
                go.AddComponent<Fusion.NetworkTransform>();
                go.AddComponent<NetworkPlayer>();
                prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
                Object.DestroyImmediate(go);
            }
            var mp = Object.FindAnyObjectByType<Multiplayer>();
            if (mp == null) mp = new GameObject("Multiplayer").AddComponent<Multiplayer>();
            mp.playerPrefab = prefab.GetComponent<Fusion.NetworkObject>();
            EditorUtility.SetDirty(mp);
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
            Debug.LogWarning("No free layer for " + name);
        }

        // The Mixamo recordings (Models/Players/Animations/Mixamo): sidesteps, falling over and
        // getting up. Everyone uses the same ones; Humanoid characters can share recordings.
        const string MixamoDir = Root + "/Animations/Mixamo/";
        // Getting up takes about 2.2 s (FirstPersonController.GetUpTime): from the back
        // ("Getting Up-2", stopped before the arms swing out), and from the front ("Standing Up",
        // started where the lying-still part ends).
        const string GetUpBack = "Getting Up-2", GetUpFront = "Standing Up";

        // Walking, running and sidestepping blend by how you're moving (sideways, forwards in
        // m/s); sitting, falling over and getting up are states of their own.
        static AnimatorController BuildController(string prefix, string sourcePerson, string name)
        {
            string animDir = Root + "/Animations/";
            var source = ConfigureModel(Root + "/" + sourcePerson + "/" + sourcePerson + ".fbx");
            var idle = Clip(animDir + prefix + "_idle_neutral_01.fbx", source, true, true);
            var walk = Clip(animDir + prefix + "_walk_neutral_01.fbx", source, true, false);
            var run = Clip(animDir + prefix + "_run_neutral_01.fbx", source, true, false);
            var sprint = Clip(animDir + prefix + "_run_fast_01.fbx", source, true, false);
            var sit = Clip(animDir + prefix + "_sit_chair_idle_neutral_01.fbx", source, true, true);
            BakeMixamo(sourcePerson, prefix);
            var left = Baked(prefix, "Left Strafe Walk");
            var right = Baked(prefix, "Right Strafe Walk");
            var fallBack = Baked(prefix, "Knocked Out");
            var fallFront = Baked(prefix, "Fall Flat");
            var upBack = Baked(prefix, GetUpBack);
            var upFront = Baked(prefix, GetUpFront);

            string path = "Assets/GlassRoom/Models/Players/" + name + ".controller";
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
            controller.AddParameter("Seated", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Down", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Backwards", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Fall", AnimatorControllerParameterType.Trigger);
            var sm = controller.layers[0].stateMachine;

            var move = controller.CreateBlendTreeInController("Move", out var tree);
            tree.blendType = BlendTreeType.FreeformDirectional2D;
            tree.blendParameter = "MoveX";
            tree.blendParameterY = "MoveZ";
            // Each recording at the speed (m/s) it was recorded moving, played faster or slower
            // to match the game's speeds so the feet don't slide: walk 1.0, jog 2.9, sprint 5.8
            // and sidestep about 1.3 m/s in the recordings; the game walks at 2.1, sprints at 4.5.
            tree.AddChild(idle, new Vector2(0f, 0f));
            tree.AddChild(walk, new Vector2(0f, 1.3f));
            tree.AddChild(run, new Vector2(0f, 2.9f));
            tree.AddChild(sprint, new Vector2(0f, 4.5f));
            tree.AddChild(walk, new Vector2(0f, -2.1f));
            if (left != null) tree.AddChild(left, new Vector2(-2.1f, 0f));
            if (right != null) tree.AddChild(right, new Vector2(2.1f, 0f));
            var children = tree.children;
            children[1].timeScale = 1.3f / 1.01f;
            children[3].timeScale = 0.85f;
            children[4].timeScale = -1.7f;
            for (int i = 5; i < children.Length; i++) children[i].timeScale = 1.5f;
            tree.children = children;
            sm.defaultState = move;

            var seated = sm.AddState("Seated");
            seated.motion = sit;
            var toSit = move.AddTransition(seated);
            toSit.AddCondition(AnimatorConditionMode.If, 0f, "Seated");
            toSit.hasExitTime = false; toSit.duration = 0.25f;
            var toStand = seated.AddTransition(move);
            toStand.AddCondition(AnimatorConditionMode.IfNot, 0f, "Seated");
            toStand.hasExitTime = false; toStand.duration = 0.25f;

            // Knocked over by a scooter: fall (backwards or onto the front), lie there, get up.
            if (fallBack != null && fallFront != null && upBack != null && upFront != null)
            {
                Knockdown(sm, move, "Back", fallBack, 0.3f, upBack, 0f, 0.92f, 0.75f, true);
                Knockdown(sm, move, "Front", fallFront, 0.05f, upFront, 0.42f, 2.3f, 0.86f, false);
            }
            EditorUtility.SetDirty(controller);
            return controller;
        }

        static void Knockdown(AnimatorStateMachine sm, AnimatorState move, string side, AnimationClip fall, float fallStart,
            AnimationClip getUp, float upStart, float upSpeed, float upEnd, bool backwards)
        {
            var falling = sm.AddState("Fall " + side);
            falling.motion = fall;
            falling.cycleOffset = fallStart; // (the recording starts in a boxer's stance)
            var hit = sm.AddAnyStateTransition(falling);
            hit.AddCondition(AnimatorConditionMode.If, 0f, "Fall");
            hit.AddCondition(backwards ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, "Backwards");
            hit.hasExitTime = false;
            hit.duration = 0.12f;
            hit.canTransitionToSelf = true;
            var rising = sm.AddState("Get Up " + side);
            rising.motion = getUp;
            rising.cycleOffset = upStart;
            rising.speed = upSpeed;
            var up = falling.AddTransition(rising);
            up.AddCondition(AnimatorConditionMode.IfNot, 0f, "Down");
            up.hasExitTime = false;
            up.duration = 0.25f;
            // Back to standing before the end of the recording (it ends in a fighter's stance).
            var done = rising.AddTransition(move);
            done.hasExitTime = true;
            done.exitTime = upEnd;
            done.duration = 0.4f;
        }

        static AnimationClip Mixamo(string name, bool loop)
        {
            string path = MixamoDir + name + ".fbx";
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) return null;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.importConstraints = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            var clips = importer.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.name = name;
                c.maskType = ClipAnimationMaskType.CopyFromOther;
                c.maskSource = HumanoidOnlyMask();
                c.loopTime = loop;
                c.loopPose = loop;
                c.lockRootRotation = true;
                c.keepOriginalOrientation = false;
                c.lockRootHeightY = true;
                c.keepOriginalPositionY = true; // (lying on the floor as recorded)
                c.lockRootPositionXZ = false;   // played where the player is
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is AnimationClip clip && !clip.name.StartsWith("__preview__")) return clip;
            return null;
        }

        static AvatarMask HumanoidOnlyMask()
        {
            const string path = Root + "/Humanoid Only.mask";
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
            if (mask != null) return mask;
            mask = new AvatarMask();
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, true);
            mask.transformCount = 0;
            AssetDatabase.CreateAsset(mask, path);
            return mask;
        }

        static Avatar ConfigureModel(string path, bool human = false)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            var type = human ? ModelImporterAnimationType.Human : ModelImporterAnimationType.Generic;
            if (importer.animationType != type || importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel || importer.importAnimation)
            {
                importer.animationType = type;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                importer.SaveAndReimport();
            }
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path)) if (o is Avatar a) return a;
            return null;
        }

        // A looping clip that plays in place: the walk/run files carry the walking motion in
        // their root bone, which is taken out so the game moves the body instead.
        static AnimationClip Clip(string path, Avatar source, bool loop, bool keepPlace)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = source;
            importer.motionNodeName = "Bip01";
            importer.importAnimation = true;
            var clips = importer.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.maskType = ClipAnimationMaskType.CreateFromThisModel;
                c.maskSource = null;
                c.loopTime = loop;
                c.loopPose = loop;
                c.lockRootRotation = true;
                c.keepOriginalOrientation = true;
                c.lockRootHeightY = false;
                c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = true;      // bake the sideways sway into the pose
                c.keepOriginalPositionXZ = false; // ...but take the forward travel out
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is AnimationClip clip && !clip.name.StartsWith("__preview__")) return clip;
            return null;
        }

        // ---- Mixamo moves, made to fit these skeletons ----
        // The Mixamo recordings are for a different skeleton. Each is played on a stand-in of
        // one of our people (set up as Humanoid just for this), and every bone's turn, frame by
        // frame, is recorded into an ordinary clip for our skeleton.

        static readonly string[] k_MixamoMoves = { "Left Strafe Walk", "Right Strafe Walk", "Knocked Out", "Fall Flat", GetUpBack, GetUpFront };
        const string BakedDir = Root + "/Animations/Baked/";

        static void BakeMixamo(string sourcePerson, string prefix)
        {
            string modelPath = Root + "/" + sourcePerson + "/" + sourcePerson + ".fbx";
            var human = ConfigureModel(modelPath, true);
            Directory.CreateDirectory(BakedDir);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var go = Object.Instantiate(model);
            var animator = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            animator.avatar = human;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // (nothing is looking at it)
            var bones = new List<Transform>();
            foreach (var t in go.GetComponentsInChildren<Transform>()) if (t.name.StartsWith("Bip01")) bones.Add(t);
            foreach (string move in k_MixamoMoves)
            {
                var source = Mixamo(move, move.Contains("Strafe"));
                if (source == null) continue;
                var baked = new AnimationClip { name = prefix + " " + move, frameRate = 30f };
                var graph = UnityEngine.Playables.PlayableGraph.Create();
                var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "bake", animator);
                var playable = UnityEngine.Animations.AnimationClipPlayable.Create(graph, source);
                UnityEngine.Playables.PlayableOutputExtensions.SetSourcePlayable(output, playable);
                int frames = Mathf.Max(2, Mathf.RoundToInt(source.length * 30f) + 1);
                var rot = new Dictionary<Transform, AnimationCurve[]>();
                var pos = new Dictionary<Transform, AnimationCurve[]>();
                foreach (var b in bones)
                {
                    rot[b] = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
                    if (b.name == "Bip01" || b.name == "Bip01 Pelvis") pos[b] = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
                }
                var lowest = new float[frames];
                for (int f = 0; f < frames; f++)
                {
                    float time = Mathf.Min(source.length, f / 30f);
                    UnityEngine.Playables.PlayableExtensions.SetTime(playable, (double)time);
                    graph.Evaluate();
                    lowest[f] = float.MaxValue;
                    foreach (var b in bones)
                        if (!b.name.Contains("Footsteps")) lowest[f] = Mathf.Min(lowest[f], b.position.y);
                    foreach (var b in bones)
                    {
                        var q = b.localRotation;
                        var c = rot[b];
                        c[0].AddKey(time, q.x); c[1].AddKey(time, q.y); c[2].AddKey(time, q.z); c[3].AddKey(time, q.w);
                        if (pos.TryGetValue(b, out var p))
                        {
                            var v = b.localPosition;
                            p[0].AddKey(time, v.x); p[1].AddKey(time, v.y); p[2].AddKey(time, v.z);
                        }
                    }
                }
                graph.Destroy();
                // Lying flat should be lying on the floor (the lowest bone, inside the body, a few
                // centimetres above it): eased in while falling, eased out while getting up.
                bool falls = move == "Knocked Out" || move == "Fall Flat", rises = move == GetUpBack || move == GetUpFront;
                if (falls || rises)
                {
                    float lift = 0.07f - (falls ? lowest[frames - 1] : lowest[0]);
                    var bip = bones.Find(b => b.name == "Bip01");
                    var keys = pos[bip][1].keys;
                    for (int k = 0; k < keys.Length; k++)
                    {
                        float u = (float)k / (keys.Length - 1);
                        float w = falls ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.8f, u)) : 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.6f, u));
                        keys[k].value += lift * w;
                    }
                    pos[bip][1].keys = keys;
                }
                foreach (var b in bones)
                {
                    string path = AnimationUtility.CalculateTransformPath(b, go.transform);
                    string[] q = { "localRotation.x", "localRotation.y", "localRotation.z", "localRotation.w" };
                    for (int i = 0; i < 4; i++) baked.SetCurve(path, typeof(Transform), q[i], rot[b][i]);
                    if (pos.TryGetValue(b, out var p))
                    {
                        baked.SetCurve(path, typeof(Transform), "localPosition.x", p[0]);
                        baked.SetCurve(path, typeof(Transform), "localPosition.y", p[1]);
                        baked.SetCurve(path, typeof(Transform), "localPosition.z", p[2]);
                    }
                }
                baked.EnsureQuaternionContinuity();
                var settings = AnimationUtility.GetAnimationClipSettings(baked);
                settings.loopTime = move.Contains("Strafe");
                AnimationUtility.SetAnimationClipSettings(baked, settings);
                string assetPath = BakedDir + prefix + " " + move + ".anim";
                AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.CreateAsset(baked, assetPath);
            }
            Object.DestroyImmediate(go);
            ConfigureModel(modelPath, false);
        }

        static AnimationClip Baked(string prefix, string move) => AssetDatabase.LoadAssetAtPath<AnimationClip>(BakedDir + prefix + " " + move + ".anim");

        static PlayerAvatar BuildPrefab(string person, AnimatorController controller)
        {
            string dir = Root + "/" + person + "/";
            var avatar = ConfigureModel(dir + person + ".fbx");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(dir + person + ".fbx");
            if (model == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            go.name = person;
            var animator = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = CharacterMaterial(dir, person, mats[i] != null ? mats[i].name : "");
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            go.AddComponent<PlayerAvatar>();
            string prefabPath = $"{PrefabDir}/{person}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            return prefab.GetComponent<PlayerAvatar>();
        }

        // The character's own textures: body, head, and (eyelashes etc.) see-through parts.
        static Material CharacterMaterial(string dir, string person, string original)
        {
            string code = null;
            foreach (var f in Directory.GetFiles(dir, "*_body_color.png")) code = Path.GetFileName(f).Replace("_body_color.png", "");
            // Each part has its own textures: body, head, see-through bits (opacity), and extras
            // like a headscarf ("keffiyeh"), named after the material.
            string lower = original.ToLowerInvariant();
            string part = lower.Contains("head") ? "head" : lower.Contains("opacity") ? "opacity" : "body";
            foreach (var f in Directory.GetFiles(dir, code + "_*_color.png"))
            {
                string candidate = Path.GetFileName(f).Substring(code.Length + 1).Replace("_color.png", "");
                if (candidate != "body" && candidate != "head" && candidate != "opacity" && lower.Contains(candidate)) part = candidate;
            }
            string matPath = $"Assets/GlassRoom/Materials/Player {person} {part}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, matPath); }
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(dir + $"{code}_{part}_color.png"));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", part == "head" ? 0.35f : 0.2f);
            if (part != "opacity")
            {
                string normal = dir + $"{code}_{part}_normal.png";
                if (AssetImporter.GetAtPath(normal) is TextureImporter ti && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
                mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normal));
                mat.EnableKeyword("_NORMALMAP");
            }
            else
            {
                mat.SetFloat("_AlphaClip", 1f);
                mat.SetFloat("_Cutoff", 0.5f);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.SetFloat("_Cull", 0f);
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
