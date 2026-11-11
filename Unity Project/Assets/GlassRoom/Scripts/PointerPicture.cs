using UnityEngine;

namespace GlassRoom
{
    /// <summary>
    /// The mouse pointer picture in computer mode: a tiny cut-out photo of Rick Astley
    /// (Resources/RickAstleyCursor*.png; see Sources.txt there for the photo's credit).
    /// The real mouse pointer shows it whenever it's visible, and the game's own pointer in
    /// the Q menu (FakeCursor) uses the larger copy. Phone mode goes back to the normal pointer.
    /// </summary>
    public static class PointerPicture
    {
        static Texture2D s_Large, s_Small;
        static bool s_Loaded;

        public static Texture2D Large { get { Load(); return s_Large; } }

        static void Load()
        {
            if (s_Loaded) return;
            s_Loaded = true;
            s_Large = Resources.Load<Texture2D>("RickAstleyCursor");
            s_Small = Resources.Load<Texture2D>("RickAstleyCursorSmall");
        }

        /// <summary>Uses the picture for the real pointer in computer mode, the normal one otherwise.</summary>
        public static void Apply(bool computer)
        {
            Load();
            if (computer && s_Small != null)
                Cursor.SetCursor(s_Small, new Vector2(s_Small.width / 2f, s_Small.height / 2f), CursorMode.Auto);
            else
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }
    }
}
