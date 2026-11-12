using System.IO;
using UnityEngine;
using UnityEngine.Video;

namespace GlassRoom
{
    /// <summary>
    /// The real sky seen through the skylight: a time-lapse video of clouds drifting across a
    /// blue sky, filmed looking straight up (StreamingAssets/Sky/Clouds.mp4, Pexels, made into a
    /// seamless loop), played onto a large plane high above the building. Until the video is
    /// ready the plane stays hidden and the still sky photo all around shows instead.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public class SkyVideo : MonoBehaviour
    {
        public string file = "Sky/Clouds.mp4";
        [Tooltip("Speed of the clouds compared with the original time-lapse.")]
        public float playbackSpeed = 0.6f;

        static readonly int k_MainTex = Shader.PropertyToID("_MainTex");
        VideoPlayer m_Player;
        RenderTexture m_Texture;
        MeshRenderer m_Renderer;
        MaterialPropertyBlock m_Block;

        void Start()
        {
            m_Renderer = GetComponent<MeshRenderer>();
            m_Renderer.enabled = false;
            m_Block = new MaterialPropertyBlock();
            m_Texture = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32) { name = "Sky Video", useMipMap = true, autoGenerateMips = true, filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp };
            m_Texture.Create();
            m_Player = gameObject.AddComponent<VideoPlayer>();
            m_Player.playOnAwake = false;
            m_Player.source = VideoSource.Url;
            m_Player.url = Path.Combine(Application.streamingAssetsPath, file);
            m_Player.isLooping = true;
            m_Player.audioOutputMode = VideoAudioOutputMode.None;
            m_Player.renderMode = VideoRenderMode.RenderTexture;
            m_Player.targetTexture = m_Texture;
            m_Player.playbackSpeed = playbackSpeed;
            m_Player.prepareCompleted += OnPrepared;
            m_Player.errorReceived += (p, message) => Debug.LogWarning("Sky video: " + message);
            m_Player.Prepare();
        }

        void OnPrepared(VideoPlayer player)
        {
            m_Renderer.GetPropertyBlock(m_Block);
            m_Block.SetTexture(k_MainTex, m_Texture);
            m_Renderer.SetPropertyBlock(m_Block);
            player.Play();
            m_Renderer.enabled = true;
        }

        void OnDestroy()
        {
            if (m_Texture != null) m_Texture.Release();
        }
    }
}
