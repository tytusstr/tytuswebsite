using UnityEngine;
using UnityEngine.Rendering;

namespace GlassRoom
{
    /// <summary>
    /// Your own body. Others see it (through the network), and you see it in the floor's
    /// reflection and in your shadow. Standing, your own view leaves it out (the camera is inside
    /// its head); sitting in a chair, your view moves to its eyes and you can look down at
    /// yourself. Which person you are is picked at random the first time and remembered; it can
    /// be changed in the settings (Character).
    /// </summary>
    [RequireComponent(typeof(FirstPersonController))]
    public class LocalPlayerBody : MonoBehaviour
    {
        public const string LayerName = "Local Body";
        const string IndexKey = "AvatarIndex";

        FirstPersonController m_Player;
        PlayerAvatar m_Avatar;
        Camera m_Camera;
        int m_Layer = -1;
        // The see-through parts (eyelashes), hidden while you look out through the eyes.
        Material[] m_SeeThrough;
        bool m_ShowingSelf;

        public int AvatarIndex { get; private set; }
        public PlayerAvatar Avatar => m_Avatar;

        /// <summary>The local player's body (for the settings menu).</summary>
        public static LocalPlayerBody Instance { get; private set; }

        /// <summary>How many people there are to choose from.</summary>
        public static int Choices
        {
            get
            {
                var library = PlayerAvatarLibrary.Instance;
                return library != null && library.avatars != null ? library.avatars.Length : 0;
            }
        }

        void Awake() => Instance = this;

        void Start()
        {
            m_Player = GetComponent<FirstPersonController>();
            m_Camera = GetComponentInChildren<Camera>();
            m_Layer = LayerMask.NameToLayer(LayerName);
            int count = Choices;
            if (count == 0) return;
            int index = PlayerPrefs.GetInt(IndexKey, -1);
            if (index < 0 || index >= count)
            {
                index = Random.Range(0, count);
                PlayerPrefs.SetInt(IndexKey, index);
                PlayerPrefs.Save();
            }
            Show(index);
            RenderPipelineManager.beginCameraRendering += BeforeRender;
        }

        /// <summary>Becomes another person (remembered for next time; others see the change).</summary>
        public void SetAvatar(int index)
        {
            int count = Choices;
            if (count == 0) return;
            index = ((index % count) + count) % count;
            PlayerPrefs.SetInt(IndexKey, index);
            PlayerPrefs.Save();
            Show(index);
        }

        void Show(int index)
        {
            if (m_Avatar != null) Destroy(m_Avatar.gameObject);
            AvatarIndex = index;
            // Not a child of the player: it's placed explicitly each frame (seated or riding it
            // sits elsewhere than the player's own transform).
            m_Avatar = PlayerAvatarLibrary.Instance.Spawn(index, null);
            m_Avatar.name = "My Body";
            if (m_Layer >= 0) m_Avatar.SetLayer(m_Layer);
            var list = new System.Collections.Generic.List<Material>();
            foreach (var r in m_Avatar.GetComponentsInChildren<Renderer>())
                foreach (var m in r.materials)
                    if (m != null && m.name.ToLowerInvariant().Contains("opacity")) list.Add(m);
            m_SeeThrough = list.ToArray();
            var heads = new System.Collections.Generic.List<Material>();
            foreach (var r in m_Avatar.GetComponentsInChildren<Renderer>())
                foreach (var m in r.materials)
                {
                    string n = m != null ? m.name.ToLowerInvariant() : "";
                    if (n.Contains(" head") || n.Contains("keffiyeh")) heads.Add(m);
                }
            m_Head = heads.ToArray();
            m_HeadHidden = false;
            m_ShowingSelf = false;
        }

        void LateUpdate()
        {
            if (m_Avatar == null) return;
            var seat = m_Player.CurrentSeat;
            PlayerAvatar.Placement(transform.position, m_Player.BodyYaw, seat, m_Player.CurrentRide, out var feet, out var yaw);
            m_Avatar.Move(m_Player.LocalMotion);
            m_Avatar.Knock(m_Player.Lying, m_Player.DownBackwards, m_Player.KnockCount);
            m_Avatar.Pose(feet, yaw, m_Player.GroundSpeed, seat != null, Time.deltaTime, PlayerAvatar.LeanBack(seat));
            m_Avatar.Look(m_Player.HeadYaw, m_Player.HeadPitch);
            m_Avatar.Hold(m_Player.CurrentRide);
            var board = MarkerBoard.Instance;
            if (board != null && board.ActiveAuthor == MarkerBoard.LocalAuthor) m_Avatar.UseBoard(board.PenPoint, board.Facing, board.ActiveTool, board.PenColor);
            else m_Avatar.UseBoard(Vector3.zero, Vector3.zero, MarkerBoard.Tool.None, Color.black);
            ShowSelf(seat != null);
        }

        // Sitting: your own camera draws your body too (without the eyelashes, which would sit
        // right in front of your eyes).
        void ShowSelf(bool show)
        {
            if (show == m_ShowingSelf || m_Camera == null || m_Layer < 0) return;
            m_ShowingSelf = show;
            if (show) m_Camera.cullingMask |= 1 << m_Layer;
            else m_Camera.cullingMask &= ~(1 << m_Layer);
            foreach (var m in m_SeeThrough)
                if (m != null) m.SetFloat("_Cutoff", show ? 1.01f : 0.5f);
        }

        // Sitting, the view is from your body's eyes: just above the neck, a little forward
        // (after the body has been posed this frame). The neck doesn't move as you look around,
        // and the point is smoothed, so the view stays still.
        Vector3 m_SeatedEye;
        bool m_HasSeatedEye;

        // Your own head, hidden from your own view while seated (you'd see inside it looking
        // down), but still there in mirrors, reflections and for everyone else.
        Material[] m_Head;
        bool m_HeadHidden;

        void HideHead(bool hide)
        {
            if (hide == m_HeadHidden || m_Head == null) return;
            m_HeadHidden = hide;
            foreach (var m in m_Head)
            {
                if (m == null) continue;
                if (hide) { m.EnableKeyword("_ALPHATEST_ON"); m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 1.01f); }
                else { m.DisableKeyword("_ALPHATEST_ON"); m.SetFloat("_AlphaClip", 0f); m.SetFloat("_Cutoff", 0.5f); }
            }
        }

        void BeforeRender(ScriptableRenderContext context, Camera camera)
        {
            HideHead(camera == m_Camera && m_ShowingSelf);
            if (camera != m_Camera) return;
            if (!m_ShowingSelf || m_Avatar == null || m_Avatar.Neck == null) { m_HasSeatedEye = false; return; }
            var root = m_Avatar.transform;
            Vector3 eye = m_Avatar.Neck.position + root.up * 0.17f + Quaternion.Euler(0f, root.eulerAngles.y, 0f) * Vector3.forward * 0.17f;
            m_SeatedEye = m_HasSeatedEye ? Vector3.Lerp(m_SeatedEye, eye, 1f - Mathf.Exp(-6f * Time.deltaTime)) : eye;
            m_HasSeatedEye = true;
            camera.transform.position = m_SeatedEye;
        }

        void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeRender;
            if (m_Avatar != null) Destroy(m_Avatar.gameObject);
            if (Instance == this) Instance = null;
        }
    }
}
