using UnityEngine;

namespace GlassRoom
{
    /// <summary>Draws the few UI shapes the HUD needs (circle, ring, rounded panel) in code.</summary>
    public static class UISprites
    {
        // Cached between Play sessions, when Unity may have destroyed them. The checks below use
        // Unity's == null, which (unlike ??=) treats a destroyed sprite as missing.
        static Sprite s_Circle, s_Ring, s_Rounded;

        public static Sprite Circle
        {
            get
            {
                if (s_Circle == null)
                    s_Circle = Make(128, (x, y, r) => Edge(r, 0.48f));
                return s_Circle;
            }
        }

        public static Sprite Ring
        {
            get
            {
                if (s_Ring == null)
                    s_Ring = Make(256, (x, y, r) => Mathf.Min(Edge(r, 0.48f), 1f - Edge(r, 0.455f)));
                return s_Ring;
            }
        }

        public static Sprite RoundedPanel
        {
            get
            {
                if (s_Rounded != null)
                    return s_Rounded;
                const int size = 96;
                const float radius = 32f;
                var tex = NewTexture(size);
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float cx = Mathf.Clamp(px, radius, size - radius);
                    float cy = Mathf.Clamp(py, radius, size - radius);
                    float d = Vector2.Distance(new Vector2(px, py), new Vector2(cx, cy));
                    float a = Mathf.Clamp01(radius - d + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
                tex.SetPixels32(pixels);
                tex.Apply();
                s_Rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                    SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
                return s_Rounded;
            }
        }

        // Anti-aliased "inside radius" mask, with r and edge in units of texture size.
        static float Edge(float r, float edge) => Mathf.Clamp01((edge - r) * 256f + 0.5f);

        static Texture2D NewTexture(int size) => new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        static Sprite Make(int size, System.Func<float, float, float, float> alpha)
        {
            var tex = NewTexture(size);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float nx = (x + 0.5f) / size - 0.5f;
                float ny = (y + 0.5f) / size - 0.5f;
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                float a = Mathf.Clamp01(alpha(nx, ny, r));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
