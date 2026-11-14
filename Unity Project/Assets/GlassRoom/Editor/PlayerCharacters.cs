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

        // Standing, walking and running blend by speed (m/s); sitting is its own state.
        static AnimatorController BuildController(string prefix, string sourcePerson, string name)
        {
            string animDir = Root + "/Animations/";
            var source = ConfigureModel(Root + "/" + sourcePerson + "/" + sourcePerson + ".fbx");
            var idle = Clip(animDir + prefix + "_idle_neutral_01.fbx", source, true);
            var walk = Clip(animDir + prefix + "_walk_neutral_01.fbx", source, true);
            var run = Clip(animDir + prefix + "_run_neutral_01.fbx", source, true);
            var sprint = Clip(animDir + prefix + "_run_fast_01.fbx", source, true);
            var sit = Clip(animDir + prefix + "_sit_chair_idle_neutral_01.fbx", source, true);

            string path = "Assets/GlassRoom/Models/Players/" + name + ".controller";
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Seated", AnimatorControllerParameterType.Bool);
            var sm = controller.layers[0].stateMachine;

            var move = controller.CreateBlendTreeInController("Move", out var tree);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            // Each clip at the speed (m/s) it was recorded moving, played a little faster or
            // slower to match the game's speeds so the feet don't slide: walk 1.0, jog 2.9 and
            // sprint 5.8 m/s in the recordings; the game walks at 2.1 and sprints at 4.5.
            // Walking backwards: the walk played in reverse (negative speeds).
            tree.AddChild(walk, -2.1f);
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, 1.3f);
            tree.AddChild(run, 2.9f);
            tree.AddChild(sprint, 4.5f);
            var children = tree.children;
            children[0].timeScale = -1.7f;
            children[2].timeScale = 1.3f / 1.01f;
            children[3].timeScale = 1f;
            children[4].timeScale = 0.85f;
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
            EditorUtility.SetDirty(controller);
            return controller;
        }

        static Avatar ConfigureModel(string path)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (importer.animationType != ModelImporterAnimationType.Generic || importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel || importer.importAnimation)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
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
        static AnimationClip Clip(string path, Avatar source, bool loop)
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
