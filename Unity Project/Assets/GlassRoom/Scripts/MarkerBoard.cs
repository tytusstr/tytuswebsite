using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GlassRoom
{
    /// <summary>
    /// A whiteboard on the wall by the cozy corner. Standing right in front of it you can write
    /// on it (text in a box you place and size), draw on it, or wipe it clean. What's put on it
    /// appears gradually, as the person writes or draws it (with the marker's squeak, their hand
    /// moving across the board holding a marker), and wiping it goes across with an eraser.
    /// Everyone in the room sees the same board; someone joining later sees what's already
    /// there. It's blank again once everyone has left.
    ///
    /// The board's face is drawn by its own camera, out of sight far below the hall, filming a
    /// 1000 × 500 canvas of text and marker lines onto the board.
    /// </summary>
    public class MarkerBoard : MonoBehaviour
    {
        public static MarkerBoard Instance { get; private set; }

        /// <summary>The board's size in its own units (left to right, bottom to top).</summary>
        public const float UnitsWide = 1000f, UnitsHigh = 500f;
        public const int TextMinSize = 14, TextMaxSize = 80;
        public static readonly Color[] Colors =
        {
            new Color(0.08f, 0.08f, 0.1f), new Color(0.1f, 0.3f, 0.85f), new Color(0.82f, 0.12f, 0.12f),
            new Color(0.1f, 0.52f, 0.24f), new Color(0.93f, 0.48f, 0.05f), new Color(0.5f, 0.2f, 0.72f),
        };
        public static readonly float[] Sizes = { 5f, 10f, 18f };
        static readonly Color k_Surface = new Color(0.965f, 0.965f, 0.955f);

        [Tooltip("Size of the writing surface, in metres.")]
        public float width = 2.2f, height = 1.1f;
        public Renderer surface;
        public AudioClip markerClip, eraseClip;

        public enum Tool { None, Marker, Eraser }

        class Item
        {
            public int author;
            public bool isText;
            public Text text;
            public string full;
            public int color, size;
            public StrokeGraphic stroke;
            public int strokeId;
            public string key;
            public bool complete;
            public readonly List<Vector2> letters = new List<Vector2>();
            // The text with its line breaks fixed where the finished text wraps, so words don't
            // jump between lines while it's being written; and where each letter falls in it.
            public string laidOut;
            public readonly List<int> laidIndex = new List<int>();
        }

        class Job
        {
            public Item item;   // null: wiping the board
            public int author;
            public float duration, elapsed;
        }

        readonly List<Item> m_Items = new List<Item>();
        // Entries on the board by their key, and those that came from the saved board.
        readonly HashSet<string> m_Keys = new HashSet<string>(), m_Loaded = new HashSet<string>();
        readonly List<Job> m_Jobs = new List<Job>();
        RenderTexture m_Texture;
        Camera m_Camera;
        RectTransform m_Content;
        Image m_Wipe;
        Font m_Font;
        AudioSource m_Sound;
        int m_Layer;
        bool m_Dirty = true;

        // ---- What's happening right now (for the writer's hand, and keeping them still) ----

        /// <summary>Who is writing, drawing or wiping right now (-2: nobody).</summary>
        public int ActiveAuthor => m_Jobs.Count > 0 && m_Jobs[0].elapsed >= 0f ? m_Jobs[0].author : NoAuthor;
        public const int NoAuthor = -2;
        /// <summary>Where the marker or eraser is on the board right now, in the world.</summary>
        public Vector3 PenPoint { get; private set; }
        public Tool ActiveTool => m_Jobs.Count == 0 ? Tool.None : m_Jobs[0].item == null ? Tool.Eraser : Tool.Marker;
        public Color PenColor => m_Jobs.Count > 0 && m_Jobs[0].item != null ? Colors[m_Jobs[0].item.color] : Color.black;

        /// <summary>Your id as an author (-1 when playing on your own).</summary>
        public static int LocalAuthor => Multiplayer.Local != null ? Multiplayer.Local.PlayerId : -1;

        /// <summary>True while something you put on the board is still appearing (you stay put meanwhile).</summary>
        public static bool LocalBusy
        {
            get
            {
                if (Instance == null) return false;
                int me = LocalAuthor;
                foreach (var job in Instance.m_Jobs) if (job.author == me) return true;
                return false;
            }
        }

        /// <summary>True if there's anything on the board.</summary>
        public bool HasContent => m_Items.Count > 0;

        /// <summary>How many writings and drawings the board holds before it has to be wiped.</summary>
        public const int MaxEntries = 30;

        /// <summary>Writings and drawings on the board (a drawing counts once, however many lines).</summary>
        public int Entries
        {
            get
            {
                int n = 0;
                var drawings = new HashSet<string>();
                foreach (var item in m_Items)
                {
                    if (item.isText) n++;
                    else drawings.Add(item.key ?? (item.author + ":" + item.strokeId));
                }
                return n + drawings.Count;
            }
        }

        public bool Full => Entries >= MaxEntries;
        public RenderTexture Picture => m_Texture;
        public Font Font => m_Font;
        /// <summary>Which way the board faces (into the room).</summary>
        public Vector3 Facing => -transform.forward;

        void Awake()
        {
            Instance = this;
            m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (m_Texture != null) m_Texture.Release();
        }

        void Start()
        {
            m_Layer = LayerMask.NameToLayer("Board");
            if (m_Layer < 0) m_Layer = 30;
            // With smaller copies for seeing it from far away, so it doesn't shimmer.
            m_Texture = new RenderTexture(1024, 512, 16, RenderTextureFormat.ARGB32)
            {
                name = "Marker Board", antiAliasing = 2, useMipMap = true, autoGenerateMips = true,
                filterMode = FilterMode.Trilinear, anisoLevel = 4,
            };
            if (surface != null)
            {
                var material = surface.material;
                material.SetTexture("_BaseMap", m_Texture);
                material.mainTexture = m_Texture;
                // A plain white board with just a faint sheen (no glow).
                material.DisableKeyword("_EMISSION");
                material.SetFloat("_Smoothness", 0.38f);
                material.SetFloat("_Metallic", 0f);
            }
            BuildStage();
            StartCoroutine(Load());
            m_Sound = gameObject.AddComponent<AudioSource>();
            m_Sound.playOnAwake = false;
            m_Sound.loop = true;
            m_Sound.spatialBlend = 1f;
            m_Sound.minDistance = 1.5f;
            m_Sound.maxDistance = 25f;
            m_Sound.rolloffMode = AudioRolloffMode.Logarithmic;
            m_Sound.volume = 0.8f;
        }

        // The canvas the board's camera films, far out of sight.
        void BuildStage()
        {
            var stage = new GameObject("Marker Board Stage");
            stage.transform.position = new Vector3(0f, -700f, 0f);
            stage.layer = m_Layer;
            var canvasGo = new GameObject("Board Canvas", typeof(RectTransform));
            canvasGo.layer = m_Layer;
            canvasGo.transform.SetParent(stage.transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)canvasGo.transform;
            rect.sizeDelta = new Vector2(UnitsWide, UnitsHigh);
            rect.localScale = Vector3.one * 0.001f;
            m_Content = NewRect("Content", rect);
            m_Content.anchorMin = Vector2.zero;
            m_Content.anchorMax = Vector2.one;
            m_Content.offsetMin = m_Content.offsetMax = Vector2.zero;
            var wipe = NewRect("Wipe", rect);
            wipe.anchorMin = wipe.anchorMax = Vector2.zero;
            wipe.pivot = Vector2.zero;
            wipe.sizeDelta = new Vector2(0f, UnitsHigh);
            m_Wipe = wipe.gameObject.AddComponent<Image>();
            m_Wipe.color = k_Surface;

            m_Camera = new GameObject("Board Camera").AddComponent<Camera>();
            m_Camera.transform.SetParent(stage.transform, false);
            m_Camera.transform.localPosition = new Vector3(0f, 0f, -1f);
            m_Camera.orthographic = true;
            m_Camera.orthographicSize = 0.25f;
            m_Camera.nearClipPlane = 0.1f;
            m_Camera.farClipPlane = 3f;
            m_Camera.clearFlags = CameraClearFlags.SolidColor;
            m_Camera.backgroundColor = k_Surface;
            m_Camera.cullingMask = 1 << m_Layer;
            m_Camera.targetTexture = m_Texture;
            m_Camera.enabled = true;
        }

        RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = m_Layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        // ---- Where things are ----

        /// <summary>A point on the board (its own units) in the world, on the surface.</summary>
        public Vector3 BoardToWorld(Vector2 u) =>
            transform.position + transform.right * ((u.x / UnitsWide - 0.5f) * width)
                               + transform.up * ((u.y / UnitsHigh - 0.5f) * height)
                               - transform.forward * 0.006f;

        /// <summary>True if someone standing here is right in front of the board.</summary>
        public bool InFront(Vector3 feet)
        {
            Vector3 local = transform.InverseTransformPoint(feet);
            float outFront = -local.z;
            return outFront > -0.05f && outFront < 1.9f && Mathf.Abs(local.x) < width * 0.5f + 0.3f;
        }

        // ---- Text: size that fits its box, the same everywhere ----

        /// <summary>Sets up a Text the way the board writes (top-left, wrapping, best fit).</summary>
        public static void StyleText(Text text, Font font)
        {
            text.font = font;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = TextMinSize;
            text.resizeTextMaxSize = TextMaxSize;
            text.fontSize = TextMaxSize;
            text.supportRichText = false;
            text.raycastTarget = false;
        }

        /// <summary>The letter size this text gets in a box this size.</summary>
        public static int FitSize(Text styled, string s, Vector2 size)
        {
            var settings = styled.GetGenerationSettings(size);
            var gen = new TextGenerator();
            gen.Populate(s, settings);
            return Mathf.Max(TextMinSize, gen.fontSizeUsedForBestFit);
        }

        // ---- Adding things (from the network, or straight away when playing alone) ----

        /// <summary>Text in a box (board units), in one of the colours.</summary>
        public void AddText(int author, string s, Rect box, int color, bool animate, string key = null)
        {
            if (string.IsNullOrWhiteSpace(s)) return;
            if (key != null && !m_Keys.Add(key)) return; // already on the board
            color = Mathf.Clamp(color, 0, Colors.Length - 1);
            var rect = NewRect("Text", m_Content);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = box.position;
            rect.sizeDelta = box.size;
            var text = rect.gameObject.AddComponent<Text>();
            StyleText(text, m_Font);
            text.color = Colors[color];
            int fontSize = FitSize(text, s, box.size);
            text.resizeTextForBestFit = false;
            text.fontSize = fontSize;
            var item = new Item { author = author, isText = true, text = text, full = s, color = color, complete = true, key = key };
            // Where each letter sits (for the writer's hand), and the finished text's line breaks.
            var gen = new TextGenerator();
            gen.Populate(s, text.GetGenerationSettings(box.size));
            foreach (var c in gen.characters)
                item.letters.Add(box.position + new Vector2(c.cursorPos.x + c.charWidth * 0.5f, c.cursorPos.y - fontSize * 0.5f));
            var breaks = new HashSet<int>();
            for (int i = 1; i < gen.lineCount; i++) breaks.Add(gen.lines[i].startCharIdx);
            var laid = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (breaks.Contains(i) && s[i] != '\n') laid.Append('\n');
                laid.Append(s[i]);
                item.laidIndex.Add(laid.Length);
            }
            item.laidOut = laid.ToString();
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.text = animate ? "" : item.laidOut;
            m_Items.Add(item);
            if (animate) m_Jobs.Add(new Job { item = item, author = author, duration = Mathf.Clamp(s.Length * 0.06f, 0.8f, 4f), elapsed = -1f });
            m_Dirty = true;
        }

        /// <summary>Part of a drawn line (lines arrive in pieces); it's drawn once the last piece is in.</summary>
        public void AddStroke(int author, int strokeId, int color, int size, string packed, bool last, bool animate, string key = null)
        {
            Item item = null;
            foreach (var existing in m_Items)
                if (!existing.isText && existing.author == author && existing.strokeId == strokeId && !existing.complete) { item = existing; break; }
            if (item == null)
            {
                // A drawing already on the board (loaded from the saved board) isn't added twice.
                if (key != null && m_Loaded.Contains(key)) return;
                if (key != null) m_Keys.Add(key);
                var rect = NewRect("Line", m_Content);
                rect.anchorMin = rect.anchorMax = Vector2.zero;
                rect.pivot = Vector2.zero;
                rect.sizeDelta = new Vector2(UnitsWide, UnitsHigh);
                var stroke = rect.gameObject.AddComponent<StrokeGraphic>();
                stroke.raycastTarget = false;
                item = new Item
                {
                    author = author, isText = false, stroke = stroke, strokeId = strokeId, key = key,
                    color = Mathf.Clamp(color, 0, Colors.Length - 1), size = Mathf.Clamp(size, 0, Sizes.Length - 1),
                };
                stroke.color = Colors[item.color];
                stroke.width = Sizes[item.size];
                stroke.SetVisible(0);
                m_Items.Add(item);
            }
            StrokeGraphic.Unpack(packed, item.stroke.points);
            if (!last) return;
            item.complete = true;
            if (animate)
            {
                item.stroke.SetVisible(0);
                m_Jobs.Add(new Job { item = item, author = author, duration = Mathf.Clamp(item.stroke.Length / 600f, 0.3f, 4f), elapsed = -1f });
            }
            else item.stroke.SetVisible(int.MaxValue);
            item.stroke.Refresh();
            m_Dirty = true;
        }

        /// <summary>Wipes the board clean (gradually, with the eraser, when animated).</summary>
        public void Erase(int author, bool animate)
        {
            if (animate) m_Jobs.Add(new Job { item = null, author = author, duration = 2.6f, elapsed = -1f });
            else ClearAll();
        }

        void ClearAll()
        {
            foreach (var item in m_Items)
                Destroy(item.isText ? item.text.gameObject : item.stroke.gameObject);
            m_Items.Clear();
            m_Keys.Clear();
            m_Loaded.Clear();
            m_Wipe.rectTransform.sizeDelta = new Vector2(0f, UnitsHigh);
            m_Dirty = true;
        }

        /// <summary>Everything on the board, oldest first (for someone who joins later).</summary>
        public void Export(System.Action<string, Rect, int> text, System.Action<int, int, List<Vector2>> stroke)
        {
            foreach (var item in m_Items)
            {
                if (!item.complete) continue;
                if (item.isText)
                {
                    var r = item.text.rectTransform;
                    text(item.full, new Rect(r.anchoredPosition, r.sizeDelta), item.color);
                }
                else stroke(item.color, item.size, item.stroke.points);
            }
        }

        // ---- Things appearing ----

        void Update()
        {
            if (m_Jobs.Count == 0)
            {
                if (m_Sound != null && m_Sound.isPlaying) m_Sound.Stop();
            }
            else
            {
                var job = m_Jobs[0];
                if (job.elapsed < 0f) StartJob(job);
                job.elapsed += Time.deltaTime;
                float p = Mathf.Clamp01(job.elapsed / job.duration);
                Animate(job, p);
                if (p >= 1f)
                {
                    FinishJob(job);
                    m_Jobs.RemoveAt(0);
                    if (m_Jobs.Count == 0 && m_Sound != null) m_Sound.Stop();
                }
                m_Dirty = true;
                if (job.author == LocalAuthor) KeepWriterInFront();
            }
            // The board's camera only films when something changed.
            if (m_Camera != null)
            {
                m_Camera.enabled = m_Dirty;
                m_Dirty = false;
            }
        }

        void StartJob(Job job)
        {
            job.elapsed = 0f;
            var clip = job.item == null ? eraseClip : markerClip;
            if (m_Sound != null && clip != null && (m_Sound.clip != clip || !m_Sound.isPlaying))
            {
                m_Sound.clip = clip;
                m_Sound.time = Random.Range(0f, Mathf.Max(0f, clip.length - 1f));
                m_Sound.pitch = Random.Range(0.95f, 1.05f);
                m_Sound.Play();
            }
        }

        void Animate(Job job, float p)
        {
            Vector2 pen;
            if (job.item == null)
            {
                // Wiping left to right in quick up-and-down strokes; the board goes white behind.
                pen = new Vector2(p * UnitsWide, UnitsHigh * 0.5f + Mathf.Sin(p * Mathf.PI * 16f) * UnitsHigh * 0.38f);
                m_Wipe.rectTransform.sizeDelta = new Vector2(Mathf.Clamp(p * UnitsWide + 30f, 0f, UnitsWide), UnitsHigh);
            }
            else if (job.item.isText)
            {
                var item = job.item;
                int shown = Mathf.Clamp(Mathf.CeilToInt(p * item.full.Length), 0, item.full.Length);
                string part = shown > 0 ? item.laidOut.Substring(0, item.laidIndex[shown - 1]) : "";
                if (item.text.text != part) item.text.text = part;
                int at = Mathf.Clamp(shown - 1, 0, Mathf.Max(0, item.letters.Count - 1));
                pen = item.letters.Count > 0 ? item.letters[at] : item.text.rectTransform.anchoredPosition;
            }
            else
            {
                var stroke = job.item.stroke;
                pen = stroke.PointAt(p * stroke.Length, out int index, out float partial);
                stroke.SetVisible(index, partial);
            }
            PenPoint = BoardToWorld(pen);
        }

        void FinishJob(Job job)
        {
            if (job.item == null) { ClearAll(); return; }
            if (job.item.isText) job.item.text.text = job.item.laidOut;
            else job.item.stroke.SetVisible(int.MaxValue);
        }

        // Your own writing: you step along the board to keep up with your hand.
        void KeepWriterInFront()
        {
            var me = FindAnyObjectByType<FirstPersonController>();
            if (me == null) return;
            Vector3 local = transform.InverseTransformPoint(PenPoint);
            Vector3 floor = transform.TransformPoint(new Vector3(Mathf.Clamp(local.x, -width * 0.5f + 0.25f, width * 0.5f - 0.25f), 0f, 0f));
            Vector3 target = new Vector3(floor.x, me.transform.position.y, floor.z) + Facing * 0.55f;
            Vector3 feet = Vector3.Lerp(me.transform.position, target, 1f - Mathf.Exp(-4f * Time.deltaTime));
            me.HoldAt(feet, Quaternion.LookRotation(-Facing).eulerAngles.y);
        }

        // ---- Kept online (Firebase), so the board stays after everyone has left ----

        const string Database = "https://tytuswebsite-board-default-rtdb.firebaseio.com";

        // Keys sort in the order things were put up.
        static string NewKey()
        {
            const string letters = "abcdefghijklmnopqrstuvwxyz0123456789";
            var sb = new System.Text.StringBuilder("e");
            sb.Append(System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("D13"));
            for (int i = 0; i < 5; i++) sb.Append(letters[Random.Range(0, letters.Length)]);
            return sb.ToString();
        }

        static string N(float v) => Mathf.RoundToInt(v).ToString();

        static string Quote(string s)
        {
            var sb = new System.Text.StringBuilder("\"");
            foreach (char ch in s)
            {
                if (ch == '"' || ch == '\\') sb.Append('\\').Append(ch);
                else if (ch < ' ') sb.Append("\\u").Append(((int)ch).ToString("x4"));
                else sb.Append(ch);
            }
            return sb.Append('"').ToString();
        }

        System.Collections.IEnumerator Save(string key, string json) => Send("PUT", Database + "/board/entries/" + key + ".json", json);

        static System.Collections.IEnumerator Send(string method, string url, string json)
        {
            var request = new UnityEngine.Networking.UnityWebRequest(url, method);
            if (json != null)
            {
                request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
            yield return request.SendWebRequest();
            if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                Debug.LogWarning("Board: couldn't save (" + request.error + ")");
            request.Dispose();
        }

        // What's on the saved board, shown straight away (no writing animation).
        System.Collections.IEnumerator Load()
        {
            var request = UnityEngine.Networking.UnityWebRequest.Get(Database + "/board/entries.json");
            yield return request.SendWebRequest();
            string body = request.result == UnityEngine.Networking.UnityWebRequest.Result.Success ? request.downloadHandler.text : null;
            request.Dispose();
            if (string.IsNullOrEmpty(body) || body == "null") yield break;
            var entries = MiniJson.Parse(body) as Dictionary<string, object>;
            if (entries == null) yield break;
            var keys = new List<string>(entries.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            int id = -1000000;
            foreach (var key in keys)
            {
                if (!(entries[key] is Dictionary<string, object> e) || m_Keys.Contains(key)) continue;
                string kind = e.TryGetValue("k", out var k) ? k as string : null;
                int color = e.TryGetValue("c", out var c) ? System.Convert.ToInt32(c) : 0;
                if (kind == "t" && e.TryGetValue("t", out var t))
                {
                    var box = new Rect(Num(e, "x"), Num(e, "y"), Num(e, "w"), Num(e, "h"));
                    AddText(-3, t as string, box, color, false, key);
                }
                else if (kind == "d" && e.TryGetValue("d", out var d) && d is string lines)
                {
                    int size = e.TryGetValue("s", out var z) ? System.Convert.ToInt32(z) : 1;
                    m_Loaded.Add(key);
                    foreach (var line in lines.Split('|'))
                    {
                        // Added as a finished line under this drawing's key.
                        m_Loaded.Remove(key);
                        AddStroke(-3, ++id, color, size, line, true, false, key);
                        m_Loaded.Add(key);
                    }
                }
            }
        }

        static float Num(Dictionary<string, object> e, string name) => e.TryGetValue(name, out var v) ? System.Convert.ToSingle(v) : 0f;

        // ---- Your requests (sent to everyone; played here straight away when you're alone) ----

        public static event System.Action<string, Rect, int, string> TextRequested;
        public static event System.Action<int, int, int, string, bool, string> StrokeRequested;
        public static event System.Action EraseRequested;

        int m_NextBatch;

        public void Write(string s, Rect box, int color)
        {
            s = s.Trim();
            if (s.Length == 0 || Full) return;
            if (s.Length > 120) s = s.Substring(0, 120);
            string key = NewKey();
            if (TextRequested != null) TextRequested(s, box, color, key);
            else AddText(LocalAuthor, s, box, color, true, key);
            StartCoroutine(Save(key, "{\"k\":\"t\",\"x\":" + N(box.x) + ",\"y\":" + N(box.y) + ",\"w\":" + N(box.width) + ",\"h\":" + N(box.height)
                                     + ",\"c\":" + color + ",\"t\":" + Quote(s) + "}"));
        }

        public void Draw(List<List<Vector2>> lines, int color, int size)
        {
            if (Full) return;
            int budget = 2000;
            // One drawing: one key for all its lines (one entry), each line its own id.
            string key = NewKey();
            int batch = ++m_NextBatch;
            int lineNumber = 0;
            var saved = new List<string>();
            foreach (var line in lines)
            {
                if (line.Count == 0 || budget <= 0) continue;
                int count = Mathf.Min(line.Count, budget);
                budget -= count;
                int id = (batch << 12) | (++lineNumber & 4095);
                // A few dozen points per message, so each stays small.
                for (int from = 0; from < count; from += 40)
                {
                    int n = Mathf.Min(40, count - from);
                    string packed = StrokeGraphic.Pack(line, from, n);
                    bool last = from + n >= count;
                    if (StrokeRequested != null) StrokeRequested(id, color, size, packed, last, key);
                    else AddStroke(LocalAuthor, id, color, size, packed, last, true, key);
                }
                saved.Add(StrokeGraphic.Pack(line, 0, count));
            }
            if (saved.Count > 0)
                StartCoroutine(Save(key, "{\"k\":\"d\",\"c\":" + color + ",\"s\":" + size + ",\"d\":" + Quote(string.Join("|", saved)) + "}"));
        }

        public void EraseAll()
        {
            if (EraseRequested != null) EraseRequested();
            else Erase(LocalAuthor, true);
            StartCoroutine(Send("DELETE", Database + "/board/entries.json", null));
        }
    }
}
