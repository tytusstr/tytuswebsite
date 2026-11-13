using UnityEngine;

namespace GlassRoom
{
    /// <summary>
    /// Your own body. Others see it (through the network), and you see it in the mirrors, in the
    /// floor's reflection and in your shadow; your own view leaves it out (the camera is inside
    /// its head). Which person you are is picked at random the first time and remembered.
    /// </summary>
    [RequireComponent(typeof(FirstPersonController))]
    public class LocalPlayerBody : MonoBehaviour
    {
        public const string LayerName = "Local Body";
        const string IndexKey = "AvatarIndex";

        FirstPersonController m_Player;
        PlayerAvatar m_Avatar;

        public int AvatarIndex { get; private set; }
        public PlayerAvatar Avatar => m_Avatar;

        void Start()
        {
            m_Player = GetComponent<FirstPersonController>();
            var library = PlayerAvatarLibrary.Instance;
            if (library == null || library.avatars == null || library.avatars.Length == 0) return;
            AvatarIndex = PlayerPrefs.GetInt(IndexKey, -1);
            if (AvatarIndex < 0 || AvatarIndex >= library.avatars.Length)
            {
                AvatarIndex = Random.Range(0, library.avatars.Length);
                PlayerPrefs.SetInt(IndexKey, AvatarIndex);
                PlayerPrefs.Save();
            }
            // Not a child of the player: it's placed explicitly each frame (seated or riding it
            // sits elsewhere than the player's own transform).
            m_Avatar = library.Spawn(AvatarIndex, null);
            m_Avatar.name = "My Body";
            int layer = LayerMask.NameToLayer(LayerName);
            if (layer >= 0) m_Avatar.SetLayer(layer);
        }

        void LateUpdate()
        {
            if (m_Avatar == null) return;
            PlayerAvatar.Placement(transform.position, m_Player.Yaw, m_Player.CurrentSeat, m_Player.CurrentRide, out var feet, out var yaw);
            m_Avatar.Pose(feet, yaw, m_Player.GroundSpeed, m_Player.CurrentSeat != null, Time.deltaTime);
        }

        void OnDestroy()
        {
            if (m_Avatar != null) Destroy(m_Avatar.gameObject);
        }
    }
}
