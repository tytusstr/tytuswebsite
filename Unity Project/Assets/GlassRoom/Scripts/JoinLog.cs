using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace GlassRoom
{
    /// <summary>
    /// Keeps a list online of who has come in: each name once (coming back just updates the
    /// time), the latest 200. Kept in the same online database as the board, under "joins";
    /// see joins.html on the website.
    /// </summary>
    public static class JoinLog
    {
        const string Url = "https://tytuswebsite-board-default-rtdb.firebaseio.com/joins";
        const int Keep = 200;

        /// <summary>You've come in under this name (empty: no name).</summary>
        public static IEnumerator Record(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length > 20) name = name.Substring(0, 20);
            long ms = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string shown = name.Length > 0 ? name : "(no name)";
            string json = "{\"n\":" + Quote(shown) + ",\"ms\":" + ms + ",\"t\":\"" + System.DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + " UTC\"}";
            yield return Send("PUT", Url + "/" + Key(shown) + ".json", json);

            // Only the latest 200: once there are a few too many, the oldest go.
            var request = UnityWebRequest.Get(Url + ".json");
            yield return request.SendWebRequest();
            string body = request.result == UnityWebRequest.Result.Success ? request.downloadHandler.text : null;
            request.Dispose();
            if (!(MiniJson.Parse(body ?? "") is Dictionary<string, object> all) || all.Count <= Keep + 5) yield break;
            var byTime = new List<(string key, double ms)>();
            foreach (var kv in all)
                if (kv.Value is Dictionary<string, object> e && e.TryGetValue("ms", out var t)) byTime.Add((kv.Key, System.Convert.ToDouble(t)));
            byTime.Sort((a, b) => a.ms.CompareTo(b.ms));
            for (int i = 0; i < byTime.Count - Keep; i++)
                yield return Send("DELETE", Url + "/" + byTime[i].key + ".json", null);
        }

        // One entry per name, whatever the capitals: letters and digits kept, the rest as codes
        // (the database doesn't allow some characters in keys).
        static string Key(string name)
        {
            var sb = new System.Text.StringBuilder("n_");
            foreach (char c in name.ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) sb.Append(c);
                else sb.Append('_').Append(((int)c).ToString("x"));
            }
            return sb.ToString();
        }

        static string Quote(string s)
        {
            var sb = new System.Text.StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        static IEnumerator Send(string method, string url, string json)
        {
            var request = new UnityWebRequest(url, method);
            if (json != null)
            {
                request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            request.downloadHandler = new DownloadHandlerBuffer();
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) Debug.LogWarning("Join log: " + request.error);
            request.Dispose();
        }
    }
}
