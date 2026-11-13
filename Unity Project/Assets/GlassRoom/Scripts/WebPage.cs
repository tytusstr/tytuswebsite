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
        [DllImport("__Internal")] static extern int WebPage_Covered();
        [DllImport("__Internal")] static extern void WebPage_SetPointer(int computer, int menuOpen);
        [DllImport("__Internal")] static extern int WebPage_PointerLocked();
        [DllImport("__Internal")] static extern int WebPage_Entered();
        [DllImport("__Internal")] static extern void WebPage_Reveal();
        [DllImport("__Internal")] static extern string WebPage_PlayerName();
        /// <summary>True when running in a web page (where the page captures the pointer).</summary>
        public const bool InBrowser = true;
#else
        static void WebPage_Ready() { }
        static int WebPage_Covered() => 0;
        static void WebPage_SetPointer(int computer, int menuOpen) { }
        static int WebPage_PointerLocked() => 0;
        static int WebPage_Entered() => 1;
        static void WebPage_Reveal() { }
        static string WebPage_PlayerName() => "";
        public const bool InBrowser = false;
#endif

        /// <summary>Tells the page whether to capture the pointer on the next click.</summary>
        public static void SetPointer(bool computer, bool menuOpen) => WebPage_SetPointer(computer ? 1 : 0, menuOpen ? 1 : 0);

        /// <summary>True while the browser really has the pointer captured.</summary>
        public static bool PointerLocked => WebPage_PointerLocked() != 0;

        /// <summary>True once Enter has been pressed (the game joins the room, then reveals itself).</summary>
        public static bool Entered => WebPage_Entered() != 0;

        static bool s_Revealed;

        /// <summary>The room is joined: the Enter screen fades and the game starts. Safe to call often.</summary>
        public static void Reveal()
        {
            if (s_Revealed) return;
            s_Revealed = true;
            WebPage_Reveal();
        }

        /// <summary>The name typed on the Enter screen ("" if none).</summary>
        public static string PlayerName
        {
            get
            {
                string name = WebPage_PlayerName() ?? "";
                name = name.Trim();
                return name.Length > 16 ? name.Substring(0, 16) : name;
            }
        }

        /// <summary>True while the page's loading / Enter screen still covers the game.</summary>
        public static bool Covered => WebPage_Covered() != 0;
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
