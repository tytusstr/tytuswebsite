using UnityEngine;

namespace GlassRoom
{
    /// <summary>Decides whether a touch was a tap rather than a drag.</summary>
    public static class TapDetector
    {
        const float MaxSeconds = 0.35f;
        // Movement allowed for a tap, as a fraction of the screen's smaller side.
        const float MaxMovement = 0.025f;

        public static bool IsTap(Vector2 down, Vector2 up, float downTime)
        {
            float allowed = Mathf.Min(Screen.width, Screen.height) * MaxMovement;
            return Time.unscaledTime - downTime <= MaxSeconds && (up - down).magnitude <= allowed;
        }
    }
}
