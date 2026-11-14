using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GlassRoom
{
    /// <summary>
    /// Underlines parts of a chat line (the names), following the text's wrapping: a thin bar
    /// under each part, line by line, placed once the text has been laid out.
    /// </summary>
    public class ChatUnderlines : MonoBehaviour
    {
        Text m_Text;
        readonly List<(int start, int length)> m_Parts = new List<(int, int)>();
        readonly List<RectTransform> m_Bars = new List<RectTransform>();
        string m_LaidOut;
        float m_Width;

        public void Set(Text text, List<(int start, int length)> parts)
        {
            m_Text = text;
            m_Parts.Clear();
            m_Parts.AddRange(parts);
            m_LaidOut = null;
        }

        void LateUpdate()
        {
            if (m_Text == null) return;
            var rect = m_Text.rectTransform.rect;
            if (m_LaidOut == m_Text.text && Mathf.Approximately(m_Width, rect.width)) return;
            var gen = m_Text.cachedTextGenerator;
            if (gen.characterCount == 0 || rect.width <= 0f) return;
            m_LaidOut = m_Text.text;
            m_Width = rect.width;
            var chars = gen.characters;
            var lines = gen.lines;
            float unit = m_Text.pixelsPerUnit > 0f ? 1f / m_Text.pixelsPerUnit : 1f;
            int used = 0;
            foreach (var (start, length) in m_Parts)
            {
                int i = start, end = Mathf.Min(start + length, chars.Count);
                while (i < end)
                {
                    int line = LineOf(lines, i);
                    int lineEnd = line + 1 < lines.Count ? lines[line + 1].startCharIdx : chars.Count;
                    int last = Mathf.Min(end, lineEnd) - 1;
                    float x0 = chars[i].cursorPos.x * unit;
                    float x1 = (chars[last].cursorPos.x + chars[last].charWidth) * unit;
                    float y = (lines[line].topY - lines[line].height) * unit + 3f;
                    if (x1 > x0)
                    {
                        var bar = Bar(used++);
                        bar.anchoredPosition = new Vector2(x0, y);
                        bar.sizeDelta = new Vector2(x1 - x0, 1.6f);
                        bar.GetComponent<Image>().color = m_Text.color;
                    }
                    i = Mathf.Min(end, lineEnd);
                }
            }
            for (int b = 0; b < m_Bars.Count; b++) m_Bars[b].gameObject.SetActive(b < used);
        }

        static int LineOf(IList<UILineInfo> lines, int index)
        {
            int line = 0;
            for (int l = 0; l < lines.Count; l++) if (lines[l].startCharIdx <= index) line = l;
            return line;
        }

        RectTransform Bar(int index)
        {
            while (m_Bars.Count <= index)
            {
                var go = new GameObject("Underline", typeof(RectTransform));
                var r = (RectTransform)go.transform;
                r.SetParent(transform, false);
                // Positions from the generator are relative to the text's pivot.
                r.anchorMin = r.anchorMax = m_Text.rectTransform.pivot;
                r.pivot = new Vector2(0f, 0.5f);
                var img = go.AddComponent<Image>();
                img.raycastTarget = false;
                m_Bars.Add(r);
            }
            m_Bars[index].gameObject.SetActive(true);
            return m_Bars[index];
        }
    }
}
