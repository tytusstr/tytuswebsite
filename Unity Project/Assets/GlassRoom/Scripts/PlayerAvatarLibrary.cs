using UnityEngine;

namespace GlassRoom
{
    /// <summary>The people players can appear as (Resources/Player Avatars).</summary>
    [CreateAssetMenu(menuName = "Glass Room/Player Avatar Library")]
    public class PlayerAvatarLibrary : ScriptableObject
    {
        public PlayerAvatar[] avatars;

        static PlayerAvatarLibrary s_Instance;
        public static PlayerAvatarLibrary Instance => s_Instance != null ? s_Instance : (s_Instance = Resources.Load<PlayerAvatarLibrary>("Player Avatars"));

        public PlayerAvatar Spawn(int index, Transform parent)
        {
            if (avatars == null || avatars.Length == 0) return null;
            var prefab = avatars[Mathf.Abs(index) % avatars.Length];
            return Instantiate(prefab, parent);
        }
    }
}
