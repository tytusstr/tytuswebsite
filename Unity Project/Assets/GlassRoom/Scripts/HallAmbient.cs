using UnityEngine;
using UnityEngine.Rendering;

namespace GlassRoom
{
    /// <summary>
    /// The soft light that fills the hall everywhere (light bounced around the room): a little
    /// brighter from above than from below. Unity only recalculates this from the scene's
    /// lighting settings when lighting is baked, so it is set here directly whenever the scene
    /// starts (in the editor too).
    /// </summary>
    [ExecuteAlways]
    public class HallAmbient : MonoBehaviour
    {
        public Color above = new Color(0.26f, 0.28f, 0.32f);
        public Color sides = new Color(0.2f, 0.21f, 0.24f);
        public Color below = new Color(0.1f, 0.1f, 0.11f);

        void OnEnable() => Apply();
        void OnValidate() => Apply();

        public void Apply()
        {
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(sides);
            // Directional terms push the top and bottom toward their own colours.
            const float k = 0.53f;
            sh.AddDirectionalLight(Vector3.up, above - sides, k);     // upward-facing surfaces
            sh.AddDirectionalLight(Vector3.down, below - sides, k);   // downward-facing surfaces
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = sh;
        }
    }
}
