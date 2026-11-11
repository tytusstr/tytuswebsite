using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GlassRoom
{
    /// <summary>
    /// A true mirror reflection on a flat wall, like the floor's (see PlanarReflection): every
    /// frame the wall is in view, a second camera renders the room mirrored across the wall and
    /// the wall's shader ("GlassRoom/Mirror Wall") shows that image. Unlike a pre-captured
    /// reflection, everything appears exactly where it should: lights, plants, chairs, you.
    /// Only walls you can see are drawn, at reduced resolution. On Low graphics the wall falls
    /// back to the soft pre-captured reflection.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class MirrorSurface : MonoBehaviour
    {
        static readonly int k_TextureId = Shader.PropertyToID("_MirrorTex");
        static readonly int k_WeightId = Shader.PropertyToID("_MirrorWeight");
        static readonly int k_MirrorsDisabledId = Shader.PropertyToID("_MirrorsDisabled");
        static readonly int k_FloorWeightId = Shader.PropertyToID("_PlanarReflectionWeight");

        [Tooltip("The mirror image is drawn at this fraction of the screen's width and height.")]
        [Range(0.25f, 1f)] public float resolutionScale = 0.45f;

        Renderer m_Renderer;
        MaterialPropertyBlock m_Block;
        Camera m_Camera;
        RenderTexture m_Texture;
        Vector3 m_Point, m_Normal;
        readonly Plane[] m_Frustum = new Plane[6];

        void Awake()
        {
            m_Renderer = GetComponent<Renderer>();
            m_Block = new MaterialPropertyBlock();
            FindPlane();
        }

        // The wall's inside face: the thinnest side of its box, on the side facing the hall.
        void FindPlane()
        {
            var b = m_Renderer.bounds;
            var hall = transform.parent != null && transform.parent.parent != null ? transform.parent.parent.position : Vector3.zero;
            Vector3 size = b.size;
            int axis = size.x < size.z ? (size.x < size.y ? 0 : 1) : (size.z < size.y ? 2 : 1);
            Vector3 n = Vector3.zero;
            n[axis] = Mathf.Sign(hall[axis] - b.center[axis]);
            m_Normal = n;
            m_Point = b.center + Vector3.Scale(n, b.extents);
        }

        void OnDisable()
        {
            SetWeight(0f, null);
            if (m_Camera != null) Destroy(m_Camera.gameObject);
            if (m_Texture != null) m_Texture.Release();
            m_Camera = null;
            m_Texture = null;
        }

        void LateUpdate()
        {
            var main = Camera.main;
            if (main == null || GraphicsQuality.Low || !InView(main))
            {
                SetWeight(0f, null);
                return;
            }
            Prepare(main);
            Render(main);
            SetWeight(1f, m_Texture);
        }

        bool InView(Camera main)
        {
            if (Vector3.Dot(main.transform.position - m_Point, m_Normal) <= 0f) return false;
            GeometryUtility.CalculateFrustumPlanes(main, m_Frustum);
            return GeometryUtility.TestPlanesAABB(m_Frustum, m_Renderer.bounds);
        }

        void SetWeight(float weight, Texture texture)
        {
            if (m_Renderer == null) return;
            m_Renderer.GetPropertyBlock(m_Block);
            m_Block.SetFloat(k_WeightId, weight);
            if (texture != null) m_Block.SetTexture(k_TextureId, texture);
            m_Renderer.SetPropertyBlock(m_Block);
        }

        void Prepare(Camera main)
        {
            int width = Mathf.Max(16, Mathf.RoundToInt(main.pixelWidth * resolutionScale));
            int height = Mathf.Max(16, Mathf.RoundToInt(main.pixelHeight * resolutionScale));
            if (m_Texture == null || m_Texture.width != width || m_Texture.height != height)
            {
                if (m_Texture != null) m_Texture.Release();
                m_Texture = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR)
                {
                    name = name + " Mirror",
                    antiAliasing = 2,
                    useMipMap = false
                };
                m_Texture.Create();
            }
            if (m_Camera == null)
            {
                var go = new GameObject(name + " Mirror Camera") { hideFlags = HideFlags.HideAndDontSave };
                m_Camera = go.AddComponent<Camera>();
                m_Camera.enabled = false;
                var data = go.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = false;
                data.renderShadows = false;
                data.requiresDepthTexture = false;
                data.requiresColorTexture = false;
                data.antialiasing = AntialiasingMode.None;
            }
            var cam = m_Camera;
            // A reflection camera (as for the floor): drawn as an ordinary game camera, browsers
            // split its lighting into mismatched halves.
            cam.cameraType = CameraType.Reflection;
            cam.clearFlags = main.clearFlags;
            cam.backgroundColor = main.backgroundColor;
            cam.fieldOfView = main.fieldOfView;
            cam.nearClipPlane = main.nearClipPlane;
            cam.farClipPlane = main.farClipPlane;
            cam.aspect = main.aspect;
            cam.allowHDR = true;
            cam.cullingMask = main.cullingMask;
            cam.targetTexture = m_Texture;
        }

        void Render(Camera main)
        {
            var cam = m_Camera;
            var plane = new Vector4(m_Normal.x, m_Normal.y, m_Normal.z, -Vector3.Dot(m_Normal, m_Point));
            Matrix4x4 reflect = ReflectionMatrix(plane);

            // The camera stands at the mirrored eye, looking along the mirrored view. Its image
            // is the mirror image flipped left-to-right, which the wall's shader undoes.
            Vector3 eye = reflect.MultiplyPoint(main.transform.position);
            Vector3 forward = Vector3.Reflect(main.transform.forward, m_Normal);
            Vector3 up = Vector3.Reflect(main.transform.up, m_Normal);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(forward, up));
            cam.ResetWorldToCameraMatrix();
            // Nothing behind the wall (outside the hall) may show: clip exactly at the wall.
            cam.projectionMatrix = main.projectionMatrix;
            cam.projectionMatrix = cam.CalculateObliqueMatrix(CameraSpacePlane(cam, m_Point, m_Normal, 0.02f));

            // While this mirror draws, other mirrors (the floor, other walls) show their plain
            // look; their live images belong to the main view.
            float floorWeight = Shader.GetGlobalFloat(k_FloorWeightId);
            Shader.SetGlobalFloat(k_FloorWeightId, 0f);
            Shader.SetGlobalFloat(k_MirrorsDisabledId, 1f);
            m_Renderer.enabled = false;
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = m_Texture };
            if (RenderPipeline.SupportsRenderRequest(cam, request))
                RenderPipeline.SubmitRenderRequest(cam, request);
            else
                cam.Render();
            m_Renderer.enabled = true;
            Shader.SetGlobalFloat(k_MirrorsDisabledId, 0f);
            Shader.SetGlobalFloat(k_FloorWeightId, floorWeight);
        }

        static Vector4 CameraSpacePlane(Camera cam, Vector3 point, Vector3 normal, float offset)
        {
            Matrix4x4 m = cam.worldToCameraMatrix;
            Vector3 p = m.MultiplyPoint(point + normal * offset);
            Vector3 n = m.MultiplyVector(normal).normalized;
            return new Vector4(n.x, n.y, n.z, -Vector3.Dot(p, n));
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
