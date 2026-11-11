#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace GlassRoom
{
    /// <summary>Talks to the website page the game runs in (does nothing outside a browser).</summary>
    public static class WebPage
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void WebPage_Ready();
#else
        static void WebPage_Ready() { }
#endif
        static bool s_Told;

        /// <summary>Tells the page the game is running, so it removes the loading cover. Safe to call often.</summary>
        public static void Ready()
        {
            if (s_Told) return;
            s_Told = true;
            WebPage_Ready();
        }
    }
}
