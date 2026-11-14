using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GlassRoom
{
    /// <summary>
    /// A marker line on the board: a run of points (in the board's own units) drawn as a smooth
    /// line with round ends, in one colour and thickness. Only the first <see cref="Visible"/>
    /// points are drawn, so a line can appear gradually, as if being drawn.
    /// </summary>
    public class StrokeGraphic : MaskableGraphic
    {
        public readonly List<Vector2> points = new List<Vector2>();
        public float width = 8f;
        int m_Visible = int.MaxValue;
        float m_Partial = 1f;

        /// <summary>How much of the line shows: whole points, plus a fraction of the next segment.</summary>
        public void SetVisible(int count, float partial = 1f)
        {
            if (count == m_Visible && Mathf.Approximately(partial, m_Partial)) return;
            m_Visible = count;
            m_Partial = partial;
            SetVerticesDirty();
        }

        public void Refresh() => SetVerticesDirty();

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            int count = Mathf.Min(points.Count, m_Visible);
            if (count <= 0) return;
            float r = width * 0.5f;
            Vector2 last = points[0];
            Dot(vh, last, r);
            for (int i = 1; i < count; i++)
            {
                Vector2 next = points[i];
                if (i == count - 1 && m_Partial < 1f) next = Vector2.Lerp(points[i - 1], points[i], m_Partial);
                Segment(vh, last, next, r);
                Dot(vh, next, r);
                last = next;
            }
        }

        void Segment(VertexHelper vh, Vector2 a, Vector2 b, float r)
        {
            Vector2 d = b - a;
            if (d.sqrMagnitude < 0.0001f) return;
            Vector2 n = new Vector2(-d.y, d.x).normalized * r;
            int start = vh.currentVertCount;
            vh.AddVert(a - n, color, Vector2.zero);
            vh.AddVert(a + n, color, Vector2.zero);
            vh.AddVert(b + n, color, Vector2.zero);
            vh.AddVert(b - n, color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }

        // A round joint, so corners and ends look like a felt-tip line.
        void Dot(VertexHelper vh, Vector2 c, float r)
        {
            const int sides = 10;
            int centre = vh.currentVertCount;
            vh.AddVert(c, color, Vector2.zero);
            for (int i = 0; i <= sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                vh.AddVert(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, color, Vector2.zero);
                if (i > 0) vh.AddTriangle(centre, centre + i, centre + i + 1);
            }
        }

        /// <summary>Total length of the line, in board units.</summary>
        public float Length
        {
            get
            {
                float total = 0f;
                for (int i = 1; i < points.Count; i++) total += Vector2.Distance(points[i - 1], points[i]);
                return total;
            }
        }

        /// <summary>The point a given distance along the line, and how many points are passed by then.</summary>
        public Vector2 PointAt(float distance, out int index, out float partial)
        {
            index = 1;
            partial = 0f;
            if (points.Count == 0) return Vector2.zero;
            if (points.Count == 1) { index = 1; partial = 1f; return points[0]; }
            float walked = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                float seg = Vector2.Distance(points[i - 1], points[i]);
                if (walked + seg >= distance)
                {
                    index = i + 1;
                    partial = seg > 0f ? (distance - walked) / seg : 1f;
                    return Vector2.Lerp(points[i - 1], points[i], partial);
                }
                walked += seg;
            }
            index = points.Count;
            partial = 1f;
            return points[points.Count - 1];
        }

        // ---- Sending lines over the network: points packed into a short piece of text ----

        public static string Pack(List<Vector2> pts, int from, int count)
        {
            var bytes = new byte[count * 4];
            for (int i = 0; i < count; i++)
            {
                var p = pts[from + i];
                ushort x = (ushort)Mathf.Clamp(Mathf.RoundToInt(p.x * 10f), 0, 10000);
                ushort y = (ushort)Mathf.Clamp(Mathf.RoundToInt(p.y * 10f), 0, 5000);
                bytes[i * 4] = (byte)(x & 255); bytes[i * 4 + 1] = (byte)(x >> 8);
                bytes[i * 4 + 2] = (byte)(y & 255); bytes[i * 4 + 3] = (byte)(y >> 8);
            }
            return System.Convert.ToBase64String(bytes);
        }

        public static void Unpack(string packed, List<Vector2> into)
        {
            byte[] bytes;
            try { bytes = System.Convert.FromBase64String(packed); } catch { return; }
            for (int i = 0; i + 3 < bytes.Length; i += 4)
            {
                int x = bytes[i] | (bytes[i + 1] << 8), y = bytes[i + 2] | (bytes[i + 3] << 8);
                into.Add(new Vector2(x / 10f, y / 10f));
            }
        }
    }
}
