using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GlassRoom
{
    /// <summary>
    /// Renders the room mirrored in a horizontal plane every frame, for a sharp glass-floor
    /// reflection. The floor shader reads the result as _PlanarReflectionTex.
    /// </summary>
    public class PlanarReflection : MonoBehaviour
    {
        static readonly int k_TextureId = Shader.PropertyToID("_PlanarReflectionTex");
        static readonly int k_WeightId = Shader.PropertyToID("_PlanarReflectionWeight");

        [Tooltip("World height of the reflecting surface.")]
        public float planeHeight = 0f;
        [Tooltip("Lower is faster. The reflection is drawn at this fraction of the screen's width and height.")]
        [Range(0.25f, 1f)] public float resolutionScale = 0.5f;
        public LayerMask reflectLayers = ~0;

        Camera m_ReflectionCamera;
        RenderTexture m_Texture;

        void OnDisable()
        {
            Shader.SetGlobalFloat(k_WeightId, 0f);
            if (m_ReflectionCamera != null) Destroy(m_ReflectionCamera.gameObject);
            if (m_Texture != null) m_Texture.Release();
            m_ReflectionCamera = null;
            m_Texture = null;
        }

        void LateUpdate()
        {
            var main = Camera.main;
            if (main == null)
                return;
            // Low graphics: no mirror image at all (it redraws the whole room), just the plain floor.
            if (GraphicsQuality.Low)
            {
                Shader.SetGlobalFloat(k_WeightId, 0f);
                return;
            }
            Prepare(main);
            Render(main);
        }

        void Prepare(Camera main)
        {
            float scale = resolutionScale;
            int width = Mathf.Max(16, Mathf.RoundToInt(main.pixelWidth * scale));
            int height = Mathf.Max(16, Mathf.RoundToInt(main.pixelHeight * scale));
            if (m_Texture == null || m_Texture.width != width || m_Texture.height != height)
            {
                if (m_Texture != null) m_Texture.Release();
                m_Texture = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR)
                {
                    name = "Planar Reflection",
                    // Smoothed edges: thin things like the scooter's pole are narrower than a
                    // pixel of this half-size image and would otherwise break into bright dashes.
                    antiAliasing = 4,
                    useMipMap = false
                };
                m_Texture.Create();
            }

            if (m_ReflectionCamera == null)
            {
                var go = new GameObject("Planar Reflection Camera") { hideFlags = HideFlags.HideAndDontSave };
                m_ReflectionCamera = go.AddComponent<Camera>();
                m_ReflectionCamera.enabled = false;
                var data = go.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = false;
                data.renderShadows = false;
                data.requiresDepthTexture = false;
                data.requiresColorTexture = false;
                data.antialiasing = AntialiasingMode.None;
            }

            var cam = m_ReflectionCamera;
            // Marked as a reflection camera. Drawn as an ordinary game camera, in browsers its
            // lights came out split into mismatched halves, putting a seam across the middle of
            // the mirror image (the top half of the screen looked slightly darker).
            cam.cameraType = CameraType.Reflection;
            cam.clearFlags = main.clearFlags;
            cam.backgroundColor = main.backgroundColor;
            cam.fieldOfView = main.fieldOfView;
            cam.nearClipPlane = main.nearClipPlane;
            cam.farClipPlane = main.farClipPlane;
            cam.aspect = main.aspect;
            cam.allowHDR = true;
            // Never draw the reflecting surface into its own reflection.
            cam.cullingMask = reflectLayers & ~(1 << gameObject.layer);
            cam.targetTexture = m_Texture;
        }

        void Render(Camera main)
        {
            var cam = m_ReflectionCamera;
            Vector3 normal = Vector3.up;
            Vector3 point = new Vector3(0f, planeHeight, 0f);
            var plane = new Vector4(normal.x, normal.y, normal.z, -Vector3.Dot(normal, point));
            Matrix4x4 reflect = ReflectionMatrix(plane);

            // An ordinary camera at the mirrored eye position, looking along the mirrored view.
            // URP renders from the camera's own transform (a custom mirrored view matrix is not
            // honoured), and an ordinary camera needs no culling tricks. Its image is the mirror
            // image flipped left-to-right, which the floor shader undoes when sampling.
            Vector3 eye = reflect.MultiplyPoint(main.transform.position);
            Vector3 forward = Vector3.Reflect(main.transform.forward, normal);
            Vector3 up = Vector3.Reflect(main.transform.up, normal);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(forward, up));
            cam.ResetWorldToCameraMatrix();
            // The floor itself is left out by layer (see Prepare), and nothing else lies below it.
            cam.projectionMatrix = main.projectionMatrix;

            // The walls' mirrors show their plain look inside the floor's reflection; their live
            // images belong to the main view (see MirrorSurface).
            Shader.SetGlobalFloat("_MirrorsDisabled", 1f);
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = m_Texture };
            if (RenderPipeline.SupportsRenderRequest(cam, request))
                RenderPipeline.SubmitRenderRequest(cam, request);
            else
                cam.Render();
            Shader.SetGlobalFloat("_MirrorsDisabled", 0f);

            Shader.SetGlobalTexture(k_TextureId, m_Texture);
            Shader.SetGlobalFloat(k_WeightId, 1f);
        }

        static Matrix4x4 ReflectionMatrix(Vector4 p)
        {
            var m = Matrix4x4.identity;
            m.m00 = 1f - 2f * p.x * p.x;
            m.m01 = -2f * p.x * p.y;
            m.m02 = -2f * p.x * p.z;
            m.m03 = -2f * p.w * p.x;
            m.m10 = -2f * p.y * p.x;
            m.m11 = 1f - 2f * p.y * p.y;
            m.m12 = -2f * p.y * p.z;
            m.m13 = -2f * p.w * p.y;
            m.m20 = -2f * p.z * p.x;
            m.m21 = -2f * p.z * p.y;
            m.m22 = 1f - 2f * p.z * p.z;
            m.m23 = -2f * p.w * p.z;
            return m;
        }
    }
}
